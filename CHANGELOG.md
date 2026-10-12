# Changelog

## 0.12.1 (hotfix: the What's new button)

- **What's new:** the "Got it" button now closes the window with a click (before, only Esc or Enter did), and the
  mouse cursor stays visible while it is open. Players who already saw the 0.12 window don't get it again.
- **Buttons:** every button of the mod (Codex, combat log, What's new) lights up under the mouse and plays the game's
  own menu sounds when you move onto it or click it. New setting **Menu sounds** (on) turns the sounds off.

## 0.12.0 (the Codex becomes a full reference; short descriptions, all numbers on Shift; save slots)

**Highlights**
- **The Codex is your reference book:** three sections (**You**, **Bestiary**, **Arsenal**) with every skull, item,
  quintessence, inscription and dark ability, all their numbers, and clickable links between them.
- **Short descriptions in the game, every number in the Codex:** the game's description boxes stay short and show the
  mod's numbers while you **hold Shift**, as one compact line per attack. The Codex keeps every hit with its
  calculation.
- **Save slots:** keep up to 5 whole games side by side, each with its own Codex, and switch between them from the
  settings page.

**Codex**
- Sections on the title row: **You**, **Bestiary** (enemies, bosses, dark enemies) and **Arsenal** (skulls, items,
  quintessences, inscriptions, dark abilities), each with its own tabs.
- Links and Back: names in page texts are clickable (an item's inscriptions, an inscription's items, a skull's
  awakened forms, what an item turns into). "< Back" (top right), Backspace or the mouse's back button returns to the
  page before.
- Arsenal pages with real numbers (without your items, so pages compare): skulls (type, passive, awakening path,
  every attack, each skill with cooldown and damage, swap, transformed bodies, summons), items (inscriptions, damage
  per part, what they turn into), quintessences, inscriptions (every step, the tuned form, every item that carries it)
  and the Dark Mirror's dark abilities (each level's values, price and, for the damaging ones, their damage per level).
- **Boxed pages:** every section of a Codex page (Attacks, Skills, Swap, Damage, Inscriptions, Steps, the stats sheet,
  ...) is a framed box with an icon, so pages read at a glance.
- **Skill boxes on skull pages:** each skill gets a box with its icon, name, description and numbers. A skill stays a
  "?" until you **win a run with it**: beat a boss of chapter 5 or later (normal or Dark Mirror) while the skill is on
  one of your skulls. The You page always shows the skills you hold.
- **Every number with its whole formula:** each hit on a Codex page shows base x hit % x attack % (also when a factor is
  100 %), so every page reads the same way. Arsenal pages use neutral stats (100 % attack) to compare gear; **hold
  Shift** on a page to see the numbers with your run's stats instead: what that item or skull would do in your build.
- **You** page: your run as a Codex inventory: both skulls, the quintessence, your dark abilities, the 9 items, your
  inscriptions with what the next step needs ("2 more for step 3") and a stats sheet (the game's stats plus the hidden
  damage stats, each explained). Click anything to read it in full with your current numbers. During a run the book
  opens on this page (setting Codex/OpenOnYou).
- Dark enemies (Bestiary): the 15 abilities dark elites can have (Protection, Immortal, Intense Cold, Tornado, ...)
  with what they do and their numbers; met and killed are tracked like other entries (unlock numbers on the settings
  page).

**Save slots** (Esc → SkulInsight QoL → Save slots)
- Up to 5 slots (setting: 3 to 5), each a whole game: story, NPCs, the witch's upgrades, the Dark Mirror, currencies
  and the run in progress, with its own Codex.
- **Play** switches: the game you were playing goes into its slot (a run in progress waits at the room you entered
  last), the other game comes in through the title screen; an empty slot starts a new game. **Copy my game here**
  copies your current game (with its Codex) into a slot; **Clear** empties one.
- Each slot shows a summary (run, story, NPCs, witch levels, dark quartz, Dark Mirror, deaths, last played). Keys,
  sound and language stay the same in every slot. The game's save is backed up before every switch (the newest 5
  backups are kept).

**Descriptions in the game**
- **Hold Shift for the numbers** (player request): the inventory, items on the ground and in shops, and the swap menu
  show the game's own text plus "Hold Shift for damage numbers" (also on skills, swaps and inscriptions in the detail
  views); hold Shift and the mod's numbers appear. Either Shift key works. Settings: Descriptions/HoldToShow (on) and
  Descriptions/HoldKey.
- **Compact numbers:** one line per attack with its hits side by side ("Basic attack: 10–12 · 22–24 · 36–42 Magic"),
  equal hits merged ("4 x 11–13"), one line per summon, a transformed body's basic, jump and dash attack, and "All
  numbers: Codex (K)" under skull texts. The longest skull texts went from 79 lines to 10 and now fit their boxes in
  every language. Setting "Compact numbers" (on; off: every hit with its formula). It replaces the "Short descriptions"
  setting.
- Summons have real names (Recruit, Archer, Assassin, Man-at-Arms, Hamlet, King Lear, Clone, Crew, Turret...) instead
  of internal ones like "Recruit 4 Swap BuddyArcher"; a quintessence's summon uses the quintessence's name, and a
  transformed body's skills use the game's own skill names.
- Inscription texts in the game's boxes are compact too: status numbers (burn, poison, bleed...) and each extra
  attack on one line without the calculation (a long German burn text ran into the next step); the Codex keeps it.
- Chain attacks are numbered "Part 1, 2, 3" (parts without damage were counted before: "Part 3, 5, 7").

**Combat log**
- **Damage table** (View: Damage table, in the combat log's filter bar): your damage per piece of gear instead of only
  per source: each skull split into basic attack, skills, dash and swap, every item, quintessence, inscription and
  dark ability on its own row, statuses and summons, each with damage, share, hits and the biggest hit. Follows the
  log's filters (room, enemies, type). Needs "Record calculations" (on by default) to know which item dealt a hit.

**Damage numbers fixed**
- The transformed King Arthur (King of Heroes) no longer lists "+ 24-28 Magic + 24-28 Magic" on every hit: gaining mana
  was counted as his sword wave's damage.
- The Mummies' "Passive" line no longer adds up every gun's hits (the guns are listed as basic attacks).
- Damage numbers for the last skull skills that had none: the Genie's lamp punch, the Golden Gargoyle's statue burst,
  Davy Jones' special cannonballs (plus the enhanced cannonball of every 3rd reload), Minotaurus' Blunt Strike
  follow-up hit, the Glacial Skull's swap shards; The Chosen Mage's Badge shows its fireballs and their explosions.
  Davy Jones' normal attack no longer lists every cannonball type.

**What's new window**
- The first time 0.12.0 reaches the castle, a small window lists these highlights once (setting "What's new"), so you
  see what changed even when you update through a mod manager.

**Other fixes**
- The mini log no longer covers the inventory or other game menus: it hides while one is open.
- Typing in a text field (the Codex search, the game's developer menu) no longer opens the combat log (L) or closes the
  Codex (K); the letter goes into the text.
- On the settings page, a section heading no longer shows over the page frame below the list.

**Languages**
- Every new text in all game languages, including enemy projectile and move names met in the Dark Mirror.
- Every description was checked in all 11 languages: in the game (each skull, item and quintessence on the ground and
  in the inventory) and offline (every text the mod writes).

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
