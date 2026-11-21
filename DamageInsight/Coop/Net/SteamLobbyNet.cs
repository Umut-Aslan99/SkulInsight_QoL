using System;
using Steamworks;

namespace DamageInsight.Coop.Net;

/// <summary>
/// Steam lobbies, invites and rich presence for co-op through the game's Steamworks.NET 14.0 (Platforms.Steam.dll;
/// the game starts Steam in SteamPlatform.Initialize and runs its callbacks every frame in SteamPlatform.Update). With
/// SteamNet.cs the only co-op code that calls Steamworks; <see cref="SteamLobby"/>'s logic is tested with a fake.
/// Made only once Steam is started (<see cref="TryCreate"/>: the pipe exists), so the callbacks are registered
/// against a live Steam. Every call is guarded: a failure is logged and answered as "failed", never thrown.
/// Lobby answers come as call results AND as callbacks (Steam posts LobbyEnter_t to every listener): each pending
/// answer is handed over once, whichever arrives first.
/// </summary>
internal sealed class SteamworksLobby : ISteamLobbyApi
{
    private static bool _broken;

    private Callback<GameLobbyJoinRequested_t> _joinRequested;
    private Callback<GameRichPresenceJoinRequested_t> _connectRequested;
    private Callback<LobbyEnter_t> _entered;
    private CallResult<LobbyCreated_t> _createCall;
    private CallResult<LobbyEnter_t> _joinCall;
    private Action<bool, ulong, int> _createDone;
    private Action<LobbyEnterResult> _joinDone;
    private ulong _joining;
    private Action<ulong> _onJoinRequested;
    private Action<string> _onConnectRequested;

