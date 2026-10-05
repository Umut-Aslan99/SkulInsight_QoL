# Damage scan (0.12 planning, CODEX_PLAN C "full scan")

Made 2026-10-05 01:30 by `CoverageTests.Damage_scan_lists_gear_without_numbers` over the full gear scan
(`C:/Users/Umut/SkulModding/GearScan`). Re-run it (writes `DamageInsight.Tests/bin/Debug/damage-scan.tsv`) after
analyzer changes to see progress.

## Result
- **Items + quintessences that deal damage: 129 / 179 show damage numbers.** Of the 50 "gaps", ~40 only *raise*
  damage (Approaching Death, Hope Slasher, Veiled Mask, ...: nothing to calculate, correctly no numbers). Real gaps
  (~10), nearly all **summons**: The Chosen Mage's Badge (fireballs), Imp, Dwarf, King Dwarf, Face Bug, Harpy (charge
  form), Hydra (acid breath), World Tree Seed (World Tree), maybe Vampire Fang (stake, Wound) and Sphinx (riddle
  trap). Their damage is in the summoned creature (scan folders `characters/`, `linked/`), which GearAnalyzer does
  not follow yet.
- **Skull sections (basic, jump, skills, swap, dash, active): 1208 / 1331 with numbers.** 90 gaps on real skulls
  (the rest are test / reference / skin prefabs players never get), all skills:

| Mechanism (guess) | Skulls and skill keys |
|---|---|
| Charges / rushes (hits while moving) | Skeleton-Pike line RushReady, RushRush, EndlessRush (_2.._4); Harpy HighSpeedFlight, RushRush; Minotaurus CowStamp, HeadButt (_2, _3); Samurai / Neo Samurai DodgeSlash; Nightmare HellBike_3; Slime SlimeJump |
| Shields | Skeleton-Shield line ShieldBash, ShieldRush (_2.._4); Prisoner AssaultShield; The King AssaultShield_2.._6, Rampart_2.._6, FrontLineShield_2.._6 |
| Summoned helpers | Carleon Recruit line BuddyRecruit, BuddyArcher, BuddyAssassin, BuddyManAtArms (_2.._4); Davy Jones Bombard, DropTheAnchor, ReadyToShoot (PrepareForBattle may be a buff) |
| Grabs | Ent Skull line NaturesGrab (+ _Enhanced, _2.._4) |
| Combos | Champion FlashStep, Combination, DirtySmash (+ _Rage) |
| Other / maybe no damage | Skul / Hero Little Bone Rebone (teleport to the head), Devil Berserker BoneHowl_2 (howl buff?), Genie FairyLamp, Golden Gargoyle Stone_3 |

## Update 2026-10-05 01:45: missed motion fields fixed
GearAnalyzer.MotionsOf now also reads `_parryMotion` (ParryAction counter hit), `_attackMotion`/`_secondMotion`
(AttackHitTriggerAction: Minotaurus Stomp/Headbutt), `_startMotion`/`_endMotion`/`_fullStreakEndMotion` (StreakAction),
`_landingMotion` (PowerbombAction, singular) and `_grabMotions` (GrabAction). Skull sections with numbers:
**1208 → 1262 / 1338** (skills 727/803); real-skull gaps 90 → 48. New step labels translated (Dash parry, Fighter
dash parry, New swap). Not added on purpose: ParryAction `_waitingMotion` (stance), GrabAction `_grabFailMotions` /
`_maintainMotions`, EnhanceableChainAction `_enhancedMotions` (a variant: would mix normal and enhanced hits).

Remaining real gaps (string table descriptions checked): summoned helpers (Carleon Recruit line Buddy*, Davy Jones
Bombard / Drop the Anchor / Ready to Shoot, The King DoubleCrossbomatic), Champion Combination / Dirty Smash (+Rage),
Genie Fairy Lamp, Golden Gargoyle Stone_3, Nightmare HellBike_3, Slime jump, shield rush (_2/_3). Correctly empty
(buffs/moves, no damage): Forward Rush (RushRush), Forward March (EndlessRush), High-Speed Flight, Rebone, Prepare
for Battle, Bone Howl.

Later the same night: GrabAction `_maintainMotions` added too (the Champion's Combination / Dirty Smash hit while
holding): 1266 / 1338. **Summons are not a real gap in game**: GearDescriptions.Analyze analyzes every character a
gear references (ObjectGraphWriter.ReferencedCharacters) and shows its damage; only this scan test doesn't link them
(Recruit buddies, Imp, Dwarf turret, Hydra, Davy Jones' crew, The King's DoubleCrossbomatic). To verify in game.
Remaining real gaps: Genie Fairy Lamp, Golden Gargoyle Stone_3, Nightmare HellBike_3, Slime jump, Shield rush,
Chosen Mage's Badge, World Tree Seed.

Findings for the last real gaps (02:10): Nightmare HellBike_3 (SimpleAction, path Equipped/OriginalBody/
Skill_HellBike) and Devil Berserker BoneHowl_2 (Equipped/Skill_Demonization) have no attack parts under the skill:
they turn into another body; the hits are in the polymorph weapon (scan GhostRider_3_Polymorph / Berserker_2_Polymorph)
→ follow the polymorph target. Davy Jones Bombard / DropTheAnchor (ChainAction, 0 attack parts): the special
cannonball is a projectile prefab outside the skill (look in linked/). Golden Gargoyle Stone_3: 0 parts under
Equipped/Skills/Stone; the damage happens when the statue form ends (an ability/operation on detach?). Genie FairyLamp
(ChainAction, 1 attack part, no hits): the punch is the second step started by pressing again; check how the chain's
second motion is reached. Correctly empty: SlimeJump ("You jump."), ShieldRush (concentration speed buff).

Transformations, exact path (01:50): the skill's motion runs `Characters.Operations.StartWeaponPolymorph` whose
`_polymorphWeapon` is the transformed Weapon (GhostRider_3: GhostRider_3_Polymorph / _Swap_Polymorph; Berserker_2:
Berserker_2_Polymorph). The walker stops at nested "Weapon" nodes on purpose, and the polymorph weapon's actions are
not in the skull's object graph (they live on the polymorph weapon's own objects; the dev scan saves them as separate
files). Fix idea for 0.12: like summons (`ObjectGraphWriter.ReferencedCharacters` → `GearDescriptions.Analyze`),
collect referenced polymorph weapons, analyze each as a weapon and show its attacks under the skill ("Transformed:
Basic attack ..."); in the scan test, link `<skull>_Polymorph.json` by name.

## Next steps (0.12)
1. Look at one skill per mechanism in the scan JSON (which operation types carry the hit) and teach GearAnalyzer
   that pattern; check with this test (numbers must go up) and DescriptionFormatterTests.
2. Summons: follow the summoned character / OperationRunner prefab (scan `characters/`, `linked/`) and add its hits as
   a section ("Summon: Imp").
3. Dark abilities: no scan data at all yet (needs a scan of the Dark Mirror ability prefabs first).
