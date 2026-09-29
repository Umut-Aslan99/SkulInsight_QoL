using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Abilities;
using Characters.Abilities.Darks;
using HarmonyLib;

namespace DamageInsight.Tools;

/// <summary>
/// Temporary diagnostics for the "dark elites stack every dark ability" bug report.
/// Logs "[DarkDiag] ..." lines to BepInEx\LogOutput.log: every elite creation, every ability roll,
/// and dark abilities being added to a character. When one character gets suspiciously many,
/// it logs the call stack once, so we can see which code keeps adding them. Only observes; changes nothing.
/// </summary>
public static class DarkEliteDiagnostics
{
    private const string Tag = "[DarkDiag]";
    private const int SuspiciousCount = 4;

    private static readonly Dictionary<int, int> ProvideCount = new();
    private static readonly Dictionary<int, int> DarkAbilityAdds = new();
    private static readonly HashSet<int> StackLogged = new();

    private static bool Enabled => Plugin.DarkEliteDiagnostics?.Value ?? false;

    private static string Describe(Character c) =>
        c == null ? "null" : $"{c.name.Replace("(Clone)", "")}#{c.GetInstanceID()} ({c.type})";

    [HarmonyPatch(typeof(DarkAbilityConstructor), nameof(DarkAbilityConstructor.Provide))]
    public static class ProvidePatch
    {
        private static void Postfix(Character target, bool __result)
        {
            if (!Enabled || target == null)
                return;
            int id = target.GetInstanceID();
            ProvideCount[id] = ProvideCount.TryGetValue(id, out int n) ? n + 1 : 1;
            Plugin.Log.LogInfo($"{Tag} Elite created: {Describe(target)} (time #{ProvideCount[id]}, ok={__result})");
            if (ProvideCount[id] > 1)
                Plugin.Log.LogWarning($"{Tag} Same character made elite again!\n{Environment.StackTrace}");
        }
    }

    [HarmonyPatch(typeof(DarkAbilityAttacher), nameof(DarkAbilityAttacher.Attach))]
    public static class AttachPatch
    {
        private static void Postfix(DarkAbilityAttacher __instance)
        {
            if (!Enabled)
                return;
            var names = __instance._abilities?.Select(a => a != null ? a.name : "null") ?? Enumerable.Empty<string>();
            Plugin.Log.LogInfo($"{Tag} Abilities rolled for {Describe(__instance._owner)}: [{string.Join(", ", names)}]");
        }
    }

    [HarmonyPatch(typeof(CharacterAbilityManager), nameof(CharacterAbilityManager.Add))]
    public static class AddPatch
    {
        private static void Prefix(CharacterAbilityManager __instance, IAbility ability)
        {
            if (!Enabled || ability == null || __instance._character == null)
                return;
            // Only dark-enemy abilities (and anything added to an elite) are interesting here.
            string ns = ability.GetType().Namespace ?? "";
            var owner = __instance._character;
            if (!ns.StartsWith("Characters.Abilities.Darks") && owner.type != Character.Type.Named)
                return;

            int id = owner.GetInstanceID();
            int count = DarkAbilityAdds[id] = DarkAbilityAdds.TryGetValue(id, out int n) ? n + 1 : 1;
            Plugin.Log.LogInfo($"{Tag} +{ability.GetType().Name} -> {Describe(owner)} (#{count})");
            if (count > SuspiciousCount && StackLogged.Add(id))
                Plugin.Log.LogWarning($"{Tag} {Describe(owner)} got {count} abilities. Call stack:\n{Environment.StackTrace}");
        }
    }
}
