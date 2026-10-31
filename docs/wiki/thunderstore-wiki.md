# SkulInsight QoL – Guide

**See the real numbers behind Skul: The Hero Slayer.** SkulInsight QoL shows where every point of damage comes
from, puts your real damage into every description, explains each hit in a combat log, previews pickups, and
adds the **Codex**: a bestiary with every enemy, boss and item, complete boss move lists and fight films you
record yourself, a **history of all your runs** with records, Skul's **achievements for every save slot** (new in
0.14), and optional **daily runs** with online leaderboards (six boards a day since 0.15).

It is a quality-of-life mod: apart from the **rewards** (new in 0.15: optional, on by default, one switch at the top
of the settings turns them off) it changes nothing about balance or drops (save slots, if you use them, keep several whole games
side by side), it only goes online if you turn on the daily run, and every feature can be switched off.
It is safe to add or remove at any time.

![Overview](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/overview.jpg)

---

## Quick start

| Key | What it opens |
|---|---|
| **K** | Codex (bestiary) |
| **L** | Combat log |
| **F5** (twice) | Restart room (back to the start of the room) |

Everything else works on its own while you play. **Settings:** pause the game (Esc) → **SkulInsight QoL**, right
under the game's own Settings. The left side lists the features in groups (**Gameplay (changes the game)**,
**Insight**, **Helpers**, **Online**, **General**), each with its switch; hover one to see on the right what it does and
its settings. It shows the settings most players change; *All settings* at the end shows every one. Changes apply at once. (They are also in r2modman / Gale → *Config editor* → `docrun.skulinsight_qol`.)
New players choose at the first start between "Only information" and "Information and gameplay".

![Pause menu](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/settings-menu.png)

![Settings page](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/settings-page.png)

---

## Features

<details>
<summary><b>Damage numbers with their source</b>: know what actually hit</summary>

- Every damage number carries a small icon or colour tag telling you where it came from: basic attack, skill,
  item, essence, dash, swap, dark ability, and statuses like poison, burn and bleed.
- Numbers stay on screen a little longer and fade out smoothly (both adjustable).
- Each source's icon can be changed on the settings page (*Damage icons*, with a preview of the icon).

![Damage numbers](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/damage-numbers.png)
</details>

<details>
<summary><b>HP numbers on big health bars</b></summary>

Bosses, dark elites, veterans and the adventurer party show `current / max` HP (and shields) right on their
health bars.

![Boss HP](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/boss-hp.png)
</details>

<details>
<summary><b>Real numbers in every description</b>: League-of-Legends style</summary>

Descriptions of skulls, skills, swaps, items, essences and inscriptions show the damage **with your current
stats**, for example `60–94 Physical (7–11 skull dmg x 530% x 160% phys. atk)`.

- **Skulls:** every hit of the basic combo, jump and dash attacks, charge levels and passives.
- **Items and essences:** their damage and when it triggers ("On dash, 7 s cooldown"), summons and spirits,
  items that transform into other items.
- **Inscriptions:** what each step unlocks (Arms armaments, Brawl shockwaves, Artifact meteors, Oberon's attacks,
  …), status numbers (poison ticks, burn, bleed incl. severe bleed and crits, freeze and stun durations) and the
  **tuned** versions.
- Mechanics the game only hints at are spelled out, e.g. how Strike's "deadlier strikes" really multiply crit
  damage.

![Skull description](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/skull-description.png)

![Skill description](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/skill-description.png)

![Inscription damage](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/inscription-description.png)
</details>

<details>
<summary><b>Pickup and swap preview</b>: what would this item do to my build?</summary>

- Items on the ground and in shops show their numbers **as if you had already picked them up**, including every
  inscription step they would activate.
- The popup lists what changes: `If picked up: Arms 1 > 2 · Courage 1 > 2 · Phys. atk 191% > 355.2%`
- With a full inventory, the swap menu shows the numbers **after swapping** the selected item.
- Plan your build right at the item, without switching to the stat sheet and back.

![Pickup preview](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/pickup-preview.png)
</details>

<details>
<summary><b>Combat log (L)</b>: every hit, explained</summary>

- Every hit, grouped by room, updated live.
- Filters: dealt / taken, this room / all rooms, enemy type, damage type, source, crits only, minimum damage.
- Pie chart of your damage by source or damage type.
- **Hover a line to see its full calculation** (click to pin it): base damage, your attack stats, crit, every
  item, inscription and dark ability that changed the number, effects on the enemy, and the final rounding.
