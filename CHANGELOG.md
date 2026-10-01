# Changelog

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
