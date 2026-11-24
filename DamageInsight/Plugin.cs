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
    /// <summary>The plugin's object, for coroutines of static code (the rewards' summons).</summary>
    internal static Plugin Instance;

    internal static ConfigEntry<string> Language;
    internal static ConfigEntry<bool> WhatsNewEnabled, UpdateNoticeEnabled;
    // The report form and the co-op run logs (docs/coop/REPORTS.md).
    internal static ConfigEntry<bool> ReportsToServer, ReportLogs;
    internal static ConfigEntry<string> ReportContact;
    internal static ConfigEntry<DamageInsight.Crash.RunLogsChoice> CoopSendRunLogs;
    internal static ConfigEntry<bool> MenuSoundsEnabled;
    internal static ConfigEntry<bool> CrashReports;
    internal static ConfigEntry<bool> PerformanceLog, PerformanceOverlay;
    internal static ConfigEntry<bool> RoomRestartEnabled, ShowAllSettings;
    internal static ConfigEntry<KeyboardShortcut> RoomRestartKey;
    internal static ConfigEntry<bool> RunHistory;
    internal static ConfigEntry<int> RunHistoryKeep;
    internal static ConfigEntry<bool> DailyEnabled;
    internal static ConfigEntry<string> DailyServer, DailyName;
    internal static ConfigEntry<bool> DailyWinnerNames;
    internal static ConfigEntry<bool> CoopEnabled;
    internal static ConfigEntry<float> CoopReviveSound;
    internal static ConfigEntry<float> CoopTeammateOpacity;
    internal static ConfigEntry<bool> CoopMatchSameLanguage;
    internal static ConfigEntry<bool> CoopTeammateOutline;
    internal static ConfigEntry<bool> CoopTeammateScreenEffects;
    internal static ConfigEntry<bool> CoopEnemyShotOutline, CoopOwnShotOutline;
    internal static ConfigEntry<float> CoopEnemyHealth;
    internal static ConfigEntry<float> CoopDoorVoteSeconds;
    internal static ConfigEntry<float> CoopRewardWaitSeconds;
    internal static ConfigEntry<string> CoopBalanceText;
    internal static ConfigEntry<float> CoopReviveHoldSeconds, CoopReviveHealth, CoopRoomClearHealth;
    internal static ConfigEntry<int> CoopRevivesPerRoom;
    internal static ConfigEntry<float> CoopProblemSeconds, CoopDropSeconds, CoopRejoinSeconds, CoopRetrySeconds;
    internal static ConfigEntry<bool> CoopConnectionBox;
    internal static ConfigEntry<bool> CoopDetailedLog;
    internal static ConfigEntry<bool> CoopMenuButton, CoopFriendsCanJoin;
    internal static ConfigEntry<int> CoopMaxPlayers;
    internal static ConfigEntry<KeyboardShortcut> CoopWindowKey;
    internal static ConfigEntry<bool> CoopMatchmaking, CoopCanHost;
    internal static ConfigEntry<int> CoopMaxPing;
    internal static ConfigEntry<string> CoopMatchMode;
    internal static ConfigEntry<bool> DamageNumbersTeammates;
    internal static ConfigEntry<KeyboardShortcut> CoopUnstuckKey;
    internal static ConfigEntry<bool> CoopAutoUnstuck;
    internal static ConfigEntry<string> LastVersionSeen;

    internal static ConfigEntry<bool> DamageSourceTags;
    internal static ConfigEntry<bool> DamageSourceIcons;
    internal static ConfigEntry<float> DamageNumberHoldTime;
    internal static ConfigEntry<float> DamageNumberFadeTime;
    internal static ConfigEntry<bool> BossHealthNumbers;
    internal static ConfigEntry<bool> DescriptionNumbers;
    internal static ConfigEntry<bool> DescriptionsHoldToShow;
    internal static ConfigEntry<bool> DescriptionsCompact;
    internal static ConfigEntry<KeyboardShortcut> DescriptionsHoldKey;
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
    internal static ConfigEntry<bool> CodexEnabled;
    internal static ConfigEntry<KeyboardShortcut> CodexKey;
    internal static ConfigEntry<bool> CodexRecordFights;
    internal static ConfigEntry<bool> CodexMoveHints;
    internal static ConfigEntry<bool> CodexProjectiles;
    internal static ConfigEntry<bool> CodexCleanFilms;
    internal static ConfigEntry<bool> CodexCompactFilms;
    internal static ConfigEntry<bool> CodexOpenOnYou;
    internal static ConfigEntry<bool> AchievementNotice;
    internal static ConfigEntry<bool> AchievementVeil;
    internal static ConfigEntry<float> AchievementSound;
    internal static ConfigEntry<bool> ClearViewEnabled, ClearViewOverRun, ClearViewWhenBusy, ClearViewProjectiles;
    internal static ConfigEntry<float> ClearViewLowest;
    internal static ConfigEntry<bool> ModManagerEnabled;
    internal static ConfigEntry<bool> AutoUpdate;
    internal static ConfigEntry<string> HealOrbLookSetting;
    internal static ConfigEntry<bool> HealOrbReminder;
    internal static ConfigEntry<bool> RewardsEnabled, RewardsAchievements, RewardsCoins, RewardsQuests, RewardsActivations;
    internal static ConfigEntry<int> RewardsSummonsPerRun, RewardsCoinsPerSummon, RewardsActivationsPerRun, RewardsQuestDarkQuartz, RewardsQuestGold, RewardsAdventurerKills;
    internal static ConfigEntry<float> RewardsQuestRuns;
    internal static ConfigEntry<int> SaveSlotSelected;
    internal static ConfigEntry<int> SaveSlotCount;
    internal static ConfigEntry<bool> CodexMoveCounter;   // bound in developer builds only (null otherwise)
    internal static ConfigEntry<bool> CodexRunLog;        // bound in developer builds only (null otherwise)
    internal static ConfigEntry<bool> PreferNewMoves;     // bound in developer builds only (null otherwise)
