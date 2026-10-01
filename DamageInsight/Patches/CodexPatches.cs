using System;
using Characters.Gear;
using Characters.Gear.Synergy.Inscriptions;
using DamageInsight.Codex;
using HarmonyLib;
using UI.GearPopup;

namespace DamageInsight.Patches;

/// <summary>
/// Codex progress: gear seen (its popup opened) and picked up (equipped), inscriptions collected/completed.
/// Enemy hits and kills come from DamageRecordPatch. None of this may ever break the game.
/// </summary>
public static class CodexPatches
{
    [HarmonyPatch(typeof(GearPopup), "SetBasic")] // every Set(...) overload goes through here
    public static class GearSeen
    {
        private static void Postfix(Gear gear) => Safe(() => CodexTracker.OnGearSeen(gear));
    }

    [HarmonyPatch(typeof(Gear), "OnEquipped")]
    public static class GearEquipped
    {
        private static void Postfix(Gear __instance) => Safe(() =>
        {
            Recording.OwnerNames.MarkDirty();
            CodexTracker.OnGearEquipped(__instance);
        });
    }

    [HarmonyPatch(typeof(Characters.Gear.Upgrades.UpgradeObject), nameof(Characters.Gear.Upgrades.UpgradeObject.Attach))]
    public static class DarkAbilityTaken
    {
        private static void Postfix(Characters.Gear.Upgrades.UpgradeObject __instance) => Safe(() =>
        {
            Recording.OwnerNames.MarkDirty();
            CodexTracker.OnDarkAbilityAttached(__instance);
        });
    }

    [HarmonyPatch(typeof(Level.EnemyWaveContainer), nameof(Level.EnemyWaveContainer.Initialize))]
    public static class MapEnemies
    {
        private static void Postfix(Level.EnemyWaveContainer __instance) => Safe(() => RunRecorder.OnMapStarted(__instance));
    }

    [HarmonyPatch(typeof(Inscription), nameof(Inscription.Update))]
    public static class InscriptionUpdated
    {
        private static void Postfix(Inscription __instance) => Safe(() =>
        {
            Recording.OwnerNames.MarkDirty();
            CodexTracker.OnInscriptionUpdated(__instance);
        });
    }

    private static bool _warned;

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            if (_warned)
                return;
            _warned = true;
            Plugin.Log.LogWarning($"Codex tracking error (only logged once): {e}");
        }
    }
}
