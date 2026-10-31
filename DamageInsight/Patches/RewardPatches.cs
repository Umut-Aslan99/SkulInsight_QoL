using System;
using Characters.Gear;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Rewards (0.15, Rewards/RewardService): every gear listens to its own discard event (Gear.onDiscard, which the
/// game fires from Item / Weapon OnDestroy while the gear still says where it lay), so a legendary item or skull
/// destroyed on the ground (dismantled, or left behind when the map is left) gives its coin.
/// Not a patch on Item.OnDestroy (first try): preparing that method made the game build Item's destroy effect
/// before its resources were loaded, and from then on every item destroyed threw (Umut's log, 2026-10-26).
/// </summary>
public static class RewardPatches
{
    [HarmonyPatch(typeof(Gear), "Awake")]
    public static class GearAwake
    {
        private static void Postfix(Gear __instance)
        {
            using var _perf = DamageInsight.Crash.Perf.Measure("Rewards (gear)");
            try
            {
                __instance.onDiscard -= DamageInsight.Rewards.RewardService.OnGearDiscarded; // never twice
                __instance.onDiscard += DamageInsight.Rewards.RewardService.OnGearDiscarded;
            }
            catch (Exception e)
            {
                Guard.Report("Rewards (gear)", e);
            }
        }
    }
}
