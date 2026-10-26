# 0.15 step log: rewards

Running log for building 0.15 (spec: docs/V015_SPEC.md, Umut's answers in its §F). Newest step last. Kept up to
date after every step so a session can resume from here.

## Facts found (2026-10-25)
- **Stats**: `Stat.Values(new Stat.Value(Stat.Category.PercentPoint, Stat.Kind.X, 0.01))` = +1 % (the Witch's
  bonuses do it this way); `player.stat.AttachValues / DetachValues / Contains / SetNeedUpdate`. Final for Percent
  kinds = Percent × (1 + Σ PercentPoint); Constant kinds (MovementSpeed) = Constant × Percent × (1 + Σ PercentPoint)
  + Fixed; **TakingDamage is a Product kind**: only Category.Percent counts, values multiply (0.97 = -3 % taken).
- **Gold**: `GameData.Currency.gold.multiplier.AddOrUpdate(key, percentPoint)` (CurrencyBonus, StoneMask); Earn()
  multiplies by `multiplier.total`.
- **Damage to one target**: `player.onGiveDamage.Add(priority, (ITarget target, ref Damage d) => ...)` with
  `d.percentMultiplier *= x` (AirAndGroundAttackDamage does it); return false = don't cancel. Summons forward to the
  player's onGiveDamage (Minion.cs), so they count.
- **Little Bone** is the weapon named "Skul" (HeroClearTracker compares `inventory.weapon.current.name`).
- **Breaking gear**: Item.OnDestroy / Weapon.OnDestroy fire `_onDiscard` and, if `destructible` and the player is
  alive, the destroy effect and the currency (dismantling, and gear left lying when the map is left: Umut).
  Upgraded skulls: `Weapon.nextLevelReference`; the base skull = the first tier of CodexPages.AwakeningPath.
- **Adventurer drops** (tools/unity/adventurer_drops.py): each adventurer's corpse has an AdventurerGearReward with
  7 % chance and three items (weights 60 / 30 / 10, the third only in the Dark Mirror, legendary, not obtainable
  otherwise): Warrior → WarriorsIronMace, WarriorsPauldrons, **TheChosenWarriorsArmor**; Cleric → ClericsVeil,
  ClericsCrossStaff, **TheChosenClericsBible**; Hero → HerosGlove, HerosHolySword, **TheChosenHerosCirclet**;
  Magician → MagesNecklace, MagesManaBracelet, **TheChosenMagesBadge**; Archer → HuntersBoot, HuntersWoodenLongbow,
  **TheChosenHuntersArrow**; Thief → ThiefsWarmer, ThiefsBlackIronDaggers, **TheChosenThiefsTwinSwords**.
- **Omen items**: `Gear.Tag.Omen` (8) / `UpgradedOmen` (16) in `gearTag` (also on GearReference); 21 in the scan,
  all "Unique", not obtainable from normal drops.
- **Inscription activations**: the Dark Mirror's banner (InscriptionSynthesisEquipmentSlot.Select) does
  `inventory.synergy.inscriptions[key].bonusCount += 1; inventory.UpdateSynergy()`, only for inscriptions you have
  (count ≥ 1), never SunAndMoon, Masterpiece, Sin, Omen.

## Steps (newest last)
1. **Catalog, store, rules** (plain logic, tests/RewardTests 16): Rewards/RewardCatalog (a reward for each of the
   103 achievements as in the spec's table: boss ×1.03 / no hit ×1.10 by the achievement's boss page, WorldsBest
   only in the Dark Mirror; Dark Mirror levels +1 % physical and magic; 44 legendary skulls +0.5 % by type; groups
   +1 %; specials; Sum: stats add, targets multiply; texts "Damage to Yggdrasil +3 %"), Rewards/RewardStore
   (Codex/rewards.json per slot: coins per gear id, Omen coins, activations, adventurer kills toward the next coin,
   quests [kills, done] per enemy, what quests paid, the run: summons used, inscriptions raised, inscriptions maxed),
   Rewards/RewardRules (adventurer of a name, its The Chosen item, base skull, quest step = runs × kills per run,
   10-150, DEV 1; a dark enemy counts 2). tools/unity/adventurer_drops.py.
   [DECISION] Adventurer coins summon The Chosen item in any run (normally Dark Mirror only); the coin is the reward.
   [DECISION] Omen coins: one per Omen item held when a run ends (won or lost); redeemed for an Omen item the slot
   has met. Coins of every kind share the per-run summon limit.
2. **Achievement rewards in the game + settings**: Rewards/RewardApplier (ticked from Plugin.Update): one
   Stat.Values on the player (PercentPoint for attack, attack speed, movement, cooldowns, crit chance / damage,
   healing; TakingDamage as a Percent product 1 - x), re-attached when the slot's achievements or the settings
   change, detached when off; the gold multiplier (AddOrUpdate / Remove with our key); a GiveDamage handler on the
   player (`percentMultiplier *= x`) for the boss pages (all phases and the sisters via the new
   CodexCatalog.BossPageOf; WorldsBest only while CodexMode.DarkMirrorNow), adventurers (Character.Type.Adventurer)
   and holding Little Bone ("Skul"). The combat log's calculation names it "Achievement rewards" (OwnerNames).
   Never in the daily slot or while the QA capture plays (RewardApplier.On). Settings section **Rewards, first on
   the page**: Enabled, AchievementRewards, Coins, Quests, InscriptionActivations, SummonsPerRun 1 (0-3),
   ActivationsPerRun 2 (0-3), QuestRuns 2 (0.5-10), QuestDarkQuartz 10, QuestGold 30, AdventurerKills 5.
3. **Bestiary rewards in the game**: Rewards/RewardService: run id = "seed:" + the save's random seed (a continued
   run keeps it; RewardStore.BeginRun); coins from Patches/RewardPatches (Item / Weapon OnDestroy prefix: dropped,
   legendary, destructible, player alive, in a run, not a shop ware (price > 0), not summoned this run; skulls by
   their first tier); kills from CodexTracker's kill count → adventurer coins (every 5th win over one adventurer →
   its The Chosen item) and quests (normal enemies only, dark enemies count 2, gold and dark quartz dropped where
   it died, a floating "Quest done"); Omen coins at the end screen (RunHistoryPatches: one per Omen item held, not
   summoned ones); inscription activations earned when an inscription is at its top step (once per run, not one
   we raised) and put back on after a continued run. Using: Summon (only in a run, after the map is cleared, the
   per-run limit; the gear drops at your feet), SummonOmen (an Omen item the slot has met), Activate (+1
   bonusCount like the Dark Mirror banners, not SunAndMoon / Masterpiece / Sin / Omen, per-run limit).
   [DECISION] Summoned gear earns no coin back in its run (else one Omen coin would give a free Omen every run).
   [DECISION] Summons wait until the map is cleared (no boss room check needed, never mid-fight).
   Test todo: does the game reset `bonusCount` between runs (the Dark Mirror banner re-applies at each start, so it
   should)? The log shows our activations; check that a new run doesn't keep the last run's +1.
