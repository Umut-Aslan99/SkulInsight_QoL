using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Match;
using DamageInsight.Describe;
using Xunit;

namespace DamageInsight.Tests.Coop;

/// <summary>The matchmaking client against the JSON shapes of server/daily/app/coop_api.py (docs/coop/MATCHMAKING.md).</summary>
public class MatchClientTests
{
    private sealed class FakeHttp : IMatchHttp
    {
        public sealed class Call
        {
            public string Method, Path, Body;
            public int Timeout;
            public Action<long, string> Done;
            public bool Answered;
        }

        public readonly List<Call> Calls = new();

        public void Send(string method, string path, string body, int timeoutSeconds, Action<long, string> done) =>
            Calls.Add(new Call { Method = method, Path = path, Body = body, Timeout = timeoutSeconds, Done = done });

        public Call Open(string method, string pathStart) =>
            Calls.LastOrDefault(c => !c.Answered && c.Method == method && c.Path.StartsWith(pathStart, StringComparison.Ordinal));

        public void Answer(string method, string pathStart, long code, string json)
        {
            var call = Open(method, pathStart);
            Assert.NotNull(call);
            call.Answered = true;
            call.Done(code, json);
        }
    }

    private sealed class Rig
    {
        public readonly FakeHttp Http = new();
        public double Now = 100;
        public int SteamPing = 40;
        public readonly MatchClient Client;
        public readonly List<string> Traces = new();
        public MatchInfo ConfirmedMatch;

        public Rig()
        {
            Client = new MatchClient(Http, () => Now, location => location.Length > 0 ? SteamPing : -1);
            Client.Trace += (kind, _) => Traces.Add(kind);
            Client.Confirmed += m => ConfirmedMatch = m;
        }

        public void Queue(int maxPing = 200)
        {
            Client.Start(Ticket(maxPing));
            Http.Answer("POST", "/api/coop/queue", 200, "{\"ok\": true, \"ticket\": \"tick-et-0001\", " + Queued(1).Substring(1));
        }
    }

    private static MatchTicket Ticket(int maxPing = 200) => new()
    {
        Player = "a1b2c3d4e5f6", SteamId = "76561198012345678", Name = "DocRun", Mod = "0.18.0", ProtocolVersion = 1,
        ModListHash = "cbf29ce484222325", Mode = "darkmirror", DmMin = 5, DmMax = 10, DmUnlocked = 7, Dlc = true,
        Size = 2, Language = "de", MaxPing = maxPing, CanHost = true, PingLocation = "fra=12+1,ams=18+1",
    };

    private static string Queued(int version, string notice = null) =>
        "{\"state\": \"queued\", \"version\": " + version + ", \"waitSeconds\": 42, \"maxPing\": 204, \"languageRelaxed\": false, " +
        "\"position\": 1, \"looking\": 4, \"estimatedWait\": " + (version > 1 ? "35" : "null") +
        (notice != null ? ", \"notice\": \"" + notice + "\"" : "") + "}";

    private static string Proposed(int version, bool accepted = false, string id = "match-0001", int maxPing = 220) =>
        "{\"state\": \"proposed\", \"version\": " + version + ", \"waitSeconds\": 50, \"maxPing\": " + maxPing + ", " +
        "\"match\": {\"id\": \"" + id + "\", \"mode\": \"darkmirror\", \"level\": 7, \"dlc\": true, \"size\": 2, " +
        "\"expiresIn\": 15, \"accepted\": " + (accepted ? "true" : "false") + ", \"estimatedPing\": 30, " +
        "\"members\": [{\"name\": \"DocRun\", \"you\": true, \"accepted\": " + (accepted ? "true" : "false") + ", \"estimatedPing\": null, \"pingLocation\": null}, " +
        "{\"name\": \"Tythos\", \"you\": false, \"accepted\": true, \"estimatedPing\": 30, \"pingLocation\": \"ams=8+0,fra=14+1\"}]}}";

