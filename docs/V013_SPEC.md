# 0.13 spec: run history, records, achievements (and the road to 0.14 / 0.15)

Written 2026-10-16 by Claude from Umut's request of the same day. Running step log with every change, every
**[DECISION]** Claude makes alone and Umut's test todos: **docs/V013_LOG.md**.

## Umut's request (2026-10-16)
- **Records**, and next to them a **run history** ("match history"): past runs to browse with their stats, a bit like
  the game's end screen after a death or a win: the damage split, the item build, a small timeline of the run (when
  which skull was played, when which item was swapped, ...).
- **Achievements**: a trophy page with every achievement Skul has on Steam, greyed out until earned, **per save slot**
  (Steam can't give one twice; a slot can). Secret ones are darkened: click and hold the mouse button 3-5 s to see them.
- After these two: work towards the **reward system**. The **Dark Mirror pages** he wants soon as well.
  **Remembrances** (CODEX_FINAL §9) move back. Release length doesn't matter.

## Version plan [DECISION, Umut may change it]
| Version | Content |
|---|---|
| **0.13.0** | Run history + records + **daily run** (Umut 2026-10-17: the daily run joins 0.13; docs/DAILY_RUN.md) |
| **0.14.0** | Achievements (spec C) + the mod's own daily achievements (likes, streaks) [DECISION Umut 2026-10-18; the later versions move by one] |
| **0.15.0** | Dark Mirror tab (outline at the end) |
| **0.16.0** | Rewards (outline at the end; its own planning session first: the first feature that changes balance) |
| later | Knowledge pages (Home + tour, World, People, Economy, Mechanics), Remembrances + our own achievements (CODEX_FINAL §9), Story |

## Facts (decompiled 2026-10-16)
- **The game's counters are per run.** `GameData.Progress.ResetNonpermaAll` (run start) zeroes play time, kills, elite /
  boss kills, total damage, damage taken, healing, best (biggest) hit, lost health, gear encountered, revives, road
  NPCs and special maps met. Permanent per save: deaths, castle housing, Dark Mirror cleared level and count, hidden
  boss cleared level, and `Record/bestTime/<chapter>/<stage>` (best time to reach each stage, shown on the loading
  screen). So **lifetime records need our own run history** and count from 0.13 on; the game's permanent ones go
  back to the start of the save.
- **End screen** (`UI.GameResult`): play time, deaths, kills, dark quartz / gold / bone income, total damage, damage
  taken, best damage, healing, skulls / items / essences encountered, stage name, the **death cam** picture
  (`CommonResource.deathCamRenderTexture`), gear icons (skulls, quintessence, items, fragment, Dark Mirror
  upgrades). Shown by `HandlePlayerDeath` (death) and `ShowEndingGameResult` (an ending).
- **Achievements**: `Platforms.Achievement.Type` (Platforms.Common.dll), **103** values; the enum names are Steam's
  API names. Earned through `type.Set()` → `PlatformManager.SetAchievement` → `SteamPlatform.SetAchievement`, which
  only skips Steam when Steam already has it. **The game never asks whether an achievement is earned**, so a hook on
  `PlatformManager.SetAchievement` sees every earn again, in every slot.
- **Steamworks** (Platforms.Steam.dll) gives, per achievement: name, description and hidden flag
  (`GetAchievementDisplayAttribute`, in Steam's language), the icon (`GetAchievementIcon` + `SteamUtils.GetImageRGBA`),
  Steam's unlock time (`GetAchievementAndUnlockTime`) and the global percentage of players.
- **How achievements are earned** (`AchievementTrackers/*` + 23 places in game code): boss clears (normal and
  "perfect" = no hit with an attack type that dealt 1 damage or more), legendary skulls (picking up a legendary skull
  whose name contains the achievement's name), counters (heart quartz income 100, fragments 3, the witch's help 5),
  areas entered, time stayed, actions counted, Dark Mirror machines, the Collector's special slot, ...
- **The Codex folder moves with the save slot** (SlotSwitcher), so everything kept under `Codex/` is per slot.

## A. Run history ("Runs")
**Recording** (new `Codex/RunJournal`; works always, setting General/RunHistory on):
- A run starts when the game resets its run counters (`ResetNonpermaAll`); it ends with a death (end screen), an
  ending (end screen), or as **abandoned** when a new run starts without an end. Quitting the game mid-run and
  continuing later keeps the same run (the journal is written to disk after every map and continues).
- Kept per run: start / end date, result (death: where and **who killed you**; ending: which), Dark Mirror level,
  play time, the end screen's numbers, the **final build** (skulls with their skills, items, quintessence,
  inscriptions with their steps, fragment, Dark Mirror abilities), the **damage split** (the combat log damage
  table for the whole run), the **path** (every map with its stage, kind and time), and a **timeline** of events:
  skull picked up / swapped / dropped / awakened, item taken / dropped / turned into another, quintessence,
  inscription steps reached, Dark Mirror ability bought, boss and adventurer kills, revives, shops, special rooms.
- Time per skull (how long each skull was the one you played) for the skull bar.
- The **death cam picture** saved small (JPG) with the run, like the end screen shows it.
- Files: `Codex/Runs/<start>.json` (+ `.jpg`); per slot through the Codex folder.

**Pages** (book: You → Runs). **Umut's reference (2026-10-16): League of Legends' match history** (two screenshots:
a list of the last 20 games, one row each; a click opens the game with tabs Scoreboard / Overview / Stats / Graphs).
Applied to runs:
- **The list: the last 20 runs** [DECISION: 20 shown, older ones behind "Show more"; all kept on disk]. One row per
  run: a big **result** word in colour (victory / defeat / abandoned) with the **kind of run** under it (normal,
  Dark Mirror n); the **portrait of what decided it**: on a win the last boss (First Hero, First Dark Hero, King
  Alexander, Dark Skul: the Codex's own boss portraits) with our own icon badge (crown for a win, bones for a death),
  on a death the killer's portrait; the **two skulls** (held one big) and the **item icons** in a strip (quintessence
  and dark abilities at the end, like League's trinket); a **numbers line** (kills · damage · gold); the **stage
  reached**, the run's **time** and the **date** on the right.
- **The run page** (a click on a row) with tabs:
  - **Overview** (like the end screen and League's header): result, kind, stage, killer / last boss, time, date; the
    end screen's numbers; the build with icons (linked to their Codex pages); the death cam picture.
  - **Damage**: the damage split per piece of gear (the restyled damage table: Umut called the current one
    "soulless"), and who hurt you most.
  - **Timeline**: the skull bar (time per skull along the run) and the run map by map with its events as icons
    (skull taken / awakened, items, inscription steps, bosses, revives).
  - **Graphs** (if it fits in 0.13): damage per map, time per stage.
- **Favourites and keeping runs** (Umut 2026-10-16: a favourite button, old runs vanish to save space): a **star** on
  every row and run page; starred runs are kept in full forever. [DECISION] old runs **shrink instead of vanishing**:
  the newest 100 (setting General/RunHistoryKeep) keep everything; older unstarred runs keep only their list row and
  numbers (~1 KB; timeline, damage split and picture removed). Records count every run, shrunk or not. An **index
  file** (`Codex/Runs/index.json`, one line per run) lets the list open at once with a thousand runs. Estimate: a
  full run is ~50-100 KB with its picture, ~50-100 MB a year at 3 runs a day without shrinking.
- **Icons**: our own small set drawn for the mod (crown, bones, flag for abandoned, skull, swap, step...), made as
  vector drawings and baked into PNG sprites at build time (Unity 2020 can't show SVG without an extra package).

## B. Records
**Page** (book: You → Records):
- Totals since 0.13 (from the run history): runs, wins, deaths, play time, kills, damage, ...
- Bests: biggest hit, most damage in a run, most kills, fastest win, highest Dark Mirror level won, longest run.
- The game's own permanent records: deaths, Dark Mirror cleared level / count, hidden boss, **best time per stage**
  (the game keeps them since the start of the save).
- Per skull: runs, time played, wins, damage; per boss: fights, wins, losses; who killed you most.
- A line says which numbers count from 0.13 on.

## C. Achievements
**Recording**: a Harmony prefix on `PlatformManager.SetAchievement` writes the earn into the slot's
`Codex/achievements.json` (id, date, run), whether Steam has it or not. A **notice** like Steam's shows when an
achievement is new **in this slot** (setting, on), since Steam itself shows nothing the second time.

**Texts and icons**: name, description and hidden flag from Steam at run time (Steam's language) and/or a table of
all 103 in the game's 11 languages built offline from Steam's public pages (to research in batch 4: so the page
follows the game's language like the rest of the mod). Icons from Steam, cached on disk once loaded.

**Page** (book: You → Achievements), Steam-like:
- Header: earned in this slot x / 103, on Steam y / 103.
- A grid of tiles: icon, name, description, earned date in this slot; not earned = greyed out. Steam's own state
  and the global percentage as a small line.
- **Hidden** achievements: a dark veil ("Hidden achievement") until you **hold the mouse button on it for 4 s**
  (a progress bar fills; letting go resets it) [DECISION: 4 s, in Umut's 3-5 s; once revealed it stays revealed in
  that slot].
- Counted achievements show their progress (e.g. heart quartz 37 / 100).
- **Tips** for the hard ones (perfect boss clears and challenges): what counts as taking damage, read from the
  game's code (Umut's idea from 2026-10-04, CODEX_FINAL §6.4).

**Achievements in slots that already exist** (Umut 2026-10-16: b + c): a slot made before 0.13 gets what its save
proves (boss clears, endings, Dark Mirror levels, rescued NPCs, ...), the rest is earned again; plus a one-time
button "take over Steam's achievements" for the slot of the original game.

## D. Book layout [DECISION, Umut may change it]
The **You** section gets sub-tabs: **Build** (today's You page), **Records**, **Runs**, **Achievements**. During a
run the book still opens on Build.

## Settings and texts
- New settings (each with a SettingsCatalog row and help in 10 languages): General/RunHistory (on), Codex/
  AchievementNotice (on), maybe General/RunHistoryKeep (how many runs to keep; default all).
- Every new text in all 10 translations in the same change (rule). Achievement names and descriptions are the
  game's own (Steam), not ours to translate; tips are ours.

## Batches (each ends with something Umut can test; log in docs/V013_LOG.md)
| # | Batch | Umut tests |
|---|---|---|
| 1 | **Run journal** (data only): recording, files, continue after quit, slot switch, death cam picture, tests | play 1-2 runs (die once, quit mid-run once); Claude reads the files |
| 2 | **Runs pages**: list + run page, skull bar, timeline, restyled damage split, links | browse the runs, look and feel |
| 3 | **Records page** + the You sub-tabs | the numbers against the game's end screen |
| 4 | **Achievements data**: hook, per-slot store, Steam texts / icons / hidden flags, the 11-language table, existing slots (Umut's choice), the notice | start the game once (Claude reads Steam's data dump); earn one |
| 5 | **Achievements page**: grid, grey tiles, hidden veil with hold, progress, tips | the page, the hold, a hidden one |
| 6 | **0.13.0 release**: settings rows, translations, README, changelog, wiki, What's new, QA of the new pages, small leftovers (N4, L3) | final test, then the go for release.ps1 |

## Outline: 0.15 Dark Mirror tab (own spec when 0.13 ships)
A new book section **Dark Mirror**: levels 0-10 with the game's multipliers (`HardmodeLevelInfo`: HP / attack per
enemy kind, dark elites per run and map, gold), the Darktech machines (what each does, settings and costs:
`Hardmode.Darktech`, `tools/unity/darktech_setting.py`), the upgrade shop (today's Arsenal "Dark abilities" tab:
move or link), fragments, Demon Castle Defense, and the hidden boss with riddle hints that get clearer with every
clue you touched (never the answer; CODEX_FINAL §6.3). Elite abilities already have pages (Bestiary, 0.12).

## Outline: 0.16 Rewards (own planning session first)
From CODEX_PLAN: a switch (the first balance-changing feature: off or on by default is Umut's call, said clearly in
README and changelog), skip tokens (skulls by base skull, items, quintessences), inscription activations, dark quartz
milestones, adventurer coins → their legendary items, Omen coins → an Omen item of choice, thresholds tuned against
the run history (0.13 gives us real run data for that).
