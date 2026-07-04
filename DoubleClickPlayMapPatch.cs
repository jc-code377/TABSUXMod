using HarmonyLib;
using Landfall.TABS;
using LevelCreator;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TABSUXMod
{
    // Clicking a custom map card opens the Play/Edit/Delete submenu, whose fade backdrop
    // (FactionCreatorFadeBG) turns on raycastTarget and covers the whole grid — including
    // the card that was just clicked. So a real second click never reaches
    // CustomContentLevelButton.OnPointerClick again; it lands on the fade instead, which
    // normally just closes the submenu. To get double-click-to-play, we remember the map
    // + time of the first click and, when the fade intercepts a follow-up click shortly
    // after while the sidebar is still showing that same map, Play it instead of closing.
    public static class DoubleClickPlayMapPatch
    {
        private const float DoubleClickWindow = 0.4f;

        private static CustomMap lastClickedMap;
        private static float lastClickTime = -1f;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CustomContentLevelButton), "OnPointerClick")]
        public static void OnLevelButtonPointerClick(CustomContentLevelButton __instance, PointerEventData eventData)
        {
            lastClickedMap = __instance.customMap;
            lastClickTime = Time.unscaledTime;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FactionCreatorFadeBG), "OnPointerClick")]
        public static bool OnFadePointerClick(FactionCreatorFadeBG __instance)
        {
            if (lastClickedMap == null) return true;
            if (Time.unscaledTime - lastClickTime > DoubleClickWindow) return true;

            var sideBar = __instance.sidebar;
            if (sideBar == null || sideBar.levelParent == null || !sideBar.levelParent.activeSelf) return true;

            lastClickedMap = null;
            lastClickTime = -1f;

            sideBar.Play();
            return false;
        }
    }
}
