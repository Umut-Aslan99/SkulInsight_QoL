# 0.12 spec: Arsenal full entries + "You" (the Codex inventory) + dark abilities

Written 2026-10-05 ~02:15 by Claude during the unattended night session, from CODEX_FINAL.md (layout, Umut's
decisions of 2026-10-04) and CODEX_PLAN.md section C. **Agreed with Umut on 2026-10-05 ~14:00** (consensus below);
choices Claude still makes alone during the build are marked **[DECISION]** in docs/V012_LOG.md.

## Consensus (Umut, 2026-10-05)
- **"You" = a better, in-Codex version of the game's inventory / character sheet.** The game's inventory has too
  little room per item; the Codex page has room for the full description, the damage calculations and links.
- **In-game descriptions stay** as they are, with the optional short setting (Descriptions/Short, built in 0.11).
- **Pages for every dark ability with their damage numbers.** Two kinds exist in the game, both get pages:
  the player's Dark Mirror shop abilities (`Characters.Gear.Upgrades`, today's "Dark" tab) and the elite enemies'
  dark abilities (`Characters.Abilities.Darks`: Ballista, Ghost stories, Living armor, Protection, Quarantine,
  Recharge, ...).
- Points 1-4 of the plan as proposed: records + achievements move to 0.12.1; gear pages show neutral numbers, the
  "You" page current numbers; unlock rules (section 2); the book opens on "You" during a run, else on the last page.
- The open 0.11 points (King Alexander retest, Codex reset, film fixes) wait; they stay in PROJECT_STATUS TODO.

## Scope
- **In 0.12**: book sections with sub-tabs, page history and links; full Arsenal entries (skulls, items,
  quintessences, inscriptions, the player's dark abilities) with real numbers and cross-links; elite dark ability
  pages (Bestiary); the "You" page; damage-scan leftovers (docs/DAMAGE_SCAN.md).
- **0.12.1**: records (the game's own counters) and achievements (Steam-like, hidden ones veiled).
- Already shipped in 0.11: short in-game descriptions (issue #3), transformed bodies' numbers.

## 1. Book layout
| Section | Sub-tabs |
|---|---|
| **You** | one page (no list): the Codex inventory |
| **Bestiary** | Enemies, Bosses, Elites (dark enemy abilities) |
| **Arsenal** | Skulls, Items, Essences, Inscriptions, Dark abilities (the player's) |
Later versions add sections (World, Economy, Mechanics, People: 0.13; Dark Mirror: 0.14).

- **Links**: names inside page texts are clickable (an item's inscriptions, an inscription's items, a skull's
  awakening tiers, an item's transformation target, an elite ability's summons...). **Back** returns to the page
  before (history of up to 30 pages); clicking a link also switches the tab to the target's.
- **Gear page layout** (all Arsenal entries): small picture at the top, title, subtitle (rarity · kind ·
  stars), unlock progress, then one long scrollable text. Enemy pages keep the big picture + films layout.

## 2. Arsenal entry pages
| Kind | Text, top to bottom |
|---|---|
| Skull | type (Power/Speed/Balance/Ranged), the game's description (passive), awakening path (tiers, linked), basic / jump / dash attack numbers, each skill (name, description, cooldown, numbers), swap (numbers), transformations, summons, flavor |
| Item | the game's description, its 2 inscriptions (linked), trigger/cooldown + numbers per part, summons, what it turns into (linked + numbers), flavor |
| Quintessence | description, cooldown, active numbers, summons, flavor |
| Inscription | every step with the game's text and its numbers, the tuned (true form) text, every item that carries it (linked) |
| Dark ability (player) | description per level (the game's own a/b/c values), price per level, numbers where it deals damage |
| Elite ability | what it does (game text), numbers (summons, projectiles, auras), which enemies can't get it |
- **Numbers**: Arsenal pages use **neutral stats** (no items, 100% attack: what the gear does by itself), so pages
  compare; skull-damage hits show the skull's own base on skull pages and "x% of skull damage" elsewhere.
  The "You" page uses the player's current stats.
- Data: the gear's prefab is loaded through its GearReference and analyzed with the same GearAnalyzer as the
  in-game descriptions (cached per gear); texts from the game's string table, so all 11 languages are free.

## 3. Unlock rules (Pokédex)
- Item / quintessence: silhouette when seen, full page after the first pickup (today's tiers).
- Skull: page when first held; each skill's numbers after using that skill once [DECISION: shown greyed "use it once
  to reveal" until then]; awakening tiers show their names from the start, their pages unlock when held.
- Inscription: page when first active (any step); higher steps' texts greyed until reached once.
- Elite ability: page when first met on an elite; numbers after killing an elite with it.
- Spoilers hidden; knowledge pages always open.

## 4. "You" page (the Codex inventory)
- **Left page** like the game's inventory: both skulls (current first), the quintessence, the 9 item slots as a
  grid, the dark abilities (Dark Mirror runs), the active inscriptions with their step and what the next step needs
  ("2 more for step 3"), and a **Stats** button.
- **Right page**: the selected thing in full (same text as its Arsenal page, but at **current stats**, plus a link
  to its Arsenal page). Stats: every stat the game's panel shows, plus the hidden damage stats the descriptions use
  (projectile / basic / skill damage, crit damage, status stats), each with a short explanation.
- Updates live while the book is open. Outside a run: "Start a run to see your character here."

## 5. Build order
1. Framework: sections + sub-tabs, page history (Back), inline links, gear page layout; navigation tests.
2. Item + quintessence pages (prefab analysis by reference, neutral stats).
3. Skull pages (skills, awakening path; skill-use unlocks).
4. Inscription pages (+ "items carrying it" index).
5. Dark abilities: the player's (numbers for the damaging ones), elite abilities (data research first).
6. "You" page.
7. Damage-scan leftovers: Genie Fairy Lamp, Golden Gargoyle Stone_3, Davy Jones' cannonballs, Chosen Mage's Badge,
   World Tree Seed.
8. Texts in all 10 languages (with each step), tests, README/wiki/changelog, release.

## 6. Test plan for Umut (when built)
- Arsenal: open an item page: numbers shown, its inscription names jump to the inscription page, Back returns.
- Skull page: skills with numbers, awakening tiers linked.
- "You" during a run: numbers change when a buff is active (e.g. Berserker's howl); selecting an item shows it in
  full; Stats shows every stat.
- Elites: meet a dark enemy, its ability page appears.
