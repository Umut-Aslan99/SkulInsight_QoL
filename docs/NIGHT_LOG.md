# Night log 2026-10-05 (unattended work session)

Running log, updated after every step, so an auto-compaction or a new session loses nothing. Newest entries at the
bottom of "Steps". Decisions Claude made for Umut are marked **[DECISION]**: Umut may change any of them.

## Setup (read first when resuming)
- Umut is asleep. Plan (his words, 2026-10-05 ~01:00): keep working on everything alone (0.11 finish, then the
  backlog: 0.12+), decide open questions and mark them, write test todos for the morning, document stepwise, stop
  before the session limit, then shut the PC down.
- **Usage watcher**: `C:\Users\Umut\ClaudeNight\usage-watch.ps1` runs hidden (started 01:00, PID 7656 at 01:14):
  screenshot of the top-left usage panel every 5 min → `C:\Users\Umut\ClaudeNight\usage_latest.png` (Read it to see
  Session (5hr) % and Weekly %), keeps the PC awake. Watchdog: touch `C:\Users\Umut\ClaudeNight\heartbeat.txt` at
  every usage check; if it is older than 45 min the PC shuts down (5 min warning, `shutdown /a` cancels). If the
  watcher is not running (check: process with "usage-watch" in its command line), start it again:
  `Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','C:\Users\Umut\ClaudeNight\usage-watch.ps1','-CropWidth','360','-CropHeight','320'`
- **Stop rule [DECISION]**: wrap up at Session ≈ 90% (Umut said max 95%) or Weekly ≥ 85% (was 62% at 00:46), then:
  update PROJECT_STATUS START HERE + this log + morning todos, commit, run
  `powershell -File C:\Users\Umut\ClaudeNight\shutdown-pc.ps1` (stops the watcher, shutdown in 5 min).
- Usage readings: 00:46 27% / 62%; 00:58 35% / 63%; 01:05 41% / 64%; 01:10 (see shot). Cost ≈ 0.3 % per tool call
  at ~130k context: work in few, large steps.

## Steps (newest last)
1. 00:20-00:45 Film review finished for every filmed boss (see FILM_REVIEW.md "Review complete"); commits up to ffca6b4.
2. Clean films (dcb0952): setting Codex/CleanFilms (on). Films hide damage numbers (FloatingText), the player's
   skill/item effects and hit sparks (effects started while the game acts for the player: FilmClutterPatches hook
   OperationInfos.CRun MoveNext + the 4 SpawnOnHitPoint hit-effect classes + EffectPool.Play), the player's
   OperationRunners (summoned item fields) and minions, and status looks on the boss (all RuntimeAnimatorControllers
   in CharacterStatusSetting + CommonResource stun/freeze). Not covered: particles, status colour tint on the boss
   sprite. PatchTargetTests now understand TargetMethod(s). Untested in game.
3. Non-moves (55bbaa7): entrances/sleep/deaths (FightRecorder.IsMove false) are no longer filmed; migration
   "0.11 no entrance films" drops the existing ones once (12 films, 57 MB here). Book and counter already ignored them.
4. Projectile picture labels (924ce61): full projectile name instead of 14 chars + "..."; Umut's 21 cut sets moved to
   `Codex/_cut_projectile_backup_20261005-0100` (recaptured when those enemies shoot again); 34 new names from the
   2026-10-04 22:20 DM run translated (all 10 languages).
5. Compact films (2b164a2): setting Codex/CompactFilms (on): new takes saved as JPG q85 (measured 97 MB → 23 MB on 20
   sheets, ~23 %). **[DECISION]** quality 85; existing PNG takes stay until refilmed or the Codex is reset.
   Umut's films: 1.4 GB now (all PNG).
6. Reset Codex (done 01:20, see commit after 2b164a2): settings page action "Reset Codex" in the Codex section; confirm dialog; writes
   Codex/reset-requested.txt; at the next game start CodexReset.RunIfRequested (before CodexMigrations) deletes
   progress.json*, Replays, Animations, Hints, Debug and "_*" backup folders, keeps migrations.txt; selecting again
   withdraws. Notes stay (they are in the DLL). Test Codex_reset_deletes_the_gathered_data_and_keeps_the_rest.
   Translations done (zh: 清除, the fonts lack 删/刪; zh-Hant Codex = 圖錄). 304 tests pass. Deployed.

