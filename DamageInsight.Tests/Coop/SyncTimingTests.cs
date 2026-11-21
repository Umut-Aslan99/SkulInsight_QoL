using System;
using System.Collections.Generic;
using DamageInsight.Coop.Sync;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The render clock (AdaptiveDelay), soft corrections and timed events (co-op §5.3).</summary>
public class SyncTimingTests
{
    /// <summary>Feeds updates every 50 ms with a delay of 150 ms + up to <paramref name="jitter"/> seconds; returns
    /// the local time reached.</summary>
    private static double Feed(AdaptiveDelay d, Random rng, double from, double seconds, double jitter, List<double> renders = null)
    {
        double now = from;
        double nextSend = Math.Ceiling(from / 0.05) * 0.05;
        var inFlight = new List<(double arrive, double send)>();
        for (; now < from + seconds; now += 1 / 60.0)
        {
            while (nextSend <= now)
            {
                inFlight.Add((nextSend + 0.15 + rng.NextDouble() * jitter, nextSend + 1000)); // sender clock 1000 s ahead
                nextSend += 0.05;
            }
            inFlight.Sort((a, b) => a.arrive.CompareTo(b.arrive));
            while (inFlight.Count > 0 && inFlight[0].arrive <= now)
            {
                d.OnReceive(inFlight[0].send, inFlight[0].arrive);
                inFlight.RemoveAt(0);
            }
            double r = d.Advance(now);
            renders?.Add(r);
        }
        return now;
    }

    /// <summary>
    /// A line like UBDuck's (COOP_LOG 62): 30 updates a second, 50 ms one way, and every 2-6 s a queue on the relay
    /// that holds every update for 0.2-0.4 s by 100-200 ms more (the ping's jumps to 200-310 ms). Returns the share of
    /// frames whose render time had no newer update yet (the puppet must wait or guess: the visible jumps).
    /// </summary>
    private static double WaitingShare(AdaptiveDelay d, int seed)
    {
        var rng = new Random(seed);
        var spikes = new List<(double from, double to, double extra)>();
        for (double t = 3; t < 130; t += 2 + rng.NextDouble() * 4)
            spikes.Add((t, t + 0.2 + rng.NextDouble() * 0.2, 0.1 + rng.NextDouble() * 0.1));
        double now = 0, nextSend = 0, newest = double.NegativeInfinity, lastArrive = 0;
        var inFlight = new List<(double arrive, double send)>();
        int frames = 0, waiting = 0;
        for (; now < 120; now += 1 / 60.0)
        {
            while (nextSend <= now)
            {
                double extra = 0;
                foreach (var (from, to, add) in spikes)
                    if (nextSend >= from && nextSend < to)
                        extra = add;
                // a queue keeps the order: nothing overtakes an update that is held
                double arrive = Math.Max(lastArrive, nextSend + 0.05 + extra + rng.NextDouble() * 0.008);
                lastArrive = arrive;
                inFlight.Add((arrive, nextSend));
                nextSend += 1 / 30.0;
            }
            inFlight.Sort((a, b) => a.arrive.CompareTo(b.arrive));
            while (inFlight.Count > 0 && inFlight[0].arrive <= now)
            {
                d.OnReceive(inFlight[0].send, inFlight[0].arrive);
                newest = Math.Max(newest, inFlight[0].send);
                inFlight.RemoveAt(0);
            }
            double r = d.Advance(now);
            if (now > 10)
            {
                frames++;
                if (r > newest)
                    waiting++;
            }
        }
        return (double)waiting / frames;
    }

    [Fact]
    public void The_internet_profile_rides_out_late_bursts_the_default_does_not()
    {
        double plain = WaitingShare(new AdaptiveDelay(1 / 30f), 7);
        double internet = WaitingShare(AdaptiveDelay.ForInternet(1 / 30f), 7);
        Assert.True(internet < plain / 2, $"waiting frames: default {plain:P1}, internet {internet:P1}");
        Assert.True(internet < 0.02, $"internet profile waits {internet:P1} of frames");
        Assert.True(Environment.GetEnvironmentVariable("SHOW_SYNC_NUMBERS") == null, $"default {plain:P2}, internet {internet:P2}");
    }

    [Fact]
    public void The_internet_profile_costs_little_on_a_calm_line()
    {
        var d = AdaptiveDelay.ForInternet(1 / 30f);
        for (int i = 0; i < 300; i++)
            d.OnReceive(i / 30.0, i / 30.0 + 0.05 + (i % 2) * 0.004);
        Assert.InRange(d.Delay, 0.06f, 0.10f);
    }

    [Fact]
    public void The_delay_settles_on_two_updates_plus_the_jitter_whatever_the_clock_offset()
    {
        var d = new AdaptiveDelay();
        double now = Feed(d, new Random(1), 0, 10, 0.04);
        Assert.InRange(d.Interval, 0.048f, 0.052f);
        Assert.InRange(d.Jitter, 0.008f, 0.025f);
        Assert.InRange(d.Delay, 0.11f, 0.16f);
        // Render time is in the sender's clock: 1000 s ahead, minus latency and the buffer.
        double behind = now + 1000 - d.RenderTime;
        Assert.InRange(behind, 0.15 + 0.11, 0.15 + 0.04 + 0.16);
        Assert.InRange(Math.Abs(d.TargetAt(now) - d.RenderTime), 0, 0.03); // trails the moving target a little
    }

