using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DamageInsight.Patches;
using DamageInsight.Recording;
using DamageInsight.Tools;
using HarmonyLib;
using Services;
using Singletons;

namespace DamageInsight;

/// <summary>
/// SkulInsight QoL: real damage numbers, a combat log with per-hit calculations and build planning for
/// Skul: The Hero Slayer. Everything is switchable in the config. Developer tools (dev menu, test room,
/// gear scan, diagnostics) only exist in Debug builds (DEV).
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> DamageSourceTags;
    internal static ConfigEntry<bool> DamageSourceIcons;
    internal static ConfigEntry<float> DamageNumberHoldTime;
    internal static ConfigEntry<float> DamageNumberFadeTime;
    internal static ConfigEntry<bool> BossHealthNumbers;
    internal static ConfigEntry<bool> DescriptionNumbers;
    internal static ConfigEntry<KeyboardShortcut> CombatLogKey;
    internal static ConfigEntry<bool> CombatLogUseDialogueBackground;
    internal static ConfigEntry<float> CombatLogOpacity;
    internal static ConfigEntry<string> CombatLogWindowRect;
    internal static ConfigEntry<bool> MiniLogEnabled;
    internal static ConfigEntry<int> MiniLogLines;
    internal static ConfigEntry<float> MiniLogIdleSeconds;
    internal static ConfigEntry<bool> MiniLogPie;
    internal static ConfigEntry<float> MiniLogOffsetY;
    internal static ConfigEntry<bool> PickupPreview;
    internal static ConfigEntry<bool> CombatLogCalculations;
    internal static ConfigEntry<bool> CombatLogToFile;
    internal static ConfigEntry<bool> CooldownTicker;
    internal static ConfigEntry<float> CooldownTickerOpacity;
#if DEV
    internal static ConfigEntry<bool> DumpIcons;
    internal static ConfigEntry<bool> ScanGear;
    internal static ConfigEntry<bool> DarkEliteDiagnostics;
    internal static ConfigEntry<bool> DevToolsEnabled;
    internal static ConfigEntry<KeyboardShortcut> DevRoomKey;

    private bool _iconDumpRunning;
    private bool _gearScanRunning;
