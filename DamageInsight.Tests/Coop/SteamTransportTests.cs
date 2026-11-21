using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>SteamTransport's logic over a fake Steam (the real Steam calls can only be tested in game).</summary>
public class SteamTransportTests
{
    private sealed class FakeSteam : ISteamSockets
    {
        public ulong Own = 76561190000000001;
        public Action<SteamStatusChange> Status;
        public readonly List<uint> Accepted = new(), Closed = new();
        public readonly List<(uint conn, byte[] bytes, bool reliable)> Sent = new();
        public readonly Dictionary<uint, Queue<byte[]>> Inbox = new();
        public uint NextConnection = 10;
        public ulong OwnId => Own;
        public Action<SteamStatusChange> StatusChanged { set => Status = value; }
        public uint Listen(int virtualPort) => 5;
        public uint Connect(ulong remote, int virtualPort) => NextConnection++;
        public bool Accept(uint connection)
        {
            Accepted.Add(connection);
            return true;
        }
        public void Close(uint connection, int reason, string debug, bool linger) => Closed.Add(connection);
        public void CloseListen(uint listenSocket) { }
        public bool Send(uint connection, byte[] data, int count, bool reliable)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(data, 0, copy, 0, count);
            Sent.Add((connection, copy, reliable));
            return true;
        }
        public int Receive(uint connection, ref byte[] buffer)
        {
            if (!Inbox.TryGetValue(connection, out var q) || q.Count == 0)
                return -1;
            var m = q.Dequeue();
            Buffer.BlockCopy(m, 0, buffer, 0, m.Length);
            return m.Length;
        }
        public bool Quality(uint connection, out int pingMs, out float lossPercent)
        {
            pingMs = 42;
            lossPercent = 1.5f;
            return true;
        }
        public void Dispose() { }
        public void Change(uint conn, SteamConnState state, ulong remote, bool incoming = true, string debug = null) =>
            Status(new SteamStatusChange(conn, state, remote, incoming, 0, debug));
    }

    private sealed class Recorder : ITransportListener
    {
        public readonly List<string> Events = new();
        public void OnConnected(PeerId peer) => Events.Add("connected " + peer);
        public void OnDisconnected(PeerId peer, string reason) => Events.Add("disconnected " + peer + ": " + reason);
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery) =>
            Events.Add($"{delivery} from {from}: {string.Join(",", new List<byte>(message))}");
    }

    [Fact]
    public void The_host_accepts_up_to_its_limit_and_maps_connections_to_steam_ids()
    {
        var steam = new FakeSteam();
        var host = new SteamTransport(steam, true, 0, 0, 2);
        var heard = new Recorder();
        steam.Change(1, SteamConnState.Connecting, 1001);
        steam.Change(2, SteamConnState.Connecting, 1002);
        steam.Change(3, SteamConnState.Connecting, 1003); // one too many
        steam.Change(1, SteamConnState.Connected, 1001);
        steam.Change(2, SteamConnState.Connected, 1002);
        host.Poll(heard);
        Assert.Equal(new uint[] { 1, 2 }, steam.Accepted);
        Assert.Equal(new uint[] { 3 }, steam.Closed);
        Assert.Equal(new[] { "connected 1001", "connected 1002" }, heard.Events);
        Assert.Equal(new PeerId(steam.Own), host.Self);
        Assert.Equal(2, host.PeerCount);

        host.Send(new PeerId(1002), new ArraySegment<byte>(new byte[] { 9, 8 }), Delivery.Reliable);
        Assert.Equal(2u, steam.Sent[0].conn);
        Assert.Equal(new byte[] { 1, 9, 8 }, steam.Sent[0].bytes);
        Assert.True(steam.Sent[0].reliable);
        steam.Inbox[1] = new Queue<byte[]>(new[] { new byte[] { 0, 5 }, new byte[] { 1, 6, 7 } });
        heard.Events.Clear();
        host.Poll(heard);
        Assert.Equal(new[] { "Unreliable from 1001: 5", "Reliable from 1001: 6,7" }, heard.Events);
        Assert.Equal(42, host.QualityOf(new PeerId(1001)).PingMs);
    }

    [Fact]
    public void A_client_hears_connected_closed_and_failed_connects()
    {
        var steam = new FakeSteam();
        var client = new SteamTransport(steam, false, 777, 0, 1);
        var heard = new Recorder();
        steam.Change(10, SteamConnState.Connected, 777, incoming: false);
        client.Poll(heard);
        Assert.Equal(new[] { "connected 777" }, heard.Events);
        steam.Change(10, SteamConnState.ClosedByPeer, 777, incoming: false, debug: "host left");
        client.Poll(heard);
        Assert.Equal("disconnected 777: host left", heard.Events[1]);
        Assert.Contains(10u, steam.Closed);

        var steam2 = new FakeSteam();
        var never = new SteamTransport(steam2, false, 888, 0, 1);
        var heard2 = new Recorder();
        steam2.Change(10, SteamConnState.ProblemDetectedLocally, 888, incoming: false, debug: "timed out");
        never.Poll(heard2);
        Assert.Equal(new[] { "disconnected 888: could not connect: timed out" }, heard2.Events);
    }

    [Fact]
    public void Without_steam_it_reports_and_never_throws()
    {
        var none = new SteamTransport(null, false, 999, 0, 1);
        var heard = new Recorder();
        none.Poll(heard);
        none.Send(new PeerId(999), new ArraySegment<byte>(new byte[1]), Delivery.Reliable);
        Assert.Equal(new[] { "disconnected 999: Steam is not running" }, heard.Events);
        Assert.Equal(-1, none.QualityOf(new PeerId(999)).PingMs);
        none.Dispose();
    }

    [Fact]
    public void A_local_disconnect_closes_with_linger_and_is_heard_at_the_next_poll()
    {
        var steam = new FakeSteam();
        var host = new SteamTransport(steam, true, 0, 0, 3);
        var heard = new Recorder();
        steam.Change(1, SteamConnState.Connecting, 1001);
        steam.Change(1, SteamConnState.Connected, 1001);
        host.Poll(heard);
        host.Disconnect(new PeerId(1001), "kicked");
        Assert.Contains(1u, steam.Closed);
        host.Poll(heard);
        Assert.Equal("disconnected 1001: kicked", heard.Events[1]);
        Assert.Equal(0, host.PeerCount);
    }
}