    [Fact]
    public void The_delay_stays_within_its_bounds()
    {
        var calm = new AdaptiveDelay();
        Feed(calm, new Random(2), 0, 5, 0);
        Assert.Equal(0.10f, calm.Delay, 3); // two updates at 20 Hz, no jitter
        var fast = new AdaptiveDelay(1 / 60f);
        for (int i = 0; i < 300; i++)
            fast.OnReceive(i / 60.0, i / 60.0 + 0.1);
        Assert.Equal(0.06f, fast.Delay, 3); // two updates at 60 Hz would be 33 ms: the floor
        var stormy = new AdaptiveDelay();
        Feed(stormy, new Random(3), 0, 5, 0.6);
        Assert.Equal(0.25f, stormy.Delay, 3);
    }

    [Fact]
    public void When_the_jitter_grows_the_render_clock_slows_gently_and_never_jumps_or_runs_back()
    {
        var d = new AdaptiveDelay();
        var renders = new List<double>();
        var rng = new Random(4);
        double now = Feed(d, rng, 0, 5, 0.01, renders);
        float before = d.Delay;
        renders.Clear();
        Feed(d, rng, now, 5, 0.12, renders);
        Assert.True(d.Delay > before + 0.04f, $"delay {before} -> {d.Delay}");
        double frame = 1 / 60.0;
        for (int i = 1; i < renders.Count; i++)
        {
            double step = renders[i] - renders[i - 1];
            Assert.InRange(step, frame * (1 - d.MaxWarp) - 1e-9, frame * (1 + d.MaxWarp) + 1e-9);
        }
    }

    [Fact]
    public void The_first_update_sets_the_render_clock_at_once()
    {
        var d = new AdaptiveDelay();
        d.Advance(5);
        Assert.False(d.HasData);
        d.OnReceive(100.0, 5.1);
        double r = d.Advance(5.1);
        Assert.True(d.Jumped);
        Assert.Equal(100.0 - d.Delay, r, 6);
        d.Advance(5.2);
        Assert.False(d.Jumped);
    }

    [Fact]
    public void A_correction_is_95_percent_gone_after_its_blend_time_and_a_big_one_snaps()
    {
        var c = new SmoothCorrection();
        Assert.False(c.Blend(new Vec2(1, 0), Vec2.Zero));
        Assert.Equal(1f, c.Offset.X);
        for (int f = 0; f < 9; f++) // 9 frames = 0.15 s
            c.Step(1 / 60f);
        Assert.InRange(c.Offset.X, 0.03f, 0.06f);
        for (int f = 0; f < 30; f++)
            c.Step(1 / 60f);
        Assert.False(c.IsActive);
        Assert.True(c.Blend(new Vec2(9, 0), Vec2.Zero));
        Assert.False(c.IsActive);
    }

    [Fact]
    public void Events_come_due_in_time_order_when_the_render_time_reaches_them()
    {
        var e = new TimedEvents<string>();
        e.Add(2.0, "attack");
        e.Add(1.0, "walk");
        e.Add(2.0, "attack sound"); // same time: after the one that came first
        e.Add(3.0, "die");
        var due = new List<DueEvent<string>>();
        Assert.Equal(0, e.Due(0.99, due));
        Assert.Equal(1, e.Due(1.0, due));
        Assert.Equal(2, e.Due(2.01, due));
        Assert.Equal(new[] { "walk", "attack", "attack sound" }, due.ConvertAll(x => x.Item));
        Assert.All(due, x => Assert.False(x.IsLate));
        Assert.Equal(0.01f, due[1].Lateness, 4);
        Assert.Equal(3.0, e.NextTime);
    }

    [Fact]
    public void An_event_that_arrives_after_its_moment_is_reported_late_by_how_much()
    {
        var e = new TimedEvents<int>();
        e.Add(1.0, 1);
        Assert.True(e.TryTakeDue(1.3, out var due));
        Assert.True(due.IsLate);
        Assert.Equal(0.3f, due.Lateness, 4);
        Assert.False(e.TryTakeDue(9, out _));
    }

    [Fact]
    public void The_event_queue_grows_and_reuses_its_space()
    {
        var e = new TimedEvents<int>(4);
        var due = new List<DueEvent<int>>();
        for (int round = 0; round < 5; round++)
        {
            for (int i = 0; i < 10; i++)
                e.Add(round * 10 + (9 - i), i); // reversed order
            Assert.Equal(10, e.Due(round * 10 + 9, due));
        }
        for (int i = 1; i < due.Count; i++)
            Assert.True(due[i].Time > due[i - 1].Time);
        Assert.Equal(0, e.Count);
    }
}
