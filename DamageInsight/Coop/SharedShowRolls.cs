using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Things every game rolls by itself only for show, made the same for everyone in a co-op session: an adventurer's
/// name and level on its health bar (`AdventurerNamePool`, `AdventurerLevelSetter`: the first paired test pictures
/// showed "Sura-Kler, level 6" on the host and "Hion-Sergir, level 3" on the client for the same archer; players who
/// talk to each other would name different enemies). The roll is drawn from a seed of the room and the bar's place
/// in the UI, and Unity's random is put back afterwards. Outside co-op: the game's own roll.
/// </summary>
[HarmonyPatch]
internal static class SharedShowRolls
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(global::UI.Adventurer.AdventurerNamePool), "OnEnable");
        yield return AccessTools.Method(typeof(global::UI.Adventurer.AdventurerLevelSetter), "OnEnable");
    }

    private static void Prefix(Component __instance, out Random.State? __state)
    {
        __state = null;
        if (CoopWorld.Current?.Active != true)
            return;
        uint room = CoopWorld.CurrentRoom();
        if (room == 0)
            return;
        // the same for both rolls of one bar (name and level differ by the type), different per bar (hero, archer ...)
        uint hash = 2166136261 ^ room;
        hash *= 16777619;
        int depth = 0;
        for (var t = __instance.transform; t != null && depth < 4; t = t.parent, depth++)
            foreach (char c in t.name)
            {
                hash ^= c;
                hash *= 16777619;
            }
        foreach (char c in __instance.GetType().Name)
        {
            hash ^= c;
            hash *= 16777619;
        }
        __state = Random.state;
        Random.InitState((int)hash);
    }

    private static void Postfix(Random.State? __state)
    {
        if (__state.HasValue)
            Random.state = __state.Value;
    }
}
