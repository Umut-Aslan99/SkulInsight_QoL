using System;
using System.Collections.Generic;
using System.Globalization;

namespace DamageInsight.Coop.Net;

/// <summary>Where this game is with its Steam lobby.</summary>
public enum LobbyState
{
    /// <summary>No lobby, nothing asked.</summary>
    Idle,
    /// <summary>Host was pressed: Steam is making the lobby.</summary>
    Creating,
    /// <summary>This game's lobby is open and its session listens (friends can join).</summary>
    Hosting,
    /// <summary>A join request (an invite, "Join game", +connect_lobby at the start) waits until this game is in the
    /// castle (joining mid-run isn't possible yet).</summary>
    WaitingForCastle,
    /// <summary>Steam is entering the friend's lobby.</summary>
    Joining,
    /// <summary>In a friend's lobby; the session connects to (or plays with) its host.</summary>
    InLobby,
    /// <summary>The last try ended with a problem (<see cref="SteamLobby.Problem"/>); Host or a new invite start over.</summary>
    Failed,
}

/// <summary>Why the lobby is in <see cref="LobbyState.Failed"/> (the window has a text for each).</summary>
public enum LobbyProblem
{
    None,
    /// <summary>Steam isn't running or the game wasn't started through Steam.</summary>
    NoSteam,
    /// <summary>Host or join outside the castle.</summary>
    NotInCastle,
    /// <summary>Steam could not make the lobby (Detail: Steam's result number or "no answer").</summary>
    CreateFailed,
    /// <summary>The lobby doesn't exist any more, or its host has left it.</summary>
    LobbyGone,
    /// <summary>The lobby is full.</summary>
    LobbyFull,
    /// <summary>Steam refused to enter the lobby (Detail: Steam's answer).</summary>
    JoinFailed,
    /// <summary>The lobby's host runs another SkulInsight version (Detail: theirs).</summary>
    VersionMismatch,
    /// <summary>The co-op session could not start (Detail: why, from the transport).</summary>
    StartFailed,
    /// <summary>The session ended (<see cref="SteamLobby.EndReason"/>, <see cref="SteamLobby.Rejection"/>).</summary>
    SessionEnded,
}

/// <summary>Steam's answer to entering a lobby (LobbyEnter_t), without Steamworks.</summary>
internal readonly struct LobbyEnterResult
{
    public const int Success = 1, DoesntExist = 2, NotAllowed = 3, Full = 4, Error = 5, Banned = 6, Limited = 7;

    public readonly ulong Lobby;
    /// <summary>Steam never answered properly (the call failed).</summary>
    public readonly bool IoFailure;
    /// <summary>EChatRoomEnterResponse (1 = success).</summary>
    public readonly int Response;

    public LobbyEnterResult(ulong lobby, bool ioFailure, int response)
    {
        Lobby = lobby;
        IoFailure = ioFailure;
        Response = response;
    }
}

/// <summary>The few Steam lobby / friends calls co-op needs. The real one (SteamworksLobby, SteamLobbyNet.cs) is the
/// only lobby code that touches Steamworks; tests use a fake. Every call answers on the main thread (Steam's
/// callbacks run in the game's SteamAPI.RunCallbacks).</summary>
internal interface ISteamLobbyApi : IDisposable
{
    ulong OwnId { get; }
    string OwnName { get; }
    /// <summary>Friends-only (friends can join from their list) or private (invited only). done(ok, lobby, Steam's result).</summary>
    void CreateLobby(bool friendsCanJoin, int maxMembers, Action<bool, ulong, int> done);
    void JoinLobby(ulong lobby, Action<LobbyEnterResult> done);
    void LeaveLobby(ulong lobby);
    void SetLobbyData(ulong lobby, string key, string value);
    /// <summary>"" when not set.</summary>
    string LobbyData(ulong lobby, string key);
    ulong LobbyOwner(ulong lobby);
    void SetJoinable(ulong lobby, bool joinable);
    void SetRichPresence(string key, string value);
    void ClearRichPresence();
    /// <summary>Steam's overlay with the friends to invite into the lobby; false when the overlay can't open.</summary>
    bool OpenInviteDialog(ulong lobby);
    /// <summary>GameLobbyJoinRequested_t: an accepted invite or "Join game" while the game runs (the lobby's id).</summary>
    Action<ulong> JoinRequested { set; }
    /// <summary>GameRichPresenceJoinRequested_t: "Join game" through the rich presence connect string.</summary>
    Action<string> ConnectRequested { set; }
}