4. **The book**: You → **Rewards** tab (CodexCategory.Rewards, CodexWindow.Rewards using the Records tab's rows,
   NewRecordRow now takes an id): four rows (Achievement rewards "12 / 103" + the two biggest bonuses, Coins held,
   Quests done, Activations held) and their pages (Rewards/RewardPages, plain + RewardContext): totals in every run,
   per boss / adventurers / Little Bone (links), every achievement by group (earned ink, the rest dim, veiled
   spoilers "???"); coins with **Summon** links (dim with the reason when they can't be used), Omen coins with the
   met Omen items, adventurers' progress bars → their The Chosen item, how coins are earned; quests in progress
   (bars, done N times); activations with "+1 for this run" links. Links "rewards:summon:/omen:/activate:" are
   actions (FollowLink → RewardService.Use; from a gear page it opens Rewards → Coins with the result). Achievement
   pages: a Reward section (and whether it counts now); the notice: a green "Reward: ..." line (box 22 px taller).
   Enemy pages: "Kill quest 37 / 50 · done 4 times" (adventurers: "Adventurer coin 3 / 5"); legendary skull / item
   pages and The Chosen items: a Coins section with Summon. Tests: RewardTests 19.
5. **Translations, docs, version (night 2026-10-25/26, NIGHT_LOG steps 2-3)**: ~124 texts in 10 languages, What's new
   for 0.15, version 0.15.0, CHANGELOG / README / wiki / store description, inscription activations tracked per
   inventory (the game never resets bonusCount), DEV F11 test rewards, DEV quest step 3. 548 tests. Debug build
   deployed. The Thunderstore flag from 0.14.1 is merged (0.15's package passes the no-updater check).
6. **Umut's answers to the night's decisions (2026-10-26 morning)**: 1-3 fine (the full mod manager stays in the
   source behind `#if !THUNDERSTORE`, Debug builds have it; a separate GitHub build with it: later, not yet).
   [Umut] "only dark mirror then": the adventurers' The Chosen items are summoned in Dark Mirror runs only (their
   coins are earned anywhere). [Umut] Omen coins "through a button in the omen items pages": each Omen item's Codex
   page has an Omen coins section with Summon (the Rewards tab keeps its list). [Umut] "We should allow for seperate 1
   summons. One skull, one Item, etc. ... different coin types should exist (legendary item coin, legendary skull
   coin, adventurer coins (specific to each adventurer))": RewardRules.CoinKind (Skull, Item, Adventurer, Omen; by
   the gear id), RewardStore counts summons per kind (rewards.json "summonsThisRun": {"Skull": 1}), the per-run limit
   (setting Summons per run, 1) applies to each kind; the Coins page has a section per kind. README / wiki say the
   rewards are optional and can be turned off. 549 tests (+1). Debug build deployed 09:18.
   [DECISION] Adventurer coins form one kind: one adventurer's item per run (each adventurer still has its own coin);
   six The Chosen items in one run would be too strong.

