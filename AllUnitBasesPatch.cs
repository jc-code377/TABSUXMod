using System;
using System.Collections.Generic;
using System.Reflection;
using DM;
using HarmonyLib;
using Landfall.TABS;
using Landfall.TABS.UnitEditor;
using UnityEngine;

namespace TABSUXMod
{
    // Adds every unit base the game knows about to the Unit Creator's base selector:
    // rigs registered in the content database plus rigs used by any unit blueprint
    // (vanilla, modded, or user-made).
    //
    // Two injection points, both idempotent (deduped by rig GameObject):
    // 1. Prefix on UnitEditorManager.Start — after other mods' sceneLoaded hooks
    //    (e.g. BeeCreative) have injected their own bases.
    // 2. Prefix on UnitEditorUnitBaseGrid.SpawnUnitBaseButtons — the actual grid
    //    builder, as a safety net in case some mod bypasses the vanilla Start path.
    //
    // Save/load round-trip: saving stores the base as the rig's root Unit entity GUID
    // (UnitBlueprint.SerializedUnit), and loading resolves it lazily through
    // ContentDatabase.GetGameObject → AssetLoader. Rigs that only exist inside other
    // blueprints (most modded ones) are not indexed there, so without help a unit saved
    // on such a base deserializes with UnitBase == null — "corrupted". Two countermeasures:
    // - Inject registers every selector rig into AssetLoader.m_nonStreamableAssets.
    // - A postfix on ContentDatabase.GetGameObject resolves missed GUIDs from a cached
    //   map of every blueprint's rig, so saved units also load in sessions where the
    //   Unit Creator was never opened (custom-content browser, battles).
    public static class AllUnitBasesPatch
    {
        private static FieldInfo s_nonStreamableAssetsField;
        private static Dictionary<DatabaseID, GameObject> s_rigsByGuid;
        private static int s_rigsBuiltFromCount = -1;
        private static bool s_resolving;
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UnitEditorManager), "Start")]
        public static void BeforeUnitEditorStart(UnitEditorManager __instance)
        {
            Debug.Log("[TABSUXMod] UnitEditorManager.Start prefix fired.");
            try
            {
                Inject(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError("[TABSUXMod] Failed to add unit bases to the Unit Creator: " + e);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UnitEditorUnitBaseGrid), "SpawnUnitBaseButtons")]
        public static void BeforeSpawnUnitBaseButtons(
            ref UnitEditorManager.UnitBaseWrapper[] unitBaseWrappers,
            UnitEditorManager unitEditorManager)
        {
            Debug.Log("[TABSUXMod] SpawnUnitBaseButtons prefix fired with " +
                (unitBaseWrappers != null ? unitBaseWrappers.Length : -1) + " wrappers.");
            try
            {
                if (unitEditorManager == null)
                {
                    return;
                }
                Inject(unitEditorManager);
                // Buttons call SwitchUnitBase(index) which indexes manager.UnitBases,
                // so the grid must be built from that exact array.
                unitBaseWrappers = unitEditorManager.UnitBases;
            }
            catch (Exception e)
            {
                Debug.LogError("[TABSUXMod] Failed to add unit bases to the Unit Creator: " + e);
            }
        }

        private static void Inject(UnitEditorManager manager)
        {
            var wrappers = new List<UnitEditorManager.UnitBaseWrapper>(manager.UnitBases);
            if (wrappers.Count == 0)
            {
                Debug.LogWarning("[TABSUXMod] UnitBases is empty; nothing to use as a template, skipping.");
                return;
            }

            // Known-good source of movement/targeting components and default stats.
            UnitBlueprint template = wrappers[0].UnitBaseBlueprint;
            if (template == null)
            {
                Debug.LogWarning("[TABSUXMod] UnitBases[0] has no blueprint to use as a template, skipping.");
                return;
            }

            var listed = new HashSet<GameObject>();
            foreach (var wrapper in wrappers)
            {
                if (wrapper != null && wrapper.UnitBaseBlueprint != null && wrapper.UnitBaseBlueprint.UnitBase != null)
                {
                    listed.Add(wrapper.UnitBaseBlueprint.UnitBase);
                }
            }

            var db = ContentDatabase.Instance();
            var candidates = new List<GameObject>();
            var iconFallback = new Dictionary<GameObject, UnitBlueprint>();

            foreach (GameObject rig in db.GetAllUnitBases())
            {
                if (rig != null)
                {
                    candidates.Add(rig);
                }
            }
            int registeredBases = candidates.Count;

            foreach (UnitBlueprint blueprint in db.GetAllUnitBlueprints())
            {
                if (blueprint == null)
                {
                    continue;
                }
                GameObject rig;
                try
                {
                    rig = blueprint.UnitBase; // database lookup; broken content can throw or return null
                }
                catch
                {
                    continue;
                }
                if (rig == null)
                {
                    continue;
                }
                candidates.Add(rig);
                if (!iconFallback.ContainsKey(rig))
                {
                    iconFallback[rig] = blueprint;
                }
            }

            int skippedUnusable = 0;
            var added = new List<UnitEditorManager.UnitBaseWrapper>();
            foreach (GameObject rig in candidates)
            {
                if (!listed.Add(rig))
                {
                    continue;
                }
                // Unit must be on the rig ROOT: saving does UnitBase.GetComponent<Unit>()
                // (UnitBlueprint.SerializedUnit) and would NRE on a child-only Unit.
                Unit unit = rig.GetComponent<Unit>();
                if (unit == null || unit.Entity == null)
                {
                    skippedUnusable++;
                    continue;
                }
                // RespawnUnit dereferences these on the spawned base; skip rigs that would throw.
                if (rig.GetComponentInChildren<Torso>(true) == null ||
                    rig.GetComponentInChildren<HealthHandler>(true) == null ||
                    rig.GetComponentInChildren<RigidbodyHolder>(true) == null)
                {
                    skippedUnusable++;
                    continue;
                }

                var blueprint = new UnitBlueprint(template);
                blueprint.UnitBase = rig;
                blueprint.Entity.Name = unit.Entity.Name;

                var wrapper = new UnitEditorManager.UnitBaseWrapper
                {
                    UnitBaseBlueprint = blueprint,
                    BaseDisplayName = unit.Entity.Name,
                    UnitBaseRestriction = CharacterItem.UnitBaseRestrictions.None
                };
                UnitBlueprint fallback;
                iconFallback.TryGetValue(rig, out fallback);
                LoadIcon(wrapper, unit.Entity, fallback);
                added.Add(wrapper);
            }

            added.Sort((a, b) => string.Compare(a.BaseDisplayName, b.BaseDisplayName, StringComparison.OrdinalIgnoreCase));
            wrappers.AddRange(added);
            manager.UnitBases = wrappers.ToArray();

            // Make every selector rig resolvable by GUID so saved units load back correctly.
            int registeredNow = 0;
            foreach (var wrapper in wrappers)
            {
                GameObject rig = (wrapper != null && wrapper.UnitBaseBlueprint != null) ? wrapper.UnitBaseBlueprint.UnitBase : null;
                if (rig != null && RegisterRig(db, rig))
                {
                    registeredNow++;
                }
            }

            Debug.Log("[TABSUXMod] Added " + added.Count + " unit bases to the Unit Creator (" + wrappers.Count +
                " total; " + registeredBases + " registered rigs, " + (candidates.Count - registeredBases) +
                " blueprint rigs, " + skippedUnusable + " skipped as unusable, " + registeredNow +
                " rigs newly registered in AssetLoader).");
        }

        // Indexes a rig in AssetLoader.m_nonStreamableAssets under its root Unit entity GUID,
        // the ID that UnitBlueprint.SerializedUnit writes and the UnitBase getter resolves.
        // Returns true if the rig was newly registered.
        private static bool RegisterRig(ContentDatabase db, GameObject rig)
        {
            Unit unit = rig.GetComponent<Unit>();
            if (unit == null || unit.Entity == null)
            {
                return false;
            }
            if (s_nonStreamableAssetsField == null)
            {
                s_nonStreamableAssetsField = typeof(AssetLoader).GetField("m_nonStreamableAssets",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            var assets = (Dictionary<DatabaseID, UnityEngine.Object>)s_nonStreamableAssetsField.GetValue(db.AssetLoader);
            if (assets.ContainsKey(unit.Entity.GUID))
            {
                return false;
            }
            assets.Add(unit.Entity.GUID, rig);
            return true;
        }

        // Saved units reference their base by GUID; for rigs that were never indexed in the
        // AssetLoader (e.g. a modded rig whose mod is loaded but which only exists inside a
        // blueprint), resolve from a lazily built map of every blueprint's rig. Runs only on
        // a null result, so the vanilla fast path is untouched.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ContentDatabase), "GetGameObject")]
        public static void AfterGetGameObject(DatabaseID databaseId, ref GameObject __result)
        {
            if (__result != null || s_resolving)
            {
                return;
            }
            try
            {
                s_resolving = true; // building the map reads UnitBase getters, which re-enter GetGameObject
                var db = ContentDatabase.Instance();
                var blueprints = new List<UnitBlueprint>(db.GetAllUnitBlueprints());
                if (s_rigsByGuid == null || blueprints.Count != s_rigsBuiltFromCount)
                {
                    var map = new Dictionary<DatabaseID, GameObject>();
                    foreach (var blueprint in blueprints)
                    {
                        if (blueprint == null)
                        {
                            continue;
                        }
                        GameObject rig;
                        try
                        {
                            rig = blueprint.UnitBase;
                        }
                        catch
                        {
                            continue;
                        }
                        if (rig == null)
                        {
                            continue;
                        }
                        Unit unit = rig.GetComponent<Unit>();
                        if (unit != null && unit.Entity != null && !map.ContainsKey(unit.Entity.GUID))
                        {
                            map[unit.Entity.GUID] = rig;
                        }
                    }
                    s_rigsByGuid = map;
                    s_rigsBuiltFromCount = blueprints.Count;
                }
                GameObject found;
                if (s_rigsByGuid.TryGetValue(databaseId, out found))
                {
                    RegisterRig(db, found); // future lookups hit the AssetLoader directly
                    __result = found;
                    Debug.Log("[TABSUXMod] Resolved unit base '" + found.name + "' (" + databaseId + ") via blueprint fallback.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[TABSUXMod] GetGameObject fallback failed: " + e);
            }
            finally
            {
                s_resolving = false;
            }
        }

        private static void LoadIcon(UnitEditorManager.UnitBaseWrapper wrapper, DatabaseEntity rigEntity, UnitBlueprint fallback)
        {
            rigEntity.GetSpriteIconAsync(sprite =>
            {
                if (sprite != null)
                {
                    wrapper.BaseIcon = sprite;
                }
                else if (fallback != null && fallback.Entity != null)
                {
                    fallback.Entity.GetSpriteIconAsync(fallbackSprite => wrapper.BaseIcon = fallbackSprite);
                }
            });
        }
    }
}