/// <summary>What the lobby needs from the running game (CoopRuntime; a fake in tests).</summary>
internal interface ILobbySessions
{
    /// <summary>Hosting and joining happen in the castle only.</summary>
    bool InCastle { get; }
    /// <summary>Players in the session now (rich presence's group size; 0 when none).</summary>
    int PlayerCount { get; }
    /// <summary>Opens a Steam host session; null = started, else why not (English, for the log).</summary>
    string StartHost(int maxPlayers);
    /// <summary>Opens a Steam client session to the host's Steam id; null = started, else why not.</summary>
    string StartJoin(ulong hostSteamId);
    /// <summary>Leaves the running session (says goodbye); nothing when none.</summary>
    void StopSession();
}

/// <summary>
/// Steam lobbies for co-op (docs/coop/NEXT_SPECS.md B): the host makes a friends-only lobby (its data: the mod
/// version, the protocol, the host's Steam id) and opens a Steam host session; friends come in through Steam's invite
/// dialog, "Join game" in the friends list or the +connect_lobby start option (Steam starts Skul with it when the
/// game was closed). A joining game enters the lobby, checks the version, and opens a client session to the host's
/// Steam id (<see cref="SteamTransport"/>); the session's Hello checks versions and mod lists as always. Leaving the
/// session leaves the lobby and the other way round. Rich presence says "In a co-op run" and groups the players.
/// Plain logic over <see cref="ISteamLobbyApi"/> and <see cref="ILobbySessions"/> (tested with fakes); Steam is
/// looked for every second until it is there, so nothing breaks without it.
/// </summary>
public sealed class SteamLobby : IDisposable
{
    public const string KeyMod = "skulinsight", KeyProtocol = "protocol", KeyHost = "host", KeyName = "name";
    /// <summary>The start option (and rich presence connect string) Steam uses for a lobby: "+connect_lobby &lt;id&gt;".</summary>
    public const string ConnectOption = "+connect_lobby";
    private const double SteamAnswerSeconds = 20, PresenceEvery = 2, ApiEvery = 1;

    private readonly ILobbySessions _sessions;
    private readonly string _modVersion;
    private readonly Func<ISteamLobbyApi> _tryApi;
    private readonly Func<double> _clock;
    private ISteamLobbyApi _api;
    private double _nextApiTry, _since, _nextPresence;
    private ulong _pending;
    private int _presenceSize = -1;
    private bool? _joinable;
    private bool _disposed;

    /// <param name="tryApi">Steam's lobby calls, or null while Steam isn't there (asked every second).</param>
    /// <param name="startupLobby">The lobby of a +connect_lobby start option (0 = none): joined once in the castle.</param>
    internal SteamLobby(ILobbySessions sessions, string modVersion, Func<ISteamLobbyApi> tryApi, Func<double> clock, ulong startupLobby = 0)
    {
        _sessions = sessions;
        _modVersion = modVersion ?? "";
        _tryApi = tryApi;
        _clock = clock;
        if (startupLobby != 0)
        {
            _pending = startupLobby;
            State = LobbyState.WaitingForCastle;
        }
    }

