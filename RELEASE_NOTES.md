# Quit to Main Menu 1.0.2

Adds **Quit to Main Menu** just below the top-right status bars across Inventory, Actions, Save, Map, Stats, Settings, Credits, and Mods. The button follows the HUD's position and scaling and becomes unclickable when menu controls are locked.

- Confirms before discarding unsaved progress; save normally first to keep your progress.
- Restores the existing main menu through the native save/load lifecycle and ModdingTools save-data hooks.
- Gives each fresh menu session a new run ID; Continue restores the saved run's original ID.
- Supports repeated New Games, custom character portraits, and enabling/disabling custom NPCs between menu visits.
- Defers custom NPC spawning until New Game or actual save loading, preventing duplicate custom-slot registration.
- Captures a temporary recovery snapshot before returning and attempts to restore the current run if the transition fails.

**Requirements:** Silverpine 1.7.3, BepInEx 5, and [ModdingTools 1.12.1 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest).

**Installation:** Close the game, extract `QuitToMainMenu-1.0.2.zip` into the game directory, and restart. The ZIP contains `BepInEx/plugins/QuitToMainMenu/`. The standalone DLL is also available for manual installation. Keep only one copy of the plugin.

**Validation:** Release build completed with zero warnings/errors. 113 isolated runtime checks passed with the installed plugin set and preloader patchers, using sample content and ten enabled custom NPCs. Coverage includes all eight menu tabs, menu and ModdingTools locks, four resolutions, repeated returns, Continue, NPC selection changes, saved NPC state, and failure recovery. Test code and isolation patchers are excluded from the release DLL.
