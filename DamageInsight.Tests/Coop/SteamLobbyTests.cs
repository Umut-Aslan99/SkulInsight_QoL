using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>SteamLobby's logic over a fake Steam and a fake runtime (the real Steam calls can only be tested in game).</summary>
public class SteamLobbyTests
{
    private const ulong Me = 76561190000000001, Friend = 76561190000000002, LobbyId = 109775240000000001, Other = 109775240000000002;

    private sealed class FakeApi : ISteamLobbyApi
    {
        public ulong Own = Me;
        public Action<bool, ulong, int> Created;
        public readonly Dictionary<ulong, Action<LobbyEnterResult>> Entering = new();
        public readonly List<ulong> Left = new();
        public readonly Dictionary<(ulong, string), string> Data = new();
        public readonly Dictionary<string, string> Presence = new();
        public readonly Dictionary<ulong, ulong> Owners = new();
        public readonly List<(ulong, bool)> Joinable = new();
        public bool FriendsOnly;
        public int Max, Invites, Clears;
        public Action<ulong> Join;
        public Action<string> Connect;
        public ulong OwnId => Own;
        public string OwnName => "Me";
        public void CreateLobby(bool friendsCanJoin, int maxMembers, Action<bool, ulong, int> done)
        {
            FriendsOnly = friendsCanJoin;
            Max = maxMembers;
            Created = done;
        }
        public void JoinLobby(ulong lobby, Action<LobbyEnterResult> done) => Entering[lobby] = done;
        public void LeaveLobby(ulong lobby) => Left.Add(lobby);
        public void SetLobbyData(ulong lobby, string key, string value) => Data[(lobby, key)] = value;
        public string LobbyData(ulong lobby, string key) => Data.TryGetValue((lobby, key), out var v) ? v : "";
        public ulong LobbyOwner(ulong lobby) => Owners.TryGetValue(lobby, out var o) ? o : 0;
        public void SetJoinable(ulong lobby, bool joinable) => Joinable.Add((lobby, joinable));
        public void SetRichPresence(string key, string value) => Presence[key] = value;
        public void ClearRichPresence()
        {
            Presence.Clear();
            Clears++;
        }
        public bool OpenInviteDialog(ulong lobby)
        {
            Invites++;
            return true;
        }
        public Action<ulong> JoinRequested { set => Join = value; }
        public Action<string> ConnectRequested { set => Connect = value; }
        public void Dispose() { }

        /// <summary>A friend's lobby as Steam would have it.</summary>
        public void FriendLobby(ulong lobby, string version = "1.0", ulong host = Friend)
        {
            Data[(lobby, SteamLobby.KeyMod)] = version;
            Data[(lobby, SteamLobby.KeyProtocol)] = Protocol.Version.ToString();
            Data[(lobby, SteamLobby.KeyHost)] = host.ToString();
            Data[(lobby, SteamLobby.KeyName)] = "Friend";
            Owners[lobby] = host;
        }
    }

    private sealed class FakeSessions : ILobbySessions
    {
        public bool Castle = true;
        public int Players;
        public string Error;
        public readonly List<string> Calls = new();
        public Action OnStop;
        public bool InCastle => Castle;
        public int PlayerCount => Players;
        public string StartHost(int maxPlayers)
        {
            Calls.Add("host " + maxPlayers);
            return Error;
        }
        public string StartJoin(ulong hostSteamId)
        {
            Calls.Add("join " + hostSteamId);
            return Error;
        }
        public void StopSession()
        {
            Calls.Add("stop");
            OnStop?.Invoke();
        }
    }

    private double _now = 100;
    private readonly FakeApi _api = new();
    private readonly FakeSessions _sessions = new();

    private SteamLobby Make(bool steam = true, ulong startup = 0) =>
        new(_sessions, "1.0", () => steam ? _api : null, () => _now, startup);

