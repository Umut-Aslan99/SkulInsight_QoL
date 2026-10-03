using System;
using System.Collections.Generic;

namespace DamageInsight.Codex;

/// <summary>
/// Developer tool (Developer/PreferNewMoves): bosses pick moves the Codex hasn't seen yet first, then moves they haven't
/// done in this fight, so a few fights show (and film) every move. The random choices of the trees are steered
/// (<see cref="Patches.PreferNewMovesPatches"/>); conditions such as HP range, distance and cooldowns still decide
/// whether a branch can run. Coroutine AIs (Leiana sisters, Chimera) pick in their own code and are not steered.
/// Plain logic, no Unity.
/// </summary>
public static class MovePicker
{
    private static readonly Random Random = new();

    public static bool On => Plugin.PreferNewMoves != null && Plugin.PreferNewMoves.Value;

    /// <summary>
    /// The child to run instead of the game's random pick: one of the children with the highest want (2 = an unseen
    /// move, 1 = a move not done in this fight), at random among equals. -1 (keep the game's pick) when no child is
    /// wanted. A want below 0 marks a child the game would never pick now (weight 0).
    /// </summary>
    public static int Pick(IReadOnlyList<int> wants, Random random = null)
    {
        int best = 0;
        var picks = new List<int>();
        for (int i = 0; i < wants.Count; i++)
        {
            if (wants[i] > best)
            {
                best = wants[i];
                picks.Clear();
            }
            if (wants[i] == best && best > 0)
                picks.Add(i);
        }
        return picks.Count == 0 ? -1 : picks[(random ?? Random).Next(picks.Count)];
    }
}
