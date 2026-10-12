# QA findings (the giant QA pass, started 2026-10-11)

What the QA pass found, grouped by kind, with a proposed fix for each. Log of the work: docs/QA_LOG.md; plan:
docs/QA_CAPTURE_PLAN.md. Sources: **[corpus]** = the offline corpus (test `Qa_corpus_writes_every_description`,
report `SkulModding/QA/report.md` from `tools/qa/analyze.py`); **[capture]** = the in-game screenshots (night shift);
**[Umut]** = Umut's own findings.

Status per finding: **fixed** (commit), **open** (fix agreed, not built), **ask** (Umut decides).

## Coverage (corpus, 2026-10-11)
1310 texts per language x 11 languages, neutral stats (fresh run, Little Bone's damage for items):
137 skull texts (passive description), 132 swaps, 627 skills, 266 items, 40 quintessences, 108 inscription steps and
tuned forms. Not in the corpus (the in-game capture covers them): the game's own skill texts (not in the gear scan),
the player's dark abilities, translated game names (the scan holds English names).

| category | with numbers | median lines | 90% | max | over 10 lines | over 20 lines |
|---|---|---|---|---|---|---|
| skull | 137 | 8 | 24 | 79 | 27 | 17 |
| swap | 110 | 1 | 1 | 5 | 0 | 0 |
| skill | 627 | 1 | 2 | 10 | 0 | 0 |
| item | 112 | 0 | 2 | 5 | 0 | 0 |
| quintessence | 34 | 1 | 7 | 15 | 2 | 0 |
| inscription | 24 | 0 | 2 | 6 | 0 | 0 |

**The length problem is the skull texts.** Skills, items, inscriptions and quintessences are mostly 1-2 lines. The
long skull texts are long for four reasons (L1-L4 below), not because the numbers themselves are long.

## Numbers (wrong or doubled)
- **N1 [Umut][corpus] fixed (1830af8):** the transformed King Arthur listed "+ 24-28 Magic + 24-28 Magic" on every
  hit, dash part and skill: every hit adds mana (AddKingArthurMana), the analyzer followed that operation's link to
  the passive and counted the passive's sword wave as damage. Same rule for WizardManaChargingSpeedBonus. King
  Arthur's text: 1831 -> 1327 characters.
- **N2 [corpus] fixed:** the passive skipped (MummyPassive / MummyGunDropPassive link every gun). Was: Mummy / Revived Mummy / Revived Ancient Mummy: the "Passive" line adds up every gun's hits (one
  line with 14 hits), probably the same kind of link (the passive that hands out guns). To check and fix like N1.
- **N3 [corpus] checked, real (night 2026-10-11):** Balrog's combo reuses one bite motion for its 3 hits, each with
  a 5-hit fire burst; Thunderlord's lightning triggers on basic hit 3 and on every skill (its own operation each);
  Rising Slash's smash comes on hit (On Character Hit). Was: Balrog: each basic hit lists "+ 5 hits of 10-14 Physical" (flagged "same hit in every
  step"); Viking Thunderlord's Boiling Power and Warrior's Rising Slash the same. Real (an aura on every hit) or a
  link like N1: check in the game data.
- **N4 [corpus] to check:** Abyssal Monarch's Abyss Meteor shows a 4000% hit (flag "huge multiplier").

## Length and structure (why skull texts get long)
**Decided by Umut (2026-10-11 ~22:00):** "the Codex is the right place to show all the damage numbers unfiltered, it's
our knowledge place." So: the game's description boxes (while Shift is held) show a **compact summary** (one line per
attack, summon or transformed body, plus "Full numbers: Codex"); the **Codex keeps everything**. L1, L2 and L4 below
follow from it (compact in the boxes, full in the Codex).

