using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DamageInsight.Coop;
using DamageInsight.Lang;
using DamageInsight.UI;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace DamageInsight.Crash;

/// <summary>The setting Coop/SendRunLogs: ask after the first co-op run, or the answer given.</summary>
public enum RunLogsChoice
{
    Ask,
    Yes,
    No,
}

/// <summary>
/// Automatic co-op run logs (Umut 2026-11-24, docs/coop/REPORTS.md part B: "automatically send the logs to the server
/// after a coop run ... we could get the two pairs of coop players at all times ... also through steam sessions").
/// Watches every co-op run (matchmaking and Steam lobbies alike): from leaving the castle to being back in it, or the
/// session ending. With Coop/SendRunLogs = Yes it sends THIS run's logs (its part of the co-op trace, the end of
/// LogOutput.log, the newest session report, report.txt) to /api/coop/runlogs with a key both games compute alike
/// (ReportPayload.RunKey), so the server pairs host and client. Ask (the default): after the run, back in the castle, a
/// small window asks once (Yes / No / Not now); nothing is sent before the answer. Test copies only log the runs.
/// </summary>
public sealed class RunLogs : MonoBehaviour
{
    // The enum's values as the settings page shows them (Loc.Name of the value's name).
    internal static readonly string[] ChoiceLabels = { Loc.N("Ask"), Loc.N("Yes"), Loc.N("No") };

    private sealed class Run
    {
        public int Seed, Level, Rooms, Players, Slot;
        public bool Host, DarkMirror;
        public uint FirstRoom, LastRoom;
        public string HostName = "", Trace;
        public long TraceFrom;
        public float Started;
    }

    /// <summary>The question is open (the cursor stays visible: CursorPatch).</summary>
    public static bool AskOpen { get; private set; }

    private Run _run;
    private Run _pending;           // ended while the answer is "Ask": sent once the player says Yes
    private string _pendingEnd = "";
    private float _nextCheck, _castleSince = -1f;
    private Canvas _ask;

    private static RunLogs _instance;

    private void Awake() => _instance = this;

    /// <summary>The launcher's window test: the question as after a first co-op run (nothing is pending: no upload).</summary>
    internal static void TestAsk()
    {
        if (_instance != null && _instance._ask == null)
            Patches.Guard.Run("Co-op run logs (test)", _instance.ShowAsk);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + 1f;
        Patches.Guard.Run("Co-op run logs", Tick);
    }

    private void Tick()
    {
        var world = CoopWorld.Current;
        bool active = world?.Active == true;
        var levels = Singleton<Service>.Instance?.levelManager;
        var chapter = levels?.currentChapter;
        bool castle = chapter == null || chapter.type is Level.Chapter.Type.Castle or Level.Chapter.Type.HardmodeCastle
            or Level.Chapter.Type.Tutorial or Level.Chapter.Type.Test;
        if (_run == null)
        {
            if (active && !castle && levels.player != null && !world.Busy)
                Begin(world, chapter);
        }
        else if (!active || castle)
        {
            var run = _run;
            _run = null;
            End(run, !active ? "left" : "castle");
        }
        else
        {
            uint room = CoopWorld.CurrentRoom();
            if (room != 0 && room != _run.LastRoom)
            {
                _run.FirstRoom = _run.FirstRoom != 0 ? _run.FirstRoom : room;
                _run.LastRoom = room;
                _run.Rooms++;
            }
        }
        if (_pending != null && _ask == null && Plugin.CoopSendRunLogs?.Value == RunLogsChoice.Ask)
            MaybeAsk(castle && levels?.player != null);
    }

    private void Begin(CoopWorld world, Level.Chapter chapter)
    {
        var session = CoopRuntime.Instance?.Session;
        if (session == null)
            return;
        CoopTrace.Flush();
        string trace = CoopTrace.BasePath != null ? CoopTrace.BasePath + ".jsonl" : null;
        _run = new Run
        {
            Seed = Data.GameData.Save.instance.randomSeed,
            Host = world.IsHost,
            Slot = Math.Max(0, session.LocalSlot),
            HostName = session.PlayerAt(0)?.Name ?? "",
            Players = session.Roster.Count,
            DarkMirror = chapter.type >= Level.Chapter.Type.HardmodeChapter1,
            Level = Data.GameData.HardmodeProgress.hardmodeLevel,
            Trace = trace,
            TraceFrom = trace != null && File.Exists(trace) ? new FileInfo(trace).Length : 0,
            Started = Time.realtimeSinceStartup,
        };
        uint room = CoopWorld.CurrentRoom();
        if (room != 0)
        {
            _run.FirstRoom = _run.LastRoom = room;
            _run.Rooms = 1;
        }
        Plugin.Log.LogInfo($"[Coop] run logs: run started ({(_run.Host ? "host" : "client")}, {chapter.type})");
    }

