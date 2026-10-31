# SkulInsight QoL

**See the real numbers behind Skul: The Hero Slayer.** Damage numbers that tell you where damage came from,
item and skull descriptions with your actual damage, a combat log that explains every hit, a Codex with every
enemy, boss and piece of gear, a history of all your runs, Skul's achievements for every save slot, and an optional
daily run with online leaderboards.

A quality-of-life mod: it shows information the game already uses internally, keeps several save slots side by
side if you use them, and only goes online if you turn on the daily run. Its only change to the game's balance is
the **rewards** (0.15): optional, on by default, and one switch at the top of the settings turns them off. Every
feature can be switched off in the game: pause (Esc) → **SkulInsight QoL**.

> **Version 0.16: restart room, new daily points.** **Restart room** (press F5 twice) takes you back to the
> moment you entered the room, as when you close the game and continue (in a daily run it ends the run). **Daily
> points** from November 1: the boss fights instead of the game's double-counted kills, speed counts more, flawless
> rooms and boss fights count, four **achievements of the day** (two of the game's, a skull of the day, a flawless
> chapter) give bonus points, and the day's board is the run alone. Yesterday's daily winners name the **adventurers**
> you meet. Fixed: mouse hover selects items, skulls and dark abilities again; no more stutters when a strong build
> kills a lot. The crash reports now note frames per second and what slows the game down (**Show FPS** for a line on
> screen).
> New in 0.15: **rewards** for every achievement, coins, kill quests and inscription activations (optional: one
> switch at the top of the settings turns them off), six daily boards a day.
> New in 0.14: Skul's **achievements** for every save slot with a small notice, **Clear view**, **Mods** in the pause
> menu (Thunderstore's Skul mods and yours), easier-to-see healing orbs, and the **Mac version** ([guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md)).
> The game shows what's new once after the update; every detail is in the [changelog](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/CHANGELOG.md).
> Please report bugs and ideas (see [Feedback](#feedback--bug-reports)).

![A boss fight with the mod: damage numbers with their source, boss HP and the mini log](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/overview.jpg)

## Features

### Damage numbers with their source
- Every damage number shows where it came from: an icon or a colour-coded tag.
  - Basic attack, skill, item, essence, dash, swap, dark ability
  - Poison, burn, bleed and other statuses
- Numbers stay on screen a little longer and fade out smoothly (both configurable).

![A basic attack (left) and a skill (right), each with its source icon](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/damage-numbers.png)

### HP numbers on big health bars
Bosses, dark elites, veterans and the adventurer party show `current / max` HP (and shields) on their health bars.

![Boss HP](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/boss-hp.png)

### Real numbers in every description (League-of-Legends style)
Descriptions of skulls, skills, swaps, items, essences and inscriptions show the damage **with your current
stats**. **Hold Shift** (or the game's own details key) to see the numbers: the game's boxes stay short and show "Hold Shift for damage numbers"
(setting Descriptions/HoldToShow). On items on the ground, the game's skill and inscription details open beside the
box so they don't cover the numbers. In the boxes each attack is one compact line, e.g. `Basic attack: 10–12 · 22–24 ·
36–42 Magic`, with "All numbers: Codex (K)"; the Codex shows every hit with its calculation, e.g. `60–94 Physical
(7–11 skull dmg x 530% x 160% phys. atk)` (setting "Compact numbers"; off: the formula in the boxes too). On Arsenal
pages, hold Shift to see what a piece of gear would do with your run's stats.
- **Skulls:** every hit of the basic combo, jump and dash attacks, charge levels (uncharged / charged / charge N)
  and passives.
- **Items and essences:**
  - their damage and when it triggers ("On dash, 7 s cooldown")
  - summons and spirits
  - items that transform into another item
- **Inscriptions:**
  - the damage each step unlocks (Arms armaments, Brawl shockwaves, Artifact meteors, Oberon's attacks, ...)
  - status numbers (poison ticks, burn, bleed including severe bleed and crits, freeze and stun durations)
  - the **tuned** (upgraded) versions, including the witch's "Tuned" box
- Explains mechanics the game only hints at, e.g. how Strike's "deadlier strikes" really multiply crit damage.

A skull's basic combo and jump attack, each hit with its real numbers:

![Skull description with the damage of every hit](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/skull-description.png)

Every skill shows its damage:

![Skill description with its damage](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/skill-description.png)

Even inscription damage is calculated, step by step (here Arms armaments and Excessive Bleeding's bleed):

![An item's inscriptions with their damage per step](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/inscription-description.png)

### Pickup and swap preview
- Items on the ground and in shops show their numbers **as if you had already picked them up**. The item's
  stats and every inscription step it would activate are included.
- The popup says what changes:
  `If picked up: Arms 1 > 2 · Courage 1 > 2 · Phys. atk 191% > 355.2%`
- With a full inventory, the swap menu shows the numbers **after swapping** the selected item.

This makes planning a build much easier: you see what an item would do right at the item, without switching to
the stat sheet and back.

![Pickup preview: the item's numbers as if picked up, with the inscription and stat changes](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/pickup-preview.png)

### Combat log (press `L`)
- Every hit, grouped by room, updated live.
- Filters: dealt / taken, room / all rooms, enemy type, damage type, source, crits only, minimum damage.
- Pie chart of your damage by source or damage type.
- **Damage table (new in 0.12):** View → Damage table: your damage per piece of gear: each skull's basic attack,
  skills, dash and swap, every item, quintessence, inscription and dark ability, statuses and summons, with share,
  hits and biggest hit. See which item really carries your build.
- **Hover a line to see its full calculation.** Click to pin it. Each step shows its icon, its multiplier and the
  running total:
  - base damage
  - your attack stats
  - crit
  - every item, inscription and dark ability that changed the number
  - effects on the enemy
  - the final rounding
- Every hit is named after what dealt it, e.g. "Shadow Spirit Death dealt 14 ...".
- Every session is saved to `BepInEx/DamageInsight/CombatLogs/` (JSON lines), so you can look at a run after
  closing the game.
- Optional mini log: a few fading lines above the minimap.

![Hovering a hit shows its full calculation: every item, inscription and effect with its multiplier](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/combat-calculation.png)

![The mini log above the minimap](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/mini-log.png)

![The combat log with filters and the pie chart by source](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/combat-log.png)

### Cooldown ticker
The skill icons of both skulls, the swap icon, the quintessence and the item and ability icons show their remaining
cooldown in real seconds, WoW style: `83s`, `2m+`, `10m`. Your cooldown speed bonuses are included.

![Seconds left on a skill icon](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/cooldown-ticker.png)

### Codex (bestiary), new in 0.10
Press **K** for a book with every enemy, boss, adventurer, skull, item, essence and inscription you have met.
- **Unlocked by playing:** met = ink outline, then a black silhouette, then colour and animations. Common enemies
  need more kills than rare ones, so every entry takes about the same number of runs (★★ after about one run's
  worth of that enemy, ★★★ after about five). Bosses and adventurers: 1 and 3 wins.
- **Boss move lists read from the game's own AI:** every boss and adventurer shows its complete list of moves, grouped
  by fight phase. Moves you haven't seen yet are shown as a black **???**, so you know what is still missing.
- **Fight films:** the first time a boss does a move, the mod films it (small picture, 10 per second). Switch between
  posed **Animations** and **Attacks (filmed)** in the book; "Refilm" replaces a take you don't like.
- **Move notes (new in 0.11):** every boss move has a short note: what it does and how to deal with it, with
  numbers from the game (cooldowns, health thresholds, Dark Mirror differences), in all game languages.
- **Clean, compact films (new in 0.11):** films show only the boss (no damage numbers, none of your own effects,
  summons or status effects) and take about a quarter of the space. Both can be switched off on the settings page.
- **Reset Codex (new in 0.11):** start the book over from the settings page; the move notes stay.
- **Dark Mirror** versions of bosses are kept separately (switch in the book).
- **Sections (new in 0.12):** **You**, **Bestiary** (enemies, bosses, dark enemies) and **Arsenal** (skulls, items,
  quintessences, inscriptions, dark abilities). Names in the texts are links; "< Back", Backspace or the mouse's back
  button go to the page before.
- **Arsenal pages (new in 0.12):** every skull, item, quintessence, inscription and Dark Mirror ability with its full
  text and real numbers: a skull's awakening path, attacks and skills, an item's inscriptions and transformation, an
  inscription's steps and the items that carry it.
- **You page (new in 0.12):** your run as a Codex inventory: skulls, quintessence, items, dark abilities, inscriptions
  with what the next step needs, and a stats sheet with the hidden damage stats explained. Click anything to read it in
  full with your current numbers.
- **Dark enemies (new in 0.12):** what each of the 15 dark elite abilities does, with its numbers.
- **Boxed pages and skill boxes (new in 0.12):** every section is a framed box with an icon. Each skull shows its skills
  as boxes (icon, name, numbers) that stay a **?** until you win a run with them: beat a boss of chapter 5 or later
  while the skill is on one of your skulls.
- **Every number with its formula (new in 0.12):** base x hit % x attack %. Arsenal pages compare gear at 100 %
  attack; **hold Shift** to see the numbers with your run's stats. Dark abilities show their damage per level.
- Everything is created on your own PC while you play; the mod ships no game art.

![A boss page playing the moves the mod filmed, with notes on how to deal with them](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-film.gif)

![An enemy page: its animations and your stats against it](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-enemy.png)

![A Dark Mirror boss page playing a filmed move](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-boss.png)

![An inscription page with every step](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-inscription.png)

### Save slots
Keep up to 5 games side by side, like profiles: pause (Esc) → **SkulInsight QoL** → **Save slots**.
- Each slot is a whole game: story, NPCs, the witch's upgrades, the Dark Mirror, currencies and the run in progress,
  with its own Codex.
- **Play** switches to a slot: the game you were playing goes into its slot (a run in progress waits at the room you
  entered last) and the other game comes in through the title screen. An empty slot starts a new game.
- **Copy my game here** copies your current game (with its Codex) into a slot; **Clear** empties one.
- Each slot shows a summary: run, story, NPCs, witch levels, dark quartz, Dark Mirror, deaths, last played.
- Your keys, sound and language stay the same in every slot. The game's save is backed up before every switch.
- Number of slots: 3 to 5 (setting).

### Run history and records (new in 0.13)
Codex (`K`) → **You** → **Runs**: every run you play, like a match history.
- Each row: how the run ended (the boss or the killer in a frame), Normal / Dark Mirror level / Rookie mode, your
  skulls and items, kills, damage, gold, time and date.
- A run's page: **Overview** (how it went, the bosses defeated, the build with every inscription, the numbers, your
  stats at the end), **Damage** (each piece of gear's share) and **Timeline** (skulls, items and inscriptions as
  they came, map by map). Every name links to its Codex page.
- **End screen** shows the game's own result panel again with the run's numbers.
- **Star** a run to keep it whole forever; older runs without a star keep only their summary (setting).

![Run history: every run as a row, and a run's page with the bosses, the build and every inscription, all linked to the Codex](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/run-history.png)

- **Records** (You → Records): totals, best runs, per skull, against bosses, deaths and the game's own counters.

![Records: totals, bests, skulls, bosses, deaths and the game's own counters](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/records.png)

### Achievements (new in 0.14)
Codex (`K`) → **You** → **Achievements**: all 101 of Skul's Steam achievements, as **this save slot** has them.
- Steam gives an achievement only once; here every save slot earns its own. A slot from before 0.14 gets what its
  save proves (bosses beaten, Dark Mirror levels, the endings, legendary skulls, ...); slot 1 (the game you had
  before save slots) can take over your Steam account's achievements once.
- Earned ones in colour with the date and the run they came in (a link), the rest in grey. Each page: how to earn it,
  the progress in your save (adventurers, dark quartz hearts, fragments, ...), a **tip** for the hard ones read from
  the game's code (what counts as getting hit in a no-hit clear, Little Bone for The Skeleton King, ...), your Steam
  account's state and the share of all players.
- Achievements that give the story away are **veiled** until you earn them or meet what they are about; hold the
  mouse button on one for 4 seconds to look anyway (setting).
- Two that the game counts but Steam doesn't have: Alexander and the Dark Skeleton without getting hit.
- When your slot earns one, a **small notice slides down from the top of the screen** with a short chime and back up
  (settings: notice, sound volume).
- **Daily runs** have SkulInsight's own achievements: thumbs up received (5 to 100) and days in a row (3 to 30).

![Achievements: every achievement as this save slot has it, with how to earn it, this slot and Steam](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/achievements.png)

### Rewards (new in 0.15; on by default, one switch turns them off)
Codex (`K`) → **You** → **Rewards**. The first feature that changes the game's balance, a little: everything belongs to
the save slot, never counts in daily runs, and *Rewards* at the very top of the settings page turns it all off
(everything earned is kept and comes back).
- **Achievement rewards:** each of Skul's 103 achievements gives a small lasting bonus that fits it. A boss beaten:
  +3 % damage to that boss; beaten without getting hit: +10 % more (they multiply: +13.3 %). Each Dark Mirror level:
  physical and magic attack +1 %. Each legendary skull: +0.5 % by its type (Power: physical attack, Speed: attack speed,
  Balance: magic attack). Dying and traps: damage taken -1 %; shops: gold +1 %; the castle and the world: movement
  speed +1 %; dark abilities and inscriptions: cooldown speed +1 %; the hidden boss's trail: critical chance +1 %; and a
  few special ones (100 adventurers: +5 % damage to adventurers; The Skeleton King: +10 % while holding Little Bone).
  With every achievement: physical and magic attack +18.5 %, still less than one good item. Each achievement's page and
  the notice say what it gives; the Rewards tab adds it all up.
- **Coins:** break a legendary item or skull, or leave it behind on a map, and you get its coin (a skull raised with
  bones gives its first form's coin). Every 5th win over one adventurer gives that adventurer's coin, for its rare
  legendary item (The Chosen ...). A run that ends with Omen items gives an Omen coin for each, for an Omen item of your
  choice (its own page has a Summon too). In a later run, after the map is cleared, **Summon** drops the gear at your
  feet: one summon per run for each kind of coin (a skull, an item, an adventurer's item, an Omen item). A summon
  takes 3 coins of that gear (or 3 Omen coins; setting *Coins per summon*), an adventurer's coin is enough on its own;
  the adventurers' items in Dark Mirror runs.
- **Kill quests:** every normal enemy has an endless quest. About two runs' worth of kills of it (10 to 150) pays
  25 dark quartz and 50 gold where it fell; dark enemies count double. A full run finishes about 21 quests (about
  535 dark quartz and 1,075 gold). Each enemy's page shows its quest as a square that fills up.
- **Inscription activations:** complete an inscription in a run (bring it to its last step) and it earns one when
  the run ends, won or lost. In a later run an activation makes an inscription you have count one more until that run
  ends, like the Dark Mirror's banners (two per run, each inscription once).
- The run history marks runs played with rewards on, with the quests and coins of that run. All the numbers are
  settings (section *Rewards*).

![Rewards: achievement rewards, coins, kill quests and inscription activations of this save slot](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/rewards.png)

### Healing orbs (new in 0.14)
The purple healing orbs enemies drop are easy to miss. They can look like the game's **Portable Potion** (a bit bigger,
with a green glow) or a big **green plus**; when a map is cleared while you are hurt and orbs are left, a short text
above you says so. Your runs count the **heals missed** (orbs left behind while hurt, and the health they would have
restored): on each run's page and on the Records page.

![Healing orbs: the game's look, the Portable Potion and the green plus](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/heal-orbs.png)

### Clear view (new in 0.14)
With a strong build, your own effects can cover the screen until you can't see what hits you. Clear view makes the
effects of your skills, items and summons (and your projectiles) fainter: over the run (full in chapter 1, the
lowest opacity from chapter 5 on) and whenever many of them are on screen at once. **Enemy attacks and their warnings
are never faded.** Settings section *Clear view*: on/off, the lowest opacity (40 %), both ways of fading, projectiles.

### Mods (new in 0.14)
Pause (Esc) → **Mods**: a mod browser in the game, on Windows, Linux / Steam Deck and Mac.
- Left: **Thunderstore's Skul mods** with search, categories and sorting (last updated, downloads, likes, A–Z), each
  with its icon, author, downloads, likes and age. Click one for its details, the mods it needs and its Thunderstore page.
- Right: **your mods** with Enabled / Disabled, and the mods folder one click away.
- Installing, updating and turning mods on or off is your mod manager's job (r2modman, Gale): Thunderstore's rules
  don't allow a mod to update itself or other mods. It only goes online (Thunderstore) when you open it.

### Daily runs (new in 0.13, six boards since 0.15; online, off until you turn it on)
Codex (`K`) → **Daily**: every day three runs for everyone, each with and without the DLC, and one try on each.
- **Normal**, the **Dark Mirror at a random level** (0-10, the same for everyone that day) and **Dark Mirror 10**.
  Each has its own seed and its own boards; your streak counts any of them.
- **With DLC** boards bring the Mythology Pack's skulls and items and are only playable if you own it (anyone can
  watch them). **Without DLC** boards are the same run for everyone, DLC or not.
- The same start for everyone: the same gifts from the castle's Fox and Ogre and the same blessing.
- Played in a separate daily save slot made from your game: every gear unlocked, the Witch at her top, the Dark
  Mirror (and its machines) up to the day's level, the whole Codex open, 15,000 dark quartz, no rewards. Your own
  game waits as it is; **Back to my game** returns.
- After the run: a name (12 characters) for the **daily** and **monthly** boards. If typing freezes your game (some
  input methods such as Pinyin do in full screen), put the name into the mod manager's config (SkulInsight QoL →
  Daily → Name) and just press Send.
- **Points** (from 2026-11-01): a win 20,000 and 5 for every second under an hour (a run that didn't win: 1,000 per
  stage), 1,000 per boss fight won and 3,000 per optional Dark Mirror boss (counted from the run, not the game's
  double-counted kills), 150 per **flawless room** and 1,000 per **flawless boss fight** (no health lost; a shield or
  a dodge keeps it flawless), -2 per damage taken, 1 per 10 gold and 2 per bone left, times the difficulty.
- **Achievements of the day:** four per board, 300 to 1,500 points each: two of the game's (only ones every run can
  get: no-hit bosses, the First Hero as Little Bone, a minute in the black market, 4 dark abilities), a **skull of the
  day** (win holding it) and a **flawless chapter**. The day's board is the run alone; the daily streak (up to 30
  days) counts on the month board.
- **Yesterday's winners** give their names to the adventurers you meet (setting Winners as adventurers).
- **Click a run on the board**: its build, damage and timeline like your own run history, its points part by part,
  and a thumbs up.
- Fair play: one try (a daily run can't be continued), runs with other mods loaded don't count, no Steam
  achievements from the daily slot.
- It talks to the daily run server (skul.umut-aslan.dev) only after you turn it on, and sends a random player id
  made by the mod, the name you enter and your daily run (numbers, build, damage, timeline). Nothing else.
- Shoutout to Tythos and his community for using and promoting this passion project: leave him some support on
  [Bilibili](https://space.bilibili.com/17576167).

![Daily runs: the day's boards (Normal, Dark Mirror at the day's level, Dark Mirror 10, DLC) and a board](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/daily-boards.png)

![Daily run: today's board, and a run on it with Overview, Damage, Timeline, Points and a thumbs up](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/daily-board.png)

### Settings in the game
Pause (Esc) → **SkulInsight QoL**, right under the game's own Settings, styled like the game's options, with keyboard,
mouse and controller. Since 0.16 the left side lists the features in groups: **Gameplay (changes the game)**, **Insight**
(what only shows), **Helpers**, **Online** and **General**, each with its switch; hovering or choosing one shows on the
right what it does and its settings (Enter goes into them, Esc back). It shows the settings most players change; **All
settings** at the end of the list shows every one, the fine-tuning too. New players choose at the first start between
"Only information" and "Information and gameplay". Changes apply at once and are saved right away.

![The SkulInsight QoL button in the pause menu](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/settings-menu.png)

![The settings page: every option with its explanation and default](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/settings-page.png)

![Choosing the icon for each damage source](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/settings-icons.png)

### Restart room (new in 0.16)
Press **F5** twice within 3 seconds (Restart Room → Key in the settings): back to the moment you entered the
room (health, gear, gold, everything), exactly as when you close the game and continue, but without closing it. Your
rewards go back too, and the run history notes each restart. In a daily run it ends the run (only full runs count).

### Crash reports
Every game session leaves a report in `BepInEx/DamageInsight/CrashReports` (the newest 20): what happened (chapters,
maps, your build), every error with **whose code it was in** (the game's, SkulInsight's, a game method SkulInsight
hooks into, or another mod) and a verdict. A screen that stays black for 20 seconds and a game that closed
unexpectedly are noticed and said on screen. Since 0.16 it also notes how smooth the game runs (frames per second,
stutters, how much of each frame SkulInsight takes) and what was going on if the game stands still; **Show FPS** puts
a line on screen. Settings page: **Crash reports folder** → Open. Please attach the newest report to bug reports.

### All game languages
The mod's texts follow the game's language (English, 한국어, 日本語, 简体中文, 繁體中文, Deutsch, Español, Português (Brasil), Русский, Polski, Français) and switch live when you change it.
The settings page has a *Language* row to pick a different one. To fix a translation without waiting for an
update, put a file `BepInEx/DamageInsight/Lang/<code>.json` with only your changes
(`{"strings": {"English text": "your text"}}`; codes: en, ko, ja, zh-Hans, zh-Hant, de, es, pt-BR, ru, pl, fr), and
please send it in so everyone gets it.

## Installation

### With a mod manager (recommended)
1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or
   [Gale](https://thunderstore.io/c/skul-the-hero-slayer/p/Kesomannen/GaleModManager/).
2. Select **Skul: The Hero Slayer**, create a profile and install **SkulInsight QoL**. The required
   [BepInExPack Skul](https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/) is installed
   automatically.
3. Start the game with **Start modded**.

### Manual installation
1. Install [BepInExPack Skul](https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/) into
   the Skul folder (the folder containing `Skul.exe`).
2. Start the game once and close it (BepInEx creates its folders).
3. Copy `DamageInsight.dll` into `Skul/BepInEx/plugins/SkulInsight_QoL/`.
4. Start the game.

### Linux and Steam Deck
The mod runs through **Proton**. Skul's native Linux version can't load it.
1. Steam → right-click Skul → *Properties* → *Compatibility*: force Proton (for example Proton 9.0).
2. Start Skul once from Steam without mods, then quit (this creates Skul's Proton prefix).
3. Leave Skul's launch options empty (the Flatpak version of r2modman is the exception).
4. Install the mod in r2modman or Gale and press **Start modded**.

Errors, Steam Deck, manual installation and a check script:
[Linux and Steam Deck guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/LINUX.md).

### macOS
The mod runs on Skul's **Mac version** (Intel and Apple Silicon); one Terminal script sets it up.
1. Start Skul once from Steam without mods, then quit.
2. Open Terminal, paste this line and press Enter:
   `curl -fsSL -o ~/Downloads/install-mac.sh https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/tools/mac/install-mac.sh && sh ~/Downloads/install-mac.sh`
3. The script prints a launch option and puts it in your clipboard: Steam → right-click Skul → *Properties* →
   *General* → *Launch options*, press Cmd+V. Then start Skul from Steam.

Other Thunderstore mods on a Mac: `sh install-mac.sh --add <the mod's Thunderstore link>`.
Manual installation, errors and how it works: [macOS guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md).

## Configuration

**In the game:** pause (Esc) → **SkulInsight QoL** (right under the game's Settings). Every setting below is there,
with an explanation and its default; changes apply at once and are saved right away. Keyboard, mouse and controller
work like in the game's own menus.

The same settings are in the settings file (created on the first start):
- `BepInEx/config/docrun.skulinsight_qol.cfg`
- in r2modman / Gale: *Config editor*

| Section | Setting | Default | What it does |
| Rewards | Enabled | on | Achievement rewards, coins, kill quests and activations (never in daily runs; off: the game as it is) |
| Rewards | AchievementRewards / Coins / Quests / InscriptionActivations | on | Each kind of reward on its own |
| Rewards | SummonsPerRun / ActivationsPerRun | 1 / 2 | Summons of each kind, and activations, usable per run (0-3) |
| Rewards | CoinsPerSummon | 3 | Coins a summon takes (1-10; an adventurer's coin is enough on its own) |
| Rewards | QuestRuns / QuestDarkQuartz / QuestGold | 2 / 25 / 50 | A quest step in runs' worth of kills, and what it pays |
| Rewards | AdventurerKills | 5 | Wins over one adventurer for its coin |
|---|---|---|---|
| Damage Numbers | ShowSourceTag / UseIcons | on / on | Source icon or tag on damage numbers |
| Damage Numbers | HoldSeconds / FadeSeconds | 0.8 / 0.7 | How long numbers stay and fade |
| Damage Icons | (per source) | | Which icon each damage source uses |
| Boss Health Bar | ShowNumbers | on | HP numbers on big health bars |
| Descriptions | ShowDamageNumbers | on | Real numbers in all descriptions |
| Descriptions | PreviewPickup | on | Pickup / swap preview |
| Descriptions | HoldToShow / HoldKey | on / Shift | Numbers in the game's boxes only while the key is held |
| Descriptions | Compact | on | One line per attack in the game's boxes (off: every hit with its formula) |
| General | WhatsNew | on | Show what's new once after an update |
| General | MenuSounds | on | The mod's buttons play the game's menu sounds (hover, click) |
| General | CrashReports | on | A report per game session in `BepInEx/DamageInsight/CrashReports` (newest 20 kept) |
| General | RunHistory / RunHistoryKeep | on / 100 | Keep every run in the Codex; how many of the newest keep all their details |
| Daily | Enabled | off | The daily run (online): only after you turn it on (Codex → Daily) |
| Combat Log | ToggleKey | L | Opens the combat log |
| Combat Log | RecordCalculations | on | Per-hit calculation (hover a line) |
| Combat Log | SaveToFile | on | Log files in `BepInEx/DamageInsight/CombatLogs` (newest 30 kept) |
| Combat Log | UseDialogueBackground / BackgroundOpacity | on / 0.85 | Look of the log window |
| Mini Log | Enabled, Lines, HideAfterSeconds, ShowPie, GapAboveMinimap | off, 4, 5, on, 40 | Small log above the minimap |
| Cooldown Ticker | Enabled / Opacity | on / 0.55 | Seconds on HUD icons |
| Clear View | Enabled / LowestOpacity | on / 0.4 | Your own effects get fainter so enemy attacks stay visible |
| Heal Orbs | Look / Reminder | Potion / on | How healing orbs look (Game, Potion, Plus); a text when some are left on a cleared map |
| Mods | Enabled | on | The Mods button in the pause menu (the mod browser) |
| Clear View | OverTheRun / WhenBusy / Projectiles | on / on / on | Fade by chapter, when many effects are on screen, and your projectiles too |
| Codex | Enabled / ToggleKey | on / K | The Codex book |
| Codex | ShowMoveHints | on | Under a beaten boss's moves: when it uses them |
| Codex | FilmBossAttacks | on | Film each boss move once for the Codex |
| Codex | CleanFilms | on | Films show only the boss (no damage numbers, own effects, summons, status effects) |
| Codex | CompactFilms | on | Save films as compact pictures (about a quarter of the size) |
| Codex | ProjectilePictures | on | Add an enemy's projectile to its pictures the first time it fires one |
| Codex | OpenOnYou | on | During a run the book opens on the You page |
| Codex | AchievementNotice / AchievementSound | on / 50 % | A small notice (with a chime) slides down from the top when your save slot earns an achievement |
| Codex | VeilSecretAchievements | on | Story spoilers stay veiled on the Achievements page until earned, met or held |
| Codex balance | (unlock numbers) | | Runs / wins / pickups it takes to unlock ★★ and ★★★ in the Codex |

## Compatibility
- Made for Skul: The Hero Slayer **1.9.x** (Steam: Windows; Linux and Steam Deck through Proton) with
  BepInExPack Skul (BepInEx 5.4.21+), or the native Mac version with BepInEx 5.4.23.5 for macOS (set up by
  [the Mac installer](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md)).
- Doesn't change balance or content. Your save is only touched when you switch save slots (a backup of the game's
  save is made before every switch); a daily run is played in its own slot. Safe to add or remove at any time.
- Should work next to content mods. Items the mod doesn't understand simply show no extra numbers.
  Please report anything odd. Daily runs only go on the board without other mods (with them they still work).
- After a game update the mod checks all of its hooks at startup and switches off only what broke. The results
  are in `BepInEx/LogOutput.log` as `[SelfTest]` lines.

## Known limitations
- A few item effects that aren't direct damage (buffs, heals, chances) show no extra numbers yet.
- The pickup preview covers item stats and plain stat inscriptions. Conditional buffs and bonus-inscription items
  are not included yet. Skulls and essences on the ground have no preview yet.
- Shock and ember damage from some sources may be labelled with a generic tag.
- Codex:
  - King Alexander's hands, First Hero's later forms, the bomb phase and the Dark Mirror helpers (Supporting
    Thief/Warrior) have no notes yet. King Alexander's heart fights with several attacks at once, so some of its
    films show two attacks.
  - Films made before 0.11 may still show your own effects or end early; "Refilm" or "Reset Codex" takes them again.
  - Arsenal pages show what you have met; a skill's numbers appear after you used it once.
- Achievements: their icons come from Steam (the first look may take a moment). No-hit clears and a few others
  (the black market, the castle) can't be proven from an old save and are earned again.
- Daily run: a crash during a daily run ends that day's try (a daily run can't be continued). Only today's board
  opens runs; past days and the monthly board don't yet.
- Translations were made with the help of the game's own terms but not checked by native speakers yet; corrections
  are very welcome (see *All game languages*). The config file and the developer tools stay English.

## Roadmap
- **0.14:** Skul's achievements per save slot, the achievement notice, the mod's own daily achievements, clear view,
  the Mods window, macOS
- **0.15 (this version):** rewards: a bonus for every achievement, coins that summon gear, kill quests, inscription
  activations
- **0.16:** Dark Mirror page (levels 0–10 with the game's values, Darktech machines, fragments, the hidden boss)
- **Later:** skins for skulls from the daily achievements
- **Later:** knowledge pages: drop chances, run layout, shops and prices, NPCs, game mechanics

## Feedback / bug reports
Please open an issue on GitHub: https://github.com/Umut-Aslan99/SkulInsight_QoL/issues. Attach the newest file
from `BepInEx/DamageInsight/CrashReports` (settings page: **Crash reports folder** → Open) and `BepInEx/LogOutput.log`,
and tell us the mod version and what you did. After a crash, start the game once more before you send it: the
report of the crashed session is completed at the next start. Screenshots help a lot. On Linux, please
also attach the report of the [check script](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/LINUX.md#the-check-script); on a Mac, the report of
[`sh install-mac.sh --check`](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md#the-check-script).

## Credits
- **DocRun**: idea, design and testing
- Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [Harmony](https://github.com/pardeike/Harmony)
- Thanks to MrBacanudo's Skul mods ([SkulHardModeMods](https://github.com/MrBacanudo/SkulHardModeMods)) for
  showing how Skul modding works, and to the Skul modding community on Thunderstore.

Skul: The Hero Slayer is made by SouthPAW Games and published by NEOWIZ. This is an unofficial fan mod and not
affiliated with them.

## License
[GPL-3.0](LICENSE). You may use, change and share this mod. If you publish a changed version or a mod built on
this code, it must also be GPL-3.0 with its source available, and keep the original credits.
