using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using DamageInsight.Lang;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DamageInsight.Crash;

/// <summary>
/// Watches how smooth the game runs (0.16; Umut 2026-10-28: lag on stream "especially with some builds"; Tythos's
/// players: the daily name screen "sometimes freezes"). Into the session report go:
/// - once a minute: frames per second, the slowest 1 %, stutters (frames of 100 ms or more), how much of each frame
///   SkulInsight's own code took and its most expensive parts (<see cref="Perf"/>), garbage collections, memory;
/// - a stutter's own line: SkulInsight's part of that frame, a garbage collection, the busiest parts;
/// - a freeze (no new frame for <see cref="FreezeSeconds"/> s, seen by a background thread): what was going on, so the
///   report says whether SkulInsight's code, Unity's drawing or Windows (an input method) held the game.
/// Optional: a small line on screen with the frames per second and SkulInsight's milliseconds (setting
/// General/PerformanceOverlay). The summary gets a performance section with the session's numbers.
/// </summary>
public sealed class PerfMonitor : MonoBehaviour
{
    public const double FreezeSeconds = 6;
    private const string FreezeTitle = "the game froze";

    private readonly PerfStats _stats = new();
    private readonly List<(string name, long ticks)> _heavy = new();
    private int _lastGc = -1;
    private float _minuteEnd, _nextStutterLine, _nextState;
    private int _stutterLinesSkipped;
    private bool _skipNext = true;
    private long _numbersAtSecond;
    private float _secondEnd;
    private int _numbersPeak;

    // Written by the game's thread, read by the freeze watch.
    private static long _lastFrame;
    private static volatile bool _focused = true;
    private static volatile string _phase = "starting", _state = "";
    private static Thread _watch;

    // The overlay.
    private Canvas _overlay;
    private TextMeshProUGUI _overlayText;
    private float _overlayNext;
    private int _overlayFrames;
    private double _overlayMod, _overlaySeconds;

    private static void I(StringBuilder sb, FormattableString s) => sb.Append(FormattableString.Invariant(s));

    private void Awake()
    {
        Perf.SetMainThread();
        Perf.Enabled = Plugin.PerformanceLog?.Value != false;
        Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
        _minuteEnd = Time.unscaledTime + 60f;
        if (CrashReporter.Report != null)
            CrashReporter.Report.PerformanceSummary = () => _stats.Summary(Perf.SessionParts());
        Canvas.willRenderCanvases += () => _phase = "drawing";
        StartCoroutine(EndOfFrames());
        if (_watch == null && CrashReporter.Report != null)
        {
            _watch = new Thread(Watch) { IsBackground = true, Name = "SkulInsight freeze watch" };
            _watch.Start();
        }
    }

