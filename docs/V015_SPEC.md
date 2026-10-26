# 0.15 spec: rewards (achievements and the bestiary)

Written 2026-10-25 by Claude from Umut's request. Umut's answers are in §F (2026-10-25);
numbers can still be tuned after playtests. A running step log (docs/V015_LOG.md) starts when building begins.
The first feature of the mod that changes the game's balance: everything here is switchable and off in daily runs.

## Umut's request (2026-10-25)
- "There should be a multitude of rewards, and i decided to implement rewards for each achievement as well that
  thematically fits." Example: "die to a trap should give a permanent run boost reward of like damage taken -1%.
  Something minuscule, but it should add up in the long run."
- "The 'hard' achievements like kill a boss without taking damage should increase your damage dealt to this boss by
  +5 or 10% permanently multiplicatively. Sounds strong, and it should be worth mastering a boss for this."
- "Other type of rewards permanently: increase in gold gain +1%, physical AND/OR magical Atk +2% permanently or some
  balanced number like that, +x% more attack speed permanently, +x% more movement speed permanently, etc. Find
  categories / groups that fit a specific permanent buff."
- "This option should be on by default, but should be very far at the top of the SkulInsight QoL settings menu to be
  able to be turned off."
- "In the same version as the achievement rewards, we want to add the rewards with the bestiary that were planned.
  Coins when breaking legendary items or skulls to spawn them in future runs manually with a button. Repeatable
  infinite rewards / quests for basic enemies killed (dark quartz, or little gold bonus for making casual runs
  better). And more."
- Version: [Umut] rewards are 0.15; the Dark Mirror page moves to 0.16.

## Principles
- **Per save slot**, like the achievements they come from: a slot's rewards are what that slot earned.
- **Off in the daily slot** (every daily run stays equal) and in the developer tools' test map.
- **Setting Rewards/Enabled (on)**, the first section on the settings page, saying plainly that it changes balance.
  Off = the game as it is; nothing earned is lost (it comes back when turned on again).
- **Visible**: every achievement page shows its reward; a new **You → Rewards** tab adds them up ("Damage taken
  -3%", "Gold +5%", per boss); the achievement notice names the reward; the You page's stats include them (they are
  the game's own stat values). Run history marks runs played with rewards on.
- **Small, adding up**: at 100 % of all achievements the global bonuses stay below what one good item gives
  (table totals below).
- **Through the game's own systems**: stats as `Stat.Values` attached to the player (`player.stat.AttachValues`,
  category PercentPoint, so they add to the items' percentages like the Witch's), gold through
  `GameData.Currency.gold.multiplier`, the boss multipliers in the damage the player deals (our damage hook knows
  the target), gear spawned with `LevelManager.DropGear` from its `GearReference`, currencies with `Currency.Earn`.

## A. Achievement rewards (all 103; generated from the catalog and the gear scan's skull types)
Groups and numbers [Umut: yes]:
- **Boss defeated**: damage dealt to that boss ×1.03 (all its phases).
- **Boss without getting hit** (Umut: 5-10 %): damage dealt to that boss ×1.10, multiplied with the above (×1.133).
- **Dark Mirror level 0-10 destroyed**: physical and magic attack +1 % each (+11 % at level 10).
- **Legendary skulls** (44), by the skull's type: Power +0.5 % physical attack, Speed +0.5 % attack speed, Balance
  +0.5 % magic attack (15 / 14 / 15 skulls: +7.5 % / +7 % / +7.5 %).
- **Danger** (dying, traps): damage taken -1 % each (Umut's example).
- **Shops**: gold gain +1 % each. **The castle and the world**: movement speed +1 % each. **Dark abilities and
  inscriptions**: cooldown speed +1 % each. **The hidden boss's trail**: critical chance +1 % each.
- **Special**: 100 adventurers → damage to adventurers +5 %; The Skeleton King → damage with Little Bone +10 %;
  Cold-Blooded → critical damage +3 %; Rookies Welcome → healing received +5 %.
- **SkulInsight's daily achievements**: no gameplay reward (they belong to the daily player, not to a slot, and
  the boards stay fair); later skull skins [Umut].

Totals with every achievement: physical and magic attack +18.5 %, attack speed +7 %, damage taken -3 %, gold +5 %,
movement +8 %, cooldowns +9 %, critical chance +4 %, critical damage +3 %, healing +5 %; per boss ×1.03 / ×1.133.

