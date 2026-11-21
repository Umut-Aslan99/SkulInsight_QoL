using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// The full skull sweep (`--skulinsight-bot-skulls=fullsweep:HALF:FIRST[:SLOT[:A+B+C]]`, docs/coop/packages/FS.md):
/// one game acts at a time. The session clock (the host's time, the same on both games) is cut into turns of
/// <see cref="SlotSeconds"/>: the host's bot (half 0) acts in the even ones, the client's bot (half 1) in the odd ones,
/// and stands still in the other game's turns. No message is needed: both games read the same clock. In its turn a bot
/// plays its current pair of skulls through (attacks, every skill, the gauge, the swaps); the next turn has the next pair.
/// Pure logic (no game types), so the tests check it.
/// </summary>
public sealed class FullSweepPlan
{
    public const string Mode = "fullsweep";
    public const float DefaultSlotSeconds = 80f;
    /// <summary>A turn begins this long after its slot began (the other game's last echoes are over) ...</summary>
    public const float StartDelay = 1.5f;
    /// <summary>... and only within this window after that (a bot that was not ready waits for its next slot) ...</summary>
    public const float StartWindow = 4f;
    /// <summary>... and ends at the latest this long before its slot ends (cut: the last actions' shots still arrive).</summary>
    public const float EndMargin = 6f;

    /// <summary>0: the host's half of the list (even turns), 1: the client's (odd turns).</summary>
    public int Half { get; private set; }
    /// <summary>The pair the sweep starts at (a later call goes on where the last one stopped).</summary>
    public int FirstPair { get; private set; }
    public float SlotSeconds { get; private set; } = DefaultSlotSeconds;
    /// <summary>Only these skulls (a retry of the ones with a problem); null: the whole list.</summary>
    public IReadOnlyList<string> Only { get; private set; }

    /// <summary>From the launch option; null when it is no full sweep.</summary>
    public static FullSweepPlan Parse(string option)
    {
        if (string.IsNullOrWhiteSpace(option))
            return null;
        var parts = option.Trim().Split(':');
        if (!parts[0].Equals(Mode, StringComparison.OrdinalIgnoreCase))
            return null;
        var plan = new FullSweepPlan();
        if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int half))
            plan.Half = half & 1;
        if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int first))
            plan.FirstPair = Math.Max(0, first);
        if (parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float slot)
            && slot >= 30f && slot <= 600f)
            plan.SlotSeconds = slot;
        if (parts.Length > 4)
        {
            var only = parts[4].Split('+').Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();
            if (only.Count > 0)
                plan.Only = only;
        }
        return plan;
    }

    /// <summary>This game's skulls in play order: the whole list (or <see cref="Only"/>'s names that exist), in name
    /// order, every second one from the first for half 0, the others for half 1 (the two lists never overlap).</summary>
    public List<string> Playlist(IEnumerable<string> all)
    {
        var names = all.Where(n => !string.IsNullOrEmpty(n)).Distinct();
        if (Only != null)
        {
            var wanted = new HashSet<string>(Only, StringComparer.Ordinal);
            names = names.Where(wanted.Contains);
        }
        return names.OrderBy(n => n, StringComparer.Ordinal).Where((n, i) => i % 2 == Half).ToList();
    }

    /// <summary>The slot (turn number) a moment of the session clock is in.</summary>
    public long Slot(double sessionTime) => (long)Math.Floor(sessionTime / SlotSeconds);

    public double SlotStart(long slot) => slot * (double)SlotSeconds;

    /// <summary>The slot is this game's (half 0: even slots, half 1: odd ones).</summary>
    public bool Mine(long slot) => (slot & 1) == Half;

    /// <summary>A turn of this game may begin now: its slot, just after the slot's start.</summary>
    public bool CanBegin(double sessionTime)
    {
        long slot = Slot(sessionTime);
        double into = sessionTime - SlotStart(slot);
        return Mine(slot) && into >= StartDelay && into <= StartDelay + StartWindow;
    }

    /// <summary>The moment (session clock) a turn begun in <paramref name="slot"/> must be over.</summary>
    public double TurnEnd(long slot) => SlotStart(slot) + SlotSeconds - EndMargin;
}
