using System;
using System.Collections.Generic;
using DamageInsight.Coop.Sync;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>
/// The whole drawing chain over a bad fake network (co-op §5.3, §9): a remote player sent at 20 Hz, packed and
/// unpacked, 150 ms delay, up to 40 ms jitter, 5 % loss (seeded), drawn at 60 frames per second.
/// </summary>
public class SyncSimulationTests
{
    private const double Frame = 1 / 60.0, Tick = 0.05, SenderClock = 500; // the sender's clock runs 500 s ahead

    private sealed class Run
    {
        public readonly List<double> Render = new();
        public readonly List<Vec2> Drawn = new();
        public readonly List<SampleKind> Kinds = new();
        public AdaptiveDelay Delay;
    }

    /// <summary>Runs <paramref name="seconds"/> of play; <paramref name="path"/> = the true position at a time,
    /// <paramref name="silent"/> = send times whose packets are lost (beside the random loss).</summary>
    private static Run Simulate(Func<double, Vec2> path, double seconds, int seed, Func<double, bool> silent = null)
    {
        var rng = new Random(seed);
        var delay = new AdaptiveDelay();
        var buffer = new SnapshotBuffer<PlayerState>();
        var run = new Run { Delay = delay };
        var inFlight = new List<(double arrive, double send, byte[] bytes)>();
        var w = new StateBytesWriter();
        int tick = 0;
        for (double now = 0; now < seconds; now += Frame)
        {
            for (; tick * Tick <= now; tick++)
            {
                double t = tick * Tick;
                if (rng.NextDouble() < 0.05 || (silent != null && silent(t)))
                    continue;
                var p = path(t);
                var v = (path(t + 0.001) - path(t - 0.001)) * (1 / 0.002f);
                w.Clear();
                new PlayerState { Position = p, Velocity = v, MotionId = 1, MotionTime = (float)t, Health = 50, MaxHealth = 50 }.Write(w);
                inFlight.Add((t + 0.15 + rng.NextDouble() * 0.04, t + SenderClock, w.ToArray()));
            }
            inFlight.Sort((a, b) => a.arrive.CompareTo(b.arrive));
            while (inFlight.Count > 0 && inFlight[0].arrive <= now)
            {
                var m = inFlight[0];
                inFlight.RemoveAt(0);
                delay.OnReceive(m.send, now); // arrival = the frame that polled it
                buffer.Add(m.send, PlayerState.Read(new StateBytesReader(m.bytes)));
            }
            double r = delay.Advance(now);
            var kind = buffer.Sample(r, out var s);
            if (kind == SampleKind.Empty)
                continue;
            run.Render.Add(r - SenderClock);
            run.Drawn.Add(s.Position);
            run.Kinds.Add(kind);
        }
        return run;
    }

    [Theory]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(23)]
    public void A_straight_run_is_drawn_without_backward_steps_or_jumps_and_close_to_the_true_path(int seed)
    {
        const float speed = 6;
        var run = Simulate(t => new Vec2(speed * (float)t, 1), 20, seed);
        int interpolated = 0;
        float maxStep = 0, maxError = 0;
        for (int i = 1; i < run.Drawn.Count; i++)
        {
            if (run.Render[i] < 2)
                continue; // the first two seconds: the delay settles
            float step = run.Drawn[i].X - run.Drawn[i - 1].X;
            Assert.True(step >= -1e-4f, $"backward step {step} at {run.Render[i]:0.000}");
            maxStep = Math.Max(maxStep, step);
            maxError = Math.Max(maxError, Math.Abs(run.Drawn[i].X - speed * (float)run.Render[i]));
            if (run.Kinds[i] == SampleKind.Interpolated)
                interpolated++;
        }
        // One frame of movement is 0.1 units; the render clock may run 8 % fast.
        Assert.InRange(maxStep, 0, (float)(speed * Frame * 1.15));
        Assert.InRange(maxError, 0, 0.05f);
        Assert.True(interpolated > 0.97 * (run.Drawn.Count - 120), $"only {interpolated} of {run.Drawn.Count} frames interpolated");
        Assert.InRange(run.Delay.Delay, 0.1f, 0.17f);
    }

    [Fact]
    public void A_curved_path_stays_within_a_tenth_of_a_unit_of_the_truth()
    {
        var run = Simulate(t => new Vec2(5 * (float)t, 2 * (float)Math.Sin(3 * t)), 20, seed: 8);
        float maxError = 0, maxStep = 0;
        for (int i = 1; i < run.Drawn.Count; i++)
        {
            if (run.Render[i] < 2)
                continue;
            double t = run.Render[i];
            var truth = new Vec2(5 * (float)t, 2 * (float)Math.Sin(3 * t));
            maxError = Math.Max(maxError, Vec2.Distance(run.Drawn[i], truth));
            maxStep = Math.Max(maxStep, Vec2.Distance(run.Drawn[i], run.Drawn[i - 1]));
        }
        Assert.InRange(maxError, 0, 0.1f);
        Assert.InRange(maxStep, 0, (float)(Math.Sqrt(25 + 36) * Frame * 1.2));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(29)]
    public void Without_updates_it_moves_on_a_quarter_second_waits_then_blends_back_without_a_snap(int seed)
    {
        const float speed = 4;
        var run = Simulate(t => new Vec2(speed * (float)t, 0), 12, seed, silent: t => t > 6 && t < 7);
        // The last update before the silence was at most at 6.0: drawn never past 6.0 + 0.25 s of movement.
        float limit = speed * (6.0f + 0.25f) + 0.02f;
        int resume = -1;
        for (int i = 0; i < run.Drawn.Count && resume < 0; i++)
            if (run.Render[i] > 6.3 && run.Kinds[i] == SampleKind.Interpolated)
                resume = i;
        Assert.True(resume > 0);
        bool held = false;
        for (int i = 0; i < resume; i++)
        {
            if (run.Render[i] <= 6)
                continue;
            Assert.True(run.Drawn[i].X <= limit, $"{run.Drawn[i].X} past the limit at {run.Render[i]:0.000}");
            held |= run.Kinds[i] == SampleKind.Held;
        }
        Assert.True(held);
        // Data came back: the gap (about 2-3 units) slides in over a few frames, it doesn't snap in one.
        Assert.InRange(run.Drawn[resume].X - run.Drawn[resume - 1].X, -0.01f, 1.5f);
        int settled = resume + 20; // a third of a second later
        Assert.InRange(Math.Abs(run.Drawn[settled].X - speed * (float)run.Render[settled]), 0, 0.05f);
        for (int i = resume + 1; i < run.Drawn.Count; i++)
            Assert.True(run.Drawn[i].X >= run.Drawn[i - 1].X - 1e-4f);
    }

    [Fact]
    public void A_teleport_is_one_clean_snap()
    {
        // Between two updates (5.025), so no update's velocity straddles the jump.
        var run = Simulate(t => t < 5.025 ? new Vec2(2 * (float)t, 0) : new Vec2(100 + 2 * (float)t, 0), 8, seed: 10);
        int jumps = 0;
        for (int i = 1; i < run.Drawn.Count; i++)
        {
            float step = run.Drawn[i].X - run.Drawn[i - 1].X;
            if (step > 1)
            {
                jumps++;
                Assert.InRange(step, 99, 101);
            }
        }
        Assert.Equal(1, jumps);
    }
}
