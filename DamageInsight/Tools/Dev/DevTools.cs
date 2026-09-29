using HarmonyLib;
using Scenes;
using Services;
using Singletons;

namespace DamageInsight.Tools;

/// <summary>
/// Testing helpers built on the game's own developer tools:
/// - F2 (the game's own key) opens Skul's hidden developer menu: spawn any gear, currencies,
///   awaken skull, damage/cooldown/HP buffs, map list, time scale, infinite revive.
/// - F7 (configurable) teleports into the developers' test map.
/// Both only work while [Developer] DevTools is enabled.
/// </summary>
public static class DevTools
{
    /// <summary>The game only allows its developer menu when its "cheats enabled" platform flag is set.</summary>
    [HarmonyPatch(typeof(global::UI.TestingTool.Panel), nameof(global::UI.TestingTool.Panel.canUse), MethodType.Getter)]
    public static class UnlockDeveloperMenu
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.DevToolsEnabled.Value)
                __result = true;
        }
    }

    /// <summary>Teleports the player into the developers' test map (the same as the menu's "test map" button).</summary>
    public static void EnterTestMap()
    {
        var levelManager = Singleton<Service>.Instance?.levelManager;
        var panel = Scene<GameBase>.instance?.uiManager?.testingTool;
        if (levelManager?.player == null || panel == null || panel._testMapPrefab == null)
        {
            Plugin.Log.LogWarning("Dev room: not available here (need to be in a run).");
            return;
        }
        Plugin.Log.LogInfo($"Dev room: entering test map '{panel._testMapPrefab.name}'");
        levelManager.EnterOutTrack(panel._testMapPrefab);
    }
}