    [Fact]
    public void Host_MakesAFriendsOnlyLobby_WithItsData_AndStartsTheSession()
    {
        var lobby = Make();
        lobby.Host();
        Assert.Equal(LobbyState.Creating, lobby.State);
        Assert.True(_api.FriendsOnly);
        Assert.Equal(4, _api.Max);
        _api.Created(true, LobbyId, 1);
        Assert.Equal(LobbyState.Hosting, lobby.State);
        Assert.Equal(LobbyId, lobby.Lobby);
        Assert.Equal(Me, lobby.HostId);
        Assert.Equal("1.0", _api.Data[(LobbyId, SteamLobby.KeyMod)]);
        Assert.Equal(Protocol.Version.ToString(), _api.Data[(LobbyId, SteamLobby.KeyProtocol)]);
        Assert.Equal(Me.ToString(), _api.Data[(LobbyId, SteamLobby.KeyHost)]);
        Assert.Equal(new[] { "host 4" }, _sessions.Calls);
        Assert.Equal("In a co-op run", _api.Presence["status"]);
        Assert.Equal("+connect_lobby " + LobbyId, _api.Presence["connect"]);
        Assert.Equal(LobbyId.ToString(), _api.Presence["steam_player_group"]);
        Assert.True(lobby.CanInvite);
        Assert.True(lobby.Invite());
        Assert.Equal(1, _api.Invites);
    }

    [Fact]
    public void Host_PrivateLobby_SmallerSize_NoConnectString()
    {
        var lobby = Make();
        lobby.FriendsCanJoin = false;
        lobby.MaxPlayers = 2;
        lobby.Host();
        _api.Created(true, LobbyId, 1);
        Assert.False(_api.FriendsOnly);
        Assert.Equal(2, _api.Max);
        Assert.Equal(new[] { "host 2" }, _sessions.Calls);
        Assert.Equal("", _api.Presence["connect"]);
    }

    [Fact]
    public void Host_WithoutSteam_SaysSo()
    {
        var lobby = Make(steam: false);
        lobby.Host();
        Assert.Equal(LobbyState.Failed, lobby.State);
        Assert.Equal(LobbyProblem.NoSteam, lobby.Problem);
        Assert.Empty(_sessions.Calls);
    }

    [Fact]
    public void Host_OutsideTheCastle_IsRefused()
    {
        _sessions.Castle = false;
        var lobby = Make();
        lobby.Host();
        Assert.Equal(LobbyProblem.NotInCastle, lobby.Problem);
        Assert.Null(_api.Created);
    }

    [Fact]
    public void Host_CreateFails_OrSessionFails()
    {
        var lobby = Make();
        lobby.Host();
        _api.Created(false, 0, 2);
        Assert.Equal(LobbyProblem.CreateFailed, lobby.Problem);
        Assert.Equal("2", lobby.Detail);

        _sessions.Error = "Steam could not open a listen socket";
        lobby.Host();
        _api.Created(true, LobbyId, 1);
        Assert.Equal(LobbyProblem.StartFailed, lobby.Problem);
        Assert.Equal(_sessions.Error, lobby.Detail);
        Assert.Contains(LobbyId, _api.Left);
        Assert.Equal(0ul, lobby.Lobby);
    }

    [Fact]
    public void Host_NoAnswerFromSteam_TimesOut_AndALateLobbyIsLeft()
    {
        var lobby = Make();
        lobby.Host();
        _now += 25;
        lobby.Tick();
        Assert.Equal(LobbyProblem.CreateFailed, lobby.Problem);
        _api.Created(true, LobbyId, 1);
        Assert.Equal(LobbyState.Failed, lobby.State);
        Assert.Contains(LobbyId, _api.Left);
        Assert.Empty(_sessions.Calls);
    }

    [Fact]
    public void Hosting_LobbyIsJoinableOnlyInTheCastle_AndGroupSizeFollowsThePlayers()
    {
        var lobby = Make();
        lobby.Host();
        _api.Created(true, LobbyId, 1);
        lobby.Tick();
        Assert.Equal(new[] { (LobbyId, true) }, _api.Joinable);
        _sessions.Castle = false;
        lobby.Tick();
        Assert.Equal((LobbyId, false), _api.Joinable[_api.Joinable.Count - 1]);
        lobby.Tick();
        Assert.Equal(2, _api.Joinable.Count); // only on change

        Assert.Equal("1", _api.Presence["steam_player_group_size"]);
        _sessions.Players = 2;
        _now += 3;
        lobby.Tick();
        Assert.Equal("2", _api.Presence["steam_player_group_size"]);
    }