| Group | Achievement | Reward |
|---|---|---|
| Boss defeated | Rescued Elder Ent | Damage to Yggdrasil ×1.03 |
| Boss defeated | The End of the Gold Mane Knights | Damage to the Leiana Sisters ×1.03 |
| Boss defeated | False Goddess, Disappear | Damage to Chimaera ×1.03 |
| Boss defeated | The Fall of the Leonia Order | Damage to St. Joan II ×1.03 |
| Boss defeated | The Final Battle of the First Hero | Damage to the First Hero ×1.03 |
| Boss defeated | True Peace | Damage to Alexander ×1.03 |
| Boss defeated | True Salvation | Damage to the Dark Skeleton ×1.03 |
| Boss without getting hit | Quick Movements | Damage to Yggdrasil ×1.10 |
| Boss without getting hit | Master of Speed | Damage to the Leiana Sisters ×1.10 |
| Boss without getting hit | Deadly Power | Damage to Chimaera ×1.10 |
| Boss without getting hit | The Levitator | Damage to St. Joan II ×1.10 |
| Boss without getting hit | The God of Battle | Damage to the First Hero ×1.10 |
| Boss without getting hit | The Strongest | Damage to the First Hero (Dark Mirror) ×1.10 |
| Boss without getting hit | True Peace (no hit) | Damage to Alexander ×1.10 |
| Boss without getting hit | True Salvation (no hit) | Damage to the Dark Skeleton ×1.10 |
| Dark Mirror level 0 | Broken World | Physical and magic attack +1% |
| Dark Mirror level 1 | A Worthwhile Adventure | Physical and magic attack +1% |
| Dark Mirror level 2 | Courageous Act | Physical and magic attack +1% |
| Dark Mirror level 3 | Result of Your Efforts | Physical and magic attack +1% |
| Dark Mirror level 4 | Self-confidence | Physical and magic attack +1% |
| Dark Mirror level 5 | Energy and Perseverance | Physical and magic attack +1% |
| Dark Mirror level 6 | Spirit of Challenge | Physical and magic attack +1% |
| Dark Mirror level 7 | Surprising Move | Physical and magic attack +1% |
| Dark Mirror level 8 | Phenomenal Success | Physical and magic attack +1% |
| Dark Mirror level 9 | Great Achievement | Physical and magic attack +1% |
| Dark Mirror level 10 | Nothing is impossible | Physical and magic attack +1% |
| Danger (dying, traps) | The Legend Begins | Damage taken -1% |
| Danger (dying, traps) | Focus! | Damage taken -1% |
| Danger (dying, traps) | New Start | Damage taken -1% |
| Shops (black market, collector) | What should I buy? | Gold gain +1% |
| Shops (black market, collector) | Brain at Full Throttle | Gold gain +1% |
| Shops (black market, collector) | Inventory Management | Gold gain +1% |
| Shops (black market, collector) | Special Guest | Gold gain +1% |
| Shops (black market, collector) | All-in | Gold gain +1% |
| The castle and the world | Go Home... | Movement speed +1% |
| The castle and the world | Castle Restoration Plans | Movement speed +1% |
| The castle and the world | The Dwarven Chandelier | Movement speed +1% |
| The castle and the world | Rules of Etiquette | Movement speed +1% |
| The castle and the world | Too Much of a Talker | Movement speed +1% |
| The castle and the world | Hall of Gods | Movement speed +1% |
| The castle and the world | The Power of Science! | Movement speed +1% |
| The castle and the world | The War Begins | Movement speed +1% |
| Dark abilities and inscriptions | Best Condition | Cooldown speed +1% |
| Dark abilities and inscriptions | The Cursed | Cooldown speed +1% |
| Dark abilities and inscriptions | Dark Ability | Cooldown speed +1% |
| Dark abilities and inscriptions | Hate the Sinner, Love the Sin | Cooldown speed +1% |
| Dark abilities and inscriptions | Terrible Omen | Cooldown speed +1% |
| Dark abilities and inscriptions | Solid Career Choice | Cooldown speed +1% |
| Dark abilities and inscriptions | Witch's Help | Cooldown speed +1% |
| Dark abilities and inscriptions | Heart of Darkness | Cooldown speed +1% |
| Dark abilities and inscriptions | Spider God’s Protection | Cooldown speed +1% |
| The hidden boss's trail | Dark Side of Harmonia | Critical chance +1% |
| The hidden boss's trail | Hidden Existence | Critical chance +1% |
| The hidden boss's trail | Dark-Tech Tank | Critical chance +1% |
| The hidden boss's trail | Bone Specialist | Critical chance +1% |
| Special | The Hero Killer | Damage to adventurers +5% |
| Special | The Skeleton King | Damage with Little Bone +10% |
| Special | Cold-Blooded | Critical damage +3% |
| Special | Rookies Welcome | Healing received +5% |
| Legendary skull (Balance) | An Honorable Name | Magic attack +0.5% |
| Legendary skull (Speed) | Charge! Again! And Again! | Attack speed +0.5% |
| Legendary skull (Power) | The Best Offense is Defense | Physical attack +0.5% |
| Legendary skull (Power) | Guardian of Nature | Physical attack +0.5% |
| Legendary skull (Speed) | The Greatest Werewolf | Attack speed +0.5% |
| Legendary skull (Speed) | The Back Alley King | Attack speed +0.5% |
| Legendary skull (Balance) | War Fanatic | Magic attack +0.5% |
| Legendary skull (Balance) | Demon King Castle Gargoyle | Magic attack +0.5% |
| Legendary skull (Speed) | Start the Show | Attack speed +0.5% |
| Legendary skull (Power) | Dominating Size | Physical attack +0.5% |
| Legendary skull (Speed) | Rider Instincts | Attack speed +0.5% |
| Legendary skull (Power) | The King of the Labyrinth Returns | Physical attack +0.5% |
| Legendary skull (Balance) | Arms Expert | Magic attack +0.5% |
| Legendary skull (Balance) | The Embodiment of Cold | Magic attack +0.5% |
| Legendary skull (Speed) | Explosions are a Work of Art | Attack speed +0.5% |
| Legendary skull (Power) | The Raging Wave | Physical attack +0.5% |
| Legendary skull (Balance) | An Endless Appetite for Research | Magic attack +0.5% |
| Legendary skull (Power) | Too Big to Be Called a Sword | Physical attack +0.5% |
| Legendary skull (Power) | Praise Me! | Physical attack +0.5% |
| Legendary skull (Balance) | Ultimate Weapon: Bow | Magic attack +0.5% |
| Legendary skull (Speed) | A Stylish Entrance | Attack speed +0.5% |
| Legendary skull (Speed) | The Legendary Ninja | Attack speed +0.5% |
| Legendary skull (Balance) | The Dark-Armored Knight | Magic attack +0.5% |
| Legendary skull (Balance) | From the Deep Abyss | Magic attack +0.5% |
| Legendary skull (Power) | Burning Soul | Physical attack +0.5% |
| Legendary skull (Power) | The Unbreakable Pact | Physical attack +0.5% |
| Legendary skull (Speed) | The Final Mystery of the Arts | Attack speed +0.5% |
| Legendary skull (Power) | Destructive Instincts | Physical attack +0.5% |
| Legendary skull (Balance) | Sealed Power | Magic attack +0.5% |
| Legendary skull (Power) | The Dokkaebi’s Supernatural Stomp | Physical attack +0.5% |
| Legendary skull (Speed) | The Legend of the Arena | Attack speed +0.5% |
| Legendary skull (Balance) | The Ruler of the Nether World | Magic attack +0.5% |
| Legendary skull (Balance) | Wanna Play a Game With Me? | Magic attack +0.5% |
| Legendary skull (Speed) | Ghostly Elusiveness | Attack speed +0.5% |
| Legendary skull (Power) | All Soul's Day | Physical attack +0.5% |
| Legendary skull (Speed) | Beheaded | Attack speed +0.5% |
| Legendary skull (Power) | Do you fear death? | Physical attack +0.5% |
| Legendary skull (Balance) | White Mage | Magic attack +0.5% |
| Legendary skull (Balance) | God of Thunder | Magic attack +0.5% |
| Legendary skull (Balance) | For Tonight, We Dine in Hell! | Magic attack +0.5% |
| Legendary skull (Power) | Strength of 10,000 Men | Physical attack +0.5% |
| Legendary skull (Power) | Hero of the Night Sky | Physical attack +0.5% |
| Legendary skull (Speed) | Sword of Promised Victory | Attack speed +0.5% |
| Legendary skull (Speed) | Say My Name | Attack speed +0.5% |

