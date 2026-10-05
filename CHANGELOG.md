# Changelog

## 0.11.0 (cleaner films and animations, move notes)

- Codex films: a take now waits for what the move leaves behind (falling bones, meteors, javelins, a thrown head)
  instead of stopping when the boss's own motion ends. Short takes filmed by older versions are filmed again once.
- Codex moves: a windup and its hit are one move (Dark Skul's "Special move"), long and short dashes are one "Dash",
  and the setup at the start of a fight ("Initialize") is no longer listed. Your progress is updated automatically.
- King Alexander's second phase: his heart's attacks are read and filmed (21 moves: its patterns and the machine's
  lasers, drill missiles, buzz saws, oil and bouncy balls).
- First Dark Hero: the attacks he makes beside his other moves (thorns, dark orb fragments, wall blasts, the big dark
  orb) are listed as moves of their own.
- Codex pictures: sets that only showed the idle pose ("Jump", "Fall" of enemies that never jump) are hidden; pieces
  that appear twice (Pope's two dark crystals) are shown once; an enemy's projectile is added to its pictures the
  first time it fires one.
- Codex move notes: every boss move now has a short note in all languages: what it does and how to deal with
  it (Yggdrasil, Leiana sisters, Chimera, St. Joan II, First Hero, the adventurers, Dark Skul, First Dark Hero, King
  Alexander), including numbers from the game (cooldowns, health thresholds, Dark Mirror differences).
- Codex "When:" line: cooldowns written as cool-time blocks are read too (First Hero's Big bang, St. Joan's Super
  baptism and Divine cross) and "while you are in the air" is shown where a move needs it.
- Clean films (new setting, on): boss films leave out your damage numbers, your skill and item effects, your summons
  and status effects on the boss (poison, burn, freeze, ...).
- Compact films (new setting, on): new films take about a quarter of the space (JPG instead of PNG).
- Entrances, sleeping and deaths are no longer filmed (the book never showed them); existing ones are removed once.
- Reset Codex (settings page, Codex section): deletes everything the Codex gathered at the next game start, so you can
  fill it again; the move notes stay.
- Damage numbers: the counter hit of parry skills (Shield Bash, Evading Slash, Ready to Charge), Minotaurus' Stomp and
  Headbutt, Nature's Grip and more skull skills now show their damage (about 50 more skill parts).
- Transformations: skulls that change into another body (Nightmare's Hell Bike, Devil Berserker, King Arthur, Archmage,
  Yaksha, Dominator) list that body's attacks with damage numbers in their description.
- Short descriptions (new setting, off): popups show one line per attack part with its total damage instead of the
  full calculation (player request, GitHub issue #3).

## 0.10.2 (cooldown seconds everywhere, screenshots)

- **Player request:** cooldown seconds now also on the **skill icons** of both skulls, the **swap** icon and the
  **quintessence** icon, with your cooldown speed bonuses included (the number is real seconds, not the base cooldown).
- Cooldown opacity changes without a restart.
- Settings page: the mouse wheel now scrolls the list while the pointer is over a setting too (it stuck there).
- README and guide: screenshots.

## 0.10.1 (settings in the game, all game languages)

- New: **the mod speaks every language the game has** (English, 한국어, 日本語, 简体中文, 繁體中文, Deutsch, Español, Português (Brasil), Русский, Polski, Français). It follows the game's
  language setting and switches live; the settings page has a *Language* row to pick another one. Descriptions,
  combat log, calculations, damage tags, the Codex (incl. boss move names and hints) and the settings page are
  translated, using the game's own terms. Translations can be corrected without an update: put a file with your
  changes into `BepInEx/DamageInsight/Lang/<code>.json` (see the guide).
- New (**player request**): **settings page in the game.** Pause (Esc) → **SkulInsight QoL**, right under the game's own Settings. Every
  setting of the mod in one list, styled like the game's options, with keyboard, mouse and controller. The right
  side explains the selected setting and shows its default. Changes apply at once and are saved right away; a key
  is changed by selecting it and pressing the new key. Also: reset the combat log window position, reset everything.
- The Codex unlock numbers (runs to ★★ / ★★★, boss wins, pickups) can now be changed by everyone (settings page or
  the config section *Codex balance*).
- Combat log background and opacity now change without a restart.
- Codex: boss moves show **when** the boss uses them (HP range, distance, cooldown, chance...), read from its AI,
  once you have beaten it (can be switched off: *Move hints*).
- Codex: cleaner move names (designer notes removed, split parts of one move joined, "(enhanced)" versions).

## 0.10.0 (Codex)

- New: **Codex** (key K), a bestiary of every enemy, boss, adventurer, skull, item, essence and inscription you have
  met, with your own stats (kills, damage dealt and taken, deaths) and descriptions.
- Pictures unlock as you play: outline when met, silhouette, then colour and every animation. Thresholds are
  balanced per enemy from the game's level data (about one run's worth of an enemy for ★★, five for ★★★).
- Bosses and adventurers: the complete move list, read from the game's AI and grouped by fight phase. Unseen moves
  are shown as "???".
- Fight films: each boss move is filmed the first time it happens; switch between posed animations and films, and
  mark a take for a refilm.
- Normal and Dark Mirror versions of bosses are kept apart.
- Known gaps (see README): parts of Dark Mirror (Dark Skul phase 2, King Alexander) are not fully analysed yet;
  some move names are rough.

## 0.9.3 (important fix)

- Fixed: bosses whose intro opens their health bar during a cutscene (e.g. the chapter 1 boss) could stay invulnerable in phase 2 and show no health bar. The HP-number feature crashed inside the cutscene.
- Every hook of the mod is now guarded: if something in the mod fails, it logs a warning and the game continues normally. An automated test makes sure this stays true.

## 0.9.2

- Fixed an error that could make an attack fail when the combat log looked up which item dealt it (spirits, summons).
- The combat log calculation and the cooldown ticker can no longer break the game if something unexpected happens; they log a warning instead.

## 0.9.1

- Fixed broken special characters (dashes, dots) on the Thunderstore page.

## 0.9.0: first public beta

- Damage numbers with source icons/tags; longer, smoother fade.
- HP numbers on boss, dark elite, veteran and adventurer health bars.
- Real damage numbers in skull, skill, swap, item, essence and inscription descriptions:
  - charge levels, summons and spirits, item transformations
  - Oberon, statuses, tuned inscriptions and the witch's "Tuned" box
- Pickup and swap preview: items on the ground, in shops and in the swap menu show the numbers you'd have after
  taking them.
- Combat log (`L`): filters, pie chart, per-hit calculation on hover (with icons), hits named after the item/skull
  that dealt them, log files per session; optional mini log.
- Cooldown ticker on the HUD's item and ability icons.
