# 0.14 step log: achievements (page, notices, the mod's daily achievements) and the 0.13 leftovers

Running log, updated after every step, so an auto-compaction or a new session loses nothing.
Decisions Claude made alone are marked **[DECISION]** (Umut may change any of them); Umut's are marked **[Umut]**.
Plan: docs/V013_SPEC.md part C (achievements) and its batches 4-6.

## Umut's request (2026-10-25)
- "Lets go and work on 0.14. You can apply your recommendation for the daily runs. We want achievements page now,
  and maybe a little notification system with a small box that is not too intrusive but comes down from top of the
  screen, displays an achievement the player got and fades away or leaves from the top of the screen again."
- [Umut] Daily record bonuses (most damage, most gold, most kills, least damage taken) only for runs that beat at
  least the first boss; first win and fastest win stay as they are.

## Queue
1. Daily record-bonus rule (server + the mod's Points tab), server tests, subtree push.
2. Achievements data: hook on PlatformManager.SetAchievement, per-slot store (Codex/achievements.json), Steam's
   texts / icons / hidden flags / global percentages, the 11-language table, existing slots (save proofs + "take
   over Steam's"), progress counters.
3. Achievement notice: a small box slides down from the top, shows icon + name, slides back up (setting).
4. Achievements page (Codex → You → Achievements): grid, greyed out until earned, hidden veil (hold 4 s), progress,
   tips.
5. The mod's own daily achievements (thumbs up 5/10/25/50/100, streaks) from /api/me.
6. Leftover: code names in damage-taken lists (DarkSkeleton_Phase1, ...).
7. Release prep: version, changelog (Mac lines), README top box, What's new, wiki. Release itself: Umut's go +
   reviewer ping.

## Steps (newest last)
1. **Daily record bonuses need a boss** (94d6f79, server pushed as skul-daily b4f53b6, deploys itself at :07/:37
   UTC). [Umut] most damage / most gold / most kills go only to runs that defeated at least one boss
   (`bossKills >= 1`: the game counts chapter bosses, not the First Hero's or Alexander's single phases/parts).
   [DECISION] least damage taken stays a win-only record (it already was; stricter than "one boss"). The rule
   applies when a board is read, so today's board follows it at once; FORMULA stays 2 (the run's own points didn't
   change). The Points tab gets one more line ("Most damage, most gold and most kills only count for runs that
   defeated at least one boss.", 11 languages; ships with 0.14). Server tests 23 (+1), mod tests 468.
2. **Achievements: data, notice, page** (fcb186b, deployed as Debug build). Facts found:
   - The game's list (Platforms.Achievement.Type) has 103; **Steam has 101**: TruePeace_2 / TrueSalvation_2 (no-hit
     Alexander and Dark Skeleton) are set by the game but Steam doesn't know them (keys 794/798 missing). **Steam
     marks all 101 as hidden.** Steam's list order differs from the game's in one spot (StoneMonkey, GraveDigger,
     Prisoner): we sort by the game's enum.
   - Texts in 11 languages from Steam's community page (`steamcommunity.com/stats/1147560/achievements`, data as JSON
     in "queryData"; the language comes from the `Steam_Language` cookie, `?l=` is ignored), built by
     `tools/achievements/steam_table.py fetch|build` into `DamageInsight/Achievements/achievements.json` (embedded,
     88 KB, with the global percentages as a fallback). Icons are not shipped: loaded from Steam at run time
     (`SteamUserStats.GetAchievementIcon` + `SteamUtils.GetImageRGBA`, flipped) and cached in
     `BepInEx/DamageInsight/AchievementIcons/<id>.png`; grey copies made by the mod (100 of 101 icons are the same
     earned / not earned on Steam). Steam's state (`GetAchievementAndUnlockTime`) and live percentages
     (`RequestGlobalAchievementPercentages`) through Platforms.Steam.dll (new reference).
   - "Getting hit" for the no-hit ones (BossClearTracker / HeroClearTracker / DarkHearoClearTracker): from the boss
     map's Start on, a hit with an attack type and at least 1 damage dealt; ColdBlood = no caged field NPC released
     in the run; SkeletonKing = Little Bone held when the First Hero dies; each Dark Mirror level's achievement only at
     that level. Counters: totalAdventurerKills (permanent), heartQuartz.totalIncome, fragmentGainCount,
     CountOfGainedSuperInscription.
   - Built: `Achievements/AchievementCatalog` (kinds, tips, targets, veils, boss pages, the game's skull rule),
     `AchievementStore` (Codex/achievements.json: earned with date / how / run, revealed, proofsChecked, steamTaken;
     moves with the slot, ForgetSlot on a switch), `AchievementSteam`, `AchievementTracker` (hook, proofs, progress,
     take-over), `AchievementPages` (texts), `Patches/AchievementPatches` (prefix on PlatformManager.SetAchievement,
     priority first), `UI/AchievementToast`, `UI/HoldToReveal`, `Codex/CodexWindow.Achievements` (You → Achievements),
     run pages list "Achievements: ..." of their run, pixel icon "trophy".
   - [DECISION] Veils only on 13 story spoilers (First Hero, chapter 6 / Alexander, the Dark Skeleton and its clues),
     not on all 101 (Steam hides all). A veil also lifts by itself once the boss it names is met in the Codex.
     Setting Codex/VeilSecretAchievements (on).
   - [DECISION] Save proofs (once per slot, silent, "proven by the save"): deaths, 100 adventurers, 100 dark quartz
     hearts, the ending, Dark Mirror levels 0..clearedLevel + Dark Side of Harmonia, Alexander cleared (also chapter
     6), the Dark Skeleton, fragments 3, the Witch 5, a hidden boss clue, stage best times (a chapter reached proves
     the boss before it), Codex kills of the bosses' last phases, legendary skulls taken, run history (rookie run,
     Dark Mirror death, normal / emperor endings). Not provable (earned again): no-hit clears, castle visits, the
     black market ones, the castle restoration, DarkAbility.
   - [DECISION] "Take over Steam's achievements" only in slot 1 (the game from before save slots), once, with
     Steam's unlock times. Nothing is recorded in the daily slot (it borrows its slot's Codex; everything unlocked).
   - [DECISION] The notice: 440-760 × 92 px box, top centre, slides in 0.4 s (ease out), stays 4.5 s, slides up and
     fades in 0.45 s; queued; no sound (not intrusive). Shown only when the slot earns it for the first time. Setting
     Codex/AchievementNotice (on). DEV builds: F7 on the Achievements tab shows the selected one's notice.
   - Translations: 50 new texts in 10 languages with Steam's own terms (Errungenschaft, logro, succès, conquista,
     osiągnięcie, достижение, 도전 과제, 実績, 成就; First Hero / Little Bone / Dark Quartz Hearts as Steam writes
     them). Game-font gaps fixed (账/帳/檔, 됨/셉/팁 reworded); 14 characters of Steam's CJK texts are not in the
     game font and come from the system font fallback.
   - Tests: 486 (+18: catalog = the game's enum, 11 languages, kinds, the skull rule with real names, store round
     trip and .bak, page texts, veils, run link).
