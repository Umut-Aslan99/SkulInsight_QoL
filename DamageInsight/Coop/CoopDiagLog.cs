using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop;

/// <summary>
/// The co-op part of the session report (Umut 2026-11-16: "proper logging to our coop things ... at all times, so that
/// we can debug better and users can send us crash logs and bugs easier"): plain English lines about the session
/// (start, joins, leaves with their reason, the end), one line per teammate each minute (ping, jitter, loss, states/s,
/// snaps, rewinds, silences), long loading-barrier waits and the co-op warnings, each kind rate limited. Bounded:
/// <see cref="MaxLines"/> lines per game session, then a summary every 5 minutes. Plain logic (tested); the game side
/// (CoopDiagnostics) feeds it and writes its lines with CrashReporter.Note. Times are seconds of any clock.
/// </summary>
public sealed class CoopDiagLog
{
    public const int MaxLines = 200, MaxSummaryLines = 100, MaxVitalExtra = 50;
    public const double Minute = 60, QuietPeriod = 300, KeySeconds = 60, SilenceLimit = 2, BarrierLimit = 3;
    public const int Burst = 3, MaxLineLength = 300;

    private enum Kind { Normal, Summary, Vital }

    private sealed class Mate
    {
        public string Name = "";
        public double PingSum, PingMax, JitterSum, LossSum, SilenceMax, TrafficSeconds;
        public int PingCount, JitterCount, LossCount, Silences, States, Snaps, Rewinds;
        public bool InSilence;
    }

    private sealed class KeyState
    {
        public int Written, Suppressed, Total;
        public double Last = double.NegativeInfinity;
    }

    private readonly Action<string> _write;
    private readonly SortedDictionary<int, Mate> _mates = new();
    private readonly Dictionary<string, KeyState> _keys = new();
    private readonly Dictionary<string, int> _errors = new();
    private int _normal, _summaries, _vital, _leftOutSinceSummary;
    private double _periodStart = double.NaN, _sessionStart = double.NaN;
    private int _joins, _leaves, _barrierWaits;

    public CoopDiagLog(Action<string> write) => _write = write ?? (_ => { });

    /// <summary>Lines written so far (all kinds) and lines left out because of the bounds.</summary>
    public int Written => _normal + _summaries + _vital;
    public int LeftOut { get; private set; }
    public bool Quiet => _normal >= MaxLines;
    public double Period => Quiet ? QuietPeriod : Minute;

    // ------------------------------------------------------------------ the session

    public void SessionStarted(double now, string role, string transport, string version, string localName, int localSlot,
        IEnumerable<string> players, string detailedLog = null)
    {
        _sessionStart = now;
        _periodStart = now;
        _joins = _leaves = _barrierWaits = 0;
        string line = Inv($"session started as {role} over {transport} (mod {version}, protocol {Protocol.Version}): this game is P{localSlot + 1} {localName}");
        var list = players?.ToList();
        if (list is { Count: > 0 })
            line += "; players: " + string.Join(", ", list);
        if (!string.IsNullOrEmpty(detailedLog))
            line += "; detailed co-op log: " + detailedLog;
        Emit(line, Kind.Vital);
    }

    public void Joined(double now, int slot, string name)
    {
        _joins++;
        MateAt(slot, name);
        Emit(Inv($"P{slot + 1} {name} joined"), Kind.Vital);
    }

    public void Left(double now, int slot, string name, LeaveReason reason)
    {
        _leaves++;
        if (_mates.TryGetValue(slot, out var m))
        {
            FlushMate(now, slot, m, Kind.Normal);
            _mates.Remove(slot);
        }
        Emit(Inv($"P{slot + 1} {name} left: {Explain(reason)} ({reason})"), Kind.Vital);
    }

    /// <summary>The whole session ended. <paramref name="detail"/>: the session's own words ("the host said no: ...").</summary>
    public void SessionEnded(double now, LeaveReason reason, string detail)
    {
        bool started = !double.IsNaN(_sessionStart);
        foreach (var pair in _mates)
            FlushMate(now, pair.Key, pair.Value, Kind.Normal);
        _mates.Clear();
        string why = Explain(reason) + " (" + reason + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + ")";
        if (!started)
        {
            // A join that never got in (a copy that starts before its host retries every few seconds): rate limited.
            Keyed(now, "session not started", "the session ended before it started: " + why);
            return;
        }
        var sb = new StringBuilder(Inv($"session ended after {Span(now - _sessionStart)}: {why}; {_joins} joined, {_leaves} left"));
        if (_barrierWaits > 0)
            sb.Append(Inv($", {_barrierWaits} long loading waits"));
        var warnings = _keys.Where(k => k.Key.StartsWith("w:", StringComparison.Ordinal)).ToList();
        if (warnings.Count > 0)
            sb.Append(Inv($", warnings: {Top(warnings.Select(k => (k.Key.Substring(2), k.Value.Total)))}"));
        if (_errors.Count > 0)
            sb.Append(Inv($", co-op errors logged: {Top(_errors.Select(k => (k.Key, k.Value)))} (see the ERROR lines)"));
        if (LeftOut > 0)
            sb.Append(Inv($", {LeftOut} co-op lines left out so far"));
        Emit(sb.ToString(), Kind.Vital);
        _sessionStart = double.NaN;
    }

