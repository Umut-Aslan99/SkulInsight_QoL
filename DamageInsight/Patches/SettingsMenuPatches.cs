using DamageInsight.UI;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>Adds the "SkulInsight QoL" button (our settings page) to the game's pause menu.</summary>
[HarmonyPatch(typeof(global::UI.Pause.Menu), "Awake")]
public static class PauseMenuButtonPatch
{
    private static void Postfix(global::UI.Pause.Menu __instance) =>
        Guard.Run("Pause menu button", () => SettingsPage.AttachTo(__instance));
}

/// <summary>
/// The pause panel calls Return for Esc / Cancel. While our settings page is in front, that means "back to the
/// menu" (or "keep the old key" during a key capture), like on the game's own Settings page.
/// </summary>
[HarmonyPatch(typeof(global::UI.Pause.Panel), nameof(global::UI.Pause.Panel.Return))]
public static class PauseReturnPatch
{
    private static bool Prefix()
    {
        bool handled = false;
        Guard.Run("Settings page (back)", () => handled = SettingsPage.HandleBack());
        return !handled;
    }
}
