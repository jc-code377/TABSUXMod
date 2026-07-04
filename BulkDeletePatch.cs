using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Landfall.TABS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TABSUXMod
{
    public static class BulkDeletePatch
    {
        private static readonly HashSet<UnitBlueprint> selected = new HashSet<UnitBlueprint>();

        // All live overlay GameObjects so we can show/hide them as a group
        private static readonly List<GameObject> overlays = new List<GameObject>();

        private static bool selectMode = false;

        private static Button deleteButton;
        private static TextMeshProUGUI deleteButtonLabel;

        // Cached reflection handles for deletion
        private static MonoBehaviour sideBarInstance;
        private static MethodInfo deleteBCSCMethod;
        private static System.Type contentTypeFilterType;
        private static System.Type customContentFilePathsType;

        // ── Clear selection when the browser opens ───────────────────────────────────
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CustomContentGridBrowser), "OnEnable")]
        public static void OnBrowserOpen(CustomContentGridBrowser __instance)
        {
            ExitSelectMode();
        }

        // ── After Populate the grid and its sibling buttons are fully in the scene ───
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CustomContentGridBrowser), "Populate")]
        public static void OnBrowserPopulate(CustomContentGridBrowser __instance)
        {
            EnsureDeleteButton(__instance);
            RefreshDeleteButton();
        }

        // ── Stamp a checkbox onto every unit card ────────────────────────────────────
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitButtonBase), "Setup")]
        public static void OnUnitButtonSetup(UnitButtonBase __instance, UnitBlueprint unit)
        {
            if (unit == null) return;

            var go = __instance.gameObject;

            const string overlayName = "BulkSelectOverlay";
            if (go.transform.Find(overlayName) != null) return;

            // Full-card click catcher — covers the whole card so clicking anywhere
            // on the unit toggles selection while selectMode is on. It sits on top
            // of the card's normal button but only blocks raycasts in select mode,
            // so normal card clicks (open unit editor, etc.) are unaffected otherwise.
            var overlayGO = new GameObject(overlayName, typeof(RectTransform));
            overlayGO.transform.SetParent(go.transform, false);
            overlayGO.SetActive(selectMode);
            overlays.Add(overlayGO);

            var rt = overlayGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var fullBg = overlayGO.AddComponent<Image>();
            fullBg.color = new Color(0f, 0f, 0f, 0f); // invisible, just a raycast target

            var btn = overlayGO.AddComponent<Button>();
            btn.targetGraphic = fullBg;
            btn.transition = Selectable.Transition.None;

            // Checkbox badge, drawn in the corner, purely visual
            var checkboxGO = new GameObject("Checkbox", typeof(RectTransform));
            checkboxGO.transform.SetParent(overlayGO.transform, false);
            var checkboxRT = checkboxGO.GetComponent<RectTransform>();
            checkboxRT.anchorMin        = new Vector2(0f, 1f);
            checkboxRT.anchorMax        = new Vector2(0f, 1f);
            checkboxRT.pivot            = new Vector2(0f, 1f);
            checkboxRT.anchoredPosition = new Vector2(4f, -4f);
            checkboxRT.sizeDelta        = new Vector2(28f, 28f);

            var bg = checkboxGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            var checkGO = new GameObject("Check", typeof(RectTransform));
            checkGO.transform.SetParent(checkboxGO.transform, false);
            var checkRT = checkGO.GetComponent<RectTransform>();
            checkRT.anchorMin = Vector2.zero;
            checkRT.anchorMax = Vector2.one;
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;

            var checkText = checkGO.AddComponent<TextMeshProUGUI>();
            checkText.text      = "";
            checkText.fontSize  = 18f;
            checkText.alignment = TextAlignmentOptions.Center;
            checkText.color     = Color.white;

            var capturedUnit  = unit;
            var capturedCheck = checkText;
            var capturedBg    = bg;

            btn.onClick.AddListener(() =>
            {
                if (!selectMode) return;

                if (selected.Contains(capturedUnit))
                {
                    selected.Remove(capturedUnit);
                    capturedCheck.text = "";
                    capturedBg.color   = new Color(0f, 0f, 0f, 0.55f);
                }
                else
                {
                    selected.Add(capturedUnit);
                    capturedCheck.text = "✔";
                    capturedBg.color   = new Color(0.18f, 0.65f, 0.18f, 0.85f);
                }
                RefreshDeleteButton();
            });

            // Restore checked state if this card is re-setup while still selected
            if (selected.Contains(unit))
            {
                checkText.text = "✔";
                bg.color = new Color(0.18f, 0.65f, 0.18f, 0.85f);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static void EnsureDeleteButton(CustomContentGridBrowser browser)
        {
            const string btnName = "BulkDeleteBtn";

            if (deleteButton != null && deleteButton.gameObject != null)
                return;

            var unitBrowser = browser.GetComponentInChildren<CustomContentUnitBrowser>(true);
            if (unitBrowser == null)
            {
                Debug.LogWarning("[TABSUXMod] CustomContentUnitBrowser not found.");
                return;
            }

            var layout01Field = typeof(CustomContentUnitBrowser)
                .GetField("layout01", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var layout01 = layout01Field?.GetValue(unitBrowser) as Component;

            if (layout01 == null)
            {
                Debug.LogWarning("[TABSUXMod] layout01 field not found.");
                return;
            }

            var unitGrid = layout01.transform.parent;
            Button newUnitBtn = null;
            foreach (Transform child in unitGrid)
            {
                if (child.name == "New Unit")
                {
                    newUnitBtn = child.GetComponent<Button>();
                    break;
                }
            }

            if (newUnitBtn == null)
            {
                Debug.LogWarning("[TABSUXMod] 'New Unit' not found in UnitGrid. Siblings:");
                foreach (Transform child in unitGrid)
                    Debug.Log($"[TABSUXMod] sibling: '{child.name}'");
                return;
            }

            var btnGO  = Object.Instantiate(newUnitBtn.gameObject, unitGrid, false);
            btnGO.name = btnName;

            deleteButton = btnGO.GetComponent<Button>();
            deleteButton.onClick = new Button.ButtonClickedEvent();
            deleteButton.onClick.AddListener(OnDeleteButtonClicked);

            var srcRT = newUnitBtn.GetComponent<RectTransform>();
            var dstRT = btnGO.GetComponent<RectTransform>();
            dstRT.anchorMin        = srcRT.anchorMin;
            dstRT.anchorMax        = srcRT.anchorMax;
            dstRT.pivot            = srcRT.pivot;
            dstRT.sizeDelta        = srcRT.sizeDelta;
            dstRT.anchoredPosition = srcRT.anchoredPosition + new Vector2(srcRT.sizeDelta.x + 8f, 0f);
            btnGO.transform.SetSiblingIndex(newUnitBtn.transform.GetSiblingIndex() + 1);

            foreach (var tmp in btnGO.GetComponentsInChildren<TextMeshProUGUI>())
                Object.Destroy(tmp.gameObject);

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(btnGO.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            deleteButtonLabel           = labelGO.AddComponent<TextMeshProUGUI>();
            deleteButtonLabel.alignment = TextAlignmentOptions.Center;
            deleteButtonLabel.fontSize  = 18f;
            deleteButtonLabel.color     = Color.white;

            CacheSideBar();
        }

        private static void OnDeleteButtonClicked()
        {
            if (!selectMode)
            {
                // Enter select mode — show checkboxes
                selectMode = true;
                SetOverlaysVisible(true);
                RefreshDeleteButton();
            }
            else if (selected.Count > 0)
            {
                // Confirm deletion, then exit select mode
                DeleteSelected();
            }
            else
            {
                // Already in select mode but nothing chosen — cancel
                ExitSelectMode();
            }
        }

        private static void ExitSelectMode()
        {
            selectMode = false;
            selected.Clear();
            SetOverlaysVisible(false);
            // Reset any checked state visuals
            foreach (var ov in overlays)
            {
                if (ov == null) continue;
                var checkboxTransform = ov.transform.Find("Checkbox");
                var img = checkboxTransform != null ? checkboxTransform.GetComponent<Image>() : null;
                if (img != null) img.color = new Color(0f, 0f, 0f, 0.55f);
                var txt = ov.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = "";
            }
            RefreshDeleteButton();
        }

        private static void SetOverlaysVisible(bool visible)
        {
            overlays.RemoveAll(o => o == null);
            foreach (var ov in overlays)
                ov.SetActive(visible);
        }

        private static void CacheSideBar()
        {
            if (sideBarInstance != null) return;

            var asm = typeof(CustomContentGridBrowser).Assembly;

            var sideBarType = asm.GetType("Landfall.TABS.CustomContentSideBar");
            if (sideBarType == null) return;

            sideBarInstance = Object.FindObjectOfType(sideBarType) as MonoBehaviour;
            if (sideBarInstance == null) return;

            deleteBCSCMethod = sideBarType.GetMethod(
                "DeleteBattleCreatorSharedCommandsContent",
                BindingFlags.NonPublic | BindingFlags.Instance);

            contentTypeFilterType      = asm.GetType("Landfall.TABS.Workshop.ContentTypeFilter");
            customContentFilePathsType = asm.GetType("Landfall.TABS.Workshop.CustomContentFilePaths");
        }

        private static void RefreshDeleteButton()
        {
            if (deleteButtonLabel == null) return;

            if (!selectMode)
            {
                deleteButtonLabel.text    = "SELECT TO DELETE";
                deleteButton.interactable = true;
            }
            else if (selected.Count == 0)
            {
                deleteButtonLabel.text    = "CANCEL";
                deleteButton.interactable = true;
            }
            else
            {
                deleteButtonLabel.text    = $"DELETE ({selected.Count})";
                deleteButton.interactable = true;
            }
        }

        private static void DeleteSelected()
        {
            if (selected.Count == 0) return;

            CacheSideBar();

            if (sideBarInstance == null || deleteBCSCMethod == null ||
                contentTypeFilterType == null || customContentFilePathsType == null)
            {
                Debug.LogError("[TABSUXMod] Could not resolve deletion methods via reflection.");
                return;
            }

            var unitsFilter = System.Enum.ToObject(contentTypeFilterType, 4);

            var unitDirProp = customContentFilePathsType.GetProperty(
                "UnitDirectoryPath", BindingFlags.Public | BindingFlags.Static);

            if (unitDirProp == null)
            {
                Debug.LogError("[TABSUXMod] UnitDirectoryPath property not found.");
                return;
            }

            var unitDirPath = (string)unitDirProp.GetValue(null);

            foreach (var unit in selected)
            {
                var folderPath = unitDirPath + unit.Entity.GUID;
                deleteBCSCMethod.Invoke(sideBarInstance, new object[] { unitsFilter, unit, folderPath });
            }

            ExitSelectMode();

            Object.FindObjectOfType<CustomContentGridBrowser>()?.Refresh();
        }
    }
}