#if DEV
    internal static ConfigEntry<bool> DumpIcons;
    internal static ConfigEntry<bool> ScanGear;
    internal static ConfigEntry<bool> ScanLevels;
    internal static ConfigEntry<bool> DarkEliteDiagnostics;
    internal static ConfigEntry<bool> DevToolsEnabled;
    internal static ConfigEntry<KeyboardShortcut> DevRoomKey;
    internal static ConfigEntry<bool> CodexDevReveal;
    internal static ConfigEntry<bool> QaCaptureOnce;
    internal static ConfigEntry<bool> CodexUnlockAll;
    internal static ConfigEntry<KeyboardShortcut> CrashTestKey;

    private bool _iconDumpRunning;
    private bool _gearScanRunning;
    private bool _levelScanRunning;
#endif

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        // Co-op test copies (--skulinsight-instance 2-4): own data folder and save, before anything reads either.
        DamageInsight.Coop.Testing.CoopTesting.Start(Config);

        // Written to BepInEx\config\<plugin GUID>.cfg on first launch.
        Language = Config.Bind("General", "Language", DamageInsight.Lang.LanguageWatcher.Auto, new ConfigDescription(
            "Language of the mod's texts. auto = the game's language (changes with it); or one of: " +
            string.Join(", ", System.Linq.Enumerable.Select(DamageInsight.Lang.Loc.Languages, l => l.code)) + ".",
            new AcceptableValueList<string>(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(
                new[] { DamageInsight.Lang.LanguageWatcher.Auto }, System.Linq.Enumerable.Select(DamageInsight.Lang.Loc.Languages, l => l.code))))));
        DamageInsight.Lang.Loc.OverrideFolder = ModData.SharedFolder("Lang");
        WhatsNewEnabled = Config.Bind("General", "WhatsNew", true,
            "After an update, show once (in the castle) what's new in this version.");
        // Umut 2026-11-23: "a notification at the beginning where the changelog shows ... in all languages", also after
        // the What's new window was closed.
        UpdateNoticeEnabled = Config.Bind("General", "UpdateNotice", true,
            "At every start, a note in the castle when a newer SkulInsight QoL is out. Asks Thunderstore for the newest version number once per start.");
        // Umut 2026-11-24 (REPORTS.md): a form for bugs and ideas that the server turns into tickets and GitHub issues.
        ReportsToServer = Config.Bind("General", "ReportsToServer", true,
            "The \"Report a bug or idea\" window sends your report to the mod's server (skul.umut-aslan.dev): its title and text become a public GitHub issue, attached logs only reach the developer. Off: the window saves the logs and opens GitHub's page instead (needs a GitHub account).");
        ReportLogs = Config.Bind("General", "ReportLogs", true,
            "A bug report starts with its logs attached (this game's logs, settings and co-op logs; Steam ids and your account name hidden). Ideas start without logs.");
        ReportContact = Config.Bind("General", "ReportContact", "",
            "Kept from the report window: how the developer may reach you about a report (e.g. a Discord name). Never public.");
        MenuSoundsEnabled = Config.Bind("General", "MenuSounds", true,
            "The mod's buttons play the game's menu sounds when the mouse moves onto them or clicks them.");
        RunHistory = Config.Bind("General", "RunHistory", true,
            "Keep every run in the Codex: how it ended, the end screen's numbers, the build, the damage of each piece of gear, " +
            "the maps and a timeline of skulls, items and inscriptions. Per save slot, in BepInEx\\DamageInsight\\Codex\\Runs.");
        RunHistoryKeep = Config.Bind("General", "RunHistoryKeep", 100,
            "How many of the newest runs keep all their details (timeline, damage, picture). Older runs without a star keep " +
            "only their summary (20-1000).");
        DailyEnabled = Config.Bind("Daily", "Enabled", false,
            "Daily runs (online): everyone plays the same seed each day, one try, daily and monthly boards. Connects to the " +
            "daily run server and sends a random player id made by the mod, the name you enter and your daily run (its numbers, " +
            "build, damage and timeline). Off " +
            "until you turn it on (Codex, Daily).");
        DailyName = Config.Bind("Daily", "Name", "",
            "Your name for the daily boards (up to 12 characters). The name box after a daily run starts with it. Type it here " +
            "(e.g. in the mod manager's config editor) if typing in the game freezes it: some input methods (Pinyin, IME) " +
            "freeze full-screen games.");
        DailyWinnerNames = Config.Bind("Daily", "WinnerNames", true,
            "The adventurers (the hero parties in runs) are named after yesterday's daily run winners. Needs daily runs on.");
        DailyServer = Config.Bind("Daily", "Server", "https://skul.umut-aslan.dev",
            "The daily run server's address.");
        // Read as plain text by the preloader (DamageInsight.Preloader, CoopGate) before the game loads: it only rewrites the
        // game's player reads when this is true. Hidden on the settings page until co-op is playable (phase 1).
        CoopEnabled = Config.Bind("Coop", "Enabled", false,
            "Play a run together over the internet: with Steam friends (invites) or with other players through matchmaking. Needs a restart of the game. Off: the game runs as it does without co-op.");
        // Umut 2026-11-21: the teammates' numbers cluttered the screen. A new key (off), so testers' configs with the old
        // "Teammates = true" don't keep them on.
        DamageNumbersTeammates = Config.Bind("Damage Numbers", "TeammatesOnScreen", false,
            "Co-op: your teammates' hits show damage numbers on your screen too, in their colour. Off: fewer numbers on screen.");
        CoopUnstuckKey = Config.Bind("Coop", "UnstuckKey", new KeyboardShortcut(UnityEngine.KeyCode.F6),
            "Co-op: puts you where the nearest teammate stands in this room (once every 10 seconds), when you are stuck or far away.");
        CoopAutoUnstuck = Config.Bind("Coop", "AutoUnstuck", true,
            "Co-op: when you are stuck inside a wall and the game can't free you, you are put next to the nearest teammate by yourself (like the Go to teammate key).");
        CoopConnectionBox = Config.Bind("Coop", "ConnectionBox", true,
            "Co-op: a small box at the top left, below the clock, with each teammate's ping (green, amber, red) and a warning when nothing arrives from them.");
        // On by default during the co-op open beta (Umut 2026-11-21 13:10: the beta's logs must be able to reconstruct a run).
        // Umut 2026-11-24 (REPORTS.md): both players' logs after every co-op run, asked once.
        CoopSendRunLogs = Config.Bind("Coop", "SendRunLogs", DamageInsight.Crash.RunLogsChoice.Ask,
            "Co-op: after each co-op run, send that run's logs to the developer (skul.umut-aslan.dev; never public, Steam ids and your account name hidden), so co-op bugs get found without a report. Ask: a window asks after your next co-op run.");
        CoopDetailedLog = Config.Bind("Coop", "DetailedLog", true,
            "Co-op: writes everything that happens in a co-op game to BepInEx\\DamageInsight\\CoopTrace (the newest 10 games, 50 MB each at most). On by default during the co-op beta; the Co-op window's \"Report a bug or idea\" packs these files with the other logs.");
        CoopMenuButton = Config.Bind("Coop", "MenuButton", true,
            "Co-op: a Co-op button in the pause menu (below Mods) that opens the Co-op window: host, invite friends, leave, the players.");
        CoopWindowKey = Config.Bind("Coop", "WindowKey", new KeyboardShortcut(UnityEngine.KeyCode.F3),
            "Co-op: opens and closes the Co-op window.");
        CoopFriendsCanJoin = Config.Bind("Coop", "FriendsCanJoin", true,
            "Co-op, when you host: your Steam friends can join from their friends list (Join game). Off: only friends you invite.");
        CoopMaxPlayers = Config.Bind("Coop", "MaxPlayers", 4, new ConfigDescription(
            "Co-op, when you host with Steam friends: how many players your game takes, you included. 3 and 4 are experimental. Matchmaking always pairs two players.",
            new AcceptableValueRange<int>(2, 4)));
        CoopMatchmaking = Config.Bind("Coop", "Matchmaking", false,
            "Co-op: find a partner through the daily run server (Find a partner in the Co-op window). Sends the daily run's random " +
            "player id, your Steam id and name, your SkulInsight version and mod list, what you want to play and your Steam ping " +
            "location; the server keeps them in memory only while you search. Off until you turn it on.");
        CoopMaxPing = Config.Bind("Coop", "MaxPing", 200, new ConfigDescription(
            "Co-op matchmaking: the highest ping (ms) a partner may have.",
            new AcceptableValueRange<int>(50, 1000)));
        CoopMatchSameLanguage = Config.Bind("Coop", "MatchSameLanguage", false,
            "Co-op matchmaking: for the first minute of a search, only partners who play SkulInsight in your language. Off: anyone (finds a partner sooner).");
        CoopCanHost = Config.Bind("Coop", "CanHost", true,
            "Co-op matchmaking: your game may be the host (the world runs on the host's game).");
        CoopMatchMode = Config.Bind("Coop", "MatchMode", "any", new ConfigDescription(
            "Co-op matchmaking: what you want to play: normal, darkmirror or any.",
            new AcceptableValueList<string>("normal", "darkmirror", "any")));
        CoopReviveSound =Config.Bind("Coop", "ReviveSound", 0.6f, new ConfigDescription(
            "Co-op: how loud the heartbeat while a teammate is revived and the sound of the revive are (0 = no sound). The game's master and effects volume apply too.",
            new AcceptableValueRange<float>(0f, 1f)));
        // Umut + UBDuck 2026-11-16 (COOP_LOG 61 (5)): with the same skull both players looked alike.
        CoopTeammateOpacity = Config.Bind("Coop", "TeammateOpacity", 0.75f, new ConfigDescription(
            "Co-op: how solid your teammates look (100% = like you, lower = see-through), so you tell them from yourself at a glance.",
            new AcceptableValueRange<float>(0.2f, 1f)));
        CoopTeammateOutline = Config.Bind("Coop", "TeammateOutline", true,
            "Co-op: your teammates have an outline in their colour (the colour of their name and damage numbers).");
        // Umut 2026-11-21: a teammate's camera shakes, flashes and zooms on your screen were clutter.
        // Umut 2026-11-21: whose shot is it (Coop/ShotOutlines).
        // Umut 2026-11-21: also for playing alone (a YouTuber, Haxa, asked for it).
        CoopEnemyShotOutline = Config.Bind("Outlines", "EnemyShots", true, "Enemy projectiles that can hit you get a thin red outline that pulses gently, so you see them in a busy fight.");
        CoopOwnShotOutline = Config.Bind("Outlines", "OwnShots", true, "Co-op: your own projectiles get a thin green outline, so you tell them from your teammates' and the enemies'.");
        CoopTeammateScreenEffects = Config.Bind("Coop", "TeammateScreenEffects", false,
            "Co-op: your teammates' moves and quintessences may shake your camera, flash or darken your screen, zoom and make your controller rumble. Off: only your own do.");
        // Umut 2026-11-21: "1.6x for 2 players felt too weak"; a slider 1.5-3x, default 2x, live on both games.
        // Umut 2026-11-23: up to 4.5x (the 0.17.0 range is kept in docs/coop/BALANCE_BACKUP.md).
        CoopEnemyHealth = Config.Bind("Coop", "EnemyHealth", 2f, new ConfigDescription(
            "Co-op, when you host: how much health enemies have with two players (2x = twice their own). Elites and bosses get a little more, three or four players more again. A change counts at once, for everyone and for enemies already there.",
            new AcceptableValueRange<float>(DamageInsight.Coop.Rules.BalanceSettings.MinTwoPlayers, DamageInsight.Coop.Rules.BalanceSettings.MaxTwoPlayers)));
        CoopDoorVoteSeconds = Config.Bind("Coop", "DoorVoteSeconds", 10f, new ConfigDescription(
            "Co-op, when you host: how long the others have to pick a door after the first player picked one. Then the picks made so far decide.",
            new AcceptableValueRange<float>(3f, 60f)));
        CoopRewardWaitSeconds = Config.Bind("Coop", "RewardWaitSeconds", 60f, new ConfigDescription(
            "Co-op, when you host: how long the door vote waits for everyone to take their room reward. Picks made meanwhile count when the wait ends.",
            new AcceptableValueRange<float>(0f, 180f)));
        CoopBalanceText = Config.Bind("Coop", "Balance", new DamageInsight.Coop.Rules.BalanceSettings().ToText(),
            "Co-op, when you host: enemy health multipliers by party size (rows 2, 3, 4 players; normal / elite / boss). " +
            "Enemy damage stays as it is.");
        // Umut 2026-11-14: "hold time: 3s, revives per room 1" (the rules' defaults).
        CoopReviveHoldSeconds = Config.Bind("Coop", "ReviveHoldSeconds", 3f, new ConfigDescription(
            "Co-op, when you host: how long a teammate holds the interact key to revive a downed player.",
            new AcceptableValueRange<float>(0.5f, 10f)));
        CoopRevivesPerRoom = Config.Bind("Coop", "RevivesPerRoom", 1, new ConfigDescription(
            "Co-op, when you host: how often each player can be revived in one room. Who stays down comes back when the room is cleared.",
            new AcceptableValueRange<int>(0, 10)));
        CoopReviveHealth = Config.Bind("Coop", "ReviveHealth", 0.3f, new ConfigDescription(
            "Co-op, when you host: the health a revived player stands up with.",
            new AcceptableValueRange<float>(0.05f, 1f)));
        CoopRoomClearHealth = Config.Bind("Coop", "RoomClearHealth", 0.1f, new ConfigDescription(
            "Co-op, when you host: the health a player who stayed down comes back with when the room is cleared.",
            new AcceptableValueRange<float>(0.05f, 1f)));
        // Package DC (COOP_PLAN §5.7: no data for 3 s = a banner, 20 s = the player drops and may rejoin; §4.8: 3 minutes).
        CoopProblemSeconds = Config.Bind("Coop", "ProblemSeconds", 3f, new ConfigDescription(
            "Co-op: after this many seconds without data from a teammate, every game shows a connection problem.",
            new AcceptableValueRange<float>(1f, 30f)));
        CoopDropSeconds = Config.Bind("Coop", "DropSeconds", 20f, new ConfigDescription(
            "Co-op: after this many seconds without data, a teammate drops out of the party (votes and rewards no longer wait for them). They may come back.",
            new AcceptableValueRange<float>(5f, 120f)));
        CoopRejoinSeconds = Config.Bind("Coop", "RejoinSeconds", 120f, new ConfigDescription(
            "Co-op: how long a player who lost the connection may come back to the run. Their game tries to join again meanwhile; after that each game plays on alone.",
            new AcceptableValueRange<float>(0f, 600f)));
        CoopRetrySeconds = Config.Bind("Coop", "RetrySeconds", 3f, new ConfigDescription(
            "Co-op: after the connection to the host is lost, the next try to join again starts this many seconds after the last one failed.",
            new AcceptableValueRange<float>(1f, 30f)));
        CrashReports = Config.Bind("General", "CrashReports", true,
            "Keep a report of every game session in BepInEx\\DamageInsight\\CrashReports (the newest 20): errors with what happened " +
            "before them and whose code they were in (the game's, SkulInsight's or another mod's), a stuck black screen, a session " +
            "that ended unexpectedly. Says so on screen. Send the report with a bug report.");
        ShowAllSettings = Config.Bind(DamageInsight.UI.SettingsCatalog.AllSettings, "Show", false,
            "The settings page shows every setting (on) or only the ones most players change (off).");
        RoomRestartEnabled = Config.Bind("Restart Room", "Enabled", true,
            "Restart room: press the key below (F5) twice within 3 seconds to go back to the moment you entered the room, as " +
            "when you close the game and continue. In a daily run it ends the run (like leaving the game).");
        RoomRestartKey = Config.Bind("Restart Room", "Key", new KeyboardShortcut(UnityEngine.KeyCode.F5),
            "Press twice within 3 seconds to restart the room you are in.");
        PerformanceLog = Config.Bind("General", "PerformanceLog", true,
            "Write into the session report how smooth the game runs: frames per second and stutters each minute, how much of " +
            "each frame SkulInsight's own code takes (part by part), and what was going on if the game stands still for a few " +
            "seconds. Costs next to nothing.");
        PerformanceOverlay = Config.Bind("General", "PerformanceOverlay", false,
            "A small line in the top left corner: frames per second and how many milliseconds of each frame SkulInsight takes.");
        LastVersionSeen = Config.Bind("General", "LastVersionSeen", "",
            "The version whose 'What's new' was last shown (kept by the mod).");
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
        DescriptionsCompact = Config.Bind("Descriptions", "Compact", true,
            "Compact numbers in the game's description boxes: one line per attack with its totals. Off: every hit with its formula (base x hit % x attack %). The Codex always shows the formula.");
        DescriptionsHoldToShow = Config.Bind("Descriptions", "HoldToShow", true,
            "The numbers in the game's description boxes show only while you hold HoldKey (the boxes stay short, like the game's own 'hold for details'). Off: always shown.");
        DescriptionsHoldKey = Config.Bind("Descriptions", "HoldKey", new KeyboardShortcut(UnityEngine.KeyCode.LeftShift),
            "Key to hold for the numbers in the game's description boxes (inventory, items on the ground, shops, swap menu). Either Shift works.");
        PickupPreview = Config.Bind("Descriptions", "PreviewPickup", true,
            "Items on the ground, in shops and in the swap menu show their numbers as if you had picked them up (their stats and inscription steps included).");
        CombatLogKey = Config.Bind("Combat Log", "ToggleKey", new KeyboardShortcut(UnityEngine.KeyCode.L),
            "Key that opens and closes the combat log.");
        CombatLogUseDialogueBackground = Config.Bind("Combat Log", "UseDialogueBackground", true,
            "Use the NPC dialogue box artwork as the window background (false = plain dark panel).");
        CombatLogOpacity = Config.Bind("Combat Log", "BackgroundOpacity", 0.85f,
            "Opacity of the dialogue background, 0 = invisible, 1 = solid.");
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
            "Show the remaining seconds on the skill, swap, quintessence, item and ability icons (83s, 2m+, 10m). Cooldown speed bonuses are included.");
        CooldownTickerOpacity = Config.Bind("Cooldown Ticker", "Opacity", 0.55f,
            "Opacity of the numbers (0-1).");
