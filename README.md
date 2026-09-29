# SkulInsight QoL

**See the real numbers behind Skul: The Hero Slayer.** Damage numbers that tell you where damage came from,
item and skull descriptions with your actual damage, a combat log that explains every hit, and a preview of what
picking up an item would do to your build.

A quality-of-life mod: it changes nothing about the game's balance or saves. It only shows information the game
already uses internally. Every feature can be switched off in the config.

> **Version 0.9.3: first public beta.** Please report bugs and ideas (see [Feedback](#feedback--bug-reports)).

<!-- SCREENSHOT: overview (fight with damage numbers + combat log) -->

## Features

### Damage numbers with their source
- Every damage number shows where it came from: an icon or a colour-coded tag.
  - Basic attack, skill, item, essence, dash, swap, dark ability
  - Poison, burn, bleed and other statuses
- Numbers stay on screen a little longer and fade out smoothly (both configurable).

<!-- SCREENSHOT: damage numbers -->

### HP numbers on big health bars
Bosses, dark elites, veterans and the adventurer party show `current / max` HP (and shields) on their health bars.

### Real numbers in every description (League-of-Legends style)
Descriptions of skulls, skills, swaps, items, essences and inscriptions show the damage **with your current
stats**, e.g. `60–94 Physical (7–11 skull dmg x 530% x 160% phys. atk)`.
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

<!-- SCREENSHOT: item description with numbers -->

### Pickup and swap preview
- Items on the ground and in shops show their numbers **as if you had already picked them up**. The item's
  stats and every inscription step it would activate are included.
- The popup says what changes:
  `If picked up: Arms 1 > 2 · Courage 1 > 2 · Phys. atk 191% > 355.2%`
- With a full inventory, the swap menu shows the numbers **after swapping** the selected item.

<!-- SCREENSHOT: pickup preview -->

### Combat log (press `L`)
- Every hit, grouped by room, updated live.
- Filters: dealt / taken, room / all rooms, enemy type, damage type, source, crits only, minimum damage.
- Pie chart of your damage by source or damage type.
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

<!-- SCREENSHOT: combat log with calculation tooltip -->

### Cooldown ticker
Item and ability icons at the bottom of the screen show their remaining cooldown, WoW style:
`83s`, `2m+`, `10m`.

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

## Configuration

The settings file is created on the first start:
- `BepInEx/config/docrun.skulinsight_qol.cfg`
- in r2modman / Gale: *Config editor*

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
| Combat Log | SaveToFile | on | Log files in `BepInEx/DamageInsight/CombatLogs` (newest 30 kept) |
| Combat Log | UseDialogueBackground / BackgroundOpacity | on / 0.85 | Look of the log window |
| Mini Log | Enabled, Lines, HideAfterSeconds, ShowPie, GapAboveMinimap | off, 4, 5, on, 40 | Small log above the minimap |
| Cooldown Ticker | Enabled / Opacity | on / 0.55 | Seconds on HUD icons |

## Compatibility
- Made for Skul: The Hero Slayer **1.9.x** (Steam, Windows) with BepInExPack Skul (BepInEx 5.4.21+).
- Doesn't touch saves, balance or content; safe to add or remove at any time.
- Should work next to content mods. Items the mod doesn't understand simply show no extra numbers.
  Please report anything odd.
- After a game update the mod checks all of its hooks at startup and switches off only what broke. The results
  are in `BepInEx/LogOutput.log` as `[SelfTest]` lines.

## Known limitations
- A few item effects that aren't direct damage (buffs, heals, chances) show no extra numbers yet.
- The pickup preview covers item stats and plain stat inscriptions. Conditional buffs and bonus-inscription items
  are not included yet. Skulls and essences on the ground have no preview yet.
- Shock and ember damage from some sources may be labelled with a generic tag.

## Roadmap
- In-game options menu for all settings
- **Codex / Bestiary:** a book with every enemy, boss, item and inscription, unlocked by playing (kill counters,
  attack animations, boss patterns)

## Feedback / bug reports
Please open an issue on GitHub: <REPO_URL>/issues. Attach
`BepInEx/LogOutput.log` and tell us the mod version and what you did. Screenshots help a lot.

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
