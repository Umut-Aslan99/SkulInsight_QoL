using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Testing;
using UnityEngine;

namespace DamageInsight.Coop.Match;

/// <summary>
/// Matchmaking in the running game (package MM): the ticket from this game (the daily run's player id, the Steam id and
/// name, versions, the mode, the Dark Mirror level, the DLC, the Steam ping location), the client's frame, and the
/// hand-off of a confirmed match to <see cref="CoopRuntime"/> (castle only; the host lets in only the matched players).
/// Talks to the server only with Coop/Matchmaking on (off until the player turns it on), or in the launcher's
/// matchmaking test (<c>--skulinsight-matchmaking=URL</c>: a local server, fake Steam ids, the UDP plan).
/// </summary>
internal sealed class CoopMatchmaking
{
    private readonly CoopRuntime _runtime;
    private readonly CoopLaunch _launch;
    private readonly List<(string kind, string detail, double at)> _early = new();
    private readonly UnityMatchHttp _http;
    private bool _testQueued, _statsBusy;
    private double _statsAt = double.NegativeInfinity;

    /// <summary>How often the open Co-op window asks for the queue's counts.</summary>
    public const double StatsSeconds = 20;

    /// <summary>The queue's counts as last heard (GET /api/coop/stats), or null (none yet, or the server is off).</summary>
    public QueueStats Stats { get; private set; }
    /// <summary>When <see cref="Stats"/> came (Time.realtimeSinceStartupAsDouble); a failed ask keeps the old ones.</summary>
    public double StatsHeard { get; private set; } = double.NegativeInfinity;

    /// <summary>The launcher's matchmaking test (two copies on one PC, one Steam account).</summary>
    public bool Test { get; }
    public MatchClient Client { get; }
    /// <summary>The last connection problem after a match (English, for the log); "" = none.</summary>
    public string ConnectProblem { get; private set; } = "";
    /// <summary>The partner's name of the last confirmed match (the window's state line).</summary>
    public string PartnerName { get; private set; } = "";

    public CoopMatchmaking(CoopRuntime runtime, CoopLaunch launch, bool test)
    {
        _runtime = runtime;
        _launch = launch;
        Test = test;
        _http = new UnityMatchHttp(runtime, () => Server);
        Client = new MatchClient(_http, () => Time.realtimeSinceStartupAsDouble,
            test ? _ => -1 : SteamPing.Estimate) { AutoAccept = test };
        Client.Log += text => Plugin.Log.LogInfo("[Coop] " + text);
        Client.Trace += (kind, detail) =>
        {
            if (CoopTrace.On)
                CoopTrace.Write(kind, what: detail);
            else if (_early.Count < 64)
                _early.Add((kind, detail, Time.realtimeSinceStartupAsDouble)); // before the session: written once the trace
                                                                              // starts, with its own time (amount: s since game start)
        };
        Client.Confirmed += OnConfirmed;
        if (!test && Plugin.CoopMatchmaking?.Value == true)
            SteamPing.WarmUp(); // Steam measures its relays now, so the first search sends a ping location
    }

    /// <summary>The server: the test's local one, else the daily run's (Daily/Server).</summary>
    public string Server => Test ? _launch.MatchServer : (Plugin.DailyServer?.Value ?? "").TrimEnd('/');

    /// <summary>The player turned matchmaking on (Coop/Matchmaking), or the test.</summary>
    public bool Enabled => Test || Plugin.CoopMatchmaking?.Value == true;

