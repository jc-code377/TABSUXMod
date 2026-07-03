# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build TABSUXMod.csproj -c Release
```

The post-build step automatically copies `TABSUXMod.dll` into the Thunderstore Mod Manager plugins folder. **Always launch TABS through Thunderstore Mod Manager**, not Steam directly — BepInEx is only injected by the manager.

## Checking logs

```powershell
Get-Content "C:\Users\joshu\AppData\Roaming\Thunderstore Mod Manager\DataFolder\TABS\profiles\Default\BepInEx\LogOutput.log" | Select-String "TABSUXMod" | Select-Object -Last 50
```

All mod log entries are prefixed `[TABSUXMod]`.

## Inspecting game types (before writing reflection code)

Use Mono.Cecil — it's already present in the BepInEx core folder:

```powershell
Add-Type -Path "C:\Users\joshu\AppData\Roaming\Thunderstore Mod Manager\DataFolder\TABS\profiles\Default\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("C:\Program Files (x86)\Steam\steamapps\common\Totally Accurate Battle Simulator\TotallyAccurateBattleSimulator_Data\Managed\Assembly-CSharp.dll")

# Find a type and list its methods
$t = $asm.MainModule.Types | Where-Object { $_.Name -eq "TypeNameHere" }
$t.Methods | Select-Object Name, IsStatic, IsPublic

