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
12. [Umut] "Posted" (reviewer ping) → `release/release.ps1`: **0.16.0 released 2026-10-31 00:24 UTC** (GitHub release
    v0.16.0 with the zip, public repo e7a7e39 + tag, Thunderstore DocRun/SkulInsight_QoL 0.16.0). Wiki text on Umut's
    clipboard to paste. Formula 3 starts by itself at 2026-11-01 00:00 UTC.
13. [Umut 2026-10-31 ~13:00 UTC] "whether or not the new point system is already active for today ... We need the
    feedback early today from the people, so they can do their tournament" → it wasn't (formula 3 from 11-01). Moved
    FORMULA3_FROM to 2026-10-31; `main.rescore_formula_3_days()` at server start counts runs of formula-3 days that
    were counted with formula 2 again (kept numbers + run history: boss rooms with health, held skull, flawless
    chapters; `points.numbers_from_run`), once. Today's dm10+dlc, before achievements: Tythos 93,520 → 133,912,
    Skul (win) 96,820 → 87,778, amun 35,340 → 47,488, Skul (0.15 death) 45,282 → 39,180; ranks unchanged.
    October's month board gets one formula-3 day. Mods pick the day's achievements up within 10 min. 42 server tests.
    [Umut] "Mach das. Save todays entries" → the old points + breakdown of every re-scored run go to table `rescored`
    (also in /api/admin/runs); public boards + run details of the day as formula 2 copied to
    C:\Users\Umut\SkulModding\backups\daily-2026-10-31-formula2. Server pushed 13:07 UTC (d8ed151..7e1c255).
    Live 13:15 UTC (the 13:07 check). dm10+dlc re-scored with the day's achievements (WorldsBest, Leiana no-hit,
    skull of the day Thor, flawless chapter 1): Tythos 136,412, Skul (win, held Thor) 91,528, Skul (0.15 death)
    39,180, amun 49,988. Umut got a prompt for the server session: deploy check every 2 min, flock, deploy.log.
    Correction: live through the server session's manual deploy at 13:14 UTC (7e1c255 + 83c0f65; DB copy first:
    ~/backups/skul-daily/pre-update-2026-10-31-7e1c255.sqlite). Server now checks every 2 min (~/ops/skul-daily-deploy.sh,
    ~/ops/crontab.txt, ~/projects/skul-daily/deploy.log; server-infra b82b617). rescored: 5 runs (incl. the modded
    normal+dlc run, 0 → 0).
