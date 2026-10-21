# Night log 2026-10-21 (unattended work session: modding Skul on macOS)
Running log, updated after every step, so an auto-compaction or a new session loses nothing.
Decisions Claude made for Umut are marked **[DECISION]**: Umut may change any of them.
The previous night's log (2026-10-11, QA capture) is in git history (`git show 6e7e9cd:docs/NIGHT_LOG.md`).

## Setup (read first when resuming)
- Plan (Umut's words, 00:40): "Someone in the skul official community asked, how to mod on mac. Can we do the
  research for him? make this research a night shift task /night-shift which means, turn off my PC when its done and
  check the session usage etc."
- Goal: find out whether and how Skul + BepInEx + SkulInsight QoL run on a Mac (native Mac version? Mono or IL2CPP?
  Apple Silicon? mod managers? Windows version through CrossOver / Wine / Whisky / Parallels?), write a public guide
  like docs/LINUX.md, and a short reply Umut can paste in the Skul Discord. PC shuts down at the end.
- Not tonight: posting the reply, pushing the guide to GitHub, a release (all outward-facing: morning todos).
- Watcher: night-check.ps1 (skill night-shift); state in C:\Users\Umut\ClaudeNight
  - [DECISION] screen.txt set from `left` to `main`: the usage panel (VS Code, Claude Code sidebar) is in the top-left
    corner of the main screen tonight; the left monitor shows Discord. Set it back to `left` when needed.
- Stop rule [DECISION]: Session ≈ 90 % or Weekly ≥ 85 % (Weekly starts at 73 %, so Weekly is the real limit), or
  the queue is done.
- Umut's addition (00:45): "If you find a way that we can make our mod compatible with mac, create mac
  compatibility tonight until the night shift is over"
- Usage readings: 00:41 S 1 % / W 73 %; 00:49 S 4 % / W 73 %; 01:00 S 14 % / W 74 %; 01:05 S 18 % / W 75 %

## Queue
1. Research: Skul's Mac version on Steam (depots, Unity version, Mono or IL2CPP, Intel or Apple Silicon).
2. Research: BepInEx 5 on macOS (run_bepinex.sh, doorstop dylib, Apple Silicon / Rosetta, Gatekeeper / quarantine,
   unstripped corlib that Skul needs), and what BepInExPack Skul contains.
3. Research: mod managers on macOS (r2modman, Gale, Thunderstore App), and the Windows-version route (CrossOver,
   Whisky, Wine, Parallels, Game Porting Toolkit) with the winhttp override.
4. Check the mod's own code for Windows-only calls (opening folders, paths) that would break on a Mac.
5. Write docs/MACOS.md (public guide), a Discord reply draft, README/wiki lines (drafts, not pushed).
6. Wrap-up: status doc, memory, shutdown.

## Steps (newest last)
1. 00:53 **Research done: the native Mac version can run the mod.** Facts (all checked on the real files: the Mac
   depot 1147568 was downloaded with the Steam console `download_depot 1147560 1147568` to
   `C:\Program Files (x86)\Steam\steamapps\content\app_1147560\depot_1147568`, 1.7 GB):
   - Steam: Skul has Windows, macOS and Linux depots; Mac launch = `Skul.app/Contents/MacOS/Skul`, version
     v1.09.02.3a3, Unity 2020.3.34f1, bundle id com.SouthpawGames.Skul, macOS 10.13+.
   - **Mono, not IL2CPP** (Frameworks/libmonobdwgc-2.0.dylib, Resources/Data/Managed) → BepInEx 5 works.
   - **x86_64 only** (no arm64 slice): on Apple Silicon the game always runs through Rosetta 2, so the well-tested
     x64 BepInEx/HarmonyX path applies, no arm64 JIT problems.
   - **Ad-hoc signed, no hardened runtime, no library validation** → `DYLD_INSERT_LIBRARIES` (Doorstop) is allowed.
     Only Valve's steam_api is hardened (irrelevant, it is a plugin dylib).
   - **Assembly-CSharp.dll is byte-identical to Windows** (md5 15e2eb16), so all patches hit the same code. Platforms*,
     Unity.TextMeshPro, UnityEngine.UI identical too; CoreModule/System/mscorlib differ only by stripping.
   - Managed is **stripped like on Windows** → the unstripped libs are needed (the mod also references
     UnityEngine.ImageConversionModule + UnityWebRequestTextureModule, which only the unstripped set has). Member diff:
     every Mac UnityEngine module is a subset of BepInExPack Skul's unstripped one, **except GameCenterModule** (Mac has
     GameCenterPlatform, the Windows-made unstripped one doesn't) → leave that file out on Mac. mscorlib/System gaps
     (90/11 linker stubs) are the same on Windows, where it works.
   - BepInEx 5.4.23.5 has `BepInEx_macos_universal_5.4.23.5.zip` (Doorstop 4.5.0, universal dylib). Its
     run_bepinex.sh on Apple Silicon runs `arch -e DYLD_INSERT_LIBRARIES=libdoorstop.dylib` and loses
     DYLD_LIBRARY_PATH (arch is a platform binary) → the relative dylib name may not be found; Doorstop 4.6.0 (2 days
     old) fixes it. Our installer makes the dylib path absolute instead.
   - Mod managers: r2modman / Gale / Thunderstore App have no macOS version; r2modmac (community, unsigned) exists,
     untested with Skul; Thunderstore's Skul mods depend on the Windows-only BepInExPack Skul. → manual install script.
   - Windows-version route (CrossOver / Whisky (discontinued) / Parallels) is possible like Proton
     (`WINEDLLOVERRIDES="winhttp=n,b"`), but the native route is simpler and free.
   - The mod's code: paths all use Path.Combine + BepInEx.Paths (BepInEx root = folder next to Skul.app), saves go
     through the game, Unity log via Application.consoleLogPath. Windows-only bits: `OpenFolder` builds
     `file:///` + path (wrong on a Mac, spaces in "Application Support" unescaped), CJK font fallback only knows
     Windows/Noto fonts, the Wine check P/Invokes kernel32 (caught, but pointless off Windows).
   - **[DECISION]** Route = native Mac version + BepInEx 5.4.23.5 macOS + BepInExPack Skul's unstripped libs (the same
     files Windows players use) minus GameCenterModule + a Steam launch option, set up by one Terminal script.
     Reason: free, no Wine, the game code is identical, everything Windows players run is reused.
   Untested: everything on a real Mac (we have none). Morning: Umut sends the guide to the Mac player.