    /// <summary>After a frame is drawn: what runs until the next frame's updates is Unity's and Windows' own (input,
    /// window messages, an input method).</summary>
    private IEnumerator EndOfFrames()
    {
        var end = new WaitForEndOfFrame();
        while (true)
        {
            yield return end;
            _phase = "between frames";
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        _focused = focused;
        _skipNext = true; // the first frame back counts the time away
    }

    private void OnApplicationPause(bool paused)
    {
        _focused = !paused;
        _skipNext = true;
    }

    private void Update()
    {
        Interlocked.Exchange(ref _lastFrame, Stopwatch.GetTimestamp());
        _phase = "updates";
        bool enabled = Plugin.PerformanceLog?.Value != false;
        Perf.Enabled = enabled;
        long modTicks = Perf.TakeFrame(_heavy);
        double ms = Time.unscaledDeltaTime * 1000.0;
        int gc = GC.CollectionCount(0);
        bool gcRan = _lastGc >= 0 && gc != _lastGc;
        _lastGc = gc;
        double modMs = Perf.Ms(modTicks);

        if (Time.unscaledTime >= _secondEnd)
        {
            _secondEnd = Time.unscaledTime + 1f;
            long started = Patches.DamageNumberPatch.Started;
            _numbersPeak = Math.Max(_numbersPeak, (int)(started - _numbersAtSecond));
            _numbersAtSecond = started;
        }

        if (_skipNext)
            _skipNext = false;
        else if (enabled)
        {
            bool loading = CrashReporter.ScreenBlack;
            _stats.Frame(ms, modMs, gcRan, loading);
            if (ms >= PerfStats.StutterMs && !loading && Time.frameCount > 30)
                Stutter(ms, modMs, gcRan);
        }

        if (enabled && Time.unscaledTime >= _minuteEnd)
        {
            _minuteEnd = Time.unscaledTime + 60f;
            var parts = Perf.TakeMinute();
            string line = _stats.TakeMinute(parts, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong());
            if (line.Length > 0)
            {
                if (_numbersPeak > 0)
                    line += FormattableString.Invariant($"; damage numbers up to {_numbersPeak} a second");
                if (_stutterLinesSkipped > 0)
                    line += FormattableString.Invariant($"; {_stutterLinesSkipped} more stutters not written one by one");
                CrashReporter.Note(line);
            }
            _numbersPeak = 0;
            _stutterLinesSkipped = 0;
        }

        if (Time.unscaledTime >= _nextState)
        {
            _nextState = Time.unscaledTime + 0.25f;
            Patches.Guard.Run("Performance (state)", () => _state = State());
        }
        Patches.Guard.Run("Performance (overlay)", () => Overlay(ms, modMs));
    }

    /// <summary>A stutter's own line (at most one in 10 s; the minute's line counts the rest).</summary>
    private void Stutter(double ms, double modMs, bool gcRan)
    {
        if (Time.unscaledTime < _nextStutterLine)
        {
            _stutterLinesSkipped++;
            return;
        }
        _nextStutterLine = Time.unscaledTime + 10f;
        var sb = new StringBuilder();
        I(sb, $"stutter: a frame of {ms:0} ms; SkulInsight {modMs:0.0} ms of it");
        if (_heavy.Count > 0)
        {
            sb.Append(" (");
            for (int i = 0; i < _heavy.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                I(sb, $"{_heavy[i].name} {Perf.Ms(_heavy[i].ticks):0.0} ms");
            }
            sb.Append(')');
        }
        if (gcRan)
            sb.Append("; a garbage collection ran");
        if (modMs < ms * 0.25)
            sb.Append("; most of it outside SkulInsight (the game, Unity or the PC)");
        CrashReporter.Note(sb.ToString());
    }

    // ------------------------------------------------------------------ what is going on (for a freeze)

    private static string State()
    {
        var parts = new List<string>();
        if (Codex.CodexWindow.IsOpen) parts.Add("the Codex is open");
        if (CombatLogWindow.IsOpen) parts.Add("the combat log is open");
        if (SettingsPage.IsOpen) parts.Add("the settings page is open");
        if (WhatsNew.IsOpen) parts.Add("What's new is open");
        if (ModManagerWindow.IsOpen) parts.Add("the Mods window is open");
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        if (field != null && field.isFocused)
        {
            parts.Add($"a text box has the keyboard ('{field.gameObject.name}', {field.text?.Length ?? 0} characters)");
            string composing = Input.compositionString;
            if (!string.IsNullOrEmpty(composing))
                parts.Add($"an input method (IME) is composing ({composing.Length} characters)");
        }
        parts.Add($"screen {Screen.width}x{Screen.height} {Screen.fullScreenMode}");
        return string.Join("; ", parts);
    }

    /// <summary>The freeze watch (its own thread): no new frame for a while is written down with what was going on.</summary>
    private static void Watch()
    {
        bool frozen = false;
        long frozenAt = 0;
        while (true)
        {
            try
            {
                Thread.Sleep(500);
                var report = CrashReporter.Report;
                if (report == null || report.Ended)
                    continue;
                long last = Interlocked.Read(ref _lastFrame);
                double gap = (Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency;
                if (!frozen && gap >= FreezeSeconds && _focused)
                {
                    frozen = true;
                    frozenAt = last;
                    var verdict = PerfStats.FreezeVerdict(Perf.Current, _phase, _state);
                    report.Incident(FreezeTitle,
                        FormattableString.Invariant($"No new frame for {gap:0} s (the freeze watch looks twice a second).\n") +
                        $"SkulInsight's code running: {(Perf.Current.Length > 0 ? Perf.Current : "none")}; the last part of it that finished: {(Perf.LastFinished.Length > 0 ? Perf.LastFinished : "-")}.\n" +
                        $"Where the frame was: {PerfStats.Phase(_phase)}.\n" +
                        $"On screen: {(_state.Length > 0 ? _state : "-")}.",
                        DateTime.Now.AddSeconds(-gap), verdict);
                }
                else if (frozen && last != frozenAt)
                {
                    frozen = false;
                    double stood = (last - frozenAt) / (double)Stopwatch.Frequency;
                    report.Resolve(FreezeTitle, FormattableString.Invariant($"the game went on after standing still for {stood:0.0} s"));
                }
            }
            catch (ThreadAbortException)
            {
                return;
            }
            catch (Exception)
            {
                // the watch never takes the game down
            }
        }
    }

    // ------------------------------------------------------------------ the overlay

    private void Overlay(double ms, double modMs)
    {
        bool want = Plugin.PerformanceOverlay?.Value == true;
        if (!want)
        {
            if (_overlay != null)
                _overlay.gameObject.SetActive(false);
            return;
        }
        if (_overlay == null)
        {
            _overlay = UiKit.OverlayCanvas("SkulInsight performance", 6000);
            _overlayText = UiKit.Text("Line", _overlay.transform, "", 18, TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.85f));
            _overlayText.enableWordWrapping = false;
            var rect = _overlayText.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -6f);
            rect.sizeDelta = new Vector2(600f, 28f);
            _overlayText.outlineWidth = 0.2f;
            _overlayText.outlineColor = new Color32(0, 0, 0, 200);
        }
        _overlay.gameObject.SetActive(true);
        _overlayFrames++;
        _overlayMod += modMs;
        _overlaySeconds += ms / 1000.0;
        if (Time.unscaledTime < _overlayNext || _overlaySeconds <= 0)
            return;
        _overlayNext = Time.unscaledTime + 0.5f;
        double fps = _overlayFrames / _overlaySeconds;
        double mod = _overlayMod / Math.Max(1, _overlayFrames);
        _overlayText.text = Loc.F("{0} fps · SkulInsight {1} ms", fps.ToString("0", CultureInfo.InvariantCulture), mod.ToString("0.00", CultureInfo.InvariantCulture));
        _overlayFrames = 0;
        _overlayMod = _overlaySeconds = 0;
    }
}
