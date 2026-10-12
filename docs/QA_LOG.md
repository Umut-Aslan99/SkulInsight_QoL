# QA pass log (started 2026-10-11 ~20:40, with Umut)

Running log, updated after every step, so an auto-compaction or a new session loses nothing. Newest entries at the
bottom of "Steps". Choices Claude made alone are marked **[DECISION]**: Umut may change any of them.
Plan: docs/QA_CAPTURE_PLAN.md. Findings: docs/QA_FINDINGS.md.

## Setup (read first when resuming)
- Umut (2026-10-11 ~20:30): the 0.12 Codex test waits; before testing 0.12 and before adding anything to the
  Codex, the full QA pass comes first. His screenshots: King Arthur's description far too long (the inventory text
  overflows its box; the Codex page repeats "+ 24-28 Magic + 24-28 Magic" on every hit).
- Scope (Umut's words): "The testing needs to conclude: every skull, every skill, every item, every description that
  has a number calculation. So every item and inscription as well." So: every skull (passive text with basic / jump /
  dash / passive / summons / transformed bodies), every skill, every swap, every item, every quintessence, every
  inscription step and tuned form, the player's dark abilities.
- Umut (~21:30): the in-game capture with screenshots stays wanted, as an unattended **night shift** job ("proof"
  with screenshots). New feature request (player feedback): the mod's extra information only while a key is held,
  like the game's own "hold for details": **hold Shift** shows our numbers in the game's description boxes.
- Save slots: Umut tested most of SAVE_SLOTS.md test todo 3 (switch back to slot 1): works.
- Branch: work on **main** (0.12.0 not released yet; QA fixes go into 0.12.0). For Umut's game: merge main into
  save-states and deploy from there (his game keeps save slots).
- Never deploy while Skul runs (watcher loop, PROJECT_STATUS "Who does what").

## Decisions
- **Umut (~22:00):** the Codex shows all numbers unfiltered (the knowledge place); the game's boxes get a compact
  summary while Shift is held. To build before the night capture, so the screenshots show the final layout.
- **[DECISION]** Two layers: (1) an **offline corpus**: every text the mod writes, for every gear in the gear scan,
  produced by the mod's own formatting code in the tests (no game needed, all 11 languages, neutral stats); it checks
  content (length, repeats, labels, numbers, translations). (2) the **in-game capture** (QA_CAPTURE_PLAN phase 1/2):
  pictures + TextMeshPro facts (overflow, lines) for layout and fit, run by Umut starting the game once.
- **[DECISION]** Stats for both: neutral (fresh run, no items), so numbers compare between gear.

## Steps (newest last)
0. 20:40 Log started. Cause of the King Arthur repeats found: every hit of the transformed King runs
   AddKingArthurMana, whose `_passive` field links the King's passive ability; the analyzer follows the link and
   counts the passive's sword wave (InstantAttack 100% + wave projectile 100%) as damage of every hit. Gaining mana
   deals no damage.
1. ~20:55 (1830af8) DescriptionLayout: how texts are put together, shared by the game (GearDescriptions) and the tests.
   QA corpus test (QA_CORPUS=1 [QA_LANGS=en]): 1310 texts x 11 languages -> SkulModding/QA/corpus/<lang>/corpus.tsv|
   jsonl|txt. Fix N1 (King Arthur): AddKingArthurMana / WizardManaChargingSpeedBonus not followed.
