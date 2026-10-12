# 0.12 build log (started 2026-10-05 ~14:00, with Umut)

Running log, updated after every step, so an auto-compaction or a new session loses nothing. Newest entries at the
bottom of "Steps". Choices Claude made alone are marked **[DECISION]**: Umut may change any of them.
Plan and consensus: docs/V012_SPEC.md (agreed with Umut 2026-10-05 ~14:00).

## Setup (read first when resuming)
- Umut is awake and testing; Claude builds in batches (feedback-work-in-batches) and hands back when Umut must test.
- Never deploy while Skul runs (watcher loop, see PROJECT_STATUS "Who does what").
- Rules: every new text in all 10 languages in the same change (Lang/<code>.json; template update with
  `UPDATE_LANG_TEMPLATE=1` on Template_has_every_text_of_the_code); every configurable feature gets Config.Bind +
  SettingsCatalog entry.
- Open 0.11 points wait (PROJECT_STATUS TODO 2-5): King Alexander retest, Codex reset decision, film fixes.

## Facts found (for the build)
- Book: `Codex/CodexWindow.cs` (1180 lines): left page = title, search, category chips (CodexCategory enum), filter
  chips, entry list; right page = title, subtitle, portrait frame (y 102-432), move switcher, tier text + bar, text
  (y 524-856, scrolls). Gear entries use the same big portrait layout today.
- Two kinds of dark ability: the player's Dark Mirror shop ones = `Characters.Gear.Upgrades` (UpgradeResource,
  today's Codex "Dark" tab, damage MotionType.DarkAbility; numbers in `valuesForDescription`); the elites' =
  `Characters.Abilities.Darks` (DarkAbility prefabs with AbilityComponents, name key `darkEnemy/ability/<name>/name`,
  pool in DarkAbilityContainer / DarkEnemySelector; Ballista summons a turret character).
- The game's stat panel (`UI.Inventory.StatOption`): health, taking damage, phys/magic atk, attack/move/charge speed,
  skill/swap/essence cooldown speed, crit chance/damage; names from `stat/<kind>/name` (11 languages free).
- Skull data: `Weapon.category` (Balance/Power/Speed/Ranged, name `label/weapon/Category/<c>`), `nextLevelReference`
  (awakening), SkillInfo `skill/<key>/name|desc`; gear texts `<keyBase>/name|desc|flavor`.
- Numbers: `GearDescriptions` analyzes live gear (or an item prefab by name: AnalyzeItemByName) with GearAnalyzer;
  `DescriptionFormatter.Section(section, stats)` formats; `StatSnapshot.Neutral` = neutral stats.