7. 01:30 Damage scan (0.12 prep): new test CoverageTests.Damage_scan_lists_gear_without_numbers → damage-scan.tsv;
   summary + mechanism table in docs/DAMAGE_SCAN.md (items/essences 129/179 with numbers, real gaps ~10 = summons;
   skull sections 1208/1331, 90 real gaps: rushes, shields, summoned buddies, grabs, Champion combos).
   Usage 01:20: 49 % / 65 %.

8. 01:45 GearAnalyzer reads the motion fields the scan found skipped (parry counter hits, Minotaurus stomp, streak
   start/end, powerbomb landing, grabs): skull sections with numbers 1208 → 1262 (real gaps 90 → 48). 3 new labels
   translated. 305 tests pass. **[DECISION]** enhanced variants (`_enhancedMotions`) stay out (would mix hits).
   Morning: check descriptions of e.g. Skeleton-Shield "Shield Bash", Samurai "Evading Slash", Ent Skull "Nature's
   Grip", Minotaurus "Stomp": they should now show damage numbers.

9. 01:55 Changelog 0.11 draft updated (ec9a077). GrabAction hold motions read too (Champion): 1266/1338. Summon
   "gaps" are only a blind spot of the scan test (runtime analyzes referenced characters). Usage 01:47: 60 % / 67 %
   (≈0.5 % per call).

10. 02:05 Log noise: the boss analysis probed component types with AccessTools.Method(type, "CRun"), HarmonyX
   logged a warning per miss (268 in the 22:20 log). Quiet lookup AttackGraph.FindMethod. Usage 02:00: 65 % / 67 %.

11. 02:15 docs/V012_SPEC.md: 0.12 spec (scope, entry pages, unlock rules, character page, short descriptions,
   build order, test plan) with every choice marked [DECISION]; DAMAGE_SCAN.md findings for the last gaps (polymorph
   skills, cannonballs, statue, lamp).

12. ~01:40 (clock was misread earlier: steps 9-11 were 01:25-01:35, not 01:55-02:15) PROJECT_STATUS START HERE
   rewritten (e9cfa53), memory skul-resume-plan updated, README + Thunderstore wiki prepared for 0.11 (features,
   config rows, known gaps, coming next). Version line in README still says 0.10.2: change at release.
   Usage 01:40: 70 % / 68 %.

13. ~01:55 Transformation skills: ObjectGraphWriter.ReferencedWeapons collects weapons outside the skull's hierarchy;
   GearDescriptions analyzes those named "*Polymorph*" like a skull and the skull's passive description gets
   "Transformed by a skill: / Transformed by the swap:" blocks (basic, jump, dash, skills of the transformed body).
   Guarded (Build try/catch); untested in game. **[DECISION]** shown in the skull description (not under the skill),
   because the mod can't yet tell which skill starts which body. Texts in all 10 languages.

14. ~02:00 Scan test counts transformed bodies: 7/7 have attack numbers (GhostRider_3 + swap, Berserker_2,
   Dominator, KingArthur, Wizard_4, Yaksha), so step 13 should show numbers for all of them in game.
   Usage ~01:58: 76 % / 69 %. Wrapping up at ~78 %: the rest of the budget stays as a safety margin.

