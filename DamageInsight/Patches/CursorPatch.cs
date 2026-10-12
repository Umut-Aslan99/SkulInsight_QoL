using DamageInsight.UI;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>The game hides the mouse cursor after a few seconds; keep it visible while the combat log, the Codex or the What's new window is open.</summary>
[HarmonyPatch(typeof(CursorManager), "Update")]
public static class CursorPatch
{
    private static void Postfix()
    {
        if (CombatLogWindow.IsOpen || DamageInsight.Codex.CodexWindow.IsOpen || WhatsNew.IsOpen)
            Cursor.visible = true;
    }
}
