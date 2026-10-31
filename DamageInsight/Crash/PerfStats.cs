using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DamageInsight.Crash;

/// <summary>
/// The numbers of the performance lines in the session report: a minute of frames (frames per second, the slowest
/// 1 %, stutters, SkulInsight's share, garbage collections) and the session's summary. Plain logic, tested.
/// Numbers are written the same way on every PC (a dot for decimals), like the rest of the report.
/// </summary>
public sealed class PerfStats
{
    /// <summary>A frame this long is a stutter you see (6 frames at 60 frames per second).</summary>
    public const double StutterMs = 100;

    private readonly List<float> _frameMs = new(20000);
    private double _modMs;
    private int _stutters, _loadingStutters, _gcs;
    private double _worstMs;

    // The session so far.
    private long _sessionFrames, _sessionStutters, _sessionLoadingStutters, _sessionGcs;
    private double _sessionSeconds, _sessionModMs;
    private (string at, double fps, double low) _worstMinute = ("", double.MaxValue, 0);

    public int Frames => _frameMs.Count;
    public double Seconds { get; private set; }

    private static string I(FormattableString s) => FormattableString.Invariant(s);

    /// <summary>One frame: its time, SkulInsight's part of it, a garbage collection in it, a map change going on.</summary>
    public void Frame(double ms, double modMs, bool gc, bool loading)
    {
        if (_frameMs.Count < 200000)
            _frameMs.Add((float)ms);
        Seconds += ms / 1000.0;
        _modMs += modMs;
        if (gc)
            _gcs++;
        if (ms > _worstMs)
            _worstMs = ms;
        if (ms >= StutterMs)
        {
            if (loading)
                _loadingStutters++;
            else
                _stutters++;
        }
    }

    /// <summary>The minute's line ("" when no frame came) and a fresh start for the next minute.</summary>
    public string TakeMinute(IReadOnlyList<(string name, Perf.Part part)> parts, string at, long heapBytes)
    {
        if (_frameMs.Count == 0 || Seconds <= 0)
            return "";
        int frames = _frameMs.Count;
        double fps = frames / Seconds;
        double low = LowFps(_frameMs);
        double modPerFrame = _modMs / frames;
        double share = _modMs / (Seconds * 1000.0) * 100.0;
        var sb = new StringBuilder();
        sb.Append(I($"performance (last {Seconds:0} s): {fps:0} fps, slowest 1 % {low:0} fps, worst frame {_worstMs:0} ms"));
        if (_stutters > 0 || _loadingStutters > 0)
            sb.Append(I($", {_stutters} stutter{(_stutters == 1 ? "" : "s")} over {StutterMs:0} ms") +
                      (_loadingStutters > 0 ? I($" (+{_loadingStutters} during map changes)") : ""));
        sb.Append(I($"; SkulInsight {modPerFrame:0.00} ms a frame ({share:0.0} % of the time)"));
        var top = parts.Where(p => p.part.Ticks > 0).Take(4).ToList();
        if (top.Count > 0)
            sb.Append(": " + string.Join(", ", top.Select(p => I($"{p.name} {Perf.Ms(p.part.Ticks) / frames:0.00}"))));
        sb.Append(I($"; garbage collections {_gcs}"));
        if (heapBytes > 0)
            sb.Append(I($", memory {heapBytes / (1024 * 1024)} MB"));

        _sessionFrames += frames;
        _sessionSeconds += Seconds;
        _sessionModMs += _modMs;
        _sessionStutters += _stutters;
        _sessionLoadingStutters += _loadingStutters;
        _sessionGcs += _gcs;
        if (Seconds >= 30 && fps < _worstMinute.fps)
            _worstMinute = (at, fps, low);

        _frameMs.Clear();
        Seconds = _modMs = _worstMs = 0;
        _stutters = _loadingStutters = _gcs = 0;
        return sb.ToString();
    }

    /// <summary>The frames per second of the slowest 1 % of frames (what a stutter feels like).</summary>
    public static double LowFps(IReadOnlyCollection<float> frameMs)
    {
        if (frameMs.Count == 0)
            return 0;
        var sorted = frameMs.OrderByDescending(f => f).ToList();
        int n = Math.Max(1, sorted.Count / 100);
        double ms = sorted.Take(n).Average(f => f);
        return ms <= 0 ? 0 : 1000.0 / ms;
    }

    /// <summary>The session's performance section for the summary.</summary>
    public string Summary(IReadOnlyList<(string name, Perf.Part part)> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Performance ===");
        if (_sessionFrames == 0 || _sessionSeconds <= 0)
        {
            sb.AppendLine("(not measured yet: the first minute isn't over)");
            return sb.ToString();
        }
        double fps = _sessionFrames / _sessionSeconds;
        double modPerFrame = _sessionModMs / _sessionFrames;
        sb.AppendLine(I($"{fps:0} fps on average over {Time(_sessionSeconds)}; {_sessionStutters} stutters over {StutterMs:0} ms") +
                      (_sessionLoadingStutters > 0 ? I($" (+{_sessionLoadingStutters} during map changes)") : "") +
                      I($"; {_sessionGcs} garbage collections."));
        if (_worstMinute.at.Length > 0)
            sb.AppendLine(I($"Slowest minute: the one up to {_worstMinute.at} ({_worstMinute.fps:0} fps, slowest 1 % {_worstMinute.low:0} fps)."));
        sb.AppendLine(I($"SkulInsight's own code: {modPerFrame:0.00} ms a frame on average ({_sessionModMs / (_sessionSeconds * 1000.0) * 100.0:0.0} % of the time). ") +
                      "Its parts (time in all, calls, slowest single call):");
        foreach (var (name, part) in parts.Take(15))
            sb.AppendLine(I($"  {name,-34} {Perf.Ms(part.Ticks) / 1000.0,8:0.0} s  {part.Calls,10:N0} calls  slowest {Perf.Ms(part.MaxTicks),6:0.0} ms"));
        return sb.ToString();
    }

    public static string Phase(string phase) => phase switch
    {
        "updates" => "the frame's updates (the game's scripts and the mod's)",
        "drawing" => "drawing the frame (Unity's UI layout and rendering)",
        "between frames" => "between two frames (Unity's and Windows' own work: input, window messages, an input method)",
        _ => "the game was starting",
    };

    /// <summary>Whose code held the game, from what the watch saw (plain logic, tested).</summary>
    public static SessionReport.Verdict FreezeVerdict(string modCode, string phase, string state)
    {
        bool typing = state != null && state.Contains("a text box has the keyboard");
        bool ime = state != null && state.Contains("input method (IME) is composing");
        if (!string.IsNullOrEmpty(modCode))
            return new SessionReport.Verdict(Blame.Mod, $"LIKELY SKULINSIGHT: the game stood still while SkulInsight's own code ran ({modCode}).");
        string where = Phase(phase);
        string extra = ime ? " An input method (IME) was composing text in a SkulInsight text box: Windows' input methods are known to hang some full-screen games; windowed mode or the previous version of the Microsoft IME may avoid it."
            : typing ? " A SkulInsight text box had the keyboard."
            : "";
        return new SessionReport.Verdict(Blame.Game, $"NOT SKULINSIGHT'S CODE: the game stood still in {where}; none of SkulInsight's code was running.{extra}");
    }

    private static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? I($"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}") : I($"{t.Minutes}:{t.Seconds:00}");
    }
}