3. **SkulInsight's own daily achievements** (83f2d6c; server skul-daily 5f71bd8, `/api/me` now has `bestStreak`,
   24 server tests). 9 achievements: thumbs up received 5 / 10 / 25 / 50 / 100 ("First Fans", "Crowd Pleaser",
   "Fan Favourite", "Star of the Board", "Legend of the Board") and days in a row 3 / 7 / 14 / 30 ("Coming Back",
   "A Week of Bones", "Two Weeks Strong", "Daily Devotion"). [DECISION] They belong to the daily player (random id),
   not to a save slot: kept in Daily/state.json (`me` counters + `achievements`), shown on every slot's Achievements
   tab as their own group "Daily runs · SkulInsight" under the game's (the grid is placed by hand now, with a header
   per group). Earned when `/api/me` (every 10 min, and with the boards) reaches a threshold: the notice shows; the
   best streak never goes down locally (a removed run doesn't take it back). Pixel icons (thumb, hourglass), grey
   tint when not earned. Page: progress, a tip, "turn on Daily runs" when off. 18 texts × 10 languages. 488 tests.
4. **Code names in damage-taken lists** (leftover from 0.13). Cause: bosses whose parts have no Codex entry or no name
   text of their own (the Dark Skeleton's phases, Alexander's body/renewal, the Leiana sisters' objects) and
   keyless objects (PowderKeg, ThornTrap) were kept by the game's object name. Also found: `DamageLog.NameOf` cached
   names by the game's key, so every keyless character ("Unspecified") got the name of the first one seen; it now
   caches them by object name. New `RunPages.AttackerName`: a boss part → its boss's Codex name, a code name →
   words ("Powder Keg"). Used when a run records a hit (new runs) and when a run page shows its damage taken (old
   runs; parts of one boss are added up under its name). 489 tests.
5. **0.14.0 prepared** (not released): version 0.14.0, CHANGELOG 0.14.0 (achievements, the notice, daily
   achievements + the record bonus rule, macOS, readable damage taken names), What's new (4 new highlights in 11
   languages; the 0.13 ones pruned), README (top box "Version 0.14", intro, Achievements section, settings table,
   known limitations, roadmap), wiki (intro, Achievements section, Coming next), Thunderstore description in
   package.ps1 (241 of 250 characters). Release build checked. Review fixes: the picture frame's hold reveals only
   on the Achievements tab; an icon Steam never hands over is given up after ~20 s. Debug build 0.14.0 deployed.
   Still open for the release: pictures of the Achievements page for README/wiki (from Umut's test), Umut's test,
   his go + the reviewer ping, the wiki paste.

## Test todos for Umut (0.14, the Debug build is installed)
1. Start Skul, go to the castle: the **What's new** window shows the four 0.14 lines.
2. Codex (K) → You → **Achievements**: the grid (earned in colour with a crown badge, the rest grey), the header
   "x / 103 in this slot · Steam y / 101", the groups "Skul: The Hero Slayer" and "Daily runs · SkulInsight".
   Icons may take a few seconds the first time (Steam hands them over). Click a few: the right page (how to earn
   it, progress, tip, this slot, Steam). Filters All / Earned / Not earned.
3. **Before** taking over Steam's: earn one the save can't prove to see the **notice**: stay in the black market
   (the merchants' map between chapters, in a run) for 1 minute → "What should I buy?" slides down from the top. (Debug build only: F7 on the
   Achievements tab shows the selected one's notice without earning it.)
4. A veiled tile (e.g. "Hidden achievement" near the First Hero ones if you haven't met him in this slot, or the
   Dark Skeleton ones): hold the mouse button on it 4 s (a gold bar fills) → it opens. Also on the big picture.
5. Slot 1: the button **Take over Steam's achievements** (bottom right) → the count jumps to Steam's.
6. Another slot (settings page → Save slots): its own achievements (proven by its save), no take-over button.
7. A run's page (You → Runs → a new run): "Achievements: ..." under the bosses when one came in that run; the
   damage taken list without code names (also the 2026-10-16 run: Dark Skeleton, Alexander, Leiana).
8. Daily (if on): the daily group shows your thumbs up / streak progress; Points tab: the new line about bosses.
9. Settings page → Codex: "Achievement notice" and "Veil secret achievements" rows with help texts.
10. Screenshots of the Achievements page (whole book) for the README; I'll crop them.

## Round 2 (Umut 2026-10-25 evening: sound, veils, Clear view, mod manager)
6. **No veiled achievements found** (Umut): not a bug. Slot 1 has all 101 Steam achievements now (66 taken over, 34
   proven by the save, 1 earned: DarkAbility on the first castle visit). Only TruePeace_2 / TrueSalvation_2 are left,
   and their veils lifted because Alexander and the Dark Skeleton were met in this slot's Codex. A fresh slot shows
   the veils.
7. **Achievement chime** (b174b84). [Umut] "a cute sound effect, not too intrusive but rewarding, like a little mario
   coin pickup or xbox achievement". Made in code (UI/AchievementSound.cs, no sound file): B5 70 ms, then E6 ringing
   (decay 0.12 s) with a faint B6 sparkle, a soft square tone (odd harmonics 1, 3, 5), 0.48 s, peak 0.42; played by
   the game's SoundManager (master + effects volume apply). Setting Codex/AchievementSound (50 %, 0 = off). A WAV of
   the same math for listening outside the game: scratchpad achievement-chime.wav (script scratchpad/scripts/chime.py).
8. **Clear view** (b174b84). [Umut] players ask for it: strong builds clutter the screen and runs die to damage you
   can't see; "the transparency of effects shall lower over the duration of the chapters or something like that
   (configurable)". Built on FilmClutter (which already tells the player's and summons' effects apart for clean
   films): UI/ClearView.cs lowers the alpha of those EffectPoolInstances (state Playing) and of the player's / summons'
   projectiles (Patches/ClearViewPatches: Projectile.Fire / Despawn) every LateUpdate; the game's own alpha changes
   are followed (base vs. what we wrote), effects fading out on their own are let go, projectiles get their alpha
   back when they despawn (the pool doesn't reset it). Enemy effects are never touched. Settings section Clear View:
   Enabled (on) [DECISION on by default: players asked; chapter 1 is unchanged], LowestOpacity (40 %), OverTheRun
   (chapter 1: 100 %, 2: 85 %, 3: 70 %, 4: 55 %, 5+: 40 %; Dark Mirror the same), WhenBusy (from 8 of your effects /
   projectiles on screen, lowest at 40), Projectiles (on); both ways off = always the lowest. Eased (1.2 per second).
   Not covered: particle systems (shared, no owner) and status looks on enemies. 499 tests.
9. **Mod manager**: plan in **docs/MOD_MANAGER.md**, not built. Thunderstore's docs have no rule on a mod that
   downloads other packages; every update of ours is reviewed by hand, so Umut asks the reviewer first (message in
   the doc). Facts: the Skul list API (56 packages, 197 KB), the install rules from install-mac.sh --add, no reload
   without a restart (BepInEx 5 / Mono), restart through steam://rungameid/1147560, r2modman / Gale profiles.
   [DECISION] it would be 0.15 (Dark Mirror page → 0.16, Rewards → 0.17).

## Test todos for Umut (round 2, the Debug build is installed)
11. The notice now plays a chime: F7 on the Achievements tab (Debug build), or earn one. Too loud / quiet / not
    "cute" enough? Settings → Codex → Achievement sound. (Or listen first: the WAV in my scratchpad folder.)
12. Veils: switch to a slot where you haven't met the First Hero (settings page → Save slots, a fresh slot) and open
    Achievements: the First Hero / Alexander / Dark Skeleton ones are dark tiles; hold one 4 s.
13. Clear view: play a run with a strong build. In chapter 1 nothing changes unless many of your effects are on
    screen; from chapter 2 on your effects get fainter step by step; enemy attacks stay as they are. For a quick look
    in chapter 1: turn off "Fade over the run" and "Fade when busy" (then always the lowest, 40 %). Is 40 % right? Do
    you miss any of your effects (something you need to see), or is anything of yours still too loud?
14. Post the reviewer question from docs/MOD_MANAGER.md in your Thunderstore thread and tell me the answer.

## Round 3 (Umut 2026-10-25 night: build the mod manager)
- [Umut] "I read the question and would say, yes it would be allowed. GaleModManager is a proof for that"; the UI
  "a little bit like tModLoader for Terraria, or maybe Minecraft resource pack selection where we have a left and
  right side, of downloadable / active mods and filters ... Be creative, make it look good". Claude's note: Gale is a
  desktop app listed on Thunderstore, not an in-game installer; Umut mentions the mod browser to the reviewer at the
  0.14 upload ping. [DECISION] it ships in 0.14 (one upload) unless Umut wants it later.
- Facts: Skul's Managed folder has no System.IO.Compression.dll (ZipArchive) but System.dll's DeflateStream, whose
  native helper ships with the game (Windows MonoBleedingEdge/EmbedRuntime/MonoPosixHelper.dll, Mac
  Contents/Frameworks/libMonoPosixHelper.dylib) → a small zip reader of our own. The title screen only waits for
  "press any key" (Scenes.Main), so the Mods button goes into the pause menu.
10. **Mod manager built** (58f7943 + review fixes; deployed, 510 tests). Code: Mods/ZipReader (central directory +
    DeflateStream, stored/deflated, backslash paths), Mods/ThunderstoreCatalog (API v1 list, search, categories,
    sorting, pinned first, deprecated / NSFW / Gale hidden, dependencies depth first without the loader, version
    compare), Mods/ModFolders (r2modman's layout: plugins/Owner-Name incl. manifest/icon/README, patchers/Owner-Name,
    config only when missing; core and unsafe paths left out; disable = *.dll → *.dll.old; remove = folder into
    BepInEx/DamageInsight/Mods/Backup; a loaded DLL that Windows won't move is renamed and the folder finished at the
    next start (CleanUp); profiles of r2modman / Gale / Thunderstore Mod Manager recognised), Mods/ModService (the
    list cached in DamageInsight/Mods/packages.json, icons in Mods/Icons, installs with dependencies, what was on at
    the start → "restart needed", the restart: a script waits for the game's process, then opens
    steam://rungameid/1147560: restart.cmd on Windows and under Proton, restart.sh with `open` on a Mac; refused in the
    daily slot), UI/ModManagerWindow (Thunderstore's dark navy look: header with the restart pill, left
    "Thunderstore" with search, sort chip, category chips and rows with icon, version, author, categories,
    description, downloads / likes / age and Install / Update / Installed / Mod loader / This mod; right "Your mods"
    with All / Enabled / Disabled and rows with a badge (Enabled, Disabled, On / Off / Removed: restart needed, This
    mod) and Update / Turn on or off / Remove; bottom: Turn all on / off, status line, Open mods folder, Restart game,
    Close; a card for details, installs with dependencies, removing, restarting). Pause menu button "Mods" under
    "SkulInsight QoL" (FitGrid now knows how many buttons we added). Setting Mods/Enabled (on). 80 texts × 10.
    [DECISION] Not on the title screen (it only waits for "press any key"). [DECISION] SkulInsight itself can't be
    turned off, removed or updated from inside (its DLL is loaded): "Update on Thunderstore" opens its page.
    [DECISION] One package without dependencies installs on one click; several, unknown dependencies or a mod manager
    profile ask first. [DECISION] in an r2modman / Gale profile installing still works after the warning.

## Test todos for Umut (round 3: the mod manager; the Debug build is installed)
15. Esc → **Mods** (under SkulInsight QoL): the window. Left: Thunderstore's list loads (icons come in over a few
    seconds), search ("legendary"), the categories, the sort chip; click a row → its card. Right: your mods (LuckBasedRNG
    or whatever you have, SkulInsight as "This mod").
16. Install a small mod (e.g. MrBacanudo's LegendaryStart), see "On: restart needed" and the orange pill, then
    **Restart game**: the game should close and come back by itself through Steam, with the mod loaded (I read the log).
17. Turn it off → restart → it's "Disabled"; Remove → it's gone after the restart (BepInEx/DamageInsight/Mods/Backup has it).
18. How does it look? Anything you'd change (colours, sizes, what the rows show)? Screenshots help.
11. **SkulInsight updates itself** (Umut 2026-10-25: "an auto update function every time it starts with BepInEx").
    Mods/SelfUpdate (plain: Thunderstore's package API answer, the DLL in the package, the checked swap) +
    ModService.UpdateSelf: 15 s after every start (so the start isn't slower) it asks
    `thunderstore.io/api/experimental/package/DocRun/SkulInsight_QoL/`; a newer version (than MyPluginInfo's) is
    downloaded from Thunderstore's own download_url only, its plugins/DamageInsight.dll written next to ours as
    .download and checked (AssemblyName: name DamageInsight, version = Thunderstore's), the old DLL copied to
    Mods/Backup/DamageInsight-<old>.dll (3 kept), renamed to DamageInsight.dll.replaced (Windows allows renaming a
    loaded DLL, not overwriting it; CleanUp deletes it at the next start), the new one moved into its place (if that
    fails the old one goes back). The new version runs from the next start; a notice says so; the Mods window shows
    "Updated to X: restart needed" and its Restart game applies it now. Setting General/AutoUpdate (on, Umut asked for
    automatic). [DECISION] never in developer (Debug) builds, so Umut's test builds aren't replaced; never in r2modman /
    Gale / Thunderstore Mod Manager profiles (they update mods themselves and would lose track). The Mods window's
    SkulInsight row: "Update" runs the same. 514 tests (+4: API answer, trusting only thunderstore.io, the DLL in the
    package, the swap with backup, refusing a wrong version / another assembly / a too small file).
    Reviewer note for the 0.14 upload: the mod downloads only its own Thunderstore package to update itself.
    Test after the release: a Release build labelled lower than Thunderstore's (e.g. 0.13.99) updates itself.
12. **Updates and the Mods window in r2modman profiles** ([Umut] "the mod is firstly installed through thunderstore /
    r2modman anyway ... auto update or the update button should work in r2modman profiles"). Checked on Umut's real
    r2modman profile "Release test" (%APPDATA%/r2modmanPlus-local/SkulTheHeroSlayer/profiles): the package sits in
    BepInEx/plugins/DocRun-SkulInsight_QoL with manifest.json, icon.png, README.md, CHANGELOG.md; r2modman's record is
    <profile>/mods.yml (YAML list, one entry per mod: name, versionNumber major/minor/patch, enabled, ...; LF);
    _state/installation_state.yml is empty (no file tracking). Built: Mods/R2modmanProfile (find an entry, change its
    versionNumber or enabled, remove it, add one in r2modman's own layout; atomic write); the self-update now runs in
    profiles too and afterwards writes the package's manifest / readme / changelog / icon and mods.yml's version; the
    Mods window writes installs (new entry or new version), on/off (enabled) and removals into mods.yml; its warning
    stays only where no mods.yml exists (Gale). Also found: **Restart game in an r2modman profile would have come
    back without the profile**: r2modman starts the game with doorstop arguments (`--doorstop-enable true
    --doorstop-target <profile>\BepInEx\core\BepInEx.Preloader.dll`), a plain steam://rungameid doesn't. Now the
    restart passes the game's own start arguments on: `steam.exe -applaunch 1147560 <args>` (SteamExe from
    HKCU\Software\Valve\Steam, like r2modman), under Proton `steam://run/1147560//<args>/` (Steam may ask to allow
    the arguments), without arguments steam://rungameid as before; "%" doubled for cmd, the script written without a
    BOM. 518 tests (+5 mods.yml with the real sample, +launch line).
    [DECISION] If r2modman is open while the game runs it may later write its own in-memory list back over our change:
    then it shows the old version and offers the update again (harmless). Switching profiles once in r2modman reloads
    the list.
    **End-to-end test prepared** (Umut starts it): the profile's DLL replaced by a Release build of today's code
    labelled 0.13.0 (scratchpad release-0.13.0; the original 0.10.0 DLL, manifest and mods.yml backed up in
    scratchpad r2-backup). Starting the profile from r2modman should, after ~15 s, update to Thunderstore's 0.13.1:
    DLL swapped, manifest.json and mods.yml at 0.13.1, the notice; then Esc → Mods → Restart game should come back
    with the same r2modman profile, running 0.13.1.
13. **First r2modman test passed** (Umut started "Release test" with Start modded, 2026-10-25 19:55): the 0.13.0-labelled
    build updated itself: DLL 0.13.1.0 in plugins/DocRun-SkulInsight_QoL (old one as DamageInsight.dll.replaced and
    Mods/Backup/DamageInsight-0.13.0.dll), manifest.json 0.13.1, mods.yml versionNumber 0.13.1, log lines
    "r2modman's mod list updated (SkulInsight 0.13.1)" and "Update: 0.13.0 → 0.13.1 installed". (Umut found the
    Mods window and achievements "weird": that build was today's code, only labelled 0.13.0.)
    From his screenshot of the Mods window: the game's font is NotoSans **Bold** already, so TMP's <b> adds letter
    spacing ("Thunderstore" ran into "48 mods", "Restart needed" didn't fit its button); the author line ended in
    "..." where the categories should be. Fixed: no <b> in the window except the big "Mods" title, the count placed
    after the measured title, categories as a small tag after the author, taller author lines. Lesson for all our UI:
    don't use <b> with the game's font.
    Second test prepared: the profile has a fresh 0.13.0-labelled build again (it updates itself to 0.13.1 again);
    then Esc → Mods → Restart game must come back in the r2modman profile (with 0.13.1, which has no Mods button).
14. **Restart in the r2modman profile works** (Umut 2026-10-25 ~20:00): the 0.13.0-labelled build updated itself to
    0.13.1 again, Mods → Restart game closed the game and Steam started it again with r2modman's doorstop arguments:
    the profile's log shows BepInEx loading SkulInsight QoL 0.13.1 from the profile. The game folder's loader files
    (winhttp.dll, doorstop_config.ini) are unchanged by the r2modman runs. Back to the Steam start (the game folder's
    BepInEx, Debug build of a76a06e deployed 20:02). The "Release test" profile now holds the real 0.13.1 from
    Thunderstore (its .replaced leftover deleted; the original 0.10.0 files are in scratchpad r2-backup).
15. **Healing orbs** ([Umut] "the purple heal balls the game drops are kind of hard to see. Many beginners miss
    them": a Heals missed counter in the match history and records, "how much health could have been restored", a
    green / big + look (switchable), "a text message somewhere unintrusive"; then "rewrite the purple orbs to become
    the existing small potion icon"). Facts: the orbs are the game's Level.Potion (CommonResource small / medium /
    large; enemies' DropLootOnDie, the adventurer party's AdventurerReward, boss chests (always large)); the bottles are
    PurchasablePotion (the adventurers' gear reward). A Potion heals `_healAmount` × TakingHealAmount (the heal is
    capped at max health) and refuses at full health (it stays on the ground). The game's names (string table): Small
    Healing Orb / Healing Orb / Big Healing Orb, Portable Potion / Uncommon Potion / King Potion, in all 11 languages:
    used in the translations. The bottle sprites PortablePotion (9×11) / AdvancedPotion / KingPotion sit in a shared
    level bundle (tools/unity/potion_sprites.py found them) and are looked up among the loaded sprites at run time.
    Built: History/HealOrbs (Patches/HealOrbPatches: LevelManager.DropPotion postfix notes every orb, Potion.InteractWith
    prefix marks it taken by the game's own check; leaving the map or dying on it counts the small orbs left while
    hurt via History/HealOrbMath: only orbs that would have healed, biggest first, health capped at the missing part;
    a win's last map doesn't count), RunNumbers healsMissed / healthMissed (set to 0 at a run's start, so older runs
    show nothing), the run page (Numbers + a dim line under How it went), Records → Totals (section Healing orbs:
    total, health, per run), UI/HealOrbLook (hides the orb's sprites, shows the bottle of its size or a green pixel
    plus, ~1.5× the orb, a soft green glow, a slow bob; follows the setting live), the reminder (map cleared =
    waveContainer Empty, you are hurt, orbs left: the game's buff text above you, green, once per map, 1.5 s after the
    clear). Settings Heal Orbs/Look (Game / Potion / Plus, default Potion) and Heal Orbs/Reminder (on). DEV builds: F9
    drops a small orb. 16 texts × 10. 527 tests (+9).
    [DECISION] The counter counts Small Healing Orbs only (Umut's words); the look and the reminder cover all orb sizes
    (bigger orb → bigger bottle). Medium / large in the counter: one line to change.
16. **From Umut's screenshots (2026-10-25 evening)**: (a) the potion look was huge ("kind of big :D") and it was our
    drawn fallback bottle (the game's PortablePotion wasn't loaded in the castle): now a fixed size (0.7 / 0.85 / 1.0
    units, about twice the game's own bottle) instead of 1.5× the orb's sprite bounds (much bigger than the orb looks),
    and a drawn bottle keeps looking for the game's picture (every 3 s) and takes it when loaded. Walking into it heals
    like the orb (Umut: works as expected). (b) **A bug since 0.13:** labels with "…" overflow whose box is lower than
    the game font's line (about 1.4× its size) are not drawn at all: the Codex page title (34 in 46: missing on the
    Records and Achievements pages), the Records rows' titles and headline numbers (24 in 32, 30 in 40; the README's
    records.png shows them missing), the Daily board names (19 in 26), the Mods window's "Thunderstore" title. Boxes
    now at least 1.5× the font (a scan of all labels found these 7). (c) No <b> on the achievement tiles either.
    README / wiki pictures: docs/images/achievements.png and mods.png from Umut's screenshots (1200 px, 256 colours;
    they still show the missing titles: replace with new shots after the fix if Umut sends them).
17. **Umut's test passed (2026-10-25 ~21:00) and his go for the release**: veils work, the Mods window works (title
    visible now), the chime "sounds nice", the potions are smaller ("still a bit big. But i dont mind this, it helps
    see them"), "the green crosses are even better!". New pictures from his screenshots: docs/images/mods.png (the
    fixed window) and docs/images/heal-orbs.png (game / Potion / Plus side by side, in README and wiki).
    achievements.png still shows the missing page title: README pictures load from GitHub's main branch, so a new
    shot can replace it later with a docs-only sync (no Thunderstore upload). Umut texts the reviewer while the
    release runs (message given in chat: mod browser + self-update explained).
