# Film review: every boss move, checked and written up

Task (Umut, 2026-10-04): go through every boss's filmed moves. For each film: does it match the move (the move list
from the AI), is the attack visible from start to finish? If the take is good, write the move's note for the Codex
(what it does, how to deal with it; conditions where the automatic "When:" line misses them) in all 11 languages.
If not, mark it for a new take and write down why. Then the next move, then the next boss.

## How
- Inventory: `python tools/codex/film_inventory.py [key]` (films, length, old takes, refilm marks, moves without film).
- Look at a film: `python tools/codex/contact_sheet.py <key> "<label>" <out.png> [--every N]` (numbered stills).
- Conditions: `Codex/Hints/<key>.json` (what the book shows under "When:"), the AI report `Codex/Debug/<key>_attacks.txt`.
- Notes: `DamageInsight/Codex/codex_content.json` (English, keyed by entry key without "@DM", then move label:
  "what", "tip") and `codex_content.<code>.json` for de, es, fr, pl, pt-BR, ru, ko, ja, zh-Hans, zh-Hant (same keys;
  game terms; glyphs of the game fonts only). One note per move label serves both Normal and Dark Mirror.
- A bad take: add its label to `Codex/Replays/<key>/refilm.txt` (only while Skul is closed) and note the reason below.
- Verdicts: **ok** (note written), **refilm** (reason), **retake pending** (an old take ≤ 3 s the mod films again
  anyway; note written only if this take already shows the whole move).

## Order
Yggdrasil, Leiana sisters (+ Awakened Leiana), Chimera, Pope (+ dark crystals), First Hero, the adventurers
(Archer, Cleric, Hero, Magician, Thief, veterans, supporting), Dark Skul 1-2, First Dark Hero, King Alexander 1, 2
(hands + heart), 4.

## Progress
(one section per boss as it is done)

### Findings that affect the recorder
- Our own damage numbers (yellow / green) show in the films (Yggdrasil Awakening, Both fist power slam): hide them
  while the film camera renders, like the player. (open)
- The film camera follows the player, so the view pans during a take; fine for big bosses.
- Chimera's seen moves include "Sleep" (the empty room before it wakes, has a film) and "Die": neither is a move.
  Drop them from the move list like "Initialize" (open).
- The Supporting Thief/Warrior takes (Dark Mirror helpers) show other adventurers' moves (Cleric's holy cross, the
  Hunter's green beam) instead of the helper's own "Attack 1/2": the helper is probably off camera or the take is
  credited to the wrong character. Marked for refilm; check the next takes (open).
- Special-skill takes (Hunter's Arrow rain, Cleric's Massive heal) show only the cast being hit by the player: the
  skill itself is missing. Marked for refilm.
- Dark Mirror takes of Chimera (marked refilm before) are messy: long idle stretches, venom balls of earlier moves
  bouncing through later takes.

### Yggdrasil (done 2026-10-04)
| Move | Normal | Dark Mirror | Note |
|---|---|---|---|
| Fist slam | ok (2 slams) | ok (3 slams + root spikes) | written |
| Sweeping | ok (left, then right) | ok | written |
| Energy bomb | refilm (3 s, already marked) | ok (ends as the last bombs fall) | written |
| Groggy | ok | ok | written |
| Appearance | entrance, not a move | - | - |
| Phase 2 · Awakening | ok | ok | written |
| Phase 2 · Fist power slam | ok (4 slams) | ok (+ pink spikes) | written |
| Phase 2 · Both fist power slam | ok (red warning line, 3 double slams) | ok (+ row of spikes) | written |
| Phase 2 · Sweeping | ok (4 sweeps) | ok | written |
| Phase 2 · Energy corps | ok | ok | written |
| Phase 2 · Groggy | ok | ok | written |
Old draft notes "Laser" (part of Energy bomb) and "Phase 2 · Sweeping combo" (now "Phase 2 · Sweeping") removed.
No AI conditions besides the phase (phase 2 at half HP); the two Groggy moves follow Energy bomb / Energy corps.

