using System;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Ping and clock offset from ping/pong pairs (package A), with made-up delays so every number is known.</summary>
public class NetClockTests
{
    /// <summary>One ping/pong pair: the peer's clock is <paramref name="trueOffset"/> ahead; the ping takes
    /// <paramref name="outbound"/> seconds there and the pong <paramref name="inbound"/> back.</summary>
    private static void Exchange(NetClock clock, double localSend, double outbound, double inbound, double trueOffset)
    {
        double remote = localSend + outbound + trueOffset;
        clock.AddSample(localSend, remote, localSend + outbound + inbound);
    }

    [Fact]
    public void The_first_sample_gives_the_round_trip_and_offset_at_once()
    {
        var clock = new NetClock();
        Assert.False(clock.HasSamples);
        Assert.Equal(0, clock.Offset);
        Assert.True(clock.AddSample(10.0, 1010.05, 10.1));
        Assert.True(clock.HasSamples);
        Assert.Equal(0.1, clock.Rtt, 9);
        Assert.Equal(100, clock.RttMs, 6);
        Assert.Equal(0.1, clock.LastRtt, 9);
        Assert.Equal(0.1, clock.MinRtt, 9);
        Assert.Equal(1000.0, clock.Offset, 9);
        Assert.Equal(0, clock.Jitter, 9);
    }

    [Fact]
    public void A_symmetric_delay_finds_the_exact_offset()
    {
        var clock = new NetClock();
        for (int i = 0; i < 20; i++)
            Exchange(clock, i * 0.5, 0.04, 0.04, trueOffset: -123.456);
        Assert.Equal(-123.456, clock.Offset, 9);
        Assert.Equal(0.08, clock.Rtt, 9);
        Assert.Equal(16, clock.SampleCount); // the window
    }

    [Fact]
    public void An_asymmetric_delay_shifts_the_offset_by_half_the_difference()
    {
        // Nobody can see a lopsided route from round trips alone (NTP's limit): 30 ms there, 70 ms back.
        var clock = new NetClock();
        for (int i = 0; i < 10; i++)
            Exchange(clock, i * 0.5, 0.03, 0.07, trueOffset: 50);
        Assert.Equal(0.1, clock.Rtt, 9);
        Assert.Equal(50 + (0.03 - 0.07) / 2, clock.Offset, 9);
    }

    [Fact]
    public void Jittery_returns_use_the_fastest_pairs_for_the_offset()
    {
        // 40 ms each way, and the way back sometimes waits in a queue for up to 80 ms more.
        const double trueOffset = 1000;
        var clock = new NetClock();
        double naive = 0;
        for (int i = 0; i < 16; i++)
        {
            double queue = ((i * 7) % 16) / 15.0 * 0.08; // 0, 37, 75, 32, ... ms: a fixed spread, no Random
            Exchange(clock, i * 0.5, 0.04, 0.04 + queue, trueOffset);
            naive += (0.04 + trueOffset) - (0.08 + queue) / 2;
        }
        naive /= 16;
        double error = Math.Abs(clock.Offset - trueOffset);
        Assert.True(error < 0.006, $"offset off by {error * 1000:0.0} ms");
        Assert.True(Math.Abs(naive - trueOffset) > 0.015, "averaging every pair would be off by about half the mean queue");
        Assert.Equal(0.08, clock.MinRtt, 9);
        Assert.True(clock.Jitter > 0.01 && clock.Jitter < 0.03, $"jitter {clock.JitterMs:0.0} ms");
    }

    [Fact]
    public void Jitter_is_the_mean_absolute_deviation_of_the_round_trips()
    {
        var clock = new NetClock();
        Exchange(clock, 0, 0.05, 0.05, 0);   // 100 ms
        Exchange(clock, 1, 0.06, 0.06, 0);   // 120 ms
        Exchange(clock, 2, 0.05, 0.05, 0);   // 100 ms
        Exchange(clock, 3, 0.06, 0.06, 0);   // 120 ms
        Assert.Equal(0.010, clock.Jitter, 9); // mean 110, every one 10 away
        Assert.Equal(10, clock.JitterMs, 6);
    }

    [Fact]
    public void The_smoothed_ping_follows_a_change_gradually()
    {
        var clock = new NetClock();
        for (int i = 0; i < 16; i++)
            Exchange(clock, i, 0.05, 0.05, 0);
        Exchange(clock, 16, 0.15, 0.15, 0); // one 300 ms spike
        Assert.Equal(0.3, clock.LastRtt, 9);
        Assert.Equal(0.1 + 0.2 * NetClock.Smoothing, clock.Rtt, 9);
        for (int i = 17; i < 60; i++)
            Exchange(clock, i, 0.15, 0.15, 0); // the route got slower for good
        Assert.InRange(clock.Rtt, 0.299, 0.301);
    }

    [Fact]
    public void Old_samples_leave_the_window()
    {
        var clock = new NetClock(window: 4);
        for (int i = 0; i < 4; i++)
            Exchange(clock, i, 0.01, 0.01, trueOffset: 5);
        Assert.Equal(5, clock.Offset, 9);
        // The peer's clock jumped (a new game copy); after a window of slower pairs only the new offset is left.
        for (int i = 4; i < 8; i++)
            Exchange(clock, i, 0.02, 0.02, trueOffset: 9);
        Assert.Equal(9, clock.Offset, 9);
        Assert.Equal(0.04, clock.MinRtt, 9);
    }

    [Fact]
    public void Times_convert_between_the_two_clocks()
    {
        var clock = new NetClock();
        Exchange(clock, 100, 0.02, 0.02, trueOffset: -40);
        Assert.Equal(60.5, clock.LocalToRemote(100.5), 9);
        Assert.Equal(100.5, clock.RemoteToLocal(60.5), 9);
        Assert.Equal(7.25, clock.RemoteToLocal(clock.LocalToRemote(7.25)), 9);
    }

    [Fact]
    public void Impossible_pairs_are_ignored_and_reset_forgets_everything()
    {
        var clock = new NetClock();
        Assert.False(clock.AddSample(10, 5, 9.9));               // came back before it left
        Assert.False(clock.AddSample(10, double.NaN, 10.1));
        Assert.False(clock.AddSample(double.NaN, 5, 10.1));
        Assert.False(clock.AddSample(10, double.PositiveInfinity, 10.1));
        Assert.False(clock.HasSamples);
        Assert.True(clock.AddSample(10, 5, 10.0));               // a round trip of 0 is fine
        clock.Reset();
        Assert.False(clock.HasSamples);
        Assert.Equal(0, clock.Rtt);
        Assert.Equal(0, clock.Offset);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetClock(0));
    }
}
