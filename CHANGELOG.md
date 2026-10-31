# Changelog

## 0.16.0 (restart room, daily points 3, fixes from the first players)

- **Restart room** (press **F5** twice within 3 seconds): back to the moment you entered the
  room (health, gear, gold, everything), exactly as when you close the game and continue, without closing it. Your
  rewards (coins, quests, activations) go back too, so restarting can't farm quests. The run history notes each
  restart. In a **daily run** it ends the run, like leaving the game: only full runs without save-scumming count.
  Settings section **Restart Room** (on, key F5).
- **Daily points, formula 3** (from **2026-11-01**, so November's month board uses one formula; feedback from Tythos
  and his community): the parts that were the same for everyone who finished are gone (stages, kills, damage dealt;
  the game counts some kills twice when several hits land at once). Now: a win 20,000, speed 5 per second under an
  hour, a run that didn't win 1,000 per stage, 1,000 per boss fight won (from the run history, not the game's
  double-counted kills), 3,000 per optional Dark Mirror boss (Dark Skul, King Alexander), 150 per **flawless room**
  and 1,000 per **flawless boss fight** (no health lost; a shield or a dodge keeps it flawless), -2 per damage
  taken, 1 per 10 gold and 2 per bone left. **The day's achievements:** each day and board has four, worth 300 to
  1,500 each: two of the game's (only the logical ones: no-hit bosses, the First Hero as Little Bone, a minute in the
  black market, 4 dark abilities), a **skull of the day** (win holding it, any of its forms) and a **flawless
  chapter**. The Daily page lists them and ticks them off; the achievement notice shows. The day's records no longer
  add points and the daily streak (100 a day, up to 30 days) counts on the month board only, so the day's board is
  the run alone (what tournaments compare). Runs sent by 0.15 still count (their bosses from the run they sent).
- **Settings page, new layout:** the features on the left in groups: **Gameplay (changes the game)** (rewards, restart
  room), **Insight** (what only shows), **Helpers** (Clear view, healing orbs), **Online** (daily runs), **General**.
  Each feature has its switch right there; hovering or choosing one shows on the right what it does and its settings.
  Enter goes into them, Esc back. It shows the settings most players change (about a third); **All settings** at the
  end of the list switches on every setting, the fine-tuning too (timings, opacities, each damage source's icon, the
  Codex's films and unlock counts, the rewards' numbers).
- **A choice at the first start:** new players are asked "Only information" or "Information and gameplay"; with the
  first (and Esc) the rewards and Restart room start off. Players who update keep their settings.
- **Yesterday's winners as adventurers:** the adventurers you meet carry the names of yesterday's daily winners
  (each board's first place; in a daily run, that board's). Setting Daily → Winners as adventurers (needs daily runs).
- **Name for the board without typing in the game:** setting Daily → Name in the mod manager's config editor. Some
  input methods (Pinyin, IME) freeze full-screen games while typing; the name box starts with this name, so Send
  works without typing. The Daily page says so after a run.
- **Fixed: hovering selects again.** The mod's windows could leave their own UI event system running in place of the
  game's, so the mouse no longer selected items, skulls and dark abilities by hovering (a click bought a dark ability
  at once). The mod's stand-in now only exists while the game has none.
- **Fixed: stutters with strong builds.** Since 0.15 every kill rewrote the whole rewards file on disk; kills are
  now saved a few seconds later.
- **Performance in the crash reports:** once a minute frames per second, stutters and how much of each frame
  SkulInsight takes (its most expensive parts); a line per stutter; a performance summary. A **freeze watch** notes
  what was going on when the game stands still for 6 seconds (SkulInsight's code or not, a text box, an input
  method). Settings General → Performance in the reports (on) and **Show FPS** (a line in the top left corner).
- Run history: flawless rooms per run ("31 of 52 fights without losing health"); a boss counts whatever dealt the
  last hit (a daily run of 0.15 missed three bosses finished by damage over time).

## 0.15.0 (rewards, six daily boards)

