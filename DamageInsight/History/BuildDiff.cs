using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.History;

/// <summary>The player's build at one moment, as Codex ids and names (filled from the inventory by RunJournal).</summary>
public sealed class BuildSnapshot
{
    public sealed class Piece
    {
        public string Id = "";
        public string Name = "";
        public string Rarity = "";
        /// <summary>Skulls: the Codex id of the skull it awakens into (empty when it can't).</summary>
        public string Next = "";
        /// <summary>Dark abilities: level.</summary>
        public int Level;
        /// <summary>Skulls: their skills' names.</summary>
        public string Extra = "";
    }

    /// <summary>The skulls in slot order (empty slots left out).</summary>
    public readonly List<Piece> Skulls = new();
    public string Held = "";
    public readonly List<Piece> Items = new();
    public readonly List<Piece> Essences = new();
    public readonly List<Piece> Darks = new();
    public Piece Fragment;
    /// <summary>Active inscriptions: key → name, count, step.</summary>
    public readonly Dictionary<string, (string name, int count, int step)> Inscriptions = new();
    /// <summary>Inscriptions in their tuned (true) form (the Demon Castle's box after the top step).</summary>
    public readonly HashSet<string> Tuned = new();
}

/// <summary>
/// Turns two build snapshots into timeline events (skull taken / dropped / awakened, item taken / lost / turned into
/// another, dark abilities, fragment, inscription steps). Comparing snapshots catches every way the game changes the
/// build (pickups, drops, swaps, transformations, a save being loaded). Plain logic.
/// </summary>
public static class BuildDiff
{
    /// <param name="changed">Item and quintessence changes the game reported as one turning into another (old id, new id).</param>
    /// <param name="maxSteps">The highest step each inscription reached in this run so far; updated.</param>
    public static List<RunEvent> Compare(BuildSnapshot before, BuildSnapshot after, int t,
        ICollection<(string from, string to)> changed, Dictionary<string, int> maxSteps)
    {
        var events = new List<RunEvent>();
        Skulls(before, after, t, events);
        Pieces(before.Items, after.Items, t, changed, RunEvents.ItemTaken, RunEvents.ItemLost, RunEvents.ItemChanged, events);
        Pieces(before.Essences, after.Essences, t, changed, RunEvents.EssenceTaken, RunEvents.EssenceLost, RunEvents.EssenceChanged, events);
        Darks(before, after, t, events);
        if (after.Fragment != null && after.Fragment.Id.Length > 0 && after.Fragment.Id != before.Fragment?.Id)
            events.Add(new RunEvent { T = t, Kind = RunEvents.Fragment, Id = after.Fragment.Id, Name = after.Fragment.Name });
        Inscriptions(after, t, maxSteps, events);
        foreach (var key in after.Tuned.Where(k => !before.Tuned.Contains(k)).OrderBy(k => k, System.StringComparer.Ordinal))
            events.Add(new RunEvent
            {
                T = t, Kind = RunEvents.InscriptionTuned, Id = key,
                Name = after.Inscriptions.TryGetValue(key, out var i) ? i.name : key,
            });
        return events;
    }

    private static void Skulls(BuildSnapshot before, BuildSnapshot after, int t, List<RunEvent> events)
    {
        var removed = Minus(before.Skulls, after.Skulls);
        var added = Minus(after.Skulls, before.Skulls);
        foreach (var a in added.ToList())
        {
            var from = removed.FirstOrDefault(r => r.Next.Length > 0 && r.Next == a.Id);
            if (from == null)
                continue;
            events.Add(new RunEvent { T = t, Kind = RunEvents.SkullAwakened, Id = a.Id, Name = a.Name, Other = from.Id, OtherName = from.Name });
            removed.Remove(from);
            added.Remove(a);
        }
        for (int i = 0; i < added.Count; i++)
        {
            var dropped = i < removed.Count ? removed[i] : null;
            events.Add(new RunEvent
            {
                T = t, Kind = RunEvents.SkullTaken, Id = added[i].Id, Name = added[i].Name,
                Other = dropped?.Id ?? "", OtherName = dropped?.Name ?? "",
            });
        }
        for (int i = added.Count; i < removed.Count; i++)
            events.Add(new RunEvent { T = t, Kind = RunEvents.SkullDropped, Id = removed[i].Id, Name = removed[i].Name });
    }

    private static void Pieces(List<BuildSnapshot.Piece> before, List<BuildSnapshot.Piece> after, int t,
        ICollection<(string from, string to)> changed, string taken, string lost, string turned, List<RunEvent> events)
    {
        var removed = Minus(before, after);
        var added = Minus(after, before);
        foreach (var a in added.ToList())
        {
            var from = removed.FirstOrDefault(r => changed != null && changed.Contains((r.Id, a.Id)));
            if (from == null)
                continue;
            events.Add(new RunEvent { T = t, Kind = turned, Id = a.Id, Name = a.Name, Other = from.Id, OtherName = from.Name });
            removed.Remove(from);
            added.Remove(a);
        }
        foreach (var r in removed)
            events.Add(new RunEvent { T = t, Kind = lost, Id = r.Id, Name = r.Name });
        foreach (var a in added)
            events.Add(new RunEvent { T = t, Kind = taken, Id = a.Id, Name = a.Name });
    }

    private static void Darks(BuildSnapshot before, BuildSnapshot after, int t, List<RunEvent> events)
    {
        foreach (var d in after.Darks)
        {
            var old = before.Darks.FirstOrDefault(b => b.Id == d.Id);
            if (old == null || d.Level > old.Level)
                events.Add(new RunEvent { T = t, Kind = RunEvents.DarkAbility, Id = d.Id, Name = d.Name, Value = d.Level });
        }
    }

    private static void Inscriptions(BuildSnapshot after, int t, Dictionary<string, int> maxSteps, List<RunEvent> events)
    {
        foreach (var pair in after.Inscriptions.OrderBy(p => p.Key, System.StringComparer.Ordinal))
        {
            int step = pair.Value.step;
            maxSteps.TryGetValue(pair.Key, out int max);
            if (step <= max)
                continue;
            maxSteps[pair.Key] = step;
            events.Add(new RunEvent { T = t, Kind = RunEvents.Inscription, Id = pair.Key, Name = pair.Value.name, Value = step });
        }
    }

    /// <summary>The pieces of <paramref name="a"/> that <paramref name="b"/> doesn't have (as a multiset, by id).</summary>
    private static List<BuildSnapshot.Piece> Minus(List<BuildSnapshot.Piece> a, List<BuildSnapshot.Piece> b)
    {
        var left = b.Select(p => p.Id).ToList();
        var result = new List<BuildSnapshot.Piece>();
        foreach (var p in a)
        {
            int i = left.IndexOf(p.Id);
            if (i >= 0)
                left.RemoveAt(i);
            else
                result.Add(p);
        }
        return result;
    }
}

/// <summary>What the journal does with the runs on disk when a run is being played (plain logic).</summary>
public static class RunResume
{
    /// <summary>
    /// The run in progress with this seed continues; every other run still marked in progress was left without an
    /// end (a new run started) and becomes abandoned.
    /// </summary>
    public static (RunRecord resume, List<RunRecord> abandon) Decide(IEnumerable<RunRecord> open, int seed)
    {
        RunRecord resume = null;
        var abandon = new List<RunRecord>();
        foreach (var r in open.Where(r => r.Result == RunResult.InProgress).OrderByDescending(r => r.Started))
        {
            if (resume == null && r.Seed == seed)
                resume = r;
            else
                abandon.Add(r);
        }
        return (resume, abandon);
    }
}
