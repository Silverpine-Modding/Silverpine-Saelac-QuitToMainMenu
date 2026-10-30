# Quit to Main Menu

Version **1.0.2**, by **Saelac and ChatGPT**.

[Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-QuitToMainMenu/releases/latest)

BepInEx plugin for Silverpine 1.7.3. Adds **Quit to Main Menu** just below the status bars at the top right of the screen across all in-game menu tabs: Inventory, Actions, Save, Map, Stats, Settings, Credits, and Mods. The button follows the bars' actual bounds and inherits the HUD's UI scaling, including live resolution changes. It hides when the menus close or the main menu is open.

## Use

Open any in-game menu tab, click **Quit to Main Menu**, and confirm. Save normally first if you want to keep your progress. Cancel keeps the current run intact. The returned main menu supports Continue, Load, character creation, and its existing mod tools.

Custom NPCs can be enabled or disabled between menu visits. Their spawning is deferred while restoring the startup menu; New Game and actual save loading use the current selections. On load, Silverpine removes disabled custom NPCs, keeps saved state for still-enabled NPCs, and spawns newly enabled ones.

The button follows the menu navigation controls' enabled state, including parent CanvasGroup interaction/raycast locks. It stays visible but unclickable while the other menu controls are locked, during save/load, turn processing, dialogue, exclusive inventory operations, or an active ModdingTools editor session. The confirmation's Quit action rechecks these conditions before starting the transition.

## Installation

Requires BepInEx 5 and [**ModdingTools 1.12.1 or newer**](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest). Close Silverpine and extract `QuitToMainMenu-1.0.2.zip` into the game directory; it contains `BepInEx/plugins/QuitToMainMenu/`. Alternatively, put the standalone `QuitToMainMenu.dll` in that folder. Keep only one copy of the plugin, then restart Silverpine. No game assemblies or existing saves are changed by installation.

## Session handling

- Captures the initialized startup world before the first New Game or Load, before Starting Inventory changes the player.
- Restores through `SerializationManager.Load`, retaining native load-completion events and other mods' normal load hooks.
- Uses ModdingTools' registered save-data resets and companion restoration. It keeps framework registrations and Harmony patches installed throughout the process.
- Creates a new startup run ID before load-completion callbacks. Loading a saved game restores that save's original ID. Each later return gets a distinct ID.
- Defers `CustomNPCManager.SpawnInNPCs` only during startup restoration, avoiding duplicate custom-slot registration on the next New Game. Other native load callbacks and ordinary save loads keep running.
- Restores the existing menu's character portraits, Continue preview, camera pan, input block, and controls. Custom character selection is supported.
- Removes native character-creation callbacks that still reference replaced players. Includes compatibility for Starting Inventory 1.0.1's menu-instance guard and QOL Char Selector's portrait cache.

Startup snapshots and temporary recovery saves are stored in `BepInEx/cache/QuitToMainMenu/`. They have no `.savmeta` entry and do not appear in Continue or the save list. A failed transition attempts to restore the unsaved run and its ModdingTools companion data. If recovery also fails, the log identifies the retained recovery file. Normal exit removes the current startup snapshot.

If startup capture fails, normal gameplay remains available and the return button stays disabled for that process; the BepInEx log explains the error.

## Mod integration

Existing `SerializationManager.Load` patches and completion callbacks run during the return. A consumer of `ModSaveData.Register` should restore default world state in its `Reset` callback and must not retain references to destroyed player/NPC objects.

Mods with additional transient state can optionally subscribe to:

```csharp
Silverpine.QuitToMainMenu.MenuReturnEvents.Returning += CancelPendingSessionWork;
Silverpine.QuitToMainMenu.MenuReturnEvents.Returned += RefreshMenuState;
```

`Returning` runs after a recovery snapshot is captured and before world replacement. A thrown exception aborts the transition. `Returned` runs after the menu has been restored; subscriber exceptions are logged independently. `MenuReturnEvents.IsReturning` covers the entire transition.

These notifications supplement the native and ModdingTools lifecycle; they do not guarantee that unrelated mods with process-wide state support repeated games. Installed-plugin integration checks use isolated test configuration and sample content, not the user's save files.

## Build and verification

Build `QuitToMainMenu.csproj` in Release with a .NET SDK. References point to the local Silverpine/BepInEx/ModdingTools installation; no NuGet packages are required.

From a clone, pass the game installation directory explicitly:

```powershell
dotnet build .\QuitToMainMenu.csproj -c Release -p:GameDir="C:\Games\Silverpine"
```

Game assemblies and dependency DLLs are not included in this repository. The remaining runtime-test commands assume the source is located at `Plugin Development/QuitToMainMenu` inside the game directory, as in the development workspace.

The developer runtime harness is excluded from the normal build. To enable it:

```powershell
dotnet build .\QuitToMainMenu.csproj -c Release -p:RuntimeTests=true -o '..\..\artifacts\ReturnMenuInvestigation-20261029\test-plugin'
dotnet build .\Tests\Isolation\Isolation.csproj -c Release
.\Tests\Run-RuntimeChecks.ps1
# Add -AllMods to load installed plugin folders, loose DLLs and preloader patchers,
# excluding the update checker. -NpcRegressionOnly runs the focused spawn regression.
```

The harness requires a marked test copy and a preloader patch that redirects `Application.persistentDataPath` into that copy. It skips inference setup. It exercises the actual buttons, screen-space raycasts, live resolution changes (16:9, 4:3, and 16:10), visibility, cancellation, repeated returns and character creation, built-in/custom portraits, Continue, save/mod payload isolation, and recovery from an injected load failure. Offscreen captures verify the native UI without displaying a desktop window. `-TestRoot` can reuse a marked copy under `artifacts`; each run resets only that copy's test saves and plugins. The all-mods suite supplies a fixed Starting Inventory fixture. Never install the test build or isolation patcher into the normal game.

## Version 1.0.2 — 2026-10-29

Made the button available across all in-game menu tabs. It now follows shared menu interaction locks, including disabled navigation buttons, parent CanvasGroups, and ModdingTools sessions. Added runtime checks for every tab and locked-button behavior.

## Version 1.0.1 — 2026-10-29

Fixed duplicate `CustomNPCSlot1` registration after returning to the menu and starting another game. Added enabled custom NPC fixtures, menu selection changes before New Game/Continue, saved NPC-state checks, and native spawn-error detection to the runtime suite. With the extended-slot patcher installed, tests enable ten custom NPCs plus a disabled alternate.

## Version 1.0.0 — 2026-10-29

Initial release.