## Steps (newest last)
0. 14:00 Consensus written into V012_SPEC.md; this log started.
1. ~16:30 Framework + all pages, first build deployed (commit a69dce4). What exists:
   - `CodexNavigation` (plain logic, tests): sections You / Bestiary / Arsenal, sub-tabs per section, Back history
     (30 pages), per-section memory, links switch section + tab. Back: chip top right, Backspace, mouse back button.
   - Window: section chips on the title row, progress text moved to the bottom line, `_listPanel` / `_youPanel`;
     right page layouts Creature (big picture), Gear (150 px picture, long text from y 306), Sheet (You: no bar).
     Names in page texts are TMP `<link="id">` tags, clicked through `CodexLinkClicks` (FollowLink).
   - `CodexPages`: Arsenal page texts (skull: type, passive, awakening path, attacks, skills with cooldown + numbers
     after first use, swap, bodies, summons; item: inscriptions linked, damage, transformation; essence: cooldown,
     damage; inscription: steps (greyed above MaxStep), tuned (tier 3), items carrying it; player's dark ability:
     levels a/b/c, price per level). Neutral stats (skull pages use the skull's own base). Game texts read without
     our appended numbers (`DescriptionPatches.PlainText`).
   - `GearDescriptions.AnalyzeReference` / `WithGearPrefab`: any gear analyzed from its GearReference (cached).
   - You page (`CodexWindow.You.cs`): 2 skulls, quintessence, 4 dark abilities (only in DM), 9 items, up to 18
     inscriptions ("2 more for step 3"), Stats sheet (game panel stats + hidden damage stats + status stats, with
     short explanations). Right page = the Arsenal text at current stats + link to the Arsenal page.
   - Elites (`CodexElites`): 15 abilities from `DarkEnemySelector._constructors[level]` pools; levels computed per
     pool; met via patch on `DarkAbilityAttacher.Attach`, kills via OnHit; "seen on" enemies stored in SummonedBy
     **[DECISION]** (same "," list format). GearAnalyzer category "darks" walks from the DarkAbility root.
   - Tracking: skills used (`SkillsUsed`, via each held skull's `onStartAction`), inscription `MaxStep`.
   - Settings: `Codex/OpenOnYou` (on), balance `EliteKills2/3` (1/5).
   - **[DECISION]** item "Comes from" links only know transformations analyzed so far (6 items; fills as pages open).
   - **[DECISION]** shop currency not named ("Price per level"): the game has no name text for heart quartz.
   - Elite facts (tools/unity/dark_abilities.py, game files): Protection = front guard, durability 30% max HP, back
     after 10 s; Immortal = every 20 s summons something within 5 m for 10 s, if it survives the enemy heals 30%;
     Intense Cold = aura on you: basic attack speed x0.7, move x0.5, dash distance x0.8; Tornado = every 8 s
     tornadoes left and right; Brawler = every 6 s an attack near it (runner); Penance = when hit (up to 9x, 3 s
     apart) a burst on itself; Conductor = every 7 s a shot from a random point 10 m around you; Disaster = every
     15 s three projectiles thrown up (falling); Judgement = every 5 s a strike at your position; Divine Power =
     every 10 s heals allies nearby 50% of max HP; Cannoneer = every 10 s a cannon, max 3; Quarantine = ring around
     it, touching the ring hurts (30% atk, 1/s), inside is safe; Regeneration = shield 50% max HP, after 4 s without
     hits refills 2% per 0.1 s; Legion = allies nearby +50% HP, +50% attack, 20% bigger, no stagger/knockback;
     Plate Armor = evades the first 10 hits.
   - Open: translations (95 strings + 15 elite texts x 10 languages), Umut's first look in game.
2. ~17:15 Translations (commit after a69dce4): 93 interface texts in all 10 languages (tools/lang/lang.py merge),
   the 15 elite descriptions in codex_content (11 languages). Terms taken from the game where it has them: dark
   enemy = de "dunkler Feind", fr "ennemi sombre", ko "검은 적", ja "闇の敵", zh "黑暗敌人/敵人". Glyph swaps for the
   game fonts: ko 쌉 찹 (감싸고 있습니다, 채워집니다), zh-Hans 侧 灼 础 阅, zh-Hant 灼 礎 閱 (燃烧, 基本, 查看).
   **[DECISION]** ko/ja/zh section names: Bestiary = 몬스터 / モンスター / 怪物, Arsenal = 무기고 / 装備 / 军械库.
   335 tests pass; Release build OK; Debug build deployed.
3. ~17:20 Handed to Umut for the first in-game look. Not built yet (next, after his feedback):
   - Damage-scan leftovers (spec step 7): Genie Fairy Lamp, Golden Gargoyle Stone_3, Davy Jones' cannonballs,
     Chosen Mage's Badge, World Tree Seed.
   - **[DECISION]** the player's dark abilities show the game's own numbers per level (a/b/c in the description);
     computed damage for the damaging ones (Time bomb, Thorns armor, Many hit mark, King slayer) waits for the
     Dark Mirror tab (0.14) unless Umut wants it now.
   - README / wiki / changelog for 0.12, release.

4. 2026-10-07 ~22:45 Damage-scan leftovers (spec step 7, commit 99d89d6). Umut asked about save states first:
   feasible at room start, agreed for **0.12.1** (docs/SAVE_STATES.md). Fixes in GearAnalyzer:
   - ChangeAction: a skill that swaps itself for a hidden "Custom" follow-up action (Genie's lamp punch 250%,
     Minotaurus' Blunt Strike following hit 340%) walks that action. Swaps to real skills (air/enhanced) stay apart.
   - `_endingOperations` (SimpleActionWithEnding) walked with the action: Golden Gargoyle's statue burst (430%
     magic), Glacial Skull's swap shards, Genie_3's dash hit, Living Armor_2.
   - Davy Jones: FireCannonBall walks only the "Normal" cannonball (it listed all 10 types before); PushCannonBall
     "SkillN" walks that special cannonball (Bombard, Drop the Anchor, ...); a second step "Enhanced cannonball"
     (every 3rd reload) with the Enhanced_ type.
   - Items: scripts under `Characters.Gear.Items.Customs` on the equipped object are walked (The Chosen Mage's
     Badge: 3 fireballs 180% magic, 3 explosions 400% when you use a skill; `_fastAttack` skipped, same hit).
   - World Tree Seed: no damage (the tree only buffs): the scan row is a false positive ("damage taken").
   - Scan: skull sections 1266/1338 -> 1273/1339, items+essences 129 -> 130/179. Remaining rows are summons
     (shown in game via ReferencedCharacters) and buff items whose text says "damage".
   - Texts: "Enhanced cannonball" + 19 enemy projectile names the template pulled from Umut's Codex (Dark Mirror run
     2026-10-07), in all 10 languages with the game's enemy names (ko "소닉붐" -> "음파": 붐 not in the font).
     340 tests pass, Release build OK, Debug build deployed.
   - Dev aid: `DUMP_GEAR="weapons/Genie,items/X" dotnet test ... --filter Dump_named_gear` prints a breakdown.

5. 2026-10-11 ~13:15 Hotfixes for 0.12.0 (Umut's reports): settings headings drawn only while wholly inside the
   list (953f8bb: a heading below the list showed over the frame); the mod's keys stay quiet while any text field
   is focused (5194ae4, `UI/TextInput.IsTyping`: typing "caerleon" in the Codex search or in the game's developer
   menu (F2) opened the combat log on L / closed the Codex on K; the old check only knew the Codex search and missed
   fields of other event systems). Both in the 0.12.0 changelog; merged into save-states and deployed.

## Test todos for Umut (first look)
1. Open the Codex (K) in the castle: You / Bestiary / Arsenal chips on top, "< Back" top right.
2. Arsenal → Items: open a found item: description, its 2 inscriptions as red links, numbers. Click an inscription,
   then Back (button, Backspace or mouse back button).
3. Arsenal → Skulls: a skull you held: type, awakening path (links), attacks, skills (numbers only for skills you
   used since this build), swap.
4. Arsenal → Inscriptions: steps (higher steps greyed until reached), items carrying it (links).
5. During a run: K opens on You: both skulls, quintessence, 9 items, inscriptions with "N more for step X", Stats.
   Click each; the right page shows it in full with your current numbers and a link to its Arsenal page.
6. Bestiary → Dark enemies: the 15 abilities ("???" until met). In the Dark Mirror: meet a dark enemy, its ability
   appears; kill it, the numbers appear.
7. Settings → Codex: "Open on You" row; Codex unlocks: dark enemies.
8. Anything that looks wrong: screenshot. Log: errors with "Codex" in BepInEx/LogOutput.log.
