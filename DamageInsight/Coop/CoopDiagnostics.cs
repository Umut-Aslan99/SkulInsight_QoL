using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using DamageInsight.Coop.Net;
using DamageInsight.Crash;

namespace DamageInsight.Coop;

/// <summary>
/// The game side of <see cref="CoopDiagLog"/> (docs/coop/packages/LG.md): always on, writes the "co-op: " lines of
/// the session report (CrashReporter's timeline). Attaches itself to whatever session CoopRuntime has (launch
/// options, the Co-op window, matchmaking: no extra code per path), samples every teammate's connection twice a
/// second, and hears the mod's own "[Coop]" warnings and errors through a BepInEx listener. Its own clock and a lock:
/// BepInEx lines can come from any thread.
/// </summary>
internal sealed class CoopDiagnostics
{
    private const double SampleSeconds = 0.5;

    /// <summary>The runtime's instance (null before the runtime started): CoopWorld reports barrier waits to it.</summary>
    public static CoopDiagnostics Current { get; private set; }

    private static bool _listening;
    [ThreadStatic] private static bool _inside;

    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CoopDiagLog _log;
    private CoopSession _session;
    private string _endDetail = "";
    private double _nextSample;

    public CoopDiagnostics()
    {
        _log = new CoopDiagLog(line => CrashReporter.Note("co-op: " + line));
        Current = this;
        if (_listening)
            return;
        _listening = true;
        try { Logger.Listeners.Add(new Listener()); }
        catch (Exception e) { Plugin.Log.LogWarning("co-op diagnostics: no log listener: " + e.Message); }
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Once a frame, before the session's Update: attaches to a new session, samples the teammates.</summary>
    public void Tick(CoopSession session, ITransport transport)
    {
        if (CrashReporter.Report == null)
            return; // reports switched off: nothing would be written anyway
        lock (_lock)
        {
            if (!ReferenceEquals(session, _session))
                Attach(session, transport);
            if (_session == null || _session.State != SessionState.Active)
                return;
            double now = Now;
            if (now >= _nextSample)
            {
                _nextSample = now + SampleSeconds;
                foreach (var p in _session.Roster)
                {
                    if (p == null || p.Slot == _session.LocalSlot)
                        continue;
                    var q = _session.QualityOf(p.Slot);
                    _log.Sample(now, p.Slot, p.Name, _session.PingMs(p.Slot), _session.JitterMs(p.Slot), q.LossPercent,
                        _session.SilenceSeconds(p.Slot));
                }
            }
            _log.Tick(now);
        }
    }

    /// <summary>What came from a teammate in the runtime's report period (its 10 s log line).</summary>
    public void Traffic(int slot, int states, int snaps, int rewinds, double seconds)
    {
        lock (_lock)
            _log.Traffic(slot, states, snaps, rewinds, seconds);
    }

    /// <summary>The loading barrier let go after <paramref name="waitedSeconds"/> (long waits are written).</summary>
    public void Barrier(double waitedSeconds, bool timedOut)
    {
        lock (_lock)
            _log.Barrier(Now, waitedSeconds, timedOut);
    }

    private void Attach(CoopSession session, ITransport transport)
    {
        if (_session != null)
        {
            _session.Started -= OnStarted;
            _session.PlayerJoined -= OnJoined;
            _session.PlayerLeft -= OnLeft;
            _session.SessionEnded -= OnEnded;
            _session.Log -= OnSessionLine;
        }
        _session = session;
        _endDetail = "";
        _nextSample = 0;
        if (session == null)
            return;
        _transportName = TransportName(transport);
        session.Started += OnStarted;
        session.PlayerJoined += OnJoined;
        session.PlayerLeft += OnLeft;
        session.SessionEnded += OnEnded;
        session.Log += OnSessionLine;
        if (session.IsHost && session.State == SessionState.Active)
            OnStarted(); // a host's session is active from the start and never raises Started
    }

    private string _transportName = "";

    private void OnStarted()
    {
        lock (_lock)
        {
            var s = _session;
            if (s == null)
                return;
            var players = s.Roster.Where(p => p != null).Select(p => "P" + (p.Slot + 1) + " " + p.Name);
            string detailed = CoopTrace.On && CoopTrace.BasePath != null ? CoopTrace.BasePath + ".jsonl" : null;
            _log.SessionStarted(Now, s.IsHost ? "host" : "client", _transportName, MyPluginInfo.PLUGIN_VERSION,
                s.LocalPlayer?.Name ?? s.Options?.PlayerName ?? "", Math.Max(0, s.LocalSlot), players, detailed);
        }
    }

    private void OnJoined(RosterEntry p)
    {
        lock (_lock)
            _log.Joined(Now, p.Slot, p.Name);
    }

    private void OnLeft(RosterEntry p, LeaveReason why)
    {
        lock (_lock)
            _log.Left(Now, p.Slot, p.Name, why);
    }

    private void OnEnded(LeaveReason why)
    {
        lock (_lock)
            _log.SessionEnded(Now, why, _endDetail);
    }

    private void OnSessionLine(string text)
    {
        lock (_lock)
        {
            // "session ended: Timeout (nothing from the host for 20 s)" comes right before SessionEnded: keep its words.
            const string ended = "session ended: ";
            if (text != null && text.StartsWith(ended, StringComparison.Ordinal))
            {
                int open = text.IndexOf(" (", ended.Length, StringComparison.Ordinal);
                _endDetail = open > 0 && text.EndsWith(")", StringComparison.Ordinal) ? text.Substring(open + 2, text.Length - open - 3) : "";
            }
            _log.SessionLine(Now, text);
        }
    }

    /// <summary>"UDP", "Steam", "UDP + simulated lag".</summary>
    private static string TransportName(ITransport transport)
    {
        bool lag = false;
        while (transport is LagSimulator l)
        {
            lag = true;
            transport = l.Inner;
        }
        string name = transport switch
        {
            UdpTransport => "UDP",
            SteamTransport => "Steam",
            null => "nothing",
            _ => transport.GetType().Name,
        };
        return lag ? name + " + simulated lag" : name;
    }

    /// <summary>The mod's "[Coop] ..." warnings and errors (catch-ups, out of step, barrier timeouts, Fail()).</summary>
    private sealed class Listener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs e)
        {
            if ((e.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0 || _inside)
                return;
            var diag = Current;
            if (diag == null || e.Source?.SourceName != MyPluginInfo.PLUGIN_NAME || CrashReporter.Report == null)
                return;
            string text = Convert.ToString(e.Data, CultureInfo.InvariantCulture) ?? "";
            if (!text.StartsWith("[Coop] ", StringComparison.Ordinal))
                return;
            int nl = text.IndexOf('\n');
            if (nl >= 0)
                text = text.Substring(0, nl).TrimEnd('\r');
            _inside = true;
            try
            {
                lock (diag._lock)
                {
                    if ((e.Level & LogLevel.Warning) != 0)
                        diag._log.Warning(diag.Now, text);
                    else
                        diag._log.Error(diag.Now, text);
                }
            }
            catch (Exception)
            {
                // never log from here
            }
            finally
            {
                _inside = false;
            }
        }

        public void Dispose()
        {
        }
    }
}
