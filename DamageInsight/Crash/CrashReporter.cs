using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using DamageInsight.Lang;
using HarmonyLib;
using Scenes;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Crash;

/// <summary>
/// Crash reports (0.12.2; a colleague's game went black after a level and nobody could say whose fault it was).
/// Setting General/CrashReports (on). The game side of <see cref="SessionReport"/>:
/// - every Unity error and exception (with its stack trace), every BepInEx error line, every error SkulInsight's
///   Guard caught, each classified by <see cref="ErrorClassifier"/>;
/// - a timeline: scenes, chapters, stages, map changes (CrashReportPatches), fades, runs, the build per map;
/// - a stuck black screen (the game's fade fully black for <see cref="StuckSeconds"/> s, no loading screen, no
///   dialogue: the game's map change coroutine died) and a camera that stopped drawing: written as incidents with
///   the game's state and a verdict, and said on screen;
/// - at the next start, a session that never wrote its end is marked "ended unexpectedly" (Unity's log of that
///   session and a crash dump's error log added) and a notice says so.
/// </summary>
public sealed class CrashReporter : MonoBehaviour
{
    public const float StuckSeconds = 20f;
    public const float CameraSeconds = 10f;
    private const string NoticeStuck = "crash.stuck", NoticePrevious = "crash.previous";

    public static SessionReport Report
    {
        get => _report != null && (Plugin.CrashReports?.Value ?? false) ? _report : null; // switched off: nothing more is written
        private set => _report = value;
    }