    public LobbyState State { get; private set; } = LobbyState.Idle;
    public LobbyProblem Problem { get; private set; }
    /// <summary>More about the problem (Steam's code, the host's version, the transport's reason); "" when none.</summary>
    public string Detail { get; private set; } = "";
    /// <summary>With <see cref="LobbyProblem.SessionEnded"/>: why the session ended.</summary>
    public LeaveReason EndReason { get; private set; }
    /// <summary>With <see cref="LeaveReason.Rejected"/>: why the host said no.</summary>
    public RejectReason Rejection { get; private set; }
    /// <summary>The lobby this game is in (0 = none).</summary>
    public ulong Lobby { get; private set; }
    /// <summary>The lobby a join waits for (WaitingForCastle / Joining), 0 = none.</summary>
    public ulong PendingLobby => _pending;
    /// <summary>The session's host: this game's own Steam id when hosting, the friend's when joined.</summary>
    public ulong HostId { get; private set; }
    /// <summary>The host's Steam name as the lobby has it ("" when unknown or when hosting).</summary>
    public string HostName { get; private set; } = "";
    /// <summary>Steam is there (its calls could be made).</summary>
    public bool SteamReady => _api != null;
    public ulong OwnId => _api?.OwnId ?? 0;
    /// <summary>This player's Steam name ("" without Steam).</summary>
    public string OwnName => _api?.OwnName ?? "";

    /// <summary>Settings, read when a lobby is made: friends-only or invited-only, the lobby's size.</summary>
    public bool FriendsCanJoin { get; set; } = true;
    public int MaxPlayers { get; set; } = Protocol.MaxPlayers;
    /// <summary>The rich presence status friends see (the runtime sets it in this player's language).</summary>
    public string PresenceStatus { get; set; } = "In a co-op run";

    /// <summary>Lines for the game's log.</summary>
    public event Action<string> Log;
    /// <summary>The state changed (the window redraws).</summary>
    public event Action Changed;

    /// <summary>The window can offer Host (no lobby, nothing under way).</summary>
    public bool CanHost => State == LobbyState.Idle || State == LobbyState.Failed;
    /// <summary>The window can offer Leave (a lobby, a join under way, or a waiting join to cancel).</summary>
    public bool CanLeave => State == LobbyState.Creating || State == LobbyState.Hosting || State == LobbyState.WaitingForCastle ||
                            State == LobbyState.Joining || State == LobbyState.InLobby;
    /// <summary>Invites go into the lobby; every member may invite (the newcomer connects to the host).</summary>
    public bool CanInvite => State == LobbyState.Hosting || State == LobbyState.InLobby;

    // ---- asking -------------------------------------------------------------------------------------------------

    /// <summary>Makes a lobby and opens a host session (castle only).</summary>
    public void Host()
    {
        if (!CanHost)
            return;
        EnsureApi(force: true);
        if (_api == null)
        {
            Fail(LobbyProblem.NoSteam, "");
            return;
        }
        if (!_sessions.InCastle)
        {
            Fail(LobbyProblem.NotInCastle, "");
            return;
        }
        int size = Math.Max(2, Math.Min(Protocol.MaxPlayers, MaxPlayers));
        SetState(LobbyState.Creating);
        Note(FormattableString.Invariant($"making a {(FriendsCanJoin ? "friends-only" : "private")} lobby for {size} players"));
        _api.CreateLobby(FriendsCanJoin, size, OnCreated);
    }

    /// <summary>Steam's invite dialog for the lobby. False when there is no lobby or the overlay can't open.</summary>
    public bool Invite()
    {
        if (!CanInvite || _api == null || Lobby == 0)
            return false;
        bool shown = _api.OpenInviteDialog(Lobby);
        Note(shown ? "invite dialog opened" : "the Steam overlay could not open (is it switched on in Steam?)");
        return shown;
    }

    /// <summary>An accepted invite, "Join game" or +connect_lobby: leaves what this game is in and joins that lobby
    /// (now in the castle, else once this game is there). [DECISION] An accepted invite always wins: the player
    /// clicked it.</summary>
    public void RequestJoin(ulong lobby)
    {
        if (lobby == 0 || _disposed)
            return;
        if (lobby == Lobby && (State == LobbyState.Hosting || State == LobbyState.InLobby))
        {
            Note("already in that lobby");
            return;
        }
        if (lobby == _pending && (State == LobbyState.Joining || State == LobbyState.WaitingForCastle))
            return;
        Note(FormattableString.Invariant($"join requested: lobby {lobby}"));
        if (State != LobbyState.Idle && State != LobbyState.Failed)
            LeaveQuietly();
        _pending = lobby;
        EnsureApi(force: true);
        if (_api != null && _sessions.InCastle)
            BeginJoin();
        else
        {
            SetState(LobbyState.WaitingForCastle);
            Note(_api == null ? "the join waits for Steam" : "the join waits until this game is in the castle");
        }
    }