# Decompile a method body
$t.Methods | Where-Object { $_.Name -eq "MethodName" } | ForEach-Object { $_.Body.Instructions }
```

This is the only reliable way to find actual method names, signatures, and access modifiers before writing reflection code. The binary string-search approach (`[System.IO.File]::ReadAllBytes`) is a fallback for broad discovery only — it cannot distinguish class boundaries or access modifiers.

## Key paths

| What | Path |
|---|---|
| Game DLLs | `C:\Program Files (x86)\Steam\steamapps\common\Totally Accurate Battle Simulator\TotallyAccurateBattleSimulator_Data\Managed\` |
| BepInEx core | `C:\Users\joshu\AppData\Roaming\Thunderstore Mod Manager\DataFolder\TABS\profiles\Default\BepInEx\core\` |
| Plugins (output) | `C:\Users\joshu\AppData\Roaming\Thunderstore Mod Manager\DataFolder\TABS\profiles\Default\BepInEx\plugins\` |
| BepInEx log | `C:\Users\joshu\AppData\Roaming\Thunderstore Mod Manager\DataFolder\TABS\profiles\Default\BepInEx\LogOutput.log` |
| Custom units on disk | `C:\Program Files (x86)\Steam\steamapps\common\Totally Accurate Battle Simulator\TotallyAccurateBattleSimulator_Data\CustomContent\CustomUnits\` |

## Architecture

### Mod loader stack

TABS runs on Unity (net472). BepInEx 5 is injected via `winhttp.dll` by Thunderstore Mod Manager. Harmony 2 (`0Harmony20.dll`) patches game methods at runtime via IL rewriting. Mods are `.dll` files dropped into `BepInEx/plugins/`.

### This mod (TABSUXMod)

Two files:
- **`Launcher.cs`** — `BaseUnityPlugin` entry point. Registers all Harmony patches in `BulkDeletePatch` via `new Harmony(id).PatchAll(typeof(BulkDeletePatch))`.
- **`BulkDeletePatch.cs`** — All patch logic. Three Harmony postfixes drive everything:
  1. `CustomContentGridBrowser.OnEnable` — clears the selection set when the browser opens/closes.
  2. `CustomContentGridBrowser.Populate` — injects the "DELETE SELECTED" button after the grid is fully built. Uses this timing because `OnEnable` fires before `layout01` and the `New Unit` button exist in the scene.
  3. `UnitButtonBase.Setup(UnitBlueprint unit)` — stamps a 28×28 checkbox overlay onto each unit card.

### Button injection pattern

The "DELETE SELECTED" button is a **clone of the "New Unit" button** (GameObject name `"New Unit"`, parent `"UnitGrid"`). To find it: get `CustomContentUnitBrowser` from the browser, read its private `layout01` field (a `GridLayoutGroup`) via reflection, then walk `layout01.transform.parent` (the `UnitGrid`) to find the sibling named `"New Unit"`. The clone's `onClick` is replaced with a fresh `Button.ButtonClickedEvent()` — not just `RemoveAllListeners()` — because cloned buttons retain Inspector-wired persistent calls which `RemoveAllListeners` does not clear.

### Deletion path

The vanilla delete flow is: `CustomContentSideBar.DeleteUnit()` → `DeleteContent()` → `ModalPanel.Choice()` (the "are you sure?" popup) → on confirm → `DeleteBattleCreatorSharedCommandsContent(ContentTypeFilter, IDatabaseEntity, string folderPath)`.

This mod **bypasses the modal** by calling `DeleteBattleCreatorSharedCommandsContent` directly via reflection. It is a `private` instance method on `CustomContentSideBar`. Arguments:
- `ContentTypeFilter.Units` = enum value `4`
- The `UnitBlueprint` as `IDatabaseEntity`
- Folder path = `CustomContentFilePaths.UnitDirectoryPath` (static property) + `unit.Entity.GUID`

`CustomContentSideBar` is found at runtime via `Object.FindObjectOfType(sideBarType)`. All reflection handles are cached after first resolution.

### Key game types

| Type | Namespace | Role |
|---|---|---|
| `CustomContentGridBrowser` | `Landfall.TABS` | Outer paginated browser; owns `Populate`, `Refresh` |
| `CustomContentUnitBrowser` | `Landfall.TABS` | Inner unit grid; holds `layout01` (GridLayoutGroup) and `customContentManager` |
| `UnitButtonBase` | `Landfall.TABS` | Base class for each unit card; `Setup(UnitBlueprint)` called per card |
| `CustomContentSideBar` | `Landfall.TABS` | Detail panel; owns `loadedUnit`, `DeleteUnit()`, `DeleteBattleCreatorSharedCommandsContent()` |
| `CustomContetnManager` | (no namespace, typo in game) | Navigation manager; `GoToUnitCreator()`, page routing — **not** the deletion path |
| `DMNewContentManager` | (static) | "New content" badge tracker only — **not** related to deletion despite the name |
| `ContentTypeFilter` | `Landfall.TABS.Workshop` | Enum: `Units=4`, `Factions=8`, `Battles=1`, `Campaigns=2`, `Maps=32` |
| `CustomContentFilePaths` | `Landfall.TABS.Workshop` | Static path helpers; `UnitDirectoryPath`, `FileEndingUnit` |
| `ModalPanel` | `Landfall.TABS` | Confirmation popup; `Choice(...)` shows a yes/no dialog |

### MFD reference project (`C:\Users\joshu\Downloads\MFD-main\MFD-main`)

A separate content-modding template (not UI). Useful as reference for:
- How to register factions/units into `LandfallContentDatabase` via reflection (`Utilities.AddUnitToDatabase`, `AddFactionToDatabase`)
- The `Mod` base class pattern for wrapping game objects
- `DEV_MODE` — when true, dumps all game item names to `BepInEx/plugins/MFDPrints/*.txt` (weapons, units, factions, etc.) — essential for finding vanilla item names to reference

### Common pitfalls

- **`OnEnable` fires too early** — the unit grid and its sibling buttons don't exist yet. Use `Populate` for anything that needs the grid to be built.
- **Cloned button retains listeners** — always replace `onClick` with `new Button.ButtonClickedEvent()`, not just `RemoveAllListeners()`.
- **`DMNewContentManager` is unrelated to deletion** — its name is misleading; it only tracks "new content" notification badges.
- **`CustomContetnManager` is misspelled** in the game source — one 't' in "Content". Use this exact spelling in `Assembly.GetType()` calls.
- **`DeleteBattleCreatorSharedCommandsContent` is private and instance-bound** — requires `BindingFlags.NonPublic | BindingFlags.Instance` and a live `CustomContentSideBar` instance.