    /// <summary>The open Co-op window: the queue's counts every <see cref="StatsSeconds"/> (Umut 2026-11-23: players see
    /// who searches "even before queuing up"). [DECISION] Asked while the window is open, also with matchmaking off: the
    /// call sends nothing about the player (no id, no name, no Steam data), only the request itself.</summary>
    public void WantStats()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (_statsBusy || now - _statsAt < StatsSeconds || string.IsNullOrEmpty(Server))
            return;
        _statsBusy = true;
        _statsAt = now;
        _http.Send("GET", MatchClient.Api + "/stats", null, MatchClient.CallTimeout, (code, body) =>
        {
            _statsBusy = false;
            var stats = code == 200 ? QueueStats.Parse(body) : null;
            if (stats != null)
            {
                Stats = stats;
                StatsHeard = Time.realtimeSinceStartupAsDouble;
            }
            else if (code == 404)
                Stats = null; // the matchmaking server is off
        });
    }

    /// <summary>The trace started (the session is up): the match events from before it.</summary>
    public void FlushTrace()
    {
        foreach (var (kind, detail, at) in _early)
            CoopTrace.Write(kind, what: detail, amount: Math.Round(at, 1));
        _early.Clear();
    }

    /// <summary>The window's Start. Null = searching, else why not (shown to the player as is).</summary>
    public string Start()
    {
        if (CoopConflicts.Blocker is { } blocked)
        {
            UI.CoopConflictNotice.ShowNow();
            return blocked;
        }
        if (!Enabled)
            return Lang.Loc.T("Turn matchmaking on first.");
        if (!CoopRuntime.InCastle)
            return Lang.Loc.T("Hosting and joining work in the castle only.");
        if (_runtime.Session is { State: not SessionState.Ended })
            return Lang.Loc.T("Leave this game first.");
        if (!Client.CanStart)
            return null;
        var ticket = MakeTicket(out string problem);
        if (ticket == null)
            return problem;
        ConnectProblem = "";
        Client.Start(ticket);
        return null;
    }

    public void Cancel() => Client.Cancel();

    /// <summary>Every frame (CoopRuntime.Update).</summary>
    public void Tick()
    {
        Client.Tick();
        // The test: queue once the copy is in the castle and its save is its own.
        if (Test && !_testQueued && CoopRuntime.InCastle && SaveIsolation.IsSafe && Client.State == MatchState.Idle)
        {
            _testQueued = true;
            string why = Start();
            if (why != null)
            {
                _testQueued = false;
                Plugin.Log.LogWarning("[Coop] matchmaking test: not queued: " + why);
            }
        }
        // The test queues again after a failed search (the other copy may have started later).
        if (Test && _testQueued && Client.State is MatchState.Failed or MatchState.Cancelled or MatchState.ServerOff
            && _runtime.Session == null)
        {
            _testQueued = false;
            Client.Clear();
        }
    }

    /// <summary>The session is up (a client in, or this client let in): the server is told "done".</summary>
    public void OnSessionUp() => Client.Finish();

    private MatchTicket MakeTicket(out string problem)
    {
        problem = null;
        string steamId, name;
        if (Test)
        {
            // Two copies share one Steam account: a fake but unique SteamID64 per copy (the server's pattern 7656119 + 10
            // digits), the same number as this copy's session player id (the host admits by it).
            steamId = TestSteamId(_launch.Instance).ToString(CultureInfo.InvariantCulture);
            name = string.IsNullOrEmpty(_launch.Name) ? "Copy" + _launch.Instance : _launch.Name;
        }
        else
        {
            var lobby = _runtime.Lobby;
            ulong id = lobby?.OwnId ?? 0;
            if (id == 0)
            {
                problem = Lang.Loc.T("Steam is not running: co-op needs Skul started from Steam.");
                return null;
            }
            steamId = id.ToString(CultureInfo.InvariantCulture);
            name = lobby.OwnName;
        }
        string mode = Plugin.CoopMatchMode?.Value is "normal" or "darkmirror" or "any" ? Plugin.CoopMatchMode.Value : "any";
        int unlocked = DarkMirrorUnlocked();
        if (mode == "darkmirror" && unlocked < 0 && !(Plugin.CoopCanHost?.Value == false))
            mode = "any"; // REVIEW(fable): nobody without the Dark Mirror can host it; "any" still finds a normal partner
        string player = Daily.DailyService.State?.PlayerId ?? "";
        if (Test)
            player = "coop-test-copy-" + _launch.Instance; // the copies' daily ids may be the same file
        return new MatchTicket
        {
            Player = player,
            SteamId = steamId,
            Name = name ?? "",
            Mod = MyPluginInfo.PLUGIN_VERSION,
            ProtocolVersion = Protocol.Version,
            ModListHash = Protocol.HashModList(CoopRuntime.ModList()),
            Mode = Test ? "normal" : mode,
            DmMin = 0,
            DmMax = 10,
            DmUnlocked = unlocked,
            Dlc = Daily.DailyRun.OwnsDlc,
            DlcRun = "any",
            Size = Protocol.MatchmadePlayers,
            // Opt-in (COOP_LOG 62): the server insists on the same language for a ticket's first 60 s; Umut (en) and
            // Kerkopi (de) queued 4 times, each restarting those 60 s, and never matched (2026-11-20).
            Language = Plugin.CoopMatchSameLanguage?.Value == true ? Lang.Loc.Current : "",
            // The test: always a match (the copies' ping is unknown: no Steam location); the host is the launcher's host.
            MaxPing = Test ? 1000 : Plugin.CoopMaxPing?.Value ?? 200,
            CanHost = Test ? _launch.Role != CoopRole.Join : Plugin.CoopCanHost?.Value != false,
            PingLocation = Test ? "" : SteamPing.LocalLocation(),
        };
    }

    /// <summary>The fake Steam id of a test copy: 76561190000000000 + its instance (never a real account: those start at
    /// 76561197960265728).</summary>
    public static ulong TestSteamId(int instance) => 76561190000000000UL + (ulong)Math.Max(0, instance);

    /// <summary>The highest Dark Mirror level this save may play: one above the highest cleared, -1 = none.</summary>
    // REVIEW(fable): "unlocked" read as clearedLevel + 1 (clearedLevel -1 = none cleared -> level 0); is level 0 open
    // before the Dark Mirror itself is unlocked in the castle?
    private static int DarkMirrorUnlocked()
    {
        try
        {
            int cleared = Data.GameData.HardmodeProgress.clearedLevel;
            return Math.Max(-1, Math.Min(10, cleared + 1));
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private void OnConfirmed(MatchInfo match)
    {
        PartnerName = string.Join(", ", match.Partners.Select(p => p.Name));
        string why = _runtime.ConnectMatch(match, Test);
        if (why == null)
            return;
        ConnectProblem = why;
        Plugin.Log.LogWarning("[Coop] matchmaking: the match did not connect: " + why);
        Client.Finish(); // the server forgets it; the player can search again
    }
}
