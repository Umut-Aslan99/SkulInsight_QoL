# 0.16 step log (player feedback after 0.15.0)

Started 2026-10-28. Trigger: the first player feedback (Tythos and his community on Bilibili, Discord 2026-10-27/28),
a session report of a freeze, and Umut's own stream (2026-10-27, r2modman profile "Release test", 0.15.0 release
build). Running log: every step with what, how, commit, open bits (see the project's stepwise-docs rule).

## Umut's list (2026-10-28)
1. A freeze: Tythos: "the ID input screen at the end sometimes freezes, so their IDs can't be recorded" + a session
   report (2026-10-27_16-08-36.txt, Windows 11 26200, zh-Hans, i5-14600KF, RTX 5060 Ti, 2560x1440 full screen).
2. Quick restart of the current room for save-scumming in normal runs (Tythos's viewers); in a daily run it counts as
   leaving the game (only full runs without save scumming / unbalanced mods count).
3. Rework the daily points: the game counts some boss kills twice (overkill), several parts are the same for everyone
   (Tythos: Victory, Stage, Boss, Kills; DocRun = Umut agreed on Kills and Stage reached); ideas: run time weighed
   more, flawless boss defeats / rooms (define flawless vs. a shield blocking), bones left, a streak bonus up to 30
   days (Tythos: not used in their competitions).
4. Hover bug: in Umut's r2modman game the mouse no longer selects items / skulls / dark abilities by hovering, only a
   click does (and a click buys a dark ability at once).
5. Brainstorm: the settings page with features switched on the left and their settings on the right; players want
   the analysis features apart from the gameplay changes (0.15's rewards).
6. (Added during the session) Umut's stream: the game lags a lot with some builds; is it the mod (0.15)? Log / monitor
   slow frames and find out why.

## Steps

1. **Findings (2026-10-28)**.
   - (4) Hover bug, cause found: the game's UI selects a slot on mouse hover only in its own input module (InControl's
     `InControlInputModule.ProcessMove`, `focusOnMouseHover`, set by `UIManager.allowMouseInput`). Unity runs only the
     first enabled EventSystem (`EventSystem.current` = `m_EventSystems[0]`). The mod's windows (Codex, combat log, Mods,
     What's new) made their own EventSystem + StandaloneInputModule (DontDestroyOnLoad) whenever none was current; once
     the game's was enabled again after a scene load it came second, ours ran: clicks worked, hovering selected nothing.
   - (1) Freeze: the report's 30 s summary snapshot never came after the start (16:08:36), so the game's main thread
     stopped (or the game was killed) within ~20 s of the castle loading in the daily slot (`GoHome` = the castle's
     fade-in; the Codex had just been built: it opens itself there for the name entry). No error, no loop found in our
     daily code. Suspects: Windows 11's Microsoft Pinyin input method (known to hang some full-screen games while typing;
     Chinese names need it), or something in the first draw of the Daily page. Not provable from the report: the freeze
     watch (step 3) will say next time. The "Releasing render texture" error is the game's own (also in Umut's session).
   - (6) Lag: since 0.15 every kill wrote the whole rewards.json (RewardStore.CountKill → Save: serialize + write +
     delete + rename on the game's thread); builds that kill a lot did that many times a second. Other suspects (not
     measured yet): damage numbers live 1.85 s instead of the game's 0.5 s (more texts animating), Clear view walks every
     player effect ever pooled twice a frame and allocates an array per projectile. Umut's stream report (3 h 15 min,
     r2modman "Release test"): 1,240 game errors (63 distinct), 1 HOOKED, 0 of ours; no performance data existed.