### Leiana sisters + Awakened Leiana (done 2026-10-04)
Notes key "LeianaShortHair" (the sisters' shared page; LongHair films show on it) and "AwakenLeiana". Sheets with
`--zoom` (the sisters are ~20 px tall). Normal and Dark Mirror look the same for the sisters (the cyan flames, purple
floor and numbers in the DM films are the player's own effects). Notes in docs/notes/LeianaShortHair.json and
AwakenLeiana.json, applied.
| Move (sisters) | Verdict | Note |
|---|---|---|
| Pair phase · Twin meteor | ok (X of aim lines, dive, meet, dash away) | written |
| Pair phase · Twin meteor pierce | ok | written |
| Pair phase · Twin meteor chain | ok | written |
| Pair phase · Twin meteor ground | ok | written |
| Single phase · Golden meteor | ok (auto retake anyway) | written |
| Single phase · Dimension pierce | ok (one beam per cast) | written |
| Single phase · Backstep | retake pending (4-8 frames; the hop is visible in LongHair's 8) | written |
| Single phase · Meteor in air | ok | written |
| Single phase · Rising pierce | ok (lightning, then waves of light pillars) | written |
| Single phase · Range attack homing | ok in ShortHair (+DM); LongHair's take: **refilm** (she is at the picture's edge, bolts already flying at frame 0) | written |
| Single phase · Rush | ok | written |
| Single phase · Dash | retake pending (4 frames, out of the picture after 1) | written from the AI |
| Single phase · Meteor in ground | ok | written |
| Awakening | ok (LongHair, 81 / 97 frames) | written |

| Move (Awakened Leiana) | Verdict | Note |
|---|---|---|
| Golden meteor | ok (2 dives, purple pillar, dark spikes both sides) | written |
| Meteor in air | ok (2-3 dives) | written |
| Meteor in ground 2 | ok (backstep, purple dash across the floor, tendrils rise along the path) | written |
| Rush | ok (teleports, criss-cross lines flash into slashes) | written |
| Dimension pierce | Normal: retake pending (9 frames); DM ok (17 frames, several beams) | written |
| Range attack homing | retake pending (14 frames both modes; orbs still firing at the end) | written |

Rules from the AI (`Characters.AI.TwinSister`, coroutines, so there is no Hints file; the notes carry them):
- Chapter2Script.Combat: pair phase (a random number of twin moves, never the same twice in a row) and a timed single
  phase alternate. In the single phase one sister fights, the other waits in the background (collider off).
- Single phase: Golden meteor opens it; Rising pierce whenever ready (not in the first seconds: pre-delay, then a
  cooldown); Dimension pierce whenever ready; otherwise a random pick: near (melee trigger) Meteor in air / Dimension
  pierce / Rush / Rising pierce / Backstep / Meteor in ground, far Meteor in air / Dimension pierce / Dash. After Dash:
  Rush (30 %) or Meteor in ground; after Backstep: Range attack homing (40 %) or a pause; after Rush a pause.
- When one sister dies the other awakens (invincible meanwhile); both dead at once: one is revived to full first.
  Awakened Leiana's health = the survivor's current health + 40 % of her own maximum (DarkAideAI.ApplyHealth).
- Awakened Leiana: Rush (after a pre-delay) and Dimension pierce whenever ready, else a random pick of Golden meteor
  (several dives), Meteor in air (several), Backstep + Meteor in ground 2, Range attack homing.
- Text: our "Leiana sisters" in ru/zh-Hans now match the game's names (Сёстры Леяны, 莱安娜姐妹); game names for
  Awakened Leiana and the Golden Mane Knights are in the notes files. String table dump: tools/unity/loctable.py
  (writes loc_table.json; key hash in docs/LOCALIZATION.md).

### Chimera (done 2026-10-04)
Notes from the Normal takes (the Dark Mirror ones are all marked for a new take already). Values from the game data
(`tools/unity/script_fields.py`): faster animations (1.5x) below 50 % HP; Wreck drop below 50 % HP every 30 s, always
followed by Venom breath and Wreck destroy; Subject drop every 20 s; Stomp when close, cooldown 7 s, 50 % chance,
1-3 stomps; Bite when close, 10 % chance; Venom fall cooldown 7 s, 4 columns 0.8 s apart (last two together), an
extra end attack in the Dark Mirror, longer rest after it below 50 % HP and in the Dark Mirror; Venom ball: no pause
after it. No Hints file (coroutine AI): the notes carry the conditions.
| Move | Verdict | Note |
|---|---|---|
| Sleep | not a move (empty room before the fight) | - |
| Intro | entrance, not a move | - |
| Bite | ok (auto retake anyway) | written |
| Stomp | ok (3 stomps) | written |
| Venom fall | **refilm** (cut while the columns still pour; the next take starts with them) | written |
| Venom ball | retake pending (23 frames, the ball still flying) | written |
| Venom cannon | ok | written |
| Subject drop | **refilm** (cut while rocks and tanks still fall) | written |
| Wreck drop | ok (leap, red line on the floor, landing, rubble starts falling) | written |
| Venom breath | ok (the rubble catches the stream: frames 24-30) | written |
| Wreck destroy | ok (auto retake anyway) | written |

### Pope / St. Joan II (done 2026-10-04)
Behaviour-tree AI: the book's "When:" line gives HP, cooldowns and chances once the Hints file exists (Pope has none
yet: written at the next fight). The notes carry what it can't say: Choice only after a dark crystal broke and while
fanatics are around (it makes them all sacrifice themselves into tentacles), Priest summon only when no cleric is left,
Purification only while a platform has no tentacle, Summon escort opens phase 2. Fight flow from Level.Chapter4/
Scenario.cs: phase 1 ends when both dark crystals break (all other enemies die); DM: the crystals take turns being
invulnerable (DarkCrystal.cs), phase 2 has periodic attacks on the platforms you stand on. Dark crystals have no moves.
| Move | Verdict | Note |
|---|---|---|
| Nervousness, Baptism, Consecration | ok | written |
| Worship | **refilm** (cut while the orb columns still sweep) | written |
| Choice | ok (auto retake anyway) | written |
| Summon escort, Grace, Soul chase, Divine impact, Divine light, Embrace, Priest summon, Sacrament, Divine cross, Super baptism | ok | written |
| Control escort, Purification | retake pending (19 / 20 frames) | written |
Learnings: Korean glyphs missing in the game font so far: 쏩 튑 뜁 젖 굵 앉 튕 (use 발사합니다, 터집니다, 물러납니다,
당긴, 두꺼운, 위에 있으며, 튀어); zh-Hant lacks 彎 匯 瀉, zh-Hans 泻 侧 俩 斜 蹲, ja 斜 (write ななめ).

### First Hero, phase 1 (done 2026-10-04)
Only Dark Mirror takes exist (FirstHero1@DM, 27; "Slam" seen, no film). Notes key "FirstHero1". Phases from the tree:
a 100-65 % (basic moves; Rush every 40 s), b 65-30 % and c 30-0 % (enhanced moves with red shock waves, Hero landing,
Assassination when you are far, Energy ball when you are close and in the air, Enhanced rush every 50 s), c adds Big
bang every 100 s. All 27 takes show their move; the 15 short old takes (Dash, Back dash, Dash chase, slashes, short
landings, Grab dash/combo, Assassination, Energy ball, enhanced slashes) are re-filmed by the mod anyway; their notes
are written from what they show plus the AI. FirstHero2/3 (later forms) have no films yet.
**Recorder fix found here:** the "When:" line ignored `CoolTime(value=N)` blocks (Big bang 100 s; Pope's Super
baptism / Divine cross 50 s) and `TargetIsGrounded(inverter=True)` (Energy ball). Both are read now (new phrase
"while you are in the air" in all languages; test Move_hints_read_cool_time_blocks_and_air_checks). Hints files are
rewritten at the next fight with each boss.

### Adventurers (done 2026-10-04)
Notes keys AdventurerArcher (the Hunter), AdventurerCleric, AdventurerHero (Rookie Hero), AdventurerMagician (Mage),
AdventurerThief, VeteranMagician, VeteranWarrior (54 moves; Intro is no move). Shared rule from the trees: below 50 %
HP an adventurer casts the special skill; enough damage during the cast (RunningUntilAmountOfDamage 200, scaled by
GetAdventurerCastingBreakDamage) breaks it and they fall Groggy; below 90 % they drink a potion (a tail, no move). The
special-skill notes say this; Groggy has one shared note. Conditions read from the trees: Hero Back dash when close
every 10 s; Warrior veteran Guard when close every 10 s, Whirlwind below 70 % every 50 s, Fury of earth below 40 % every
80 s. VeteranMagician has no AI report yet (notes from the films only). Most adventurer takes are short old takes the
mod re-films; notes describe what they show. Refilm: Hunter Arrow rain, Cleric Massive heal (skill not visible),
Supporting Thief/Warrior Attack 1/2 (wrong subject; no notes written for the helpers).

### Dark Skul 1 + 2 (done 2026-10-04)
Dark Mirror only. Notes keys DarkSkul1 (16) and DarkSkul2 (47; the Balance-form moves reuse phase 1's texts; Special
move, Dash and Earthshatter normal written from the AI without a film). Conditions come from the Hints files (forms,
below 55 % HP, cooldowns); the notes name the form at the start of Power/Speed moves. Form names follow the game's
own words (Balance / Power / Speed, string table rows 27-29). Takes seen: the old short takes Umut flagged (Bone rain,
Jump throw head, Raise bone rain, Star fall ready, Javelin, Punto, Spike spot, Fast moves, Stamping center, Rush) are
still the old ones: the 0.11 re-film has not happened yet (no Dark Skul fight since the build). Additionally marked:
DS1 Raise bone rain; DS2 Fast move aerial (only the background flames are in the picture), Stamping center, Star
fall ready, Bone rain.

### First Dark Hero (done 2026-10-04)
Dark Mirror only, 27 takes, notes key FirstDarkHero (27). The tree has three stages (`Step == 1/2/3?`; the "When:"
line shows "in stage N"); the notes don't repeat them. All takes show their move; short ones (Dash ground to wall,
Raid, Dash wall to wall (marked before), Grab dash try, Double slash, Dash grab fail, Dimension rush, Zigzag rush,
Thorny hell on wall) are re-filmed by the mod anyway. Overrun's take is 24.8 s with a long idle tail (fine).
Text fix found here: pl "Ciernisty piekło" → "Cierniste piekło" (Thorny hell on ground/wall, neuter noun).

