using BepInEx;
using HarmonyLib;

namespace TABSUXMod
{
    [BepInPlugin("joshbaier.tabsuxmod", "Better UX", "1.1.2")]
    public class Launcher : BaseUnityPlugin
    {
        private void Awake()
        {
            var harmony = new Harmony("joshbaier.tabsuxmod");
            harmony.PatchAll(typeof(BulkDeletePatch));
            harmony.PatchAll(typeof(DoubleClickPlayMapPatch));
            harmony.PatchAll(typeof(AllUnitBasesPatch));
            SuppressLaunchInvitePatch.Apply(harmony);
            AutoConfirmLoadModsPatch.Apply(harmony);
            foreach (var method in harmony.GetPatchedMethods())
            {
                Logger.LogInfo("Patched: " + method.DeclaringType?.Name + "." + method.Name);
            }
            Logger.LogInfo("Better UX loaded.");
        }
    }
}
