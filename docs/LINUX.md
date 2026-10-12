# Linux and Steam Deck

SkulInsight QoL runs on Linux and Steam Deck through **Proton**, Steam's layer for Windows games. This page covers the
setup, the problems players ran into and how to fix them.

It applies to every distribution (Ubuntu, Mint, Pop!_OS, Fedora, Arch, Bazzite, SteamOS, ...). What matters is how
Steam, Proton and the mod manager are installed, not the distribution.

## Quick setup

1. **Force Proton.** In Steam, right-click Skul → *Properties* → *Compatibility* → tick *Force the use of a specific
   Steam Play compatibility tool* and pick a current Proton (for example Proton 9.0). Steam then downloads the Windows
   version of Skul.
2. **Start Skul once without mods** from Steam, wait for the main menu, then quit. This creates Skul's Proton prefix,
   which the mod manager needs.
3. **Leave the launch options empty:** *Properties* → *General* → *Launch options*. The only exception is the Flatpak
   version of r2modman (see below).
4. **Install SkulInsight QoL** in r2modman or Gale and press **Start modded**.

It works when you see damage numbers on hits and the pause menu (Esc) has a **SkulInsight QoL** entry.

## Why Proton is needed

- Skul has a native Linux version, and Steam uses it by default on Linux and Steam Deck. BepInExPack Skul, the loader
  the mod needs, only contains the Windows loader. So the mod only runs with the Windows version through Proton.
- The loader is a `winhttp.dll` in the game folder. Wine only uses it when a DLL override says so. Before every
  modded start, r2modman and Gale write that override into Skul's Proton prefix
  (`steamapps/compatdata/1147560/pfx/user.reg`, 1147560 is Skul's Steam app id) and pass the loader's path to the
  game. That's why the prefix must exist (step 2), and the mod managers look for it in the same Steam library as the
  game.
- The mod managers decide between Proton and native from the game folder: a `.exe` in it (or an empty file named
  `.forceproton`) means Proton. r2modman also has a switch in its settings, *Change launch behaviour*
  (Auto / Native / Proton). For Skul use Auto or Proton.

## Mod managers

- **r2modman (.deb, AppImage, AUR):** no launch options needed. If r2modman shows you a launch option with
  `linux_wrapper.sh` for Skul, don't use it. That one is for native Linux games and stops Skul from starting modded.
  r2modman offers it when it saw the native version of Skul.
- **r2modman (Flatpak):** this version starts games through a Steam link and needs a launch option of its own. It shows
  you the line, which contains `web_start_wrapper.sh`: use exactly that one, never `linux_wrapper.sh`. (This follows
  r2modman's own code; we haven't tested the Flatpak version ourselves yet.)
- **Gale:** decides and sets up Proton the same way as r2modman. No launch options needed.
- Use a recent mod manager. r2modman 3.2.4 to 3.2.7 couldn't start modded games on Linux (fixed in 3.2.8).

## Manual installation (no mod manager)

This is also the simplest way to play modded in the Steam Deck's Game Mode, because it only needs a launch option.

1. Do steps 1 and 2 of the quick setup (Windows version through Proton, one start without mods).
2. Install [BepInExPack Skul](https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/) into the
   game folder, the one with `Skul.exe` (Steam: *Properties* → *Installed Files* → *Browse*).
3. Set Skul's launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`
4. Start the game once and quit (BepInEx creates its folders).
5. Copy `DamageInsight.dll` into `Skul/BepInEx/plugins/SkulInsight_QoL/` and start the game.

Every start from Steam is modded now. To play without mods, remove the launch option.

## Steam Deck

- Do the setup in Desktop Mode. "Start modded" in r2modman or Gale works there. To play modded in Game Mode, use the
  manual installation.
- **Game on the SD card:** Steam may keep the Proton prefix on internal storage
  (`~/.local/share/Steam/steamapps/compatdata/1147560`) while the mod manager looks for it next to the game on the SD
  card. See "compatibility data does not exist" below.

## Troubleshooting

**r2modman: `Error invoking remote method 'node:fs:readFile': Error: ENOENT: no such file or directory, open
'…/compatdata/1147560/pfx/user.reg'`** (Gale: an error about reading the wine registry)
Skul has never run through Proton in this Steam library, so the prefix is empty. Do steps 1 and 2 of the quick setup,
then press Start modded again.

**r2modman: `compatibility data does not exist in Steam's specified location`**
Same cause, or the prefix is in a different Steam library than the game (SD card, second drive). Start Skul once from
Steam first. If the error stays and the prefix exists elsewhere (usually
`~/.local/share/Steam/steamapps/compatdata/1147560`), link it into the game's library. Do this only if the folder
doesn't exist there yet:

```
ln -s ~/.local/share/Steam/steamapps/compatdata/1147560 /path/to/library/steamapps/compatdata/1147560
```

**Start modded: Steam shows Skul as running, then it closes at once (or nothing opens)**
Skul's launch options contain `linux_wrapper.sh`, left over from r2modman's setup for native games. Clear Skul's
launch options (*Properties* → *General*).

**The game starts, but without the mod** (no damage numbers, no SkulInsight QoL in the pause menu, no
`BepInEx/LogOutput.log` in the profile folder)
- Steam runs the native Linux version: the game folder has `Skul.x86_64` and no `Skul.exe`. Force Proton (step 1).
- A leftover launch option: clear it (except the Flatpak r2modman line).
- r2modman's *Change launch behaviour* is set to Native: set it to Auto or Proton.

**A Flatpak app (Steam or the mod manager) doesn't see a Steam library on another drive**
Flatpak apps only see the folders they are allowed to. Give the app access, for example with Flatseal or
`flatpak override --user --filesystem=/path/to/library <app id>`.

## Where things are

| What | Where |
|---|---|
| Steam, Debian/Ubuntu package (Ubuntu, Mint, Pop!_OS, ...) | `~/.steam/debian-installation` (also reachable as `~/.steam/steam`) |
| Steam, most other installs | `~/.local/share/Steam` |
| Steam, Flatpak | `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` |
| Skul's Proton prefix | `<Steam library>/steamapps/compatdata/1147560` |
| r2modman profiles | `~/.config/r2modmanPlus-local/SkulTheHeroSlayer/profiles/<profile>` (Flatpak: under `~/.var/app/<app id>/config/`) |
| Profile folder in r2modman / Gale | *Settings* → *Browse profile folder* |
| Mod log | `<profile>/BepInEx/LogOutput.log` |
| Game log, Windows version through Proton | `<prefix>/pfx/drive_c/users/steamuser/AppData/LocalLow/Southpaw Games/Skul/Player.log` |
| Game log, native version | `~/.config/unity3d/Southpaw Games/Skul/Player.log` |

## The check script

If it still doesn't work, run our check script and attach its report to a
[bug report](https://github.com/Umut-Aslan99/SkulInsight_QoL/issues):

```
curl -LO https://raw.githubusercontent.com/Umut-Aslan99/SkulInsight_QoL/main/tools/linux/skul-linux-check.sh
bash skul-linux-check.sh
```

It only reads and changes nothing. It writes `~/skul-linux-report.txt` with what's needed to find the problem:
- your Steam libraries and their file systems
- which version of Skul is installed (Windows or native)
- the Proton prefix
- the launch options and compatibility tool
- the mod manager's logs

The report contains your Linux user name (in paths). Steam account numbers are hidden.

## Not supported

- **The native Linux version of Skul:** BepInExPack Skul has no Linux loader. Use the Windows version through Proton.