### King Alexander 1, 2 (heart), 4 (done 2026-10-04)
Dark Mirror only. Notes keys Alexander1 (22, incl. Only warp out / Vertical long laser / Chasing missile 10 written
without a film), Alexander2_Heart (14), Alexander4 (12). The "about" texts name the game's "Alexander, Emperor of
Carleon" and explain the forms. Heart move names are pairs of three parts (dark ball / dark laser / dark ground = oil
that ignites; the translations already say "Dunkelkugel + Dunkellaser" etc.), the notes describe them that way. All
heart takes are take 2 but many are cluttered by overlapping hand attacks and were already marked for refilm (Ball-
laser, Dark big bang, Enhanced dark laser/ground/ball, Groggy 2, Celestial meteor); notes describe what the takes show
and stay valid. Alexander's hands (Alexander2_Left/Right) have no films and no notes yet. Phase 3 (bomb) never seen.
CJK: "island" is 浮き足場 / 浮台 in the move names (fonts lack 島/岛), the notes follow. First Hero's "Sword of
Carleon" in ja/zh/fr now uses the game's spelling (カーリアンの剣, 卡利恩的剑, 卡倫的劍, Caerleon).

### Review complete (2026-10-04 evening)
Every boss with films has reviewed notes in 11 languages: Yggdrasil, Leiana sisters + Awakened Leiana, Chimera, Pope,
First Hero 1, 7 adventurers, Dark Skul 1-2, First Dark Hero, King Alexander 1/2/4 (about 320 notes). Not covered (no
films yet): First Hero 2-3, Alexander's hands, Alexander 3 (bomb), Supporting Thief/Warrior (their takes show other
adventurers). Next round: after Umut's fights re-film the marked takes, check those takes again (film_inventory shows
`take 2` and refilm marks).