    private const string ConfirmedClient =
        "{\"state\": \"confirmed\", \"version\": 6, \"waitSeconds\": 55, " +
        "\"match\": {\"id\": \"match-0001\", \"mode\": \"darkmirror\", \"level\": 7, \"dlc\": true, \"size\": 2, \"youHost\": false, " +
        "\"host\": {\"name\": \"Tythos\", \"you\": false, \"steamId\": \"76561198000000002\"}, " +
        "\"members\": [{\"name\": \"DocRun\", \"you\": true, \"host\": false, \"estimatedPing\": null}, " +
        "{\"name\": \"Tythos\", \"you\": false, \"host\": true, \"estimatedPing\": 30}]}}";

    private const string ConfirmedHost =
        "{\"state\": \"confirmed\", \"version\": 6, \"waitSeconds\": 55, " +
        "\"match\": {\"id\": \"match-0001\", \"mode\": \"normal\", \"level\": -1, \"dlc\": false, \"size\": 2, \"youHost\": true, " +
        "\"host\": {\"name\": \"DocRun\", \"you\": true}, " +
        "\"members\": [{\"name\": \"DocRun\", \"you\": true, \"host\": true, \"estimatedPing\": null}, " +
        "{\"name\": \"Tythos\", \"you\": false, \"host\": false, \"estimatedPing\": 30, \"steamId\": \"76561198000000003\"}]}}";

    [Fact]
    public void The_ticket_has_every_field_of_the_api()
    {
        var n = GearDoc.ParseNode(Ticket().ToJson());
        Assert.Equal("a1b2c3d4e5f6", n.Str("player"));
        Assert.Equal("76561198012345678", n.Str("steamId"));
        Assert.Equal("DocRun", n.Str("name"));
        Assert.Equal("0.18.0", n.Str("mod"));
        Assert.Equal(1, n.Num("protocolVersion"));
        Assert.Equal("cbf29ce484222325", n.Str("modListHash"));
        Assert.Equal("darkmirror", n.Str("mode"));
        Assert.Equal(5, n.Num("dmMin"));
        Assert.Equal(10, n.Num("dmMax"));
        Assert.Equal(7, n.Num("dmUnlocked"));
        Assert.True(n.Bool("dlc"));
        Assert.Equal("any", n.Str("dlcRun"));
        Assert.Equal(2, n.Num("size"));
        Assert.Equal("de", n.Str("language"));
        Assert.Equal(200, n.Num("maxPing"));
        Assert.True(n.Bool("canHost"));
        Assert.Equal("fra=12+1,ams=18+1", n.Str("pingLocation"));
    }

    [Fact]
    public void No_language_and_out_of_range_numbers_stay_inside_the_server_limits()
    {
        var t = Ticket(5000);
        t.Language = null;
        t.DmUnlocked = 99;
        var n = GearDoc.ParseNode(t.ToJson());
        Assert.False(n.Has("language"));
        Assert.Equal(1000, n.Num("maxPing"));
        Assert.Equal(10, n.Num("dmUnlocked"));
    }