- Every hit is named after what dealt it ("Shadow Spirit Death dealt 14 …").
- Each session is saved to `BepInEx/DamageInsight/CombatLogs/` so you can look at a run afterwards.
- Optional **mini log**: a few fading lines above the minimap (*Mini Log → Enabled*).

![Calculation of a hit](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/combat-calculation.png)

![Combat log](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/combat-log.png)

![Mini log](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/mini-log.png)
</details>

<details>
<summary><b>Cooldown ticker</b></summary>

The skill icons of both skulls, the swap icon, the quintessence and the item and ability icons show their remaining
cooldown in real seconds, WoW style: `83s`, `2m+`, `10m`. Your cooldown speed bonuses are included.

![Cooldown seconds](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/cooldown-ticker.png)
</details>

---

<details>
<summary><b>All game languages</b></summary>

The mod's texts follow the game's language (English, 한국어, 日本語, 简体中文, 繁體中文, Deutsch, Español, Português (Brasil), Русский, Polski, Français) and switch live
when you change it in the game's settings. The mod's settings page has a *Language* row to pick a different one.

Spotted a clumsy translation? Put a file `BepInEx/DamageInsight/Lang/<code>.json` next to the mod's data with only
your changes, e.g. `{"strings": {"Combat Log": "Kampflog"}}` (codes: en, ko, ja, zh-Hans, zh-Hant, de, es, pt-BR,
ru, pl, fr). It is used at once on the next language change or game start; please send it in so everyone gets it.
</details>

### Save slots (new in 0.12)
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
- Earned ones in colour with the date and the run they came in, the rest in grey. Each page: how to earn it, your
  progress, a **tip** for the hard ones (what counts as getting hit in a no-hit clear, ...), your Steam account's
  state and the share of all players.
- Story spoilers are **veiled** until you earn them or meet what they are about; hold the mouse button on one for 4
  seconds to look anyway.
- When your slot earns one, a **small notice slides down from the top of the screen** with a short chime.
- Daily runs have SkulInsight's own achievements: thumbs up received (5 to 100) and days in a row (3 to 30).

![Achievements: every achievement as this save slot has it, with how to earn it, this slot and Steam](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/achievements.png)

### Rewards (new in 0.15)
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
The purple healing orbs enemies drop can look like the game's Portable Potion or a big green plus, a short text says
when some are left on a cleared map while you are hurt, and your runs count the heals you missed (run page, Records).

![Healing orbs: the game's look, the Portable Potion and the green plus](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/heal-orbs.png)

### Clear view (new in 0.14)
With a strong build, your own effects can cover the screen. Clear view makes the effects of your skills, items and
summons (and your projectiles) fainter over the run and whenever many of them are on screen at once. Enemy attacks
and their warnings are never faded. Settings section *Clear view*.

### Mods (new in 0.14)
Pause (Esc) → **Mods**: Thunderstore's Skul mods on the left (search, categories, sorting, details, their
Thunderstore pages), your mods on the right (Enabled / Disabled). Install, update and turn mods on or off with your
mod manager (r2modman, Gale): Thunderstore's rules don't allow a mod to update itself or other mods.

### Restart room (new in 0.16)
Press **F5** twice within 3 seconds (Restart Room → Key in the settings): back to the moment you entered the
room (health, gear, gold, everything), exactly as when you close the game and continue, but without closing it. Your
rewards go back too, and the run history notes each restart. In a daily run it ends the run (only full runs count).

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

## The Codex (K), new in 0.10

A book with every enemy, boss, adventurer, skull, item, essence and inscription you have met: your kills, damage
dealt and taken, deaths, and descriptions. Nothing is shipped with the mod: every picture and film is created on
your own PC while you play. Since 0.11 every boss move has a note (what it does, how to deal with it), and
films show only the boss (switch "Clean films" off on the settings page to see everything).

Since 0.12 the book has three sections on its title row:
- **You**: your run as a Codex inventory (skulls, quintessence, items, dark abilities, inscriptions with what the
  next step needs, and a stats sheet with the hidden damage stats explained). Click anything to read it in full with
  your current numbers. During a run the book opens here (setting *Codex → OpenOnYou*).
- **Bestiary**: enemies, bosses and the 15 **dark enemy** abilities (what each does, with its numbers).
- **Arsenal**: a full page for every skull, item, quintessence, inscription and Dark Mirror ability, with real
  numbers: a skull's awakening path, attacks and skills (a skill's numbers appear after you used it once), an
  item's inscriptions and transformation, an inscription's steps and every item that carries it.