14. [Tythos 2026-10-31 14:55, Discord] "After pressing [F5], I get reset to the starting character with all my gear
    gone. I have to fully close and reopen the game". Umut's own session report of 10-30 22:48 had it too:
    NullReferenceException in GameResourceLoader.TakeWeapon1 ← Inventory.LoadSavedWeaponFromPreloader. Cause: the
    game preloads the saved run's gear once at boot (GameResourceLoader.PreloadSavedGear) and Inventory.LoadFromSave
    takes it; SlotSwitcher.ReturnToTitle didn't preload again. Fix: the steps of the game's own
    PlatformUserChangedHandler.OnUserChanged incl. PreloadSavedGear (+ DestroyPlayer in case Reset stopped half
    way); also fixes a slot switch into a slot with a run in progress. 0.16.1 (CHANGELOG; no new texts). 572 tests.
    Debug build waits for the game to close (DLL locked). Workaround for 0.16.0 players: close the game right away,
    before entering another room (the next room's save would keep the gearless state).
15. [Umut 2026-11-01, Discord with Tythos] new scoring iteration. [Umut] gold/bones "Rates without a cap", "Today,
    re-score today's runs". Server formula 4 (from 11-01): 1 point per 5 gold, 25 per bone; `main.rescore_rates()`
    re-counts runs of formula-4 days whose gold/bone parts have the old rates (old points kept in `rescored`). Today's
    dm10+dlc: amun 122,152 → 122,155, YuRuwen 103,158 → 104,042, 1212 89,772 → 91,172, Skul 56,570 → 61,098, SUO
    46,560 → 49,048, DocRun 45,545 → 47,868; no place changes. [Tythos] challenges overlap with the points + should be
    in-run conditions, not skill checks → from 11-02: 1 game achievement from SkeletonKing / WhattoBuy /
    BestCondition (DM), skull of the day, quintessence of the day (22 common/rare, win holding it; held essence from
    the run JSON at finish, every mod sends it). /api/board, /api/runs/detail, /api/dailies send "rules"; mod 0.16.1
    fills "How points are counted" from them + "Quintessence of the day" line + hint without the no-hit clause (4
    texts in 10 languages). Design notes: DAILY_RUN.md "Formula 4 ...". 45 server tests, 573 mod tests.
16. [Tythos/Umut 2026-11-01] in-game changelog before 0.16.1; [Umut] "should include ALL changelogs from previous
    versions", What's new after every update, "The Current changelogs ... boxed in and highlighted". UI/Changelog.cs:
    all 18 versions (0.9.0-0.16.1) as translated highlights (22 lines of 0.12-0.15 back from git with their
    translations, 18 new texts x 10 languages; the detailed English CHANGELOG stays on Thunderstore / r2modman).
    What's new (UI/WhatsNew.cs) = that list: what is new since LastVersionSeen in a box (Changelog.Split), earlier
    versions below, scroll (wheel, arrows, PgUp/PgDn, Home/End; the event system's navigation paused); shows after
    every version change (hotfixes too). Settings General → Changelog (Basic; Esc closes the window first:
    SettingsPage.HandleBack). Test: the newest entry is the csproj version (a new version needs its entry).
    576 mod tests. Font fixes zh 览/覽, ko 켤.
    [Umut] Next: other players' runs show in the sender's language (names stored as text) → re-localize from ids.
17. 2026-11-02 [Tythos, Discord] "If you've edited your ID [name] once before, you can't change it again ... neither
    type new text nor paste" → the Daily page is drawn every second and refilled the name box whenever it was empty
    (an emptied box, or Pinyin composing); now filled once when the box appears (CodexWindow.Daily). [Umut] runs in
    the viewer's language, inside 0.16.1: Lang/AnyLanguage translates stored names through the game's string table
    (LocalizationStringResource holds every language's column, Loc.GameOrder) and the mod's own language files, "A: B"
    and "A · B" part by part; used for skills, damage table rows, damage taken, stage names, killer, Ref fallbacks and
    the board lines. Old runs too, no recording change. 2 changelog lines in 10 languages (font: ja 欄, ko 칸
    replaced). 577 tests. Formula 4 has been live since 2026-11-01 13:58 UTC; the second set of challenges is live
    today (dm10+dlc: SkeletonKing, skull Wizard, essence Cyclops).
18. [Umut] "All work perfectly, do the push" + "i posted the review ping". **0.16.1 released 2026-11-02**: GitHub
    v0.16.1 (release.ps1 -SkipThunderstore, then tcli publish once the ping was confirmed), Thunderstore 0.16.1.
19. [Umut 2026-11-02] three "Skul" on today's DM10+DLC board: one player submitting several times, or the default?
    → three players: three player ids (entry ids are per player), two of the runs overlapped in time (15:23-16:15 and
    15:27-16:18 on their clocks), mods 0.16.0 / 0.16.0 / 0.15.0, streaks 2 / 1 / 1, all three games in Chinese. "Skul"
    is the mod's name for an empty box (and the server's for an unusable name). [Umut] "fill in the name the game
    recognizes at the bottom right" → 0.16.2: DailyRun.StartingName(config Daily/Name, last name, platform name
    = PlatformManager.platform.userName, Steam's persona name); "Skul" counts as none; 12 characters. Changelog entry
    0.16.2 (10 languages). 578 tests.
