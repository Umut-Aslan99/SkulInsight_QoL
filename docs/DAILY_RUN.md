# Daily run with a hosted leaderboard (idea, Umut 2026-10-16)

Status: **being built for 0.13.0** (Umut 2026-10-17: the daily run joins 0.13; before: planned as 0.14). Written by
Claude from Umut's message during the 0.13 batch 1 work. Its own spec and batches when 0.13 ships.

## Umut's idea
- A small server script on Umut's netcup server (umut-aslan.dev) makes a **seed every day**. SkulInsight asks for it
  and keeps it for the day, so everyone can play **the same run**.
- **Daily and monthly leaderboards** on the server. Whoever **wins** the run gets an arcade-style "enter your name";
  **points** come from e.g. damage dealt, the run's time, damage taken, gold left, and whatever else is interesting.
- The leaderboard shows **in the castle before a run** (optional), with a button that **locks the seed and starts
  the run**.
- **One try** per person and day (win or die).
- The **difficulty** (normal or Dark Mirror 0-10) is set by the server for the day. A player who hasn't unlocked that
  level can't play it, "but maybe we can think of a system for that".

## Facts from the game's code (2026-10-16)
- The run seed is `GameData.Save.randomSeed`. The game draws a new one when you **enter the castle**
  (`GameData.Save.ResetAll`) and saves it, so a continued run keeps it. The mod can set it before the run leaves the
  castle.
- **48 game systems** derive their randomness from it (each one adds its own constant and the chapter, stage and
  path): the stage paths (`StageInfo` "Generate Path"), enemy waves, chests, gear rewards, boss chests, adventurer
  parties and rewards, dark elites, the Black Market (Collector, Chef, Headless, Quintessence Master, Tomb Raider),
  road NPCs, altars, special rooms, the castle's Fox and Ogre gifts, Darktech machines, the Dark Mirror upgrade shop.
  So with the same seed, two players get the **same paths and the same reward slots**.
- **But the gear pool comes from each player's own save** (`GearManager`: only gear that is `unlocked`, and DLC gear
  only when `dlcPurchased`). So two players with the same seed get the same items only if they have the same
  unlocks and DLC. The Witch's masteries (stats) and the Dark Mirror levels also come from the save.
- Combat randomness (crits, enemy AI choices) uses Unity's random, not the seed: fights differ between players, which
  is fine for a leaderboard.
- The mod already has what a score needs: the **run journal** (0.13 batch 1) keeps the result, time, damage dealt and
  taken, kills, gold, the build and a timeline of every run.

## Design proposal (Claude, for discussion)
**Server** (Docker + Traefik on the netcup server, like the Albion Analyzer; Claude writes it, Umut uploads it with
WinSCP and gives the server session a prompt):
- Every day at 00:00 UTC: a new seed and the day's difficulty (rotating, e.g. normal on some days, Dark Mirror levels
  on others).
- `GET /daily` → date, seed, difficulty, seconds left. `GET /board?day=...` and `?month=...` → the boards.
  `POST /score` → the score with the run's summary (from the journal).
- SQLite storage; one entry per player and day; a name filter; rate limits; an admin token for Umut to remove
  entries.

**Mod:**
- In the castle: a board (today's top scores, your place, time left; daily / monthly tab) and **"Start today's
  run"**, which sets the seed and the difficulty and marks today's try as used. Shown only when the setting is on.
- At the end: points from the journal's numbers; on a win, an arcade name entry; the score goes to the server.
- **Fairness and locked difficulties in one go**: play the daily in its **own "Daily" save slot** built from a
  standard profile (every gear unlocked, the Witch at a fixed level, every Dark Mirror level open). Everyone gets the
  same gear pool and stats, nobody needs the level unlocked, and the player's own game stays untouched (the save slot
  switch from 0.12 does the swapping). DLC gear: left out of the daily pool so owners and non-owners play the same run.
  **Steam achievements stay off during a daily**, so a profile with everything unlocked can't hand them out.
- The network: the mod only connects when the player turns the feature on (a setting, off by default, asked once at
  the board). The README says what is sent: a random player id made by the mod, the name entered, the score and the
  run summary. No Steam id.

**Cheating**: the mod is open source, so a determined player can always send a fake score. Answer: an honour
system with server checks (time against maps, numbers within what's possible, the journal's timeline must fit) and
Umut's admin removal. Good enough for a fan leaderboard.

## Decided (Umut 2026-10-18)
- Other mods: **no other BepInEx plugin at all** on a daily run (multiplayer and balance mods are the point, and there
  are too many to list). `DAILY_ALLOWED_MODS` stays empty; an exception later needs the plugin's GUID, which the mod
  sends with every disqualified run (server admin list: `numbers.mods`) and writes to the log.
