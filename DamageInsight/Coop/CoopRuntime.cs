using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Sync;
using DamageInsight.Coop.Testing;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Co-op in the running game, phase 0 (docs/COOP_PLAN.md §10: "two copies see each other run around the castle
/// smoothly at a simulated 200 ms"). One session per game: the transport from the start options (UDP between copies
/// on this PC, wrapped in the lag simulator when asked), the session (hello, roster, ping), this game's player sent
/// about 30 times a second, and every teammate drawn from a snapshot buffer a little in the past (package C).
/// Every 10 seconds the log gets a line per teammate: ping, buffer delay, how many frames were drawn smoothly.
/// Only the test launcher starts it for now (CoopStartPlan); Steam invites come with phase 1.
/// </summary>
internal sealed class CoopRuntime : MonoBehaviour
{
    public const float SendRate = 30f;
    private const float ReportSeconds = 10f;
    private const float RejoinSeconds = 3f;

    public static CoopRuntime Instance { get; private set; }

    private CoopStartPlan _plan;
    private SessionOptions _options;
    private ITransport _transport;
    private CoopSession _session;
    private readonly NetWriter _writer = new(64);
    private readonly Dictionary<int, Remote> _remotes = new();
    private LocalBot _bot;
    private double _nextSend, _nextReport, _rejoinAt = -1, _waitingSince = -1;
    private Vector2 _lastSentPosition;
    private double _lastSentTime;
    private readonly TeammateTags _tags = new();
    private readonly CoopDiagnostics _diag = new(); // the "co-op: " lines of the session report (LG.md)
    private CoopWorld _world;
    private EnemySync _enemies;
    private SharedScenes _scenes;
    private CoopRevive _revive;
    private SharedLoot _loot;
    private CoopAutoRun _autoRun;
    private Testing.BotItems _botItems;
    private Testing.BotEssences _botEssences;
    private CoopHealthScaling _health;
    private CoopTimeEffects _timeEffects;
    private CoopHostRules _hostRules;
    private CoopPlayerNotes _playerNotes;
    private CoopDamageBoard _damageBoard;
    private HostWorld _hostWorld;
    private ConnectionBox _connection;
    // This game's player as sent: its loadout (checked a few times a second) and the player whose moves are followed.
    private Loadout _sentLoadout;
    private double _nextLoadoutCheck;
    private Characters.Character _followed;
    private bool _quitting, _saveWarned;
    // Steam co-op (the Co-op window, invites): the lobby, and whether the running session was started at runtime
    // (StartHost / StartJoin) instead of from the launch options.
    private SteamLobby _lobby;
    private bool _runtimeSession, _steamWarmed, _runInBackgroundBefore;
    // Matchmaking (package MM): the client, the matched players a host lets in (null = anyone), whether they are
    // checked by the Steam connection (true) or by the player id of the Hello (the UDP test), and the test's state.
    private Match.CoopMatchmaking _matchmaking;
    private HashSet<ulong> _matchAdmit;
    private bool _matchAdmitBySteam, _matchSession, _matchTestConfirmed;
    // A broken line (package DC): the rules, the lines on screen, whether the session was in the party (a client:
    // welcomed), the Steam host to join again, and the drop test's outage (one object for every try's transport).
    private readonly Rules.DropWatch _drops = new();
    private readonly List<Rules.DropLine> _dropLines = new();
    private readonly System.Text.StringBuilder _dropText = new();
    private bool _wasActive, _goingSolo;
    private ulong _steamHost;
    private Outage _outage;

    /// <summary>One teammate as this game sees them.</summary>
    private sealed class Remote
    {
        public int Slot;
        public string Name = "";
        public readonly AdaptiveDelay Delay = AdaptiveDelay.ForInternet(1f / SendRate);
        public readonly SnapshotBuffer<PlayerState> Buffer = new();
        public uint MapKey;
        public RemoteAvatar Avatar;
        public readonly TimedEvents<TeammateEvent> Events = new();
        public Loadout Loadout;
        /// <summary>The teammate's follower (TeammateFollowers), "" none.</summary>
        public string Follower = "";
        public float Health, MaxHealth;
        public double LastRender = double.NegativeInfinity;
        public double LastHeard = Time.realtimeSinceStartupAsDouble; // a state came (a client's view of another client)
        // Counted since the last report line.
        public int Received, Interpolated, Extrapolated, Held, Waiting, Snaps, Rewinds, Moves;
    }

    private static double Now => Time.realtimeSinceStartupAsDouble;

    /// <summary>Test tools (the full skull sweep's turns): the session clock (the host's time), or null while there is
    /// no session or a client's clock is not synced to the host's yet.</summary>
    internal static double? SessionClock => Instance?._session is { } session && session.ClockSynced ? session.ToSessionTime(Now) : null;

    /// <summary>Called once from Plugin.Awake: starts co-op when the start options ask for it.</summary>
    internal static void Begin(GameObject host)
    {
        var launch = CoopLaunch.Current;
        var plan = CoopStartPlan.From(launch);
        if (launch.FromLauncher || plan.Starts)
            Plugin.Log.LogInfo("[Coop] start: " + plan);
        if (plan.Starts)
        {
            host.AddComponent<CoopRuntime>().Init(plan, launch);
            if (launch.WindowTest)
            {
                host.AddComponent<UI.CoopWindow>();
                host.AddComponent<Testing.CoopWindowTest>();
            }
            if (launch.MatchServer != null)
                host.AddComponent<UI.MatchFoundBox>(); // the matchmaking test: the box shows (the bot accepts)
            return;
        }
        // Steam co-op (the Co-op window, invites, +connect_lobby): only with the co-op switch on, which also makes the
        // preloader rewrite the player reads at the next start. [DECISION] Not in test copies: they share one Steam
        // account (and their launch options start UDP sessions).
        if (Plugin.CoopEnabled?.Value != true || launch.IsTestCopy)
            return;
        if (TestBuild.IsTestBuild)
        {
            // a private test build for one person (TestBuild): its line on screen; after its end no co-op at all
            host.AddComponent<TestBuildMark>();
            if (TestBuild.Expired)
            {
                Plugin.Log.LogWarning($"[Coop] this private test build (for {TestBuild.For}) has ended: co-op stays off");
                return;
            }
        }
        host.AddComponent<CoopRuntime>().InitSteam();
        host.AddComponent<UI.CoopWindow>();
        host.AddComponent<UI.MatchFoundBox>();
    }

    private void Init(CoopStartPlan plan, CoopLaunch launch)
    {
        Instance = this;
        _plan = plan;
        // Test copies sit side by side: the window without focus must keep running (and sending).
        Application.runInBackground = true;
        _options = new SessionOptions
        {
            PlayerName = string.IsNullOrEmpty(launch.Name) ? "Copy " + launch.Instance : launch.Name,
            // Copies share one Steam account: an id per game start keeps them apart (the session refuses duplicates).
            PlayerId = unchecked((ulong)DateTime.UtcNow.Ticks * 8 + (ulong)(launch.Instance & 7)),
            ModVersion = MyPluginInfo.PLUGIN_VERSION,
            ModListHash = Protocol.HashModList(ModList()),
        };
        if (launch.MatchServer != null)
        {
            // The matchmaking test: no session until the local server confirmed a match; the player id is the copy's fake
            // Steam id (the host lets in only the matched ids).
            _matchmaking = new Match.CoopMatchmaking(this, launch, test: true);
            _options.PlayerId = Match.CoopMatchmaking.TestSteamId(launch.Instance);
            Plugin.Log.LogInfo("[Coop] matchmaking test: queues at " + launch.MatchServer + " in the castle");
        }
        if (launch.Bot)
        {
            _bot = new LocalBot(launch.Instance, launch.BotSkulls);
            _botItems = Testing.BotItems.From(launch.BotItems);
            _botEssences = Testing.BotEssences.From(launch.BotItems);
        }
        if (launch.AutoRun)
            _autoRun = new CoopAutoRun(this, launch);
        CoopRevive.TestDownAfter = launch.AutoRunDown;
        if (launch.Mute)
            AudioListener.volume = 0f; // the game never sets it (its volumes go through mixers)
        _outage = plan.Drop;
        if (launch.FromLauncher)
            CoopShots.Start(this, launch);
        if (PlayerReadsBridge.Reads == null)
            Plugin.Log.LogWarning("[Coop] the preloader is not active (" + PlayerReadsBridge.Status + "): enemies aim at this " +
                                  "game's player and teammates' gear acts for it. Co-op needs a restart with co-op on.");
        if (!plan.NeedsSaveIsolation)
            StartSession();
    }

    /// <summary>No session at the start: the Co-op window or a Steam invite starts one (<see cref="StartHost"/>,
    /// <see cref="StartJoin"/>). A +connect_lobby start option (Steam started Skul from an invite) joins that lobby
    /// once Steam is up and this game is in the castle.</summary>
    private void InitSteam()
    {
        Instance = this;
        _plan = new CoopStartPlan { Reason = "Steam: from the Co-op window or an invite" };
        string[] args;
        try { args = Environment.GetCommandLineArgs(); }
        catch (Exception) { args = Array.Empty<string>(); }
        ulong startup = SteamLobby.ParseConnectLobby(args);
        _lobby = new SteamLobby(new LobbySessions(this), MyPluginInfo.PLUGIN_VERSION, SteamworksLobby.TryCreate, () => Now, startup);
        _lobby.Log += text => Plugin.Log.LogInfo("[Coop] " + text);
        _matchmaking = new Match.CoopMatchmaking(this, CoopLaunch.Current, test: false);
        ApplyLobbySettings();
        Plugin.Log.LogInfo(startup != 0
            ? FormattableString.Invariant($"[Coop] Steam co-op ready; started from an invite: joins lobby {startup} in the castle")
            : "[Coop] Steam co-op ready: host or join from the Co-op window (pause menu) or a Steam invite");
    }

    // ------------------------------------------------------------------ Steam co-op at runtime (the Co-op window)

    /// <summary>The Steam lobby (null in launch-option sessions and without the co-op switch).</summary>
    internal SteamLobby Lobby => _lobby;

    /// <summary>The running session, or null.</summary>
    internal CoopSession Session => _session;

    /// <summary>Matchmaking (null in launch-option sessions other than the matchmaking test).</summary>
    internal Match.CoopMatchmaking Matchmaking => _matchmaking;

    /// <summary>The running session came from matchmaking (the window's state line).</summary>
    internal bool InMatchSession => _matchSession && _session != null && _session.State != SessionState.Ended;

    /// <summary>How the last matchmaking session ended (the window says why), or null.</summary>
    internal (LeaveReason why, RejectReason rejection, string hostVersion)? LastMatchEnd { get; private set; }

    /// <summary>A confirmed match (package MM): the host opens a session that lets in only the matched players, the
    /// others join the host. Castle only. The test (two copies, one Steam account) uses the launcher's UDP plan. Null =
    /// started, else why not (English, for the log).</summary>
    // REVIEW(fable): [DECISION] no Steam lobby for a match (COOP_PLAN 7.5): a direct SteamTransport host / join; the
    // host admits by the Steam connection's identity (peer = SteamID64), the test by the Hello's player id.
    internal string ConnectMatch(Match.MatchInfo match, bool test)
    {
        var partners = new HashSet<ulong>();
        foreach (var partner in match.Partners)
            if (ulong.TryParse(partner.SteamId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong id) && id != 0)
                partners.Add(id);
        LastMatchEnd = null;
        if (test)
        {
            if (!_plan.Starts)
                return "no launcher plan";
            if (match.YouHost != (_plan.Kind == CoopStartKind.Host))
                Plugin.Log.LogWarning($"[Coop] matchmaking test: the server picked this copy as {(match.YouHost ? "host" : "client")}, the launcher as {_plan.Kind}");
            _plan.Kind = match.YouHost ? CoopStartKind.Host : CoopStartKind.Join;
            _matchAdmit = match.YouHost ? partners : null;
            _matchAdmitBySteam = false;
            _matchTestConfirmed = true;
            _matchSession = true;
            CloseTransport();
            StartSession();
            return null;
        }
        if (!InCastle)
            return "co-op starts in the castle only";
        string why;
        if (match.YouHost)
        {
            if (partners.Count == 0)
                return "the server sent no partner";
            _matchAdmit = partners;
            _matchAdmitBySteam = true;
            why = StartHost(SteamTransport.Host(0, Math.Max(1, match.Size - 1)), Math.Max(2, match.Size));
        }
        else
        {
            if (!ulong.TryParse(match.HostSteamId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong host) || host == 0)
                return "the server sent no host";
            _matchAdmit = null;
            ApplyLobbySettings();
            _steamHost = host; // to join again after a broken line (package DC's Retry)
            var transport = SteamTransport.Join(host);
            why = StartJoin(transport, transport.HostPeer);
        }
        if (why != null)
            _matchAdmit = null;
        else
            _matchSession = true;
        return why;
    }

    /// <summary>The window's Leave for a matchmaking session (it has no Steam lobby).</summary>
    internal void LeaveMatch()
    {
        if (InMatchSession)
            Stop();
    }

    /// <summary>Hosting and joining happen in the castle only [DECISION: joining mid-run isn't possible yet].</summary>
    internal static bool InCastle
    {
        get
        {
            var type = Singleton<Service>.Instance?.levelManager?.currentChapter?.type;
            return type == Chapter.Type.Castle || type == Chapter.Type.HardmodeCastle;
        }
    }

    /// <summary>The lobby's settings and the rich presence text (in this player's language) before it acts.</summary>
    private void ApplyLobbySettings()
    {
        if (_lobby == null)
            return;
        _lobby.FriendsCanJoin = Plugin.CoopFriendsCanJoin?.Value ?? true;
        // 3-4 players in a Steam lobby: experimental since 0.17.2 (COOP_LOG 70); matchmaking keeps 2 (Protocol.MatchmadePlayers).
        _lobby.MaxPlayers = Math.Max(2, Math.Min(Protocol.MaxPlayers, Plugin.CoopMaxPlayers?.Value ?? Protocol.MaxPlayers));
        _lobby.PresenceStatus = Lang.Loc.T("In a co-op run");
    }

    /// <summary>The window's Host: a Steam lobby, then a host session (<see cref="StartHost"/>).</summary>
    internal void HostSteam()
    {
        ApplyLobbySettings();
        _lobby?.Host();
    }

    /// <summary>Opens a session with this transport as its host, at runtime (the same session code as the launch
    /// options: handshake, mod checks, rejects). Null = started, else why not (English, for the log). The transport
    /// is disposed when the start fails.</summary>
    internal string StartHost(ITransport transport, int maxPlayers = Protocol.MaxPlayers) =>
        StartAtRuntime(transport, PeerId.None, maxPlayers);

    /// <summary>Opens a client session to the host over this transport, at runtime. Null = started, else why not.</summary>
    internal string StartJoin(ITransport transport, PeerId host) => StartAtRuntime(transport, host, Protocol.MaxPlayers);

    /// <summary>Leaves the session started at runtime (goodbye to the others); the transport closes next frame.</summary>
    internal void Stop()
    {
        if (!_runtimeSession || _session == null)
            return;
        try
        {
            _session.Leave(); // SessionEnded -> OnSessionEnded tears the parts down
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] leaving: " + e.Message);
        }
    }

    private string StartAtRuntime(ITransport transport, PeerId host, int maxPlayers, bool rejoin = false)
    {
        if (!TestBuild.MayStart) // private test builds only (TestBuild): English on purpose
            return TestBuild.Expired ? "This test build has ended." : "This test build starts co-op once it reached the internet (skul.umut-aslan.dev).";
        string why = _plan.Starts ? "a test session from the launch options runs"
            : _session != null && _session.State != SessionState.Ended ? "a session runs already"
            : !InCastle && !rejoin ? "co-op starts in the castle only" // a rejoin after a broken line also mid-run (package DC)
            : transport is SteamTransport { Self.IsNone: true } ? "Steam is not running"
            : transport is SteamTransport { Failed: { } failed } ? failed
            : null;
        if (why != null)
        {
            try { transport?.Dispose(); }
            catch (Exception) { }
            Plugin.Log.LogWarning("[Coop] the session did not start: " + why);
            return why;
        }
        CloseTransport(); // the last session's, if it is still open
        _closeEnded = false; // (that was it: the next frame must not close the new one)
        _options = new SessionOptions
        {
            PlayerName = _lobby?.OwnName is { Length: > 0 } name ? name : "Player",
            // The Steam id: the same every time, so a host's kick-and-ban holds.
            PlayerId = _lobby?.OwnId is { } id && id != 0 ? id : transport.Self.Value,
            ModVersion = MyPluginInfo.PLUGIN_VERSION,
            ModListHash = Protocol.HashModList(ModList()),
            MaxPlayers = Math.Max(2, Math.Min(Protocol.MaxPlayers, maxPlayers)),
        };
        try
        {
            Open(transport, host);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[Coop] could not start the session: " + e);
            CloseTransport();
            try { transport.Dispose(); }
            catch (Exception) { }
            return "the session could not start (" + e.GetType().Name + ")";
        }
        // The game must keep running (and sending) when its window loses focus; given back when the session ends
        // (a rejoin try keeps the first session's).
        if (!_runtimeSession)
            _runInBackgroundBefore = Application.runInBackground;
        _runtimeSession = true;
        Application.runInBackground = true;
        return null;
    }

    /// <summary>The runtime's side of the lobby: castle check, player count, start / stop over Steam.</summary>
    private sealed class LobbySessions : ILobbySessions
    {
        private readonly CoopRuntime _runtime;

        public LobbySessions(CoopRuntime runtime) => _runtime = runtime;

        public bool InCastle => CoopRuntime.InCastle;

        public int PlayerCount => _runtime._session is { State: SessionState.Active } s ? s.Roster.Count : 0;

        // REVIEW(fable): the host's Steam transport takes maxPlayers - 1 clients; the session's own MaxPlayers refuses more (Full).
        public string StartHost(int maxPlayers)
        {
            _runtime._drops.Reset();
            return _runtime.StartHost(SteamTransport.Host(0, maxPlayers - 1), maxPlayers);
        }

        public string StartJoin(ulong hostSteamId)
        {
            _runtime.ApplyLobbySettings(); // the rich presence text in this player's language
            _runtime._drops.Reset();
            _runtime._steamHost = hostSteamId; // to join again after a broken line (package DC)
            var transport = SteamTransport.Join(hostSteamId);
            return _runtime.StartJoin(transport, transport.HostPeer);
        }

        public void StopSession() => _runtime.Stop();
    }

    /// <summary>A test copy opens its session once its save is its own (SaveIsolation); a failed redirect keeps it
    /// closed for good (the copy would play on the real save).</summary>
    private bool WaitForSave(double now)
    {
        if (SaveIsolation.IsSafe)
        {
            if (_waitingSince >= 0)
                Plugin.Log.LogInfo($"[Coop] the test save is redirected after {now - _waitingSince:0.0} s: starting");
            _plan.NeedsSaveIsolation = false;
            StartSession();
            return false;
        }
        if (_waitingSince < 0)
            _waitingSince = now;
        else if (!_saveWarned && now - _waitingSince > 60)
        {
            _saveWarned = true;
            Plugin.Log.LogWarning("[Coop] co-op stays off: the test copy's save is not isolated (" + SaveIsolation.Problem + ")");
        }
        return true;
    }

    internal static IEnumerable<string> ModList()
    {
        var list = new List<string>();
        try
        {
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                if (info.Metadata != null)
                    list.Add(info.Metadata.GUID + "@" + info.Metadata.Version);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] the mod list could not be read: " + e.Message);
        }
        return list;
    }

    private void StartSession()
    {
        _rejoinAt = -1;
        if (_matchmaking is { Test: true } && !_matchTestConfirmed)
            return; // the matchmaking test opens its session when the match is confirmed (ConnectMatch)
        try
        {
            var udp = new UdpTransportOptions { Port = _plan.Port };
            ITransport transport = _plan.Kind == CoopStartKind.Host ? UdpTransport.Host(udp) : UdpTransport.Join(udp);
            if (_plan.Lag != null || _outage != null)
            {
                // on the game's clock: the drop test's outage is armed with it and outlives this transport
                transport = new LagSimulator(transport, _plan.Lag ?? new LagSettings(), seed: CoopLaunch.Current.Instance, clock: () => Now)
                {
                    Outage = _outage,
                };
            }
            Open(transport, _plan.Kind == CoopStartKind.Host ? PeerId.None : UdpTransport.HostPeer);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[Coop] could not start the session: " + e);
            CloseTransport();
            if (_plan.Kind == CoopStartKind.Join)
                _rejoinAt = Now + RejoinSeconds;
        }
    }

    /// <summary>The session over this transport (host when <paramref name="host"/> is None, else a client of it) and
    /// every part of co-op on it. The launch options' sessions and the window's (Steam) go through here alike.</summary>
    private void Open(ITransport transport, PeerId host)
    {
        if (!_saveBackedUp && !CoopLaunch.Current.IsTestCopy)
        {
            // [DECISION] 2026-11-16: until the co-op slot of COOP_PLAN 4.9 exists, a co-op run plays in the player's
            // own save; the first session of each game start backs that save up first (a co-op bug can't cost it).
            _saveBackedUp = true;
            SaveSlots.SlotSwitcher.BackupGameSave();
        }
        _transport = transport;
        if (!host.IsNone)
            LogRelay("joining");
        // A broken line (package DC): the drop after DropSeconds is the session's timeout; a join try that gets no
        // answer ends sooner (the next try follows); the host keeps a dropped player's slot for the rejoin window.
        var drop = DropSettingsNow();
        _drops.Apply(drop);
        _options.TimeoutSeconds = _drops.Settings.DropSeconds;
        _options.HandshakeTimeoutSeconds = HandshakeSeconds;
        _options.RejoinWindowSeconds = _drops.Settings.RejoinWindowSeconds;
        _session = host.IsNone
            ? CoopSession.Host(transport, _options, Now)
            : CoopSession.Join(transport, host, _options, Now);
        _wasActive = _session.IsHost;
        _session.Log += text => Plugin.Log.LogInfo("[Coop] " + text);
        if (_session.IsHost && _matchAdmit != null)
        {
            // A matchmaking host lets in only the matched players (others: Reject NotAccepting).
            var admit = _matchAdmit;
            bool bySteam = _matchAdmitBySteam;
            _session.Admit = (peer, playerId) => admit.Contains(bySteam ? peer.Value : playerId);
        }
        _session.Started += () =>
        {
            Plugin.Log.LogInfo($"[Coop] session started: slot {_session.LocalSlot} of {_session.Roster.Count} ({(_session.IsHost ? "host" : "client")})");
            _wasActive = true;
            _drops.Reconnected(Now); // a try after a broken line made it (no-op otherwise)
            StartTrace();
            if (!_session.IsHost)
                _matchmaking?.OnSessionUp(); // matchmaking: connected, the server is told "done"
        };
        if (_session.IsHost)
            StartTrace(); // a host's session is active from the start (Started may have fired already)
        _session.PlayerJoined += p =>
        {
            Plugin.Log.LogInfo($"[Coop] {p.Name} joined (slot {p.Slot})");
            _sentLoadout = null; // the newcomer needs this player's gear
            if (_session.IsHost)
                _matchmaking?.OnSessionUp();
            _drops.Joined(p.Slot, p.Name, p.PlayerId, Now);
        };
        _session.PlayerLeft += (p, why) =>
        {
            Plugin.Log.LogInfo($"[Coop] {p.Name} left (slot {p.Slot}): {why}");
            if (why is LeaveReason.Timeout or LeaveReason.ConnectionLost)
                LogRelay("lost slot " + p.Slot);
            Forget(p.Slot);
            _drops.Left(p.Slot, p.Name, p.PlayerId, why, Now);
        };
        _session.SessionEnded += OnSessionEnded;
        _world?.Dispose();
        _world = new CoopWorld(_session, this, _tags, rejoin: _drops.Reconnecting);
        _enemies?.Dispose();
        _enemies = new EnemySync(_session, AvatarOf, SlotOf, PlayerOf);
        _scenes?.Dispose();
        _scenes = new SharedScenes(_session);
        _hostRules?.Dispose();
        _hostRules = new CoopHostRules(_session);
        _playerNotes?.Dispose();
        _playerNotes = new CoopPlayerNotes(_session, _hostRules);
        _damageBoard?.Dispose();
        _damageBoard = new CoopDamageBoard(_session);
        _hostWorld?.Dispose();
        _hostWorld = new HostWorld(_session);
        _connection?.Destroy();
        _connection = new ConnectionBox(_session, _tags);
        _revive?.Dispose();
        _revive = new CoopRevive(_session, PlayerOf, SlotOf, _tags, _hostRules.Revive);
        _loot?.Dispose();
        _loot = new SharedLoot(_session, PlayerOf);
        _health?.Dispose();
        _health = new CoopHealthScaling(_session, _hostRules.Balance);
        _timeEffects?.Dispose();
        _timeEffects = new CoopTimeEffects(_session);
        _session.RegisterHandler(MessageType.PlayerState, OnPlayerState);
        _session.RegisterHandler(MessageType.TimedEvent, OnTeammateEvent);
        if (_session.IsHost)
        {
            // Clients see each other through the host (3-4 players).
            _session.SetRelay(MessageType.PlayerState);
            _session.SetRelay(MessageType.TimedEvent);
        }
        Plugin.Log.LogInfo($"[Coop] {(_session.IsHost ? "hosting" : "joining")} as {_options.PlayerName}: " +
                           (_plan.Starts ? _plan.ToString() : $"{transport.GetType().Name}, self {transport.Self.Value}, host {(host.IsNone ? "this game" : host.Value.ToString())}"));
    }

    private void StartTrace()
    {
        var session = _session;
        if (session == null)
            return;
        if (CoopTrace.On)
        {
            // matchmaking: the first session (queued as a possible host) may not be the role this game plays
            string side = session.IsHost ? "host" : "client";
            CoopTrace.SwitchSide(side);
            CoopTrace.Write("session", what: side, slot: session.LocalSlot);
            LogRelay("session start");
            return;
        }
        // The runtime's current session, not this one: after a rejoin the trace goes on in the new session's time
        // (T.md Open 11; the probe below already read the field).
        CoopTrace.Start(session.IsHost ? "host" : "client", () => _session != null ? _session.ToSessionTime(Now) : Now);
        CoopTrace.Write("session", what: session.IsHost ? "host" : "client", slot: session.LocalSlot);
        _matchmaking?.FlushTrace(); // the match-* events from before the session
        CoopProbe.Start(local => _session != null ? _session.ToSessionTime(local) : local);
        LogRelay("session start");
    }

    /// <summary>Steam's relay network status to the log and the trace (relay-status) in a Steam session: at its start,
    /// when joining, and when a line breaks (COOP_LOG 70: "Steam found no route" from mainland China).</summary>
    private void LogRelay(string when)
    {
        if (_transport is not SteamTransport)
            return;
        string text = SteamRelay.Describe();
        if (text == null)
            return;
        Plugin.Log.LogInfo("[Coop] Steam " + text + " (" + when + ")");
        CoopTrace.Write("relay-status", name: when, what: text);
    }

    private void OnSessionEnded(LeaveReason why)
    {
        Plugin.Log.LogInfo("[Coop] session ended: " + why);
        if (why is LeaveReason.Timeout or LeaveReason.ConnectionLost)
            LogRelay("session lost");
        var ended = _session;
        bool wasActive = _wasActive;
        _wasActive = false;
        TearDown();
        if (ended != null && !ended.IsHost && !_quitting && !_goingSolo)
        {
            // Package DC: a client that lost its host tries again (the parts are gone, the room's puppets wait as
            // they are); the host ending the session, or this game leaving, means playing on alone at once.
            _drops.HostLost(ended.PlayerAt(0)?.Name ?? "", why, wasActive, Now);
            if (_drops.Reconnecting || _drops.IsSolo)
            {
                _closeEnded = true; // the transport closes next frame (this runs inside its Poll)
                _rejoinAt = -1;
                return; // TickDrops: the next try, or playing on alone
            }
        }
        if (_goingSolo)
        {
            _closeEnded = true;
            return; // PlayOnAlone does the rest
        }
        if (_runtimeSession)
        {
            // [DECISION] A session from the window that ended on purpose (a goodbye, a kick, a reject; a broken line
            // retries above): the lobby is left and the window says why; the player joins again through the invite or
            // the friends list. The transport closes next frame (this runs inside the session's Update, inside the
            // transport's Poll).
            EndRuntimeSession(why, ended);
            return;
        }
        // A copy that started before its host (or lost it) tries again: phase 0 tests restart copies freely.
        if (!_quitting && _plan.Kind == CoopStartKind.Join && why != LeaveReason.Rejected && why != LeaveReason.Kicked)
            _rejoinAt = Now + RejoinSeconds;
    }

    /// <summary>A session from the window is over for good: the lobby hears why, the transport closes next frame.</summary>
    private void EndRuntimeSession(LeaveReason why, CoopSession session)
    {
        _runtimeSession = false;
        _closeEnded = true;
        Application.runInBackground = _runInBackgroundBefore;
        if (_matchSession)
        {
            // A matchmaking session (package MM) has no lobby: the window shows why it ended from here.
            _matchSession = false;
            _matchAdmit = null;
            LastMatchEnd = (why, session?.Rejection ?? RejectReason.None, session?.HostModVersion ?? "");
            CoopTrace.Write("match-session-ended", what: why.ToString());
            // A match that ended before its session came up (refused, no answer): the server forgets the ticket now
            // and the player can search again at once, not after the 30 s "no session came up" wait (MM.md Left 3).
            _matchmaking?.Client.Finish();
            return;
        }
        try { _lobby?.OnSessionEnded(why, session?.Rejection ?? RejectReason.None, session?.HostModVersion ?? ""); }
        catch (Exception e) { Fail("the Steam lobby", e); }
    }

    /// <summary>Every part of co-op on the session goes (the session itself is ended or about to be).</summary>
    private void TearDown()
    {
        _enemies?.Dispose();
        _enemies = null;
        _scenes?.Dispose();
        _scenes = null;
        _revive?.Dispose();
        _revive = null;
        _loot?.Dispose();
        _loot = null;
        _health?.Dispose();
        _health = null;
        _timeEffects?.Dispose();
        _timeEffects = null;
        _playerNotes?.Dispose();
        _playerNotes = null;
        _hostRules?.Dispose();
        _hostRules = null;
        _damageBoard?.Dispose();
        _damageBoard = null;
        _hostWorld?.Dispose();
        _hostWorld = null;
        _connection?.Destroy();
        _connection = null;
        _world?.Dispose();
        _world = null;
        foreach (int slot in _remotes.Keys.ToList())
            Forget(slot);
        _tags.Banner("");
        _tags.Notice("");
    }

    private bool _closeEnded;
    private static bool _saveBackedUp;

    // ------------------------------------------------------------------ a broken line (package DC)

    /// <summary>A join try's handshake without an answer ends after this long (then the next try).</summary>
    private const double HandshakeSeconds = 10;

    private static Rules.DropSettings DropSettingsNow() => new()
    {
        ProblemSeconds = Plugin.CoopProblemSeconds?.Value ?? Rules.DropSettings.DefaultProblemSeconds,
        DropSeconds = Plugin.CoopDropSeconds?.Value ?? Rules.DropSettings.DefaultDropSeconds,
        RejoinWindowSeconds = Plugin.CoopRejoinSeconds?.Value ?? Rules.DropSettings.DefaultRejoinWindowSeconds,
        RetrySeconds = Plugin.CoopRetrySeconds?.Value ?? Rules.DropSettings.DefaultRetrySeconds,
    };

    /// <summary>Once a frame, with or without a session: who is silent, the windows, the tries, the lines.</summary>
    private void TickDrops(double now)
    {
        var session = _session;
        int teammates = 0;
        if (session is { State: SessionState.Active })
        {
            foreach (var p in session.Roster)
            {
                if (p.Slot == session.LocalSlot)
                    continue;
                teammates++;
                // the host hears every client itself; a client hears the host itself and the others through it
                double silence = session.IsHost || p.Slot == 0 ? session.SilenceSeconds(p.Slot)
                    : _remotes.TryGetValue(p.Slot, out var remote) ? now - remote.LastHeard : 0;
                _drops.Heard(p.Slot, p.Name, p.PlayerId, Math.Max(0, silence), now);
            }
            if (teammates > 0)
                _outage?.Arm(now); // the drop test's clock starts with the first teammate
        }
        _drops.Tick(now, teammates);
        if (_drops.Reconnecting && (session == null || session.State == SessionState.Ended) && !_closeEnded && _drops.RetryDue(now))
            Retry(now);
        foreach (var note in _drops.Notes)
            OnDropNote(note, now);
        _drops.Notes.Clear();
        _drops.Lines(_dropLines, now);
        _dropText.Clear();
        foreach (var line in _dropLines)
        {
            if (_dropText.Length > 0)
                _dropText.Append('\n');
            _dropText.Append(LineText(line));
        }
        _tags.Connection(_dropText.ToString());
    }

    private static string LineText(in Rules.DropLine line) => line.Kind switch
    {
        Rules.DropLineKind.Problem => Lang.Loc.F("Connection problem with {0}... {1} s", line.Name, line.Seconds),
        Rules.DropLineKind.WaitingFor => Lang.Loc.F("{0} lost the connection. Waiting for them: {1} s", line.Name, line.Seconds),
        Rules.DropLineKind.Reconnecting => Lang.Loc.F("Lost the connection to {0}. Trying again: {1} s, then you play on alone.", line.Name, line.Seconds),
        Rules.DropLineKind.Back => Lang.Loc.F("{0} is back.", line.Name),
        Rules.DropLineKind.LostAlone => Lang.Loc.F("{0} lost the connection. You play on alone.", line.Name),
        Rules.DropLineKind.Lost => Lang.Loc.F("{0} lost the connection.", line.Name),
        _ => Lang.Loc.T("The host left. You play on alone."),
    };

    private void OnDropNote(in Rules.DropNote note, double now)
    {
        string kind = note.Kind switch
        {
            Rules.DropNoteKind.Problem => "drop-problem",
            Rules.DropNoteKind.ProblemOver => "drop-problem-over",
            Rules.DropNoteKind.Dropped => "drop-dropped",
            Rules.DropNoteKind.RetryNow => "drop-rejoin-try",
            Rules.DropNoteKind.Rejoined => "drop-rejoined",
            Rules.DropNoteKind.Gone => "drop-gone",
            _ => "drop-solo",
        };
        CoopTrace.Write(kind, CoopWorld.CurrentRoom(), name: note.Name, amount: note.Seconds, slot: note.Slot);
        Plugin.Log.LogInfo(FormattableString.Invariant($"[Coop] {kind}: {note.Name} (slot {note.Slot}), {note.Seconds:0.#}") +
                           (note.Kind == Rules.DropNoteKind.RetryNow ? FormattableString.Invariant($" s left, try {_drops.Tries}") : ""));
        if (note.Kind == Rules.DropNoteKind.Solo)
            PlayOnAlone(now);
    }

    /// <summary>A client after a broken line: one more join try (the same game process, the same player id).</summary>
    private void Retry(double now)
    {
        CloseTransport();
        _closeEnded = false;
        if (_plan.Starts)
            StartSession();
        else if (_lobby != null && _steamHost != 0)
        {
            var transport = SteamTransport.Join(_steamHost);
            string why = StartAtRuntime(transport, transport.HostPeer, Protocol.MaxPlayers, rejoin: true);
            if (why != null)
                Plugin.Log.LogWarning("[Coop] joining again did not start: " + why);
        }
        if (_session == null || _session.State == SessionState.Ended)
            _drops.HostLost("", LeaveReason.ConnectionLost, false, now); // the next try follows
    }

    /// <summary>
    /// The tries failed (or the host ended the session): this game's run goes on alone from where it stands, with its
    /// own gear. Every co-op part is gone, so the patches act as in single player; the room's puppets become this
    /// game's own enemies again (EnemySync.ReleasePuppets) so the room ends by the game itself.
    /// [DECISION] No later rejoin: once alone, alone for the rest of this run (a new session from the window, or the
    /// launcher's restart, starts over). The run counts as this game's own solo run from here; its history keeps the
    /// co-op part (the run history has no co-op mark yet: see DC.md).
    /// </summary>
    // REVIEW(fable): package DC, never run in the game: playing on alone after a broken line.
    private void PlayOnAlone(double now)
    {
        _rejoinAt = -1;
        if (_session != null && _session.State != SessionState.Ended)
        {
            _goingSolo = true; // a join try under way is given up (it says goodbye)
            try { _session.Leave(); }
            catch (Exception e) { Plugin.Log.LogWarning("[Coop] leaving the join try: " + e.Message); }
            finally { _goingSolo = false; }
        }
        TearDown();
        int released = 0;
        try { released = EnemySync.ReleasePuppets(Singleton<Service>.Instance?.levelManager?.player); }
        catch (Exception e) { Fail("playing on alone", e); }
        CoopTrace.Write("drop-solo-enemies", CoopWorld.CurrentRoom(), amount: released);
        Plugin.Log.LogInfo($"[Coop] playing on alone from {(Map.Instance != null ? Map.Instance.name : "no map")}: {released} enemies are this game's own");
        if (_runtimeSession)
            EndRuntimeSession(_session is { State: SessionState.Ended, EndReason: LeaveReason.HostLeft } ? LeaveReason.HostLeft : LeaveReason.ConnectionLost, _session);
    }

    private void Update()
    {
        using var _perf = DamageInsight.Crash.Perf.Measure("Co-op");
        double now = Now;
        if (_lobby != null)
            TickSteam();
        if (_matchmaking != null)
        {
            try { _matchmaking.Tick(); }
            catch (Exception e) { Fail("matchmaking", e); }
        }
        if (_plan.NeedsSaveIsolation && WaitForSave(now))
            return;
        if (_closeEnded)
        {
            _closeEnded = false;
            CloseTransport();
        }
        try { TickDrops(now); }
        catch (Exception e) { Fail("the connection watch", e); }
        if (_session == null || _session.State == SessionState.Ended)
        {
            if (_rejoinAt >= 0 && now >= _rejoinAt && !_drops.Reconnecting && !_drops.IsSolo)
            {
                CloseTransport();
                StartSession();
            }
            // a test bot plays on without a session after a broken line (trying again, or alone)
            if (_drops.Reconnecting || _drops.IsSolo)
            {
                try
                {
                    _bot?.Tick();
                    _botItems?.Tick(Singleton<Service>.Instance?.levelManager?.player);
                    _botEssences?.Tick(Singleton<Service>.Instance?.levelManager?.player);
                }
                catch (Exception e) { Fail("the report", e); }
            }
            return;
        }
        // Every part of the frame's work is guarded by itself: an error in one (drawing the copies) must not skip
        // the others of that frame (the room, the enemies), as one try around everything did.
        try { _diag.Tick(_session, _transport); }
        catch (Exception e) { Fail("the co-op diagnostics", e); }
        try { _session.Update(now); }
        catch (Exception e) { Fail("session", e); }
        if (_session != null && _session.State == SessionState.Active)
        {
            try
            {
                SendLocal(now);
                SendLoadout(now);
            }
            catch (Exception e) { Fail("sending this player", e); }
            try { DrawRemotes(now); }
            catch (Exception e) { Fail("the teammates' copies", e); }
            try { TeammateProjectiles.Tick(); }
            catch (Exception e) { Fail("the teammates' shots", e); }
            try { _world?.Tick(now); }
            catch (Exception e) { Fail("the room", e); }
            try { _connection?.Tick(now); }
            catch (Exception e) { Fail("the connection box", e); }
            try { PlayerWatch.Tick(); }
            catch (Exception e) { Fail("the player watch", e); }
            try { CoopUnstuck.Tick(); }
            catch (Exception e) { Fail("unstuck", e); }
            try { OwnCameraTriggers.Tick(); }
            catch (Exception e) { Fail("the arena camera", e); }
            try { _enemies?.Tick(now); }
            catch (Exception e) { Fail("the enemies", e); }
            try { _timeEffects?.Tick(now); }
            catch (Exception e) { Fail("the slow motions", e); }
            try { _hostWorld?.Tick(now); }
            catch (Exception e) { Fail("the host's world", e); }
            try { _damageBoard?.Tick(now); }
            catch (Exception e) { Fail("the damage board", e); }
            try { _playerNotes?.Tick(now); }
            catch (Exception e) { Fail("the players' notes", e); }
            try { _scenes?.Tick(); }
            catch (Exception e) { Fail("the scenes", e); }
            try { _revive?.Tick(now); }
            catch (Exception e) { Fail("going down and revive", e); }
            try { _loot?.Tick(now); }
            catch (Exception e) { Fail("the shared loot", e); }
            try
            {
                _autoRun?.Tick(_world);
                if (CoopProbe.On)
                {
                    CoopProbe.Tick(now, this);
                    CoopShots.Tick(CoopProbe.ShownMoment(now));
                }
            }
            catch (Exception e) { Fail("the test tools", e); }
        }
        try
        {
            _bot?.Tick();
            _botItems?.Tick(Singleton<Service>.Instance?.levelManager?.player);
            _botEssences?.Tick(Singleton<Service>.Instance?.levelManager?.player);
            if (now >= _nextReport)
            {
                _nextReport = now + ReportSeconds;
                Report();
            }
        }
        catch (Exception e) { Fail("the report", e); }
    }

    /// <summary>The Steam lobby's frame: Steam found (warm-up once), waiting joins, the castle gate on the host.</summary>
    private void TickSteam()
    {
        try
        {
            _lobby.Tick();
            if (_lobby.SteamReady && !_steamWarmed)
            {
                _steamWarmed = true;
                SteamTransport.WarmUp(); // relay access and ping data before the first connection
            }
            // Castle only [DECISION]: the host turns new players away during a run (RejectReason.NotAccepting); the
            // lobby is also closed then, so this is for a join already under way when the run starts.
            if (_runtimeSession && _session is { IsHost: true } host)
                host.AcceptingPlayers = InCastle;
        }
        catch (Exception e) { Fail("the Steam lobby", e); }
    }

    private readonly Dictionary<string, (string error, int count)> _failures = new();

    /// <summary>Logs a part's error in full once; the same error again (often every frame) only every 600th time.</summary>
    private void Fail(string part, Exception e)
    {
        string error = e.GetType().Name + ": " + e.Message;
        _failures.TryGetValue(part, out var last);
        int count = last.error == error ? last.count + 1 : 1;
        _failures[part] = (error, count);
        if (count == 1)
            Plugin.Log.LogError($"[Coop] {part}: {e}");
        else if (count % 600 == 0)
            Plugin.Log.LogError($"[Coop] {part}: the same error {count} times in a row: {error}");
    }

    // ------------------------------------------------------------------ this game's player

    private void SendLocal(double now)
    {
        if (now < _nextSend)
            return;
        // A long frame (loading) doesn't make a burst of catch-up sends.
        _nextSend = Math.Max(_nextSend + 1.0 / SendRate, now);
        var player = Singleton<Service>.Instance?.levelManager?.player;
        var map = Map.Instance;
        var state = new PlayerState();
        uint mapKey = 0;
        if (player != null && map != null)
        {
            mapKey = PlayerStateMessage.MapKey(map.name);
            Vector2 origin = map.transform.position;
            Vector2 position = player.transform.position;
            // Velocity from the positions sent (the receiver extrapolates and animates with it).
            Vector2 velocity = Vector2.zero;
            double dt = now - _lastSentTime;
            if (dt > 0.001 && dt < 0.5)
                velocity = (position - _lastSentPosition) / (float)dt;
            _lastSentPosition = position;
            _lastSentTime = now;
            state.Position = new Vec2(position.x - origin.x, position.y - origin.y);
            state.Velocity = new Vec2(velocity.x, velocity.y);
            state.FacingRight = player.lookingDirection == Characters.Character.LookingDirection.Right;
            state.Grounded = player.movement != null && player.movement.isGrounded;
            state.Health = (float)player.health.currentHealth;
            state.MaxHealth = (float)player.health.maximumHealth;
        }
        _writer.Reset();
        PlayerStateMessage.Write(_writer, now, mapKey, state);
        _session.Broadcast(MessageType.PlayerState, _writer, Delivery.Unreliable);
    }

    private void SendEvent(TeammateEvent e)
    {
        _writer.Reset();
        e.Write(_writer, Now);
        _session.Broadcast(MessageType.TimedEvent, _writer, Delivery.Reliable);
    }

    /// <summary>This game's gear as teammates need it: sent when it changes (and again for a newcomer).</summary>
    private void SendLoadout(double now)
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        Follow(player);
        if (player == null || now < _nextLoadoutCheck)
            return;
        _nextLoadoutCheck = now + 0.25;
        var loadout = LoadoutOf(player);
        if (loadout == null || loadout.Equals(_sentLoadout))
            return;
        _sentLoadout = loadout;
        SendEvent(TeammateEvent.ForLoadout(loadout));
        SendEvent(TeammateEvent.ForFollower(TeammateFollowers.Local)); // with the gear: a newcomer gets it too
    }

    private static Loadout LoadoutOf(Characters.Character player)
    {
        var inventory = player.playerComponents?.inventory;
        var weapons = inventory?.weapon;
        if (weapons == null)
            return null;
        var loadout = new Loadout { Current = Math.Max(0, weapons.currentIndex) };
        for (int i = 0; i < Loadout.Slots && i < weapons.weapons.Length; i++)
        {
            var weapon = weapons.weapons[i];
            loadout.Weapons[i] = weapon != null ? Loadout.GearName(weapon.name) : "";
            loadout.Skills[i] = weapon?.currentSkills?.Where(k => k != null).Select(k => k.key).ToArray() ?? Array.Empty<string>();
        }
        var essence = inventory.quintessence?.items?.FirstOrDefault(e => e != null);
        loadout.Essence = essence != null ? Loadout.GearName(essence.name) : "";
        loadout.Items = inventory.item?.items?.Where(i => i != null).Select(i => Loadout.GearName(i.name)).ToArray() ?? Array.Empty<string>();
        return loadout;
    }

    /// <summary>Follows the moves of this game's player (a new run makes a new player object).</summary>
    private void Follow(Characters.Character player)
    {
        if (player == _followed)
            return;
        if (_followed != null)
        {
            _followed.onStartMotion -= OnLocalMotion;
            if (_followed.health != null)
                _followed.health.onTookDamage -= OnLocalHit;
        }
        _followed = player;
        TeammateProjectiles.Local = player;
        TeammateProjectiles.Send ??= e =>
        {
            if (_session != null && _session.State == SessionState.Active)
                SendEvent(e);
        };
        if (player != null)
        {
            player.onStartMotion += OnLocalMotion;
            player.health.onTookDamage += OnLocalHit;
        }
    }

    /// <summary>Trace: this player was hit (by whom, how much, health after).</summary>
    private void OnLocalHit(in Characters.Damage original, in Characters.Damage took, double dealt)
    {
        if (CoopTrace.On && dealt > 0)
            CoopTrace.Write("player-hit", CoopWorld.CurrentRoom(), name: CoopTrace.NameOf(took.attacker.character),
                what: took.attackType.ToString(), amount: dealt, after: _followed != null ? _followed.health.currentHealth : double.NaN);
    }

    private void OnLocalMotion(Characters.Actions.Motion motion, float runSpeed) => SendMotion(motion, runSpeed, false);

    /// <summary>A move of a character that doesn't take it over (RemoteAvatar.NonblockMoves).</summary>
    internal void OnNonblockMotion(Characters.Character character, Characters.Actions.Motion motion)
    {
        if (character != null && character == _followed)
            SendMotion(motion, 1f, true);
    }

    private void SendMotion(Characters.Actions.Motion motion, float runSpeed, bool nonblock)
    {
        if (_session == null || _session.State != SessionState.Active || motion == null || motion.action == null || _followed == null)
            return;
        try
        {
            int index = Array.IndexOf(motion.action.motions, motion);
            if (index < 0)
                return;
            SendEvent(TeammateEvent.ForMotion(RemoteAvatar.PathOf(motion.action.transform, _followed.transform), index, runSpeed, nonblock));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] a move was not sent: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ teammates

    private void OnPlayerState(GameMessage message, NetReader payload)
    {
        var state = PlayerStateMessage.Read(payload, out double sentAt, out uint mapKey);
        var remote = RemoteOf(message.FromSlot);
        if (mapKey != remote.MapKey)
        {
            // Another map: its positions start over (no sliding across the old map).
            remote.MapKey = mapKey;
            remote.Buffer.Clear();
        }
        remote.Delay.OnReceive(sentAt, Now);
        if (mapKey != 0)
            remote.Buffer.Add(sentAt, state);
        remote.Received++;
        remote.LastHeard = Now;
    }

    private void OnTeammateEvent(GameMessage message, NetReader payload)
    {
        var e = TeammateEvent.Read(payload, out double sentAt);
        if (e == null)
            return;
        var remote = RemoteOf(message.FromSlot);
        if (e.Kind == TeammateEventKind.Loadout)
        {
            remote.Loadout = e.Loadout;
            TeammateProjectiles.LoadGear(e.Loadout); // the shots of its items and quintessence (a copy has only its skulls)
        }
        else if (e.Kind == TeammateEventKind.Follower)
            remote.Follower = e.GearName ?? "";
        remote.Events.Add(sentAt, e);
    }

    private Remote RemoteOf(int slot)
    {
        if (!_remotes.TryGetValue(slot, out var remote))
        {
            remote = _remotes[slot] = new Remote
            {
                Slot = slot,
                Name = _session.PlayerAt(slot)?.Name ?? "Player " + (slot + 1),
            };
        }
        return remote;
    }

    private void DrawRemotes(double now)
    {
        var map = Map.Instance;
        bool inMap = map != null && Singleton<Service>.Instance?.levelManager?.player != null;
        uint localMap = inMap ? PlayerStateMessage.MapKey(map.name) : 0;
        foreach (var remote in _remotes.Values)
        {
            double render = remote.Delay.Advance(now);
            if (render < remote.LastRender - 0.001)
            {
                remote.Rewinds++;
                remote.Buffer.Clear(); // the render clock jumped back: old samples would replay
            }
            remote.LastRender = render;
            var kind = remote.Buffer.Sample(render, out var state);
            switch (kind)
            {
                case SampleKind.Interpolated: remote.Interpolated++; break;
                case SampleKind.Extrapolated: remote.Extrapolated++; break;
                case SampleKind.Held: remote.Held++; break;
                default: remote.Waiting++; break;
            }
            if (remote.Buffer.Snapped)
                remote.Snaps++;
            if (kind != SampleKind.Empty && kind != SampleKind.Waiting && state.MaxHealth > 0)
            {
                remote.Health = state.Health;
                remote.MaxHealth = state.MaxHealth;
            }
            bool drawable = inMap && remote.MapKey == localMap && kind != SampleKind.Empty && kind != SampleKind.Waiting;
            if (drawable && (remote.Avatar == null || !remote.Avatar.Alive))
            {
                remote.Avatar = RemoteAvatar.Create(remote.Slot, remote.Name);
                if (remote.Avatar != null && remote.Loadout != null)
                    remote.Avatar.ApplyLoadout(remote.Loadout, this);
            }
            if (drawable && remote.Avatar != null)
            {
                remote.Avatar.Apply(state, map.transform.position);
                TeammateEssences.Keep(remote.Avatar.Character, remote.Loadout?.Essence);
                TeammateFollowers.Keep(remote.Avatar.Character, remote.Follower);
            }
            else
                remote.Avatar?.Show(false);
            // Gear and moves on the same timeline as the positions.
            while (remote.Events.TryTakeDue(render, out var due))
            {
                var e = due.Item;
                if (remote.Avatar == null || !remote.Avatar.Alive)
                    continue; // the loadout is kept in remote.Loadout for when the copy is made
                if (e.Kind == TeammateEventKind.Loadout)
                    remote.Avatar.ApplyLoadout(e.Loadout, this);
                else if (e.Kind == TeammateEventKind.Projectile)
                {
                    if (drawable && remote.Avatar.Shown)
                        TeammateProjectiles.Fire(remote.Avatar.Character, e);
                }
                else if (e.Kind == TeammateEventKind.Essence)
                {
                    if (drawable && remote.Avatar.Shown)
                    {
                        TeammateEssences.Keep(remote.Avatar.Character, remote.Loadout?.Essence);
                        TeammateEssences.Use(remote.Avatar.Character, e.GearName);
                    }
                    else
                        CoopTrace.Write("essence-unseen", CoopWorld.CurrentRoom(), what: e.GearName, slot: remote.Slot);
                }
                else if (e.Kind == TeammateEventKind.Corpse || e.Kind == TeammateEventKind.Grave)
                {
                    if (drawable)
                        TeammateGraves.Show(remote.Avatar.Character, e, map.transform.position);
                }
                else if (e.Kind == TeammateEventKind.Follower)
                {
                    // kept in remote.Follower when it arrived (TeammateFollowers.Keep shows it)
                }
                else if (drawable)
                {
                    remote.Avatar.PlayMotion(e.ActionPath, e.MotionIndex, e.Speed, e.Kind == TeammateEventKind.NonblockMotion);
                    remote.Moves++;
                    CoopTrace.Write("teammate-move", CoopWorld.CurrentRoom(), what: e.ActionPath + "#" + e.MotionIndex, slot: remote.Slot);
                }
            }
            _tags.Update(remote.Slot, remote.Name, drawable ? remote.Avatar?.Character : null, remote.Health, remote.MaxHealth);
        }
    }

    // ------------------------------------------------------------------ for the test probe (Testing/CoopProbe)

    internal int LocalSlot => _session?.LocalSlot ?? 0;

    /// <summary>A teammate's copy when it is shown here, or null.</summary>
    internal Characters.Character CopyOf(int slot) => AvatarOf(slot);

    /// <summary>Every teammate copy shown here: slot, the copy, the moment of ITS OWNER's clock it is drawn at.</summary>
    internal void ForProbe(Action<int, Characters.Character, double> each)
    {
        foreach (var remote in _remotes.Values)
            if (remote.Avatar?.Alive == true && remote.Avatar.Character.gameObject.activeInHierarchy)
                each(remote.Slot, remote.Avatar.Character, remote.LastRender);
    }

    /// <summary>A teammate's copy in this game (the attacker of their hits on the host's enemies), or null.</summary>
    private Characters.Character AvatarOf(int slot) =>
        _remotes.TryGetValue(slot, out var remote) && remote.Avatar?.Alive == true ? remote.Avatar.Character : null;

    /// <summary>A copy's (or this player's) slot from anywhere (TeammateLook), -1 when no session runs.</summary>
    internal static int SlotOfCharacter(Characters.Character character) => Instance?.SlotOf(character) ?? -1;

    /// <summary>This game's player -> its slot; a teammate's copy -> the teammate's slot; else -1.</summary>
    private int SlotOf(Characters.Character character)
    {
        if (character == null || _session == null)
            return -1;
        if (character == Singleton<Service>.Instance?.levelManager?.player)
            return _session.LocalSlot;
        foreach (var remote in _remotes.Values)
            if (remote.Avatar?.Character == character)
                return remote.Slot;
        return -1;
    }

    /// <summary>A slot -> this game's player (own slot) or that teammate's copy.</summary>
    private Characters.Character PlayerOf(int slot) =>
        _session != null && slot == _session.LocalSlot ? Singleton<Service>.Instance?.levelManager?.player : AvatarOf(slot);

    private void Forget(int slot)
    {
        if (_remotes.TryGetValue(slot, out var remote))
        {
            remote.Avatar?.Destroy();
            _remotes.Remove(slot);
        }
        _tags.Remove(slot);
    }

    /// <summary>One log line per teammate: how the connection and the drawing went since the last line.</summary>
    private void Report()
    {
        if (_session == null || _remotes.Count == 0)
            return;
        foreach (var r in _remotes.Values)
        {
            int frames = r.Interpolated + r.Extrapolated + r.Held + r.Waiting;
            double smooth = frames == 0 ? 0 : 100.0 * r.Interpolated / frames;
            Plugin.Log.LogInfo(
                $"[Coop] P{r.Slot + 1} {r.Name}: ping {_session.PingMs(r.Slot):0} ms, jitter {_session.JitterMs(r.Slot):0} ms, " +
                $"{r.Received / ReportSeconds:0.#} states/s, buffer delay {r.Delay.Delay * 1000:0} ms; drawn {smooth:0.#}% interpolated " +
                $"(extrapolated {r.Extrapolated}, held {r.Held}, waiting {r.Waiting} of {frames} frames), {r.Snaps} snaps, " +
                $"{r.Rewinds} rewinds, {r.Moves} moves played; {(r.Avatar?.Alive == true ? "shown" : "not shown")}");
            _diag.Traffic(r.Slot, r.Received, r.Snaps, r.Rewinds, ReportSeconds);
            r.Received =r.Interpolated = r.Extrapolated = r.Held = r.Waiting = r.Snaps = r.Rewinds = r.Moves = 0;
        }
        Plugin.Log.LogInfo($"[Coop] traffic: {_session.BytesSent / 1024.0:0.#} KB sent, {_session.BytesReceived / 1024.0:0.#} KB received");
        var e = _enemies;
        if (e != null && (e.SnapshotsSent + e.SnapshotsReceived + e.HitsSent + e.HitsApplied) > 0)
        {
            Plugin.Log.LogInfo($"[Coop] enemies: snapshots {e.SnapshotsSent} sent / {e.SnapshotsReceived} received, events " +
                               $"{e.EventsSent} sent / {e.EventsPlayed} played, hits {e.HitsSent} sent / {e.HitsApplied} applied, {e.Deaths} deaths from the host");
            e.SnapshotsSent = e.SnapshotsReceived = e.EventsSent = e.EventsPlayed = e.HitsSent = e.HitsApplied = e.Deaths = 0;
        }
    }

    // ------------------------------------------------------------------ the end

    private void CloseTransport()
    {
        try
        {
            _transport?.Dispose();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] closing the transport: " + e.Message);
        }
        _transport = null;
        _session = null;
    }

    private void OnApplicationQuit()
    {
        _quitting = true;
        try { _matchmaking?.Cancel(); } // off the queue (the request may not make it out before the game closes)
        catch (Exception) { }
        Follow(null);
        CoopTrace.Write("quit");
        CoopTrace.Stop();
        CoopProbe.Stop();
        try
        {
            _session?.Leave();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] leaving: " + e.Message);
        }
        CloseTransport();
        try
        {
            _lobby?.Dispose(); // leaves the Steam lobby, clears the rich presence
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] leaving the Steam lobby: " + e.Message);
        }
    }
}
