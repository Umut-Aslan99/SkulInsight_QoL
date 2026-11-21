using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

public class ReliableChannelTests
{
    /// <summary>Two channels over a simulated pipe that loses, doubles, delays and reorders datagrams (seeded).</summary>
    private sealed class Link
    {
        private readonly Random _random;
        private readonly List<(double at, bool toB, byte[] bytes)> _flying = new();
        public double Now;
        public double Loss, Duplicate, DelaySeconds = 0.02, JitterSeconds;
        public readonly ReliableChannel A, B;
        public readonly List<(byte[] bytes, Delivery delivery)> AtA = new(), AtB = new();

        public Link(int seed, ReliableChannelOptions options = null)
        {
            _random = new Random(seed);
            A = new ReliableChannel(() => Now, d => Fly(d, true), (m, d) => AtA.Add((Copy(m), d)), options);
            B = new ReliableChannel(() => Now, d => Fly(d, false), (m, d) => AtB.Add((Copy(m), d)), options);
        }

        private static byte[] Copy(ArraySegment<byte> m)
        {
            var c = new byte[m.Count];
            Buffer.BlockCopy(m.Array!, m.Offset, c, 0, m.Count);
            return c;
        }

        private void Fly(ArraySegment<byte> datagram, bool toB)
        {
            if (_random.NextDouble() < Loss)
                return;
            int copies = _random.NextDouble() < Duplicate ? 2 : 1;
            for (int i = 0; i < copies; i++)
                _flying.Add((Now + DelaySeconds + _random.NextDouble() * JitterSeconds, toB, Copy(datagram)));
        }

        public void Step(double seconds = 0.01)
        {
            Now += seconds;
            for (int i = 0; i < _flying.Count; i++)
            {
                var f = _flying[i];
                if (f.at > Now)
                    continue;
                _flying.RemoveAt(i--);
                (f.toB ? B : A).Receive(new ArraySegment<byte>(f.bytes));
            }
            A.Update();
            B.Update();
        }

        public void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += 0.01)
                Step();
        }
    }

    private static byte[] Numbered(int i, int size = 8)
    {
        var b = new byte[size];
        b[0] = (byte)i;
        b[1] = (byte)(i >> 8);
        for (int k = 2; k < size; k++)
            b[k] = (byte)(i * 31 + k);
        return b;
    }

    [Fact]
    public void A_thousand_reliable_messages_arrive_once_and_in_order_at_20_percent_loss()
    {
        var link = new Link(7) { Loss = 0.2, Duplicate = 0.05, JitterSeconds = 0.04 };
        for (int i = 0; i < 1000; i++)
        {
            link.A.Send(new ArraySegment<byte>(Numbered(i, 8 + i % 40)), Delivery.Reliable);
            if (i % 20 == 0)
                link.Step();
        }
        link.Run(20);
        Assert.Equal(1000, link.AtB.Count);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(Delivery.Reliable, link.AtB[i].delivery);
            Assert.Equal(Numbered(i, 8 + i % 40), link.AtB[i].bytes);
        }
        Assert.Equal(0, link.A.PendingReliable);
        Assert.True(link.A.Resends > 0);
    }

    [Fact]
    public void Unreliable_messages_are_never_doubled_even_when_the_pipe_doubles_datagrams()
    {
        var link = new Link(3) { Duplicate = 0.5, Loss = 0.1, JitterSeconds = 0.05 };
        for (int i = 0; i < 300; i++)
        {
            link.A.Send(new ArraySegment<byte>(Numbered(i)), Delivery.Unreliable);
            link.Step();
        }
        link.Run(1);
        var seen = new HashSet<int>();
        foreach (var (bytes, delivery) in link.AtB)
        {
            Assert.Equal(Delivery.Unreliable, delivery);
            Assert.True(seen.Add(bytes[0] | bytes[1] << 8));
        }
        Assert.InRange(seen.Count, 200, 299);
        Assert.True(link.B.DuplicatesDropped > 0);
    }

    [Fact]
    public void A_10_KB_message_is_split_and_joined_again_reliable_and_unreliable()
    {
        var link = new Link(5) { Loss = 0.2, JitterSeconds = 0.03 };
        var big = Numbered(4242, 10 * 1024);
        link.A.Send(new ArraySegment<byte>(big), Delivery.Reliable);
        link.Run(5);
        Assert.Single(link.AtB);
        Assert.Equal(big, link.AtB[0].bytes);

        var clean = new Link(6);
        clean.A.Send(new ArraySegment<byte>(big), Delivery.Unreliable);
        clean.A.Send(new ArraySegment<byte>(Numbered(1)), Delivery.Unreliable);
        clean.Run(0.5);
        Assert.Equal(2, clean.AtB.Count);
        Assert.Equal(big, clean.AtB.Find(m => m.bytes.Length == big.Length).bytes);
        Assert.True(clean.A.DatagramsSent >= 9);
    }

    [Fact]
    public void Acks_stop_the_resends()
    {
        var link = new Link(1);
        for (int i = 0; i < 50; i++)
            link.A.Send(new ArraySegment<byte>(Numbered(i)), Delivery.Reliable);
        link.Run(1);
        Assert.Equal(50, link.AtB.Count);
        Assert.Equal(0, link.A.PendingReliable);
        long resends = link.A.Resends;
        link.Run(5);
        Assert.Equal(resends, link.A.Resends);
        Assert.Equal(0, resends);
    }

    [Fact]
    public void The_resend_timeout_follows_the_measured_round_trip()
    {
        double RtoAfter(double oneWay)
        {
            var link = new Link(2) { DelaySeconds = oneWay };
            for (int i = 0; i < 40; i++)
            {
                link.A.Send(new ArraySegment<byte>(Numbered(i)), Delivery.Reliable);
                link.Run(0.1);
            }
            link.Run(1);
            Assert.InRange(link.A.SmoothedRttMs, oneWay * 2000 - 1, oneWay * 2000 + 30);
            return link.A.RtoMs;
        }
        double fast = RtoAfter(0.01), slow = RtoAfter(0.15);
        Assert.InRange(fast, 50, 100);
        Assert.InRange(slow, 300, 400);
    }

    [Fact]
    public void Silence_is_noticed_and_keep_alives_prevent_it()
    {
        var link = new Link(4, new ReliableChannelOptions { DeadAfterSeconds = 1, KeepAliveSeconds = 0.2 });
        int died = 0;
        link.B.Died = () => died++;
        link.Run(3);
        Assert.False(link.B.IsDead); // A's keep-alives arrive
        link.Loss = 1;
        link.Run(2);
        Assert.True(link.B.IsDead);
        Assert.Equal(1, died);
    }

    [Fact]
    public void Several_small_messages_share_one_datagram()
    {
        var link = new Link(8);
        for (int i = 0; i < 20; i++)
            link.A.Send(new ArraySegment<byte>(Numbered(i, 20)), i % 2 == 0 ? Delivery.Reliable : Delivery.Unreliable);
        link.A.Flush();
        Assert.Equal(1, link.A.DatagramsSent);
        link.Step(0.05);
        Assert.Equal(20, link.AtB.Count);
    }
}
