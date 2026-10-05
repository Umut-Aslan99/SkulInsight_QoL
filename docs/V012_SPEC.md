# 0.12 spec: Arsenal full entries + "You" (character page)

Written 2026-10-05 ~02:15 by Claude during the unattended night session, from CODEX_FINAL.md (layout, Umut's
decisions of 2026-10-04) and CODEX_PLAN.md section C. Every choice Claude made alone is marked **[DECISION]**:
Umut can change any of them; nothing here is built yet.

## Scope
- **In 0.12** [DECISION]: full Arsenal entries (skulls, items, quintessences, inscriptions) with real numbers and
  cross-links; the character page ("You" tab, first page); the damage-scan fixes (docs/DAMAGE_SCAN.md); a setting for
  short in-game descriptions (GitHub issue #3).
- **Moved to 0.12.1** [DECISION]: records (the game's own counters) and achievements (Steam-like, hidden ones veiled):
  both need their own data research; keeping 0.12 shippable.
- Dark ability damage: needs a dev scan of `Characters.Abilities.Darks` first (tools/Dev/GearScan has no category
  for them). **[DECISION]** do the scan in 0.12 if it takes under a session, else move the page to 0.14 (Dark Mirror tab).

## 1. Arsenal entry pages
| Kind | Left page | Right page |
|---|---|---|
| Skull | portrait, name, rarity, type (Power/Speed/Balance), lore (game text), awakening path (tiers with their names) | basic attack, skills (each: game description + damage numbers at neutral stats, cooldown), swap attack, passive |
| Item | icon, name, rarity, game description, flavor text, its 2 inscriptions as clickable chips | damage numbers (neutral stats) per part ("Hit 1", "Summon: Imp"), cooldown/trigger, what it upgrades into |
| Quintessence | icon, name, rarity, game description | active/passive numbers, summons (turret, Imp...) |
| Inscription | icon, name, steps (2/4/6...) with the game text per step | every item that carries it (clickable), numbers per step where it deals damage (Arms, Brawl, ...) |
- **Numbers** [DECISION]: entry pages show *neutral* numbers (no items, base stats: what the gear does by itself) so
  pages are comparable; the character page shows numbers with the player's current stats.
- **Cross-links**: inscription chips on items → inscription page; inscription page item list → item pages; skull
  awakening tiers → the next/previous skull. Back button returns to the previous page (a small history stack).
- Data: GearDescriptions/GearAnalyzer already produce Breakdown per gear at runtime (with summons); the book reuses
  them; texts from the game's string table (Localization.TryGetLocalizedString) so all 11 languages are free.

## 2. Unlock rules (Pokédex)
- [DECISION] Item / quintessence page: unlocks on first pickup (today: "seen" shows silhouette, "picked up" full page).
- [DECISION] Skull page: unlocks when the skull is first held; each skill's numbers unlock after using that skill once;
  awakening tiers show their names from the start, their pages unlock when held.
- [DECISION] Inscription page: unlocks when the inscription is first active (any step); higher steps' texts show
  greyed until reached once.
- Spoilers hidden (Umut's decision 3); knowledge pages always open (decision 2).

## 3. Character page ("You" tab, first page)
- Left page: both skulls (current first) with skills and their damage **at current stats**, cooldowns with the current
  cooldown speed; quintessence with its numbers.
- Right page: the items (up to 9) as a grid; selecting one shows its numbers at current stats underneath; active
  inscriptions with their current step and the next step's requirement ("2 more for step 3").
- Updates live while the book is open (stats change with buffs); reads WeaponInventory, ItemInventory,
  QuintessenceInventory, InscriptionInventory (all used by today's descriptions).
- Opens with the Codex key; [DECISION] the book opens on the character page during a run and on the last page used
  otherwise.

## 4. Short in-game descriptions (issue #3) — BUILT 2026-10-05 night (setting Descriptions/Short, off)
- Setting `Descriptions/Short` [DECISION: off by default in 0.12, so current players see no change]: when on, the
  in-game popups show only the key numbers (one line per part) plus "Details: Codex (key)".
- Texts in all 10 languages, help text on the settings page (rule).

## 5. Build order (suggested)
1. Entry page framework: tab bar + page history + cross-link chips (CodexWindow), tests for the navigation model.
2. Item + quintessence pages from existing Breakdowns (neutral stats via StatSnapshot.Neutral).
3. Skull pages (skills, awakening path; skill-use unlocks via the existing skill hooks).
4. Inscription pages (+ the "items carrying it" index from the gear catalog).
5. Character page (live stats).
6. Short descriptions setting.
7. Damage-scan leftovers: polymorph skills (Hell Bike, Bone Howl), Davy Jones' cannonballs, Gargoyle statue, Genie lamp.

## 6. Test plan for Umut (when built)
- Open an item page: numbers shown, inscription chips jump to the inscription page and back.
- Character page during a run: numbers change when a buff is active (e.g. Berserker's howl).
- Short descriptions on: popups are one line per part; off: unchanged.