## Test todos for Umut (0.15, the Debug build is installed)
1. Esc → SkulInsight QoL: **Rewards** is the first section (Rewards, Achievement rewards, Coins, Kill quests,
   Inscription activations, the numbers). Help texts readable in your language?
2. Codex → You → **Rewards** (5th tab): four rows. Achievement rewards: "In every run" totals, "Against" (bosses with
   links), then every group with your achievements (earned dark ink, others dim, spoilers "???").
3. An achievement's page: the **Reward** section. DEV F7 (the notice test): the notice has a green "Reward: ..." line.
4. In a run: You page / stats higher than before (physical attack etc.); hover a hit on a boss in the combat log: a
   line "Achievement rewards" with its multiplier.
5. Kill normal enemies: every 3rd kill of one kind (DEV) → "Quest done: ..." above you, gold and dark quartz drop
   where it fell; its Codex page shows "Kill quest 1 / 3 · done N times".
6. F11 in a run: test coins. Rewards → Coins has a section per kind of coin. **Summon** is dim with "Clear the map
   first." while enemies are left; after the map is cleared the skull drops at your feet, and an item coin works too
   in the same run (each kind has its own summon); a second skull says "1 summon per run: used.". The Chosen
   Warrior's Armor (an adventurer coin) only in a Dark Mirror run (outside it says so). Omen coin: an Omen item's own
   Codex page has a Summon too (DEV lists all Omen items).
7. Rewards → Activations: "+1 for this run" on an inscription you have → its step goes up. Quit to the menu,
   continue the run → still +1 (the log says "inscription activations of this run on this inventory"). The next run:
   back to normal.
8. Dismantle (or leave behind) a legendary item or skull → "Coin earned: ..." (not for shop wares).
9. Turn Rewards off → the stats go back, the tab says off. In the daily slot nothing applies.
10. A run's page: "Played with rewards on (N quests done, M coins earned)."
Afterwards Claude reads BepInEx/LogOutput.log ("Rewards:" lines).
7. **Kill quests as squares (Umut 2026-10-26 with a drawing)**: [Umut] "each unit should have its own progress bar in
   form of a square that fills up with a blue bar and the reward shows in the middle (+10 quartz or gold or
   whatever)", "i do like it with the summary". New page block PageBlocks.Meter (CodexWindow.AddMeterRow): a 108 px
   square filling up blue from the bottom, the reward in its middle with the game's own currency icons (its TMP sprite
   set holding "Others/DarkQuartz_Icon" / "Others/Gold_Icon"; plain coloured numbers if it isn't found), the quest
   next to it ("Kill quest 2 / 14 · done 1 time", "Every 14 kills: 10 dark quartz and 30 gold."). On every normal
   enemy's page under its numbers; adventurers' pages get "+1" toward their coin. The Rewards tab keeps the summary.
   [Umut] "the ratio is quite unbalanced. Example: Ent 58 kills, Root Ent 12 kills": the release build already scales
   each quest by how many of that enemy a run has (Ent 44, Root Ent 14, Blossom Ent 13, Forest Keeper 10 kills);
   only the DEV step was a flat 3. DEV now uses a fifth of the real step (at least 2: Ent 9, Root Ent 3, Forest
   Keeper 2), so the ratio shows in tests. 550 tests (+1).
