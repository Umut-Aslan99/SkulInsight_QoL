using Characters.Gear;
using Characters.Gear.Items;
using Characters.Gear.Quintessences;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Weapons;
using DamageInsight.Describe;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Appends real damage numbers to the game's description texts. The game builds every description
/// through these getters (inventory, drop popup, shop, details view), so patching them covers all screens.
/// </summary>
public static class DescriptionPatches
{
    [HarmonyPatch(typeof(Gear), nameof(Gear.description), MethodType.Getter)]
    public static class GearDescription
    {
        private static void Postfix(Gear __instance, ref string __result)
        {
            switch (__instance)
            {
                case Weapon weapon:
                    __result += GearDescriptions.ForWeapon(weapon);
                    break;
                case Item item:
                    __result += GearDescriptions.ForItem(item);
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(Weapon), nameof(Weapon.activeDescription), MethodType.Getter)]
    public static class SwapDescription
    {
        private static void Postfix(Weapon __instance, ref string __result) =>
            __result += GearDescriptions.ForSwap(__instance);
    }

    [HarmonyPatch(typeof(Quintessence), nameof(Quintessence.activeDescription), MethodType.Getter)]
    public static class QuintessenceDescription
    {
        private static void Postfix(Quintessence __instance, ref string __result) =>
            __result += GearDescriptions.ForQuintessence(__instance);
    }

    [HarmonyPatch(typeof(Inscription), nameof(Inscription.GetDescription), typeof(int))]
    public static class InscriptionDescription
    {
        private static void Postfix(Inscription __instance, int step, ref string __result) =>
            __result += GearDescriptions.ForInscription(__instance, step);
    }

    /// <summary>
    /// The "Tuned ..." box the witch offers after the First Hero in the Dark Mirror (DCDefense.SuperInscriptionBox)
    /// uses the static overload: show the numbers of the player's own inscription there too.
    /// </summary>
    [HarmonyPatch(typeof(Inscription), nameof(Inscription.GetSuperDescription), typeof(Inscription.Key))]
    public static class TunedBoxDescription
    {
        private static void Postfix(Inscription.Key key, ref string __result)
        {
            var inscriptions = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy?.inscriptions;
            if (inscriptions != null)
                __result += GearDescriptions.ForInscriptionSuper(inscriptions[key]);
        }
    }

    [HarmonyPatch(typeof(Inscription), nameof(Inscription.GetSuperDescription), new System.Type[0])] // the instance overload, not the static (Key) one
    public static class InscriptionSuperDescription
    {
        private static void Postfix(Inscription __instance, ref string __result) =>
            __result += GearDescriptions.ForInscriptionSuper(__instance);
    }

    [HarmonyPatch(typeof(SkillInfo), nameof(SkillInfo.description), MethodType.Getter)]
    public static class SkillDescription
    {
        private static void Postfix(SkillInfo __instance, ref string __result) =>
            __result += GearDescriptions.ForSkill(__instance);
    }
}
