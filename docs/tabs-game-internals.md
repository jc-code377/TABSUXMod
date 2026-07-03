# TABS Game Internals — Discovered Knowledge

This documents what we've learned about TABS internals through IL inspection and live modding.
It is intentionally scoped to what we've actually verified — not guesses.

---

## Custom Unit Browser UI

The custom unit browser is composed of several nested MonoBehaviours:

```
CustomContentGridBrowser        ← outer paginated browser
  └── CustomContentUnitBrowser  ← inner unit grid
        └── UnitGrid (Transform)
              ├── layout01      ← GridLayoutGroup holding the unit cards (private field)
              └── New Unit      ← the "+" / Make New Unit button (sibling of layout01)
```

**Key lifecycle events:**
- `OnEnable` fires when the browser screen opens. The grid and bottom buttons do NOT exist yet at this point.
- `Populate` fires after the grid is fully built. This is the correct moment to inject new UI or read the button hierarchy.
- `Refresh` can be called on `CustomContentGridBrowser` at any time to repopulate the grid.

**Finding `layout01`:**
It is a `private` field on `CustomContentUnitBrowser`. Must be accessed via reflection:
```csharp
typeof(CustomContentUnitBrowser).GetField("layout01",
    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
```

**The "New Unit" button:**
- GameObject name: `"New Unit"`
- Parent: `UnitGrid` (same parent as `layout01`)
- Has persistent (Inspector-wired) `onClick` listeners that open the unit creator
- `RemoveAllListeners()` does NOT clear these — you must replace the entire event:
  ```csharp
  button.onClick = new Button.ButtonClickedEvent();
  ```

---

## Unit Cards

Each card in the grid is driven by `UnitButtonBase`. The method `Setup(UnitBlueprint unit)` is called once per card when the grid populates. Patching this postfix is the correct place to add per-card UI overlays.

---

## Deletion Flow

The vanilla deletion path when a user clicks delete in the sidebar:

```
CustomContentSideBar.DeleteUnit()
  └── DeleteContent()
        └── ModalPanel.Choice(...)   ← shows "are you sure?" confirmation popup
              └── [on confirm] DeleteBattleCreatorSharedCommandsContent(
                      ContentTypeFilter contentType,
                      IDatabaseEntity contentData,
                      string folderPath)
```

**To delete without the modal**, call `DeleteBattleCreatorSharedCommandsContent` directly:

```csharp
// Find the sidebar at runtime
var sideBarType = assembly.GetType("Landfall.TABS.CustomContentSideBar");
var sidebar = Object.FindObjectOfType(sideBarType) as MonoBehaviour;

// Get the method
var method = sideBarType.GetMethod(
    "DeleteBattleCreatorSharedCommandsContent",
    BindingFlags.NonPublic | BindingFlags.Instance);

// Build the arguments
var filterType  = assembly.GetType("Landfall.TABS.Workshop.ContentTypeFilter");
var unitsFilter = Enum.ToObject(filterType, 4); // Units == 4

var pathsType   = assembly.GetType("Landfall.TABS.Workshop.CustomContentFilePaths");
var unitDir     = (string)pathsType.GetProperty("UnitDirectoryPath",
    BindingFlags.Public | BindingFlags.Static).GetValue(null);

var folderPath  = unitDir + unit.Entity.GUID;

// Invoke
method.Invoke(sidebar, new object[] { unitsFilter, unit, folderPath });
```

---

## Key Types Reference

| Type | Namespace | Notes |
|---|---|---|
| `CustomContentGridBrowser` | `Landfall.TABS` | Outer browser; `Populate`, `Refresh` |
| `CustomContentUnitBrowser` | `Landfall.TABS` | Inner grid; holds private `layout01` |
| `UnitButtonBase` | `Landfall.TABS` | Base for each unit card; `Setup(UnitBlueprint)` |
| `UnitBlueprint` | `Landfall.TABS` | The unit data object; inherits from `SerializedScriptableObject` (Odin), implements `IDatabaseEntity` |
| `CustomContentSideBar` | `Landfall.TABS` | Detail panel; owns deletion logic |
| `ModalPanel` | `Landfall.TABS` | Confirmation popup; `Choice(...)` |
| `ContentTypeFilter` | `Landfall.TABS.Workshop` | Enum — `Units=4`, `Factions=8`, `Battles=1`, `Campaigns=2`, `Maps=32` |
| `CustomContentFilePaths` | `Landfall.TABS.Workshop` | Static path helpers; `UnitDirectoryPath` (static property) |
| `CustomContetnManager` | (no namespace) | Navigation — `GoToUnitCreator()`. **Misspelled in game source** (one 't'). Not related to deletion. |
| `DMNewContentManager` | (static) | "New content" badge tracking only. Not related to deletion despite the name. |

---

## Gotchas

- **`CustomContetnManager` is misspelled** — one 't' in "Content". Use this exact spelling in any `Assembly.GetType()` call.
- **`DMNewContentManager` is a static class** — cannot be used as a type argument for `FindObjectOfType<T>()`. Must use the non-generic overload.
- **`UnitBlueprint` requires `Sirenix.Serialization`** as a reference — it inherits from `SerializedScriptableObject`. Without this reference the project won't compile.
- **`DeleteUnit()` always shows the modal** — there is no flag to skip it. The only bypass is calling `DeleteBattleCreatorSharedCommandsContent` directly.
- **Cloned Unity buttons retain Inspector listeners** — `RemoveAllListeners()` only removes runtime listeners. Replace the entire `onClick` field to fully reset.

---

## Inspecting the Game Binary

Use Mono.Cecil (already in BepInEx core) to read `Assembly-CSharp.dll` without loading it:

```powershell
Add-Type -Path "$env:APPDATA\Thunderstore Mod Manager\DataFolder\TABS\profiles\Default\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
    "C:\Program Files (x86)\Steam\steamapps\common\Totally Accurate Battle Simulator\TotallyAccurateBattleSimulator_Data\Managed\Assembly-CSharp.dll")

# List methods on a type
$t = $asm.MainModule.Types | Where-Object { $_.Name -eq "CustomContentSideBar" }
$t.Methods | Select-Object Name, IsPublic, IsStatic

# Read IL instructions for a method
($t.Methods | Where-Object { $_.Name -eq "DeleteUnit" }).Body.Instructions
```

This is the primary tool for discovering method signatures and access levels before writing reflection code.
