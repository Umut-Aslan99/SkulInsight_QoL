using System;
using System.Collections.Generic;

namespace DamageInsight.Describe;

/// <summary>
/// The rules the pickup preview needs from the game, as plain logic (tested without Unity):
/// which step an inscription reaches with a given count (Inscription.Update), how a SimpleStatBonusKeyword turns
/// its per-step number into a stat value (SimpleStatBonusKeyword.UpdateStat), and how a stat's final value is
/// computed from its categories (Stat.Update).
/// </summary>
public static class PickupMath
{
    /// <summary>The highest step index whose threshold the count reaches (steps[0] is 0 = inactive).</summary>
    public static int StepFor(IReadOnlyList<int> steps, int count)
    {
        int step = 0;
        for (int i = 0; i < steps.Count && count >= steps[i]; i++)
            step = i;
        return step;
    }

    /// <summary>The stat value a keyword attaches for a raw per-step number ("45" → +0.45 or x1.45).</summary>
    public static double StatValue(string category, double raw) => category switch
    {
        "Percent" => raw * 0.01 + 1.0,
        "PercentPoint" => raw * 0.01,
        _ => raw,
    };

    /// <summary>Inscription count changes: +1 per key of the picked-up item, -1 per key of the item it replaces.</summary>
    public static Dictionary<string, int> CountDeltas(string[] added, string[] removed)
    {
        var deltas = AddedCounts(added);
        foreach (var pair in AddedCounts(removed))
            deltas[pair.Key] = (deltas.TryGetValue(pair.Key, out int n) ? n : 0) - pair.Value;
        return deltas;
    }

    /// <summary>How many of each inscription the item adds (an item can carry the same inscription twice).</summary>
    public static Dictionary<string, int> AddedCounts(params string[] keys)
    {
        var added = new Dictionary<string, int>();
        foreach (var key in keys)
            if (!string.IsNullOrEmpty(key) && key != "None")
                added[key] = added.TryGetValue(key, out int n) ? n + 1 : 1;
        return added;
    }
}

/// <summary>
/// A stat as the game keeps it, per category (Stat.Update): Percent values multiply, PercentPoint values add,
/// and a percent-form stat's final value is Percent x (1 + PercentPoint). Enough to check the preview's plan.
/// </summary>
public sealed class StatModel
{
    public double Percent = 1, PercentPoint;

    public void Apply(string category, double value)
    {
        if (category == "Percent")
            Percent *= value;
        else if (category == "PercentPoint")
            PercentPoint += value;
        else
            throw new ArgumentException(category);
    }

    public double Final => Percent * (1 + PercentPoint);
}