#if !THUNDERSTORE
        AutoUpdate = Config.Bind("General", "AutoUpdate", true,
            "At every start, look on Thunderstore for a newer SkulInsight QoL and install it; it runs from the next start. In an " +
            "r2modman profile its mod list gets the new version too. The old version is kept in BepInEx/DamageInsight/Mods/Backup.");
#endif
        ModManagerEnabled = Config.Bind("Mods", "Enabled", true,
#if THUNDERSTORE
            "A Mods button in the pause menu: Thunderstore's Skul mods and yours side by side, with search, details and their " +
            "Thunderstore pages. It only goes online (Thunderstore) when you open it.");
#else
            "A Mods button in the pause menu: your BepInEx mods (turn on / off, remove) and Thunderstore's Skul mods to install " +
            "or update. It only goes online (Thunderstore) when you open it; changes need a restart of the game.");
#endif
        HealOrbLookSetting = Config.Bind("Heal Orbs", "Look", "Potion", new ConfigDescription(
            "How the healing orbs enemies drop look: Game (the game's purple orb), Potion (the game's Portable Potion, a bit " +
            "bigger, with a green glow) or Plus (a big green +).", new AcceptableValueList<string>("Game", "Potion", "Plus")));
        HealOrbReminder = Config.Bind("Heal Orbs", "Reminder", true,
            "When a map is cleared while you are hurt and healing orbs are left on it, a short text above you says so (once per map).");
        RewardsEnabled = Config.Bind("Rewards", "Enabled", true,
            "Rewards for your achievements and the bestiary (small lasting bonuses, coins, quests). They change the game's balance " +
            "a little; never in daily runs. Off: the game as it is; everything earned is kept and comes back when turned on.");
        RewardsAchievements = Config.Bind("Rewards", "AchievementRewards", true,
            "Each achievement of this save slot gives a small lasting bonus (more damage to a boss you beat, attack, speed, gold, ...).");
        RewardsCoins = Config.Bind("Rewards", "Coins", true,
            "Breaking a legendary item or skull gives its coin, every 5th win over an adventurer that adventurer's coin, a run " +
            "ended with Omen items an Omen coin: in a later run coins summon that gear (Codex, You, Rewards; Coins per summon).");
        RewardsQuests = Config.Bind("Rewards", "Quests", true,
            "Every normal enemy has an endless quest: every few runs' worth of kills pays some dark quartz and gold at once.");
        RewardsActivations = Config.Bind("Rewards", "InscriptionActivations", true,
            "Completing an inscription in a run (its last step) earns an activation when the run ends: in a later run it makes " +
            "an inscription you have count one more until that run ends (each one once per run), like the Dark Mirror's banners.");
        RewardsSummonsPerRun = Config.Bind("Rewards", "SummonsPerRun", 1, new ConfigDescription(
            "How many summons of each kind (legendary skull, legendary item, adventurer, Omen) you can make per run (0-3).",
            new AcceptableValueRange<int>(0, 3)));
        // Umut 2026-10-26 after one Hope Slasher coin: "How many are needed to summon? Hopefully not just one."
        RewardsCoinsPerSummon = Config.Bind("Rewards", "CoinsPerSummon", 3, new ConfigDescription(
            "Coins a summon takes: of that legendary skull or item, or Omen coins for an Omen item (1-10). An adventurer's " +
            "coin is enough on its own (it already takes several wins).", new AcceptableValueRange<int>(1, 10)));
        RewardsActivationsPerRun = Config.Bind("Rewards", "ActivationsPerRun", 2, new ConfigDescription(
            "How many inscription activations you can use per run (0-3).", new AcceptableValueRange<int>(0, 3)));
        RewardsQuestRuns = Config.Bind("Rewards", "QuestRuns", 2f, new ConfigDescription(
            "How many runs' worth of an enemy one quest step takes (0.5-10).", new AcceptableValueRange<float>(0.5f, 10f)));
        RewardsQuestDarkQuartz = Config.Bind("Rewards", "QuestDarkQuartz", 25, new ConfigDescription(
            "Dark quartz per quest step (0-100).", new AcceptableValueRange<int>(0, 100)));
        RewardsQuestGold = Config.Bind("Rewards", "QuestGold", 50, new ConfigDescription(
            "Gold per quest step, only inside a run (0-300).", new AcceptableValueRange<int>(0, 300)));
        RewardsAdventurerKills = Config.Bind("Rewards", "AdventurerKills", 5, new ConfigDescription(
            "Wins over one adventurer for its coin (1-50).", new AcceptableValueRange<int>(1, 50)));
        ClearViewEnabled = Config.Bind("Clear View", "Enabled", true,
            "Make the effects of your skills, items and summons fainter, so enemy attacks stay easy to see with a strong build. " +
            "Enemy attacks and their warnings are never faded.");
        ClearViewLowest = Config.Bind("Clear View", "LowestOpacity", 0.4f, new ConfigDescription(
            "How faint your effects get at most (0.1-1).", new AcceptableValueRange<float>(0.1f, 1f)));
        ClearViewOverRun = Config.Bind("Clear View", "OverTheRun", true,
            "Fade over the run: full in chapter 1, the lowest opacity from chapter 5 on (Dark Mirror chapters the same).");
        ClearViewWhenBusy = Config.Bind("Clear View", "WhenBusy", true,
            "Fade when many of your effects are on screen at once (from 8; the lowest opacity at 40), in any chapter.");
        ClearViewProjectiles = Config.Bind("Clear View", "Projectiles", true,
            "Your projectiles and your summons' fade too.");
        CodexEnabled = Config.Bind("Codex", "Enabled", true,
            "Record your progress for the Codex (enemies met and killed, gear found) and capture enemy portraits.");
        CodexKey = Config.Bind("Codex", "ToggleKey", new KeyboardShortcut(UnityEngine.KeyCode.K),
            "Key that opens and closes the Codex book.");
        CodexMoveHints = Config.Bind("Codex", "ShowMoveHints", true,
            "Under a boss move in the Codex: when the boss uses it (HP range, distance, cooldown...), read from its AI. " +
            "Shown once you have beaten the boss.");
        CodexRecordFights = Config.Bind("Codex", "FilmBossAttacks", true,
            "Film each boss attack once (a small picture 10 times a second, effects included) to show it in the Codex.");
        CodexProjectiles = Config.Bind("Codex", "ProjectilePictures", true,
            "Add the projectile an enemy fires (arrow, bolt, ...) to its pictures in the Codex, the first time it fires one.");
        CodexCompactFilms = Config.Bind("Codex", "CompactFilms", true,
            "Save boss films as compact pictures (about a quarter of the size, nearly the same look). Off: lossless pictures, about 1 GB more once every boss is filmed.");
        CodexCleanFilms = Config.Bind("Codex", "CleanFilms", true,
            "Boss films show only the boss: damage numbers, the effects of your skills and items, your summons and status effects on the boss (poison, burn, freeze, ...) are left out.");
        CodexOpenOnYou = Config.Bind("Codex", "OpenOnYou", true,
            "During a run, the Codex opens on the You page (your skulls, items, inscriptions and stats). Off: it opens on the page you read last.");
        AchievementNotice = Config.Bind("Codex", "AchievementNotice", true,
            "When this save slot earns an achievement, a small box with it slides down from the top of the screen for a few seconds " +
            "(also for achievements your Steam account already has: each slot earns them on its own).");
        AchievementSound = Config.Bind("Codex", "AchievementSound", 0.5f, new ConfigDescription(
            "How loud the little chime of the achievement notice is (0 = no sound). The game's master and effects volume apply too.",
            new AcceptableValueRange<float>(0f, 1f)));
        AchievementVeil = Config.Bind("Codex", "VeilSecretAchievements", true,
            "Achievements that give the story away stay veiled in the Codex until you earn them, meet what they are about, or hold " +
            "the mouse button on them for 4 seconds.");
        AchievementVeil.SettingChanged += (_, _) => DamageInsight.Codex.CodexWindow.Instance?.Invalidate();
        DamageInsight.Achievements.AchievementStore.Folder = DamageInsight.Codex.CodexTracker.Folder;
        DamageInsight.Rewards.RewardStore.Folder = DamageInsight.Codex.CodexTracker.Folder;
        DamageInsight.Codex.CodexBalance.Bind(Config);
        SaveSlotSelected = Config.Bind("Save Slots", "Slot", 1,
            "The save slot that Play, Copy and Clear use. The panel on the right shows what it holds.");
        SaveSlotCount = Config.Bind("Save Slots", "Slots", 3, "How many save slots there are (3 to 5).");
        DamageInsight.SaveSlots.SlotStore.Folder = ModData.Folder("SaveSlots");
        DamageInsight.SaveSlots.SlotStore.Load();
        DamageInsight.SaveSlots.SlotSwitcher.ConnectSettingsPage();
