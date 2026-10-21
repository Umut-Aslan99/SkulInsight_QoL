# macOS

SkulInsight QoL runs on the **Mac version of Skul** from Steam, on Intel Macs and on Apple Silicon Macs (M1 to M4).
You don't need Wine, CrossOver or Windows. One Terminal script sets everything up, and you paste one line into Steam.

> **New (October 2026):** the Mac setup is built and tested on our side, but nobody has run it on a real Mac yet. If
> you try it, please tell us how it went (a GitHub issue, or in the Skul Discord), even if it just works.

## Quick setup

1. **Start Skul once without mods** from Steam, wait for the main menu, then quit. On an Apple Silicon Mac, macOS asks
   to install **Rosetta** the first time if it isn't there yet. Say yes, because Skul needs it even without mods.
2. **Run the installer.** Open **Terminal** (Spotlight: Cmd+Space, type *Terminal*), paste this line and press Enter:

   ```sh
   curl -fsSL -o ~/Downloads/install-mac.sh https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/tools/mac/install-mac.sh && sh ~/Downloads/install-mac.sh
   ```

   It downloads the script into your Downloads folder and runs it. The script is plain text, so you can read it
   first: [install-mac.sh](https://github.com/Umut-Aslan99/SkulInsight_QoL/blob/main/tools/mac/install-mac.sh).
3. **Paste the launch option.** At the end, the script shows a line like this, and it's already in your clipboard:

   ```
   "/Users/you/Library/Application Support/Steam/steamapps/common/Skul/run_bepinex.sh" %command%
   ```

   In Steam, right-click Skul → *Properties* → *General* → *Launch options*, press Cmd+V and close the window.
4. **Start Skul from Steam** as usual.

It works when you see damage numbers on hits and the pause menu (Esc) has a **SkulInsight QoL** entry.

- **Update the mod:** run the same Terminal line again. Your settings and records stay.
- **Play without mods:** empty the launch option. Put it back to play modded again.
- **Remove everything:** `sh ~/Downloads/install-mac.sh --uninstall`, then empty the launch option.
- **Skul in another Steam library** (an external drive, for example): the script finds the libraries Steam knows
  about. If it still can't find Skul, add `--game "/path/to/the folder with Skul.app"` at the end of the line.

## What the script does

You can also do all of it by hand (next section). The script:

1. Finds `Skul.app` in your Steam libraries. It stops if it finds the Windows version (`Skul.exe`) or if Skul is
   running.
2. Downloads **BepInEx 5.4.23.5 for macOS** (the mod loader) from BepInEx's GitHub. It puts `run_bepinex.sh`,
   `libdoorstop.dylib` and `BepInEx/core` into the Skul folder.
3. Downloads **BepInExPack Skul** from Thunderstore and takes only its **unstripped libraries**, the same files that
   Windows players use. They go into `BepInEx/unstripped_corlib`, all except `UnityEngine.GameCenterModule.dll`.
   - Both downloads are checked against fixed SHA-256 checksums.
4. Sets `run_bepinex.sh` up for Skul: the game's name, the libraries folder, and a full path to `libdoorstop.dylib`.
5. Downloads the newest SkulInsight QoL release from GitHub into `BepInEx/plugins/SkulInsight_QoL/`.
6. Makes the start script executable and removes macOS's quarantine flag from the new files.

It never touches your saves, Steam's settings or anything outside the Skul folder.

## Manual installation

1. Open the Skul folder: Steam → right-click Skul → *Manage* → *Browse local files*. It's the folder that holds
   `Skul.app`, normally `~/Library/Application Support/Steam/steamapps/common/Skul`.
2. Download [BepInEx_macos_universal_5.4.23.5.zip](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
   and unpack it into that folder.
3. Download [BepInExPack Skul](https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/) with
   *Manual Download*. Unpack it somewhere else.
   - Copy every `.dll` from its `BepInExPack/2020.3.34` folder into a new folder `Skul/BepInEx/unstripped_corlib`.
   - Delete `UnityEngine.GameCenterModule.dll` there.
4. Open `run_bepinex.sh` with TextEdit and change three lines:
   ```sh
   executable_name="Skul.app"
   dll_search_path_override="/Users/you/Library/Application Support/Steam/steamapps/common/Skul/BepInEx/unstripped_corlib"
   doorstop_name="${doorstop_directory}libdoorstop.${lib_extension}"
   ```
   - Use your own full path in the second line.
   - The third line replaces `doorstop_name="libdoorstop.${lib_extension}"` further down.
   - TextEdit must save the file as plain text: *Format* → *Make Plain Text*.
5. In Terminal, inside the Skul folder (type `cd `, drag the folder into the window, press Enter):
   ```sh
   chmod +x run_bepinex.sh
   xattr -dr com.apple.quarantine .
   ```
6. From the [newest release](https://github.com/Umut-Aslan99/SkulInsight_QoL/releases/latest), copy
   `plugins/DamageInsight.dll` out of the zip into `Skul/BepInEx/plugins/SkulInsight_QoL/`.
7. Set the launch option as in step 3 of the quick setup: `"<your Skul folder>/run_bepinex.sh" %command%`.

## Why it works this way

- **Same game code.** The Mac version of Skul (1.9.2) has the same game code as the Windows version:
  `Assembly-CSharp.dll` is byte for byte the same file. It uses Mono, like Windows, so BepInEx 5 and every hook of
  the mod work on it.
- **Rosetta on Apple Silicon.** Skul's Mac version is built for Intel only, so on Apple Silicon it always runs through
  Rosetta 2. That's normal for Skul, and it means BepInEx runs in its Intel version, which has been used for years.
- **Stripped libraries.** Skul's own .NET libraries are cut down, so BepInEx can't start with them. That's why the
  Windows pack has its "unstripped" copies, and the Mac setup uses the same files.
  - The Windows copy of `UnityEngine.GameCenterModule.dll` lacks the Mac's Game Center code, so the game keeps its
    own copy of that one file.
- **Full path to Doorstop.** On Apple Silicon, `run_bepinex.sh` starts the game through macOS's `arch` tool, and
  `arch` drops the variable that tells macOS where `libdoorstop.dylib` is. A full path doesn't need that variable.
  - Doorstop 4.6 fixes it the same way. BepInEx 5.4.23.5 still ships Doorstop 4.5.
- **No mod manager.** BepInExPack Skul and the Thunderstore mod managers (r2modman, Gale, the Thunderstore App) only
  exist for Windows and Linux. That's why the Mac setup is a script.
- **Other BepInEx mods.** Most other BepInEx 5 mods for Skul only contain `.dll` files. Put them into
  `BepInEx/plugins` the same way. Mods that ship their own Windows libraries won't work on a Mac.

## Troubleshooting

**The game starts, but there are no damage numbers and no SkulInsight QoL entry in the pause menu.**
The launch option is missing or has a typo. Look in the Skul folder: if there is no `BepInEx/LogOutput.log`, BepInEx
never started. Check the launch option:
- It needs the quotes around the path, because "Application Support" has a space.
- It needs `%command%` at the end.

**The game doesn't start at all after setting the launch option.**
Run `sh ~/Downloads/install-mac.sh --check` and look at the report. Common causes:
- `run_bepinex.sh` isn't executable. Fix: `chmod +x run_bepinex.sh` in the Skul folder.
- macOS blocked `libdoorstop.dylib` ("cannot be opened because Apple cannot check it"). Run the installer again, or
  run `xattr -dr com.apple.quarantine .` in the Skul folder. You can also use *System Settings* →
  *Privacy & Security* → *Open Anyway*.

**`LogOutput.log` shows `MissingMethodException` with `GetPEKind`, or BepInEx stops early.**
BepInEx didn't find the unstripped libraries. Check that `BepInEx/unstripped_corlib` has about 78 `.dll` files and
that `dll_search_path_override` in `run_bepinex.sh` points to it.

**`Please set executable_name to a valid name`** in Terminal: `run_bepinex.sh` wasn't edited. Run the installer
again.

**Korean, Japanese or Chinese text shows boxes in the mod's texts.**
The mod borrows a system font for characters that Skul's fonts don't have. Before version 0.14 the mod only knew
the Windows fonts. From 0.14 on, it also uses the Mac's own fonts (Apple SD Gothic Neo, Hiragino, PingFang / STHeiti,
Arial Unicode).

**No Steam overlay in modded starts.** That's expected. macOS drops Steam's overlay when a start script runs first.
Without the launch option, the overlay is back.

**After a Skul update.** Steam updates the game and leaves BepInEx and the mod in place. *Verify integrity of game
files* doesn't delete them either. If something stops working after an update, run the installer again.

## Where things are

| What | Where |
|---|---|
| Skul folder | `~/Library/Application Support/Steam/steamapps/common/Skul` (Finder: *Go* → *Go to Folder…*, Cmd+Shift+G) |
| BepInEx log | `Skul/BepInEx/LogOutput.log` |
| The mod's settings | `Skul/BepInEx/config/docrun.skulinsight_qol.cfg` (easier: pause menu → SkulInsight QoL) |
| The mod's records, crash reports | `Skul/BepInEx/DamageInsight/` (crash reports in `CrashReports`) |
| Unity's log | `~/Library/Logs/Southpaw Games/Skul/Player.log` |
| Game saves | Steam Cloud, locally in `~/Library/Application Support/Southpaw Games/Skul` |

## The check script

`sh ~/Downloads/install-mac.sh --check` changes nothing. It writes `~/skul-mac-report.txt` with what we need to help
you:
- your macOS version, CPU and Rosetta;
- your Steam libraries and the Skul version;
- the BepInEx files and the launch option;
- the errors in `LogOutput.log`.

Please attach that file to your bug report, together with `BepInEx/LogOutput.log` and the newest crash report.

## Other ways (not tested by us)

- **The Windows version in CrossOver or Parallels:** this works like Proton on Linux. Install BepInExPack Skul into
  the Windows Skul folder and tell Wine to load `winhttp.dll` from the game folder.
  - Launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%`, or in CrossOver: the bottle's *Wine Configuration* →
    *Libraries* → `winhttp` → *Native then Builtin*.
  - It costs a CrossOver or Parallels license. Whisky, the free option, is no longer developed.
  - The native route above is simpler and runs better.
- **r2modmac:** a community mod manager for macOS (unsigned). We haven't tried it with Skul. Skul's Thunderstore mods
  bring the Windows BepInEx pack, which can't start the Mac version, so use the script for the Mac version.
