using HarmonyLib;
using LevelCreator;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TABSUXMod
{
    // Clicking a custom map card opens the Play/Edit/Delete submenu (original OnPointerClick
    // -> Click() -> ShowLevel()). That submenu opening can interfere with the engine's own
    // PointerEventData.clickCount (e.g. selection/focus changes reset the click streak), so
    // double-click detection is tracked here manually per-map instead of trusting clickCount.
    public static class DoubleClickPlayMapPatch
    {
        private const float DoubleClickWindow = 0.4f;

        private static CustomMap lastClickedMap;
        private static float lastClickTime = -1f;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CustomContentLevelButton), "OnPointerClick")]
        public static void OnLevelButtonPointerClick(CustomContentLevelButton __instance, PointerEventData eventData)
        {
            var map = __instance.customMap;
            if (map == null) return;

            float now = Time.unscaledTime;
            bool isDoubleClick = map == lastClickedMap && (now - lastClickTime) <= DoubleClickWindow;

            lastClickedMap = map;
            lastClickTime = now;

            if (!isDoubleClick) return;

            lastClickedMap = null;
            lastClickTime = -1f;

            if (__instance.browserManager == null) return;

            var sideBar = __instance.browserManager.customContentSideBar;
            if (sideBar == null) return;

            sideBar.Play();
        }
    }
}