    /// <summary>Leaves the session and the lobby (or cancels a waiting join).</summary>
    public void Leave()
    {
        if (!CanLeave)
            return;
        Note("leaving");
        LeaveQuietly();
        SetState(LobbyState.Idle);
    }

    /// <summary>The runtime's session ended (any reason): this game leaves the lobby too.</summary>
    public void OnSessionEnded(LeaveReason why, RejectReason rejection, string hostModVersion)
    {
        if (State != LobbyState.Hosting && State != LobbyState.InLobby)
            return; // Leave (state already Idle) or a session this lobby didn't start
        ulong lobby = Lobby;
        Lobby = 0;
        _pending = 0;
        if (lobby != 0)
            _api?.LeaveLobby(lobby);
        ClearPresence();
        if (why == LeaveReason.LocalLeave)
        {
            SetState(LobbyState.Idle);
            return;
        }
        EndReason = why;
        Rejection = why == LeaveReason.Rejected ? rejection : RejectReason.None;
        Fail(LobbyProblem.SessionEnded, why == LeaveReason.Rejected && rejection == RejectReason.VersionMismatch ? hostModVersion ?? "" : "");
    }

    // ---- every frame --------------------------------------------------------------------------------------------

    public void Tick()
    {
        if (_disposed)
            return;
        double now = _clock();
        EnsureApi(force: false);
        if (_api == null)
            return;
        switch (State)
        {
            case LobbyState.WaitingForCastle:
                if (_sessions.InCastle)
                    BeginJoin();
                break;
            case LobbyState.Creating:
                if (now - _since > SteamAnswerSeconds)
                    Fail(LobbyProblem.CreateFailed, "no answer");
                break;
            case LobbyState.Joining:
                if (now - _since > SteamAnswerSeconds)
                {
                    _pending = 0;
                    Fail(LobbyProblem.JoinFailed, "no answer");
                }
                break;
            case LobbyState.Hosting:
                // Castle only [DECISION]: friends see "Join game" only while the host can take them.
                bool joinable = _sessions.InCastle;
                if (_joinable != joinable)
                {
                    _joinable = joinable;
                    _api.SetJoinable(Lobby, joinable);
                    Note(joinable ? "the lobby takes players (castle)" : "the lobby is closed to new players (a run)");
                }
                UpdatePresence(now);
                break;
            case LobbyState.InLobby:
                UpdatePresence(now);
                break;
        }
    }

    // ---- Steam's answers ----------------------------------------------------------------------------------------

    private void OnCreated(bool ok, ulong lobby, int result)
    {
        if (State != LobbyState.Creating)
        {
            if (ok && lobby != 0 && lobby != Lobby)
                _api?.LeaveLobby(lobby); // Leave was pressed meanwhile (a second answer for our own lobby is ignored)
            return;
        }
        if (!ok || lobby == 0)
        {
            Fail(LobbyProblem.CreateFailed, result.ToString(CultureInfo.InvariantCulture));
            return;
        }
        Lobby = lobby;
        HostId = _api.OwnId;
        HostName = "";
        _api.SetLobbyData(lobby, KeyMod, _modVersion);
        _api.SetLobbyData(lobby, KeyProtocol, Protocol.Version.ToString(CultureInfo.InvariantCulture));
        _api.SetLobbyData(lobby, KeyHost, HostId.ToString(CultureInfo.InvariantCulture));
        _api.SetLobbyData(lobby, KeyName, _api.OwnName ?? "");
        string error = _sessions.StartHost(Math.Max(2, Math.Min(Protocol.MaxPlayers, MaxPlayers)));
        if (error != null)
        {
            Lobby = 0;
            _api.LeaveLobby(lobby);
            Fail(LobbyProblem.StartFailed, error);
            return;
        }
        _joinable = null;
        Note(FormattableString.Invariant($"lobby {lobby} made; hosting as {HostId}"));
        SetState(LobbyState.Hosting);
        SetPresence();
    }

