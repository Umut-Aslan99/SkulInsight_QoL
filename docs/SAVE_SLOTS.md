# Save slots (0.12.1, branch `save-states`)

Several whole games side by side: every slot is a game save of its own (story, rescued NPCs, the Witch's upgrades,
Dark Mirror, currencies, records, the run in progress) with its own Codex. Running log at the bottom.

## What Umut wants (decisions)
- 2026-10-07: 3-5 slots, a small UI or a button press; ships in 0.12.1 after 0.12.
- 2026-10-11 (after the first build, which saved *runs* at room start): **a slot is the whole game**, not a run.
  Skul has one save; Umut wants several, like profiles. Answers to the design questions:
  - **Profiles**: each slot keeps progressing; switching keeps the current game in its slot.
  - **Empty slot = a new game** (the game's start-up: intro, cinematic, tutorial).
  - **Codex per slot** (each game has its own book).
  - **No room loading of our own**: the game's native continue does it. Switching in the middle of a run keeps
    that run at the room you entered last; switching back continues it.
  - No extra pause menu button: the "Save slots" section on the SkulInsight QoL page is the UI.
- **[DECISION]** the game's own settings (keys, sound, language, ...: keys "Settings/...") stay when switching, so a
  new game doesn't reset the controls. No hotkeys (a switch goes back to the title screen; a key for that is risky).
- **[DECISION]** the section is called "Save slots" (they are whole games, not snapshots).

## How the game saves (facts, decompiled)
- One file for everything: `%USERPROFILE%/AppData/LocalLow/SouthPAW Games/Skul/<steamid>.dat` (+ `.bak`), written by
  `Platforms.Steam.SteamSaveSystem` (encrypted JSON of `Platforms.SaveFileModel`: typed key/value lists
  StringData, IntData, FloatData, BoolData, UlongData; `ToJson()` / `FromJson()` give the plain JSON).
- In memory: `PlatformManager.Instance.platform.data`; the game's `Data` objects (GameData.*) read it when they are
  made (`GameData.Initialize()`) and write it on Save. `PlatformManager.SaveDataToFile()` writes the file.
- The run ("continue") is part of it (`Save/*`), written on every room change; on start `LevelManager.LoadGame()`
  continues it (`LoadFromSave`) or loads the castle / the tutorial (`Generic/tutorialPlayed`).
- The game's own "return to title" (`Service.ResetGameScene`): load scene "Main", `levelManager.ClearEvents()`,
  `GameData.Initialize()`, `Save.ResetAll()` (ends the run), pools emptied, file written.
- Steam Cloud only knows the game's own file, i.e. the slot being played; the other slots stay on this PC.

## How a switch works (`SaveSlots/SlotSwitcher.cs`)
1. Pause menu closed; Codex progress saved; films being made dropped (`FightRecorder.ForgetSlot`: films still
   waiting to be written are thrown away by a generation number); game save files backed up
   (SaveSlots/GameSaveBackup, 5 newest).
2. Codex folders (while no film is written): `DamageInsight/Codex` -> `SaveSlots/slot<from>/Codex`,
   `SaveSlots/slot<to>/Codex` (if any) -> `DamageInsight/Codex`. The code everywhere keeps using `CodexTracker.Folder`.
3. Game save: the live model as JSON -> `SaveSlots/slot<from>/save.json`; the next slot's save.json (or an empty
   model = new game) is read; `slots.json` gets the new active slot; then the live model's lists are replaced,
   keeping the "Settings/..." values; file written. A failure before the marker moved puts the Codex folders back.
4. The mod's Codex caches are forgotten (CodexTracker, CodexAnimations, CodexPortraits, MoveHints, RunRecorder,
   CodexWindow) and the migrations run on the new folder.
5. The game's "return to title" without `Save.ResetAll()`, plus `LevelManager.Reset()` before (player and pools) and
   `_currentChapterIndex = -1` (the chapter is loaded afresh, its stages from the restored run's seed).
- Copy: the live model + the Codex folder (background task, while no film is written) into a slot. Clear: deletes
  a waiting slot's folder. The slot being played can't be copied into or cleared.
- First start with this version: the game you have is slot 1 (`slots.json` missing = active 1).

## Log
1. 2026-10-07/11 (ed2b9c7, a816d4b): room save states (snapshots of the run at room start, F5/F9/F6, pause button).
   Replaced, see decisions. The room code is in git history (`git show ed2b9c7 -- DamageInsight/SaveStates`).
2. 2026-10-11 (953f8bb on main): settings headings drawn only while wholly inside the list (a heading below the list
   showed over the frame; a bug since 0.10.1).
3. 2026-10-11 ~12:50 (910c31a): save slots as above. 346 tests pass, Release build OK.
   Umut's game save was also copied to SkulModding/GameSaveBackups/2026-10-11_before-save-states-test.

## Test todos for Umut (save slots)
1. Pause -> SkulInsight QoL -> "Save slots" (under General): Slot 1 says "(playing)" and shows your game (run,
   story, NPCs, Witch levels, dark quartz, Dark Mirror, deaths).
2. Choose slot 2 (empty) -> Play -> confirm: the title screen, then a brand-new game (intro / tutorial). Your keys,
   sound and language are unchanged.
3. Back in slot 2's game: pause -> Save slots -> slot 1 -> Play: your old game is back (castle, or your run at the
   room you entered last), with your Codex as it was.
4. Copy my game here into slot 3, then look at slot 3's details. Clear slot 3.
5. Anything odd (wrong NPCs, missing upgrades, music, stuck screen): screenshot + tell Claude; the log has
   "Save slots:" lines.
