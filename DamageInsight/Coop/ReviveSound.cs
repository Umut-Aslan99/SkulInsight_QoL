using System;
using FX;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// The sounds of a revive (Umut 2026-11-14: "give it a heartbeat sound"): one heartbeat ("lub-dub", two soft low
/// thumps) for every beat of the ring while a teammate holds, and a short rising chime with a deep thump when the
/// revive lands. Made in code like the achievement chime (no sound files), played through the game's SoundManager,
/// so the game's master and effects volume apply on top of the setting Coop/ReviveSound (0 = off).
/// </summary>
internal static class ReviveSound
{
    public const int Rate = 44100;
    private const float BeatLength = 0.42f, BeatGain = 1f, LandLength = 0.95f, LandGain = 0.6f, Attack = 0.004f;
    // Umut 2026-11-15: "maybe just a little too quiet, when the music is running". A hold now starts at 80 % of the
    // set volume (was 55 %), and the beat is pressed against its peak (BeatDrive): louder at the same peak, with
    // overtones that carry over the music's bass.
    private const float BeatFloor = 0.8f, BeatDrive = 1.5f;

    private static SoundInfo _beat, _land;

    private static float Volume => Mathf.Clamp01(Plugin.CoopReviveSound?.Value ?? 0.6f);

    /// <summary>One heartbeat. <paramref name="strength"/> 0..1: quieter at the start of a hold, full at its end.</summary>
    public static void Beat(float strength)
    {
        float volume = Volume * Mathf.Lerp(BeatFloor, 1f, Mathf.Clamp01(strength));
        if (volume <= 0f)
            return;
        Patches.Guard.Run("Revive heartbeat", () =>
        {
            // Kept for good: SoundInfo's finalizer would let go of the clip.
            _beat ??= Make("SkulInsight_ReviveHeartbeat", BeatSamples(), 0.12f);
            Play(_beat, volume);
        });
    }

    /// <summary>The revive landed. <paramref name="strong"/>: a teammate's revive; false: back by themselves when
    /// the room was cleared (the same sound, quieter).</summary>
    public static void Landed(bool strong)
    {
        float volume = Volume * (strong ? 1f : 0.5f);
        if (volume <= 0f)
            return;
        Patches.Guard.Run("Revive sound", () =>
        {
            _land ??= Make("SkulInsight_ReviveLanded", LandSamples(), 0.2f);
            Play(_land, volume);
        });
    }

    private static SoundInfo Make(string name, float[] samples, float uniqueTime)
    {
        var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
        clip.SetData(samples, 0);
        return new SoundInfo(clip) { _uniqueTime = uniqueTime };
    }

    private static void Play(SoundInfo sound, float volume)
    {
        sound._volume = volume;
        PersistentSingleton<SoundManager>.Instance?.PlaySound(sound, Vector3.zero);
    }

    /// <summary>One heartbeat (mono, <see cref="Rate"/> Hz): "lub" at 0 s, a smaller and slightly higher "dub"
    /// Rules.ReviveBeat.DubSeconds later. Plain math, tests use it.</summary>
    internal static float[] BeatSamples()
    {
        var output = new float[(int)(Rate * BeatLength)];
        Thump(output, 0f, 150f, 62f, 0.035f, 0.075f, 1f);
        Thump(output, Rules.ReviveBeat.DubSeconds, 190f, 80f, 0.03f, 0.055f, 0.7f);
        return Finish(output, BeatGain, BeatDrive);
    }

    /// <summary>The revive landing: a deep thump, a quick rise, four rising bell notes (A major) that ring out.</summary>
    internal static float[] LandSamples()
    {
        var output = new float[(int)(Rate * LandLength)];
        Thump(output, 0f, 170f, 55f, 0.05f, 0.13f, 1.1f);
        Rise(output, 0f, 0.2f, 300f, 1250f, 0.16f);
        Bell(output, 440.00f, 0.03f, 0.10f, 0.55f);  // A4
        Bell(output, 554.37f, 0.09f, 0.11f, 0.6f);   // C#5
        Bell(output, 659.25f, 0.15f, 0.13f, 0.7f);   // E5
        Bell(output, 880.00f, 0.21f, 0.24f, 1f);     // A5: the note that rings
        Bell(output, 1318.51f, 0.21f, 0.14f, 0.3f);  // E6: a little light on top
        return Finish(output, LandGain);
    }

    /// <summary>A soft drum hit: a sine that falls from <paramref name="from"/> to <paramref name="to"/> Hz and dies
    /// away, with two overtones so small speakers carry it too.</summary>
    private static void Thump(float[] output, float start, float from, float to, float sweep, float decay, float level)
    {
        for (int i = (int)(Rate * start); i < output.Length; i++)
        {
            double t = (double)i / Rate - start;
            double envelope = Math.Min(1.0, t / Attack) * Math.Exp(-t / decay);
            if (envelope < 1e-4 && t > Attack)
                break;
            // the phase is the integral of the falling pitch
            double phase = 2 * Math.PI * (to * t + (from - to) * sweep * (1 - Math.Exp(-t / sweep)));
            output[i] += (float)(level * envelope * (Math.Sin(phase) + 0.45 * Math.Sin(2 * phase) + 0.2 * Math.Sin(3 * phase)));
        }
    }

    private static void Bell(float[] output, float freq, float start, float decay, float level)
    {
        for (int i = (int)(Rate * start); i < output.Length; i++)
        {
            double t = (double)i / Rate - start;
            double envelope = Math.Min(1.0, t / Attack) * Math.Exp(-t / decay);
            if (envelope < 1e-4 && t > Attack)
                break;
            double phase = 2 * Math.PI * freq * t;
            output[i] += (float)(level * envelope * (Math.Sin(phase) + 0.3 * Math.Sin(2 * phase) + 0.12 * Math.Sin(3 * phase)));
        }
    }

    /// <summary>A quiet tone gliding up: the breath before the notes.</summary>
    private static void Rise(float[] output, float start, float length, float from, float to, float level)
    {
        int first = (int)(Rate * start), count = (int)(Rate * length);
        double phase = 0;
        for (int i = 0; i < count && first + i < output.Length; i++)
        {
            double x = (double)i / count;
            phase += 2 * Math.PI * (from + (to - from) * x * x) / Rate;
            output[first + i] += (float)(level * Math.Sin(Math.PI * x) * Math.Sin(phase));
        }
    }

    /// <summary>Fades the end out and sets the peak to <paramref name="gain"/>. <paramref name="drive"/> above 0
    /// presses the sound against that peak (a soft limiter: the quieter parts come up, the peak stays).</summary>
    private static float[] Finish(float[] output, float gain, float drive = 0f)
    {
        int n = output.Length, fade = Rate / 100;
        for (int i = 0; i < fade; i++)
            output[n - 1 - i] *= (float)i / fade;
        float peak = 0f;
        foreach (float s in output)
            peak = Math.Max(peak, Math.Abs(s));
        if (peak <= 0f)
            return output;
        double top = drive > 0f ? Math.Tanh(drive) : 1.0;
        for (int i = 0; i < n; i++)
        {
            double x = output[i] / peak;
            output[i] = (float)(gain * (drive > 0f ? Math.Tanh(drive * x) / top : x));
        }
        return output;
    }
}