    private void BeginJoin()
    {
        _since = _clock();
        SetState(LobbyState.Joining);
        Note(FormattableString.Invariant($"entering lobby {_pending}"));
        _api.JoinLobby(_pending, OnEntered);
    }

    private void OnEntered(LobbyEnterResult result)
    {
        if (State != LobbyState.Joining || result.Lobby != _pending)
        {
            if (!result.IoFailure && result.Response == LobbyEnterResult.Success && result.Lobby != 0 && result.Lobby != Lobby)
                _api?.LeaveLobby(result.Lobby); // an answer for a join given up meanwhile
            return;
        }
        ulong lobby = result.Lobby;
        _pending = 0;
        if (result.IoFailure)
        {
            Fail(LobbyProblem.JoinFailed, "no answer");
            return;
        }
        if (result.Response != LobbyEnterResult.Success)
        {
            switch (result.Response)
            {
                case LobbyEnterResult.DoesntExist:
                    Fail(LobbyProblem.LobbyGone, "");
                    break;
                case LobbyEnterResult.Full:
                    Fail(LobbyProblem.LobbyFull, "");
                    break;
                default:
                    Fail(LobbyProblem.JoinFailed, EnterText(result.Response));
                    break;
            }
            return;
        }
        // In the lobby: who hosts, and the same version?
        string theirs = _api.LobbyData(lobby, KeyMod) ?? "", protocol = _api.LobbyData(lobby, KeyProtocol) ?? "";
        ulong.TryParse(_api.LobbyData(lobby, KeyHost) ?? "", NumberStyles.None, CultureInfo.InvariantCulture, out ulong host);
        ulong owner = _api.LobbyOwner(lobby);
        if (host == 0)
            host = owner;
        string problemDetail = null;
        var problem = LobbyProblem.None;
        if (host == 0)
            (problem, problemDetail) = (LobbyProblem.JoinFailed, "no host");
        else if (owner != 0 && owner != host)
            (problem, problemDetail) = (LobbyProblem.LobbyGone, ""); // the host left; Steam gave the lobby to someone else
        else if (host == _api.OwnId)
            (problem, problemDetail) = (LobbyProblem.JoinFailed, "own lobby");
        else if ((theirs.Length > 0 && theirs != _modVersion) ||
                 (protocol.Length > 0 && protocol != Protocol.Version.ToString(CultureInfo.InvariantCulture)))
            (problem, problemDetail) = (LobbyProblem.VersionMismatch, theirs.Length > 0 ? theirs : "protocol " + protocol);
        if (problem != LobbyProblem.None)
        {
            _api.LeaveLobby(lobby);
            Fail(problem, problemDetail);
            return;
        }
        Lobby = lobby;
        HostId = host;
        HostName = _api.LobbyData(lobby, KeyName) ?? "";
        string error = _sessions.StartJoin(host);
        if (error != null)
        {
            Lobby = 0;
            _api.LeaveLobby(lobby);
            Fail(LobbyProblem.StartFailed, error);
            return;
        }
        Note(FormattableString.Invariant($"in lobby {lobby}; connecting to the host {host} ({HostName}, SkulInsight {theirs})"));
        SetState(LobbyState.InLobby);
        SetPresence();
    }

    private static string EnterText(int response) => response switch
    {
        LobbyEnterResult.NotAllowed => "not allowed",
        LobbyEnterResult.Error => "error",
        LobbyEnterResult.Banned => "banned",
        LobbyEnterResult.Limited => "limited account",
        _ => "code " + response.ToString(CultureInfo.InvariantCulture),
    };

    // ---- helpers ------------------------------------------------------------------------------------------------

    private void EnsureApi(bool force)
    {
        if (_api != null || _tryApi == null)
            return;
        double now = _clock();
        if (!force && now < _nextApiTry)
            return;
        _nextApiTry = now + ApiEvery;
        _api = _tryApi();
        if (_api == null)
            return;
        _api.JoinRequested = RequestJoin;
        _api.ConnectRequested = connect =>
        {
            ulong lobby = ParseConnectLobby(new[] { connect });
            if (lobby != 0)
                RequestJoin(lobby);
            else
                Note("a join request without a lobby: " + connect);
        };
        Note(FormattableString.Invariant($"Steam is there ({_api.OwnId})"));
    }