#if DEV
        DumpIcons = Config.Bind("Developer", "DumpIconsOnce", false,
            "Save the game's icons as PNGs to BepInEx\\DamageInsight\\IconDump when entering a run. Turns itself off afterwards.");
        ScanGear = Config.Bind("Developer", "ScanGearOnce", false,
            "Save every skull/item/quintessence's data as JSON to BepInEx\\DamageInsight\\GearScan when entering a run. Turns itself off afterwards.");
        ScanLevels = Config.Bind("Developer", "ScanLevelsOnce", false,
            "Codex balancing: read every chapter's stages and every map's enemy waves (normal and Dark Mirror groups) to " +
            "Codex\\Debug\\levels.json when you are in the castle or a run. Turns itself off afterwards.");
        DarkEliteDiagnostics = Config.Bind("Developer", "DarkEliteDiagnostics", false,
            "Log how dark elites get their abilities ([DarkDiag] lines in LogOutput.log). For bug hunting; changes nothing in the game.");
        DevToolsEnabled = Config.Bind("Developer", "DevTools", true,
            "Unlock Skul's hidden developer menu (F2) and the test-map teleport (DevRoomKey). Cheats change your save (currencies, unlocks).");
        DevRoomKey = Config.Bind("Developer", "DevRoomKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
            "Teleport into the developers' test map (needs DevTools = true and a running game).");
        CodexDevReveal = Config.Bind("Developer", "CodexRevealAllMet", true,
            "Codex testing: everything you have met is fully revealed, and enemies are captured when first met instead of on a kill.");
        CodexMoveCounter = Config.Bind("Developer", "CodexMoveCounter", true,
            "Codex testing: under a boss's health bar, how many of its moves were seen and filmed, which are missing, " +
            "and ALL MOVES SEEN once complete.");
        CodexRunLog = Config.Bind("Developer", "CodexRunLog", true,
            "Codex balancing: per run, every map's enemies (planned, appeared, killed) and time, to Codex\\Debug\\runs.");
        PreferNewMoves = Config.Bind("Developer", "PreferNewMoves", false,
            "Codex testing: bosses do the moves the Codex hasn't seen or filmed yet, one after another. Tree bosses (Dark Skul, " +
            "First Dark Hero, King Alexander, adventurers, Yggdrasil, Pope, First Hero) are led to them: distance, HP, cooldown " +
            "and chance checks are passed, and they change form first when a move needs another form. Bosses run by their own " +
            "code (Leiana sisters, Awakened Leiana, Chimera) start a missing move of the same kind instead of a filmed one.");
        QaCaptureOnce = Config.Bind("Developer", "QaCaptureOnce", false,
            "QA: in the castle or a run, play every skull, item and quintessence through the game's screens in the test map (ground popup, " +
            "inventory, detail views) and save a picture and the text facts of each to BepInEx\\DamageInsight\\QA. Your inventory is emptied. Turns itself off.");
        CodexUnlockAll = Config.Bind("Developer", "CodexUnlockEverything", false,
            "Codex testing: every page fully unlocked, met or not (every entry, skill box, inscription step, boss move). " +
            "Only shows: your progress stays as it is.");
        CrashTestKey = Config.Bind("Developer", "CrashTestKey", new KeyboardShortcut(UnityEngine.KeyCode.F10),
            "Crash report test (in a run): the first press logs a test error from the mod's code and turns the screen black " +
            "(after 20 s the 'stuck' notice and report entry appear), the second press brings the screen back. With Ctrl held: the game " +
            "crashes hard on purpose (like a real crash; start it again to see the 'ended unexpectedly' notice).");
        void ApplyReveal()
        {
            DamageInsight.Codex.CodexTiers.UnlockAll = CodexUnlockAll.Value;
            DamageInsight.Codex.CodexTiers.RevealAllMet = CodexDevReveal.Value || CodexUnlockAll.Value;
            DamageInsight.Codex.CodexWindow.Instance?.Invalidate();
        }
        ApplyReveal();
        CodexDevReveal.SettingChanged += (_, _) => ApplyReveal();
        CodexUnlockAll.SettingChanged += (_, _) => ApplyReveal();