8. **Quests pay more, with an estimate (Umut 2026-10-26)**: [Umut] "set the base to 25 quartz + 50 gold, 2 runs worth
   as default. I want the rewards to be felt, and 1075 gold is not too bad." Defaults QuestDarkQuartz 25, QuestGold 50
   (QuestRuns stays 2). The Kill quests page now says "About 21 quests in a full run (all five chapters): about 535
   dark quartz and 1,075 gold." (RewardRules.QuestsPerRun: each normal enemy's kills per run from the level analysis
   over its real step; follows the settings; real steps also in DEV builds). README / wiki / CHANGELOG / spec updated.
   550 tests. Umut's own config still had 10 / 30 (written at the first start): set to 25 / 50 with the game closed.
9. **Fixes from Umut's test (2026-10-26)**: (a) [Umut] the same inscription could be activated twice ("Courage,
   Courage"): each inscription once per run (ActivationChoices leaves out raised ones; Activate says "Already raised
   in this run."). (b) [Umut] "only the first time it reaches 4, it should give a coin at the end of the entire run
   (win or lose)": NoteMaxed only records; RewardStore.PayMaxed() at the end screen (death or ending) pays one
   activation per inscription at its top in that run, once (maxedPaid in rewards.json); the floating text says "...
   when this run ends!"; the Activations page lists the ones waiting. Umut's "2 tokens" were Courage + the F11 test
   activation (rewards.json: maxedThisRun Brave, DevTest0); Arms at 4/6 was not counted. F11 now adds its activation
   directly. (c) **Bug: the Item.OnDestroy patch** made Harmony prepare that method at the start, which ran the
   game's Item.Assets static constructor before CommonResource existed: NullReferenceException, the type broken for
   the session, and every item destroyed afterwards threw TypeInitializationException (destroy effect and the
   dismantle currency lost). Replaced by subscribing to the game's own Gear.onDiscard in a Gear.Awake postfix
   (Patches/RewardPatches); Weapon.OnDestroy patch removed too. (d) Characters no game font has (the log's "× /
   → / • ... replaced by  "): × → x, → → · / », • → ». The ★/☆ of the Codex (tiers, the run star
   chip, "{0} more at ★★★") are missing too since 0.10 (the settings page already writes "3 stars"): still open.
   (e) "InvalidCastException ... UI.UIManager.Update": the game's own bug (Esc on a dialogue casts it to the
   Witch's upgrade panel), harmless. 549 tests.
10. **Daily runs: the same start for everyone (Umut 2026-10-26)**: [Umut] "During normal mode, the npcs that drop the
    items, the skulls and give the blessing before a run starts seem to not give the same items." Found: loading a
    castle draws a new run seed (LevelManager.Load → GameData.Save.ResetAll), and the castle's Fox (skull) and Ogre
    (item) roll their gifts from it in Awake / Start (System.Random(randomSeed + const)); the mod only wrote the day's
    seed afterwards. The Holy Grail's blessing (CutScenes ApplyBlessing.Run) uses `_blessings.Random()`, Unity's shared
    random. Fixed: DailyPatches.DaySeedAtTheCastle (Save.ResetAll postfix: the armed try's seed goes back at once),
    DailyRun.Tick loads the castle once more if it still ran with another seed, DailyPatches.DayBlessing picks the
    blessing from the day's seed. The stage paths (StageInfo.GeneratePath: System.Random(randomSeed) at each stage's
    load, both doors of a pair share their map) and special rooms (reset every run) already follow the seed; if room
    layouts still differ after this, compare two runs' map lists (run pages).
    [Umut] Dark Mirror dailies only with the Darktech up to the day's level: the daily profile's clearedLevel is now
    exactly the day's level (was max(player's, 10)), and in the daily slot DailyRun.Tick sets it and loads the castle
    once, so the game's own DarktechManager.TryUnlock locks the machines above it (Lock = not unlocked, not on).
    [Umut] "its codex entries should all be fully unlocked immediately": CodexTiers.Tier is Max in the daily slot.
    Rewards, achievements and the Witch were already evened / off in the daily slot. 551 tests. Deployed 11:07.
    [DECISION] Darktech of the day's level itself is included (a Dark Mirror 5 day has the machines of levels 0-5).
    Open (needs Umut): the two daily categories (server + mod), DLC gear (see the chat).
