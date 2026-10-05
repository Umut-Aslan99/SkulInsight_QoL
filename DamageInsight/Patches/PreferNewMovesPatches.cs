#if DEV // a developer tool: release builds carry none of these hooks
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Characters.AI.Behaviours;
using DamageInsight.Codex;
using HarmonyLib;
using BDOneByOneSelector = BehaviorDesigner.Runtime.Tasks.OneByOneSelector;
using BDSelector = BehaviorDesigner.Runtime.Tasks.Selector;
using BDWeightedSelector = BehaviorDesigner.Runtime.Tasks.WeightedSelector;
using TaskStatus = BehaviorDesigner.Runtime.Tasks.TaskStatus;

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
/// <item>BehaviorDesigner trees are led by <see cref="MoveDirector"/> when it has a next move: Selectors start at, and
/// WeightedSelectors and OneByOneSelectors take, the child on the way; gating checks on it return what it needs.</item>
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
                // The move director's way first; else steered towards missing moves.
                bool directed = MoveDirector.ChildFor(__instance, out int pick) && pick < children.Count && pick < weights.Count;
                var wants = new int[children.Count];
                if (!directed)
                {
                    for (int i = 0; i < wants.Length; i++)
                        wants[i] = i == __state || i >= weights.Count || (weights[i] <= 0f && i != current) ? -1 : FightRecorder.Want(children[i]);
                    pick = MovePicker.Pick(wants);
                }
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
                if (!directed)
                    Logged("tree selector", children[pick], wants[pick]);
            });
        }
    }

    /// <summary>The move director: a tree Selector starting afresh begins at the child on the way (skips the others).</summary>
    [HarmonyPatch(typeof(BDSelector), nameof(BDSelector.CurrentChildIndex))]
    public static class TreeSelector
    {
        private static void Prefix(BDSelector __instance)
        {
            if (!MovePicker.On)
                return;
            try
            {
                if (__instance.currentChildIndex == 0 && __instance.executionStatus == TaskStatus.Inactive &&
                    MoveDirector.ChildFor(__instance, out int child) && child > 0 && child < __instance.children.Count)
                    __instance.currentChildIndex = child;
            }
            catch (Exception)
            {
                // keep the game's order
            }
        }
    }

    /// <summary>The move director: a OneByOneSelector (Dark Skul's speed form) takes the child on the way.</summary>
    [HarmonyPatch(typeof(BDOneByOneSelector), "OnStart")]
    public static class TreeOneByOne
    {
        private static void Postfix(BDOneByOneSelector __instance)
        {
            if (!MovePicker.On)
                return;
            try
            {
                if (MoveDirector.ChildFor(__instance, out int child) && child < __instance.children.Count)
                    __instance._currentChildIndex = child;
            }
            catch (Exception)
            {
                // keep the game's pick
            }
        }
    }

    /// <summary>
    /// The move director: checks on the way to the next move that only gate it (distance, wall, HP, cooldown, chance;
    /// <see cref="MoveDirector.ForcedTypes"/>) return what keeps the tree on the way.
    /// </summary>
    [HarmonyPatch]
    public static class TreeChecks
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            MoveDirector.ForcedTypes.Select(t => AccessTools.DeclaredMethod(t, "OnUpdate"));

        private static void Postfix(object __instance, ref TaskStatus __result)
        {
            try
            {
                if (MovePicker.On && MoveDirector.Forced(__instance, out bool pass))
                    __result = pass ? TaskStatus.Success : TaskStatus.Failure;
            }
            catch (Exception)
            {
                // keep the game's result
            }
        }
    }

    [HarmonyPatch(typeof(WeightedSelector), "CRun")]
    public static class BlockWeightedSelector
    {
        private static void Prefix(WeightedSelector __instance) =>
            SteerSafe(__instance, "weighted", () => __instance._weightedRandomizer, () =>
            {
                MoveDirector.Ran(__instance);
                var weights = __instance._weights?.components;
                if (weights == null)
                    return null;
                if (MoveDirector.ChildFor(__instance, out int child) && child < weights.Length && weights[child]?.key != null)
                    return new WeightedRandomizer<Behaviour>(new[] { (weights[child].key, 1f) });
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
                MoveDirector.Ran(__instance);
                if (MoveDirector.ChildFor(__instance, out int child) && child < infos.Length && infos[child] != null)
                    return new[] { infos[child] };
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
                    if (MoveDirector.Forced(__instance, out bool pass))
                        return pass ? 1f : 0f;
                    int want = __instance._behaviour != null ? FightRecorder.Want(__instance._behaviour) : 0;
                    if (want <= 0)
                        return null;
                    Logged("chance", __instance._behaviour, want);
                    return 1f;
                },
                value => __instance._successChance = (float)value);
    }

    /// <summary>The move director, blocks: a Selector starts at the child on the way (the ones before it are skipped).</summary>
    [HarmonyPatch(typeof(Selector), "CRun")]
    public static class BlockSelector
    {
        private static void Prefix(Selector __instance)
        {
            var list = __instance._children;
            if (list == null)
                return;
            SteerSafe(__instance, "selector", () => list._components, () =>
            {
                var infos = Originals.TryGetValue(__instance, out var own) ? (BehaviourInfo[])own : list._components;
                MoveDirector.Ran(__instance);
                return infos != null && MoveDirector.ChildFor(__instance, out int child) && child > 0 && child < infos.Length
                    ? infos.Skip(child).ToArray()
                    : null;
            }, value => list._components = (BehaviourInfo[])value);
        }
    }

    /// <summary>The move director, blocks: a UniformSelector draws the child on the way (its bag refills as usual).</summary>
    [HarmonyPatch(typeof(UniformSelector), "CRun")]
    public static class BlockUniformSelector
    {
        private static void Prefix(UniformSelector __instance)
        {
            if (!MovePicker.On)
                return;
            Guard.Run("Prefer new moves (uniform)", () =>
            {
                MoveDirector.Ran(__instance);
                var weights = __instance._weights?.components;
                if (weights != null && MoveDirector.ChildFor(__instance, out int child) && child < weights.Length && weights[child]?.key != null)
                {
                    __instance._container.Clear();
                    __instance._container.Add(weights[child].key);
                }
            });
        }
    }

    /// <summary>The move director, blocks: a CoolTime or Count gate on the way lets its block run now.</summary>
    [HarmonyPatch]
    public static class BlockGates
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            new[] { AccessTools.Method(typeof(CoolTime), "CRun"), AccessTools.Method(typeof(Count), "CRun") };

        private static void Prefix(Behaviour __instance)
        {
            if (!MovePicker.On)
                return;
            Guard.Run("Prefer new moves (gate)", () =>
            {
                if (!MoveDirector.Forced(__instance, out bool pass) || !pass)
                    return;
                if (__instance is CoolTime coolTime)
                    coolTime._canRun = true;
                else if (__instance is Count count)
                {
                    count._max = Math.Max(count._max, 1);
                    count._current = Math.Min(count._current, count._max - 1);
                }
            });
        }
    }

    /// <summary>The move director, blocks: a Conditional's check on the way (HP, cooldown, distance) passes.</summary>
    [HarmonyPatch(typeof(Characters.AI.Conditions.Condition), nameof(Characters.AI.Conditions.Condition.IsSatisfied))]
    public static class BlockConditions
    {
        private static void Postfix(Characters.AI.Conditions.Condition __instance, ref bool __result)
        {
            try
            {
                if (MovePicker.On && MoveDirector.Forced(__instance, out bool pass))
                    __result = pass;
            }
            catch (Exception)
            {
                // keep the game's result
            }
        }
    }

    /// <summary>The move director, blocks: which part of its tree a boss is in (its blocks that started lately).</summary>
    [HarmonyPatch(typeof(BehaviourInfo), nameof(BehaviourInfo.CRun))]
    public static class BlockStarted
    {
        private static void Prefix(BehaviourInfo __instance)
        {
            try
            {
                if (MovePicker.On)
                    MoveDirector.Ran(__instance);
            }
            catch (Exception)
            {
                // only bookkeeping
            }
        }
    }
}
#endif
