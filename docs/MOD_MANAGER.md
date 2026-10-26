# In-game mod manager

Written 2026-10-25 by Claude after Umut's request. Status: **built for 0.14** (Umut: "yes it would be allowed",
GaleModManager is on Skul's Thunderstore page; he tells the reviewer about it at the 0.14 upload ping). Code:
DamageInsight/Mods/ (ZipReader, ThunderstoreCatalog, ModFolders, ModService), UI/ModManagerWindow.cs, the Mods
button in UI/SettingsPage.AttachTo; tests DamageInsight.Tests/ModManagerTests.cs. Decisions are marked
**[DECISION]**, open points **[OPEN]**.

## Where it stands (2026-10-26, decided)
- **Thunderstore (and the GitHub release, the same zip):** since 0.14.1 the Mods window only browses. It lists
  Thunderstore's Skul mods and the mods you have, shows details and opens a mod's Thunderstore page; installing,
  updating, turning mods on/off, removing and the restart are left to the mod manager (r2modman, Gale). Built by
  `release/package.ps1` with `-p:IsThunderstoreBuild=true` (THUNDERSTORE: the installer code is not compiled in);
  package.ps1 refuses a DLL that still holds it. Checked on the published 0.14.1 (downloaded from Thunderstore):
  no installer code in it.
- **Developer (Debug) builds:** the full manager (install, update, on/off, remove, restart). That is the build on
  Umut's PC, so he sees Install buttons; players never get it.
- **[Umut 2026-10-26] later, not yet:** a separate GitHub download with the full manager, next to the Thunderstore
  build. The code stays in the source behind `#if !THUNDERSTORE` for it.

## Umut's request (2026-10-25)
"Another feature should be sort of a 'modloader' in my QoL mod. Like a menu that shows installed mods and displays
mods on Thunderstore, with an easy download / install button (accessible on all platforms Windows, Linux, Mac) and
auto restart / reload the mods for easy use."

## Why we ask first
Every SkulInsight update on Thunderstore is reviewed by hand, and the reviewers read the code (they asked about the
Wine check in the crash reports). A mod that downloads other packages and puts code into BepInEx/plugins is what a mod
manager does, but inside a mod it is unusual. Thunderstore's public docs (wiki.thunderstore.io, "Creating a package")
have no rule about it either way, so we ask before building, rather than having the whole package rejected or hidden
like 0.13.0 was.

## Facts (checked 2026-10-25)
- **Thunderstore's Skul list:** `https://thunderstore.io/c/skul-the-hero-slayer/api/v1/package/` (no login): 56
  packages, 197 KB, each with owner, name, categories, deprecated / NSFW flags, rating and every version (number,
  description, icon, dependencies, download URL, size, downloads). 51 are not deprecated, 1 is NSFW. The most
  downloaded: Gale (the manager itself), BepInExPack_Skul, LuckBasedRNG, LegendaryStart, DevMenuTurbo.
- **Installing like r2modman** is already worked out in `tools/mac/install-mac.sh --add` (0.13/0.14, tested on all
  56 packages): newest version from the API, dependencies too, BepInExPack_Skul skipped, files to
  `plugins/<Namespace-Name>/` (root DLLs and `plugins/`), `patchers/<Namespace-Name>/`, `config/` without
  overwriting; zips may use Windows backslash paths (OnlineCoopBeta). The C# version would follow the same rules.
- **Installed mods:** `BepInEx.Bootstrap.Chainloader.PluginInfos` (GUID, name, version, DLL path) at run time; a
  Thunderstore package is recognised by its folder name `Namespace-Name` (r2modman / Gale / our layout) or a
  `manifest.json` next to it.
- **No reload without a restart:** BepInEx 5 loads plugins once at start, and Mono can't unload assemblies; Harmony
  patches and preloader patchers only take effect from the start. So "reload" = restart the game.
- **Restart through Steam:** quit, and a small helper started just before waits until the game is gone and opens
  `steam://rungameid/1147560` (Windows: `cmd /c`; macOS: `sh -c "sleep ...; open steam://..."`; Linux/Proton: the
  Windows way inside Wine, which hands steam:// links to the system). Steam starts the game with its launch options,
  so the Mac launch option (`run_bepinex.sh`) and Proton stay in place. [OPEN] test on Linux.
- **Mod managers:** when the game runs from an r2modman or Gale profile (BepInEx root inside their profile folders),
  installing from the game would bypass their records. [DECISION] there the page lists mods and links to Thunderstore,
  but installs only after a warning ("your mod manager won't know about it"), or not at all (reviewer's view).

## What it would be (if allowed)
A new book section or a window from the settings page (**Mods**):
1. **Installed**: every loaded BepInEx plugin (name, version, author, where it is), its Thunderstore page if known,
   "update available" when Thunderstore has a newer version; Disable (rename the DLL to `.old`, like r2modman) and
   Remove (moved to a backup folder, not deleted).
2. **Thunderstore**: the Skul packages with icon, description, downloads, rating and categories; deprecated and NSFW
   ones hidden; search; Install (with its dependencies, shown first), Update.
3. **Restart now** after a change (the restart above), or "on the next start".
- Off until turned on (it goes online), like the daily run. Only Thunderstore's own download URLs over HTTPS; zip
  paths checked (no `..`, nothing outside BepInEx); nothing runs until BepInEx loads it at the next start.
- Settings and all texts in the 11 languages, like everything else.

## Version plan [DECISION, Umut may change it]
0.14 ships first (achievements, the chime, Clear view, macOS). The mod manager would be **0.15** if the reviewer
agrees; the Dark Mirror page moves to 0.16 and Rewards to 0.17.

## The question for the reviewer (Umut posts it in his Thunderstore Discord thread)
> Hi! A question before I build something: for a later SkulInsight QoL version I'd like to add an optional in-game
> mod browser. It lists the installed BepInEx mods and the Skul packages from Thunderstore's API, and on a click it
> downloads and installs a package (and its dependencies) into BepInEx/plugins from Thunderstore's own download URLs,
> the same layout r2modman uses, then restarts the game through Steam. It would be off until the player turns it on,
> nothing is downloaded without a click, and when the game runs from r2modman/Gale it would only show the list (or
> warn). Would a package with this feature be allowed on Thunderstore? Anything you'd want it to do or avoid (for
> example only showing mods, or a separate package)?

## 0.14.1: Thunderstore's rules (2026-10-25)
Thunderstore's moderator Robyn: "We rely on mod managers to do updating, plugins updating themselves or other packages
is not permitted" (0.14.0 was deactivated). Since 0.14.1 the Thunderstore build (`-p:IsThunderstoreBuild=true`,
define THUNDERSTORE, release/package.ps1) leaves out the self-update, installing / updating / removing / switching
mods and the restart: the window lists Thunderstore's Skul mods and yours, with details and a button to each mod's
Thunderstore page. package.ps1 refuses a DLL that still holds the updater or installer. Debug builds keep everything.