#endif

        // Crash reports start before the patches, so a patch that fails is in the report too.
        DamageInsight.Crash.CrashReporter.Begin(gameObject, Config);
        // Frame times, SkulInsight's share and the freeze watch (0.16): from the start, so a slow load is in the report too.
        gameObject.AddComponent<DamageInsight.Crash.PerfMonitor>();

        // Apply every [HarmonyPatch] class one by one, so a patch broken by a game update only
        // disables its own feature. Results go to the log as [SelfTest] lines.
        var patches = PatchRegistry.ApplyAll(new Harmony(MyPluginInfo.PLUGIN_GUID));
        SelfTest.ReportPatches(patches);
        DamageInsight.Crash.CrashReporter.Note($"patches applied: {System.Linq.Enumerable.Count(patches, r => r.Ok)}/{patches.Count}");
        DamageLog.Recorded += record => SelfTest.Observe(record);
        LogFile.Attach();
        DamageInsight.History.RunJournal.Attach();
        DamageInsight.Codex.CodexOrigins.Attach();
        gameObject.AddComponent<DamageInsight.Daily.DailyService>();
        DamageInsight.Coop.PlayerReadsBridge.Check(CoopEnabled.Value);
        DamageInsight.Coop.CoopRuntime.Begin(gameObject);
        DamageInsight.Coop.Testing.ReproRun.Begin(gameObject);

        gameObject.AddComponent<DamageInsight.UI.CombatLogWindow>();
        gameObject.AddComponent<DamageInsight.UI.MiniLog>();
        gameObject.AddComponent<DamageInsight.UI.ClearView>();
        gameObject.AddComponent<DamageInsight.Coop.ShotOutlines>(); // red outlines on enemy shots, also alone
        gameObject.AddComponent<DamageInsight.Mods.ModService>();
        gameObject.AddComponent<DamageInsight.UI.WhatsNew>();
        gameObject.AddComponent<DamageInsight.UI.UpdateNotice>();
        gameObject.AddComponent<DamageInsight.UI.CoopConflictNotice>(); // other co-op mods / missing co-op part (COOP_LOG 73)
        gameObject.AddComponent<DamageInsight.UI.ReportWindow>();
        gameObject.AddComponent<DamageInsight.Crash.RunLogs>();
        DamageInsight.Codex.CodexReset.RunIfRequested(); // a requested reset happens before anything reads the Codex
        DamageInsight.Codex.CodexMigrations.Run(); // before anything reads the saved moves and films
        gameObject.AddComponent<DamageInsight.Codex.CodexWindow>();
        gameObject.AddComponent<DamageInsight.Codex.FightRecorder>();
        UnityEngine.Application.quitting += () => DamageInsight.Codex.CodexTracker.SaveIfNeeded(force: true);
        UnityEngine.Application.quitting += () => Guard.Run("Rewards: save at quit", DamageInsight.Rewards.RewardStore.Flush);