    private SteamworksLobby()
    {
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        _connectRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnConnectRequested);
        _entered = Callback<LobbyEnter_t>.Create(OnEnteredCallback);
        _createCall = CallResult<LobbyCreated_t>.Create(OnCreated);
        _joinCall = CallResult<LobbyEnter_t>.Create(OnEnteredCall);
    }

    /// <summary>null while Steam isn't started (asked again later), or for good when Steamworks is missing.</summary>
    public static SteamworksLobby TryCreate()
    {
        if (_broken)
            return null;
        try
        {
            if (!SteamAPI.IsSteamRunning() || SteamAPI.GetHSteamPipe() == (HSteamPipe)0)
                return null; // Steam not running, or the game hasn't started it (yet)
            return new SteamworksLobby();
        }
        catch (Exception e)
        {
            _broken = true; // another platform's build, or no steam_api64: never asked again
            Warn("Steam lobbies are not available", e);
            return null;
        }
    }

    private static void Warn(string what, Exception e)
    {
        try
        {
            Plugin.Log?.LogWarning($"[Coop] {what} ({e.GetType().Name}: {e.Message})");
        }
        catch (Exception)
        {
            // No plugin (tests): nothing to log to.
        }
    }

    public ulong OwnId
    {
        get
        {
            try { return SteamUser.GetSteamID().m_SteamID; }
            catch (Exception) { return 0; }
        }
    }

    public string OwnName
    {
        get
        {
            try { return SteamFriends.GetPersonaName() ?? ""; }
            catch (Exception) { return ""; }
        }
    }

    public Action<ulong> JoinRequested { set => _onJoinRequested = value; }
    public Action<string> ConnectRequested { set => _onConnectRequested = value; }

    // ---- lobbies ------------------------------------------------------------------------------------------------

    public void CreateLobby(bool friendsCanJoin, int maxMembers, Action<bool, ulong, int> done)
    {
        _createDone = done;
        try
        {
            // REVIEW(fable): FriendsOnly lets every Steam friend join from the friends list (the setting can make it Private).
            var call = SteamMatchmaking.CreateLobby(friendsCanJoin ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePrivate, maxMembers);
            if (call == SteamAPICall_t.Invalid)
            {
                Created(false, 0, 0);
                return;
            }
            _createCall.Set(call);
        }
        catch (Exception e)
        {
            Warn("CreateLobby failed", e);
            Created(false, 0, 0);
        }
    }

    private void OnCreated(LobbyCreated_t result, bool ioFailure)
    {
        try
        {
            bool ok = !ioFailure && result.m_eResult == EResult.k_EResultOK && result.m_ulSteamIDLobby != 0;
            Created(ok, ok ? result.m_ulSteamIDLobby : 0, ioFailure ? -1 : (int)result.m_eResult);
        }
        catch (Exception e)
        {
            Warn("the lobby's answer could not be read", e);
        }
    }

    private void Created(bool ok, ulong lobby, int result)
    {
        var done = _createDone;
        _createDone = null;
        done?.Invoke(ok, lobby, result);
    }

    public void JoinLobby(ulong lobby, Action<LobbyEnterResult> done)
    {
        _joinDone = done;
        _joining = lobby;
        try
        {
            var call = SteamMatchmaking.JoinLobby(new CSteamID(lobby));
            if (call == SteamAPICall_t.Invalid)
            {
                Entered(new LobbyEnterResult(lobby, true, 0));
                return;
            }
            _joinCall.Set(call);
        }
        catch (Exception e)
        {
            Warn("JoinLobby failed", e);
            Entered(new LobbyEnterResult(lobby, true, 0));
        }
    }

    private void OnEnteredCall(LobbyEnter_t result, bool ioFailure)
    {
        try
        {
            Entered(new LobbyEnterResult(ioFailure ? _joining : result.m_ulSteamIDLobby, ioFailure, (int)result.m_EChatRoomEnterResponse));
        }
        catch (Exception e)
        {
            Warn("the lobby's answer could not be read", e);
        }
    }

    /// <summary>LobbyEnter_t as a callback: also for the host's own new lobby (no join waits then: ignored).</summary>
    private void OnEnteredCallback(LobbyEnter_t result)
    {
        try
        {
            if (_joinDone != null && result.m_ulSteamIDLobby == _joining)
                Entered(new LobbyEnterResult(result.m_ulSteamIDLobby, false, (int)result.m_EChatRoomEnterResponse));
        }
        catch (Exception e)
        {
            Warn("the lobby's answer could not be read", e);
        }
    }

    private void Entered(LobbyEnterResult result)
    {
        var done = _joinDone;
        _joinDone = null;
        _joining = 0;
        done?.Invoke(result);
    }

    public void LeaveLobby(ulong lobby)
    {
        if (lobby == _joining)
        {
            _joinDone = null;
            _joining = 0;
        }
        try { SteamMatchmaking.LeaveLobby(new CSteamID(lobby)); }
        catch (Exception e) { Warn("LeaveLobby failed", e); }
    }

    public void SetLobbyData(ulong lobby, string key, string value)
    {
        try
        {
            if (!SteamMatchmaking.SetLobbyData(new CSteamID(lobby), key, value ?? ""))
                Plugin.Log?.LogWarning("[Coop] Steam lobby: could not set " + key);
        }
        catch (Exception e) { Warn("SetLobbyData failed", e); }
    }

    public string LobbyData(ulong lobby, string key)
    {
        try { return SteamMatchmaking.GetLobbyData(new CSteamID(lobby), key) ?? ""; }
        catch (Exception) { return ""; }
    }

    public ulong LobbyOwner(ulong lobby)
    {
        try { return SteamMatchmaking.GetLobbyOwner(new CSteamID(lobby)).m_SteamID; }
        catch (Exception) { return 0; }
    }

    public void SetJoinable(ulong lobby, bool joinable)
    {
        try { SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobby), joinable); }
        catch (Exception e) { Warn("SetLobbyJoinable failed", e); }
    }

    // ---- friends ------------------------------------------------------------------------------------------------

    public void SetRichPresence(string key, string value)
    {
        try { SteamFriends.SetRichPresence(key, value ?? ""); }
        catch (Exception e) { Warn("SetRichPresence failed", e); }
    }

    public void ClearRichPresence()
    {
        try { SteamFriends.ClearRichPresence(); }
        catch (Exception e) { Warn("ClearRichPresence failed", e); }
    }

    public bool OpenInviteDialog(ulong lobby)
    {
        try
        {
            if (!SteamUtils.IsOverlayEnabled())
                return false;
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby));
            return true;
        }
        catch (Exception e)
        {
            Warn("the invite dialog could not open", e);
            return false;
        }
    }

    private void OnJoinRequested(GameLobbyJoinRequested_t request)
    {
        try
        {
            Plugin.Log?.LogInfo($"[Coop] Steam: join request for lobby {request.m_steamIDLobby.m_SteamID} (from {request.m_steamIDFriend.m_SteamID})");
            _onJoinRequested?.Invoke(request.m_steamIDLobby.m_SteamID);
        }
        catch (Exception e)
        {
            Warn("a join request could not be handled", e);
        }
    }

    private void OnConnectRequested(GameRichPresenceJoinRequested_t request)
    {
        try
        {
            Plugin.Log?.LogInfo($"[Coop] Steam: rich presence join \"{request.m_rgchConnect}\" (from {request.m_steamIDFriend.m_SteamID})");
            _onConnectRequested?.Invoke(request.m_rgchConnect ?? "");
        }
        catch (Exception e)
        {
            Warn("a join request could not be handled", e);
        }
    }

    public void Dispose()
    {
        try
        {
            _joinRequested?.Dispose();
            _connectRequested?.Dispose();
            _entered?.Dispose();
            _createCall?.Dispose();
            _joinCall?.Dispose();
        }
        catch (Exception)
        {
        }
        _joinRequested = null;
        _connectRequested = null;
        _entered = null;
        _createCall = null;
        _joinCall = null;
        _createDone = null;
        _joinDone = null;
    }
}
