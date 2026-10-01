using Characters;
using DamageInsight.UI;
using HarmonyLib;
using TMPro;
using UI;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>
/// Adds a HealthNumberLabel to the big health bars at the top of the screen:
/// chapter bosses, veteran adventurers, dark (elite) enemies and adventurer parties.
/// </summary>
/// <remarks>
/// We hook CharacterHealthBar.Initialize, which runs whenever a bar is pointed at a character.
/// That covers every big-bar panel, no matter when its UI is created. Small bars over
/// regular enemies are skipped because they don't sit under one of those panels.
/// </remarks>
[HarmonyPatch(typeof(CharacterHealthBar))]
public static class BossHealthNumbersPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CharacterHealthBar.Initialize))]
    private static void AfterInitialize(CharacterHealthBar __instance, Character character)
    {
        Guard.Run("Boss HP numbers", () => Attach(__instance));
        // A boss bar opens when the fight starts: begin filming now, not only at the player's first hit.
        Guard.Run("Codex film start", () =>
        {
            if (DamageInsight.Codex.BossAttacks.Covers(character) && DamageInsight.Codex.CodexTracker.EntryOf(character) is { } entry)
                DamageInsight.Codex.FightRecorder.Watch(character, entry.key);
        });
    }

    private static void Attach(CharacterHealthBar __instance)
    {
        if (__instance._container == null || __instance.GetComponentInChildren<HealthNumberLabel>(true) != null)
            return;

        Component panel = FindBigBarPanel(__instance);
        if (panel == null)
            return;

        // Borrow the game's own font from the panel (e.g. the boss name), so we match its style.
        var existingText = panel.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = existingText != null ? existingText.font : null;

        HealthNumberLabel.Attach(__instance, font);
        if (Plugin.CodexMoveCounter != null)
            MoveCounterLabel.Attach(__instance, font);
        Plugin.Log.LogInfo($"Added HP numbers to '{__instance.name}' ({panel.GetType().Name})");
    }

    private static Component FindBigBarPanel(CharacterHealthBar bar)
    {
        // includeInactive: true, because bars are often initialized before their panel is shown.
        var boss = bar.GetComponentsInParent<BossHealthbarController>(true);
        if (boss.Length > 0)
            return boss[0];
        var veteran = bar.GetComponentsInParent<VeteranHealthbarController>(true);
        if (veteran.Length > 0)
            return veteran[0];
        var dark = bar.GetComponentsInParent<DarkEnemyHealthbarController>(true);
        if (dark.Length > 0)
            return dark[0];
        var adventurers = bar.GetComponentsInParent<AdventurerHealthBarUIController>(true);
        if (adventurers.Length > 0)
            return adventurers[0];
        return null;
    }
}
