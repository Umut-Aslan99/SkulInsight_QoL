using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The co-op session (package A) on in-memory transports with a fake clock: handshake, rejects, roster,
/// ping, timeouts, goodbye, game messages and the host's relay, up to a full party of four.</summary>
public class CoopSessionTests
{
    private const double Frame = 1 / 60.0;

    /// <summary>What polling once a frame adds to a measured round trip: each way waits for the next frame, and the
    /// way to the host also for the host's next update (it updates first).</summary>
    private const double SlackMs = 3 * Frame * 1000;

    /// <summary>Holds what a transport receives for <see cref="Delay"/> seconds of the fake clock (order kept).</summary>
    private sealed class DelayTransport : ITransport, ITransportListener
    {
        private readonly ITransport _inner;
        private readonly Func<double> _now;
        private readonly Queue<(double at, int kind, PeerId peer, byte[] bytes, Delivery delivery, string reason)> _held = new();
        public double Delay;

        public DelayTransport(ITransport inner, Func<double> now, double delay)
        {
            _inner = inner;
            _now = now;
            Delay = delay;
        }

        public PeerId Self => _inner.Self;
        public void Send(PeerId to, ArraySegment<byte> message, Delivery delivery) => _inner.Send(to, message, delivery);
        public void Disconnect(PeerId peer, string reason) => _inner.Disconnect(peer, reason);
        public ConnectionQuality QualityOf(PeerId peer) => _inner.QualityOf(peer);
        public void Dispose() => _inner.Dispose();

        public void Poll(ITransportListener listener)
        {
            _inner.Poll(this);
            while (_held.Count > 0 && _held.Peek().at <= _now() + 1e-9)
            {
                var e = _held.Dequeue();
                if (e.kind == 0) listener.OnConnected(e.peer);
                else if (e.kind == 1) listener.OnDisconnected(e.peer, e.reason);
                else listener.OnMessage(e.peer, new ArraySegment<byte>(e.bytes), e.delivery);
            }
        }

