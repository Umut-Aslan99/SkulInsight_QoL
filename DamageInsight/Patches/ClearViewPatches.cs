using System;
using Characters.Projectiles;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Clear view (UI/ClearView): your projectiles and your summons' fade with your effects. These run for every
/// projectile in the game, so they stay small and never let an error through (no closure, no Guard).
/// </summary>
public static class ClearViewPatches
{
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Fire))]
    public static class ProjectileFired
    {
        private static void Postfix(Projectile __instance, Characters.Character owner)
        {
            using var _perf = DamageInsight.Crash.Perf.Measure("Clear view (projectiles)");
            try
            {
                DamageInsight.UI.ClearView.Fired(__instance, owner);
            }
            catch (Exception)
            {
                // never break the game's projectiles
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Despawn))]
    public static class ProjectileDespawned
    {
        private static void Prefix(Projectile __instance)
        {
            using var _perf = DamageInsight.Crash.Perf.Measure("Clear view (projectiles)");
            try
            {
                DamageInsight.UI.ClearView.Despawned(__instance);
            }
            catch (Exception)
            {
                // never break the game's projectiles
            }
        }
    }
}