11. **Daily boards (2026-10-26, done after the compaction)**. [Umut] six boards a day:
    Normal / Dark Mirror random level 0-10 / Dark Mirror 10, each with and without DLC ("I dont want to give DLC content
    for free"; DLC boards only for owners; without-DLC boards the same for owners). Any daily counts for streak/likes.
    Shoutout: one dim line on the Daily page, "Daily run tournaments: Tythos and his community on Bilibili"
    (https://space.bilibili.com/17576167, link "url:" handled in CodexWindow.FollowLink).
    DONE, NOT COMMITTED: server/daily (store.py rewritten: board column, PK (date,board,player), migration keeps
    runs_v1/likes_v1; days.py BOARDS, CUTOVER 2026-10-26, random_level, board_seed ("normal" keeps the old seed),
    board_info, legacy_board; main.py: /api/dailies, board param on start/finish/board/detail/like/month/admin,
    /api/daily = "normal" for 0.14.1 clients; admin.js shows/deletes by board; 29 pytest pass). Mod: DailyBoards,
    DailyInfo.Board/Dlc + ListFromJson, tries keyed date|board (normal = date), State.Board, DailyService.SelectBoard /
    Days / InfoOf, board on every call, DailyRun.OwnsDlc / DlcBoard / per-board CannotPlay + Play, DLC gear + CheckDLC
    patches by board, board chips + DLC chip on the Daily page (frame moved to y=160), texts in 10 languages.
    Then: zh-Hant "存檔位" → "儲存位" (the font has no 檔); 555 tests, 29 server tests. Today (a Monday) was a Normal
    day in the old schedule, so the runs 0.14.1 players already finished today stay on `normal` with the same seed.
12. **One release: 0.15.0 (Umut 2026-10-26: "I will let you decide")**. [DECISION] Rewards, the daily fairness fixes
    and the six boards ship together as 0.15.0 (0.14.1 is on Thunderstore; nothing of this was released, so no
    0.14.2 / 0.16 split). The server goes first: it is backward compatible (0.14.1 → `/api/daily` = the `normal`
    board, the same seed), and 0.15.0 falls back to `/api/daily` if the server were old. What's new +1 line (daily
    boards, 10 languages), CHANGELOG "0.15.0 (rewards, six daily boards)", README / wiki daily section rewritten
    (boards, DLC rule, the same start, the Tythos line), docs/DAILY_RUN.md "Six boards a day", server README (boards,
    `/api/dailies`, `board` on every call). 555 tests.
    Before the release: Umut tests the Debug build (todos in the chat), then the reviewer ping, then release.ps1.
    Server deployed 10:37 UTC (`/api/dailies` live; the migration kept every run: 2026-10-24's DM 10 runs on `dm`,
    today's on `normal`). [DECISION] month boards are per board too (the old Dark Mirror days count on `dm`'s month).
13. **Umut's test of the boards (2026-10-26) and the last fixes before the release**. [Umut] boards, chips, DLC rule,
    Darktech by level, the same starts: work. Fixes:
    (a) [Umut] the shoutout "hardly ever visible ... should be more personal": its own section **Shoutout** (heart)
    right under "Your try", text "Shoutout to Tythos and his community for using and promoting this passion project.
    Leave him some support on Bilibili." (README / wiki likewise).
    (b) [Umut] after DM 3, the DM 10 daily's skull machine "goes through its very first activation animation": the
    machine of Dark Mirror 7+ (ManufacturingMachineInteractive.isEnhanced) waits for the cutscene
    `강화두개골제조기_Intro`, and the daily slot is copied from slot 3, which never entered the Dark Mirror (all its
    Dark Mirror cutscene flags false; slot 1 has them true). DailyProfile.SeenCutscenes: the 12 Dark Mirror first-time
    scenes ("BoolDataEnumArray/<key>") are set seen, like a save that cleared it (no Dwarf Engineer talks, no machine
    intros). Also: the castle reloads are keyed per board (`t.Key`), so a second board on the same day reloads too.
    (c) "No connection to the daily run server." after a finished run: SendPending posts the start again (409, as
    meant) and Send took every protocol error for a lost connection. Now only no answer or a 5xx sets Problem; any
    answer clears it.
    (d) [Umut] "How many are needed to summon? Hopefully not just one." [DECISION] setting Rewards/CoinsPerSummon,
    default **3** (1-10) for legendary skulls, items and Omen coins; an adventurer's coin stays 1 (it takes 5 wins).
    The Coins page shows "2 / 3" and a dim Summon until there are enough; F11 grants a summon's worth.
    (e) [Umut] "the description on how to earn them is very weird": the Activations page says Earn: / Use: (complete an
    inscription = its last step, like Courage at 4); the other texts say "complete" instead of "top step". The system
    itself stays as it is for now ([Umut] "lets keep it this way").
    Mods window: [Umut] "the mod manager still shows install buttons": his game runs the Debug build (full manager);
    the Thunderstore package is the browse-only one (checked on the published 0.14.1 download). docs/MOD_MANAGER.md
    "Where it stands". 16 texts in 10 languages (ko: 프로젝트 → 모드, the font has no 젝). 556 tests.
