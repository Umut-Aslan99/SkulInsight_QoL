using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.History;

/// <summary>
/// Heals missed (0.14, Umut: "Many beginners miss them"): what the heal orbs left on a map would have done. An orb
/// counts when it would have healed: leaving a map 5 health short with three 10-health orbs on the ground missed one
/// orb and 5 health, not three. Plain logic, no Unity.
/// </summary>
public static class HealOrbMath
{
    /// <summary>The orbs left (their heal each) when the player left the map with <paramref name="hp"/> of <paramref name="max"/>.</summary>
    public static (int orbs, double health) Missed(IEnumerable<double> leftHeals, double hp, double max)
    {
        double missing = Math.Max(0, max - Math.Max(0, hp));
        if (missing < 0.5)
            return (0, 0);
        int orbs = 0;
        double health = 0;
        // The biggest first: the fewest orbs that would have filled the gap.
        foreach (double heal in leftHeals.Where(h => h > 0).OrderByDescending(h => h))
        {
            if (health >= missing)
                break;
            orbs++;
            health = Math.Min(missing, health + heal);
        }
        return (orbs, health);
    }
}
