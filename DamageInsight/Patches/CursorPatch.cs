using DamageInsight.UI;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>The game hides the mouse cursor after a few seconds; keep it visible while the combat log is open.</summary>
[HarmonyPatch(typeof(CursorManager), "Update")]
public static class CursorPatch
{
    private static void Postfix()
    {
        if (CombatLogWindow.IsOpen)
            Cursor.visible = true;
    }
}