- **L1 [corpus] fixed (compact, 28fc5ca):** summons are listed whole: every summon with every action (Carleon Commander: 8 buddies, 79
  lines; Grave Digger line: 4 corpses with 3 actions each; Overlord's clone: 13 actions). Proposal: one line per summon
  with its main attack, the rest in the Codex.
- **L2 [corpus] fixed (compact, 28fc5ca):** transformed bodies (6 skulls) repeat a whole skull (combo, jump, dash, skills). Proposal: one
  line per action ("Basic attack: 6 hits, 21-87 Magic"), full list in the Codex.
- **L3 [corpus] fixed in the boxes (compact: "4 x 11-13", "8 x 17-20"), open in the Codex:** identical parts are listed one by one (31 texts: King Arthur's dash "Part 1..4" all 11-13
  Magic, Ninja Master's 8 equal jump hits, Jingu Rush's 7 equal parts). Proposal: one line "4 parts of 11-13 Magic =
  44-52".
- **L4 [corpus] fixed in the boxes (compact: more than two combos on one line, equal lines once):** several basic combos (10 skulls: the Mummies' guns, Davy Jones, Champion, Spartans, Plague
  Doctor) each get a full block; the Mummies list every gun twice ("Fire forward" / "Fire upward" with the same
  numbers). Proposal: merge forward/upward when equal; one line per gun.
- **L5 [corpus] note:** the Short descriptions setting shortens lines but not their count (one line per step either
  way): it does not help the overflowing boxes.

## Labels
- **T1 [corpus] fixed (this step):** our own names (SummonNames: Recruit, Archer, Assassin, Man-at-Arms, Hamlet, King
  Lear, Macbeth, Othello, Crew, Rifle/Cannon/Bomb crew, Double Crossbomatic, Clone, Turret, Upgraded turret; Sphinx's
  Poison...), translated; a quintessence's own summon uses the game's quintessence name (Hydra, Imp, Raven Lord); a
  transformed body's skills use the game's skill names. Was: summon names come from the game's prefab names: "Summons Recruit 4 Swap BuddyRecruit:",
  "Summons GraveDigger 3 KingLear:", "Summons DavyJones ReadyToShoot Crew Rifle Stand(Parent):" (the summons have no
  name in the game's texts, key "Unspecified"). The same English-looking names show in every language (they are most of
  the "English left in translations" counts). Proposal: our own short names, translated (Recruit, Archer, Assassin,
  Man-at-Arms; Hamlet, King Lear, Macbeth, Othello; Clone; Crew; Turret...).
- **T2 [corpus] ask:** summon action labels made from code names: "Skill buddha cyclone early finish", "Skill rpaid
  thrust following hit finish" (typo in the game data), "Buddy recruit attack", "Emerge-attack-despawn", "Intro".
  Goes away with L1's one line per summon.
- **T3 [corpus] fixed (night 2026-10-11):** parts without damage are dropped, the rest numbered 1, 2, 3 (corpus: 42
  texts with gaps -> 0). Was: part numbers that start late or skip (42 texts: Champion's Combination "Part 3, 4, 5, 6, 7, 8,
  10..."; Dark Paladin's Dark Rush "Part 2, 3, 4"): the numbers count motions that deal no damage. Proposal: number
  only the parts with damage (1, 2, 3...).

## Translations and fonts
- **F1 [corpus] ok:** no character outside the game's fonts in any language; no leftover placeholders ({0}, ???,
  NaN), no 0 or negative damage.
- **F2 [corpus] ok apart from T1:** English left in translations is nearly all summon names (T1) and gear names (only
  English in the offline scan; translated in the game).

## Layout (in-game capture)
- **U1 [capture] fixed:** the mod's mini log was drawn over the inventory (a "38 Item >> Dummy" line across an
  inscription text): it hides while any game menu is open (Dialogue.anyDialogueOpened).