    private static SessionReport _report;
    public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "DamageInsight", "CrashReports");

    private static ErrorClassifier _classifier;
    private static SessionReport.Previous _previous;
    private static string _previousPath;
    private static ConfigFile _config;
    [ThreadStatic] private static bool _inHandler;

    // Map change phases, watched without hooks (a hook of ours on the game's map change would put that game method
    // into every map change error's trace as "hooked by SkulInsight" and blame the mod for nothing).
    private DateTime _blackAt, _mapGoneAt, _mapNewAt, _visibleAt;
    private Level.Map _map;

    private static string _build;

    /// <summary>The screen is faded to black (a map change): a long frame then is loading, not a stutter.</summary>
    public static bool ScreenBlack { get; private set; }
    private string _scene, _place;
    private bool _languageNoted;
    private bool _hadPlayer, _playerDead, _black, _stuckReported, _cameraReported, _modsListed, _previousShown;
    private float _blackSince = -1f, _cameraOffSince = -1f, _nextHookRefresh, _nextPoll;
    private DateTime _blackSinceClock;
    private int _hookCount = -1;

    /// <summary>Called first in Plugin.Awake (before the patches, so their errors are in the report).</summary>
    public static void Begin(GameObject host, ConfigFile config)
    {
        _config = config;
        if (Plugin.CrashReports == null || !Plugin.CrashReports.Value)
            return;
        try
        {
            // The session before is looked at in the first frame, when the hooks and the other mods are known.
            _previousPath = SessionReport.NewestReport(Folder);
            _classifier = new ErrorClassifier(MyPluginInfo.PLUGIN_GUID);
            Report = new SessionReport(Folder, _classifier);
            Report.Open(Header());
            Application.logMessageReceivedThreaded += OnUnityLog;
            BepInEx.Logging.Logger.Listeners.Add(new BepInExErrors());
            Application.quitting += () =>
            {
                Report?.Event("the game closes");
                _report?.End(); // also when switched off meanwhile: the file gets its end
            };
            host.AddComponent<CrashReporter>();
            Plugin.Log.LogInfo($"Crash report: {Report.FilePath}");
        }
        catch (Exception e)
        {
            Report = null;
            Plugin.Log.LogWarning($"Crash reports are off for this session: {e.Message}");
        }
    }

    /// <summary>A timeline line from anywhere in the mod (null-safe: nothing when reports are off).</summary>
    public static void Note(string text)
    {
        try
        {
            Report?.Event(text);
        }
        catch (Exception)
        {
            // never let the report break the caller
        }
    }

    /// <summary>An error SkulInsight's Guard caught (the game went on without that feature).</summary>
    public static void Caught(string where, Exception e)
    {
        if (Report == null || e == null)
            return;
        try
        {
            Report.Error("Caught", $"{where}: {e.GetType().Name}: {e.Message}", e.StackTrace, Blame.Mod, where);
        }
        catch (Exception)
        {
            // never let the report break the caller
        }
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            return;
        if (_inHandler || Report == null)
            return;
        // BepInEx's own lines reach Unity's log as well; BepInExErrors has them with their source.
        if (condition != null && condition.StartsWith("[", StringComparison.Ordinal) && condition.IndexOf(':') is > 1 and < 20)
            return;
        // Unity's note at every start that it looks for the Steam library: not an error.
        if (condition != null && condition.StartsWith("Check for ", StringComparison.Ordinal) && condition.Contains("steam_api"))
            return;
        _inHandler = true;
        try
        {
            Report.Error(type.ToString(), condition, stackTrace);
        }
        catch (Exception)
        {
            // never log from here (it would come back to us)
        }
        finally
        {
            _inHandler = false;
        }
    }

    /// <summary>Error and fatal lines of every BepInEx log source (other mods, HarmonyX, ours).</summary>
    private sealed class BepInExErrors : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs e)
        {
            if ((e.Level & (LogLevel.Error | LogLevel.Fatal)) == 0 || _inHandler || Report == null)
                return;
            string source = e.Source?.SourceName ?? "";
            if (source == "Unity Log")
                return; // OnUnityLog has these with their stack traces
            _inHandler = true;
            try
            {
                string text = Convert.ToString(e.Data, CultureInfo.InvariantCulture) ?? "";
                int nl = text.IndexOf('\n');
                string first = nl >= 0 ? text.Substring(0, nl) : text, rest = nl >= 0 ? text.Substring(nl + 1) : "";
                Blame? blame = source == MyPluginInfo.PLUGIN_NAME ? Blame.Mod
                    : source is "BepInEx" or "HarmonyX" or "Preloader" or "" ? null : Blame.OtherMod;
                Report.Error("Logged", $"[{source}] {first}", rest, blame, blame == null ? null : source);
            }
            catch (Exception)
            {
                // never log from here
            }
            finally
            {
                _inHandler = false;
            }
        }

        public void Dispose()
        {
        }
    }

    // ------------------------------------------------------------------ every frame

    private void Update()
    {
        if (Report == null)
            return;
        float now = Time.unscaledTime;
        // Each watch on its own: one that fails (a game object not ready yet) doesn't skip the others.
        Patches.Guard.Run("Crash reports (tick)", () => Report.Tick());
#if DEV
        Patches.Guard.Run("Crash reports (test key)", TestKeys);
#endif
        if (now >= _nextHookRefresh)
        {
            _nextHookRefresh = now + 15f;
            Patches.Guard.Run("Crash reports (hooks)", RefreshHooks);
        }
        Patches.Guard.Run("Crash reports (screen)", () => WatchScreen(now));
        Patches.Guard.Run("Crash reports (map)", WatchMap);
        if (now >= _nextPoll)
        {
            _nextPoll = now + 0.25f;
            Patches.Guard.Run("Crash reports (poll)", Poll);
        }
        Patches.Guard.Run("Crash reports (previous session)", ShowPreviousOnce);
    }

    /// <summary>The game methods each Harmony owner patches (SkulInsight patches some later, e.g. boss AI), and the other mods.</summary>
    private void RefreshHooks()
    {
        var methods = Harmony.GetAllPatchedMethods().ToList();
        if (methods.Count != _hookCount)
        {
            _hookCount = methods.Count;
            var hooks = new List<KeyValuePair<string, IEnumerable<string>>>();
            foreach (var method in methods)
            {
                var info = Harmony.GetPatchInfo(method);
                if (method.DeclaringType == null || info == null)
                    continue;
                hooks.Add(new KeyValuePair<string, IEnumerable<string>>(
                    method.DeclaringType.FullName + "::" + method.Name, info.Owners.ToList()));
            }
            _classifier.SetHooks(hooks);
        }
        if (!_modsListed)
        {
            _modsListed = true;
            var mods = new List<(string ns, string mod)>();
            var gameNamespaces = new HashSet<string>(SafeTypes(typeof(Level.Chapter).Assembly).Select(t => t.Namespace ?? ""));
            var listed = new List<string>();
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                string name = info.Metadata?.Name ?? info.Metadata?.GUID ?? "?";
                listed.Add($"{name} {info.Metadata?.Version}");
                if (info.Metadata?.GUID == MyPluginInfo.PLUGIN_GUID || info.Instance == null)
                    continue;
                foreach (string ns in SafeTypes(info.Instance.GetType().Assembly).Select(t => t.Namespace).Where(n => !string.IsNullOrEmpty(n)).Distinct())
                    if (!gameNamespaces.Contains(ns))
                        mods.Add((ns, name));
            }
            _classifier.SetOtherMods(mods);
            Report.Event("loaded mods: " + string.Join(", ", listed));
            FinishPreviousSession();
        }
    }

    /// <summary>The report of the session before: finished if it ended unexpectedly (with the crash's verdict).</summary>
    private static void FinishPreviousSession()
    {
        if (_previousPath == null)
            return;
        _previous = SessionReport.FinishPrevious(_previousPath, DateTime.Now, _classifier, PreviousExtras);
        _previousPath = null;
        if (_previous != null)
            Report.Event(_previous.EndedUnexpectedly
                ? $"the previous session ended unexpectedly (report {Path.GetFileName(_previous.FilePath)}): {_previous.Verdict}"
                : $"previous session: ended normally{(_previous.StuckIncidents > 0 ? $", the screen stayed black {_previous.StuckIncidents} time(s)" : "")}");
    }

    private static IEnumerable<Type> SafeTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
        catch (Exception)
        {
            return Enumerable.Empty<Type>();
        }
    }

    /// <summary>Scenes, chapters, maps, the player and the build: a line whenever one changes.</summary>
    private void Poll()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            Report.Event($"scene: {scene}");
        }
        if (!_languageNoted && Scene<GameBase>.instance != null)
        {
            _languageNoted = true;
            Report.Event($"language: game {Data.GameData.Settings.language}, mod {Loc.Current}");
        }

        var levels = Singleton<Service>.Instance?.levelManager;
        var chapter = levels?.currentChapter;
        // Chapter and stage here; each map has its own line from WatchMap.
        string place = chapter == null ? "" : $"chapter {chapter.type}, stage {chapter.stageIndex}";
        if (place != _place && place.Length > 0)
        {
            _place = place;
            Report.Event(place);
        }

        var player = levels?.player;
        bool hasPlayer = player != null;
        if (hasPlayer != _hadPlayer)
        {
            _hadPlayer = hasPlayer;
            _playerDead = false;
            Report.Event(hasPlayer ? "player spawned" : "player gone (run over, castle or title)");
        }
        if (hasPlayer && player.health != null && player.health.dead != _playerDead)
        {
            _playerDead = player.health.dead;
            if (_playerDead)
                Report.Event("player died");
        }
    }

