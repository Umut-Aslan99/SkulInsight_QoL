# 0.13 step log (run history, records, achievements)

Running log: every step with its commit, every **[DECISION]** Claude made alone, Umut's test todos. Plan:
docs/V013_SPEC.md. Updated after every step (survives a context compaction).

## Step 0: plan (2026-10-16)
- Umut's request: records + run history ("match history", end-screen style, damage split, build, timeline), achievements
  per save slot with a Steam-like trophy page (greyed until earned, hidden ones held 3-5 s to reveal); then rewards;
  Dark Mirror pages soon; Remembrances later; split into batches.
- Research (decompiled): the game's run counters reset at every run start (only deaths, housing, Dark Mirror levels
  and best stage times are permanent); the end screen's data; 103 achievements in `Platforms.Achievement.Type`
  (Platforms.Common.dll, decompiled today to `C:/Users/Umut/SkulModding/Decompiled/Platforms*`); the game never
  checks whether one is earned (a hook on `PlatformManager.SetAchievement` sees every earn again); Steamworks gives
  names, descriptions, hidden flags, icons, unlock times, global percentages.
- Written: docs/V013_SPEC.md (facts, features A-D, 6 batches, outlines for 0.14 Dark Mirror and 0.15 Rewards).
- [DECISION] versions: 0.13.0 = history + records + achievements; 0.14.0 = Dark Mirror tab; 0.15.0 = Rewards;
  knowledge pages, Remembrances and Story later.
