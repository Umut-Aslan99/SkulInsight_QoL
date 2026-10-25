using System;
using FX;
using Singletons;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// The achievement notice's chime (Umut 2026-10-25: "a cute sound effect, not too intrusive but it shall feel
/// rewarding, like a little coin pickup"): two quick notes, B5 then a ringing E6 with a faint B6 sparkle, a soft
/// square tone, about half a second. Made in code (no sound file), played through the game's SoundManager, so the
/// game's master and effects volume apply on top of the setting Codex/AchievementSound (0 = off).
/// </summary>
internal static class AchievementSound
{
    public const int Rate = 44100;
    private const float Length = 0.48f, Gain = 0.42f, Attack = 0.004f;

    // (frequency Hz, start s, hold s, decay time constant s, level)
    private static readonly (float freq, float start, float hold, float decay, float level)[] Notes =
    {
        (987.77f, 0.000f, 0.070f, 0.030f, 1.00f),  // B5: the short first note
        (1318.51f, 0.075f, 0.000f, 0.120f, 1.00f), // E6: the ringing second note
        (1975.53f, 0.075f, 0.000f, 0.050f, 0.22f), // B6: a little sparkle on top
    };

    private static SoundInfo _sound;

    public static void Play()
    {
        float volume = Plugin.AchievementSound?.Value ?? 0.5f;
        if (volume <= 0f)
            return;
        Patches.Guard.Run("Achievement sound", () =>
        {
            if (_sound == null)
            {
                var clip = AudioClip.Create("SkulInsight_AchievementChime", (int)(Rate * Length), 1, Rate, false);
                clip.SetData(Samples(), 0);
                // Kept for good: SoundInfo's finalizer would let go of the clip.
                _sound = new SoundInfo(clip) { _uniqueTime = 0.2f };
            }
            _sound._volume = Mathf.Clamp01(volume);
            PersistentSingleton<SoundManager>.Instance?.PlaySound(_sound, Vector3.zero);
        });
    }

    /// <summary>The chime's samples (mono, <see cref="Rate"/> Hz, peak <see cref="Gain"/>). Plain math, tests use it.</summary>
    internal static float[] Samples()
    {
        int n = (int)(Rate * Length);
        var output = new float[n];
        foreach (var (freq, start, hold, decay, level) in Notes)
            for (int i = (int)(Rate * start); i < n; i++)
            {
                double t = (double)i / Rate - start;
                double envelope = Math.Min(1.0, t / Attack) * (t < hold ? 1.0 : Math.Exp(-(t - hold) / decay));
                if (envelope < 1e-4 && t > hold)
                    break;
                double phase = 2 * Math.PI * freq * t;
                // A soft square: the first three odd harmonics, gentler than a real square wave.
                output[i] += (float)(level * envelope * (Math.Sin(phase) + 0.2 * Math.Sin(3 * phase) + 0.08 * Math.Sin(5 * phase)));
            }
        int fade = Rate / 100;
        for (int i = 0; i < fade; i++)
            output[n - 1 - i] *= (float)i / fade;
        float peak = 0f;
        foreach (float s in output)
            peak = Math.Max(peak, Math.Abs(s));
        for (int i = 0; i < n; i++)
            output[i] = Gain * output[i] / peak;
        return output;
    }
}
