using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TABSUXMod
{
    // Clicking "Custom" shows "Warning: Mods can cause the game to become unstable.
    // Do you want to load mods now?" (POPUP_MODIO_CONFIRM_LOAD_MODS) every session.
    //
    // From IL of Landfall.TABS.Workshop.CustomContentLoaderModIO:
    //   CheckPermissionToLoadMods(bool refresh, CheckPermissionToLoadModsCallback doneCallback)
    //     - if DidGivePermissionToLoadMods: OnCheckedPermissionToLoadMods(false, doneCallback)
    //     - else: shows the modal; YES = { DidGivePermissionToLoadMods = true;
    //             OnCheckedPermissionToLoadMods(refresh, doneCallback); }
    //
    // Fix: prefix CheckPermissionToLoadMods, skip the original, and run the YES branch directly —
    // set the permission flag and forward (refresh, doneCallback) to OnCheckedPermissionToLoadMods.
    // Identical to the user clicking YES, just without the popup.
    public static class AutoConfirmLoadModsPatch
    {
        private static MethodInfo setPermission;   // private set_DidGivePermissionToLoadMods(bool)
        private static MethodInfo onCheckedPermission; // private OnCheckedPermissionToLoadMods(bool, callback)

        public static void Apply(Harmony harmony)
        {
            Assembly asm = null;
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "Assembly-CSharp") { asm = a; break; }
            }
            if (asm == null)
            {
                Debug.LogWarning("[TABSUXMod] Assembly-CSharp not found — load-mods auto-confirm skipped.");
                return;
            }

            var loaderType = asm.GetType("Landfall.TABS.Workshop.CustomContentLoaderModIO");
            if (loaderType == null)
            {
                Debug.LogWarning("[TABSUXMod] CustomContentLoaderModIO not found — load-mods auto-confirm skipped.");
                return;
            }

            var nonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

            var permissionProp = loaderType.GetProperty("DidGivePermissionToLoadMods",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            setPermission = permissionProp != null ? permissionProp.GetSetMethod(true) : null;
            onCheckedPermission = loaderType.GetMethod("OnCheckedPermissionToLoadMods", nonPublicInstance);

            var checkPermission = loaderType.GetMethod("CheckPermissionToLoadMods",
                BindingFlags.Public | BindingFlags.Instance);

            if (setPermission == null || onCheckedPermission == null || checkPermission == null)
            {
                Debug.LogWarning("[TABSUXMod] CustomContentLoaderModIO members not found — load-mods auto-confirm skipped.");
                return;
            }

            var pre = typeof(AutoConfirmLoadModsPatch).GetMethod(
                nameof(CheckPermissionPrefix), BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(checkPermission, prefix: new HarmonyMethod(pre));

            Debug.Log("[TABSUXMod] Load-mods warning auto-confirm active.");
        }

        // Replicate the modal's YES action and skip the original (which would show the popup).
        // Parameters are bound by name to the original's (refresh, doneCallback).
        private static bool CheckPermissionPrefix(object __instance, bool refresh, object doneCallback)
        {
            Debug.Log("[TABSUXMod] Auto-confirming 'load mods?' warning.");
            setPermission.Invoke(__instance, new object[] { true });
            onCheckedPermission.Invoke(__instance, new object[] { refresh, doneCallback });
            return false; // skip original — no popup
        }
    }
}
