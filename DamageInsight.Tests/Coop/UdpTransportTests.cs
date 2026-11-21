using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Real sockets on 127.0.0.1; the host binds port 0 (any free port), so tests never collide.</summary>
public class UdpTransportTests
{
    private sealed class Recorder : ITransportListener
    {
        public readonly List<PeerId> Connected = new();
        public readonly List<(PeerId peer, string reason)> Disconnected = new();
        public readonly List<(PeerId from, byte[] bytes, Delivery delivery)> Messages = new();
        public void OnConnected(PeerId peer) => Connected.Add(peer);
        public void OnDisconnected(PeerId peer, string reason) => Disconnected.Add((peer, reason));
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery)
        {
            var copy = new byte[message.Count];
            Buffer.BlockCopy(message.Array!, message.Offset, copy, 0, message.Count);
            Messages.Add((from, copy, delivery));
        }
    }

    private sealed class Node : IDisposable
    {
        public readonly UdpTransport Transport;
        public readonly Recorder Heard = new();
        public bool Paused;
        public Node(UdpTransport transport) => Transport = transport;
        public void Poll()
        {
            if (!Paused)
                Transport.Poll(Heard);
        }
        public void Dispose() => Transport.Dispose();
    }

    private static UdpTransportOptions Options(int port = 0, Func<double> clock = null) =>
        new() { Port = port, Clock = clock, ConnectTimeoutSeconds = 3, TimeoutSeconds = 2 };

    private static void PollUntil(Func<bool> done, params Node[] nodes)
    {
        for (int i = 0; i < 3000 && !done(); i++)
        {
            foreach (var n in nodes)
                n.Poll();
            Thread.Sleep(1);
        }
        Assert.True(done(), "timed out waiting");
    }

    private static void Send(Node from, PeerId to, string text, Delivery delivery) =>
        from.Transport.Send(to, new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(text)), delivery);

    [Fact]
    public void A_host_and_two_clients_connect_and_exchange_reliable_and_unreliable_messages()
    {
        using var host = new Node(UdpTransport.Host(Options()));
        int port = host.Transport.BoundPort;
        using var a = new Node(UdpTransport.Join(Options(port)));
        using var b = new Node(UdpTransport.Join(Options(port)));
        PollUntil(() => host.Heard.Connected.Count == 2 && a.Heard.Connected.Count == 1 && b.Heard.Connected.Count == 1, host, a, b);

        Assert.Equal(new PeerId(1), host.Transport.Self);
        Assert.Equal(UdpTransport.HostPeer, a.Heard.Connected[0]);
        var ids = new HashSet<ulong> { a.Transport.Self.Value, b.Transport.Self.Value };
        Assert.Equal(new HashSet<ulong> { 2, 3 }, ids);
        Assert.Equal(ids, new HashSet<ulong> { host.Heard.Connected[0].Value, host.Heard.Connected[1].Value });

        for (int i = 0; i < 50; i++)
        {
            Send(a, UdpTransport.HostPeer, "a" + i, Delivery.Reliable);
            Send(b, UdpTransport.HostPeer, "b" + i, Delivery.Unreliable);
            Send(host, a.Transport.Self, "h" + i, Delivery.Reliable);
        }
        var big = new byte[10 * 1024];
        new Random(1).NextBytes(big);
        host.Transport.Send(b.Transport.Self, new ArraySegment<byte>(big), Delivery.Reliable);
        PollUntil(() => host.Heard.Messages.Count >= 100 && a.Heard.Messages.Count == 50 && b.Heard.Messages.Count == 1, host, a, b);

        var fromA = host.Heard.Messages.FindAll(m => m.from == a.Transport.Self);
        Assert.Equal(50, fromA.Count);
        for (int i = 0; i < 50; i++)
            Assert.Equal("a" + i, System.Text.Encoding.UTF8.GetString(fromA[i].bytes));
        Assert.All(host.Heard.Messages.FindAll(m => m.from == b.Transport.Self), m => Assert.Equal(Delivery.Unreliable, m.delivery));
        Assert.Equal("h49", System.Text.Encoding.UTF8.GetString(a.Heard.Messages[49].bytes));
        Assert.Equal(big, b.Heard.Messages[0].bytes);
        Assert.True(a.Transport.QualityOf(UdpTransport.HostPeer).PingMs >= 0);
    }

    [Fact]
    public void A_disconnect_reaches_both_sides()
    {
        using var host = new Node(UdpTransport.Host(Options()));
        using var a = new Node(UdpTransport.Join(Options(host.Transport.BoundPort)));
        PollUntil(() => host.Heard.Connected.Count == 1 && a.Heard.Connected.Count == 1, host, a);
        a.Transport.Disconnect(UdpTransport.HostPeer, "leaving");
        PollUntil(() => host.Heard.Disconnected.Count == 1 && a.Heard.Disconnected.Count == 1, host, a);
        Assert.Equal((new PeerId(2), "leaving"), host.Heard.Disconnected[0]);
        Assert.Equal(UdpState.Closed, a.Transport.State);
        Assert.Equal(0, host.Transport.PeerCount);
    }

    [Fact]
    public void A_silent_peer_times_out_and_its_id_is_given_again()
    {
        double now = 0;
        using var host = new Node(UdpTransport.Host(Options(clock: () => now)));
        using var a = new Node(UdpTransport.Join(Options(host.Transport.BoundPort, () => now)));
        PollUntil(() => host.Heard.Connected.Count == 1 && a.Heard.Connected.Count == 1, host, a);
        a.Paused = true; // the game froze or crashed: no keep-alives
        now += 2.5;
        host.Poll();
        Assert.Equal((new PeerId(2), "timeout"), host.Heard.Disconnected[0]);

        using var c = new Node(UdpTransport.Join(Options(host.Transport.BoundPort, () => now)));
        PollUntil(() => c.Heard.Connected.Count == 1, host, c);
        Assert.Equal(new PeerId(2), c.Transport.Self);
    }

    [Fact]
    public void A_full_host_refuses_and_a_missing_host_times_out()
    {
        var options = Options();
        options.MaxClients = 1;
        using var host = new Node(UdpTransport.Host(options));
        using var a = new Node(UdpTransport.Join(Options(host.Transport.BoundPort)));
        PollUntil(() => a.Heard.Connected.Count == 1, host, a);
        using var b = new Node(UdpTransport.Join(Options(host.Transport.BoundPort)));
        PollUntil(() => b.Heard.Disconnected.Count == 1, host, a, b);
        Assert.Equal("the host is full", b.Heard.Disconnected[0].reason);
        Assert.Empty(b.Heard.Connected);

        double now = 0;
        int unused = host.Transport.BoundPort; // nobody else listens there after the host closed
        host.Dispose();
        using var lonely = new Node(UdpTransport.Join(Options(unused, () => now)));
        lonely.Poll();
        now += 3.5;
        lonely.Poll();
        Assert.Equal(UdpTransport.HostPeer, lonely.Heard.Disconnected[0].peer);
        Assert.Equal(UdpState.Closed, lonely.Transport.State);
    }

    [Fact]
    public void Stray_packets_are_ignored()
    {
        using var host = new Node(UdpTransport.Host(Options()));
        using var a = new Node(UdpTransport.Join(Options(host.Transport.BoundPort)));
        PollUntil(() => host.Heard.Connected.Count == 1 && a.Heard.Connected.Count == 1, host, a);
        using (var stray = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            var to = new IPEndPoint(IPAddress.Loopback, host.Transport.BoundPort);
            stray.SendTo(new byte[] { 0xA4, 1, 2, 3, 4, 0, 0, 0 }, to); // data with a wrong token
            stray.SendTo(new byte[] { 0xA1, (byte)'X', 0, 0 }, to); // not a valid request
            stray.SendTo(new byte[] { 0x00 }, to);
        }
        for (int i = 0; i < 50; i++)
        {
            host.Poll();
            Thread.Sleep(1);
        }
        Assert.Single(host.Heard.Connected);
        Assert.Empty(host.Heard.Messages);
        Assert.Empty(host.Heard.Disconnected);
    }
}