2. ~21:05 (40c0093) tools/qa/analyze.py -> SkulModding/QA/report.md; docs/QA_FINDINGS.md started.
3. ~21:20 (ecbd58e) Hold to show (Umut's request): Descriptions/HoldToShow (on) + HoldKey (LeftShift, either Shift);
   hint "Hold Shift for damage numbers"; open boxes refill on key change (ExtraInfo: inventory re-selects, ground
   popup Set again with its label kept, swap menu re-selects). Texts in 10 languages; 17 Codex move names translated
   (the template test failed on names from Umut's recent play).
4. ~21:35 (d84afc4) QA capture tool (Tools/Dev/QaCapture.cs, Debug only): per gear and language: drop -> ground popup
   + detail (forced "hold down") -> pick up (DroppedGear.InteractWith) -> inventory simple + detail (forced "hold D"),
   every skill of a skull (currentSkills swapped) -> discard. Picture cropped to the box grown to the drawn text, JSON
   with each TMP box's facts. Test map, invulnerable, input blocked, CodexTracker.Paused, ExtraInfo.Forced. Start:
   Developer/QaCaptureOnce or QA/request.json (skips "press any key", windowed 1920x1080, quits when done).
5. ~21:45 (28fc5ca) Compact numbers in the game's boxes (Umut: the Codex has all numbers): one line per attack,
   totals side by side, equal hits merged, named steps kept, many combos on one line, summons one line each (max 4,
   once per name), transformed body basic/jump/dash only, "All numbers: Codex (K)". Descriptions/Compact (on).
   Skull texts: median 8 -> 4 lines, max 79 -> 10. tools/qa/night-capture.ps1 (backup -> Steam start -> window to
   the second monitor -> wait -> restore save files with a fresh time stamp, game registry settings, mod config,
   Codex progress).
6. ~22:00 (c73c72b) T1: summon names (SummonNames table + translations; quintessence summons use the game's name);
   transformed skills use the game's skill names. (0070dd3) N2: Mummy passives not followed.
7. ~22:05 tools/qa/captures.py (report + contact sheets of a capture run). Merged main into save-states (06616dc),
   354 tests pass, Debug build deployed 21:24.
   **[DECISION]** captures with the numbers shown (ExtraInfo.Forced) and the detail views open; the "hold Shift"
   hint state is checked on a few samples by hand. **[DECISION]** items and quintessences are captured holding
   Little Bone (dropped + picked up first), skulls last. **[DECISION]** a full-screen JPG per gear only in English
   (disk), cropped PNGs for every language. **[DECISION]** compact boxes drop notes (chance, can't crit) and keep a
   section's trigger ("On critical hit"); the Codex shows everything.
   Untested: the capture tool and the night script have not run yet (need the game): next is a short test run.
8. 21:27-21:48 Five short test runs with Umut's OK (English, 3-8 gear each; save, game settings, mod config and
   Codex progress backed up and restored byte for byte every time). Fixes found by them:
   - the request file was read with Loc.Parse (only "strings"): fell back to all gear, no quit -> GearDoc.ParseRaw;
     PowerShell wrote one language as a string -> @(...).
   - pictures cut at the sides: taken while the inventory parchment / popup was still unrolling (Umut: "it was so
     fast") -> ScrollAnimator.Appear finishes at once while capturing, short real-time waits before each picture.
   - Umut: no focus stealing, no tabbing out of fullscreen programs, game on monitor 2 -> night-capture.ps1 starts
     Skul.exe directly ("show without activating", temporary steam_appid.txt so Steamworks doesn't restart it through
     Steam), borderless on the second monitor via the game's own UnitySelectMonitor (+ -monitor 2), no window moving
     (the old SetWindowPos without NOACTIVATE was what activated the window), focus watchdog gives the focus back
     (1-5 times per run). The pictures come from the game's rendering, so a background game is fine (Umut asked).
   - "overflow" by box-height ratio gave false alarms (one-line labels) -> "outside" (text drawn beyond the visible
     panel: every visible image of the box + the inventory parchment) and "runs into" (texts overlapping), hidden
     texts skipped, popup detail measured on its own box.
   - inventory crops back to the detail box (the night would have been ~7 GB); no popup detail picture without one.
   - QA findings from the pictures: U1 the mini log covered the inventory -> hidden while any game menu is open;
     Archdemon's summon showed as "Character" -> the gear's own name.
   - Control run with the full texts (-Full): King Arthur (41 lines) and Carleon Commander (48 lines) flagged as
     running into the name, category, flavor and skill names (Umut's screenshot); compact: clean. Commits 5e..: see
     git log (QA capture fixes, round 2-5).
   Timing: ~5 s per gear -> ~40 min per language, ~7 h for all 11 (night order en, de, ru, ko, zh-Hans, ja, zh-Hant,
   fr, es, pt-BR, pl; a stopped run continues with -Run <name>).