2. **Hover fix + rewards save fix**. `UI/ModEventSystem.cs`: ours is made only when no EventSystem exists and is
   destroyed (and the game's made current) as soon as another one is enabled (checked twice a second, Plugin.Update);
   the four creation sites call it. RewardStore: kills (quest steps, adventurer kills) are saved 3 s later
   (`ChangedSoon`, `Tick`, `Flush`); coins, summons, activations, a finished quest step still save at once; the slot
   switch and quitting flush first. Test Store_KillsAreSavedAFewSecondsLater_NotEachOne.
3. **Performance monitor + freeze watch** (`Crash/Perf.cs`, `Crash/PerfStats.cs`, `Crash/PerfMonitor.cs`).
   Every `Guard.Run/Text` and the busy hooks that use their own try/catch (damage numbers + their animation, combat log
   hits, damage trace, Clear view projectiles / effects, Codex attacks / projectiles, cooldown numbers, attack tracking,
   heal orbs, rewards gear) are measured scopes. Session report: a line each minute (fps, slowest 1 %, stutters >= 100 ms
   apart from map changes, SkulInsight's ms a frame and share, its top 4 parts, garbage collections, Mono memory, damage
   numbers per second at most), a line per stutter (at most one in 10 s: the mod's part, parts over 1 ms, a GC),
   a Performance section in the summary (session fps, stutters, slowest minute, the mod's 15 most expensive parts).
   Freeze watch: a background thread; no frame for 6 s → INCIDENT "the game froze" with what ran (SkulInsight's part
   or none, the frame phase: updates / drawing / between frames), what was open, a focused text box, an IME composing,
   the screen mode; its own verdict (LIKELY SKULINSIGHT if our code ran, else NOT SKULINSIGHT'S CODE + where); resolved
   with the time it stood still; a freeze the game never leaves is the next start's verdict. Settings General/
   PerformanceLog (on) and General/PerformanceOverlay (off: "60 fps · SkulInsight 0.40 ms" top left). 7 tests.
   Texts (5) still to translate.
4. **Restart room** (`SaveSlots/RoomRestart.cs`, `Patches/RoomRestartPatches.cs`): snapshot of the game's save data at
   LevelManager.InvokeOnMapChanged (right after the game writes its room save) + the rewards store; pause menu button
   "Restart room" under "New game" (game's confirm dialog; only shown during a run, MenuFit refits the column), key F5
   pressed twice within 3 s. Restart = SlotSwitcher.ReturnToTitle(roomSave) + skip "press any key" (Main.StartGame;
   LogoAnimationsCompleted skipped while continuing). [DECISION] rewards go back to the room's start too (no quest
   farming). Daily run: first press warns, restart ends the run like leaving the game (RefuseContinue). Run history:
   event "restart" + number roomRestarts. Settings section "Restart Room" (Enabled, Key). NOT TESTED IN GAME.
5. **Daily points formula 3** (server `points.py`/`main.py` + mod), cutover 2026-11-01 (month start). Mod sends
   fightRooms, flawlessRooms, bonesLeft, roomRestarts, bossRooms (per-map health lost/hits/kills now in run files);
   a boss counts whatever dealt the last hit. Server: win 20,000, speed 5/s under 1 h, progress 1,000/stage for
   non-wins, 1,000 per main boss fight (a win = all 5), 3,000 per optional boss (Dark Skul, Alexander), 150 per
   flawless room, 1,000 per flawless boss fight, -2 per damage taken, gold/10, bones x2; no day records on F3 days;
   streak only on the month board (100/day up to 30 x factor). 36 server tests pass. Simulation on real boards
   (scratchpad simulate_f3.py): 10-28 Tythos #1 (lower damage taken), boss-count noise gone.
   **NOT PUSHED to skul-daily (waits for Umut's OK + the 0.16 release before 2026-11-01).**

## Open when resuming (usage ran out 2026-10-28)
- Mod tests: 3 fail: Formula_3_numbers_flawless_rooms_and_the_boss_rooms (debug it), and the two Localization
  template tests (new texts: run tests with UPDATE_LANG_TEMPLATE=1, then translate all new texts into 10 languages via
  tools/lang/lang.py merge).
- Item 5 (settings rework + analysis/gameplay split): brainstorm not written yet.
- Then: deploy Debug build, Umut tests (hover, restart room, perf overlay/report), version 0.16.0, release, then
  `git subtree push --prefix server/daily skul-daily main` BEFORE 2026-11-01 (server is backward compatible:
  0.15 clients still work; F3 only applies from Nov 1).
- Ask Tythos: the 15:16 report of 2026-10-27, whether the whole game freezes, and if they type names with Microsoft Pinyin.

## 2026-10-30 (resumed)
6. [Umut] "I think it has to do with the pinyin" (the name-entry freeze). Tythos (Discord 10-30): 16 boss kills and
   1,120 kills in a speedrun (multi-source killing blows count several times); tournament next Saturday. New asks:
   achievements as bonus points (a daily random subset, all achievable in one run, only logical ones, not dominating),
   custom achievements ("play as <random skull>"), yesterday's winners as the adventurers' names.
7. Fixed the failing formula-3 test (test data order + run id). **Day's achievements** (server `points.py`
   ACHIEVEMENT_POOL / CUSTOM, `days.day_achievements` / `day_pick`, `/api/dailies` boards[].achievements): 2 of the
   game's + skull of the day + flawless chapter; mod records the game's achievements in the daily slot
   (DailyRun.OnAchievement from the daily slot's SetAchievement prefix), sends achievements / heldSkull /
   flawlessChapters; Daily page "Today's achievements" with "done" marks, the toast, Points page part.
   **Winners as adventurers** (`/api/dailies` champions, cached 5 min; Patches/WinnerNamesPatch on
   AdventurerNamePool.OnEnable; setting Daily/WinnerNames; system font fallback for CJK names). **Pinyin
   workaround**: config Daily/Name (hidden in-game, typed in the mod manager), the name box starts with it, hint on
   the Daily page after a run. 40 server tests.
8. 51 texts in 10 languages (font fixes: zh 帧/幀/暢/檔, ja 〜, pt-BR º), old 0.15 What's new lines pruned; version
   0.16.0, CHANGELOG, What's new (5 lines + "since 0.15"), README, wiki, DAILY_RUN, docs/SETTINGS_V2.md (item 5
   brainstorm). 570 tests. Debug build deployed.
   Server push (`git subtree push --prefix server/daily skul-daily main`) blocked by auto mode: waits for Umut's OK.
   It is backward compatible (formula 2 until 2026-11-01; new fields ignored by 0.15).
9. [Umut 2026-10-30, screenshot of the pause menu] "Remove the Restart room menu point. It kind of just works with
   F5, just make sure to name it somewhere" → menu entry gone (FitGrid back), F5 named in the setting's help, What's
   new, README, wiki, CHANGELOG, the welcome. [Umut] split option 1 → first-start welcome in the What's new window
   for new players (LastVersionSeen empty): "Only information" (default, Esc: Rewards and Restart room off) /
   "Information and gameplay". [Umut] settings: "left side shows the toggles for the settings, and when hovering, it
   selects them and the right side shows the settings / description for that one" → SettingsCatalog.Features (groups
   Gameplay / Insight / Helpers / Online / General; main switch per feature: Enabled, or ShowNumbers / ShowSourceTag /
   ShowDamageNumbers; Damage Icons inside Damage Numbers; Codex balance one feature), SettingsPage: left rows = the
   features (the game's On/Off row, or a plain row), right = title, description, the feature's settings in their own
   list (sub-headings), the selected setting's help + default below; Enter into a feature, Esc back. 22 texts in 10
   languages (font fixes 焕/煥/檔, ko 끕/룹). 571 tests. Debug build deployed. NOT TESTED IN GAME (the page layout
   needs Umut's eyes: positions are estimates in the game's 1920x1080 pause canvas).
10. [Umut 2026-10-30, screenshots] "It looks weird" → rich text on the help (colour tags showed), features without a
    switch: see-through button, "8 settings" as value, name lights up; right list holds 6 whole rows + scrollbar.
    [Umut] "really figure out, what is important for the normal user ... thin it out by a lot ... another setting,
    that 'fans out' a more in depth version" → SettingsCatalog.Basic whitelist (~35 of ~95 rows: every feature's
    switch + keys, rewards' parts, icons, pickup preview, Codex notice/sound, Clear view opacity, orbs, language, Show
    FPS, crash folder, save slot + actions, Reset), everything else `Advanced`; new setting "All settings/Show" (off)
    at the end of General rebuilds the page with everything (stays on that row). A new setting starts advanced.
    Codex unlocks, Developer, Save states only with All settings. 572 tests. Debug build deployed.

## 2026-10-31 (release)
11. [Umut] "give me a whole run down on this new version that we will push now (it is also your go)". 572 mod tests
    + 40 server tests green. **Server pushed** (`git subtree push --prefix server/daily skul-daily main`,
    d504783..d8ed151, 00:06 UTC; the server deploys it at :07/:37 after its own pytest). Thunderstore zip dry run OK
    (no self-update, no installer). Config: idleCompaction off in ~/.claude/settings.json (Umut).
    Next: Umut posts the reviewer ping, then `release/release.ps1` (GitHub + Thunderstore).
