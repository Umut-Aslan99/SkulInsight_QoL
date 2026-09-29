using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.Recording;

public readonly struct Slice<TKey>
{
    public readonly TKey Key;
    public readonly double Value;
    public readonly double Percent; // 0..100

    public Slice(TKey key, double value, double percent)
    {
        Key = key;
        Value = value;
        Percent = percent;
    }
}

/// <summary>Damage shares for the pie chart. Plain logic, no Unity, so it can be unit tested.</summary>
public static class DamageStats
{
    /// <summary>Total damage per key, largest first, with each slice's share of the total.</summary>
    public static List<Slice<TKey>> Shares<TKey>(IEnumerable<DamageRecord> records, Func<DamageRecord, TKey> keyOf) =>
        FromTotals(records.GroupBy(keyOf).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount)));

    /// <summary>Slices from already-summed totals per key, largest first.</summary>
    public static List<Slice<TKey>> FromTotals<TKey>(IDictionary<TKey, double> totals)
    {
        double total = totals.Values.Sum();
        return totals
            .Where(t => t.Value > 0)
            .OrderByDescending(t => t.Value)
            .Select(t => new Slice<TKey>(t.Key, t.Value, total > 0 ? t.Value / total * 100.0 : 0.0))
            .ToList();
    }
}
