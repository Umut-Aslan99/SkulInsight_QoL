# Night log 2026-10-25/26 (unattended work session)
Running log, updated after every step, so an auto-compaction or a new session loses nothing.
Decisions Claude made for Umut are marked **[DECISION]**: Umut may change any of them.

## Setup (read first when resuming)
- Plan (Umut's words, 23:50): "Please continue with the /night-shift skill. Turn my PC off, once the version with the
  mod manager is fixed + pushed on github / thunderstore. And in case you are continuing the next version with the
  rewards already, continue that as well until its finished and then turn my PC off, prepare a morning report as well"
- Why 0.14.1: Thunderstore's moderator Robyn (Discord, 21:07-21:58): "We rely on mod managers to do updating, plugins
  updating themselves or other packages is not permitted" ... "I'm going to need you to not include that feature in
  the Thunderstore build" (suggested an `IsThunderstoreBuild` msbuild value with `#if` around the updating code). 0.14.0
  was deactivated; "0.14.0 is not available anymore as a version number ... make sure you use 0.14.1 or higher".
  Robyn: "get my attention when you upload a new version".
- Release tonight is explicitly asked for by Umut (an exception to "nothing outward-facing at night").
- 0.14.1 is built in the worktree C:\Users\Umut\SkulModding\SkulMods-0.14.1 (branch release-0.14.1 from d78d017,
  the 0.14.0 release state), so the unfinished 0.15 work on main stays out of it.
- Watcher: night-check.ps1 (skill night-shift); state in C:\Users\Umut\ClaudeNight; watchdog 75 min.
- Stop rule [DECISION]: Session ≈ 90 % or Weekly ≥ 85 %; the PC shuts down at the end (Umut asked).
- Usage readings: 23:53 S 8 % / W 69 % (session resets ~04:00).

## Queue
1. 0.14.1 (worktree): IsThunderstoreBuild → THUNDERSTORE: no self-update, and the Mods window only lists mods and
   links to their Thunderstore pages (no download / install / update / remove / on-off: "updating ... other packages
   is not permitted"); the AutoUpdate setting hidden; README / wiki / changelog / What's new say so; package.ps1
   builds with the flag and checks the DLL has no updater; tests; release (GitHub + Thunderstore).
2. Merge the flag into main (0.15 keeps it).
3. 0.15 rewards (main, docs/V015_LOG.md): translations of the ~118 new texts in 10 languages, font glyph test,
   What's new / changelog / README / wiki for 0.15, version 0.15.0, Debug build deployed for Umut's test, test todos.
4. Wrap-up: morning report, status doc, memory, shutdown.

## Steps (newest last)
1. 00:00 **0.14.1 released** (GitHub v0.14.1 + Thunderstore DocRun/SkulInsight_QoL 0.14.1; commit 8838e3d on branch
   release-0.14.1 from the 0.14.0 state, merged into main as a851e3d). `-p:IsThunderstoreBuild=true` defines
   THUNDERSTORE (DamageInsight.csproj; release/package.ps1 passes it): SelfUpdate.cs, the update-at-start, Install,
   Remove, SetEnabled, Restart and the restart scripts and the last session's clean-up are compiled out; the
   setting *Update automatically* isn't bound. ModService.CanChange (false there) hides Install / Update / Turn on-off
   / Remove / Turn all / Restart game; store rows show "Thunderstore page" / "Installed" / "This mod" and open the
   details card (with the Thunderstore page button). package.ps1 refuses a DLL that still contains the updater or
   installer (searches "api/experimental/package", "UpdateSelfLater", "InstallAll", "restart.cmd", "-applaunch"; an
   unflagged build has all five, the released one none). 3 new texts in 10 languages; CHANGELOG 0.14.1, README, wiki,
   MOD_MANAGER.md. 527 tests. The GitHub release carries the same (Thunderstore) zip.
   [DECISION] The Mods window's install / update / remove / on-off are left out of the Thunderstore build too, not
   only the self-update: Robyn's rule names "updating themselves or other packages". Debug builds keep everything.
   [DECISION] Released tonight without the reviewer ping (Umut asked for the push tonight; Robyn: "If I'm not around
   I'll get u when I can"). Morning: ping Robyn that 0.14.1 is up (message in the morning todos).
   Untested in game: the browse-only Mods window (the Thunderstore build); the worktree folder SkulMods-0.14.1 couldn't
   be deleted (in use), it is unregistered: delete the folder in the morning.
   Usage 00:00: S 14 % / W 69 %.
2. 00:13 **0.15 finished on Claude's side** (commits f33ee0b + this one): all ~124 new texts in 10 languages (the
   game's own names for the adventurers from its string table: Warrior / Cleric / Hero / Mage / Hunter / Thief, the
   game's word for Omen, the existing Little Bone names; words the CJK fonts can't draw replaced: zh 浏览/瀏覽, 檔,
   ja 鑑, ko 퀘스트 → 임무, 셉니다); What's new for 0.15 (5 lines, plus one about 0.14 for players coming from 0.13:
   0.14.0 was withdrawn); version 0.15.0; CHANGELOG 0.15.0, README (top box, the intro no longer says "changes
   nothing about the balance", a Rewards section, the settings table, roadmap), wiki, the Thunderstore description
   (243 chars, "achievements and rewards per save slot"). Review fix: inscription activations are now tracked per
   inventory (the game never resets bonusCount; the Dark Mirror banners add theirs to a fresh inventory): a new
   inventory gets this run's activations again, the same inventory in a new run gives the last run's back.
   548 tests. Debug build of 0.15.0 deployed 00:12 (Skul closed).
   [DECISION] The 0.15 README intro says the rewards are the mod's only balance change (on by default).
   Untested: everything in game (see morning todos).
3. 00:15 **Test helpers** for the morning: DEV builds, **F11** in a run grants a coin for the held skull's first form,
   a coin for The Chosen Warrior's Armor, an Omen coin and an inscription activation (F10 is the crash-test key).
   [DECISION] DEV builds: a quest step is 3 kills (was 1: every kill would drop gold and quartz). The You tabs fit in
   every language (German widest, 610 of 708 px). The 0.15 Thunderstore package builds and passes the no-updater
   check (release/out, not uploaded). Debug build deployed 00:14.

## End of the night (00:17, Session ≈ 30 %, Weekly ≈ 71 %)
Last commit: see `git log -1` (wrap-up). Ended early because the queue is done: 0.15 is code-complete and everything
left needs Umut (the in-game test, the reviewer ping, the 0.15 release).

## Decisions made tonight (Umut: change any of them)
1. The Thunderstore build leaves out the Mods window's install / update / remove / on-off / restart too, not only the
   self-update (Robyn's rule names "updating themselves or other packages"). Debug builds keep everything.
2. 0.14.1 was uploaded tonight without the reviewer ping (Umut asked for the push tonight); ping in the morning.
3. The GitHub release carries the same Thunderstore zip (no updater in any public build).
4. Inscription activations are tracked per inventory (the game never resets bonusCount).
5. DEV builds: quest step 3 kills; F11 grants test coins, an Omen coin and an activation.
6. The README intro now says the rewards are the mod's only balance change (on by default, one switch).
7. What's new 0.15: five lines, plus one about 0.14 for players who come straight from 0.13.
8. Korean "퀘스트" became "임무" (the game's Korean font has no 퀘), zh 浏览器/瀏覽器 → 列表, ja 図鑑 → 図録, zh-Hant 存檔 → 儲存.
9. (From the evening, V015_LOG step 1/3) adventurer coins summon The Chosen item in any run; Omen coins one per Omen
   item held at a run's end; summoned gear earns no coin back; summons wait until the map is cleared.

## Morning todos for Umut
1. **Ping Robyn** in the Thunderstore thread (ready to paste):
   "Hi Robyn! 0.14.1 is up (uploaded last night). The Thunderstore build no longer has the self-update, and the in-game
   Mods window only lists mods and links to their Thunderstore pages: it doesn't download, install, update, remove or
   toggle anything. Both are compiled out with an IsThunderstoreBuild flag like you suggested, and the packaging script
   refuses a DLL that still contains the updater code. Thanks a lot for your help!"
2. Delete the folder `C:\Users\Umut\SkulModding\SkulMods-0.14.1` (the 0.14.1 worktree; git already let it go, the
   folder was in use).
3. The r2modman profile "Release test" still holds my 0.13.99 test build (the self-update test is cancelled): update
   it to 0.14.1 in r2modman, start it, Esc → Mods: no Install / Update / Remove buttons, rows say "Thunderstore page" /
   "Installed" and open the details. Close the game and tell me.
4. Test 0.15 (Steam start, the Debug build is installed): the 10 test todos at the end of docs/V015_LOG.md (settings,
   Rewards tab, achievement page and notice, stats and the combat log, quests every 3rd kill, F11 coins and Summon,
   activations incl. a continued run, a dismantled legendary, Rewards off, a run page). I read
   BepInEx/LogOutput.log afterwards.
5. Then: fixes, the reviewer ping, and the 0.15 release (`release/release.ps1`).
