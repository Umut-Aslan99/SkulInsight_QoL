using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Rules;

/// <summary>
/// Who an enemy without a target of its own goes for when its code asks for "the player" (CoopTargeting.Standing):
/// the standing player nearest to it, this game's own player or a teammate's copy. It keeps its last pick while that
/// one stands and isn't much farther than the nearest (<see cref="KeepRatio"/>, <see cref="KeepSlack"/>), so two
/// players at about the same distance don't swap it from read to read.
/// Before, the answer was this game's player while it stood: on the host that is the host. Ratvibing's run
/// 2026-11-23 (COOP_LOG 68): the Dark Mirror chapter 4 Veteran Thief started its fight with no target, took the host,
/// who stood outside the arena, and Shadow Stepped to him for the whole fight; the client alone in the arena never
/// reached it ("the miniboss didn't spawn"). Also the same read gave each game its own player.
/// </summary>
public static class NoTargetPick
{
    /// <summary>The last pick stays while it is at most this many times the nearest one's distance...</summary>
    public const float KeepRatio = 1.5f;

    /// <summary>...or at most this much farther (map units), for players right next to the enemy.</summary>
    public const float KeepSlack = 3f;

    /// <summary>
    /// The index of the candidate to go for, -1 when none can be picked. <paramref name="distances"/>: one per
    /// candidate, its distance to the enemy; negative or NaN = can't be picked (down, dead, gone). Ties go to the
    /// lower index (the caller puts this game's player first). <paramref name="last"/>: the index of the last pick,
    /// -1 = none.
    /// </summary>
    public static int Pick(IReadOnlyList<float> distances, int last)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < distances.Count; i++)
        {
            float d = distances[i];
            if (!CanPick(d) || d >= bestDistance)
                continue;
            best = i;
            bestDistance = d;
        }
        if (best < 0)
            return -1;
        if (last >= 0 && last < distances.Count && last != best && CanPick(distances[last]) &&
            distances[last] <= Math.Max(bestDistance * KeepRatio, bestDistance + KeepSlack))
            return last;
        return best;
    }

    private static bool CanPick(float distance) => distance >= 0f && !float.IsNaN(distance);
}