    [Fact]
    public void Leave_WhileHosting_StopsTheSession_LeavesTheLobby_ClearsPresence_Once()
    {
        var lobby = Make();
        lobby.Host();
        _api.Created(true, LobbyId, 1);
        // The runtime reports the session's end from inside StopSession (its Leave fires SessionEnded).
        _sessions.OnStop = () => lobby.OnSessionEnded(LeaveReason.LocalLeave, RejectReason.None, "");
        lobby.Leave();
        Assert.Equal(LobbyState.Idle, lobby.State);
        Assert.Equal(new[] { LobbyId }, _api.Left);
        Assert.Contains("stop", _sessions.Calls);
        Assert.Empty(_api.Presence);
        Assert.Equal(LobbyProblem.None, lobby.Problem);
    }

    [Fact]
    public void Join_EntersTheLobby_ChecksIt_AndConnectsToTheHost()
    {
        _api.FriendLobby(LobbyId);
        var lobby = Make();
        lobby.Tick(); // finds Steam, wires the join callbacks
        _api.Join(LobbyId);
        Assert.Equal(LobbyState.Joining, lobby.State);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        Assert.Equal(LobbyState.InLobby, lobby.State);
        Assert.Equal(Friend, lobby.HostId);
        Assert.Equal("Friend", lobby.HostName);
        Assert.Equal(new[] { "join " + Friend }, _sessions.Calls);
        Assert.Equal("+connect_lobby " + LobbyId, _api.Presence["connect"]);
        Assert.True(lobby.CanInvite);
        Assert.False(lobby.CanHost);
    }

    [Fact]
    public void Join_ThroughTheRichPresenceConnectString()
    {
        _api.FriendLobby(LobbyId);
        var lobby = Make();
        lobby.Tick();
        _api.Connect("+connect_lobby " + LobbyId);
        Assert.Equal(LobbyState.Joining, lobby.State);
        Assert.True(_api.Entering.ContainsKey(LobbyId));
    }

    [Fact]
    public void Join_OtherVersion_IsRefusedBeforeConnecting()
    {
        _api.FriendLobby(LobbyId, version: "0.9");
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        Assert.Equal(LobbyProblem.VersionMismatch, lobby.Problem);
        Assert.Equal("0.9", lobby.Detail);
        Assert.Contains(LobbyId, _api.Left);
        Assert.Empty(_sessions.Calls);
    }

