using System;
using System.Linq;
using DamageInsight.Coop;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The revive ring's logic (Umut 2026-11-14: a ring that fills, pulses like a heartbeat, with a heartbeat
/// sound): the beat's times, what the ring shows between two messages, and the generated sounds.</summary>
public class ReviveRingTests
{
    private const double Frame = 1.0 / 60;

    /// <summary>Runs the model frame by frame; returns the moments a beat began.</summary>
    private static double[] Run(ReviveRingModel model, double from, double to, Action<double> eachFrame = null, bool beating = true)
    {
        var beats = new System.Collections.Generic.List<double>();
        for (double now = from; now < to; now += Frame)
        {
            eachFrame?.Invoke(now);
            if (model.Tick(now, beating))
                beats.Add(now);
        }
        return beats.ToArray();
    }

    [Fact]
    public void The_heart_beats_faster_towards_the_end_of_a_hold()
    {
        Assert.Equal(ReviveBeat.IdleRate, ReviveBeat.Rate(false, 0.9f));
        Assert.Equal(ReviveBeat.HoldStartRate, ReviveBeat.Rate(true, 0f));
        Assert.Equal(ReviveBeat.HoldEndRate, ReviveBeat.Rate(true, 1f));
        Assert.True(ReviveBeat.Rate(true, 0.5f) < ReviveBeat.Rate(true, 0.8f));
        Assert.True(ReviveBeat.IdleRate < ReviveBeat.HoldStartRate);

        // a 3 s hold, reported every frame as the reviver's own game does
        var model = new ReviveRingModel();
        var beats = Run(model, 0, 3, now => model.Report(0, 1, (float)(now / 3), 3f, now));
        Assert.InRange(beats.Length, 5, 8);
        Assert.Equal(0, beats[0], 3); // the first beat comes with the press
        var gaps = beats.Zip(beats.Skip(1), (a, b) => b - a).ToArray();
        for (int i = 1; i < gaps.Length; i++)
            Assert.True(gaps[i] < gaps[i - 1] + 1e-6, $"gap {i} ({gaps[i]:0.000}) is not shorter than the one before ({gaps[i - 1]:0.000})");
        Assert.True(gaps[gaps.Length - 1] < 0.5);
        // a beat is never shorter than the sound's two thumps
        Assert.True(1f / ReviveBeat.HoldEndRate > ReviveBeat.DubSeconds + 0.1f);
    }

    [Fact]
    public void A_beat_is_a_big_thump_and_a_smaller_one()
    {
        Assert.Equal(0f, ReviveBeat.Pulse(-1f));
        Assert.Equal(0f, ReviveBeat.Pulse(0f));
        Assert.Equal(1f, ReviveBeat.Pulse(0.03f), 3);                     // lub
        float between = ReviveBeat.Pulse(ReviveBeat.DubSeconds - 0.005f);
        float dub = ReviveBeat.Pulse(ReviveBeat.DubSeconds + 0.03f);
        Assert.True(dub > between, "the second thump rises again");
        Assert.InRange(dub, 0.5f, 0.7f);
        Assert.True(ReviveBeat.Pulse(0.6f) < 0.01f);                       // at rest before the next beat
    }

    [Fact]
    public void The_ring_moves_on_evenly_between_a_teammates_messages()
    {
        var model = new ReviveRingModel();
        double next = 0;
        float last = 0f, biggestStep = 0f;
        Run(model, 0, 2.4, now =>
        {
            if (now >= next) // ten messages a second
            {
                model.Report(1, 1, (float)(now / 3), 3f, now);
                next += 0.1;
            }
            biggestStep = Math.Max(biggestStep, model.Progress - last);
            Assert.True(model.Progress >= last, "the ring never runs back during a hold");
            last = model.Progress;
        });
        Assert.True(model.Holding);
        Assert.Equal(1, model.By);
        Assert.Equal(0.8f, model.Progress, 1);
        Assert.True(biggestStep < 0.03f, $"a step of {biggestStep} would be seen as a jump");
    }

    [Fact]
    public void Messages_that_stop_coming_end_the_hold_and_the_ring_runs_empty()
    {
        var model = new ReviveRingModel();
        Run(model, 0, 1.5, now => model.Report(1, 1, (float)(now / 3), 3f, now));
        Assert.InRange(model.Progress, 0.45f, 0.55f);
        Run(model, 1.5, 1.5 + ReviveRingModel.ReportTimeout - 0.05);
        Assert.True(model.Holding); // still within the wait: the ring went on by itself
        Assert.True(model.Progress > 0.6f);
        Run(model, 2.05, 2.2);
        Assert.False(model.Holding);
        Assert.Equal(-1, model.By);
        Run(model, 2.2, 2.2 + ReviveRingModel.DrainSeconds + 0.1);
        Assert.Equal(0f, model.Progress);
    }