    // ------------------------------------------------------------------ events

    /// <summary>A line of the session's own log (rejections, broken messages, failed handlers); joins, leaves and
    /// the end come as events and are skipped here.</summary>
    public void SessionLine(double now, string text)
    {
        if (string.IsNullOrEmpty(text) || text.StartsWith("session ended: ", StringComparison.Ordinal)
            || text.Contains(" joined in slot ") || text.StartsWith("joined the host ", StringComparison.Ordinal) || text.Contains(") left: "))
            return;
        Keyed(now, "n:" + KeyOf(text), "network: " + text);
    }

    /// <summary>A "[Coop]" warning of the mod (catch-ups, out of step, barrier timeouts, missing paths...).</summary>
    public void Warning(double now, string text)
    {
        text = Strip(text);
        Keyed(now, "w:" + KeyOf(text), "warning: " + text);
    }

    /// <summary>A "[Coop]" error of the mod: the report has it as an ERROR already; counted for the end line.</summary>
    public void Error(double now, string text)
    {
        string key = KeyOf(Strip(text));
        _errors[key] = _errors.TryGetValue(key, out int n) ? n + 1 : 1;
    }

    /// <summary>The loading barrier let go (timeouts come as a warning: only long waits are written here).</summary>
    public void Barrier(double now, double waitedSeconds, bool timedOut)
    {
        if (timedOut || waitedSeconds <= BarrierLimit)
            return;
        _barrierWaits++;
        Keyed(now, "barrier", Inv($"loading barrier: waited {waitedSeconds:0.0} s for everyone"));
    }

    // ------------------------------------------------------------------ teammates

    /// <summary>A look at one teammate's connection (a few times a second). -1: not known.</summary>
    public void Sample(double now, int slot, string name, double pingMs, double jitterMs, double lossPercent, double silenceSeconds)
    {
        var m = MateAt(slot, name);
        if (pingMs >= 0)
        {
            m.PingSum += pingMs;
            m.PingCount++;
            m.PingMax = Math.Max(m.PingMax, pingMs);
        }
        if (jitterMs >= 0)
        {
            m.JitterSum += jitterMs;
            m.JitterCount++;
        }
        if (lossPercent >= 0)
        {
            m.LossSum += lossPercent;
            m.LossCount++;
        }
        if (silenceSeconds > SilenceLimit)
        {
            if (!m.InSilence)
                m.Silences++;
            m.InSilence = true;
            m.SilenceMax = Math.Max(m.SilenceMax, silenceSeconds);
        }
        else
            m.InSilence = false;
    }

    /// <summary>What came from a teammate in the last <paramref name="seconds"/> (CoopRuntime's 10 s report).</summary>
    public void Traffic(int slot, int states, int snaps, int rewinds, double seconds)
    {
        if (!_mates.TryGetValue(slot, out var m))
            return;
        m.States += states;
        m.Snaps += snaps;
        m.Rewinds += rewinds;
        m.TrafficSeconds += seconds;
    }

    /// <summary>Once a frame or so: writes the teammate lines when a period (a minute, 5 in quiet mode) is over.</summary>
    public void Tick(double now)
    {
        if (double.IsNaN(_periodStart))
            _periodStart = now;
        if (now - _periodStart < Period)
            return;
        var kind = Quiet ? Kind.Summary : Kind.Normal;
        foreach (var pair in _mates)
            FlushMate(now, pair.Key, pair.Value, kind);
        if (Quiet && _leftOutSinceSummary > 0)
        {
            int n = _leftOutSinceSummary;
            Emit(Inv($"{n} co-op lines left out in the last {Span(now - _periodStart)} ({LeftOut} in all)"), Kind.Summary);
            _leftOutSinceSummary = 0;
        }
        _periodStart = now;
    }

    /// <summary>One teammate's line for the period so far ("P2 Bob (last 1 min): ping 82 ms avg / 140 max, ...").</summary>
    public static string MateLine(int slot, string name, double seconds, double pingAvg, double pingMax, double jitter, double loss,
        double statesPerSecond, int snaps, int rewinds, int silences, double longestSilence)
    {
        var sb = new StringBuilder(Inv($"P{slot + 1} {name} (last {Span(seconds)}): "));
        sb.Append(pingAvg >= 0 ? Inv($"ping {pingAvg:0} ms avg / {pingMax:0} max") : "ping not measured");
        if (jitter >= 0)
            sb.Append(Inv($", jitter {jitter:0} ms"));
        if (loss >= 0)
            sb.Append(Inv($", loss {loss:0.#}%"));
        if (statesPerSecond >= 0)
            sb.Append(Inv($", {statesPerSecond:0.#} states/s"));
        sb.Append(Inv($", {snaps} snaps, {rewinds} rewinds"));
        if (silences > 0)
            sb.Append(Inv($", {silences} silence{(silences == 1 ? "" : "s")} over {SilenceLimit:0} s (longest {longestSilence:0.0} s)"));
        return sb.ToString();
    }

