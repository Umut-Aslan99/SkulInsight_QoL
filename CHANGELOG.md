# Changelog

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
