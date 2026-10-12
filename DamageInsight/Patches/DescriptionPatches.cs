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
    /// <summary>True while the Codex reads the game's own text (it adds its numbers itself): no numbers appended.</summary>
    public static bool Plain;

    /// <summary>The game's text without our numbers.</summary>
    public static string PlainText(System.Func<string> read)
    {
        bool before = Plain;
        Plain = true;
        try
        {
            return read();
        }
        finally
        {
            Plain = before;
        }
    }

    [HarmonyPatch(typeof(Gear), nameof(Gear.description), MethodType.Getter)]
    public static class GearDescription
    {
        private static void Postfix(Gear __instance, ref string __result) =>
            __result += Guard.Text("Gear description numbers", () => __instance switch
            {
                Weapon weapon => GearDescriptions.ForWeapon(weapon),
                Item item => GearDescriptions.ForItem(item),
                _ => "",
            }) + Guard.Text("Hold to show hint", () => ExtraInfo.Hint(__instance));
    }

    [HarmonyPatch(typeof(Weapon), nameof(Weapon.activeDescription), MethodType.Getter)]
    public static class SwapDescription
    {
        private static void Postfix(Weapon __instance, ref string __result) =>
            __result += Guard.Text("Swap description numbers", () => GearDescriptions.ForSwap(__instance))
                + Guard.Text("Swap hint", () => ExtraInfo.HintIf(GearDescriptions.HasSwapNumbers(__instance)));
    }

    [HarmonyPatch(typeof(Quintessence), nameof(Quintessence.activeDescription), MethodType.Getter)]
    public static class QuintessenceDescription
    {
        private static void Postfix(Quintessence __instance, ref string __result) =>
            __result += Guard.Text("Essence description numbers", () => GearDescriptions.ForQuintessence(__instance));
    }

    [HarmonyPatch(typeof(Inscription), nameof(Inscription.GetDescription), typeof(int))]
    public static class InscriptionDescription
    {
        private static void Postfix(Inscription __instance, int step, ref string __result) =>
            __result += Plain ? "" : Guard.Text("Inscription description numbers", () => GearDescriptions.ForInscription(__instance, step))
                + Guard.Text("Inscription hint", () => ExtraInfo.HintIf(GearDescriptions.FirstNumberStep(__instance) == step));
    }

    /// <summary>
    /// The "Tuned ..." box the witch offers after the First Hero in the Dark Mirror (DCDefense.SuperInscriptionBox)
    /// uses the static overload: show the numbers of the player's own inscription there too.
    /// </summary>
    [HarmonyPatch(typeof(Inscription), nameof(Inscription.GetSuperDescription), typeof(Inscription.Key))]
    public static class TunedBoxDescription
    {
        private static void Postfix(Inscription.Key key, ref string __result) =>
            __result += Guard.Text("Tuned box numbers", () =>
            {
                var inscriptions = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy?.inscriptions;
                return inscriptions != null ? GearDescriptions.ForInscriptionSuper(inscriptions[key]) : "";
            });
    }

    [HarmonyPatch(typeof(Inscription), nameof(Inscription.GetSuperDescription), new System.Type[0])] // the instance overload, not the static (Key) one
    public static class InscriptionSuperDescription
    {
        private static void Postfix(Inscription __instance, ref string __result) =>
            __result += Plain ? "" : Guard.Text("Tuned description numbers", () => GearDescriptions.ForInscriptionSuper(__instance));
    }

    [HarmonyPatch(typeof(SkillInfo), nameof(SkillInfo.description), MethodType.Getter)]
    public static class SkillDescription
    {
        private static void Postfix(SkillInfo __instance, ref string __result) =>
            __result += Guard.Text("Skill description numbers", () => GearDescriptions.ForSkill(__instance))
                + Guard.Text("Skill hint", () => ExtraInfo.HintIf(GearDescriptions.HasSkillNumbers(__instance)));
    }
}