Names in the texts are links. **< Back** (top right), Backspace or the mouse's back button return to the page
before.

![Codex enemy page](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-enemy.png)

<details>
<summary><b>How entries unlock (★ / ★★ / ★★★)</b></summary>

| Stars | Enemies | Bosses & adventurers | Skulls, items, essences |
|---|---|---|---|
| ★ | met: ink outline | met: outline | seen |
| ★★ | colour + first animations | 1 win | picked up once |
| ★★★ | every animation | 3 wins | picked up 5 times |

Enemy thresholds are balanced **per enemy** from the game's level data: about **one run's worth** of that enemy
for ★★ and **five runs' worth** for ★★★. A Caerleon recruit (about 100 per run) needs 100 / 500 kills, a
rare enemy only 3 / 15. So every entry takes about the same number of runs. The bar under the picture always
shows what's next.
</details>

<details>
<summary><b>Boss move lists and the "???"</b></summary>

- For every boss and adventurer, the mod reads the **complete list of moves straight from the game's AI**, grouped
  by fight phase (e.g. "Phase 2 · Fist power slam", or "Pair phase / Single phase" for the Leiana sisters).
- Moves you haven't seen yet are shown as a black silhouette named **???**. The list is complete, so you always
  know how many moves are still missing.
- A move counts as seen once the boss actually performs it in front of you.

![Codex boss page](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-boss.png)

![Codex inscription page](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-inscription.png)
</details>

<details>
<summary><b>Fight films</b></summary>

- The first time a boss does a move, the mod films it (a small picture, 10 times a second; your own character
  is hidden).
- In the book, switch between **Animations** (posed pictures) and **Attacks (filmed)** with the chip at the
  bottom left of the picture.
- Don't like a take? Press **Refilm**: the next time the boss does that move, it is filmed again.
- Filming can be switched off (*Codex → FilmBossAttacks*).

![Fight films](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/codex-film.gif)
</details>

<details>
<summary><b>Dark Mirror</b></summary>

Dark Mirror versions of bosses are recorded separately: their moves gain extra effects (Yggdrasil's sweeps throw
debris, his slam raises a spike under you, …). Use the **Normal / Dark Mirror** chip in the picture box to switch.
Dark Mirror-only bosses get their own pages.
</details>

<details>
<summary><b>Tips for completing boss pages</b></summary>

- Many moves only appear in an HP range (e.g. below 80 % or 50 %). Take your time in each range.
- Distance matters: some moves only happen when you are close, others when you are far away.
- Special skills of adventurers can be **interrupted** by dealing enough damage while they cast: that's how you
  see their **Groggy**.
- Some moves have long cooldowns (up to 2–3 minutes).
</details>

---

## Installation

**r2modman / Gale (recommended):** search for *SkulInsight QoL*, install, start with **Start modded**. BepInExPack
Skul is installed automatically.

<details>
<summary><b>Manual installation</b></summary>

1. Install [BepInExPack Skul](https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/) into the
   Skul folder (the folder containing `Skul.exe`).
2. Start the game once and close it.
3. Copy `DamageInsight.dll` into `Skul/BepInEx/plugins/SkulInsight_QoL/`.
4. Start the game.
</details>

<details>
<summary><b>Linux and Steam Deck</b></summary>

The mod runs through **Proton**. Skul's native Linux version can't load it.