        public void OnConnected(PeerId peer) => _held.Enqueue((_now() + Delay, 0, peer, null, Delivery.Reliable, null));
        public void OnDisconnected(PeerId peer, string reason) => _held.Enqueue((_now() + Delay, 1, peer, null, Delivery.Reliable, reason));
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery) =>
            _held.Enqueue((_now() + Delay, 2, from, message.ToArray(), delivery, null));
    }

    /// <summary>A host and its clients on one loopback hub, each with its own clock offset; records every event.</summary>
    private sealed class Rig
    {
        public readonly LoopbackTransport.Hub Hub = new();
        public readonly LoopbackTransport HostTransport;
        public readonly CoopSession Host;
        public readonly List<CoopSession> Clients = new();
        public readonly List<ITransport> ClientTransports = new();
        public readonly Dictionary<CoopSession, List<string>> Events = new();
        public readonly HashSet<CoopSession> Frozen = new();
        private readonly Dictionary<CoopSession, double> _shift = new();
        public double Now = 100;

        public Rig(SessionOptions hostOptions = null, double hostDelay = 0)
        {
            HostTransport = Hub.Join();
            ITransport t = hostDelay > 0 ? new DelayTransport(HostTransport, () => Now, hostDelay) : HostTransport;
            Host = CoopSession.Host(t, hostOptions ?? Options("Host", 1000), Now);
            Watch(Host, 0);
        }

        public static SessionOptions Options(string name, ulong id, string version = "0.17.0", string mods = "abc") => new()
        {
            PlayerName = name, PlayerId = id, ModVersion = version, ModListHash = mods,
        };

        public CoopSession Join(string name, ulong id, SessionOptions options = null, double clockShift = 0, double delay = 0)
        {
            var lt = Hub.Join();
            Hub.Connect(HostTransport, lt);
            ITransport t = delay > 0 ? new DelayTransport(lt, () => Now, delay) : lt;
            var session = CoopSession.Join(t, HostTransport.Self, options ?? Options(name, id), Now + clockShift);
            Clients.Add(session);
            ClientTransports.Add(t);
            Watch(session, clockShift);
            return session;
        }

        /// <summary>A client session made outside <see cref="Join"/> (its own transport), run and watched like the others.</summary>
        public CoopSession Adopt(CoopSession s)
        {
            Clients.Add(s);
            Watch(s, 0);
            return s;
        }

        private void Watch(CoopSession s, double shift)
        {
            _shift[s] = shift;
            var log = Events[s] = new List<string>();
            s.Started += () => log.Add("started");
            s.PlayerJoined += e => log.Add($"joined {e.Slot} {e.Name}");
            s.PlayerLeft += (e, why) => log.Add($"left {e.Slot} {e.Name} {why}");
            s.SessionEnded += why => log.Add($"ended {why}");
        }

        public void Run(double seconds)
        {
            int frames = (int)Math.Round(seconds / Frame);
            for (int i = 0; i < frames; i++)
            {
                Now += Frame;
                foreach (var s in new[] { Host }.Concat(Clients))
                    if (!Frozen.Contains(s))
                        s.Update(Now + _shift[s]);
            }
        }

        public List<string> Of(CoopSession s) => Events[s];
    }

    private static string Names(CoopSession s) => string.Join(",", s.Roster.Select(e => e.ToString()));

    // ---- handshake ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_client_joins_and_both_sides_see_the_same_roster()
    {
        var rig = new Rig();
        var bob = rig.Join("Bob", 2000);
        Assert.Equal(SessionState.Connecting, bob.State);
        Assert.Equal(-1, bob.LocalSlot);
        rig.Run(0.1);

        Assert.True(rig.Host.IsHost);
        Assert.False(bob.IsHost);
        Assert.Equal(SessionState.Active, bob.State);
        Assert.Equal(1, bob.LocalSlot);
        Assert.Equal(0, rig.Host.LocalSlot);
        Assert.Equal("0:Host,1:Bob", Names(rig.Host));
        Assert.Equal("0:Host,1:Bob", Names(bob));
        Assert.Equal(2000ul, bob.LocalPlayer.PlayerId);
        Assert.Equal(rig.HostTransport.Self, bob.PlayerAt(0).Peer);
        Assert.True(bob.PlayerAt(0).IsHost);
        Assert.Null(bob.PlayerAt(2));
        Assert.Null(bob.PlayerAt(9));
        // The Hello went out at the start and again on connect: answered twice, joined once.
        Assert.Equal(new[] { "joined 1 Bob" }, rig.Of(rig.Host));
        Assert.Equal(new[] { "started", "joined 0 Host" }, rig.Of(bob));
    }

    [Fact]
    public void A_client_whose_transport_connects_later_says_hello_on_connect()
    {
        var rig = new Rig();
        var lt = rig.Hub.Join();
        var bob = CoopSession.Join(lt, rig.HostTransport.Self, Rig.Options("Bob", 2), rig.Now); // not connected: dropped
        rig.Run(0.2);
        Assert.Equal(SessionState.Connecting, bob.State);
        rig.Hub.Connect(rig.HostTransport, lt);
        for (int i = 0; i < 5; i++)
        {
            rig.Now += Frame;
            bob.Update(rig.Now);
            rig.Host.Update(rig.Now);
        }
        Assert.Equal(SessionState.Active, bob.State);
        Assert.Equal(1, bob.LocalSlot);
    }

    [Fact]
    public void A_different_protocol_version_is_rejected_with_the_hosts_versions()
    {
        var rig = new Rig();
        var lt = rig.Hub.Join();
        rig.Hub.Connect(rig.HostTransport, lt);
        // A future game: its Hello starts with protocol 2, the rest may look different.
        var hello = new NetWriter();
        hello.WriteByte((byte)MessageType.Hello);
        hello.WriteUInt16((ushort)(Protocol.Version + 1));
        hello.WriteFloat(123); // a layout this version doesn't know
        lt.Send(rig.HostTransport.Self, hello.ToSegment(), Delivery.Reliable);
        rig.Run(0.1);

        var answers = new List<byte[]>();
        lt.Poll(new Collector(answers));
        var reject = answers.Single(a => a[0] == (byte)MessageType.Reject);
        var r = new NetReader(new ArraySegment<byte>(reject, 1, reject.Length - 1));
        Assert.Equal(RejectReason.VersionMismatch, (RejectReason)r.ReadByte());
        Assert.Equal(Protocol.Version, r.ReadUInt16());
        Assert.Equal("0.17.0", r.ReadString());
        Assert.Equal("0:Host", Names(rig.Host));
        Assert.Empty(rig.Of(rig.Host));
    }

    private sealed class Collector : ITransportListener
    {
        private readonly List<byte[]> _messages;
        public readonly List<string> Disconnects = new();
        public Collector(List<byte[]> messages) => _messages = messages;
        public void OnConnected(PeerId peer) { }
        public void OnDisconnected(PeerId peer, string reason) => Disconnects.Add(reason);
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery) => _messages.Add(message.ToArray());
    }

    [Fact]
    public void Another_mod_version_is_rejected_and_the_client_hears_why()
    {
        var rig = new Rig();
        var bob = rig.Join("Bob", 2, Rig.Options("Bob", 2, version: "0.16.2"));
        rig.Run(0.1);
        Assert.Equal(SessionState.Ended, bob.State);
        Assert.Equal(LeaveReason.Rejected, bob.EndReason);
        Assert.Equal(RejectReason.VersionMismatch, bob.Rejection);
        Assert.Equal("0.17.0", bob.HostModVersion);
        Assert.Equal(Protocol.Version, bob.HostProtocolVersion);
        Assert.Equal(new[] { "ended Rejected" }, rig.Of(bob));
        Assert.Equal("0:Host", Names(rig.Host));

        // The host keeps the rejected connection a moment (so the reason arrives), then closes it.
        rig.Run(1.5);
        Assert.Empty(rig.Of(rig.Host));
    }

    [Fact]
    public void Another_mod_list_is_rejected()
    {
        var rig = new Rig();
        var bob = rig.Join("Bob", 2, Rig.Options("Bob", 2, mods: "different"));
        rig.Run(0.1);
        Assert.Equal(LeaveReason.Rejected, bob.EndReason);
        Assert.Equal(RejectReason.ModListMismatch, bob.Rejection);
    }

    [Fact]
    public void Versions_may_differ_when_the_host_allows_it()
    {
        var options = Rig.Options("Host", 1);
        options.RequireSameModVersion = false;
        options.RequireSameModList = false;
        var rig = new Rig(options);
        var bob = rig.Join("Bob", 2, Rig.Options("Bob", 2, version: "0.17.1", mods: "x"));
        rig.Run(0.1);
        Assert.Equal(SessionState.Active, bob.State);
    }

    [Fact]
    public void A_fifth_player_is_turned_away_as_full()
    {
        var rig = new Rig();
        var clients = Enumerable.Range(1, 3).Select(i => rig.Join("P" + i, (ulong)i)).ToList();
        rig.Run(0.1);
        var fifth = rig.Join("P5", 5);
        rig.Run(0.1);
        Assert.All(clients, c => Assert.Equal(SessionState.Active, c.State));
        Assert.Equal(RejectReason.Full, fifth.Rejection);
        Assert.Equal(4, rig.Host.Roster.Count);
    }

    [Fact]
    public void A_two_player_host_turns_away_the_third()
    {
        var options = Rig.Options("Host", 1);
        options.MaxPlayers = 2;
        var rig = new Rig(options);
        var bob = rig.Join("Bob", 2);
        rig.Run(0.1);
        var carol = rig.Join("Carol", 3);
        rig.Run(0.1);
        Assert.Equal(SessionState.Active, bob.State);
        Assert.Equal(RejectReason.Full, carol.Rejection);
    }

    [Fact]
    public void The_same_player_id_twice_is_rejected()
    {
        var rig = new Rig();
        rig.Join("Bob", 2);
        rig.Run(0.1);
        var copy = rig.Join("Bob again", 2);
        var hostCopy = rig.Join("Host twin", 1000);
        rig.Run(0.1);
        Assert.Equal(RejectReason.DuplicatePlayer, copy.Rejection);
        Assert.Equal(RejectReason.DuplicatePlayer, hostCopy.Rejection);
    }

    [Fact]
    public void A_host_not_accepting_players_turns_them_away()
    {
        var rig = new Rig();
        rig.Host.AcceptingPlayers = false;
        var bob = rig.Join("Bob", 2);
        rig.Run(0.1);
        Assert.Equal(RejectReason.NotAccepting, bob.Rejection);
    }

    [Fact]
    public void A_matchmaking_host_lets_in_only_the_matched_players()
    {
        var rig = new Rig();
        rig.Host.Admit = (peer, playerId) => playerId == 2;
        var bob = rig.Join("Bob", 2);
        var carol = rig.Join("Carol", 3);
        rig.Run(0.1);
        Assert.Equal(SessionState.Active, bob.State);
        Assert.Equal(RejectReason.NotAccepting, carol.Rejection);
    }

    [Fact]
    public void A_broken_hello_drops_the_peer()
    {
        var rig = new Rig();
        var lt = rig.Hub.Join();
        rig.Hub.Connect(rig.HostTransport, lt);
        lt.Send(rig.HostTransport.Self, new ArraySegment<byte>(new byte[] { (byte)MessageType.Hello, 1 }), Delivery.Reliable);
        rig.Run(0.1);
        var collector = new Collector(new List<byte[]>());
        lt.Poll(collector);
        Assert.Single(collector.Disconnects);
        Assert.Equal(1, rig.Host.MalformedMessages);
        Assert.Equal("0:Host", Names(rig.Host));
    }

    [Fact]
    public void A_handshake_without_an_answer_times_out()
    {
        var rig = new Rig();
        var bob = rig.Join("Bob", 2);
        rig.Frozen.Add(rig.Host); // the host's game hangs
        rig.Run(10.5);
        Assert.Equal(LeaveReason.Timeout, bob.EndReason);
        Assert.Equal(new[] { "ended Timeout" }, rig.Of(bob));
    }

    [Fact]
    public void A_connection_that_never_says_hello_is_dropped()
    {
        var rig = new Rig();
        var lt = rig.Hub.Join();
        rig.Hub.Connect(rig.HostTransport, lt);
        rig.Run(10.5);
        var collector = new Collector(new List<byte[]>());
        lt.Poll(collector);
        Assert.Single(collector.Disconnects);
        Assert.Empty(rig.Of(rig.Host));
    }

    // ---- roster -------------------------------------------------------------------------------------------------

    [Fact]
    public void Roster_changes_reach_every_client()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        rig.Run(0.1);
        var b = rig.Join("Ben", 2);
        rig.Run(0.1);
        var c = rig.Join("Cat", 3);
        rig.Run(0.1);

        foreach (var s in new[] { rig.Host, a, b, c })
            Assert.Equal("0:Host,1:Ann,2:Ben,3:Cat", Names(s));
        Assert.Equal(new[] { "started", "joined 0 Host", "joined 2 Ben", "joined 3 Cat" }, rig.Of(a));
        Assert.Equal(new[] { "started", "joined 0 Host", "joined 1 Ann", "joined 3 Cat" }, rig.Of(b));
        Assert.Equal(new[] { "started", "joined 0 Host", "joined 1 Ann", "joined 2 Ben" }, rig.Of(c));
        Assert.Equal(new[] { "joined 1 Ann", "joined 2 Ben", "joined 3 Cat" }, rig.Of(rig.Host));

        b.Leave();
        rig.Run(0.1);
        foreach (var s in new[] { rig.Host, a, c })
            Assert.Equal("0:Host,1:Ann,3:Cat", Names(s));
        Assert.Equal("left 2 Ben Goodbye", rig.Of(a).Last());
        Assert.Equal("left 2 Ben Goodbye", rig.Of(c).Last());
        Assert.Equal("left 2 Ben Goodbye", rig.Of(rig.Host).Last());
        Assert.Equal("ended LocalLeave", rig.Of(b).Last());
        Assert.Equal(SessionState.Ended, b.State);

        // The free slot is taken by the next player.
        var d = rig.Join("Dan", 4);
        rig.Run(0.1);
        Assert.Equal(2, d.LocalSlot);
        Assert.Equal("joined 2 Dan", rig.Of(a).Last());
        Assert.Equal("0:Host,1:Ann,2:Dan,3:Cat", Names(c));
    }

    [Fact]
    public void Roster_entries_that_did_not_change_stay_the_same_objects()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        rig.Run(0.1);
        var hostEntry = a.PlayerAt(0);
        rig.Join("Ben", 2);
        rig.Run(0.1);
        Assert.Same(hostEntry, a.PlayerAt(0));
    }

    [Fact]
    public void Names_from_clients_are_cleaned()
    {
        var rig = new Rig();
        var a = rig.Join("  \u0007Ann\n" + new string('n', 60), 1);
        var b = rig.Join("   ", 2);
        rig.Run(0.1);
        Assert.Equal(Protocol.MaxNameChars, a.LocalPlayer.Name.Length);
        Assert.StartsWith("Annnn", a.LocalPlayer.Name);
        Assert.Equal("Player 3", b.LocalPlayer.Name);
    }

    // ---- ping and time ------------------------------------------------------------------------------------------

    [Fact]
    public void Ping_is_measured_both_ways_with_a_real_delay()
    {
        // 60 ms to the client, 40 ms back to the host: a 100 ms round trip (plus up to a frame of polling).
        var rig = new Rig(hostDelay: 0.04);
        var bob = rig.Join("Bob", 2, delay: 0.06);
        Assert.Equal(-1, bob.PingMs(0));
        rig.Run(3);

        Assert.InRange(bob.PingMs(0), 100, 100 + SlackMs);
        Assert.InRange(rig.Host.PingMs(1), 100, 100 + SlackMs);
        Assert.True(bob.ClockOf(0).SampleCount >= 4);
        Assert.True(rig.Host.ClockOf(1).SampleCount >= 4);
        Assert.InRange(bob.JitterMs(0), 0, SlackMs);
        Assert.Null(bob.ClockOf(1));      // a client has no clock for itself
        Assert.Null(rig.Host.ClockOf(0)); // nor the host
        Assert.Equal(-1, rig.Host.PingMs(2));
        Assert.Equal(ConnectionQuality.Unknown.PingMs, bob.QualityOf(0).PingMs);
        Assert.InRange(bob.SilenceSeconds(0), 0, 0.6);
        Assert.Equal(-1, bob.SilenceSeconds(2));
    }

    [Fact]
    public void A_client_knows_the_hosts_clock_as_the_session_time()
    {
        // The client's game started 1000 s later than the host's; the delays are equal both ways.
        var rig = new Rig(hostDelay: 0.05);
        var bob = rig.Join("Bob", 2, clockShift: -1000, delay: 0.05);
        Assert.False(bob.ClockSynced);
        Assert.True(rig.Host.ClockSynced);
        rig.Run(3);

        Assert.True(bob.ClockSynced);
        double bobNow = rig.Now - 1000;
        Assert.InRange(bob.ToSessionTime(bobNow) - rig.Now, -Frame, Frame);
        Assert.InRange(bob.ToLocalTime(rig.Now) - bobNow, -Frame, Frame);
        Assert.Equal(rig.Now, rig.Host.ToSessionTime(rig.Now));
        Assert.InRange(bob.ClockOf(0).Offset - 1000, -Frame, Frame);
        Assert.InRange(rig.Host.ClockOf(1).Offset + 1000, -Frame, Frame);
    }

    [Fact]
    public void Pings_go_out_twice_a_second()
    {
        var rig = new Rig();
        var bob = rig.Join("Bob", 2);
        rig.Run(5.05);
        // A pong for each ping: about 10 in 5 s on each side (the window keeps 16).
        Assert.InRange(bob.ClockOf(0).SampleCount, 9, 11);
        Assert.InRange(rig.Host.ClockOf(1).SampleCount, 9, 11);
    }

    // ---- timeouts and endings -----------------------------------------------------------------------------------

    [Fact]
    public void A_silent_client_times_out_on_the_host()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        rig.Run(1);
        rig.Frozen.Add(a); // Ann's game hangs
        rig.Run(9.5);
        Assert.Equal(3, rig.Host.Roster.Count);
        rig.Run(1);
        Assert.Equal("left 1 Ann Timeout", rig.Of(rig.Host).Last());
        Assert.Equal("left 1 Ann Timeout", rig.Of(b).Last());
        Assert.Equal("0:Host,2:Ben", Names(b));
        Assert.Equal(SessionState.Active, b.State);
    }

    [Fact]
    public void A_silent_host_times_out_on_the_client()
    {
        var rig = new Rig();
        var bob = rig.Join("Bob", 2);
        rig.Run(1);
        rig.Frozen.Add(rig.Host);
        rig.Run(9.5);
        Assert.Equal(SessionState.Active, bob.State);
        rig.Run(1);
        Assert.Equal(LeaveReason.Timeout, bob.EndReason);
        Assert.Equal("ended Timeout", rig.Of(bob).Last());
    }

    [Fact]
    public void The_timeout_is_configurable()
    {
        var options = Rig.Options("Host", 1);
        options.TimeoutSeconds = 2;
        var rig = new Rig(options);
        var bob = rig.Join("Bob", 2);
        rig.Run(1);
        rig.Frozen.Add(bob);
        rig.Run(2.2);
        Assert.Equal("left 1 Bob Timeout", rig.Of(rig.Host).Last());
    }

    [Fact]
    public void A_lost_connection_counts_as_connection_lost()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        rig.Run(0.5);
        rig.ClientTransports[0].Dispose(); // Ann's game closed without a goodbye
        rig.Run(0.1);
        Assert.Equal("left 1 Ann ConnectionLost", rig.Of(rig.Host).Last());
        Assert.Equal("left 1 Ann ConnectionLost", rig.Of(b).Last());
        Assert.Equal(LeaveReason.ConnectionLost, a.EndReason);
    }

    [Fact]
    public void The_host_leaving_ends_every_clients_session()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        rig.Run(0.5);
        rig.Host.Leave();
        rig.Run(0.1);
        Assert.Equal(new[] { "joined 1 Ann", "joined 2 Ben", "ended LocalLeave" }, rig.Of(rig.Host));
        Assert.Equal(LeaveReason.HostLeft, a.EndReason);
        Assert.Equal(LeaveReason.HostLeft, b.EndReason);
        Assert.Equal("ended HostLeft", rig.Of(a).Last());
        Assert.Equal("0:Host,1:Ann,2:Ben", Names(a)); // the last roster stays for the screen
        rig.Host.Leave(); // twice is harmless
        Assert.Equal(1, rig.Of(rig.Host).Count(e => e.StartsWith("ended")));
    }

    [Fact]
    public void The_host_leaving_turns_away_a_game_in_the_handshake()
    {
        var rig = new Rig();
        var lt = rig.Hub.Join();
        rig.Hub.Connect(rig.HostTransport, lt);
        rig.Run(0.1); // connected, no Hello yet
        rig.Host.Leave();
        var answers = new List<byte[]>();
        var collector = new Collector(answers);
        lt.Poll(collector);
        var reject = answers.Single(a => a[0] == (byte)MessageType.Reject);
        Assert.Equal((byte)RejectReason.HostLeaving, reject[1]);
        Assert.Single(collector.Disconnects);
    }

    [Fact]
    public void A_kicked_player_hears_it_and_a_ban_keeps_it_out()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        rig.Run(0.2);
        Assert.False(rig.Host.Kick(0));
        Assert.False(rig.Host.Kick(3));
        Assert.False(a.Kick(2)); // only the host kicks
        Assert.True(rig.Host.Kick(1, ban: true));
        rig.Run(0.1);
        Assert.Equal(LeaveReason.Kicked, a.EndReason);
        Assert.Equal("left 1 Ann Kicked", rig.Of(rig.Host).Last());
        Assert.Equal("left 1 Ann Kicked", rig.Of(b).Last());

        var again = rig.Join("Ann", 1);
        rig.Run(0.1);
        Assert.Equal(RejectReason.Kicked, again.Rejection);

        Assert.True(rig.Host.Kick(2)); // no ban: Ben may come back
        rig.Run(0.1);
        var ben = rig.Join("Ben", 2);
        rig.Run(0.1);
        Assert.Equal(SessionState.Active, ben.State);
    }

    [Fact]
    public void Leaving_inside_an_event_is_safe()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        rig.Run(0.5);
        rig.Host.PlayerLeft += (_, _) => rig.Host.Leave();
        rig.Frozen.Add(a);
        rig.Frozen.Add(b);
        rig.Run(11); // both time out in the same frame; the first one's event ends the session
        Assert.Equal(SessionState.Ended, rig.Host.State);
        Assert.Equal(1, rig.Of(rig.Host).Count(e => e.StartsWith("left")));
    }

    // ---- game messages ------------------------------------------------------------------------------------------

    private static NetWriter Payload(int value)
    {
        var w = new NetWriter();
        w.WriteVarInt(value);
        return w;
    }

    private static List<string> Listen(CoopSession s, MessageType type)
    {
        var got = new List<string>();
        s.RegisterHandler(type, (m, r) => got.Add($"{m.FromSlot}:{r.ReadVarInt32()}{(m.Relayed ? " relayed" : "")}"));
        return got;
    }

    [Fact]
    public void Game_messages_reach_their_handler_with_the_senders_slot()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        Assert.False(a.Send(0, MessageType.TimedEvent, Payload(1), Delivery.Reliable)); // not welcomed yet
        rig.Run(0.1);
        var host = Listen(rig.Host, MessageType.TimedEvent);
        var gotA = Listen(a, MessageType.TimedEvent);
        var gotB = Listen(b, MessageType.TimedEvent);

        Assert.True(a.Send(0, MessageType.TimedEvent, Payload(11), Delivery.Reliable));
        Assert.True(rig.Host.Send(2, MessageType.TimedEvent, Payload(22), Delivery.Reliable));
        Assert.Equal(2, rig.Host.Broadcast(MessageType.TimedEvent, Payload(33), Delivery.Unreliable));
        Assert.Equal(1, b.Broadcast(MessageType.TimedEvent, Payload(44), Delivery.Reliable));
        rig.Run(0.1);

        Assert.Equal(new[] { "1:11", "2:44" }, host); // not relayed: TimedEvent isn't marked
        Assert.Equal(new[] { "0:33" }, gotA);
        Assert.Equal(new[] { "0:22", "0:33" }, gotB);
    }

    [Fact]
    public void Sends_with_nobody_to_reach_fail()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        rig.Run(0.1);
        Assert.False(rig.Host.Send(0, MessageType.PlayerState, Payload(1), Delivery.Reliable)); // itself
        Assert.False(rig.Host.Send(2, MessageType.PlayerState, Payload(1), Delivery.Reliable)); // empty slot
        Assert.False(rig.Host.Send(7, MessageType.PlayerState, Payload(1), Delivery.Reliable));
        Assert.False(a.Send(2, MessageType.PlayerState, Payload(1), Delivery.Reliable));        // clients reach clients via the host
        Assert.True(a.Send(0, MessageType.PlayerState, null, Delivery.Reliable));                // an empty payload is fine
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Send(0, MessageType.Ping, Payload(1), Delivery.Reliable));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.RegisterHandler(MessageType.Hello, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.SetRelay((MessageType)31));
        a.Leave();
        Assert.False(a.Send(0, MessageType.PlayerState, Payload(1), Delivery.Reliable));
        Assert.Equal(0, a.Broadcast(MessageType.PlayerState, Payload(1), Delivery.Reliable));
    }

    [Fact]
    public void The_host_relays_marked_types_to_the_other_clients_only()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        var c = rig.Join("Cat", 3);
        rig.Run(0.1);
        foreach (var s in new[] { rig.Host, a, b, c })
        {
            s.SetRelay(MessageType.PlayerState);
        }
        var host = Listen(rig.Host, MessageType.PlayerState);
        var gotA = Listen(a, MessageType.PlayerState);
        var gotB = Listen(b, MessageType.PlayerState);
        var gotC = Listen(c, MessageType.PlayerState);

        a.Broadcast(MessageType.PlayerState, Payload(1), Delivery.Unreliable);
        rig.Run(0.1);
        Assert.Equal(new[] { "1:1" }, host);
        Assert.Empty(gotA); // never back to the sender
        Assert.Equal(new[] { "1:1 relayed" }, gotB);
        Assert.Equal(new[] { "1:1 relayed" }, gotC);

        rig.Host.SetRelay(MessageType.PlayerState, false);
        b.Broadcast(MessageType.PlayerState, Payload(2), Delivery.Unreliable);
        rig.Run(0.1);
        Assert.Equal(new[] { "1:1", "2:2" }, host);
        Assert.Empty(gotA);
        Assert.Single(gotC);
    }

    [Fact]
    public void A_relay_works_without_a_handler_on_the_host()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        var b = rig.Join("Ben", 2);
        rig.Run(0.1);
        rig.Host.SetRelay(MessageType.EnemySnapshot);
        var gotB = Listen(b, MessageType.EnemySnapshot);
        a.Broadcast(MessageType.EnemySnapshot, Payload(5), Delivery.Reliable);
        rig.Run(0.1);
        Assert.Equal(new[] { "1:5 relayed" }, gotB);
    }

    [Fact]
    public void A_message_claiming_another_sender_is_dropped()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        rig.Join("Ben", 2);
        rig.Run(0.1);
        var host = Listen(rig.Host, MessageType.PlayerState);
        var forged = new byte[] { (byte)MessageType.PlayerState, 2, 6 }; // Ann pretends to be slot 2
        rig.ClientTransports[0].Send(rig.HostTransport.Self, new ArraySegment<byte>(forged), Delivery.Reliable);
        rig.Run(0.1);
        Assert.Empty(host);
        Assert.Equal(1, rig.Host.MalformedMessages);
        Assert.Equal(SessionState.Active, a.State);
    }

    [Fact]
    public void A_failing_handler_does_not_break_the_session()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        rig.Run(0.1);
        int calls = 0;
        rig.Host.RegisterHandler(MessageType.TimedEvent, (_, r) =>
        {
            calls++;
            if (calls == 1)
                r.ReadUInt64(); // more than the payload holds
            else if (calls == 2)
                throw new InvalidOperationException("a bug in a game package");
        });
        for (int i = 0; i < 3; i++)
            a.Send(0, MessageType.TimedEvent, Payload(i), Delivery.Reliable);
        var lines = new List<string>();
        rig.Host.Log += lines.Add;
        rig.Run(0.1);
        Assert.Equal(3, calls);
        Assert.Equal(1, rig.Host.MalformedMessages);
        Assert.Equal(1, rig.Host.HandlerErrors);
        Assert.Contains(lines, l => l.Contains("InvalidOperationException"));
        Assert.Equal(SessionState.Active, a.State);
        Assert.Equal(2, rig.Host.Roster.Count);
    }

    [Fact]
    public void Unhandled_game_messages_are_ignored_and_handlers_can_be_removed()
    {
        var rig = new Rig();
        var a = rig.Join("Ann", 1);
        rig.Run(0.1);
        var host = Listen(rig.Host, MessageType.TimedEvent);
        a.Send(0, (MessageType)200, Payload(1), Delivery.Reliable);
        a.Send(0, MessageType.TimedEvent, Payload(2), Delivery.Reliable);
        rig.Run(0.1);
        rig.Host.UnregisterHandler(MessageType.TimedEvent);
        a.Send(0, MessageType.TimedEvent, Payload(3), Delivery.Reliable);
        rig.Run(0.1);
        Assert.Equal(new[] { "1:2" }, host);
        Assert.Equal(0, rig.Host.MalformedMessages);
        Assert.True(rig.Host.BytesReceived > 0 && rig.Host.BytesSent > 0);
    }

    [Fact]
    public void A_full_party_of_four_trades_states_through_the_host()
    {
        // Every client has its own delay and clock; each sends its state to everyone 20 times a second.
        var rig = new Rig(hostDelay: 0.03);
        var clients = new[]
        {
            rig.Join("Ann", 1, clockShift: 5, delay: 0.02),
            rig.Join("Ben", 2, clockShift: -7, delay: 0.08),
            rig.Join("Cat", 3, clockShift: 300, delay: 0.05),
        };
        var all = new[] { rig.Host }.Concat(clients).ToArray();
        var heard = new Dictionary<CoopSession, HashSet<int>>();
        foreach (var s in all)
        {
            s.SetRelay(MessageType.PlayerState);
            var set = heard[s] = new HashSet<int>();
            s.RegisterHandler(MessageType.PlayerState, (m, r) =>
            {
                var (x, y) = r.ReadVector2();
                Assert.Equal(m.FromSlot * 10f, x);
                Assert.Equal((float)-m.FromSlot, y);
                set.Add(m.FromSlot);
            });
        }
        rig.Run(1);
        Assert.All(all, s => Assert.Equal(SessionState.Active, s.State));
        Assert.All(all, s => Assert.Equal("0:Host,1:Ann,2:Ben,3:Cat", Names(s)));

        var w = new NetWriter();
        for (int tick = 0; tick < 40; tick++)
        {
            foreach (var s in all)
            {
                w.Reset();
                w.WriteVector2(s.LocalSlot * 10f, -s.LocalSlot);
                s.Broadcast(MessageType.PlayerState, w, Delivery.Unreliable);
            }
            rig.Run(0.05);
        }
        rig.Run(0.5);
        foreach (var s in all)
            Assert.Equal(new HashSet<int>(new[] { 0, 1, 2, 3 }.Where(slot => slot != s.LocalSlot)), heard[s]);

        // Pings to the host follow each client's delays (client delay + host delay).
        Assert.InRange(clients[0].PingMs(0), 50, 50 + SlackMs);
        Assert.InRange(clients[1].PingMs(0), 110, 110 + SlackMs);
        Assert.InRange(clients[2].PingMs(0), 80, 80 + SlackMs);
        Assert.InRange(rig.Host.PingMs(2), 110, 110 + SlackMs);
        // Every client's session time is the host's clock, whatever its own clock says (off by at most half the
        // difference between its two ways, here up to 25 ms, plus the polling).
        foreach (var (c, shift) in clients.Zip(new[] { 5.0, -7.0, 300.0 }, (c, s) => (c, s)))
            Assert.InRange(c.ToSessionTime(rig.Now + shift) - rig.Now, -0.05, 0.05);
        Assert.All(all, s => Assert.Equal(0, s.MalformedMessages));
        Assert.All(all, s => Assert.Equal(0, s.HandlerErrors));
    }

    // ---- rejoin after a broken line (package DC) ----------------------------------------------------------------

    private static SessionOptions RejoinHost(double window = 60)
    {
        var options = Rig.Options("Host", 1000);
        options.RejoinWindowSeconds = window;
        return options;
    }

    [Fact]
    public void A_dropped_players_slot_is_kept_and_they_get_it_back_during_a_run()
    {
        var rig = new Rig(RejoinHost());
        var ann = rig.Join("Ann", 11);
        rig.Join("Ben", 22);
        rig.Run(1);
        rig.Frozen.Add(ann); // Ann's line breaks
        rig.Run(10.5);
        Assert.Equal("left 1 Ann Timeout", rig.Of(rig.Host).Last());
        Assert.True(rig.Host.IsReserved(1));
        var carl = rig.Join("Carl", 33); // a newcomer doesn't get the kept slot
        rig.Run(0.2);
        Assert.Equal(3, carl.LocalSlot);
        rig.Host.AcceptingPlayers = false; // the run is on
        var back = rig.Join("Ann", 11); // the same game, a new session
        rig.Run(0.2);
        Assert.Equal(SessionState.Active, back.State);
        Assert.Equal(1, back.LocalSlot);
        Assert.False(rig.Host.IsReserved(1));
        Assert.Equal("joined 1 Ann", rig.Of(rig.Host).Last());
        Assert.Equal("0:Host,1:Ann,2:Ben,3:Carl", Names(rig.Host));
        var stranger = rig.Join("Dan", 44);
        rig.Run(0.2);
        Assert.Equal(RejectReason.NotAccepting, stranger.Rejection);
    }

    [Fact]
    public void A_goodbye_or_a_kick_frees_the_slot_at_once()
    {
        var rig = new Rig(RejoinHost());
        var ann = rig.Join("Ann", 11);
        rig.Join("Ben", 22);
        rig.Run(1);
        ann.Leave();
        rig.Run(0.2);
        Assert.Equal("left 1 Ann Goodbye", rig.Of(rig.Host).Last());
        Assert.False(rig.Host.IsReserved(1));
        Assert.True(rig.Host.Kick(2));
        rig.Run(0.2);
        Assert.False(rig.Host.IsReserved(2));
    }

    [Fact]
    public void The_kept_slot_is_given_up_after_the_window()
    {
        var rig = new Rig(RejoinHost(30));
        var ann = rig.Join("Ann", 11);
        rig.Run(1);
        rig.Frozen.Add(ann);
        rig.Run(10.5);
        Assert.True(rig.Host.IsReserved(1));
        Assert.InRange(rig.Host.ReservedUntil(1), rig.Now + 29, rig.Now + 30);
        rig.Run(30);
        Assert.False(rig.Host.IsReserved(1));
        var ben = rig.Join("Ben", 22);
        rig.Run(0.2);
        Assert.Equal(1, ben.LocalSlot);
    }

    [Fact]
    public void Without_a_rejoin_window_nothing_is_kept()
    {
        var rig = new Rig();
        var ann = rig.Join("Ann", 11);
        rig.Run(1);
        rig.Frozen.Add(ann);
        rig.Run(10.5);
        Assert.False(rig.Host.IsReserved(1));
    }

    [Fact]
    public void A_new_session_on_the_same_connection_id_leaves_and_comes_back()
    {
        var options = RejoinHost();
        options.TimeoutSeconds = 30;
        options.RepeatedHelloSeconds = 2;
        var rig = new Rig(options);
        var lt = rig.Hub.Join();
        rig.Hub.Connect(rig.HostTransport, lt);
        var first = rig.Adopt(CoopSession.Join(lt, rig.HostTransport.Self, Rig.Options("Ann", 11), rig.Now));
        rig.Run(1);
        Assert.Equal(SessionState.Active, first.State);
        rig.Frozen.Add(first); // that session is gone; the connection id (a Steam id) stays the same
        rig.Run(5);
        var second = rig.Adopt(CoopSession.Join(lt, rig.HostTransport.Self, Rig.Options("Ann", 11), rig.Now));
        rig.Run(0.5);
        Assert.Equal(SessionState.Active, second.State);
        Assert.Equal(1, second.LocalSlot);
        var host = rig.Of(rig.Host);
        Assert.Equal(new[] { "joined 1 Ann", "left 1 Ann ConnectionLost", "joined 1 Ann" }, host);
    }

    [Fact]
    public void A_join_try_without_an_answer_ends_after_the_handshake_timeout()
    {
        var options = Rig.Options("Bob", 2);
        options.TimeoutSeconds = 20;
        options.HandshakeTimeoutSeconds = 5;
        var rig = new Rig();
        rig.Frozen.Add(rig.Host);
        var bob = rig.Join("Bob", 2, options);
        rig.Run(5.5);
        Assert.Equal(LeaveReason.Timeout, bob.EndReason);
    }
}