2. 00:56 **The mod's Mac fixes** (new `Tools/PlatformPaths.cs`, plain logic + 4 tests, 468 tests pass, Debug deployed,
   Release builds): crash reports' "Open folder" builds an escaped `file:///Users/...` URL on a Mac (Windows keeps its
   old form byte for byte); the CJK font fallback also knows Apple's fonts (Korean AppleSDGothicNeo, Japanese
   Hiragino W6/W3, Chinese PingFang / Hiragino Sans GB / STHeiti, Arial Unicode last) and looks in /System/Library/Fonts,
   …/Supplemental, /Library/Fonts, ~/Library/Fonts, comparing names NFC-normalized (macOS may store the Japanese
   names decomposed); the Wine check returns at once off Windows. Proton players keep the Windows paths (the game is
   the Windows version there; IsWindows = `Path.DirectorySeparatorChar == '\\'`).
   **[DECISION]** No setting for this: it only changes what happens on a Mac, where the old code could not work.
   Untested: on a Mac (fonts, Open folder). Windows: nothing visible changes (Umut may click "Crash reports folder"
   [Open] once).
3. 01:00 **Mac installer `tools/mac/install-mac.sh`** (POSIX sh: macOS's /bin/sh is bash 3.2) + its test
   `tools/mac/test-install-mac.sh` (34 checks, ALL PASSED in Git Bash). `sh install-mac.sh` finds Skul.app in every
   Steam library (default + libraryfolders.vdf) or takes `--game`, refuses a Windows copy and a running Skul, downloads
   BepInEx 5.4.23.5 macOS + BepInExPack Skul (both SHA-256 pinned) + the newest SkulInsight release from the GitHub
   API, copies the 78 unstripped libraries to `BepInEx/unstripped_corlib` (GameCenterModule left out), edits
   run_bepinex.sh (each edit must match exactly once, else it stops: executable_name="Skul.app",
   dll_search_path_override relative + resolved with the script's abs_path, Doorstop by full path for Apple Silicon),
   chmod +x, removes quarantine flags, prints the Steam launch option `"<game>/run_bepinex.sh" %command%` and puts it
   in the clipboard (pbcopy). `--check` writes ~/skul-mac-report.txt (macOS, CPU, Rosetta, libraries, Skul version
   and arch, BepInEx files and edits, quarantine, launch option from localconfig.vdf, LogOutput.log errors);
   `--uninstall` removes loader + mod, keeps BepInEx/config, the mod's records and the saves; `--no-mod`, `--mod-zip`.
   The test fakes an Apple Silicon Mac (uname/sysctl/file/arch that drops DYLD_* like the real one) and starts the
   edited run_bepinex.sh like Steam does: the fake game receives Doorstop by full path, the preloader, the
   unstripped folder and the Steam arguments. sync-public publishes install-mac.sh (the test stays private).
   **[DECISION]** Steam overlay: not kept in modded starts (/bin/sh drops Steam's DYLD_INSERT_LIBRARIES; Doorstop 4.6
   re-adds it via STEAM_DYLD_INSERT_LIBRARIES, untested there, and an overlay dylib that fails to load would stop
   the game). Reason: the safer start; Skul doesn't need the overlay.
   **[DECISION]** The launch option is pasted by the player (the script prints it and copies it): editing Steam's
   localconfig.vdf while Steam runs gets overwritten, and it is the player's Steam account.
   Untested: on a real Mac (needs the Mac player).
4. 01:03 **Docs: `docs/MACOS.md`** (public guide like LINUX.md: quick setup with the one Terminal line, what the
   script does, manual installation, why it works this way, troubleshooting, where things are, the check script,
   other ways: CrossOver/Parallels with `WINEDLLOVERRIDES="winhttp=n,b"`, r2modmac untested). README: section
   *macOS* under Installation, Compatibility line, Feedback asks for the Mac report. Wiki draft
   (docs/wiki/thunderstore-wiki.md): *macOS* block under Installation + a macOS FAQ entry (Umut pastes). Bug report
   template: macOS in the System line + the Mac report.
   **[DECISION]** The guide says openly that nobody has run it on a real Mac yet and asks for feedback.
   **[DECISION]** The font fix is announced for 0.14 (next release), no 0.13.x hotfix: on a Mac 0.13.1 already works
   apart from CJK boxes and the Open-folder button.
   Note: the one-line install command and the guide links only work after `sync-public.ps1` pushed main to the
   public repo (morning todo).
5. 01:05 Checked the official BepInEx docs (bepinex-docs, steam_interop.md, macOS tab): the launch option there is
   `"<full path>/run_bepinex.sh" %command%`, the same as ours. Wrap-up: this log, PROJECT_STATUS, memory.

## End of the night (01:05, Session 18 %, Weekly 75 %)
The queue is done: research, the mod's Mac fixes, the installer + its test, the guide and the doc lines. Last commit:
the wrap-up commit after c8cd0b5. 468 tests + the installer test (34 checks) pass, the Debug build is deployed, and
the working tree is clean. Nothing was pushed or released. The Mac depot download (1.7 GB) stays in
`C:\Program Files (x86)\Steam\steamapps\content\app_1147560\depot_1147568` for later checks; it can be deleted.

## Decisions made tonight (Umut: change any of them)
1. screen.txt `left` → `main`: the usage panel was on the main screen tonight.
2. Stop rule: Weekly ≥ 85 % or Session ≈ 90 % (Weekly was the real limit); the night ended early because the queue
   was done.
3. Route = native Mac version + BepInEx 5.4.23.5 macOS + BepInExPack Skul's unstripped libraries (without
   GameCenterModule), set up by one Terminal script. Not CrossOver/Wine: costs money, slower, and the game code is
   identical anyway.
4. The mod's Mac fixes have no setting: they only change what happens on a Mac.
5. No Steam overlay in modded Mac starts (the safer start).
6. The player pastes the launch option (the script prints it and copies it); we don't edit Steam's files.
7. The guide says openly that nobody has run it on a real Mac yet.
8. The font / Open-folder fixes ship with 0.14, no 0.13.x hotfix (0.13.1 already runs on a Mac).

## Morning todos for Umut
1. **Publish the Mac guide and installer** (docs only, no release): `release/sync-public.ps1 -NoTag -Message "macOS
   guide and installer"`. That makes the one-line command and the docs/MACOS.md links work. I can run it as soon as
   you say "push it". Afterwards check that this opens:
   https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/tools/mac/install-mac.sh
2. **Answer the Mac player in the Skul Discord** (#general-chat) with the reply below, after step 1.
3. **When the Mac player answers:** send me their `~/skul-mac-report.txt` (`sh ~/Downloads/install-mac.sh --check`)
   and `Skul/BepInEx/LogOutput.log`. I'll read the BepInEx lines first: Chainloader start, "SkulInsight QoL" loaded,
   the [SelfTest] patch results.
4. **Optional, Windows:** Esc → SkulInsight QoL → *Crash reports folder* → Open should still open Explorer as before.
   In the Debug build, which is deployed, check that Korean/Japanese/Chinese still show the Windows font (LogOutput.log
   line "Fonts: malgunbd.ttf draws ..." or similar).
5. **Wiki:** paste the new *macOS* blocks (Installation + FAQ) from docs/wiki/thunderstore-wiki.md with the next
   wiki update. I'll put it on the clipboard when you want.

### Discord reply (draft, for #general-chat)
> Hey! I looked into it: Skul's Mac version can be modded. It has the same game code as Windows, so BepInEx works on
> it. The mod managers (r2modman etc.) have no Mac version, so I wrote an installer for my mod SkulInsight QoL. It
> also sets up BepInEx for other mods.
> 1. Start Skul once without mods, then quit.
> 2. Open Terminal and paste:
> `curl -fsSL -o ~/Downloads/install-mac.sh https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/tools/mac/install-mac.sh && sh ~/Downloads/install-mac.sh`
> 3. It copies a launch option to your clipboard. In Steam: right-click Skul → Properties → Launch options → Cmd+V.
>    Then start Skul.
>
> Guide and manual steps: https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/docs/MACOS.md
> It's brand new and I have no Mac to test it on, so please tell me if it works! If it doesn't, run
> `sh ~/Downloads/install-mac.sh --check` and send me the file `skul-mac-report.txt` from your home folder.