## B. Bestiary rewards (the 2026-09-30 plan in docs/CODEX_PLAN.md, updated by Umut's request)
### B1. Legendary coins (Umut: "coins when breaking legendary items or skulls to spawn them in future runs manually with a button")
- Breaking a **legendary** item or skull gives **1 coin of that gear**: dismantling it, or leaving it on the floor
  (the game destroys it when you leave the map, the same event) [Umut].
- A skull counts as its **base skull** (a Caerleon Recruit raised to legendary with bones gives a Caerleon Recruit
  coin, not a free legendary one) [Umut]. Quintessences: no (they have no legendary rarity).
- **Summon**: a button on the gear's Codex page and in the Rewards tab, only during a run (not in the castle, not in a
  boss room): the gear drops at the player's feet, the coin is used up. **1 summon per run** (setting, 0-3) [Umut].
- Hooks: `Item.OnDestroy` / `Weapon.OnDestroy` (state Dropped, `destructible`, player alive, not a map unload),
  `LevelManager.DropGear` with the gear's `GearReference`.

### B2. Kill quests (Umut: "repeatable infinite rewards / quests for basic enemies killed (dark quartz, or little gold bonus for making casual runs better)")
- Every normal enemy has an endless quest: every **step** of kills gives a reward at once (floating text over the
  player, the game's own coin sounds). The step is paced in runs like the Codex stars: **2 runs' worth** of that enemy
  (`KillsPerRun` from the level analysis), at least 10, at most 150 kills, so a footman and a rare enemy pay about
  equally often.
- Reward: **25 dark quartz + 50 gold** [Umut 2026-10-26, was 10 + 30: "I want the rewards to be felt"; a full run
  ≈ 21 quests ≈ 535 dark quartz and 1,075 gold] (gold only counts inside a run). Dark enemies count ×2 toward the step.
  A casual run finishes 5-10 quests: 50-100 dark quartz and 150-300 gold, about one extra shop item [Umut: yes].
- The enemy's page shows "Quest 37/50: 10 dark quartz, 30 gold · done 4 times".

### B3. Adventurer coins
- Every **5 kills of one adventurer** give 1 coin of that adventurer, redeemed (Summon, as B1, same per-run limit)
  for that adventurer's legendary item (the rare drop they can give).

### B4. Also in 0.15 [Umut]
- **Omen coins** (finishing runs while holding Omen items, redeemed for an Omen item of your choice) and
  **inscription activations** (2 per run, `Inscription.bonusCount += 1` like the Dark Mirror shop's banners: an
  inscription that reached its top step in a run earns one, redeemed in a later run). Details settled when building.

## C. Settings: a new section "Rewards", the first on the settings page
- **Rewards / Enabled** (on): everything below; the help text says it changes the game's balance and is off in
  daily runs.
- Achievement rewards (on) · Legendary and adventurer coins (on) · Kill quests (on) · Summons per run (1, 0-3) ·
  Quest step in runs (2, 0.5-10) · Quest dark quartz (25) · Quest gold (50).
- Everything earned is kept when switched off and comes back when switched on.

## D. Where the player sees it
- **Achievement page**: a "Reward" line (veiled secrets keep it hidden). **Notice**: a second line with the reward.
- **You → Rewards** (new tab): what all rewards add up to (stat by stat, each boss's multiplier), coins held with
  their Summon buttons, quests done and what they paid.
- **Gear pages**: coins held + Summon. **Enemy pages**: quest progress.
- **Run history**: "Rewards on" on runs played with them, and what the run's quests and coins paid.
- 0.15 "What's new" page, README section, Thunderstore wiki.

## E. Building notes
- `RewardCatalog` (achievement → group → reward, the table above as data), `RewardStore` per slot
  (Codex/rewards.json: coins, quest counts, summons this run), `RewardApplier`: when a run's player exists, attach one
  `Stat.Values` with the totals (PercentPoint, like items); re-attach when an achievement is earned mid-run or the
  setting changes; detach when off. Damage taken = `TakingDamage`, healing = `TakingHealAmount`, cooldowns =
  `CooldownSpeed` (check each sign in game). Gold: `GameData.Currency.gold.multiplier`.
- Per-target multipliers: `player.onGiveDamage.Add(priority, ...)` with `damage.percentMultiplier *= x` (the way the
  game's own AirAndGroundAttackDamage ability does it), by the target's codex id (bosses, adventurers) or the
  current skull (Little Bone).
- Never in the daily slot or the developer test map. Steam-imported achievements count (they were earned) [Umut].
  The mod's daily achievements give no gameplay reward (later: skull skins, cosmetics, far future) [Umut].
- Developer builds: thresholds = 1 (like `CodexTiers.RevealAllMet`), F-keys to grant a coin / finish a quest.
- Tests: the catalog covers all 103 achievements, totals, stacking math, store load/save, daily-slot exclusion,
  quest step math, coin rules (base skull, legendary only, per-run limit).

## F. Umut's answers (2026-10-25)
1. Numbers: **yes** (boss ×1.03 / ×1.10, skulls +0.5 %, groups +1 %, quests 10 dark quartz + 30 gold).
2. Breaking: **both count**. [Umut] "leaving gear on the floor dismantles them at the end when leaving a room, i
   think the dismantle is an event that we can hook onto" (the same OnDestroy path with the game's destroy effect).
3. Skull coins give the **base skull**: yes.
4. Summons per run: **1, setting 0-3**.
5. Steam-imported achievements give rewards: **yes**. The mod's daily achievements: no gameplay reward; [Umut] "they
   should in future give 'skins' for skulls. I want cosmetics later but thats far in future".
6. Omen coins and inscription activations: **also in 0.15**, not 0.16. [Umut] "The Inscription activations (that work
   like the banners in the shop in dark mirror) should be part of the reward idea."

## G. Build order
1. RewardCatalog + tests · 2. stats and per-target damage · 3. settings section · 4. achievement page, notice,
Rewards tab · 5. legendary coins + Summon · 6. kill quests · 7. adventurer coins, Omen coins, inscription activations · 8. translations (10 + template) ·
9. docs, README, What's new.
