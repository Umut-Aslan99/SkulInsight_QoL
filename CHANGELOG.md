# Changelog

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
