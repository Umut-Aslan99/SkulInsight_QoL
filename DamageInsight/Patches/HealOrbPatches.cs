using System;
using HarmonyLib;
using Level;

namespace DamageInsight.Patches;

/// <summary>Heal orbs (History/HealOrbs): every orb the game drops, and which ones are taken.</summary>
public static class HealOrbPatches
{
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DropPotion), typeof(Potion), typeof(UnityEngine.Vector3))]
    public static class OrbDropped
    {
        private static void Postfix(Potion __result)
        {
            using var _perf = DamageInsight.Crash.Perf.Measure("Heal orbs");
            try
            {
                DamageInsight.History.HealOrbs.Dropped(__result);
            }
            catch (Exception e)
            {
                Guard.Report("Heal orbs (dropped)", e);
            }
        }
    }

    [HarmonyPatch(typeof(Potion), nameof(Potion.InteractWith))]
    public static class OrbTouched
    {
        private static void Prefix(Potion __instance, Characters.Character character)
        {
            using var _perf = DamageInsight.Crash.Perf.Measure("Heal orbs");
            try
            {
                DamageInsight.History.HealOrbs.Touched(__instance, character);
            }
            catch (Exception e)
            {
                Guard.Report("Heal orbs (taken)", e);
            }
        }
    }
}
