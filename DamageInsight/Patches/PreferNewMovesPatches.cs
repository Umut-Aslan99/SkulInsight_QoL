#if DEV // a developer tool: release builds carry none of these hooks
using System;
using System.Collections.Generic;
using System.Linq;
using Characters.AI.Behaviours;
using DamageInsight.Codex;
using HarmonyLib;
using BDWeightedSelector = BehaviorDesigner.Runtime.Tasks.WeightedSelector;

namespace DamageInsight.Patches;

/// <summary>
/// Developer/PreferNewMoves (<see cref="MovePicker"/>): steers the random choices of boss trees towards moves the Codex
/// hasn't seen, then towards moves not done in this fight. The game's own blocks keep running (the fight recorder
/// follows their coroutines to cut takes); only what they pick from is changed, right before they pick:
/// <list type="bullet">
/// <item>BehaviorDesigner WeightedSelector (Dark Skul, First Dark Hero): the child index it chose in OnStart.</item>
/// <item>Behaviour blocks (First Hero, Pope): a WeightedSelector's randomizer holds only the wanted child, a
/// RandomBehaviour's list only the wanted child, a Chance always succeeds when its block is wanted. The game's own
/// value is put back as soon as nothing is wanted or the setting is off.</item>
/// </list>
/// </summary>
public static class PreferNewMovesPatches
{
    private static readonly Dictionary<object, object> Originals = new(); // steered block → the game's own value

    /// <summary>One log line per steered pick, so a test fight shows that (and where) the tool works.</summary>
    private static void Logged(string block, object child, int want)
    {
        FightRecorder.NoteSteered(child);
        Plugin.Log.LogInfo($"PreferNewMoves: {block} → {FightRecorder.MovesText(child)} ({(want >= 2 ? "unseen or unfilmed" : "not done this fight")})");
    }

    /// <summary>Before a block picks: the steered value in, or the game's own value back.</summary>
    private static void SteerSafe(object block, string name, Func<object> current, Func<object> steered, Action<object> set)
    {
        if (!MovePicker.On && Originals.Count == 0)
            return;
        Guard.Run($"Prefer new moves ({name})", () =>
        {
            bool had = Originals.TryGetValue(block, out var original);
            var value = MovePicker.On ? steered() : null;
            if (value != null)
            {
                if (!had)
                    Originals[block] = current();
                set(value);
            }
            else if (had)
            {
                set(original);
                Originals.Remove(block);
            }
        });
    }

    [HarmonyPatch(typeof(BDWeightedSelector), "OnStart")]
    public static class TreeWeightedSelector
    {
        // excludePreviousIndex: the child the game left out this time (not the same child twice in a row).
        private static void Prefix(BDWeightedSelector __instance, out int __state)
        {
            __state = -1;
            try
            {
                if (MovePicker.On && __instance._excludePreviousIndex)
                    __state = __instance._prevChildIndex;
            }
            catch (Exception)
            {
                // keep the game's pick
            }
        }

        private static void Postfix(BDWeightedSelector __instance, int __state)
        {
            if (!MovePicker.On)
                return;
            Guard.Run("Prefer new moves (tree)", () =>
            {
                var children = __instance.Children;
                var weights = __instance._weights;
                if (children == null || weights == null)
                    return;
                int current = __instance._currentChildIndex;
                var wants = new int[children.Count];
                for (int i = 0; i < wants.Length; i++)
                    wants[i] = i == __state || i >= weights.Count || (weights[i] <= 0f && i != current) ? -1 : FightRecorder.Want(children[i]);
                int pick = MovePicker.Pick(wants);
                if (pick < 0 || pick == current)
                    return;
                if (__instance._excludePreviousIndex)
                {
                    // The game set its own pick's weight to 0 for next time: give it back and leave ours out instead.
                    weights[current] = __instance._prevChildWeight;
                    __instance._prevChildWeight = weights[pick];
                    weights[pick] = 0f;
                    __instance._prevChildIndex = pick;
                }
                __instance._currentChildIndex = pick;
                Logged("tree selector", children[pick], wants[pick]);
            });
        }
    }

    [HarmonyPatch(typeof(WeightedSelector), "CRun")]
    public static class BlockWeightedSelector
    {
        private static void Prefix(WeightedSelector __instance) =>
            SteerSafe(__instance, "weighted", () => __instance._weightedRandomizer, () =>
            {
                var weights = __instance._weights?.components;
                if (weights == null)
                    return null;
                var wants = weights.Select(w => w != null && w.key != null && w.value > 0 ? FightRecorder.Want(w.key) : -1).ToArray();
                int pick = MovePicker.Pick(wants);
                if (pick < 0)
                    return null;
                Logged("weighted selector", weights[pick].key, wants[pick]);
                return new WeightedRandomizer<Behaviour>(new[] { (weights[pick].key, 1f) });
            }, value => __instance._weightedRandomizer = (WeightedRandomizer<Behaviour>)value);
    }

    [HarmonyPatch(typeof(RandomBehaviour), "CRun")]
    public static class BlockRandomBehaviour
    {
        private static void Prefix(RandomBehaviour __instance)
        {
            var list = __instance._behaviours;
            if (list == null)
                return;
            SteerSafe(__instance, "random", () => list._components, () =>
            {
                // While steered, the list holds only the last pick: choose from the game's own list.
                var infos = Originals.TryGetValue(__instance, out var own) ? (BehaviourInfo[])own : list._components;
                if (infos == null)
                    return null;
                var wants = infos.Select(b => b != null ? FightRecorder.Want(b) : -1).ToArray();
                int pick = MovePicker.Pick(wants);
                if (pick < 0)
                    return null;
                Logged("random", infos[pick], wants[pick]);
                return new[] { infos[pick] };
            }, value => list._components = (BehaviourInfo[])value);
        }
    }

    [HarmonyPatch(typeof(Chance), "CRun")]
    public static class BlockChance
    {
        private static void Prefix(Chance __instance) =>
            SteerSafe(__instance, "chance", () => __instance._successChance,
                () =>
                {
                    int want = __instance._behaviour != null ? FightRecorder.Want(__instance._behaviour) : 0;
                    if (want <= 0)
                        return null;
                    Logged("chance", __instance._behaviour, want);
                    return 1f;
                },
                value => __instance._successChance = (float)value);
    }
}
#endif