## Review 2 (2026-10-05, after the Codex reset and Umut's full Dark Mirror run)
Films: Yggdrasil … First Dark Hero filmed in natural play in the morning (old PreferNewMoves steering); Dark Skul 2's
last 10 moves and King Alexander 1 / heart / 4 with the move director (no pacing yet). Notes compared to every
boss's AI move list (`Codex/Debug/<key>_attacks.txt`) and the takes looked at as contact sheets.

| Boss | Films | Takes vs notes | Open |
|---|---|---|---|
| Yggdrasil | 10/10 | all match, whole | – |
| Leiana sisters | 12+13 | all match | note for "Single phase · Meteor in ground 2" (no film of its own: runs inside twin moves) |
| Awakened Leiana | 6/6 | all match, clean | – |
| Chimera | 9/9 | match; "Venom ball" runs on into Wreck drop, "Wreck drop" starts with old venom balls | – |
| Pope | 15/17 | match; Divine cross runs into Sacrament's circles; Priest summon barely shows the clerics | Choice, Purification never came |
| First Hero 1 | 27/28 | match | Slam no film |
| Adventurer Cleric | 5/6 | match; Massive heal shows only the cast | Reinforce no film |
| Supporting Thief | 1/2 | "Attack 1" shows the Cleric's crosses (same pictures: the camera follows the player) | notes for Attack 1/2 (from the AI) |
| Dark Skul 1 | 16/16 | match; quick moves are ~1 s (Throw head, Dash, Upper attack) also in natural play | – |
| Dark Skul 2 | 46/46 | morning takes match; the 10 director takes run into each other (Melee attack shows the bone rain before and the hard smash after; Throw head 0.9 s and Stamping center 0.8 s cut) | note for "Unstoppable"; retake the 10 with pacing |
| First Dark Hero | 29/29 | match | "Summon thorn" (take shows nothing yet) and "Dark" (= Dark orb fragments + Dark blast wall merged: step key bug, fixed) |
| King Alexander 1 | 21/22 | good despite the director (long, distinct moves) | Horizontal long laser (platform) no film |
| King Alexander heart | 14/21 | busy: two lanes at once; Celestial meteor 0.9 s and Enhanced dark ground 0.8 s cut | 11 moves without note; 4 old notes for names that no longer exist (see below) |
| King Alexander 4 | 10/12 | usable; boss small at the edge, quartz from the move before lingers | Shoving, Summon bombman no film |
| King Alexander 3, hands; First Hero 2-3 | – | never met (hands only run an "Operation"); First Hero 2-3 only in normal mode | |

**Heart move names changed** (66ca14d, operations as steps): sequences that run an operation and then pick one of the
heart's patterns became one move named after the operation: "Oil ball" = oil balls + Enhanced dark laser / Enhanced dark
ball; "Bouncy ball" = bouncy balls + Enhanced wave generator; "Buzz saw" also holds Cosmic explosion. The old notes
(Enhanced dark ball/laser, Enhanced wave generator, Cosmic explosion) have no move now. Decision for Umut: how the heart's
moves are listed (see PROJECT_STATUS).

**Director rushing** (Umut's worry, confirmed for tree bosses): forced moves came back to back, takes were cut by the
next move or filmed on into it. Fixed by pacing (MoveDirector.Pacing.cs): a led move is waited for until its take is
saved; a BehaviorDesigner boss is paused the moment the move ends and resumed when the take is done (max 10 s).
Natural play shows the same kind of overlap less often (Chimera), so it is partly a recorder limit (one camera).