1. Steam → right-click Skul → *Properties* → *Compatibility*: force Proton (for example Proton 9.0).
2. Start Skul once from Steam without mods, then quit (this creates Skul's Proton prefix).
3. Leave Skul's launch options empty (the Flatpak version of r2modman is the exception).
4. Install the mod in r2modman or Gale and press **Start modded**.

Errors, Steam Deck, manual installation and a check script: the
[Linux and Steam Deck guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/LINUX.md).
</details>

<details>
<summary><b>macOS</b></summary>

The mod runs on Skul's **Mac version** (Intel and Apple Silicon); one Terminal script sets it up.
1. Start Skul once from Steam without mods, then quit.
2. Open Terminal, paste this line and press Enter:
   `curl -fsSL -o ~/Downloads/install-mac.sh https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/tools/mac/install-mac.sh && sh ~/Downloads/install-mac.sh`
3. The script prints a launch option and puts it in your clipboard: Steam → right-click Skul → *Properties* →
   *General* → *Launch options*, press Cmd+V. Then start Skul from Steam.

Manual installation, errors and how it works: the [macOS guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md).
</details>

<details>
<summary><b>All settings</b></summary>

| Section | Setting | Default | What it does |
|---|---|---|---|
| Damage Numbers | ShowSourceTag / UseIcons | on / on | Source icon or tag on damage numbers |
| Damage Numbers | HoldSeconds / FadeSeconds | 0.8 / 0.7 | How long numbers stay and fade |
| Damage Icons | (per source) | | Which icon each damage source uses |
| Boss Health Bar | ShowNumbers | on | HP numbers on big health bars |
| Descriptions | ShowDamageNumbers | on | Real numbers in all descriptions |
| Descriptions | PreviewPickup | on | Pickup / swap preview |
| Combat Log | ToggleKey | L | Opens the combat log |
| Combat Log | RecordCalculations | on | Per-hit calculation (hover a line) |
| Combat Log | SaveToFile | on | Log files (newest 30 kept) |
| Mini Log | Enabled, Lines, HideAfterSeconds | off, 4, 5 | Small log above the minimap |
| Cooldown Ticker | Enabled / Opacity | on / 0.55 | Seconds on HUD icons |
| Codex | Enabled / ToggleKey | on / K | The Codex book |
| Codex | FilmBossAttacks | on | Film each boss move once |
| Codex | OpenOnYou | on | During a run the book opens on the You page |
</details>

---

## FAQ & troubleshooting

<details>
<summary><b>Does it change the game or my save?</b></summary>

It changes nothing about balance or drops: it only shows information the game already uses internally. Your save
is only touched when you switch save slots (Esc → SkulInsight QoL → Save slots); the game's save is backed up before
every switch.
</details>

<details>
<summary><b>Linux: error about user.reg, or the game starts without the mod</b></summary>

- **`ENOENT … compatdata/1147560/pfx/user.reg`** or **"compatibility data does not exist"**: Skul has never run through
  Proton. Force Proton (Steam → *Properties* → *Compatibility*), start Skul once without mods, then press Start
  modded again.
- **Skul closes right away after Start modded:** Skul's launch options contain `linux_wrapper.sh` (r2modman's setup
  for native games). Clear the launch options.
- **The game starts without the mod:** Steam runs the native Linux version (the game folder has `Skul.x86_64`, no
  `Skul.exe`). Force Proton.

More fixes and a check script: the
[Linux and Steam Deck guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/LINUX.md).
</details>

<details>
<summary><b>macOS: the game starts without the mod, or doesn't start</b></summary>

- **No damage numbers, no SkulInsight QoL in the pause menu:** the launch option is missing or mistyped. It needs
  the quotes around the path and `%command%` at the end.
- **Skul doesn't start any more:** run `sh ~/Downloads/install-mac.sh --check` and look at the report; usually
  running the installer again fixes it. To play without mods, empty the launch option.
- **r2modman / Gale / Thunderstore App:** they have no Mac version, and BepInExPack Skul is for Windows. Use the
  installer script.

More fixes: the [macOS guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md).
</details>

<details>
<summary><b>A feature stopped working after a game update</b></summary>

At startup the mod checks all of its hooks and switches off only what broke; the rest keeps working. The results
are in `BepInEx/LogOutput.log` as `[SelfTest]` lines. Please report it.
</details>

<details>
<summary><b>Known gaps in this version</b></summary>

- King Alexander's hands, First Hero's later forms and the bomb phase have no films or notes yet.
- Films made before 0.11 may end early or show your own effects: use "Refilm" on a move, or "Reset Codex" on the
  settings page to start the book over (the move notes stay).
- Arsenal pages show what you have met; a skill's numbers appear after you used it once.
- Translations are not checked by native speakers yet; corrections are welcome (see *All game languages*).
</details>

<details>
<summary><b>Reporting a bug</b></summary>

Open an issue on [GitHub](https://github.com/Umut-Aslan99/SkulInsight_QoL/issues) and attach the newest file from
`BepInEx/DamageInsight/CrashReports` (in the game: Esc → SkulInsight QoL → Crash reports folder → Open),
`BepInEx/LogOutput.log`, the mod version and what you did. Screenshots help a lot. After a crash, start the game
once more before sending: the crashed session's report is completed at the next start, with a verdict on whose code
the errors were in (the game's, SkulInsight's or another mod's).
</details>

---

## Coming next

- **0.15 (this version):** rewards for achievements, coins that summon gear, kill quests, inscription activations.
- **0.16:** Dark Mirror page. **Later:** skins for skulls from the daily achievements.
- **Later:** knowledge pages: drop chances, run layout, shops and prices, NPCs, game mechanics.