- [DECISION] the You section gets sub-tabs Build / Records / Runs / Achievements.
- [DECISION] hidden achievements: hold 4 s; once revealed they stay revealed in that slot.
- Open for Umut: achievements in slots made before 0.13 (spec C: empty / proven by the save / take over Steam's).

## Step 1: batch 1, the run journal (2026-10-16, commit 6d33399)
- New `DamageInsight/History/`: `RunRecord` (one run: result, end screen numbers, build, damage rows, damage taken
  per attacker, maps, events, time per skull; own JSON writer/reader), `BuildDiff` (two build snapshots -> timeline
  events; `RunResume.Decide`), `RunJournal` (Unity side), `Patches/RunHistoryPatches.cs` (end screen: `GameResult.Show`
  = death, `ShowEndingResult` = ending; `ItemInventory` / `QuintessenceInventory.Change(At)` = "turned into").
- Files: `Codex/Runs/<start>.json` (+ `<start>.jpg`, the death cam picture) and `Codex/Runs/open.txt` (the run in
  progress: id + seed). Per save slot (the Codex folder moves with the slot); a Codex reset keeps them (it only
  deletes Replays, Animations, Hints, Debug, progress.json).
- How it works: a run starts at the first moment in Chapter 1 (or Dark Mirror chapter 1) with a player; the game's
  run seed (`GameData.Save.randomSeed`, drawn at castle entry) is the run's identity: continuing after a quit finds
  open.txt with the same seed and goes on; a different seed marks the old run abandoned. The build is compared every
  0.5 s (catches every way the game changes it); the first comparison waits 1.5 s (a continued run's inventory loads
  over a few frames) and a continued run starts from its saved build. Ends: the end screen (death: numbers, killer =
  last hit on the player within 15 s, death cam picture; ending: normal / darkmirror / emperor from the game's own
  `emperorCleared`), or back in the castle without an end screen (abandoned).
- [DECISION] the run's time starts when the run leaves the castle (the game's own play time also counts the castle
  before it; the end screen's value is kept as `playTime`).
- [DECISION] a run found halfway without a file (started before 0.13 or with the setting off) is recorded from there
  on and marked `joinedLate` (no start events).
- [DECISION] swaps are counted (not listed as events: a run has hundreds); skulls taken, dropped and awakened are.
- [DECISION] inscription steps: an event only when a step is higher than any reached before in this run (no noise
  when items move around).
- Setting General/RunHistory (on) + SettingsCatalog row, texts in 10 languages. 16 new tests, all 401 pass; Release
  build clean; Debug build deployed 2026-10-16 ~10:00.

## Step 2: daily run idea captured (2026-10-16)
- Umut's message: a daily seed from his server, daily and monthly leaderboards, one try, a set difficulty. Written
  to **docs/DAILY_RUN.md** with facts from the code (48 systems use the run seed; the gear pool depends on each
  save's unlocks and DLC), a design proposal (own "Daily" slot with a standard profile, Steam achievements off in it)
  and Umut's open decisions.

## Step 3: Umut's reference for the Runs pages (2026-10-16)
- Two League of Legends match history screenshots: the list of the last 20 games and a game's page with tabs. Written
  into V013_SPEC A ("Pages"): list rows (result + kind, boss / killer portrait with our crown / bones badge, skulls,
  item strip, numbers, stage, time, date), run page tabs Overview / Damage / Timeline / Graphs, our own icon set.
- [DECISION] the list shows the last 20 runs, older ones behind "Show more"; every run stays on disk.

## Step 4: Umut's decisions (2026-10-16)
- [Umut] daily run = **0.14.0** right after 0.13; Dark Mirror tab 0.15, rewards 0.16 (spec, DAILY_RUN.md, README
  roadmap updated).
- [Umut] daily run in its **own "Daily" save slot** with a standard profile.
- [Umut] achievements in slots made before 0.13: what the save proves + a one-time Steam import for the original
  game's slot.

## Step 5: favourites (Umut 2026-10-16)
- [Umut] a favourite button; old runs go away to save space unless starred.
- [DECISION] old runs shrink (list row + numbers stay, ~1 KB) instead of being deleted, after the newest 100
  (setting General/RunHistoryKeep); records count every run; an index file for a fast list. Built in batch 2.

## Step 6: rookie mode, batch 2 (Runs pages), first live data (2026-10-16)
- Umut: keep the difficulty per run (normal, rookie mode, Dark Mirror 0-10). Rookie mode = `GameData.Settings.easyMode`
  (normal mode only; Dark Mirror switches it off; can be turned on mid-run): kept as "on at some point" (a88dc46).
- **Batch 2 built (877d94f)**: You tabs Build | Runs (`CodexCategory.YouBuild/Runs`, `CodexNavigation.YouTabs`, You
  build page 40 px lower, inscription rows 31 px); `Codex/CodexWindow.Runs.cs` (list rows, filters All / Victories /
  Starred, Show more, run page with tabs Overview / Damage / Timeline, star chip, death picture in the frame);
  `History/RunPages.cs` (texts, plain logic); `History/RunIndex.cs` (index.json in step with the files, shrinking);
  `UI/PixelIcons.cs` (crown, bones, flag, hourglass, star + outline, heart, door: 11x11 pixel art in code);
  `PageBlocks` icon rows (clickable), bars, timeline lines; `UiKit.OnHover` / `HoverLight`.
- [DECISION] pixel-art icons drawn in code instead of SVG files (Skul's look, no Unity vector package needed).
- [DECISION] the list row: portrait = the last boss (win) or the killer (death) from the Codex portraits, badge =
  crown / bones; with no portrait the result icon itself, big. The run page's frame shows the death cam picture.
- [DECISION] run times count from leaving the castle; dates as "2026-10-16 14:03" (the same in every language).
- 70 new texts in 10 languages; the glyph test caught 筛 览 覽 → 됨 (reworded). 417 tests pass, Release build clean.
- **Umut's live DM 10 run** (read while it ran, 10:14-10:27): 48 maps over chapters 1-3, skull times, bosses
  (Yggdrasil, the Leiana sisters), adventurers, dark abilities, inscription steps, damage rows, damage taken: all
  recorded. Fixed (44fad09): dark ability levels were one too high (the game counts from 1); bosses / adventurers /
  killers were stored with object names ("Scarecraw", "Dark"), now with their Codex names.
- Finding for batch 6 (not new): the combat log's damage table can't place some hits ("Item (unknown which)",
  "Mystery bright light spirit attack", "Skeleton spear 4 endless rush" under Other); the run's damage split shows
  the same rows. To fix with the damage table work (attribution of summons / inscription spirits / skill parts).
- Batch 2 is built but **not deployed** (Skul was running Umut's test); deploy after his batch 1 test.

## Step 7: batch 1 tested by Umut, the game's end screen for past runs (2026-10-16 ~11:00)
- **Umut's DM 10 win** (10:14-10:44, emperor ending on the Demon King Castle Top Floor) and **the quit / continue /
  die test** (10:45: "continuing the run of 10:45" in the log, one file, ended as a death to the thorn trap): every
  number equals the game's end screens (play time, deaths, kills, dark quartz, gold, bones, total / max damage, taken,
  healed). Batch 1 passed. The death picture was saved; the win had none (only deaths saved one).
- Facts: the game's "obtained" lines add the held gear to the met counts, and its essence line uses the item count
  by mistake (a game bug: Umut's death screen showed 1 essence for 0). The win's picture is rendered by the ending's
  cutscene through `CameraController.RenderEndingScene` -> `PlayerCameraController.RenderDeathCamera`.
- Umut (with his win's screenshot): the run page should look "along the lines of" the game's end screen, with the
  dark powers. **Built (7b69d37): "End screen" on a run page** opens a copy of the game's `UI.GameResult` filled with
  the run's data: panel art (normal / Dark Mirror / emperor top level), title, subtitle and stage in the current
  language (the run keeps the string keys, decided like the game does), the 13 numbers, the picture, the gear row
  and the dark abilities; slides in like the game's. The copy keeps only Unity / TMP components and the game's text
  localizers (the panel's logic pauses the game and waits for a key).
- [DECISION] obtained counts as the game counts them (met + held), but the essence line counts essences (not the
  game's bug).
- [DECISION] the picture is caught after every render of the game's death camera (`OnPhoto`), so wins keep theirs.
- Traps get readable names ("Thorn Trap"); runs from before batch 2 get their last boss from the timeline.
- 10 boss move names met in Umut's DM 10 run (Dark Skul, King Alexander, demon hunter) translated with "End screen".
  430 tests pass; Release build clean; deployed 2026-10-16 ~11:05.

## Step 8: Umut's first look at the Runs pages (2026-10-16/17, commit 303b660)
- Umut's screenshots and wishes: a win should say which bosses fell (First Hero, the optional Dark Skul, King
  Alexander), show the fragment (Rib of Horror) and the tuned Poisoning, and the end stats (attack, crit...); icons
  "small and weird" in run rows, the build, the Arsenal list and the Arsenal skull page; the end screen copy.
- Found: the run page title showed nothing (a colour tag at the start of a title shortened with "..." made TMP show no
  text; the colour now goes on the label); skull icons came from the reference's thumbnail (the small floor sprite):
  skulls now use the inventory's head icon everywhere; the end screen copy's icons were 3-4x too big (SetNativeSize
  uses the canvas's pixels per unit: the game canvas's scaler settings are copied); fragments have no Codex page
  (their icon and name now come from FragmentResource).
- Fact: the game's "Total Damage" adds every hit's full number (PlayerGameResultDataTracker: gaveDamage.amount, also
  beyond an enemy's last health); our damage split counts the health each hit took. [DECISION] keep ours (what each
  piece of gear really did) and say so under the Damage tab's title with the end screen's number.
- Built: boss portraits + "Bosses defeated" in the Overview (one per name: Dark Skul's and King Alexander's phases
  once), a win's last three bosses on its list row; fragment name; tuned inscriptions (`Inscription.isSuper`, kept
  in the build, a timeline line); "Stats at the end" (16 stats, the game's own names where it has them; the You
  page's formats); damage taken per attacker under its Codex name; code-named damage origins matched to their gear
  (`OriginMatch` + `CodexOrigins`, used by the combat log's damage table too, and for old runs at display time);
  maps passed in no time left out of the timeline; a win without a picture shows its last boss in the frame and in
  the end screen copy.
- 7 texts in 10 languages (one more boss move name from Umut's Codex; the Korean 셉 reworded). 439 tests pass;
  Release build clean; deployed 2026-10-17.

## Step 9: the bosses on a run page (2026-10-17)
- Umut's screenshots: "Bosses defeated" showed every boss phase under code names ("First hero1", "Alexander2 right",
  "Dark Crystal Left"), empty icon squares, a row running off the page. "We don't need all bosses."
- Found: (a) every boss phase has its own Codex page, and the game has no "enemy/name/<key>" text for them, so the
  Codex names them by their code names (the real names sit in each boss prefab's BossNameDisplay._nameKey; the
  string table is stored by hashes, so keys can't be listed); (b) creatures met only in the Dark Mirror (King
  Alexander, Dark Skul, First Dark Hero) keep their pictures as "<key>@DM", and the portrait lookup only tried the
  plain key; (c) icon rows didn't wrap; (d) the fragment's icon: `FragmentResource.Initialize` is empty, so its icon
  dictionary stays empty; the references list has the icons and names.
- [DECISION] a run page names only the **end bosses**: First Hero (the ending), First Dark Hero (the Dark Mirror's),
  Dark Skul (hidden, optional), King Alexander (the Demon King Castle); each once, pictured by its first phase. The
  list row of a win names them all.
- Built: `RunPages.EndBosses` / `EndBossOf`; portraits fall back to "<key>@DM" (the Bestiary's lists too); icon rows
  are a wrapping grid; `CodexCatalog.FragmentOf` / `FragmentIcon` (reference list, name match) for run pages and the
  end screen copy. `tools/unity/boss_names.py` scanned the game files for the bosses' name keys: 26 title cards;
  Yggdrasil `map/chapter1/bossname`, chapters 2-5 `label/healthbar/chapterN/name` (chapter 5 = the First Hero, also
  in the Dark Mirror), Dark Skul `label/healthbar/darkSkeleton/name`, King Alexander `label/healthbar/chapter6/name`
  (each with a `.../title` subtitle). `CodexCatalog.BossNameKeys` names the first phase's page by them (Bestiary
  too); other phases keep their code names for now (to look at with the Bestiary later). 439 tests pass.

## Step 10: one First Hero, the bosses' real names, the list rows (2026-10-17)
- Umut's screenshot: "First Hero" twice, "Skul" and "Alexander" (ambiguous), the win's row still a big crown, the
  crossed bones over the killer's portrait ("not sure I like it").
- Facts: in the Dark Mirror the First Hero fight is FirstHero1 then FirstDarkHero (his dark form, same title card).
  The game's string table is keyed by Mono's case-insensitive string hash (h = h*31 + upper(c)): with it
  `tools/unity/loctable.py`'s dump can be read offline. The bosses' health bar texts: Yggdrasil / Elder Ent, Leiana
  Sisters / Gold Mane Knights Adjutants, Chimera / Distorted Goddess, St. Joan II / Archbishop of the Leonia Order,
  First Hero / The Sword of Carleon, **Skul / Dark Skeleton** (the hidden boss), Alexander / Emperor of Carleon
  (`label/healthbar/chapter1..6|darkSkeleton/name|title`). The list's rows are index summaries without a timeline,
  so they could not see the bosses.
- [DECISION] the First Hero's dark form counts as the First Hero (one boss). The hidden boss is named by its title
  "Dark Skeleton" (its name "Skul" is the player's). Run pages show a boss's title next to its name.
- [DECISION] list rows: no badge over a portrait; the portrait's frame takes the result's colour; without a portrait
  the result's icon fills the box (Umut may want the badge back).
- Built: `CodexCatalog.BossNames` (name + title keys; Bestiary names too: St. Joan II, Leiana Sisters, ...), our
  "Leiana sisters" page name dropped for the game's; summaries keep `EndBosses` (and the last boss); index format 2
  (older indexes are built again from the files). 440 tests pass.

## Step 11: the Dark Mirror chip, boss phase names (2026-10-17)
- Umut: on a boss page the "Dark Mirror" chip couldn't be clicked to switch to normal ("I thought we had this").
  Fact: every save slot has its own Codex; in slot 3 all boss pictures were taken in the Dark Mirror (folders
  "<key>@DM" only), so there is no normal version to show: the switch worked as built, but looked broken.
  [DECISION] with only one version the chip says "Dark Mirror only" and isn't clickable.
- His screenshot also showed "First Hero" twice in the Bestiary (step 10's names: the dark form shares the title
  card) and code names for the other phases. Built: `CodexCatalog.BossPhases`: "First Hero · phase 2/3",
  "First Hero · dark form", "Leiana Sisters · awakened", "Dark Skeleton · phase 2", "Alexander · phase 2/3/4",
  "Alexander · left / right hand" (only when the boss itself has its real name). 6 texts in 10 languages; 440 tests.
- Umut asked what the Records page is: explained (batch 3: all-time totals, bests, per skull, per end boss, the
  game's best stage times; not the daily run, that is 0.14).

## Step 12: the daily run joins 0.13; Records by a second agent (2026-10-17)
- [Umut] the daily run with the hosted leaderboard now, as part of 0.13 (nothing released before it); free names
  (12 characters), everyone on the board with wins first, monthly = sum of daily points. docs/DAILY_RUN.md.
- [Umut] a second agent builds the Records page in parallel (its own git worktree and branch; merged by Claude).

## Step 13: D1, the daily run server (2026-10-17)
- Umut: "a script or service on my server that gives the seed and does the leaderboard, light; and a prompt for my
  server's Claude session"; Docker or Python script: Claude's call. [DECISION] Docker (like the Albion Analyzer:
  Traefik does HTTPS and the subdomain; updates are one command).
- Built `server/daily/`: FastAPI + SQLite (`app/days.py` HMAC day seeds + weekday difficulty, `app/points.py`
  formula 1 + plausibility limits, `app/names.py` 12 characters / no markup / word filter with whole-word matching
  for short words, `app/store.py` one row per player and day, boards; `app/main.py` endpoints, per-IP rate limit,
  admin removal), Dockerfile, docker-compose.yml (Traefik labels to adapt), .env.example, README.md (API),
  DEPLOY_PROMPT.md (the prompt for the server session). 12 pytest tests pass (local venv, no Docker on the PC).
- [DECISION] weekday difficulty: Mon normal, Tue DM 3, Wed DM 5, Thu DM 7, Fri DM 9, Sat DM 10, Sun normal; days
  are UTC days; a run started before midnight may finish up to 6 h after.

## Step 14: D2 + D3, the daily run in the mod; other mods don't count (2026-10-17/18)
- The session ran out of tokens mid-way (Umut: "continue where we left off"); the work was intact on disk.
- Built (`DamageInsight/Daily/`): `DailyModel` (the server's answers, the run's numbers), `DailyState` (Daily/state.json,
  not per slot: random player id, last name, origin slot, tries per day with seed / mode / level / numbers / pending
  body), `DailyService` (UnityWebRequest client: the day every 10 min and at the UTC day change, boards at most once a
  minute while the book shows them, start, finish with resend + start first), `DailyProfile` (evens out a copy of
  the player's save: gear unlocked, Witch at the top, Darktech unlocked and on, DM cleared 10, NPCs rescued,
  currencies 0, no run), `DailyRun` (Play: profile -> slot 9 -> switch with the Codex borrowed; Tick in the daily
  slot: the right castle via `ResetGame`, the level, the seed after the castle drew its own; the run's start counts
  the try; the end waits for the name; Leave back to the origin slot), `DailyPages` (texts), `DailyMods` (other
  BepInEx plugins). `Patches/DailyPatches.cs`: no Steam achievements and no DLC gear in the daily slot. The Codex
  section **Daily** (`CodexWindow.Daily.cs`): boards Today / This month (your line marked), today's run, your try,
  buttons Turn on / Play today's run / Back to my game, the name field with Send / Don't send; the book opens on it
  after a daily run in the castle. Save slots: `SlotStore.DailySlot` = 9 (hidden), `Switch(..., keepCodex)`, other
  switches blocked while in the daily slot. Settings Daily/Enabled (off) + Daily/Server (hidden). Run files keep
  `daily` (the date) and `goldLeft`; run pages say "Daily run · ...".
- [Umut] "disqualify points if people use other (imbalanced) mods, only vanilla items/skulls count": the mod sends the
  other mods' GUIDs and whether the build had gear the game's catalog doesn't know; the server keeps such a run with
  0 points, marked `modded`, off both boards (`DAILY_ALLOWED_MODS` can allow harmless ones); the Daily page warns
  beforehand. Honest limit: the mod is open source, a determined cheat can fake it; the admin removal stays.
- [DECISION] the castle set-up reads the armed try (its own seed / mode / level), so it works offline after "Play".
- [DECISION] the daily slot borrows the Codex: the daily run shows in the player's own Runs list.
- 49 texts in 10 languages; 449 mod tests and 13 server tests pass; Release build clean.

## Step 16: merged and installed (2026-10-18)
- Records (step 15) merged into main (90cd4df): conflicts with the Daily section were "keep both"; the language files
  merged as the union of both sides, the template written again. 459 tests, Release build clean, Debug installed.
  The server folder for the upload was exported again (`C:/Users/Umut/SkulModding/upload/skul-daily`: now with the
  disqualification of modded runs).

## Step 17: the board as a match history, points visible, record bonuses, likes, streak (2026-10-18)
- [Umut] "start with 15.000 dark quartz" for the Dark Mirror castle's machines: `DailyProfile.StartDarkQuartz`.
- [Umut] "the leaderboard browsable the same way as our match history": the mod now sends the whole run (its run
  history file, ~30 KB) with the result; the server keeps it (`runs.run`, at most 400 KB) and gives it out by a board
  line's id (`GET /api/runs/detail`, an HMAC of date and player: the player id stays hidden). In the book a click on a
  line of today's board opens the run page (Overview / Damage / Timeline, End screen) with the player's name as title;
  today's run is pinned on top of both boards to go back.
- [Umut] "the point calculations visible + bonus points from daily records": a **Points** tab (the parts before the
  difficulty factor, the factor, the day's records the run holds, the streak, the total, how points are counted).
  Server FORMULA 2: the same base points, plus the day's records for whoever holds them when the board is read:
  first win 3,000, fastest win 3,000, most damage 2,000, least damage taken in a win 2,000, most gold left 1,000, most
  kills 1,000; x the difficulty. The month adds up the days with their bonuses.
- [Umut] "play today's run still shows after playing": hidden once today's try is played.
- [Umut] likes + streak: a thumbs up on others' runs (once per player and run, not one's own; the server refuses
  both), the count on the board's lines. Streak bonus: 250 per day in a row before today (up to 2,500) x difficulty.
  `GET /api/me` (likes received, streak, days played) is ready for the later achievements (5/10/25/50/100 likes, streak).
- Runs sent before this version have no run (Umut's first daily of 2026-10-17): their line shows only the Points tab.
- Plurals for "{0} runs today" / "{0} players this month"; 29 texts in 10 languages (the font has no "x" sign: plain x).
- 460 mod tests, 19 server tests pass; Release build clean; Debug installed.

## Step 19: 0.13.0 prepared for release (2026-10-18)
- Version 0.13.0; CHANGELOG, What's new (4 highlights), README (Run history and records, Daily run, known limits,
  roadmap), wiki (same sections, Coming next), Thunderstore description (241 of 250 characters); the Daily setting's
  text now says the whole daily run is sent. 5 texts in 10 languages; 463 tests; `release/package.ps1` builds
  `DocRun-SkulInsight_QoL-0.13.0.zip`; installed (Debug).
- The public GitHub sync leaves `server/*` out (it has its own private repo; DEPLOY_PROMPT.md describes the server).
- [PROPOSAL, Umut decides] 0.13 ships without achievements (spec batches 4-5): 0.14 = Skul's achievements + the
  mod's own daily achievements (likes 5/10/25/50/100, streaks; data from `/api/me`). README/wiki roadmap say so.
- Not built (fine for 0.13): past days' boards, the month board's runs, the castle panel, the death cam picture of
  others' runs; leftovers N4/L3 (QA_FINDINGS) stay open.

## Step 20: Umut's answers; the try's place from the live board (2026-10-18)
- [Umut] achievements in 0.14, the later versions move by one (the roadmap already says so). Other mods: none
  allowed on a daily run (as built; DAILY_ALLOWED_MODS stays empty).
- [Umut] after the server removed his run the Daily page still said "31,828 points, place 1 of 1": that line came
  from the server's answer when the run was sent (kept in Daily/state.json). Now "Your try" reads the day's board:
  points with bonuses, the current place, or "No longer on the board (removed)". 463 tests.

## Step 25: the README's top for the next version (2026-10-18)
- [Umut] on Thunderstore the README's top still announced 0.12.2 / 0.12.0 (only the sections below were updated).
  Fixed in the repo (the top box "Version 0.13", the intro, the settings table with Run history / Runs kept whole /
  Daily, compatibility, the wiki intro). [DECISION Umut] no extra 0.13.2 upload ("I don't want to bother the
  reviewer"): it goes out with 0.14, whose top box then names 0.14. release.ps1 now refuses a README whose top box
  doesn't name the version being released.

## Step 24: 0.13.1 released (2026-10-18)
- [Umut] "go" after his heads-up in the Discord thread; README/wiki got three pictures (run-history.png,
  records.png, daily-board.png: cropped, 256 colours, 40-80 KB). `pwsh release/release.ps1`: 464 tests, the zip with
  `plugins/DamageInsight.dll` (checked), public repo 95afb15 + tag v0.13.1, GitHub release, Thunderstore 0.13.1
  (review status approved). Open: Umut pastes the wiki (now with pictures).
- Seen in Umut's screenshots, for 0.14: (1) his 20 s daily death got 10,048 points, 10,000 of them the day's
  record bonuses (alone on the board, most damage / gold / kills also go to deaths): bonuses for non-wins or with few
  players need a rule. (2) The damage taken list of the first run (2026-10-16) shows code names (DarkSkeleton_Phase1,
  Emperor_Renewal, Scarecraw): check whether new runs still keep them.

## Step 23: Thunderstore rejected 0.13.0 ("Invalid submission"); 0.13.1 packed right (2026-10-18)
- The listing's review status became "rejected" (the whole package hidden, older versions too); reason shown to Umut:
  "Invalid submission". Umut asked in the Thunderstore Discord (support + rejected-uploads).
- Cause found: the 0.13.0 zip had `plugins\DamageInsight.dll` (a backslash: no plugins folder). Claude ran
  release.ps1 with Windows PowerShell 5.1, whose Compress-Archive writes backslashes; earlier releases ran in
  PowerShell 7. package.ps1 now writes the zip itself with "/" and checks the layout (any shell). Also: the shown
  description came from package.ps1 (old 0.12 text); it now mentions the opt-in online daily run.
- 0.13.1 = the same mod, packed right. Waiting for Umut's go to publish it (and the moderators' answer).
- Moderator Robyn (Discord) relisted the package (review status approved, 0.13.0 visible again) after asking about the
  Wine check in CrashReporter.Header (crash reports of Linux players). From now on every update is reviewed: tell a
  reviewer in the thread right before uploading. Skul has no AI tag, so none needed.
- Lesson for releases: run `release.ps1` with **pwsh** (the script itself said so for github-release.ps1).

## Step 22: 0.13.0 released (2026-10-18)
- [Umut] "Go" (no time for a daily run first). `release/release.ps1`: tests, zip, public repo (4fc7f2b, tag v0.13.0,
  no `server/`), GitHub release https://github.com/Umut-Aslan99/SkulInsight_QoL/releases/tag/v0.13.0, Thunderstore
  DocRun-SkulInsight_QoL 0.13.0. The server's automatic deploy took step 21 by itself (/admin/ and /api/me "today" live).
- Open for Umut: paste `docs/wiki/thunderstore-wiki.md` into the Thunderstore wiki; play a daily run with 0.13 when
  there is time (the board's run page, 15,000 dark quartz); look at /admin/. Next: 0.14 achievements.

## Step 21: the server decides "played"; the admin page (2026-10-18)
- [Umut] "make this caching dependant on the servers answer: if no run from me has been recorded, I can do the run":
  `/api/me` says today's try (none / running / finished); the mod asks with the day (every 10 min) and the boards
  (every minute while open) and drops a sent try the server doesn't have (`DailyState.FollowServer`). Today's local
  try was cleared by hand (backup `Daily/state.before-reset.json`) so Umut can replay 2026-10-17 with 0.13.
- One try on the server too: a second `start` while a try runs is refused (409 `already_started`), so a run not
  sent ("Don't send") can't be replayed by deleting the local file. Deleting a run also deletes its likes.
- [Umut + the server session's spec] admin page at `/admin/` (app/admin: index.html, admin.css, admin.js): today's
  run, a health dot (every 60 s), the token in sessionStorage (localStorage with "remember", Forget), a UTC date and
  tabs Runs (admin list, expandable numbers, Delete with a confirm) / Board (a line shows its points) / Month. Strict
  CSP, noindex, textContent only, the portfolio's colours with a light variant. 22 server tests, 464 mod tests.
- Deploy: `git subtree push` to `skul-daily`; the server checks GitHub at :07 and :37, runs pytest, deploys, checks
  health, rolls back on failure (first real test of that automation).

## Test todos for Umut (0.13.0 final test, before the go for release.ps1)
1. Start Skul: the What's new window shows the 0.13 highlights (4 lines), "Got it" closes it.
2. Esc → SkulInsight QoL → settings: Daily runs (online) shows the new text; Run history, Runs kept whole present.
3. Codex → You → Runs / Records; Codex → Daily: today's board, your run, the Points tab.
4. Tomorrow's daily run (optional but best): 15,000 dark quartz; play it; the board's run page has Overview / Damage /
   Timeline / End screen / Points. (Closing the game mid-run ends the try: test that only if you accept losing the day.)
5. Then: "go" for `release/release.ps1` (tests, zip, public repo, GitHub release, Thunderstore) and paste the wiki.

## Step 18: a daily run can't be continued; the board's run page verified (2026-10-18)
- [Umut] "make sure people can't cheat by closing the game and continuing (heal, fix mistakes)". Game facts
  (LevelManager.Save/LoadFromSave, HandlePlayerDeath): the game saves a run on entering every map (health, gear,
  place, buffs); Continue enters that map again with that health. Death clears the save at once (closing the game
  while dying doesn't help); the game's own "back to title" drops the run (Save.ResetAll).
- [DECISION] (Claude; Umut can loosen it) a daily run can't be continued: when the game loads the daily slot's game
  (only the title screen does, `LevelManager.LoadGame`, patched in DailyPatches) while a try is "started", the try
  ends as abandoned with the run as the journal last wrote it (at most 30 s old: quitting can only lose numbers),
  the save's run is dropped and the castle loads, where the name is asked. A crash ends the try too (the price).
  The Daily page says it ("One try means one go"); a notice says why when it happens.
- The daily slot always records runs (the try and the board's run come from the run journal), even with Run history off.
- Verified for Umut: a run sent to the board gives the same Overview / Damage / Timeline pages (links included) as the
  run history: a test round trip, and Umut's real daily run file through the server (identical, 31 KB, 97 events).
  Not sent: the death cam picture (the frame shows the boss / killer).
- 2 texts in 10 languages; 463 tests pass; Release build clean. Not installed yet (Skul was running).

## Test todos for Umut (step 17; after the server update below)
0. Server update: WinSCP `C:/Users/Umut/SkulModding/upload/skul-daily` over `~/projects/skul-daily` (keep `.env`),
   then tell the server session: `docker compose up -d --build`, check `/api/daily` and `/api/board`.
1. Codex -> Daily: "Today's run" on top of the board; your line below it. "Play today's run" is gone (you played).
2. Click your line: the Points tab (your run of 2026-10-17 was sent before this version: only points, no run page).
3. A daily run played from now on (tomorrow): Overview / Damage / Timeline work on the board too; others can like it.
4. In the daily slot: 15,000 dark quartz at the start.

## Test todos for Umut (D2 + D3: the daily run; needs the server from D1 running)
1. Codex (K) -> **Daily** (new section): the page explains daily runs; **Turn on daily runs**. Today's date, mode
   and level appear; the boards load (empty at first).
2. In the castle: **Play today's run**. The game goes to the title screen and loads the daily slot; continue. In the
   castle a notice says the run is ready (if the day is a Dark Mirror day, the game moves to the Dark Mirror castle
   first). Your gear, Witch and levels are maxed here; your own game is untouched.
3. Leave the castle and play (die early is fine). Back in the castle the book opens on Daily: type a name, **Send**.
   The board shows you with points and a place.
4. **Back to my game**: you are in your own slot again; K -> You -> Runs lists the daily run ("Daily run · ...").
5. Codex -> Daily again: "You played today's run" for today; "Play today's run" says so too.

## Test todos for Umut (D1)
1. WinSCP: upload `server/daily` (without `.venv`) to `~/projects/skul-daily`.
2. Give the server's Claude session the prompt in `server/daily/DEPLOY_PROMPT.md`.
3. When it reports success: open https://skul.umut-aslan.dev/api/daily in a browser (today's seed and difficulty).

## Step 15: Records page (second agent, 2026-10-17, branch `records`, merged into main)
- **Built**: the You tabs are now Build | Records | Runs (`CodexCategory.Records`, opens on "records:totals").
  `History/RunRecords.cs` (plain logic) adds up the slot's runs (`RunJournal.Runs()`, shrunk ones too): totals,
  bests with the run that set them, time per skull, end bosses, killers, places of death, Dark Mirror levels; and
  `GameRecords` (the game's own permanent records). `History/RecordPages.cs` writes the pages; `Codex/
  CodexWindow.Records.cs` is the UI. Left: filter All / Normal / Dark Mirror, six rows with a headline number each
  (Totals: play time, Bests: fastest win, Skulls: the most played one's share, Bosses: end bosses defeated, Deaths,
  The game's records: highest Dark Mirror level cleared), a line saying where the numbers start. Right: the page
  (Totals with the numbers of a run page added up and a bar per Dark Mirror level; Bests as tiles with the skull held
  at the end and a link to the run, which opens it in the Runs tab, Back returns; Skulls as time bars and the skulls
  held at the end of wins; Bosses as tiles with portrait, title, defeated / killed you; Deaths: run history vs the
  save's counter, who killed you, where; the game's records: deaths, Dark Mirror cleared level and clears, Dark
  Skeleton (highest level), Alexander, best stage times normal / Dark Mirror with the stages' names).
- The game's records read live: `GameData.Progress.deaths`, `HardmodeProgress.clearedLevel / clearedCount`,
  `demonCastleDefense.hiddenBossClearedLevel / emperorCleared`, and every `Record/bestTime/<chapter>/<stage>` save
  value in one pass (the game's GetBestTime searches the whole list each call); stage names from `map/<chapter>/
  <stage>` like `Chapter.stageName` (the Dark Mirror's chapters use the normal names, its castle has one name).
- [DECISION] only finished runs count (the run being played joins when it ends). Abandoned runs count as runs and play
  time (they have no end screen numbers) but not in the win rate (wins / (wins + deaths)).
- [DECISION] runs recorded from the middle (joinedLate) set no time records (fastest win, longest run).
- [DECISION] the Normal filter includes rookie mode; Dark Mirror levels are a section of Totals, not 11 more chips.
- [DECISION] skulls per version (an awakened skull is its own row, like the Arsenal); a "win" of a skull = it was held
  at the end of a won run; no damage per skull (summaries keep no damage rows).
- [DECISION] bosses = the end bosses only (First Hero with his dark form, Dark Skeleton, Alexander), as on the run
  pages; chapter bosses show under "Who killed you" and in the Totals' boss counter.
- [DECISION] places of death: a Dark Mirror stage counts with its normal stage (same stage, same name).
- [DECISION] on a tie the run that got there first keeps the best. No setting (a page that only reads).
- 43 texts in 10 languages (the glyph test caught 셉 and 檔: reworded to 집계합니다 / 儲存位); 10 new tests; 450 tests
  pass; Debug and Release builds clean (not deployed).

## Test todos for Umut (Records)
1. K -> You: tabs **Build | Records | Runs**; Records opens on Totals.
2. Totals in the slot with your DM 10 runs: 3 runs, 1 won, kills 855 + 32 + 419 = 1,306; check the numbers against
   their run pages / end screens. Filter Normal: "No run fits this filter." (if that slot has no normal run).
3. Bests: fastest win 21:08 (the DM 10 win); click its date: the run page opens, Backspace returns to Records.
4. Skulls: Pikeman Captain on top (20:45, about half the time), bars for the others; the captain under "Held at the
   end of a win".
5. Bosses: First Hero, Dark Skeleton and Alexander with their portraits, "Defeated: 1 · killed you: 0".
6. Deaths: Armored Golem and Thorn Trap; The Deep Woods and Bio Alchemy Lab; the save's own count (224 or more).
7. The game's records: Dark Mirror 10 cleared, the clears, Dark Skeleton / Alexander, best stage times with stage
   names: compare one with a loading screen's best time.
8. Look and feel: row sizes, the headline numbers, long captions in German; tell Claude what is missing.

## Test todos for Umut (step 11)
1. Bestiary -> Bosses: one "First Hero", then "First Hero · dark form", "Dark Skeleton · phase 2", "Alexander · phase
   2/4" and so on.
2. A boss page in this slot: the chip says "Dark Mirror only" and doesn't react; a boss fought in both modes (slot 1)
   still switches.

## Test todos for Umut (step 10)
1. The DM 10 win's row: King Alexander's portrait in a blue frame, "First Hero · Dark Skeleton · Alexander".
2. Its Overview: "Bosses defeated: First Hero (The Sword of Carleon) · Dark Skeleton (Skul) · Alexander (Emperor of
   Carleon)", three portraits.
3. Defeat rows: the killer's portrait in a red frame (no crossed bones); the thorn trap death keeps the big bones.
4. Bestiary -> Bosses: the game's boss names.

## Test todos for Umut (step 9)
1. Your DM 10 win: "Bosses defeated" shows only the end bosses (the First Hero's Dark Mirror form, Dark Skul, King
   Alexander) with their portraits and the game's own names; the row on the left names them too.
2. The build row: the fragment Rib of Horror has its icon.
3. A death's row and run page: the killer's portrait (e.g. Armored Golem) instead of the crossed bones.
4. Bestiary -> Bosses: King Alexander, Dark Skul, First Hero, Yggdrasil, Chimera, Pope under their real names.

## Test todos for Umut (step 8)
1. Runs: the right page has a title again (VICTORY in blue, DEFEAT in red).
2. Skull icons: run rows, the build row and the Arsenal (list and skull page) show the skull's head, big enough.
3. Your DM 10 win: the row says "First Dark Hero · Dark Skul · King Alexander" (or similar); Overview shows the
   boss portraits, "Bosses defeated", the fragment Rib of Horror (icon and name). End screen: normal-sized icons,
   King Alexander in the picture frame.
4. Damage tab of the win: Pikeman Captain's skill and Indomitable Will in their own groups (no longer under Other),
   and the line about the end screen's total.
5. A new run (any length): Overview "Stats at the end" (crit chance, crit damage, attacks...); a tuned inscription
   shows "Tuned" (only runs from now on: the old win didn't keep it).

## Test todos for Umut (batch 2 + end screen; installed)
0. **End screen**: K -> You -> Runs -> a run -> "End screen" (top left of the picture): the game's own panel with the
   run's numbers, picture and dark abilities. Click, Esc or Enter closes it. Try your DM 10 win (purple-gold emperor
   panel; it has no picture, it was won before the picture fix) and the thorn-trap death.

## Test todos for Umut (batch 2, after it is installed)
1. K (Codex) -> You: tabs **Build** and **Runs** under the title; Build looks as before (a bit lower).
2. Runs: your runs as rows (result, kind, portrait with badge, skulls and items, numbers, time, date). Click one:
   the right page shows Overview / Damage / Timeline; click item icons (opens their page; Backspace returns).
3. Star a run (row star or the chip on the right), filter "Starred", unstar it.
4. Settings -> General: "Run history" and "Runs kept whole" (100).
5. Tell Claude what feels off (look, sizes, missing info); a screenshot of the list and a run page helps.

## Test todos for Umut (batch 1: nothing to see yet, Claude reads the files)
1. Play a run from the castle: a few maps, take a skull, swap a few times, take and drop items, then die (or win).
2. Start another run, play a map or two, quit the game (close it), start it again, **continue**, play a bit, die.
3. Esc -> SkulInsight QoL -> General: a row "Run history" (on).
4. Send nothing: Claude reads `BepInEx/DamageInsight/Codex/Runs` (and the crash reports) afterwards.