    private void FlushMate(double now, int slot, Mate m, Kind kind)
    {
        double seconds = Math.Max(0, now - (double.IsNaN(_periodStart) ? now : _periodStart));
        if (m.PingCount + m.JitterCount + m.TrafficSeconds > 0)
            Emit(MateLine(slot, m.Name, seconds, m.PingCount > 0 ? m.PingSum / m.PingCount : -1, m.PingMax,
                m.JitterCount > 0 ? m.JitterSum / m.JitterCount : -1, m.LossCount > 0 ? m.LossSum / m.LossCount : -1,
                m.TrafficSeconds > 0 ? m.States / m.TrafficSeconds : -1, m.Snaps, m.Rewinds, m.Silences, m.SilenceMax), kind);
        // A new period (kept: the name and whether a silence is still going on).
        m.PingSum = m.PingMax = m.JitterSum = m.LossSum = m.SilenceMax = m.TrafficSeconds = 0;
        m.PingCount = m.JitterCount = m.LossCount = m.Silences = m.States = m.Snaps = m.Rewinds = 0;
    }

    private Mate MateAt(int slot, string name)
    {
        if (!_mates.TryGetValue(slot, out var m))
            _mates[slot] = m = new Mate();
        if (!string.IsNullOrEmpty(name))
            m.Name = name;
        return m;
    }

    // ------------------------------------------------------------------ bounds

    /// <summary>The first <see cref="Burst"/> lines of a kind at once, then one per <see cref="KeySeconds"/> with how
    /// many were left out since.</summary>
    private void Keyed(double now, string key, string line)
    {
        if (!_keys.TryGetValue(key, out var k))
            _keys[key] = k = new KeyState();
        k.Total++;
        if (k.Written >= Burst && now - k.Last < KeySeconds)
        {
            k.Suppressed++;
            return;
        }
        if (k.Suppressed > 0)
            line += Inv($" (+{k.Suppressed} more like it since the last one)");
        k.Suppressed = 0;
        k.Written++;
        k.Last = now;
        Emit(line, Kind.Normal);
    }

    private void Emit(string line, Kind kind)
    {
        if (line.Length > MaxLineLength)
            line = line.Substring(0, MaxLineLength - 3) + "...";
        switch (kind)
        {
            case Kind.Normal when _normal < MaxLines:
                _normal++;
                _write(line);
                if (_normal == MaxLines)
                    _write(Inv($"{MaxLines} co-op lines written: from now on a summary every {QuietPeriod / 60:0} minutes, joins, leaves and the end"));
                return;
            case Kind.Summary when _summaries < MaxSummaryLines:
                _summaries++;
                _write(line);
                return;
            case Kind.Vital when _normal < MaxLines:
                _normal++;
                _write(line);
                return;
            case Kind.Vital when _vital < MaxVitalExtra:
                _vital++;
                _write(line);
                return;
        }
        LeftOut++;
        _leftOutSinceSummary++;
    }

    // ------------------------------------------------------------------ text

    public static string Explain(LeaveReason reason) => reason switch
    {
        LeaveReason.Goodbye => "left on purpose",
        LeaveReason.Timeout => "nothing heard for too long",
        LeaveReason.ConnectionLost => "the connection was lost",
        LeaveReason.ProtocolError => "sent data that made no sense",
        LeaveReason.Kicked => "kicked by the host",
        LeaveReason.HostLeft => "the host ended the session",
        LeaveReason.Rejected => "the host refused to let this game in",
        LeaveReason.LocalLeave => "this game left",
        _ => "unknown reason",
    };

    /// <summary>"[Coop] text" -> "text".</summary>
    private static string Strip(string text)
    {
        text ??= "";
        return text.StartsWith("[Coop] ", StringComparison.Ordinal) ? text.Substring(7) : text;
    }

    /// <summary>The kind of a line: its words before the first ':' (at most 40 characters), digits as '#'.</summary>
    public static string KeyOf(string text)
    {
        text ??= "";
        int colon = text.IndexOf(':');
        string head = colon > 0 ? text.Substring(0, colon) : text;
        if (head.Length > 40)
            head = head.Substring(0, 40);
        var sb = new StringBuilder(head.Length);
        foreach (char c in head)
            if (!char.IsDigit(c))
                sb.Append(c);
            else if (sb.Length == 0 || sb[sb.Length - 1] != '#')
                sb.Append('#');
        return sb.ToString().Trim();
    }

    private static string Top(IEnumerable<(string key, int count)> counts) =>
        string.Join(", ", counts.OrderByDescending(c => c.count).Take(4).Select(c => Inv($"{c.key} x{c.count}")));

    /// <summary>"42 s", "5 min", "1 h 5 min".</summary>
    public static string Span(double seconds)
    {
        int s = (int)Math.Round(Math.Max(0, seconds));
        if (s < 60)
            return Inv($"{s} s");
        int min = s / 60;
        return min < 60 ? Inv($"{min} min") : Inv($"{min / 60} h {min % 60} min");
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
