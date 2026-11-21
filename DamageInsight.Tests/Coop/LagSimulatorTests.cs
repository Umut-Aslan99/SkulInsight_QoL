using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

public class LagSimulatorTests
{
    private sealed class Recorder : ITransportListener
    {
        public readonly List<(int number, Delivery delivery, double at)> Messages = new();
        public readonly List<string> Events = new();
        public Func<double> Clock = () => 0;
        public void OnConnected(PeerId peer) => Events.Add("connected " + peer);
        public void OnDisconnected(PeerId peer, string reason) => Events.Add("disconnected " + peer);
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery)
        {
            Events.Add("message");
            Messages.Add((message.Array![message.Offset] | message.Array[message.Offset + 1] << 8, delivery, Clock()));
        }
    }

    /// <summary>A sends through the lag simulator, B receives directly; the clock moves in 1 ms steps.</summary>
    private sealed class Setup
    {
        public double Now;
        public readonly LagSimulator A;
        public readonly LoopbackTransport B;
        public readonly Recorder AtA = new(), AtB = new();

        public Setup(LagSettings settings, int seed = 1)
        {
            var hub = new LoopbackTransport.Hub();
            var a = hub.Join();
            B = hub.Join();
            hub.Connect(a, B);
            A = new LagSimulator(a, settings, seed, () => Now);
            AtB.Clock = () => Now;
            Tick();
            AtA.Events.Clear();
            AtB.Events.Clear();
        }

        public void Send(int number, Delivery delivery) =>
            A.Send(B.Self, new ArraySegment<byte>(new[] { (byte)number, (byte)(number >> 8), (byte)7 }), delivery);

        public void Tick()
        {
            A.Poll(AtA);
            B.Poll(AtB);
        }

        public void Run(double seconds)
        {
            for (int i = 0; i < seconds * 1000; i++)
            {
                Now += 0.001;
                Tick();
            }
        }
    }

    private static (double mean, double sd, double min, double max) Delays(Setup s)
    {
        double sum = 0, sq = 0, min = double.MaxValue, max = 0;
        foreach (var m in s.AtB.Messages)
        {
            double ms = (m.at - m.number * 0.001) * 1000;
            sum += ms;
            sq += ms * ms;
            min = Math.Min(min, ms);
            max = Math.Max(max, ms);
        }
        int n = s.AtB.Messages.Count;
        double mean = sum / n;
        return (mean, Math.Sqrt(sq / n - mean * mean), min, max);
    }

    private static Setup SendOnePerMs(LagSettings settings, int count, Delivery delivery, int seed = 1)
    {
        var s = new Setup(settings, seed);
        for (int i = 0; i < count; i++)
        {
            s.Now = i * 0.001;
            s.Send(i, delivery);
            s.Tick();
        }
        s.Run(3);
        return s;
    }

    [Fact]
    public void Delay_and_jitter_spread_like_the_settings_say()
    {
        var bell = Delays(SendOnePerMs(new LagSettings { DelayMs = 100, JitterMs = 30, ReorderUnreliable = true }, 2000, Delivery.Unreliable));
        Assert.InRange(bell.mean, 98, 102.5);
        Assert.InRange(bell.min, 69, 101);
        Assert.InRange(bell.max, 99, 131);
        Assert.InRange(bell.sd, 8, 12); // a bell of +-30 ms: sd = 10

        var uniform = Delays(SendOnePerMs(new LagSettings { DelayMs = 100, JitterMs = 30, Shape = LagJitter.Uniform, ReorderUnreliable = true }, 2000, Delivery.Unreliable));
        Assert.InRange(uniform.mean, 98, 102.5);
        Assert.InRange(uniform.sd, 15.5, 19); // uniform +-30 ms: sd = 17.3
    }

    [Fact]
    public void The_loss_share_of_unreliable_messages_matches()
    {
        var s = SendOnePerMs(new LagSettings { DelayMs = 20, LossPercent = 20 }, 2000, Delivery.Unreliable);
        Assert.InRange(s.AtB.Messages.Count, 1520, 1680);
        Assert.Equal(2000 - s.AtB.Messages.Count, s.A.Dropped);
    }

    [Fact]
    public void Reliable_messages_are_never_lost_and_keep_their_order()
    {
        var s = SendOnePerMs(new LagSettings { DelayMs = 50, JitterMs = 40, LossPercent = 30 }, 1000, Delivery.Reliable);
        Assert.Equal(1000, s.AtB.Messages.Count);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(i, s.AtB.Messages[i].number);
        Assert.True(s.A.SimulatedResends > 200);
        Assert.True(Delays(s).max >= 50 + 150); // a resend costs about a round trip
    }

    [Fact]
    public void Unreliable_messages_keep_their_order_unless_reordering_is_on()
    {
        static int Overtakes(Setup s)
        {
            int n = 0;
            for (int i = 1; i < s.AtB.Messages.Count; i++)
                if (s.AtB.Messages[i].number < s.AtB.Messages[i - 1].number)
                    n++;
            return n;
        }
        var kept = SendOnePerMs(new LagSettings { DelayMs = 50, JitterMs = 40 }, 500, Delivery.Unreliable);
        Assert.Equal(500, kept.AtB.Messages.Count);
        Assert.Equal(0, Overtakes(kept));
        var mixed = SendOnePerMs(new LagSettings { DelayMs = 50, JitterMs = 40, ReorderUnreliable = true }, 500, Delivery.Unreliable);
        Assert.True(Overtakes(mixed) > 20);
    }

    [Fact]
    public void Settings_change_while_running_and_nothing_waits_when_off()
    {
        var settings = new LagSettings { DelayMs = 200 };
        var s = new Setup(settings);
        s.Send(1, Delivery.Reliable);
        s.Tick();
        Assert.Empty(s.AtB.Messages);
        Assert.Equal(1, s.A.Waiting);
        s.Run(0.2);
        Assert.Single(s.AtB.Messages);

        settings.DelayMs = 0;
        s.Send(2, Delivery.Unreliable);
        s.Tick();
        Assert.Equal(2, s.AtB.Messages.Count);
        Assert.Equal(0, s.A.Waiting);
    }

    [Fact]
    public void Incoming_delay_lets_one_copy_simulate_the_whole_round_trip()
    {
        var s = new Setup(new LagSettings { DelayMs = 100, Incoming = true });
        s.AtA.Clock = () => s.Now;
        s.B.Send(s.A.Self, new ArraySegment<byte>(new byte[] { 5, 0 }), Delivery.Reliable);
        s.Run(0.099);
        Assert.Empty(s.AtA.Messages);
        s.Run(0.002);
        Assert.Single(s.AtA.Messages);
    }

    [Fact]
    public void A_disconnect_comes_after_the_messages_sent_before_it()
    {
        var s = new Setup(new LagSettings { DelayMs = 100 });
        s.Send(1, Delivery.Reliable);
        s.A.Disconnect(s.B.Self, "bye");
        s.Tick();
        Assert.Equal(new[] { "message", "disconnected " + s.A.Self }, s.AtB.Events);
    }
}