#if DEV
    private bool _testFreeze;

    /// <summary>Developer/CrashTestKey in a run; with Ctrl held, a real crash (Unity's crash handler, a crash dump).</summary>
    private void TestKeys()
    {
        if (Plugin.CrashTestKey == null || Singleton<Service>.Instance?.levelManager?.player == null)
            return;
        var key = Plugin.CrashTestKey.Value.MainKey;
        if (Input.GetKeyDown(key) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            Report.Event("developer test: a hard crash on purpose (Ctrl + CrashTestKey)");
            Report.Save();
            UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);
        }
        else if (Plugin.CrashTestKey.Value.IsDown())
        {
            ToggleTestFreeze();
        }
    }

    /// <summary>Developer/CrashTestKey: a test error from our code, then a black screen like a dead map change (press again: back).</summary>
    private void ToggleTestFreeze()
    {
        var fade = Singleton<Service>.Instance.fadeInOut;
        _testFreeze = !_testFreeze;
        if (_testFreeze)
        {
            Report.Event("developer test: a test error and a black screen (CrashTestKey)");
            try
            {
                throw new InvalidOperationException("Crash report test (developer key): not a real error"); // thrown: a stack trace with our frame
            }
            catch (InvalidOperationException e)
            {
                Debug.LogException(e);
            }
            fade.FadeOutImmediately();
        }
        else
        {
            Report.Event("developer test: screen back (CrashTestKey)");
            StartCoroutine(fade.CFadeIn());
        }
    }