15. ~02:08 (0.12 item, after the planned end) Short descriptions (GitHub issue #3): setting Descriptions/Short
   (**[DECISION]** off by default): popups show one line per attack part with the total ("4 hits = 202–317
   Physical") instead of every hit's calculation (DescriptionFormatter.Short / ShortHits; set per Build). Settings
   row + texts in all 10 languages; test Short_descriptions_show_only_the_totals. 306 tests pass. Deployed.

## End of the night (~02:12, Session 81 %, Weekly 70 %)
Everything is committed (last commits: ce58bc3 and this log) and installed in Umut's game (Debug build). The PC is
shut down by `C:\Users\Umut\ClaudeNight\shutdown-pc.ps1` (5 min warning). Next session: read PROJECT_STATUS START
HERE, then this log.

## Decisions made tonight (Umut: change any of them)
1. Stop rule: wrapped up at ~78 % session (margin to the 95 % Umut allowed; the next task was a larger design change).
2. Clean films and Compact films are **on** by default; compact = JPG quality 85; existing PNG takes stay until
   refilmed or the Codex is reset.
3. Reset Codex deletes at the next game start (not immediately) and also removes the dev backup folders ("_*").
4. Entrances, sleeping and deaths are never filmed (they were never shown).
5. Analyzer: enhanced variants (`_enhancedMotions`) are not mixed into normal hits; parry waiting stance, grab misses
   are not counted; grab hold motions are.
6. Transformed bodies are listed in the skull's passive description ("Transformed by a skill / the swap"), not under
   the specific skill (no mapping skill → body yet).
7. 0.12 scope and page rules: see docs/V012_SPEC.md (records/achievements → 0.12.1, neutral numbers on entry pages,
   short descriptions setting off by default, ...).

## Morning todos for Umut
1. Start Skul. The log (`BepInEx/LogOutput.log`, Claude reads it) should once say "removed the entrance, sleep and
   death films of N bosses". The ~270 "Could not find method" warnings should be gone.
2. Settings page → Codex: rows "Clean films", "Compact films", "Reset Codex" with help texts (try one other language).
3. Fight a boss with poison/burn items or a summon: the new take (Codex/Replays/<boss>/clip_*.jpg) should show no
   damage numbers, no own effects, no poison clouds. Does the JPG look fine in the book?
4. Skull descriptions: Shield Bash, Evading Slash, Ready to Charge (counter hits), Nature's Grip, Minotaurus' Stomp /
   Headbutt, Champion's Combination / Dirty Smash should now show damage numbers; Nightmare and Devil Berserker show
   "Transformed by a skill:" blocks (also King Arthur, Archmage, Yaksha, Dominator).
5. Decide: full Codex reset now (Settings → Codex → Reset Codex → restart) so all films are re-taken clean and compact
   (frees ~1.4 GB of old PNG films + ~420 MB of old backups).
6. King Alexander retest (one fight through all forms), then release 0.11.0 (Claude runs the release script).
7. Read docs/V012_SPEC.md and change any [DECISION] you don't like.
8. Settings → Descriptions → "Short descriptions" on: skull/item popups show one total line per part. Keep it off by default?

## Day session 2026-10-05 (Umut testing, Claude on call)
16. ~11:00 Umut reached Dark Skul phase 2 (37/46 moves filmed); the 9 missing were Balance-form far-range moves
   (Bone rain, Dash, Throw head, Jump throw head, Execution, Head hunting) plus Melee attack, Hard smash and Stamping center.
   PreferNewMoves couldn't reach them: it never steered the form change (a form's moves are not below its switch node),
   never passed distance checks, and the form change waits on a 30 s cooldown.
   **Move director** (Codex/MoveDirector.cs + .Live.cs, hooks in PreferNewMovesPatches): for BehaviorDesigner bosses
   (Dark Skul 1/2, First Dark Hero, adventurers) PreferNewMoves now plans the way from the tree root to the next missing
   move: Selectors start at the child on the way, WeightedSelector/OneByOneSelector take it, gating checks on the way
   (distance, wall, HP, ChronometerCoolDown, CanUseChronometerCoolDown, RandomProbability; also inside
   ConditionalEvaluators) return what the way needs; a check on a value the tree sets (IntComparison/BoolComparison:
   Dark Skul's form, First Dark Hero's step) is reached through the SetInt/SetBool that sets it (the form change runs
   first). Real-event checks (Grabbed, IsNullSharedVariable = head thrown, CanUseAction, CheckWithinSight, IsGrounded)
   are left to the game. Moves come one after another in the book's order, fewest form changes first; a move that
   doesn't come within 30 s (or the boss starts nothing for 8 s) is put aside for 90 s. Log lines "Move director: ...",
   move counter shows "Director: next X (first Y)". **[DECISION]** no new setting: PreferNewMoves (dev) does it; block
   AIs (First Hero, Pope) keep the old steering. 2 projectile names from the morning run translated. 315 tests pass.
17. ~12:00 Umut: the director for every boss. Three ways, all under Developer/PreferNewMoves:
   - BehaviorDesigner trees (step 16): Dark Skul 1/2, First Dark Hero, King Alexander (heart/phases), adventurers.
   - Behaviour blocks (Codex/MoveDirector.Blocks.cs): Yggdrasil, Pope, First Hero 1-3 (SimpleAI). The AI's top blocks
     are found through its fields (also Pope.Sequence's Phase1/Phase2); Selector/Sequence/WeightedSelector/
     UniformSelector/RandomBehaviour are led, Conditional/Chance/CoolTime/Count are gates (forced: HealthCondition,
     CoolDown, BehaviourCoolTime, BetweenTargetAndWall, CompareDistanceFromWall, CheckCollision, TargetIsGrounded;
     left alone: BehaviourResult, BreakedDarkCrystal, CanChoice, CanPurify, CanStartAction, MonsterCount, ...).
     A way counts only if one of its blocks started in the last 20 s (BehaviourInfo.CRun hook): blocks run from the
     AI's own loop, Pope's phase 2 can't be reached from phase 1. First Hero's combo system picks combos itself.
   - Coroutine AIs (Codex/MoveDirector.Coroutines.cs): Leiana sisters, Awakened Leiana, Chimera. Their attack
     coroutines (Cast...) get a prefix: an attack the AI starts itself (not inside another attack: AttackTracker now
     keeps the stack of running tracked MoveNext calls, InsideAttack()) that is filmed already becomes the first missing
     attack of the same kind (same dispatcher, or the same " · " fight part for the sisters, whose conductor is the
     master AI); arguments carried over by type ("left"), else defaults. 3 swaps without a film: put aside 90 s.
   **[DECISION]** forcing HP checks lets a block boss do later-phase moves early (inside the same AI); swapping can
   break Chimera's scripted wreck sequence while moves are missing. 321 tests pass. Untested in game.
18. ~13:30 Review 2 of all films after Umut's full run (docs/FILM_REVIEW.md "Review 2"): takes match the notes for
   every boss filmed in natural play; the director's takes (Dark Skul 2's last 10, King Alexander heart) were rushed
   (cut or running into the next move). Fixes: pacing (MoveDirector.Pacing.cs: wait for each led/swapped move's take,
   pause a BehaviorDesigner tree from the move's end until the take is saved, max 10 s); First Dark Hero's operations
   all named "Operations" got merged into "Dark" (BossAttacks.StepKeyOf: generic operation names keyed by object);
   10 heart operation names translated (Buzz saw, Drill missile, Chase laser, ...). 321 tests pass. Open: heart move
   model (Umut decides), notes for the heart (11) and 5 other moves, retake Dark Skul 2's 10 and the heart with pacing.
19. ~14:30 Umut: agreed to finish (definition of done: every move listed, seen, described; films are evidence only).
   **[DECISION]** heart moves stay as listed now (operation + pattern as one move; split possibly in 0.12). Notes
   written for 17 moves in 11 languages (heart 11: Chase/Enhanced chase/Curtain/Targeting laser, Heart beat, Drill
   missile, Dark ring, Dark bouncy ball, Bouncy ball, Oil ball, Buzz saw; Dark Skul 2 Unstoppable; First Dark Hero
   Summon thorn, Dark orb fragments, Dark blast wall, Dark orb (the last three from the AI only, check on the next
   fight); Leiana single Meteor in ground 2). First Dark Hero's labels after the step-key fix checked offline (dump
   with distinct keys). Supporting Thief/Warrior: no notes (never on camera), listed as known gap. Version 0.11.0:
   csproj, CHANGELOG, README, wiki; package zip builds. Waiting for Umut's go to publish.