#endif

    private void Awake()
    {
        Log = Logger;

        // Written to BepInEx\config\<plugin GUID>.cfg on first launch.
        DamageSourceTags = Config.Bind("Damage Numbers", "ShowSourceTag", true,
            "Mark damage numbers with where the damage came from (icon or ATK, SKILL, ITEM, ...).");
        DamageSourceIcons = Config.Bind("Damage Numbers", "UseIcons", true,
            "Use icons instead of text tags where an icon is configured (see [Damage Icons]).");
        DamageInsight.UI.IconLibrary.Bind(Config);
        DamageNumberHoldTime = Config.Bind("Damage Numbers", "HoldSeconds", 0.8f,
            "How long a damage number stays fully visible after popping up. (Game default: about 0.15)");
        DamageNumberFadeTime = Config.Bind("Damage Numbers", "FadeSeconds", 0.7f,
            "How long a damage number takes to fade out.");
        BossHealthNumbers = Config.Bind("Boss Health Bar", "ShowNumbers", true,
            "Show current / maximum HP as text on boss, adventurer and dark enemy health bars.");
        DescriptionNumbers = Config.Bind("Descriptions", "ShowDamageNumbers", true,
            "Add real damage numbers (with your current stats) to skull, skill, swap, item, essence and inscription descriptions.");
        PickupPreview = Config.Bind("Descriptions", "PreviewPickup", true,
            "Items on the ground, in shops and in the swap menu show their numbers as if you had picked them up (their stats and inscription steps included).");
        CombatLogKey = Config.Bind("Combat Log", "ToggleKey", new KeyboardShortcut(UnityEngine.KeyCode.L),
            "Key that opens and closes the combat log.");
        CombatLogUseDialogueBackground = Config.Bind("Combat Log", "UseDialogueBackground", true,
            "Use the NPC dialogue box artwork as the window background (false = plain dark panel). Applies after a game restart.");
        CombatLogOpacity = Config.Bind("Combat Log", "BackgroundOpacity", 0.85f,
            "Opacity of the dialogue background, 0 = invisible, 1 = solid. Applies after a game restart.");
        CombatLogWindowRect = Config.Bind("Combat Log", "WindowRect", "",
            "Position and size of the log window (left,top,width,height at 1920x1080). Saved automatically; empty = default.");
        CombatLogCalculations = Config.Bind("Combat Log", "RecordCalculations", true,
            "Record how every hit's number came about (stats, items, inscriptions, dark abilities, crits, debuffs); hover a log line to see it.");
        CombatLogToFile = Config.Bind("Combat Log", "SaveToFile", true,
            "Also write every hit (with its calculation) to BepInEx\\DamageInsight\\CombatLogs, one file per game session (the newest 30 are kept).");
        MiniLogEnabled = Config.Bind("Mini Log", "Enabled", false,
            "Show a few log lines above the minimap. Can also be toggled in the combat log window.");
        MiniLogLines = Config.Bind("Mini Log", "Lines", 4, "Number of lines (1-8).");
        MiniLogIdleSeconds = Config.Bind("Mini Log", "HideAfterSeconds", 5f,
            "Hide the mini log when no new damage came in for this many seconds.");
        MiniLogPie = Config.Bind("Mini Log", "ShowPie", true, "Show a tiny pie chart at the right of the mini log.");
        MiniLogOffsetY = Config.Bind("Mini Log", "GapAboveMinimap", 40f,
            "Distance between the minimap and the mini log (reference pixels at 1920x1080).");
        CooldownTicker = Config.Bind("Cooldown Ticker", "Enabled", true,
            "Show the remaining seconds on the item/ability icons at the bottom of the screen (83s, 2m+, 10m).");
        CooldownTickerOpacity = Config.Bind("Cooldown Ticker", "Opacity", 0.55f,
            "Opacity of the numbers (0-1).");
#if DEV
        DumpIcons = Config.Bind("Developer", "DumpIconsOnce", false,
            "Save the game's icons as PNGs to BepInEx\\DamageInsight\\IconDump when entering a run. Turns itself off afterwards.");
        ScanGear = Config.Bind("Developer", "ScanGearOnce", false,
            "Save every skull/item/quintessence's data as JSON to BepInEx\\DamageInsight\\GearScan when entering a run. Turns itself off afterwards.");
        DarkEliteDiagnostics = Config.Bind("Developer", "DarkEliteDiagnostics", false,
            "Log how dark elites get their abilities ([DarkDiag] lines in LogOutput.log). For bug hunting; changes nothing in the game.");
        DevToolsEnabled = Config.Bind("Developer", "DevTools", true,
            "Unlock Skul's hidden developer menu (F2) and the test-map teleport (DevRoomKey). Cheats change your save (currencies, unlocks).");
        DevRoomKey = Config.Bind("Developer", "DevRoomKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
            "Teleport into the developers' test map (needs DevTools = true and a running game).");
#endif

        // Apply every [HarmonyPatch] class one by one, so a patch broken by a game update only
        // disables its own feature. Results go to the log as [SelfTest] lines.
        SelfTest.ReportPatches(PatchRegistry.ApplyAll(new Harmony(MyPluginInfo.PLUGIN_GUID)));
        DamageLog.Recorded += record => SelfTest.Observe(record);
        LogFile.Attach();

        gameObject.AddComponent<DamageInsight.UI.CombatLogWindow>();
        gameObject.AddComponent<DamageInsight.UI.MiniLog>();
#if DEV
        gameObject.AddComponent<DevTranslator>();
#endif

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }

    private void Update()
    {
        bool inRun = Singleton<Service>.Instance?.levelManager?.player != null;
        if (inRun)
            SelfTest.CheckRun();
#if DEV
        if (inRun && DevToolsEnabled.Value && DevRoomKey.Value.IsDown())
            DevTools.EnterTestMap();

        if (DumpIcons.Value && !_iconDumpRunning && inRun)
        {
            _iconDumpRunning = true;
            Log.LogInfo($"Dumping icons to {IconDump.OutputFolder} ...");
            StartCoroutine(IconDump.Run(count =>
            {
                Log.LogInfo($"Icon dump finished: {count} icons saved");
                DumpIcons.Value = false;
                _iconDumpRunning = false;
            }));
        }
        else if (ScanGear.Value && !_gearScanRunning && !_iconDumpRunning && inRun)
        {
            _gearScanRunning = true;
            Log.LogInfo($"Scanning gear to {GearScan.OutputFolder} ...");
            ShowAbovePlayer("Gear scan started - please wait");
            StartCoroutine(GearScan.Run(ShowAbovePlayer, result =>
            {
                Log.LogInfo($"Gear scan finished: {result}");
                ShowAbovePlayer("Gear scan finished!");
                ScanGear.Value = false;
                _gearScanRunning = false;
            }));
        }
#endif
    }

#if DEV
    /// <summary>Floating message above the player (like the game's buff texts).</summary>
    private static void ShowAbovePlayer(string text)
    {
        try
        {
            var player = Singleton<Service>.Instance?.levelManager?.player;
            if (player != null)
                Singleton<Service>.Instance.floatingTextSpawner.SpawnBuff(text, player.transform.position + UnityEngine.Vector3.up * 2f);
            Log.LogInfo(text);
        }
        catch (System.Exception)
        {
            // purely informational
        }
    }
#endif
}
