# Building SkulInsight QoL

The mod is a BepInEx 5 plugin (net472) for Skul: The Hero Slayer (Unity 2020.3.34, Mono). The game's DLLs are
referenced from your own Skul installation; they are not part of this repository and must never be committed.

## Requirements
- Skul (Steam) with [BepInExPack Skul](https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/)
  installed
- .NET SDK (any recent version)

## Build
```
dotnet build                  # Debug: includes developer tools, copies the DLL into <Skul>/BepInEx/plugins/DamageInsight/
dotnet build -c Release       # what gets released: no developer tools
```
- The Skul folder is found automatically in the default Steam locations. Otherwise pass
  `-p:SkulRoot="path\to\Skul"`.
- Add `-p:DeployToSkul=false` to build without copying into the game.
- Close the game before building: it locks the DLL.
- Private game members are accessible at compile time through
  [BepInEx.AssemblyPublicizer](https://github.com/BepInEx/BepInEx.AssemblyPublicizer).

## Package for Thunderstore
```
powershell -ExecutionPolicy Bypass -File release/package.ps1
```
Builds Release and writes `release/out/DocRun-SkulInsight_QoL-<version>.zip` with `manifest.json`, `icon.png`,
`README.md`, `CHANGELOG.md` and the DLL.

## Tests
```
dotnet test
```
Three layers, from fast to real:
1. **Unit tests** (xUnit) for plain logic:
   - log filters, text and calculations
   - the description formatter
   - the pickup preview math
   - the gear analyzer
   Some tests use serialized game data in `DamageInsight.Tests/Fixtures/`. That game data is not in the
   public repository, so these tests skip themselves. With a Debug build you can create it yourself: set
   `ScanGearOnce = true` in the config, enter a run, then copy files from `BepInEx/DamageInsight/GearScan/`
   into the Fixtures folder.
2. **Patch target tests:** every Harmony patch is checked against the real `Assembly-CSharp.dll` (method,
   parameters, private fields, no ambiguous overloads). Run these first after a game update.
3. **In-game self-test:** `[SelfTest] PASS/FAIL` lines in `BepInEx/LogOutput.log`, covering:
   - every patch applied
   - resources found
   - the first hit of each damage source

## Layout
- `DamageInsight/Patches/`: Harmony hooks. `PatchRegistry` applies each patch class separately, so one broken
  hook only disables its own feature.
- `DamageInsight/Describe/`: description numbers.
  - Gear is serialized at runtime into JSON (`Tools/ObjectGraphWriter`).
  - `GearAnalyzer` walks actions/operations and finds every hit.
  - Refiners add titles, triggers and steps.
  - `DescriptionFormatter` turns hits and the player's stats into text.
  - `PickupPreview` computes the stats you'd have after taking an item.
- `DamageInsight/Recording/`: damage log and per-hit calculation traces (`DamageTrace`), log files.
- `DamageInsight/UI/`: combat log window, mini log, tooltip, cooldown ticker.
- `DamageInsight/Tools/Dev/`: developer tools (Debug builds only):
  - Skul's hidden dev menu (F2) with English translation
  - test map (F7)
  - gear scan and icon dump
  - diagnostics

## Game code
To find classes and fields, decompile `Skul_Data/Managed/Assembly-CSharp.dll` locally (e.g. with ILSpy). Don't
publish decompiled code.
