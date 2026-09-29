using System;
using Characters;
using DamageInsight.Recording;
using HarmonyLib;
using Level;

namespace DamageInsight.Patches;

/// <summary>Feeds the DamageLog: every hit taken by any character, and every room change.</summary>
public static class DamageRecordPatch
{
    // Patch the version with the out parameter: the other TakeDamage overload calls it.
    [HarmonyPatch(typeof(Health), nameof(Health.TakeDamage),
        new[] { typeof(Damage), typeof(double) }, new[] { ArgumentType.Ref, ArgumentType.Out })]
    public static class TakeDamagePatch
    {
        private static void Postfix(Health __instance, bool __result, ref Damage damage, ref double dealtDamage)
        {
            // __result == true means the hit was cancelled (evaded, invulnerable, ...).
            if (__result || dealtDamage <= 0.0 || __instance.owner == null)
                return;
            try
            {
                DamageLog.Add(__instance.owner, damage, dealtDamage);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not record damage: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.InvokeOnMapChanged))]
    public static class MapChangedPatch
    {
        private static void Postfix() => DamageLog.StartRoom();
    }
}
