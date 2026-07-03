using BepInEx;
using HarmonyLib;

namespace TABSUXMod
{
    [BepInPlugin("joshbaier.tabsuxmod", "TABS UX Mod", "1.0.0")]
    public class Launcher : BaseUnityPlugin
    {
        private void Awake()
        {
            new Harmony("joshbaier.tabsuxmod").PatchAll(typeof(BulkDeletePatch));
            Logger.LogInfo("TABS UX Mod loaded — bulk unit delete enabled.");
        }
    }
}
