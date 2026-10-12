# Night log 2026-10-11 (unattended work session: the QA night capture)
Running log, updated after every step, so an auto-compaction or a new session loses nothing.
Decisions Claude made for Umut are marked **[DECISION]**: Umut may change any of them.
The previous night's log (2026-10-05, 0.11) is in git history (`git show 662e045:docs/NIGHT_LOG.md`).

## Setup (read first when resuming)
- Plan (Umut's words, 21:52): "Take the left one and make sure that bepinex command or steam dont take focus either.
  You can actually start the FULL night shift now." Goal given to the skill: QA capture all 11 languages via
  tools/qa/night-capture.ps1 (game on the left monitor, no focus stealing), analyse with tools/qa/captures.py +
  contact sheets, findings into docs/QA_FINDINGS.md, fix clear findings in batches (tests pass, no deploy while the
  game runs); max Session 90 %; no release/push; no PC shutdown (Umut didn't ask for one).
- Context: Umut is awake and playing League of Legends on the main screen (the usage screenshot shows it), so the
  usage panel can't be read; the capture runs on the left monitor without taking the focus. QA background:
  docs/QA_LOG.md (steps 1-8), docs/QA_CAPTURE_PLAN.md, docs/QA_FINDINGS.md.
- Watcher: night-check.ps1 (skill night-shift); state in C:\Users\Umut\ClaudeNight.
  **[DECISION]** watcher restarted with -WatchdogMinutes 720 (effectively no watchdog shutdown): Umut is at the PC
  and asked for no shutdown; a stuck Claude must not switch his PC off mid-game.
- Stop rule [DECISION]: Session ~90 % (max 90) or Weekly >= 85 %. The panel isn't visible while Umut plays, so pace
  by cost: few, long waits (one background wait per check), analysis in big steps.
- Usage readings: 21:53 panel not visible (League of Legends on the main screen); 21:56 S 61 % / W 69 %
  (watcher on the left monitor from then on); 22:11 S 65 % / W 70 %.
- Capture run: `night_2026-10-11_2154` (started 21:54, background task, log SkulModding/QA/night-full.log; game window
  at -1920,0 1920x1080 = the left monitor). Order en, de, ru, ko, zh-Hans, ja, zh-Hant, fr, es, pt-BR, pl; ~40 min
  per language. A stopped run continues with `night-capture.ps1 -Languages all -Run night_2026-10-11_2154`.
- Branches: code and docs on main, merged into save-states (Umut's game branch). No deploy while Skul runs.

## Queue
1. Watch the capture; analyse each language when it is done (captures.py report + contact sheets; Read the sheets).
2. While it runs (no deploy, tests only): QA findings N3 (Balrog / Thunderlord / Warrior: the same hit in every step:
   real or a link?), N4 (Abyss Meteor 4000 %), T3 (part numbers that skip: number only parts with damage).
3. Findings from the capture: write them into QA_FINDINGS.md, fix the clear ones (build after the capture ends).
4. If budget is left: swap-menu samples for the capture tool; spec for phase 2 (Codex pages capture).

## Steps (newest last)
1. 21:54 Night started: watcher (12 h watchdog), full capture started (run night_2026-10-11_2154, left monitor
   confirmed by the window position), this log. Before it (21:50, f271b21 and earlier): night-capture.ps1 switches
   BepInEx's console window off for the run (it took the focus), logs the window position.
2. 22:00 Umut: the usage panel is on the left monitor (VS Code over the background game). Watcher: screen.txt =
   "left" makes usage-watch.ps1 photograph the left monitor's top-left (skill updated). Reading 21:56: Session 61 %
   (reset ~22:19), Weekly 69 % (resets in 5 days): Weekly is the limit tonight (stop at 85 %).
   QA N3 checked: real data, not link bugs (QA_FINDINGS). QA T3 fixed: part numbers without gaps (GearAnalyzer;
   corpus 42 -> 0); not deployed yet (the capture runs), tests pass (349). Dev dump prints each hit's owner path.
   Watcher: night-check restarted a default watcher (45 min watchdog) next to the manual one -> watchdog.txt (720)
   is read by every watcher start (skill updated); one watcher since 22:00.
3. 22:24 English of the first pass analysed (run night_2026-10-11_2154, ~2050 pictures): no text outside its panel,
   no real overlap, no missing glyphs. The only flags were the ground popup's covered one-skill box behind the
   two-skill boxes (checked by eye on the flagged sheet: clean) -> captures.py ignores that pair.
   But the sample sheet showed two items left in the inventory for the rest of the run (Lake in a Bottle and Undine
   Water Spirit turn into another item when picked up; the new item was never discarded): their inscriptions and
   stats changed every later number (skull skills ~2.2x). And the inventory crops were full-screen (a full-screen
   overlay image counted as the panel: ~7.6 GB per night, "outside" blind in the inventory).
   -> First pass stopped at 22:27 (English + a little German; kept on disk, 695 MB, for comparison **[DECISION]**:
   deleted only after Umut's look). Tool fixes (round 6, deployed): transformed items found and discarded,
   inventory emptied after every gear, lasting stat changes logged ("[QA] ... stats changed for good"), overlays
   ignored, skulls first and items/quintessences holding Little Bone. Check run 22:29 (6 gear): crops 40-280 KB,
   both transforming items captured, nothing left behind.
4. 22:31 Second (full) pass started: run name in SkulModding/QA/night-full2.log, all 11 languages, ~37 min each.
   Known: Icarus's Dagger's ground popup fails inside the game's popup code ("key not present"), its inventory
   pictures work (finding, not ours to fix unless it is our description code).
   Send-to-back (22:35): the started game covered VS Code on the left monitor (usage panel hidden): the Skul window is
   sent behind every other window without activating it; pictures keep coming and are fresh (checked); built into
   night-capture.ps1 for later runs (1bbcf11). Reading 22:33: S 5 % (new window) / W 71 %.
   Icarus's Dagger checked: a cut item (not obtainable, no name, inscriptions "None"): the game's popup fails on it.
5. 23:05 English of pass 2 analysed: 2092 pictures, 0 outside, 0 overlap, 0 missing glyphs (422 MB, ~4.6 GB for
   all languages). Failures: Dracula, Skul_Tutorial, Icarus's Dagger (G1). The stat log caught the Olive Tree Cudgel
   (physical 1 -> 2 for good, G2): items after it in every language show doubled physical numbers (layout unaffected)
   **[DECISION]** keep the run going (layout is what the capture checks; neutral numbers come from the corpus); the
   tool takes the cudgel last from the next run on (not deployed while the game runs). Brawl's "2" checked: real.
   Umut (23:00) asked for a changelog of the upcoming version when all work is done -> wrap-up: the 0.12.0 section of
   CHANGELOG.md rewritten as player release notes, text in the morning summary.
6. 23:15 Changelog 0.12.0 rewritten as player release notes (3ef468e). Umut (23:12): "What about save games" ->
   asked: save slots ship in **0.12.0** (his answer). save-states merged into main (1b0fb09, clean, 355 tests pass);
   changelog (highlight + Save slots section), README (intro, version line, hold Shift / compact / Codex in the
   description section, new Save slots section, compatibility line about saves, roadmap), wiki (intro, Save slots
   section, FAQ about the save, roadmap) and the store description (228 chars, "hold Shift", "save slots") updated.
   Work continues on main only. Morning: README screenshots still show the old long descriptions (new ones with the
   compact lines and a Save slots picture would help; Umut's call).
7. 23:59 German and Russian analysed (6218 pictures in the run): 3 flags. L7 fixed (German Arson step text ran into
   step 2: compact boxes drop status calculations, inscription parts one line each; StatusDescriptions.Details,
   DescriptionLayout.InscriptionParts, ForInscription sets compact for game boxes); corpus de checked; 355 tests
   pass; changelog line. G3 (Russian squad labels, the game's own text) noted. Not deployed (capture running).
   Paths for Umut (asked): changelog, README, wiki, findings, screenshots folder, save slots folder.
8. 00:50 Korean and Chinese (Simplified) analysed (10298 pictures in the run): no flag, no missing glyph; a ko/zh
   sample sheet checked by eye (glyphs render, mod lines inside the panels; Korean breaks words at line ends like the
   game's own Korean texts). Reading 23:59: S 14 % / W 72 %. Remaining: ja, zh-Hant, fr, es, pt-BR, pl (~2.5 h),
   analysed together at the end; then deploy (L7, T3, tool fixes) and a short German/Russian check run.
9. 01:50 Umut asked: were all 14,800 pictures analysed? Answer: every picture by its JSON facts (outside, overlap,
   glyphs), ~80 by eye (flagged + samples); not every picture by eye (millions of tokens). Added a readability check
   to captures.py (text shrunk below 90 % of the box's normal size, over all JSONs): finding R1 (Umut decides).
   Umut asked for: (a) PC shutdown when all is done -> last wrap-up step runs shutdown-pc.ps1 **[DECISION]**: plus
   the watchdog back on at 75 min (watchdog.txt) as the stuck-Claude safety; (b) keep the 60 min prompt cache warm
   -> a background timer (sleep 44 min) wakes this session; every tick: check-in, progress, next timer.
10. 03:35 Full capture finished at 03:23: 22,517 pictures, 11 languages (3.4 GB), 44 failures = the 3 special/cut gear
    x languages (G1), focus never taken after the start. Last six languages: the Arson step (L7) again in es, fr, pl,
    pt-BR; the game's squad labels (G3) in es, pt-BR, pl; French flavor texts over the rarity label (G4, game). Deployed
    tonight's fixes (L7, T3, tool) and ran a check (de, es, fr, ru: Burning Scripture, Manatech Cogwheel, Guan Yu 3,
    Davy Jones): the Arson step fits in all four (checked by eye in German). Spanish "3ª" is the game's text (G5).
    Readings: 02:31 tick; 03:15 S 29 % / W 74 %.

## End of the night (03:40, Session ~30 % of a fresh window, Weekly ~74 %)
Last code commit before this log: see git log (QA findings and wrap-up commit). Deployed: main (Debug, 03:29). Tests:
355 pass. The PC shuts down at the end (Umut asked at 01:45; 5 minute warning, `shutdown /a` cancels).

## Decisions made tonight (Umut: change any of them)
1. Watchdog: 720 min while Umut was at the PC (no surprise shutdown), back to 75 min when he asked for the shutdown;
   watchdog.txt and screen.txt (usage panel on the left monitor) are read by every watcher start.
2. The first capture pass was stopped after English (items left in the inventory, full-screen crops) and redone;
   its folder (night_2026-10-11_2154, 695 MB) is kept until Umut has looked.
3. The second pass kept running although the Olive Tree Cudgel doubles physical attack for good (G2): the capture
   checks layout; neutral numbers come from the offline corpus. The tool takes the cudgel last from now on.
4. Captures show the numbers (as if Shift were held) and the detail views; skulls first, items and quintessences
   holding Little Bone; a full-screen picture per gear only in English.
5. Compact boxes also drop the calculation from status lines and write inscription parts one per line (L7): the
   same rule as Umut's decision for the skull texts; the Codex keeps everything.
6. captures.py ignores the ground popup's covered one-skill box (it measured as overlapping, checked by eye).
(Save slots in 0.12.0 was Umut's decision.)

## Morning todos for Umut
1. Play a run and **hold Shift** on skulls, skills, items and inscriptions: are the compact lines easy to read? Decide
   R1: some boxes shrink their font while Shift is held (German swap texts down to 57 %): accept, or shorter lines?
2. **Olive Tree Cudgel (G2):** pick it up in a normal run and drop/destroy it: does physical attack stay doubled? Then
   it is the game's own behaviour (or a game bug worth knowing).
3. Read the **0.12.0 changelog** (CHANGELOG.md, top): good as the release notes? Then the 0.12 test (docs/V012_LOG.md
   test todos, now also hold Shift and save slots) and your go for the release (`release/release.ps1` from main).
4. Optional: new README screenshots (compact lines, the Save slots section).
5. Optional: the night's pictures are in `<Skul>/BepInEx/DamageInsight/QA/night_2026-10-11_2231` (3.4 GB, report.md
   and sheets/); say when the first pass (695 MB) and the test runs can go.