    /// <summary>Leaves the lobby, the session and the rich presence; no state change (the caller sets it).</summary>
    private void LeaveQuietly()
    {
        ulong lobby = Lobby;
        var before = State;
        Lobby = 0;
        _pending = 0;
        HostId = 0;
        HostName = "";
        // State first: the session's end (StopSession -> OnSessionEnded) then finds no lobby to leave twice.
        State = LobbyState.Idle;
        if (lobby != 0)
            _api?.LeaveLobby(lobby);
        ClearPresence();
        if (before == LobbyState.Hosting || before == LobbyState.InLobby)
            _sessions.StopSession();
    }

    private void SetPresence()
    {
        if (_api == null || Lobby == 0)
            return;
        string id = Lobby.ToString(CultureInfo.InvariantCulture);
        _api.SetRichPresence("status", PresenceStatus ?? "");
        // "Join game" in the friends list of a friends-only lobby; a private lobby takes invited friends only.
        _api.SetRichPresence("connect", FriendsCanJoin ? ConnectOption + " " + id : "");
        _api.SetRichPresence("steam_player_group", id);
        _presenceSize = -1;
        _nextPresence = 0;
        UpdatePresence(_clock());
    }

    private void UpdatePresence(double now)
    {
        if (now < _nextPresence || Lobby == 0)
            return;
        _nextPresence = now + PresenceEvery;
        int size = Math.Max(1, _sessions.PlayerCount);
        if (size == _presenceSize)
            return;
        _presenceSize = size;
        _api.SetRichPresence("steam_player_group_size", size.ToString(CultureInfo.InvariantCulture));
    }

    private void ClearPresence()
    {
        _presenceSize = -1;
        _joinable = null;
        _api?.ClearRichPresence();
    }

    private void Fail(LobbyProblem problem, string detail)
    {
        Problem = problem;
        Detail = detail ?? "";
        if (problem != LobbyProblem.SessionEnded)
        {
            EndReason = LeaveReason.Goodbye;
            Rejection = RejectReason.None;
        }
        string text = "failed: " + problem + (Detail.Length > 0 ? " (" + Detail + ")" : "");
        if (problem == LobbyProblem.SessionEnded)
            text += ", session " + EndReason + (Rejection != RejectReason.None ? " / " + Rejection : "");
        Note(text);
        SetState(LobbyState.Failed);
    }

    private void SetState(LobbyState state)
    {
        if (state != LobbyState.Failed)
        {
            Problem = LobbyProblem.None;
            Detail = "";
        }
        if (state == LobbyState.Creating || state == LobbyState.Joining)
            _since = _clock();
        State = state;
        Changed?.Invoke();
    }

    private void Note(string text) => Log?.Invoke("Steam lobby: " + text);

    /// <summary>The lobby id in "+connect_lobby &lt;id&gt;" (start options, one per item, or a whole connect string); 0 = none.</summary>
    public static ulong ParseConnectLobby(IEnumerable<string> args)
    {
        if (args == null)
            return 0;
        var words = new List<string>();
        foreach (string arg in args)
            if (!string.IsNullOrEmpty(arg))
                words.AddRange(arg.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            string value = null;
            if (string.Equals(word, ConnectOption, StringComparison.OrdinalIgnoreCase))
                value = i + 1 < words.Count ? words[i + 1] : null;
            else if (word.StartsWith(ConnectOption + "=", StringComparison.OrdinalIgnoreCase))
                value = word.Substring(ConnectOption.Length + 1);
            if (value != null && ulong.TryParse(value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) && lobby != 0)
                return lobby;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (Lobby != 0)
            _api?.LeaveLobby(Lobby);
        Lobby = 0;
        _api?.ClearRichPresence();
        _api?.Dispose();
        _api = null;
        _disposed = true;
    }
}