#endif

    /// <summary>The game's map object: the old one is destroyed during a map change, then the new one appears.</summary>
    private void WatchMap()
    {
        var map = Singleton<Service>.Instance?.levelManager?.currentChapter?.map;
        // ReferenceEquals: Unity's == calls a destroyed map equal to null, and the "removed" step would be missed.
        if (ReferenceEquals(map, _map) || (map is null && _map is null))
            return;
        bool gone = map == null;
        _map = map;
        if (gone)
        {
            _mapGoneAt = DateTime.Now;
            Report.Event("map removed (a map change is under way)");
        }
        else
        {
            _mapNewAt = DateTime.Now;
            var chapter = Singleton<Service>.Instance.levelManager.currentChapter;
            Report.Event("map loaded: " + Place(chapter) + (chapter.map.darkEnemy ? " (dark enemies)" : ""));
            NoteBuild();
        }
    }

    /// <summary>"Chapter2, stage 1 (path 0, node 1), map 'name'".</summary>
    private static string Place(Level.Chapter chapter)
    {
        var stage = chapter.currentStage;
        string map = chapter.map != null ? chapter.map.name.Replace("(Clone)", "").Trim() : "(no map)";
        return $"{chapter.type}, stage {chapter.stageIndex}" + (stage != null ? $" (path {stage.pathIndex}, node {stage.nodeIndex})" : "") + $", map '{map}'";
    }

    /// <summary>The run's skulls, items and quintessence, once per change (called when a map has loaded).</summary>
    internal static void NoteBuild()
    {
        var inventory = Singleton<Service>.Instance?.levelManager?.player?.playerComponents?.inventory;
        if (inventory == null || Report == null)
            return;
        string Names(IEnumerable<UnityEngine.Object> gear) =>
            string.Join(", ", gear.Where(g => g != null).Select(g => g.name.Replace("(Clone)", "").Trim()));
        string build = $"build: skulls {Names(inventory.weapon.weapons)}; items {Names(inventory.item.items)}; quintessence {Names(inventory.quintessence.items)}";
        if (build == _build)
            return;
        _build = build;
        Report.Event(build);
    }

    // ------------------------------------------------------------------ stuck screen and camera

    private void WatchScreen(float now)
    {
        var fade = Singleton<Service>.Instance?.fadeInOut;
        bool black = false;
        if (fade != null && fade._fadeBackground != null)
            black = fade._fadeBackground.color.a >= 0.99f && fade._fadeBackground.gameObject.activeInHierarchy
                    && (fade._loadingScreen == null || !fade._loadingScreen.gameObject.activeInHierarchy);
        if (black && (Scene<GameBase>.instance == null || StoryOnScreen()))
            black = false; // only in the game (not the title), and story scenes talk on a black screen

        ScreenBlack = black;
        if (black != _black)
        {
            _black = black;
            if (black)
            {
                _blackSince = now;
                _blackSinceClock = _blackAt = DateTime.Now;
                Report.Event("screen faded to black");
            }
            else
            {
                _visibleAt = DateTime.Now;
                float seconds = now - _blackSince;
                Report.Event($"screen visible again (black for {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s)");
                if (_stuckReported)
                {
                    _stuckReported = false;
                    UI.Notice.Hide(NoticeStuck);
                    Report.Resolve(SessionReport.StuckTitle,
                        $"the screen came back after {seconds.ToString("0", CultureInfo.InvariantCulture)} s (a slow load, not a freeze)");
                }
            }
        }
        if (black && !_stuckReported && now - _blackSince >= StuckSeconds)
        {
            _stuckReported = true;
            string verdict = Report.Incident(SessionReport.StuckTitle, State(now), _blackSinceClock);
            UI.Notice.Show(NoticeStuck, Loc.F("The screen has been black for {0} seconds. If the game is stuck, a report was saved: {1}",
                ((int)StuckSeconds).ToString(CultureInfo.InvariantCulture), ShortPath(Report.FilePath)) + "\n" + ShortVerdict(verdict), 0f);
        }

        // The game camera stopped drawing to the screen (while the screen isn't faded on purpose).
        var camera = Scene<GameBase>.instance != null ? Scene<GameBase>.instance.camera : null;
        bool cameraOff = Singleton<Service>.Instance?.levelManager?.player != null && camera != null && !black
                         && (!camera.isActiveAndEnabled || camera.targetTexture != null);
        if (!cameraOff)
        {
            if (_cameraOffSince >= 0f)
                Report.Event("game camera draws again");
            _cameraOffSince = -1f;
            _cameraReported = false;
        }
        else if (_cameraOffSince < 0f)
        {
            _cameraOffSince = now;
            Report.Event("game camera stopped drawing to the screen");
        }
        else if (!_cameraReported && now - _cameraOffSince >= CameraSeconds)
        {
            _cameraReported = true;
            Report.Incident("the game camera stopped drawing", State(now));
        }
    }

    /// <summary>A dialogue, a story narration, a cutscene's letterbox or the ending credits: black on purpose.</summary>
    private static bool StoryOnScreen()
    {
        if (global::UI.Dialogue.anyDialogueOpened)
            return true;
        var ui = Scene<GameBase>.instance?.uiManager;
        if (ui == null)
            return false;
        // Each check alone: the narration's getters throw while it isn't set up (the castle's first frame, 0.12.2 test).
        return Is(() => ui.narration != null && (ui.narration.sceneVisible || ui.narration.textVisible))
               || Is(() => ui.letterBox != null && ui.letterBox.visible)
               || Is(() => ui.endingCredit != null && ui.endingCredit.gameObject.activeInHierarchy);
    }

    private static bool Is(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The game's state for an incident: where, the map change's phases, time, input, player, camera, memory.</summary>
    private string State(float now)
    {
        var sb = new StringBuilder();
        var service = Singleton<Service>.Instance;
        var levels = service?.levelManager;
        var chapter = levels?.currentChapter;
        if (_black)
            sb.AppendLine($"Screen black for {(now - _blackSince).ToString("0", CultureInfo.InvariantCulture)} s (the game's fade; no loading screen, no dialogue).");
        sb.AppendLine("Where: " + (chapter != null ? Place(chapter) : "no chapter loaded") + $"; scene {_scene}.");
        string Phase(DateTime t) => t == default ? "never" : t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        sb.AppendLine($"Last screen fade: black {Phase(_blackAt)}; old map removed {Phase(_mapGoneAt)}; new map loaded {Phase(_mapNewAt)}; " +
                      $"screen visible again {Phase(_visibleAt)}.");
        if (_black && _mapGoneAt >= _blackAt && _mapGoneAt > _mapNewAt)
            sb.AppendLine("=> a map change began (old map removed) but the new map never appeared: the game's map change stopped while loading it.");
        else if (_black && _mapNewAt >= _blackAt)
            sb.AppendLine("=> the new map appeared but the screen never faded in: the game's map change stopped after building the map.");
        else if (_black)
            sb.AppendLine("=> the screen went black but no map change followed (the old map is still there).");
        try
        {
            sb.AppendLine($"Game time scale: {Chronometer.global.timeScale.ToString("0.##", CultureInfo.InvariantCulture)}" +
                          $" (Unity {Time.timeScale.ToString("0.##", CultureInfo.InvariantCulture)}); player input blocked: " +
                          (Characters.Controllers.PlayerInput.blocked.value
                              ? "yes, by " + string.Join(", ", Characters.Controllers.PlayerInput.blocked._list.Select(o => o?.GetType().Name ?? "?"))
                              : "no") + ".");
        }
        catch (Exception e)
        {
            sb.AppendLine($"Game time: unreadable ({e.Message}).");
        }
        var player = levels?.player;
        sb.AppendLine(player == null ? "Player: none."
            : $"Player: at {player.transform.position.x:0.#}, {player.transform.position.y:0.#}; health {player.health?.currentHealth:0}/{player.health?.maximumHealth:0}{(player.health != null && player.health.dead ? ", dead" : "")}; active {player.gameObject.activeInHierarchy}.");
        var camera = Scene<GameBase>.instance != null ? Scene<GameBase>.instance.camera : null;
        sb.AppendLine(camera == null ? "Game camera: none."
            : $"Game camera: {(camera.isActiveAndEnabled ? "on" : "OFF")}, {(camera.targetTexture == null ? "draws to the screen" : "draws into a texture '" + camera.targetTexture.name + "'")}.");
        sb.AppendLine($"Memory: {GC.GetTotalMemory(false) / (1024 * 1024)} MB managed, {SystemInfo.systemMemorySize} MB RAM; session {(int)(now / 60)} min.");
        return sb.ToString();
    }

    private void ShowPreviousOnce()
    {
        if (_previousShown || _previous == null || (!_previous.EndedUnexpectedly && _previous.StuckIncidents == 0))
            return;
        // A moment after the title or the castle is on screen (not on the black boot screen).
        if (Time.unscaledTime < 6f || _black)
            return;
        _previousShown = true;
        string text = _previous.EndedUnexpectedly
            ? Loc.F("Your last game session ended unexpectedly. A report was saved: {0}", ShortPath(_previous.FilePath))
            : Loc.F("In your last game session the screen stayed black. A report was saved: {0}", ShortPath(_previous.FilePath));
        string verdict = ShortVerdict(_previous.Verdict);
        UI.Notice.Show(NoticePrevious, text + (verdict.Length > 0 ? "\n" + verdict : "") + "\n" +
                                       Loc.T("Please add it to your bug report (settings page: Crash reports folder, Open)."), 20f);
    }

    /// <summary>The verdict for players, translated (empty when there is none).</summary>
    public static string ShortVerdict(string verdict)
    {
        if (string.IsNullOrEmpty(verdict))
            return "";
        return SessionReport.BlameOf(verdict) switch
        {
            null => Loc.T("No error was recorded right before it."),
            Blame.Mod => Loc.T("An error right before it is in SkulInsight's own code: likely the mod."),
            Blame.Hooked => Loc.T("An error right before it passed through game code that SkulInsight hooks into: SkulInsight may be involved."),
            Blame.OtherMod => Loc.T("An error right before it is in another mod's code."),
            _ => Loc.T("The errors right before it are only in the game's own code, not in SkulInsight's."),
        };
    }

    /// <summary>"BepInEx/DamageInsight/CrashReports/2026-10-15_20-14-03.txt" (from the game folder).</summary>
    public static string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        string root = Path.GetDirectoryName(BepInEx.Paths.BepInExRootPath) ?? "";
        string shortPath = root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length).TrimStart('\\', '/') : path;
        return shortPath.Replace('\\', '/');
    }

    /// <summary>Opens the reports folder (settings page button).</summary>
    public static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Application.OpenURL(DamageInsight.Tools.PlatformPaths.FolderUrl(Folder, DamageInsight.Tools.PlatformPaths.IsWindows));
    }

    // ------------------------------------------------------------------ header and the previous session

    private static string Header()
    {
        var sb = new StringBuilder();
        string build =
#if DEV
            "developer build";
#else
            "release build";
#endif
        sb.AppendLine($"Mod: {MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} ({build}), BepInEx {typeof(BepInEx.Paths).Assembly.GetName().Version}, HarmonyX {typeof(Harmony).Assembly.GetName().Version}");
        sb.AppendLine($"Game: Skul {Application.version}, Unity {Application.unityVersion}");
        string wine = WineVersion();
        sb.AppendLine($"System: {SystemInfo.operatingSystem}{(wine != null ? $" (Wine/Proton {wine})" : "")}; {SystemInfo.processorType}, {SystemInfo.systemMemorySize} MB RAM; " +
                      $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} MB); screen {Screen.width}x{Screen.height}{(Screen.fullScreen ? " full screen" : "")}");
        // The game's own language comes in the timeline once the game has loaded (its data isn't read this early).
        sb.AppendLine($"Mod language: {Plugin.Language?.Value}; save slot {Plugin.SaveSlotSelected?.Value}");
        sb.AppendLine("Settings changed from their defaults: " + ChangedSettings());
        sb.AppendLine($"Game folder: {Path.GetDirectoryName(BepInEx.Paths.BepInExRootPath)}");
        return sb.ToString();
    }

    private static string ChangedSettings()
    {
        if (_config == null)
            return "(unknown)";
        var changed = new List<string>();
        foreach (var pair in _config)
        {
            var entry = pair.Value;
            if (pair.Key.Key is "WindowRect" or "LastVersionSeen" || Equals(entry.BoxedValue, entry.DefaultValue))
                continue;
            changed.Add($"{pair.Key.Section}/{pair.Key.Key} = {entry.BoxedValue}");
        }
        return changed.Count == 0 ? "none" : string.Join("; ", changed);
    }

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("ntdll", EntryPoint = "wine_get_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr WineGetVersion();

    /// <summary>Wine's version when the game runs under Wine or Proton (Linux, Steam Deck), else null.</summary>
    internal static string WineVersion()
    {
        if (!DamageInsight.Tools.PlatformPaths.IsWindows)
            return null; // the Mac version: no kernel32 to ask
        try
        {
            var ntdll = GetModuleHandle("ntdll.dll");
            if (ntdll == IntPtr.Zero || GetProcAddress(ntdll, "wine_get_version") == IntPtr.Zero)
                return null;
            return Marshal.PtrToStringAnsi(WineGetVersion()) ?? "?";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// For a session that ended unexpectedly: Unity's log of that session (renamed Player-prev.log at this start, or the
    /// crash folder's copy) without the crash handler's module list, the crash's stack for the verdict, and the files of
    /// a Unity crash dump written after it began (Unity 2020 writes Player.log and crash.dmp there).
    /// </summary>
    private static SessionReport.Extras PreviousExtras(DateTime started)
    {
        var extras = new SessionReport.Extras();
        try
        {
            DirectoryInfo dump = null;
            string crashes = Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName, "Crashes");
            if (Directory.Exists(crashes))
                dump = new DirectoryInfo(crashes).GetDirectories().Where(d => d.CreationTime >= started.AddMinutes(-1))
                    .OrderByDescending(d => d.CreationTime).FirstOrDefault();

            string log = Application.consoleLogPath;
            string prev = string.IsNullOrEmpty(log) ? null : Path.Combine(Path.GetDirectoryName(log) ?? "", "Player-prev.log");
            if (prev == null || !File.Exists(prev))
                prev = dump != null && File.Exists(Path.Combine(dump.FullName, "Player.log")) ? Path.Combine(dump.FullName, "Player.log") : null;
            if (prev != null)
            {
                var lines = File.ReadAllLines(prev);
                var (excerpt, stack) = SessionReport.ReadUnityLog(lines);
                extras.CrashStack = stack;
                extras.Sections.Add(($"Unity log of that session ({Path.GetFileName(prev)})",
                    $"({prev}, {lines.Length} lines; errors, the crash and the last lines; '...' = lines left out)\n{excerpt}"));
            }

            if (dump != null)
            {
                var sb = new StringBuilder();
                foreach (var file in dump.GetFiles())
                    sb.AppendLine($"{file.Name} ({file.Length / 1024} KB)");
                string error = Path.Combine(dump.FullName, "error.log");
                if (File.Exists(error))
                    sb.AppendLine().AppendLine(string.Join("\n", File.ReadAllLines(error).Take(120)));
                extras.Sections.Add(($"Unity crash dump {dump.FullName}", sb.ToString()));
            }
        }
        catch (Exception e)
        {
            extras.Sections.Add(("extras", $"could not be read: {e.Message}"));
        }
        return extras;
    }
}
