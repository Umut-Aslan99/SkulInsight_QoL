# SkulInsight QoL – Guide

**See the real numbers behind Skul: The Hero Slayer.** SkulInsight QoL shows where every point of damage comes
from, puts your real damage into every description, explains each hit in a combat log, previews pickups, and
adds the **Codex**: a bestiary with every enemy, boss and item, complete boss move lists and fight films you
record yourself.

It is a quality-of-life mod: it changes nothing about balance, drops or saves, and every feature can be switched
off. It is safe to add or remove at any time.

![Overview](https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/docs/images/overview.jpg)

---

## Quick start

| Key | What it opens |
|---|---|
| **K** | Codex (bestiary) |
| **L** | Combat log |

Everything else works on its own while you play. **Settings:** pause the game (Esc) → **SkulInsight QoL**, right
under the game's own Settings. Every option is there with an explanation; changes apply at once. (They are also in
r2modman / Gale → *Config editor* → `docrun.skulinsight_qol`.)

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

## The Codex (K), new in 0.10

A book with every enemy, boss, adventurer, skull, item, essence and inscription you have met: your kills, damage
dealt and taken, deaths, and descriptions. Nothing is shipped with the mod: every picture and film is created on
your own PC while you play.

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
</details>

---

## FAQ & troubleshooting

<details>
<summary><b>Does it change the game or my save?</b></summary>

No. It only shows information the game already uses internally. Your save, balance and drops are untouched.
</details>

<details>
<summary><b>A feature stopped working after a game update</b></summary>

At startup the mod checks all of its hooks and switches off only what broke; the rest keeps working. The results
are in `BepInEx/LogOutput.log` as `[SelfTest]` lines. Please report it.
</details>

<details>
<summary><b>Known gaps in this version</b></summary>

- King Alexander's second phase is not in the Codex yet (its moves are not recognized); some Dark Mirror films are
  cut too short or split into parts (Dark Skul's bone rains, javelins, ...). Fixes come in 0.11.
- Some move names are still rough (a few First Hero and First Dark Hero moves appear in parts).
- Translations are not checked by native speakers yet; corrections are welcome (see *All game languages*).
- Move descriptions exist only for some bosses so far.
- Films can show effects of your own items around the boss.
</details>

<details>
<summary><b>Reporting a bug</b></summary>

Open an issue on [GitHub](https://github.com/Umut-Aslan99/SkulInsight_QoL/issues) and attach
`BepInEx/LogOutput.log`, the mod version and what you did. Screenshots help a lot.
</details>

---

## Coming next

- **0.11:** Codex films and animations cleaned up (complete boss moves, effects and projectiles of enemies).
- **0.12:** full Codex entries for skulls, items, essences, inscriptions and dark abilities, unlocked by playing; a
  character page with your whole build.
- **0.13:** knowledge pages: drop chances, run layout, shops and prices, NPCs, game mechanics.
- **0.14:** Dark Mirror page. **0.15:** optional Codex rewards.