    [Fact]
    public void Queueing_then_a_long_poll_with_the_version()
    {
        var rig = new Rig();
        rig.Client.Start(Ticket());
        Assert.Equal(MatchState.Queueing, rig.Client.State);
        Assert.Equal("POST", rig.Http.Calls[0].Method);
        Assert.Equal("/api/coop/queue", rig.Http.Calls[0].Path);
        rig.Http.Answer("POST", "/api/coop/queue", 200, "{\"ok\": true, \"ticket\": \"tick-et-0001\", " + Queued(1).Substring(1));
        Assert.Equal(MatchState.Queued, rig.Client.State);
        Assert.Equal("tick-et-0001", rig.Client.TicketId);
        Assert.Equal(4, rig.Client.Last.Looking);
        Assert.Equal(-1, rig.Client.Last.EstimatedWait);
        var poll = rig.Http.Open("GET", "/api/coop/queue/tick-et-0001");
        Assert.Equal("/api/coop/queue/tick-et-0001?since=1&wait=20", poll.Path);
        Assert.True(poll.Timeout > MatchClient.PollWait);
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Queued(2));
        Assert.Equal(35, rig.Client.Last.EstimatedWait);
        Assert.Equal("/api/coop/queue/tick-et-0001?since=2&wait=20", rig.Http.Open("GET", "/api/coop/queue/").Path);
        Assert.Contains("match-queued", rig.Traces);
    }

    [Fact]
    public void A_proposal_is_shown_and_accepted()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        Assert.Equal(MatchState.Proposed, rig.Client.State);
        Assert.Equal(15, rig.Client.ProposalSecondsLeft);
        Assert.Equal(40, rig.Client.CheckedPing);
        Assert.Equal("Tythos", rig.Client.Match.Partners.Single().Name);
        rig.Now += 5;
        Assert.Equal(10, rig.Client.ProposalSecondsLeft);
        rig.Client.Accept();
        Assert.Equal(MatchState.Accepted, rig.Client.State);
        var accept = rig.Http.Open("POST", "/api/coop/match/match-0001/accept");
        Assert.Contains("\"ticket\":\"tick-et-0001\"", accept.Body);
        rig.Http.Answer("POST", "/api/coop/match/match-0001/accept", 200, Proposed(5, accepted: true));
        Assert.Equal(MatchState.Accepted, rig.Client.State);
        Assert.Contains("match-proposed", rig.Traces);
        Assert.Contains("match-accept", rig.Traces);
    }

    [Fact]
    public void Steam_says_the_ping_is_too_high_so_it_declines_for_free()
    {
        var rig = new Rig { SteamPing = 300 };
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4, maxPing: 220));
        Assert.Equal(MatchState.Queued, rig.Client.State); // no box
        var decline = rig.Http.Open("POST", "/api/coop/match/match-0001/decline");
        Assert.Contains("\"reason\":\"ping\"", decline.Body);
        Assert.Contains("match-ping-decline", rig.Traces);
        rig.Http.Answer("POST", "/api/coop/match/", 200, Queued(5, "ping_check_failed"));
        Assert.Equal(MatchState.Queued, rig.Client.State);
        Assert.True(rig.Client.Searching);
    }

    [Fact]
    public void An_unknown_steam_estimate_does_not_decline()
    {
        var rig = new Rig { SteamPing = -1 };
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        Assert.Equal(MatchState.Proposed, rig.Client.State);
        Assert.Equal(-1, rig.Client.CheckedPing);
    }

    [Fact]
    public void A_client_gets_the_hosts_steam_id_and_finishes_with_delete()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        rig.Client.Accept();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, ConfirmedClient);
        Assert.Equal(MatchState.Confirmed, rig.Client.State);
        Assert.NotNull(rig.ConfirmedMatch);
        Assert.False(rig.ConfirmedMatch.YouHost);
        Assert.Equal("76561198000000002", rig.ConfirmedMatch.HostSteamId);
        Assert.Null(rig.Http.Open("GET", "/api/coop/queue/")); // no more polls
        rig.Client.Finish();
        Assert.Equal(MatchState.Done, rig.Client.State);
        Assert.NotNull(rig.Http.Open("DELETE", "/api/coop/queue/tick-et-0001"));
        Assert.Contains("match-done", rig.Traces);
    }

    [Fact]
    public void The_host_gets_the_members_steam_ids()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, ConfirmedHost);
        Assert.True(rig.ConfirmedMatch.YouHost);
        Assert.Equal(new[] { "76561198000000003" }, rig.ConfirmedMatch.Partners.Select(p => p.SteamId).ToArray());
        Assert.Equal("", rig.ConfirmedMatch.HostSteamId);
    }

    [Fact]
    public void No_session_after_the_match_still_tells_the_server_done()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, ConfirmedClient);
        rig.Now += MatchClient.FinishAfterSeconds + 1;
        rig.Client.Tick();
        Assert.Equal(MatchState.Done, rig.Client.State);
        Assert.NotNull(rig.Http.Open("DELETE", "/api/coop/queue/tick-et-0001"));
    }

    [Fact]
    public void A_cooldown_shows_its_seconds_and_ends()
    {
        var rig = new Rig();
        rig.Client.Start(Ticket());
        rig.Http.Answer("POST", "/api/coop/queue", 409, "{\"detail\": \"cooldown\", \"seconds\": 287}");
        Assert.Equal(MatchState.Cooldown, rig.Client.State);
        Assert.Equal(287, rig.Client.CooldownSecondsLeft);
        Assert.False(rig.Client.CanStart);
        rig.Now += 288;
        rig.Client.Tick();
        Assert.Equal(MatchState.Idle, rig.Client.State);
        Assert.True(rig.Client.CanStart);
    }

    [Fact]
    public void The_server_with_matchmaking_off()
    {
        var rig = new Rig();
        rig.Client.Start(Ticket());
        rig.Http.Answer("POST", "/api/coop/queue", 404, "{\"detail\": \"coop_off\"}");
        Assert.Equal(MatchState.ServerOff, rig.Client.State);
        Assert.True(rig.Client.CanStart);
    }

    [Theory]
    [InlineData(400, "{\"detail\": \"modListHash\"}", MatchProblem.VersionRefused)]
    [InlineData(400, "{\"detail\": \"steamId\"}", MatchProblem.BadField)]
    [InlineData(503, "{\"detail\": \"queue_full\"}", MatchProblem.QueueFull)]
    [InlineData(0, "", MatchProblem.Offline)]
    [InlineData(500, "oops", MatchProblem.ServerError)]
    public void Refused_tickets(long code, string json, MatchProblem problem)
    {
        var rig = new Rig();
        rig.Client.Start(Ticket());
        rig.Http.Answer("POST", "/api/coop/queue", code, json);
        Assert.Equal(MatchState.Failed, rig.Client.State);
        Assert.Equal(problem, rig.Client.Problem);
    }

    [Fact]
    public void Cancel_deletes_the_ticket_and_drops_late_answers()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Client.Cancel();
        Assert.Equal(MatchState.Cancelled, rig.Client.State);
        Assert.NotNull(rig.Http.Open("DELETE", "/api/coop/queue/tick-et-0001"));
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4)); // the long poll that was under way
        Assert.Equal(MatchState.Cancelled, rig.Client.State);
    }

    [Fact]
    public void Cancel_before_the_ticket_came_back_deletes_it_when_it_comes()
    {
        var rig = new Rig();
        rig.Client.Start(Ticket());
        rig.Client.Cancel();
        Assert.Equal(MatchState.Cancelled, rig.Client.State);
        rig.Http.Answer("POST", "/api/coop/queue", 200, "{\"ok\": true, \"ticket\": \"tick-et-0002\", " + Queued(1).Substring(1));
        Assert.Equal(MatchState.Cancelled, rig.Client.State);
        Assert.NotNull(rig.Http.Open("DELETE", "/api/coop/queue/tick-et-0002"));
        Assert.Null(rig.Http.Open("GET", "/api/coop/queue/"));
    }

    [Fact]
    public void A_player_decline_ends_the_search()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        rig.Client.Decline();
        Assert.Equal(MatchState.Cancelled, rig.Client.State);
        Assert.Equal(MatchProblem.Declined, rig.Client.Problem);
        Assert.Contains("\"reason\":\"player\"", rig.Http.Open("POST", "/api/coop/match/match-0001/decline").Body);
    }

    [Theory]
    [InlineData("timeout", MatchState.Failed, MatchProblem.Timeout)]
    [InlineData("expired", MatchState.Failed, MatchProblem.Expired)]
    [InlineData("replaced", MatchState.Failed, MatchProblem.Replaced)]
    [InlineData("declined", MatchState.Cancelled, MatchProblem.Declined)]
    [InlineData("done", MatchState.Done, MatchProblem.None)]
    public void Ended_tickets(string why, MatchState state, MatchProblem problem)
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, "{\"state\": \"" + why + "\", \"version\": -1}");
        Assert.Equal(state, rig.Client.State);
        Assert.Equal(problem, rig.Client.Problem);
        Assert.Null(rig.Http.Open("GET", "/api/coop/queue/"));
    }

    [Fact]
    public void A_partner_declined_puts_it_back_in_the_queue()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        rig.Client.Accept();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Queued(6, "partner_declined"));
        Assert.Equal(MatchState.Queued, rig.Client.State);
        Assert.Equal("partner_declined", rig.Client.Last.Notice);
        Assert.Contains("match-requeued", rig.Traces);
    }

    [Fact]
    public void A_lost_connection_is_retried_then_given_up()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 0, "");
        Assert.Equal(MatchState.Queued, rig.Client.State);
        rig.Client.Tick();
        Assert.Null(rig.Http.Open("GET", "/api/coop/queue/"));
        rig.Now += MatchClient.RetrySeconds + 0.1;
        rig.Client.Tick();
        Assert.NotNull(rig.Http.Open("GET", "/api/coop/queue/"));
        rig.Http.Answer("GET", "/api/coop/queue/", 502, "");
        rig.Now += MatchClient.LostAfterSeconds;
        rig.Client.Tick();
        Assert.Equal(MatchState.Failed, rig.Client.State);
        Assert.Equal(MatchProblem.Offline, rig.Client.Problem);
    }

    [Fact]
    public void An_unknown_ticket_is_lost()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 404, "{\"detail\": \"unknown_ticket\"}");
        Assert.Equal(MatchProblem.Lost, rig.Client.Problem);
    }

    [Fact]
    public void Bots_accept_at_once()
    {
        var rig = new Rig();
        rig.Client.AutoAccept = true;
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        Assert.Equal(MatchState.Accepted, rig.Client.State);
        Assert.NotNull(rig.Http.Open("POST", "/api/coop/match/match-0001/accept"));
    }

    [Fact]
    public void The_queue_answer_can_be_a_proposal_already()
    {
        // The second player of a pair: the server matches at once (seen with the local server, 2026-11-16).
        var rig = new Rig();
        rig.Client.Start(Ticket());
        rig.Http.Answer("POST", "/api/coop/queue", 200, "{\"ok\": true, \"ticket\": \"tick-et-0003\", " + Proposed(2).Substring(1));
        Assert.Equal(MatchState.Proposed, rig.Client.State);
        Assert.Equal("tick-et-0003", rig.Client.TicketId);
        Assert.NotNull(rig.Http.Open("GET", "/api/coop/queue/tick-et-0003?since=2"));
    }

    [Fact]
    public void The_launcher_option_names_the_matchmaking_server()
    {
        var launch = DamageInsight.Coop.Testing.CoopLaunch.Parse(new[] { "--skulinsight-instance", "3", "--skulinsight-matchmaking=http://127.0.0.1:8765/" });
        Assert.Equal("http://127.0.0.1:8765", launch.MatchServer);
        Assert.Null(DamageInsight.Coop.Testing.CoopLaunch.Parse(new[] { "--skulinsight-matchmaking=ftp://x" }).MatchServer);
        Assert.Equal(76561190000000003UL, DamageInsight.Coop.Match.CoopMatchmaking.TestSteamId(launch.Instance));
    }

    [Fact]
    public void An_older_answer_is_ignored()
    {
        var rig = new Rig();
        rig.Queue();
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Proposed(4));
        rig.Client.Accept();
        rig.Http.Answer("POST", "/api/coop/match/", 200, Proposed(5, accepted: true));
        rig.Http.Answer("GET", "/api/coop/queue/", 200, Queued(3));
        Assert.Equal(MatchState.Accepted, rig.Client.State);
    }
}