    private void End(Run run, string end)
    {
        Plugin.Log.LogInfo($"[Coop] run logs: run ended ({end}, {run.Rooms} rooms, key {ReportPayload.RunKey(run.Seed, run.FirstRoom, run.HostName)})");
        if (run.FirstRoom == 0 || Coop.Testing.CoopLaunch.Current.FromLauncher)
            return; // never got into a room; test copies track and log, never send or ask
        switch (Plugin.CoopSendRunLogs?.Value ?? RunLogsChoice.Ask)
        {
            case RunLogsChoice.Yes:
                Send(run, end);
                break;
            case RunLogsChoice.Ask:
                _pending = run; // the newest run waits for the answer
                _pendingEnd = end;
                break;
        }
    }

    // ---------------------------------------------------------------- sending

    private void Send(Run run, string end)
    {
        CoopTrace.Flush();
        string key = ReportPayload.RunKey(run.Seed, run.FirstRoom, run.HostName);
        string json = ReportPayload.RunJson(key, run.Host, run.Slot, MyPluginInfo.PLUGIN_VERSION, Application.version,
            run.DarkMirror ? "darkmirror" : "normal", run.DarkMirror ? run.Level : 0, run.Rooms,
            (int)(Time.realtimeSinceStartup - run.Started), run.Players, end);
        var parts = new List<ZipPart> { new() { Name = "run.json", Data = Encoding.UTF8.GetBytes(json) } };
        if (run.Trace != null)
            parts.Add(new ZipPart { Name = "CoopTrace/" + Path.GetFileName(run.Trace), Path = run.Trace, From = run.TraceFrom, Cuttable = true });
        string log = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
        long logFrom = File.Exists(log) ? Math.Max(0, new FileInfo(log).Length - 6L * 1024 * 1024) : 0;
        parts.Add(new ZipPart { Name = "BepInEx/LogOutput.log", Path = log, From = logFrom, Cuttable = true });
        foreach (string file in ReportZip.Newest(CrashReporter.Folder, "*.txt", 1))
            parts.Add(new ZipPart { Name = "SessionReports/" + Path.GetFileName(file), Path = file, Cuttable = true });
        var report = new StringBuilder();
        report.AppendLine("SkulInsight co-op run logs, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        report.Append(CrashReporter.Header());
        report.AppendLine($"Run: key {key}, {(run.Host ? "host" : "client " + run.Slot)}, {run.Rooms} rooms, end {end}, trace from byte {run.TraceFrom}");
        parts.Add(new ZipPart { Name = "report.txt", Data = Encoding.UTF8.GetBytes(report.ToString()) });
        string user = Environment.UserName;
        var now = DateTime.Now;
        var pack = BackgroundWork.Track(Task.Run(() => ReportZip.Build(parts, ReportPayload.ZipMax, user, now)));
        StartCoroutine(CSend(pack, key));
    }

    private static IEnumerator CSend(Task<byte[]> pack, string key)
    {
        while (!pack.IsCompleted)
            yield return null;
        if (pack.Status != TaskStatus.RanToCompletion || pack.Result == null)
        {
            Plugin.Log.LogWarning("[Coop] run logs: not packed: " + pack.Exception?.InnerException?.Message);
            yield break;
        }
        string server = (Plugin.DailyServer?.Value ?? "").TrimEnd('/');
        using var request = new UnityWebRequest(server + ReportPayload.RunLogsPath, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(pack.Result),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 120,
        };
        request.SetRequestHeader("Content-Type", "application/zip");
        request.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return request.SendWebRequest();
        Plugin.Log.LogInfo($"[Coop] run logs: {key} sent ({pack.Result.Length / 1024} KB), answer " +
                           (request.result is UnityWebRequest.Result.Success or UnityWebRequest.Result.ProtocolError ? request.responseCode.ToString() : request.error));
    }

    // ---------------------------------------------------------------- the question (asked once)

    private void MaybeAsk(bool inCastle)
    {
        if (!inCastle || global::UI.Dialogue.anyDialogueOpened || WhatsNew.IsOpen || UpdateNotice.IsOpen || SettingsPage.IsOpen
            || CoopWindow.IsOpen || ReportWindow.IsOpen || Codex.CodexWindow.IsOpen)
        {
            _castleSince = -1f;
            return;
        }
        if (_castleSince < 0f)
            _castleSince = Time.unscaledTime;
        if (Time.unscaledTime - _castleSince >= 3f)
            ShowAsk();
    }

    private void ShowAsk()
    {
        _ask = UiKit.OverlayCanvas("SkulInsight Co-op run logs", 2250);
        _ask.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        ModEventSystem.Ensure();
        AskOpen = true;
        var panel = UiKit.Image("Panel", _ask.transform, new Color32(0x1B, 0x12, 0x24, 0xF2));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(800, 330);
        var outline = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color32(0x5A, 0x50, 0xC8, 0xFF);
        outline.effectDistance = new Vector2(2, -2);
        UiKit.Text("Title", rect, Loc.T("Send co-op run logs?"), 30, TextAlignmentOptions.Center, new Color32(0xF2, 0xD7, 0x8B, 0xFF))
            .rectTransform.Place(30, 20, 740, 44);
        var body = UiKit.Text("Body", rect,
            Loc.T("Co-op bugs are hard to find without both players' logs. After each co-op run, SkulInsight can send that run's logs to the developer: never public, Steam ids and your account name hidden.") +
            $"\n\n<color=#A89F94>{Loc.T("You can change it later: settings, Co-op, Send co-op run logs.")}</color>", 20, TextAlignmentOptions.Top);
        body.rectTransform.Place(40, 76, 720, 170);
        body.enableAutoSizing = true;
        body.fontSizeMin = 14;
        body.fontSizeMax = 20;
        var buttons = new (string label, Action click, Color color)[]
        {
            (Loc.T("Yes, after every run"), () => Answer(RunLogsChoice.Yes), new Color32(0x3D, 0xDC, 0x97, 0xFF)),
            (Loc.T("No"), () => Answer(RunLogsChoice.No), new Color32(0xE5, 0x62, 0x6A, 0xFF)),
            (Loc.T("Not now"), () => Answer(RunLogsChoice.Ask), new Color32(0x4A, 0x55, 0xC8, 0xFF)),
        };
        float total = 0;
        foreach (var b in buttons)
            total += UiKit.ChipWidth(b.label) + 30 + 12;
        float x = (800 - total + 12) / 2;
        foreach (var (label, click, color) in buttons)
        {
            float w = UiKit.ChipWidth(label) + 30;
            var chip = new UiKit.Chip(rect, label, x, 330 - 22 - 38, w, click, color);
            chip.Background.rectTransform.sizeDelta = new Vector2(w, 38);
            chip.SetOn(true);
            x += w + 12;
        }
        Plugin.Log.LogInfo("[Coop] run logs: asked");
    }

    private void LateUpdate()
    {
        if (_ask != null && Input.GetKeyDown(KeyCode.Escape))
            Answer(RunLogsChoice.Ask);
    }

    private void Answer(RunLogsChoice choice)
    {
        if (_ask != null)
            Destroy(_ask.gameObject);
        _ask = null;
        AskOpen = false;
        MenuSounds.Play(MenuSounds.Kind.Close);
        var pending = _pending;
        _pending = null; // "Not now": this run is not sent, the next one asks again
        if (choice == RunLogsChoice.Ask)
            return;
        if (Plugin.CoopSendRunLogs != null)
            Plugin.CoopSendRunLogs.Value = choice;
        Plugin.Log.LogInfo("[Coop] run logs: the answer is " + choice);
        if (choice == RunLogsChoice.Yes && pending != null)
            Patches.Guard.Run("Co-op run logs (send)", () => Send(pending, _pendingEnd));
    }
}
