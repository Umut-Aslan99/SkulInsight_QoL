using Characters;
using Characters.Projectiles;
using DamageInsight.Recording;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Names projectile hits after what fired them (e.g. "Wind Spirit Sylphid" instead of "Sylphids wing"):
/// while a FireProjectile operation runs, its owner (gear, dark ability, inscription) is remembered, and every
/// projectile fired in that moment is linked to it.
/// </summary>
public static class ProjectileOriginPatch
{
    [HarmonyPatch(typeof(Characters.Operations.Attack.FireProjectile), nameof(Characters.Operations.Attack.FireProjectile.Run), typeof(Character))]
    public static class Firing
    {
        private static void Prefix(Characters.Operations.Attack.FireProjectile __instance) =>
            Guard.Run("Projectile origin (fire)", () =>
            {
                if (Plugin.CombatLogCalculations.Value)
                    OwnerNames.BeginFiring(__instance);
            });

        private static void Finalizer() => OwnerNames.EndFiring();
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Fire))]
    public static class Fired
    {
        private static void Postfix(Projectile __instance) =>
            Guard.Run("Projectile origin (link)", () => OwnerNames.LinkProjectile(__instance));
    }
}
