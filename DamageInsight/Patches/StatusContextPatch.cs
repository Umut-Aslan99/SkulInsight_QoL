using Characters.Abilities.Statuses;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// The game labels all status damage as MotionType.Status. Each status effect deals its damage
/// from its own GiveDamage method, so we mark "which status is dealing damage right now"
/// for the duration of that call. DamageSources.Classify reads the mark.
/// </summary>
public static class StatusContextPatch
{
    [HarmonyPatch(typeof(Poison), "GiveDamage")]
    public static class PoisonPatch
    {
        private static void Prefix() => DamageSources.CurrentStatus = DamageSource.Poison;
        private static void Finalizer() => DamageSources.CurrentStatus = null;
    }

    [HarmonyPatch(typeof(Burn), "GiveDamage")]
    public static class BurnPatch
    {
        private static void Prefix() => DamageSources.CurrentStatus = DamageSource.Burn;
        private static void Finalizer() => DamageSources.CurrentStatus = null;
    }

    [HarmonyPatch(typeof(Wound), "GiveDamage")]
    public static class BleedPatch
    {
        private static void Prefix() => DamageSources.CurrentStatus = DamageSource.Bleed;
        private static void Finalizer() => DamageSources.CurrentStatus = null;
    }

    [HarmonyPatch(typeof(Discharge), "GiveDamage")]
    public static class ShockPatch
    {
        private static void Prefix() => DamageSources.CurrentStatus = DamageSource.Shock;
        private static void Finalizer() => DamageSources.CurrentStatus = null;
    }

    [HarmonyPatch(typeof(Ember), "GiveDamage")]
    public static class EmberPatch
    {
        private static void Prefix() => DamageSources.CurrentStatus = DamageSource.Ember;
        private static void Finalizer() => DamageSources.CurrentStatus = null;
    }
}