- **Rewards** (Codex → You → **Rewards**), the first feature that changes the game's balance, a little. On by
  default; the new settings section **Rewards** is the first on the settings page and turns it all off (everything
  earned is kept). Everything belongs to the save slot and never counts in daily runs.
  - **Achievement rewards:** a lasting bonus for each of the 103 achievements, by theme. Boss beaten: +3 % damage to
    that boss (all its phases); without getting hit: +10 % more (multiplied: +13.3 %; The Strongest only in the Dark
    Mirror). Dark Mirror levels 0-10: physical and magic attack +1 % each. Legendary skulls: +0.5 % each by type
    (Power: physical attack, Speed: attack speed, Balance: magic attack). Danger (dying, traps): damage taken -1 %.
    Shops: gold gained +1 %. The castle and the world: movement speed +1 %. Dark abilities and inscriptions: cooldown
    speed +1 %. The hidden boss's trail: critical chance +1 %. Special: 100 adventurers +5 % damage to adventurers,
    The Skeleton King +10 % while holding Little Bone, Cold-Blooded critical damage +3 %, Rookies Welcome healing
    received +5 %. Added to your stats like the Witch's (the You page and the descriptions include them); the combat
    log's calculation names them "Achievement rewards". Achievements taken over from Steam count too.
  - Each achievement's page has a **Reward** section; the achievement notice shows the reward on a second line.
  - **Coins:** a legendary item or skull broken or left behind on a map gives its coin (a skull raised with bones gives
    its first form's; shop wares and summoned gear don't count). Every 5th win over one adventurer gives its coin,
    for its rare legendary item (The Chosen Warrior's Armor, Cleric's Bible, Hero's Circlet, Mage's Badge, Hunter's
    Arrow, Thief's Twin Swords). A run that ends with Omen items gives an Omen coin each, for an Omen item of your
    choice (one you have met). **Summon** (Rewards tab, or the gear's page; Omen items' pages for Omen coins) drops the
    gear at your feet, in a run, once the map is cleared: one summon per run for each kind of coin (legendary skull,
    legendary item, adventurer, Omen). A summon takes 3 coins of that gear (or 3 Omen coins; setting Coins per
    summon); an adventurer's coin is enough on its own. The adventurers' items are summoned in Dark Mirror runs
    (where they belong).
  - **Kill quests:** every normal enemy has an endless quest: about two runs' worth of kills of it (at least 10, at
    most 150) pays 25 dark quartz and 50 gold where it fell; dark enemies count double; a full run finishes about 21
    (about 535 dark quartz and 1,075 gold; the Kill quests page shows the estimate). Enemy pages show the quest as a
    square that fills up, the reward in its middle
    (adventurers: their coin's progress).
  - **Inscription activations:** completing an inscription in a run (its last step) earns one, paid when the run
    ends, won or lost; in a later run "+1 for this run" makes an inscription you have count one more until that run
    ends, like the Dark Mirror's banners (not Sun and Moon, Masterpiece, Sin or Omen); two per run, each inscription
    once.
  - The run history marks runs played with rewards on, with that run's quests and coins.
  - Settings: Rewards, Achievement rewards, Coins, Kill quests, Inscription activations, Summons per run (1 of each kind),
    Coins per summon (3), Activations per run (2), Quest length (2 runs), Quest dark quartz (25), Quest gold (50), Adventurer wins per coin (5).
- **Daily runs: six boards a day.** Normal, the Dark Mirror at a random level (0-10, the same for everyone that day)
  and Dark Mirror 10, each with and without the DLC. Each has its own seed, its own daily and monthly boards and one
  try; the Daily page picks the board with chips (and a DLC chip). The DLC boards bring the Mythology Pack's skulls
  and items and are only playable for players who own it (anyone can watch them); the boards without DLC are the
  same run for owners too (no DLC gear, no DLC map events). A streak counts any board. 0.14 keeps playing the
  Normal board without DLC.
- **Daily runs: the same start for everyone.** The castle's Fox and Ogre give everyone the same gifts and the Holy
  Grail the same blessing (they were rolled before the day's seed was set). A Dark Mirror daily has the Darktech
  machines up to the day's level only (a Dark Mirror 5 day has no Darktech of levels 6-10), and the daily slot's
  Codex is fully open. The Dark Mirror's first-time scenes count as seen in the daily slot, like in a save that
  cleared it: no Dwarf Engineer talks or machine intros, and the skull machine of Dark Mirror 7 and up works at once
  (it stayed off until its intro ran when your own slot had never been that far).
- The Daily page has a **Shoutout** to Tythos and his community, who use and promote the mod, with a link to his
  Bilibili page.
- Fixes: the Daily page said "No connection to the daily run server" after every finished run (the server's answer
  to a start sent again was taken for a lost connection); a second board played on the same day now reloads the
  castle for its own seed too; characters the game's fonts don't have (×, →, •) are replaced by ones they have.
- **What's new** lists 0.15, and one line about 0.14 for players who come straight from 0.13.

## 0.14.1 (the Thunderstore version follows Thunderstore's rules)

- 0.14.0 was taken off Thunderstore: "plugins updating themselves or other packages is not permitted" there; mod
  managers (r2modman, Gale) install and update mods. So in this version:
  - **SkulInsight doesn't update itself** any more (and the setting *Update automatically* is gone). r2modman and Gale
    update it as before.
  - **Mods** (pause menu) shows Thunderstore's Skul mods and yours side by side, with search, categories, sorting,
    each mod's details and a button to its Thunderstore page; it doesn't download, install, update, remove or switch
    mods (do that in your mod manager).
- Everything else is 0.14.0 as below.

## 0.14.0 (achievements; macOS)

- **Achievements** (Codex → You → **Achievements**): all 101 of Skul's Steam achievements as **this save slot** has
  them. Steam gives each achievement only once; here every save slot earns its own, also the ones your Steam account
  already has.
  - Earned ones in colour with the date and the run they came in, the others in grey. Each page shows how to earn it,
    your progress in this save (100 adventurers, 100 dark quartz hearts, 3 fragments, 5 inscriptions modified), a
    **tip** for the hard ones read from the game's code (what counts as getting hit in a no-hit clear, holding Little
    Bone for The Skeleton King, freeing no caged NPC for Cold-Blooded, each Dark Mirror level on its own, ...), links
    to the boss or the skull, your Steam account's state and the share of all players.
  - Slots from before 0.14 get what their save proves: bosses beaten (by the stages reached), the endings, Dark
    Mirror levels, Alexander and the Dark Skeleton, legendary skulls taken, the counters. Slot 1 (the game you had
    before save slots) can **take over your Steam account's achievements** once.
  - Achievements that give the story away are **veiled** until you earn them or meet what they are about; hold the
    mouse button on one for 4 seconds to look anyway. New setting **Veil secret achievements** (on).
  - Two achievements the game counts but Steam doesn't have: Alexander and the Dark Skeleton without getting hit.
  - A run's page lists the achievements earned in it.
- **Achievement notice:** when your slot earns an achievement, a small box with its icon and name slides down from
  the top of the screen with a short chime and goes back up after a few seconds. New settings **Achievement notice**
  (on) and **Achievement sound** (50 %; the game's volume applies too).
- **Clear view** (players' request): with a strong build your own effects cover everything. Now the effects of your
  skills, items and summons (and your projectiles) get fainter over the run (full in chapter 1, the lowest opacity
  from chapter 5 on) and whenever many of them are on screen at once. Enemy attacks and their warnings are never
  faded. New settings section **Clear view**: Fade your effects (on), Lowest opacity (40 %), Fade over the run (on),
  Fade when busy (on), Your projectiles too (on).
- **Mods** (pause menu → **Mods**), a mod manager in the game, styled after tModLoader and Minecraft's pack
  screen: Thunderstore's Skul mods on the left (search, categories, sorting by update, downloads, likes or name; each
  with its icon, author, downloads, likes and age; a click shows its details), your mods on the right (Enabled /
  Disabled / Restart needed). **Install** (with the mods it needs), **Update**, **Turn on / off**, **Remove** (into a
  backup folder), turn all on or off, open the mods folder, and **Restart game** (the game closes and Steam starts it
  again; on Windows, Linux / Steam Deck and Mac). Mods go where r2modman and Gale put them (BepInEx/plugins/Owner-Name).
  It goes online (Thunderstore) only when you open it. In an r2modman profile every change goes into r2modman's mod
  list too, so r2modman shows what is really installed. New setting **Mod manager** (on).
- **Updates itself:** at every start SkulInsight looks on Thunderstore for a newer version of itself and installs it
  in the background; it runs from the next start (or right away with Mods → Restart game), and a short message says
  so. Only from its own Thunderstore package, and the downloaded file is checked first; the old version is kept in
  `BepInEx/DamageInsight/Mods/Backup`. Also in r2modman and Gale profiles: r2modman's mod list gets the new version,
  and **Restart game** starts the game with the same profile again. New setting **Update automatically** (on). The
  Mods window's Update on SkulInsight's own row does the same.
- **Healing orbs** (beginners often miss the purple orbs enemies drop): they can look like the game's **Portable
  Potion** (a bit bigger, with a green glow) or a big **green plus** (setting **Healing orb look**: the game's orb /
  Portable Potion / green plus). When a map is cleared while you are hurt and orbs are left, a short text above you
  says so (setting **Healing orb reminder**). The run history counts **Heals missed**: Small Healing Orbs left on a
  map (or on the map you died on) while you weren't at full health, and the health they would have restored, on each
  run's page and added up on the Records page.
- **Daily runs:** SkulInsight's own achievements for thumbs up received (5, 10, 25, 50, 100) and days in a row (3,
  7, 14, 30; your longest streak counts, also one from before 0.14). The day's record bonuses for most damage, most
  gold and most kills now only go to runs that defeated at least one boss (a quick death alone on the board got
  them before).
- **macOS:** the mod runs on Skul's native Mac version (Intel and Apple Silicon), set up by one Terminal script
  ([guide](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md)), which can also add other mods from Thunderstore. On a Mac the Korean, Japanese and Chinese
  texts use Apple's fonts for missing characters, and *Crash reports folder* → Open works.
- **Run history:** the damage taken list shows readable names (bosses' parts under their boss, no more code names
  like DarkSkeleton_Phase1), also in older runs. The combat log no longer gives every unnamed object (traps, kegs)
  the name of the first one it met.
- All new texts in every game language.

## 0.13.1 (package fix)

- The 0.13.0 package on Thunderstore was built wrongly (its DLL wasn't inside the `plugins` folder) and was taken
  down. Same mod as 0.13.0, packed correctly; the package description now mentions the optional online daily run.
- README: pictures of the run history, the records and the daily run.

## 0.13.0 (run history, records, daily run)

- **Run history** (Codex → You → **Runs**): every run is kept, like a match history. A row shows how the run ended
  (the boss or the killer in a frame), the kind of run (Normal, Dark Mirror level, Rookie mode), your skulls and
  items, kills, damage, gold, time and date. A run's page has **Overview** (how it went, the bosses defeated, the
  build with every inscription, the numbers and your stats at the end), **Damage** (each piece of gear's share) and
  **Timeline** (skulls, items and inscriptions as they came, map by map). Every name links to its Codex page.
  **End screen** shows the game's own result panel again with the run's numbers. **Star** a run to keep it whole
  forever; older runs without a star keep only their summary. New settings **Run history** (on) and **Runs kept
  whole** (100).
- **Records** (Codex → You → **Records**): your bests over all runs: totals, best runs, per skull, against bosses,
  deaths and the game's own counters. A record links to its run.
- **Daily run** (Codex → **Daily**; online, off until you turn it on): every day one run for everyone with the same
  seed and the same difficulty (a weekday schedule from Normal to Dark Mirror 10), and one try.
  - You play it in a separate daily save slot made from your game: every gear unlocked, the Witch at her top, every
    Dark Mirror level open, 15,000 dark quartz, no DLC gear, so everyone starts equal. Your own game waits as it is;
    **Back to my game** returns to it.
  - After the run you enter a name (12 characters) for the **daily and monthly boards**. Points for the result,
    stages, bosses, kills, damage, little damage taken, gold left and a fast win, times the day's difficulty, plus
    bonuses for the **day's records** (first win, fastest win, most damage, least damage taken, most gold, most
    kills) and a **daily streak**.
  - **Click a run on the board** to see it like your own run history (build, damage, timeline), how its points came
    about part by part, and give it a **thumbs up**.
  - Fair play: a daily run can't be continued (closing the game or going back to the title screen ends it as it
    was); runs with other mods loaded or gear the game doesn't know stay off the boards; no Steam achievements from
    the daily slot.
  - Sent to the daily run server (skul.umut-aslan.dev): a random player id made by the mod, the name you enter and
    your daily run (its numbers, build, damage and timeline). Nothing else, nothing from your own save slots. New
    setting **Daily runs (online)** (off).
- All new texts in every game language.

## 0.12.2 (crash reports; skill details beside the box)

- **Crash reports:** the mod keeps a report of every game session in `BepInEx/DamageInsight/CrashReports` (the newest
  20). It lists what happened (chapters, maps, your build per map), every error with where it came from, and says
  **whose code it was in**: the game's own, SkulInsight's, a game method SkulInsight hooks into, or another mod. If
  the screen stays black for 20 seconds (a level that never finishes loading), a message says so and the report
  notes the game's state and a verdict. If the game closes unexpectedly, the next start says so and adds Unity's log
  of that session. Please attach the newest report to bug reports (settings page: **Crash reports folder**, Open).
  New setting **Crash reports** (on).
- **Skill details beside the box:** holding the details key on a skull or item on the ground (or in a shop) shows
  the mod's numbers in its box since 0.12, and the game's skill / inscription boxes opened right on top of them. They
  now open next to the box (left and right, where the screen has room).
- 11 boss move names met since 0.12 are translated.

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