- **T4 [capture] fixed:** the Archdemon's summon was called "Character" (its prefab's name): the gear's own name.
- **L6 [capture] confirmed:** with the old full texts King Arthur (41 lines) and Carleon Commander (48 lines) run into
  the name, category, flavor and skill names in the inventory (Umut's screenshot); the compact texts fit (test run
  2026-10-11 21:42: King Arthur, Recruit_4, Mummy_3, Grave Digger 4, Davy Jones, Archdemon, Sphinx, Third Demon
  Lord's Horn: nothing outside its panel, nothing running into another text).
- **English, full capture (night 2026-10-11, run night_2026-10-11_2231): 2092 pictures, nothing drawn outside its
  panel, nothing running into another text, no missing glyph.** Every skull (popup, popup detail, inventory, detail,
  every skill), item (popup + inscription details, inventory + detail) and quintessence. Sample and flagged sheets
  checked by eye.
- **G1 [capture] note:** gear the capture can't show: Dracula (popup and pickup fail: a special skull), Skul_Tutorial
  (the tutorial skull), Icarus's Dagger (a cut item: not obtainable, no name, inscriptions "None"; the game's own popup
  fails on it). None of them reaches players; nothing to fix.
- **G2 [capture] closed (2026-10-12): not in normal play** (Umut picked it up and dropped it: no lasting bonus), so an
  artefact of the capture tool's programmatic discard; the tool takes the cudgel last. Was: after the Olive Tree Cudgel is picked up and discarded, physical attack stays doubled (the
  tool logged 1 -> 2). Players discard it with the game's own destroy key the same way, so this is likely the game's
  own behaviour; worth a look in a normal run (Umut). The capture takes it last from now on.
- **N5 [capture] checked, real:** Brawl's shockwave shows "2 Physical/Magic": 15 % of 8-12, up to 3 hits a second.
- **German and Russian (same run): 4126 pictures, 3 flags, no missing glyph.**
- **L7 [capture] fixed and checked in game (03:31):** German Feuerschutz (Arson) step 1 (on Burning Scripture, Manatech Cogwheel): the burn and
  splash lines with their calculation ran into step 2 even at the fit patch's smallest font (60 %). Compact boxes now
  drop the calculation from status lines and write each inscription part on one line (heading · trigger: total).
- **G3 [capture] game:** Russian Carleon Commander ground popup: the game's own two squad-member labels
  ("Товарищ по отряду: лучник" / "... бронированный солдат") run into each other. Not the mod's text; could be fixed
  by letting those labels shrink (later, if wanted).
- G2 again: the Olive Tree Cudgel adds +1 physical attack in every language (1 -> 2 -> 3 -> 4): the bonus stacks with
  every pickup and stays after the discard.
- **Korean and Chinese (Simplified): no flag, no missing glyph** (sample sheet checked by eye).
- **R1 [capture] ask (readability, all 14,800 pictures by their JSON facts):** while the numbers show (Shift held),
  the game's auto-sizing boxes shrink their font to fit the mod's lines: skull swap description 807 boxes (smallest
  57 %, German Guan Yu), inscription steps 512 (69 %, our fit patch), skull passive 394 (65 %, German Davy Jones),
  skill descriptions 116 (73 %). Mostly German and Russian. Without Shift the boxes show the game text only (no
  shrink). Options: accept (it only happens while Shift is held), or make swap/passive lines even shorter (only the
  total per attack). Umut decides. (The game's own long names shrink too: ~1,000 boxes, not ours.)
- **Full capture done (night 2026-10-11/12, run night_2026-10-11_2231, 03:23): 22,517 pictures, 11 languages,**
  every skull, item and quintessence. Mod text outside its panel or running into another text: only L7 (Arson step
  1 in de, es, fr, pl, pt-BR; fixed and checked in game at 03:31: German, Spanish, French, Russian fit now). No missing
  glyph in the mod's texts. Failures: only G1 (3 special/cut gear per language).
- **G4 [capture] game:** French flavor texts of three lines (Cup of Fate, Veteran Warrior's Gloves, Time of the
  Great Sage) are taller than their band and run into the rarity label. The game's own text.
- **G5 [capture] game:** the game's Spanish Davy Jones text writes "3ª"; "ª" is not in the game's font (shown through a
  fallback font in the full run, missing in a later short run). The game's own text.