#if DEV
        gameObject.AddComponent<DevTranslator>();
        gameObject.AddComponent<QaCapture>();
#endif

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded");
    }

    private void Update()
    {
        DamageInsight.Lang.LanguageWatcher.Tick();
        DamageInsight.Describe.ExtraInfo.Tick();
        Guard.Run("UI (event system)", DamageInsight.UI.ModEventSystem.Tick);
        var player = Singleton<Service>.Instance?.levelManager?.player;
        bool inRun = player != null;
        DamageInsight.Codex.CodexTracker.Tick(player);
        Guard.Run("Run history", () => DamageInsight.History.RunJournal.Tick(player));
        Guard.Run("Heal orbs", () => DamageInsight.History.HealOrbs.Tick(player));
        Guard.Run("Rewards", () => DamageInsight.Rewards.RewardApplier.Tick(player));
        Guard.Run("Rewards (bestiary)", () => DamageInsight.Rewards.RewardService.Tick(player));
        Guard.Run("Rewards (save)", DamageInsight.Rewards.RewardStore.Tick);
        Guard.Run("Daily run", DamageInsight.Daily.DailyRun.Tick);
        Guard.Run("Restart room (key)", DamageInsight.SaveSlots.RoomRestart.Tick);
        if (inRun)
            SelfTest.CheckRun();
#if DEV
        if (inRun && DevToolsEnabled.Value && DevRoomKey.Value.IsDown())
            DevTools.EnterTestMap();
        if (inRun && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            StartCoroutine(ProbeAtEndOfFrame());
        // F11: test rewards (coins, an Omen coin, an inscription activation) to try Summon and +1 without farming.
        if (inRun && DevToolsEnabled.Value && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11))
            Guard.Run("Dev: rewards", DamageInsight.Rewards.RewardService.DevGrant);
        // F9: a small healing orb at your feet (to look at the healing orb look and the reminder without farming).
        if (inRun && DevToolsEnabled.Value && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            Guard.Run("Dev: healing orb", () => Singleton<Service>.Instance.levelManager.DropPotion(
                GameResources.CommonResource.instance.smallPotion, player.transform.position + UnityEngine.Vector3.up));

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
        else if (ScanLevels.Value && !_levelScanRunning && inRun)
        {
            _levelScanRunning = true;
            ShowAbovePlayer("Level scan started - please wait");
            StartCoroutine(DamageInsight.Tools.LevelScan.Run(text => Log.LogInfo(text), result =>
            {
                Log.LogInfo($"Level scan finished: {result}");
                ShowAbovePlayer("Level scan finished!");
                ScanLevels.Value = false;
                _levelScanRunning = false;
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
    private static System.Collections.IEnumerator ProbeAtEndOfFrame()
    {
        yield return new UnityEngine.WaitForEndOfFrame();
        CaptureProbe.Run();
        ShowAbovePlayer("Capture probe done - close the game and tell Claude");
    }

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
