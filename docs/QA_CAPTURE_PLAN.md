# Giant QA pass: every description and every Codex page, captured and checked (plan)

**Status 2026-10-11 ~21:50:** built and tested (docs/QA_LOG.md steps 1-8: five short test runs, every fix in the
log). Offline corpus done; the full night capture (every gear, 11 languages) is next. Umut's scope: every place the mod changes text, played for real (start the game,
drop, pick up, open the inventory), as an unattended night shift with screenshots as proof.

## Text surfaces (checklist; [x] = captured by the tool)
- [x] Skull: ground popup (passive text + numbers), popup detail (hold down: both skills), inventory (passive), inventory
  detail (hold D: both skill texts + swap text), every other skill of the skull (skills swapped into the slots).
- [x] Item: ground popup (text + pickup preview), popup detail (its two inscriptions), inventory, inventory detail
  (both inscriptions with every step: covers every inscription's step texts).
- [x] Quintessence: ground popup (active text), inventory.
- [ ] Swap menu (full inventory, 10th item): a few samples (to add).
- [ ] Shop popup: same box as the ground popup with a price (covered by the ground popup).
- [ ] The inscription list (hold A) and the tuned (super) inscription box: Codex inscription pages cover the texts.
- [ ] The player's dark abilities (no numbers in the game's boxes; Codex pages).
- [ ] Phase 2: every Codex page (You, Bestiary, Arsenal tabs), scrolled.
- [ ] Settings page rows (labels/help in every language), combat log, boss HP numbers, cooldown seconds.

## How to run it (night shift)
1. Skul closed, Debug build deployed (dev tools).
2. `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/qa/night-capture.ps1 -Languages all` (or `-Languages en
   -Limit 5` / `-Only "KingArthur,Recruit_4"` for a test, `-Full` for the full texts, `-Run <name>` to continue a
   stopped run). It backs up the save, the game's registry settings, the mod config and the Codex progress, starts
   Skul.exe directly on the second monitor without taking the focus (Steam must be running), gives the focus back
   if the game takes it, waits for done.txt, restores everything. About 5 s per gear: ~40 min per language, ~7 h for
   all 11 (order: en, de, ru, ko, zh-Hans, ja, zh-Hant, fr, es, pt-BR, pl).
3. `python tools/qa/captures.py "<Skul>/BepInEx/DamageInsight/QA/night_<stamp>"` -> report.md + sheets/.
4. Claude reviews the report and the sheets, writes the findings into docs/QA_FINDINGS.md.

Asked by Umut on 2026-10-11 for a future session (not scheduled yet). A developer tool shows every gear in the
game's own inventory screen, one after another, and saves what the player sees. Then Claude analyses the whole
batch (Umut checks in parallel and shares his findings). Afterwards the same for the whole Codex.

## Goal
A complete quality check of everything the mod writes:
- **Readable:** does the text fit its box, any cut-off or overflowing lines, missing glyphs (boxes) in any language?
- **Length:** too long for the panel? Where would the short descriptions setting help?
- **Useful:** do the damage lines say something a player can use? Are the labels right ("Hit 2", "Enhanced
  cannonball", ...), and are the numbers plausible?
- **Consistent:** same terms everywhere, the game's own words, translations complete.

## Phase 1: gear in the inventory screen
**What gets captured:** every skull (each prefab, so every rarity / awakening tier), every item, every quintessence;
probably also the player's Dark Mirror abilities (the inventory has slots for them). About 480 gear (gear scan:
164 weapon prefabs incl. reference/polymorph ones that are skipped, 278 items, 40 quintessences).

**How (developer tool, Debug builds only, like ScanGearOnce / ScanLevelsOnce):**
- Started by a developer setting `Developer/CaptureDescriptionsOnce` (runs on entering a run, turns itself off) and
  a developer key, with progress text above the player and a stop key.
- Per gear: load it through its GearReference (`reference.LoadAsync()`, as the game's own developer menu does in
  `UI.TestingTool.GearListElement`), equip it into slot 1 (`inventory.item.EquipAt/ChangeAt`,
  `inventory.weapon.ForceEquipAt`, `inventory.quintessence.EquipAt`) or drop it and pick it up
  (`LevelManager.DropGear`), open the inventory (`uiManager.inventory.Open()`), select slot 1
  (`EventSystem.SetSelectedGameObject` on the slot's GearElement; its onSelected fills the detail panel), wait a few
  frames for the layout, capture, close, remove the gear, next.
- Fixed setup so numbers compare: a fresh run (or the developers' test map), no other items, the same skull for
  items and quintessences; the stats at capture time are stored with every capture.
- **Per capture:** a picture (full screen and the detail panel cropped) **and a JSON** with: gear name, display name,
  rarity, language, the description exactly as shown (the game's text + our numbers), and measured facts from
  TextMeshPro: `isTextOverflowing`, line count, preferred height vs. the box, characters missing from the font.
  These make "too long" and "cut off" exact instead of eyeballed.
- Output: `BepInEx/DamageInsight/QA/<date>/<language>/<category>/<name>.png|.json` + `index.tsv`; resumable
  (a progress file, so a crash continues where it stopped); failures listed, never fatal.
- **Languages:** text length differs a lot (German and Russian are long, Korean/Chinese have glyph gaps). All 11
  languages = about 5,300 captures (~1-2 hours, unattended: a good **night shift** job); or English + a few.

## Phase 2: the Codex
Same idea for the book: every entry of every section and tab (You, Bestiary: enemies, bosses, dark enemies;
Arsenal: skulls, items, quintessences, inscriptions, dark abilities), opened one by one through the book's own
navigation; long right pages captured at each scroll position. Same JSON facts per page (text, overflow, glyphs).
The You page needs a run with a full inventory (several builds). Boss pages: films and move notes as they are.

## Analysis (Claude, while Umut reviews in parallel)
1. **Automatic** (a script in tools/qa/): overflow and cut-off flags, very long texts, missing glyphs, untranslated
   English in other languages, leftover placeholders ("{0}", "???"), gear that deals damage but shows no numbers
   (compare with the damage scan), odd numbers (0, huge, negative).
2. **Visual review** by Claude of every English capture and of everything flagged in the other languages, with a
   checklist (fits, readable, useful, labels, terms), noting each finding with its capture name.
3. **Findings** in `docs/QA_FINDINGS.md`, grouped by kind (layout, wording, numbers, translation), with Umut's own
   findings merged in; then fixed in batches like the film review.

## To decide at the start of that session
- Which languages (all 11 overnight, or English + German + Russian + one CJK language).
- Also the ground pickup popup (it shows the pickup preview), or only the inventory screen.
- Which build / stats the items are shown with (neutral fresh run, or also a "full build" pass).
- Disk space: full-screen PNGs are large; the cropped panel as PNG + the full screen as JPG keeps it small.