    [Fact]
    public void A_broken_hold_drains_and_its_late_message_does_not_start_it_again()
    {
        var model = new ReviveRingModel();
        Run(model, 0, 1.0, now => model.Report(1, 5, (float)(now / 3), 3f, now));
        model.Stop(1, 5);
        Assert.False(model.Holding);
        model.Report(1, 5, 0.34f, 3f, 1.02); // overtaken by the "it ended"
        Assert.False(model.Holding);
        double mid = 1.0 + ReviveRingModel.DrainSeconds / 6; // a third of a ring is gone in a third of the drain time
        Run(model, 1.0, mid);
        Assert.InRange(model.Progress, 0.05f, 0.3f); // on its way down, not gone at once
        Run(model, mid, 1.6);
        Assert.Equal(0f, model.Progress);

        model.Report(1, 6, 0f, 3f, 1.6); // the next hold counts
        Assert.True(model.Holding);
        // someone else's "it ended" does not end this one
        model.Stop(2, 6);
        Assert.True(model.Holding);
    }

    [Fact]
    public void Two_revivers_on_one_player_the_ring_follows_the_one_who_is_further()
    {
        var model = new ReviveRingModel();
        model.Report(1, 1, 0.6f, 3f, 0);
        model.Tick(0, true);
        model.Report(2, 1, 0.2f, 3f, 0.05);
        Assert.Equal(1, model.By);
        model.Report(2, 1, 0.9f, 3f, 0.1);
        Assert.Equal(2, model.By);
    }

    [Fact]
    public void A_heart_that_stands_still_does_not_beat()
    {
        var model = new ReviveRingModel();
        var beats = Run(model, 0, 3, beating: false);
        Assert.Empty(beats);
        Assert.Equal(0f, model.Pulse);
        // waiting for a teammate: a slow beat
        beats = Run(model, 3, 9);
        Assert.InRange(beats.Length, 4, 5);
    }

    [Fact]
    public void The_heartbeat_sound_is_two_low_thumps_in_step_with_the_ring()
    {
        var samples = ReviveSound.BeatSamples();
        double length = samples.Length / (double)ReviveSound.Rate;
        Assert.InRange(length, 0.3, 0.5);
        Assert.True(length < 1.0 / ReviveBeat.HoldEndRate + 0.1, "a beat's sound is over before the next beat is far in");
        Assert.InRange(samples.Max(Math.Abs), 0.8f, 1f);
        Assert.Equal(0f, samples[samples.Length - 1], 3);
        // loud at the lub and at the dub, quiet between them and at the end
        Assert.True(Level(samples, 0.00, 0.06) > 0.25);
        Assert.True(Level(samples, ReviveBeat.DubSeconds, ReviveBeat.DubSeconds + 0.06) > 0.12);
        Assert.True(Level(samples, ReviveBeat.DubSeconds - 0.03, ReviveBeat.DubSeconds) < Level(samples, ReviveBeat.DubSeconds, ReviveBeat.DubSeconds + 0.03));
        Assert.True(Level(samples, 0.36, 0.42) < 0.02);
        // the second thump is the smaller one
        Assert.True(Level(samples, 0.00, 0.06) > Level(samples, ReviveBeat.DubSeconds, ReviveBeat.DubSeconds + 0.06));
        // low: few zero crossings (a 60-190 Hz thump, not a beep)
        int crossings = Enumerable.Range(1, samples.Length - 1).Count(i => samples[i - 1] < 0 != samples[i] < 0);
        Assert.InRange(crossings / length / 2, 30, 400);
    }

    [Fact]
    public void The_revive_sound_rings_out_under_a_second()
    {
        var samples = ReviveSound.LandSamples();
        double length = samples.Length / (double)ReviveSound.Rate;
        Assert.InRange(length, 0.6, 1.0);
        Assert.InRange(samples.Max(Math.Abs), 0.5f, 0.7f);
        Assert.Equal(0f, samples[samples.Length - 1], 3);
        Assert.True(Level(samples, 0.0, 0.3) > Level(samples, 0.6, 0.9) * 3, "it dies away");
        Assert.DoesNotContain(samples, float.IsNaN);
    }

    /// <summary>Root mean square between two moments.</summary>
    private static double Level(float[] samples, double from, double to)
    {
        int a = (int)(from * ReviveSound.Rate), b = Math.Min(samples.Length, (int)(to * ReviveSound.Rate));
        double sum = 0;
        for (int i = a; i < b; i++)
            sum += samples[i] * (double)samples[i];
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }
}