- The daily slot starts with 15,000 dark quartz (the Dark Mirror castle's machines).
- The board can be browsed like the run history: a line opens the run its player sent (build, skulls, damage,
  timeline) and a Points tab (part by part, the day's records, the streak, how points are counted).
- Bonus points for the day's records (first win, fastest win, most damage, least damage taken in a win, most gold
  left, most kills) and for a daily streak (250 per day in a row before today, up to 2,500), all x the difficulty.
  Since 2026-10-25 (Umut) most damage, most gold and most kills go only to runs that defeated at least one boss.
- Likes: a thumbs up once per run, not on one's own. Later: own achievements for likes received (5/10/25/50/100) and
  for streaks (data: `GET /api/me`).

## Decided (Umut 2026-10-17)
- **Now, as part of 0.13** (nothing is released until the daily run is done; 0.13.0 = run history + daily run;
  records and achievements follow after it). The Records page is built in parallel by a second agent (Umut's idea).
- **Name**: free, up to 12 characters (the server filters words).
- **Board**: everyone who plays appears; wins rank first (by points), deaths below them (by how far they got, then
  points).
- **Monthly board**: the sum of a player's daily points in the month.

## Decided (Umut 2026-10-16)
- **Version**: 0.14.0, right after 0.13.
- **Fairness**: the daily is played in its **own "Daily" save slot** with a standard profile (everything unlocked,
  the same Witch for all, every Dark Mirror level open, no DLC gear, Steam achievements off).

## Build plan (Claude, 2026-10-17)

### Server (`server/daily/` in this repo; Umut uploads it, his server session runs it)
- Python 3.12 + FastAPI + SQLite in Docker behind Traefik (like the Albion Analyzer: `~/projects/<app>`, Traefik in
  `~/traefik`); subdomain `skul.umut-aslan.dev` (the server session adapts the Traefik labels / DNS).
- **The day's run** needs no timer: seed and difficulty come from HMAC-SHA256(secret, "YYYY-MM-DD") (UTC days), so
  every request for a day gives the same answer and nothing has to run at midnight.
  [DECISION] difficulty by weekday (configurable): Mon normal, Tue DM 3, Wed DM 5, Thu DM 7, Fri DM 9, Sat DM 10,
  Sun normal.
- Endpoints: `GET /api/daily` (date, seed, mode, level, seconds left), `POST /api/runs/start` (registers the day's
  one try per player id), `POST /api/runs/finish` (result + the run's numbers; **the server computes the points**,
  so the formula lives in one place and can be tuned without a mod update), `GET /api/board?date=` (day),
  `GET /api/board/month?month=`, `GET /healthz`, admin removal with a token. Rate limits per IP, a word filter for
  names, plausibility checks (time vs maps, numbers within bounds).
- Ranking: day = wins first (by points), then the rest (by how far they got, then points); month = sum of points.
- Points (draft, tuned with real runs): win 10,000 + 500 per stage reached + 1,000 per boss + 5 per kill + damage
  dealt / 2,000 - 2 per damage taken + gold left / 10 + on a win 3 per second under one hour; times a difficulty
  factor (normal 1.0, Dark Mirror n: 1 + 0.15 n) so harder days count more in the month.
- Tests with pytest (run locally with Python, no Docker on Umut's PC); handoff: the folder for WinSCP + a prompt for
  the server session.

### Mod
- **Settings**: Daily/Enabled (online; off until the player turns it on at the board, which says what is sent),
  Daily/CastlePanel (the castle's small panel, on), the server address (hidden).
- **Codex section "Daily"**: left page the board (Today / This month), right page today's run (difficulty, time left,
  your status), the button "Play today's run", the name for the board.
- **The daily slot**: a hidden save slot ("D") made from a copy of the player's own save, evened out: every gear
  unlocked (`gear/unlocked/<type>/<name>`), the Witch's masteries at their top, every Dark Mirror level open
  (`HardmodeProgress/clearedLevel` 10), the castle NPCs rescued, currencies at 0, no run in progress. In it: DLC gear
  left out of the pools (owners and non-owners play the same run), Steam achievements not sent. The save slot
  switch (0.12) does the swapping; after the daily the player goes back to their slot.
- **Starting**: the button switches to the daily slot (via the title screen, like a slot switch), puts the player in
  the day's castle (normal or Dark Mirror, `HardmodeProgress.hardmodeLevel` = the day's level) and sets the run seed
  (`GameData.Save.randomSeed`, after the castle has drawn its own); leaving the castle starts the run. The try counts
  from the first map (server `start`); quitting and continuing the same run is fine; one try per player and day.
- **The end**: after the end screen, back in the castle: the name (12 characters, the last one kept) and the run is
  sent; the board shows the place. The run history marks daily runs.
- **Castle panel** (optional): the day's difficulty, time left, your status and the top 3, "K: Daily".

### Batches
| # | Batch | Umut tests |
|---|---|---|
| D1 | Server: API, seed/difficulty, points, boards, checks, tests, Docker + Traefik files, handoff prompt | uploads it, his server session starts it; `https://skul.umut-aslan.dev/api/daily` answers |
| D2 | Mod: client, player id + local state, settings, Codex "Daily" section (boards, today's run) | the boards and today's run in the book |
| D3 | Mod: the daily slot (copy + evening out), start (mode, level, seed), achievements off, DLC gear off | play a daily run |
| D4 | Mod: points + name entry + sending, place on the board, castle panel, run history mark | finish (or die in) a daily run, see the board |
| D5 | 0.13.0 release: texts, README, changelog, wiki, What's new (with Records and run history) | final test, go |

## Open decisions (Umut, at the 0.14 planning)
3. Points: which numbers count and how much (Claude proposes a formula with examples; tuned with real runs).
4. The name: 3 letters like an arcade machine, or a free name (12 characters)?
5. The monthly board: the sum of a month's daily scores, or the best day?
6. Only wins on the board (as said), or deaths too with fewer points (more people on the board)?

## Six boards a day (0.15, Umut 2026-10-26)
- [Umut] "Normal (own category with dlc and without), DM random level (today random seed with random selected DM
  0-10, also DLC and non DLC version), DM 10 (also DLC and non DLC)". "Any daily counts for sure" (streaks, likes).
- [Umut] "I dont want to give DLC content for free to players, but players with DLC should be able to play dailies
  with them": the `+dlc` boards are only playable when the game says the Mythology Pack is owned (anyone can watch
  them); on the boards without DLC owners get no DLC gear and no DLC map events either (the same run for everyone).
- Server: boards `normal`, `dm`, `dm10`, `normal+dlc`, `dm+dlc`, `dm10+dlc` (`days.BOARDS`); from
  `days.CUTOVER` (2026-10-26) each has its own seed (HMAC of date and board; `normal` keeps the old seed) and the
  `dm` level is HMAC "dm-level|date" mod 11. `/api/dailies` lists them; `/api/daily` (0.14) is the `normal`
  board. Every call takes `board` (default `normal`). The first start with boards keeps the old tables as
  `runs_v1` / `likes_v1` and copies the rows (days before the cutover: `normal` or `dm` by the old schedule).
- Mod: a try per day and board (key `date|board`, `normal` = the date as before), board chips + a DLC chip on the
  Daily page, the day's Darktech up to its level, the same castle gifts and blessing from the seed (V015_LOG 10-11).
- Shoutout: [Umut] Tythos (Bilibili creator, https://space.bilibili.com/17576167) runs daily run tournaments; one
  dim line with the link at the bottom of the Daily page.

## Points formula 3, the day's achievements, winners as adventurers (0.16, 2026-10-28/30)
- [Tythos] boss and kill counts differ for the same content (the game counts a kill once per damage source that
  lands the last hit at the same time); Victory, Stage, Boss, Kills are the same for everyone who finishes. [Umut]
  drop kills and stages, keep bosses (the optional Dark Mirror two give extra), run time stronger, flawless rooms /
  boss fights (define flawless vs. a shield), bones left, a streak up to 30 days (Tythos: not in their tournaments).
- Formula 3 from 2026-11-01 (server `points.py`, `formula_of`): see CHANGELOG 0.16.0. Flawless = no health lost (the
  game's own no-hit rule: a hit counts when it costs 1+ health); a win counts all five main boss fights; the day's
  records no longer add points; the streak (100 a day, up to 30) counts on the month board only (`monthBonus`).
- The day's achievements (Umut 2026-10-30: a daily random subset, all achievable in one run, only the logical ones;
  custom ones like "play as <random skull>"): 2 of the game's (`ACHIEVEMENT_POOL`: no-hit bosses, Skeleton King, a
  minute in the black market, 4 dark abilities in the Dark Mirror; no dying, castle, gear or shop luck) + "skull of
  the day" (win holding a common/rare skull family; Mythology Pack skulls only on DLC boards) + "flawless chapter"
  (1-3). 300-1,500 each, about a tenth of a win in all. The mod records the game's achievements given during a daily
  run (the daily slot's achievement hook) and sends them; a no-hit one also counts from the run's boss rooms.
- [Umut] yesterday's winners as adventurer names: `/api/dailies` has `champions` (each board's first place, a win);
  the mod puts them on the adventurers' name tags (AdventurerNamePool.OnEnable).
- Ideas for later (Umut, Discord 2026-10-30): challenge-themed days (only physical items / skulls, only items with
  certain inscriptions, "how far in 20 minutes"), "pass on runs" (each player plays one chapter), randomizers, a
  tournament-only build for Tythos's live tournaments, a monthly video submission contest (Tythos).
