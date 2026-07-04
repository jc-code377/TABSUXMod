using BepInEx;
using HarmonyLib;

namespace TABSUXMod
{
    [BepInPlugin("joshbaier.tabsuxmod", "Better UX", "1.0.0")]
    public class Launcher : BaseUnityPlugin
    {
        private void Awake()
        {
            var harmony = new Harmony("joshbaier.tabsuxmod");
            harmony.PatchAll(typeof(BulkDeletePatch));
            harmony.PatchAll(typeof(DoubleClickPlayMapPatch));
            SuppressLaunchInvitePatch.Apply(harmony);
            AutoConfirmLoadModsPatch.Apply(harmony);
            Logger.LogInfo("Better UX loaded.");
        }
    }
}