    [Theory]
    [InlineData(LobbyEnterResult.Full, LobbyProblem.LobbyFull)]
    [InlineData(LobbyEnterResult.DoesntExist, LobbyProblem.LobbyGone)]
    [InlineData(LobbyEnterResult.Banned, LobbyProblem.JoinFailed)]
    public void Join_SteamSaysNo(int response, LobbyProblem problem)
    {
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, response));
        Assert.Equal(LobbyState.Failed, lobby.State);
        Assert.Equal(problem, lobby.Problem);
        Assert.Empty(_sessions.Calls);
    }

    [Fact]
    public void Join_TheHostHasLeft_LobbyGone()
    {
        _api.FriendLobby(LobbyId);
        _api.Owners[LobbyId] = 76561190000000003; // Steam gave the lobby to another member
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        Assert.Equal(LobbyProblem.LobbyGone, lobby.Problem);
        Assert.Contains(LobbyId, _api.Left);
    }

    [Fact]
    public void Join_OutsideTheCastle_Waits_ThenJoins()
    {
        _api.FriendLobby(LobbyId);
        _sessions.Castle = false;
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        Assert.Equal(LobbyState.WaitingForCastle, lobby.State);
        Assert.Equal(LobbyId, lobby.PendingLobby);
        lobby.Tick();
        Assert.Empty(_api.Entering);
        _sessions.Castle = true;
        lobby.Tick();
        Assert.Equal(LobbyState.Joining, lobby.State);
        Assert.True(_api.Entering.ContainsKey(LobbyId));
    }

    [Fact]
    public void StartupOption_JoinsOnceSteamAndTheCastleAreThere()
    {
        _api.FriendLobby(LobbyId);
        bool steam = false;
        _sessions.Castle = false;
        var lobby = new SteamLobby(_sessions, "1.0", () => steam ? _api : null, () => _now, LobbyId);
        Assert.Equal(LobbyState.WaitingForCastle, lobby.State);
        lobby.Tick();
        steam = true;
        _now += 2;
        lobby.Tick(); // Steam there, still loading
        Assert.Empty(_api.Entering);
        _sessions.Castle = true;
        lobby.Tick();
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        Assert.Equal(LobbyState.InLobby, lobby.State);
    }

    [Fact]
    public void Leave_CancelsAWaitingJoin()
    {
        _sessions.Castle = false;
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        lobby.Leave();
        Assert.Equal(LobbyState.Idle, lobby.State);
        _sessions.Castle = true;
        lobby.Tick();
        Assert.Empty(_api.Entering);
        Assert.Empty(_sessions.Calls); // no session was running: nothing to stop
    }

    [Fact]
    public void AnInviteWhileHosting_LeavesTheOwnLobby_AndJoinsTheFriend()
    {
        _api.FriendLobby(Other);
        var lobby = Make();
        lobby.Host();
        _api.Created(true, LobbyId, 1);
        lobby.RequestJoin(Other);
        Assert.Contains(LobbyId, _api.Left);
        Assert.Contains("stop", _sessions.Calls);
        Assert.Equal(LobbyState.Joining, lobby.State);
        _api.Entering[Other](new LobbyEnterResult(Other, false, LobbyEnterResult.Success));
        Assert.Equal(LobbyState.InLobby, lobby.State);
        Assert.Equal(Friend, lobby.HostId);
    }

    [Fact]
    public void ALateEnterAnswer_ForAJoinGivenUp_IsLeft()
    {
        _api.FriendLobby(LobbyId);
        _api.FriendLobby(Other);
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        lobby.RequestJoin(Other);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        Assert.Contains(LobbyId, _api.Left);
        Assert.Equal(LobbyState.Joining, lobby.State);
        _api.Entering[Other](new LobbyEnterResult(Other, false, LobbyEnterResult.Success));
        Assert.Equal(LobbyState.InLobby, lobby.State);
        Assert.Equal(new[] { "join " + Friend }, _sessions.Calls);
    }

    [Fact]
    public void SessionEnds_Rejected_KeepsTheReason_AndLeavesTheLobby()
    {
        _api.FriendLobby(LobbyId);
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        lobby.OnSessionEnded(LeaveReason.Rejected, RejectReason.ModListMismatch, "1.0");
        Assert.Equal(LobbyState.Failed, lobby.State);
        Assert.Equal(LobbyProblem.SessionEnded, lobby.Problem);
        Assert.Equal(LeaveReason.Rejected, lobby.EndReason);
        Assert.Equal(RejectReason.ModListMismatch, lobby.Rejection);
        Assert.Contains(LobbyId, _api.Left);
        Assert.Empty(_api.Presence);
        Assert.True(lobby.CanHost);
    }

    [Fact]
    public void SessionEnds_HostLeft_Then_HostAgainClearsTheProblem()
    {
        _api.FriendLobby(LobbyId);
        var lobby = Make();
        lobby.RequestJoin(LobbyId);
        _api.Entering[LobbyId](new LobbyEnterResult(LobbyId, false, LobbyEnterResult.Success));
        lobby.OnSessionEnded(LeaveReason.HostLeft, RejectReason.None, "");
        Assert.Equal(LeaveReason.HostLeft, lobby.EndReason);
        lobby.Host();
        Assert.Equal(LobbyState.Creating, lobby.State);
        Assert.Equal(LobbyProblem.None, lobby.Problem);
    }

    [Theory]
    [InlineData(new[] { "Skul.exe", "+connect_lobby", "109775240000000001" }, 109775240000000001ul)]
    [InlineData(new[] { "Skul.exe", "-foo", "+connect_lobby 109775240000000001" }, 109775240000000001ul)]
    [InlineData(new[] { "+CONNECT_LOBBY=109775240000000001" }, 109775240000000001ul)]
    [InlineData(new[] { "Skul.exe", "+connect_lobby" }, 0ul)]
    [InlineData(new[] { "Skul.exe", "+connect_lobby", "abc" }, 0ul)]
    [InlineData(new[] { "Skul.exe" }, 0ul)]
    public void ParseConnectLobby(string[] args, ulong expected) => Assert.Equal(expected, SteamLobby.ParseConnectLobby(args));
}
