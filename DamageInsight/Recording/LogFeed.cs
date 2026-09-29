using System.Collections.Generic;
using Characters;

namespace DamageInsight.Recording;

/// <summary>
/// The filtered view of the damage log that the big and the mini log display.
/// Works incrementally: each Update only looks at hits recorded since the last call, and
/// only re-filters everything when the filter, the room (in "This room" scope) or the log itself changed.
/// Plain logic, no Unity, so it can be unit tested.
/// </summary>
public sealed class LogFeed
{
    public readonly LogFilter Filter;

    /// <summary>All hits that pass the filter, oldest first.</summary>
    public readonly List<DamageRecord> Shown = new();
    public readonly Dictionary<DamageSource, double> TotalBySource = new();
    public readonly Dictionary<Damage.Attribute, double> TotalByAttribute = new();

    /// <summary>Increases whenever Shown changes.</summary>
    public int Version { get; private set; }

    /// <summary>Increases whenever Shown was rebuilt from scratch (not just appended to).</summary>
    public int Rebuilds { get; private set; }

    private long _processedTotal;
    private int _filterVersion = -1;
    private int _clearCount = -1;
    private int _room = -1;

    public LogFeed(LogFilter filter)
    {
        Filter = filter;
    }

    /// <param name="records">The recorded hits (the oldest may have been trimmed).</param>
    /// <param name="totalAdded">How many hits were ever added, including trimmed ones.</param>
    /// <param name="clearCount">Increases whenever the log was cleared.</param>
    /// <param name="currentRoom">The room the player is in.</param>
    public void Update(IReadOnlyList<DamageRecord> records, long totalAdded, int clearCount, int currentRoom)
    {
        bool rebuild = _filterVersion != Filter.Version
                       || _clearCount != clearCount
                       || (Filter.Scope == LogScope.Room && _room != currentRoom)
                       || totalAdded < _processedTotal;

        long firstIndex = totalAdded - records.Count; // global index of records[0]
        int start;
        if (rebuild)
        {
            Shown.Clear();
            TotalBySource.Clear();
            TotalByAttribute.Clear();
            _filterVersion = Filter.Version;
            _clearCount = clearCount;
            _room = currentRoom;
            Rebuilds++;
            Version++;
            start = 0;
        }
        else
        {
            start = (int)System.Math.Max(0, _processedTotal - firstIndex);
        }

        bool added = false;
        for (int i = start; i < records.Count; i++)
        {
            var r = records[i];
            if (!Filter.Passes(r, currentRoom))
                continue;
            Shown.Add(r);
            Add(TotalBySource, r.Source, r.Amount);
            Add(TotalByAttribute, r.Attribute, r.Amount);
            added = true;
        }
        _processedTotal = totalAdded;
        if (added)
            Version++;
    }

    private static void Add<T>(Dictionary<T, double> totals, T key, double amount)
    {
        totals.TryGetValue(key, out double value);
        totals[key] = value + amount;
    }
}
