using System;

namespace DamageInsight.UI;

/// <summary>
/// The text of the cooldown ticker on the HUD's ability icons, WoW style: whole seconds below 100
/// ("83s"), minutes from 100 on ("10m" when exact, "2m+" when there is a rest). Plain logic, no Unity.
/// </summary>
public static class CooldownFormat
{
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            return "";
        // Round up, so the number only drops when a full second has passed and "1s" shows until the end.
        long s = (long)Math.Ceiling(seconds - 1e-4);
        if (s < 100)
            return $"{s}s";
        long minutes = s / 60;
        return s % 60 == 0 ? $"{minutes}m" : $"{minutes}m+";
    }
}
