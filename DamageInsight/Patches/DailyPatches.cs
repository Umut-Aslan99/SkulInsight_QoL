using DamageInsight.Daily;
using HarmonyLib;
using Platforms;

namespace DamageInsight.Patches;

/// <summary>
/// The daily slot's rules (0.13, docs/DAILY_RUN.md): its evened-out profile has everything unlocked, so Steam
/// achievements are not sent from it (it would hand them out); DLC gear is left out of its gear pools, so owners and
/// non-owners of a DLC play the same run. A daily run can't be continued (DailyRun.RefuseContinue).
/// </summary>
public static class DailyPatches
{
    [HarmonyPatch(typeof(PlatformManager), nameof(PlatformManager.SetAchievement))]
    public static class NoAchievementsInTheDailySlot
    {
        private static bool _said;

        private static bool Prefix(Achievement.Type achievementType)
        {
            try
            {
                if (!DailyRun.InDailySlot)
                    return true;
                if (!_said)
                {
                    _said = true;
                    Plugin.Log.LogInfo($"Daily run: Steam achievements are not sent from the daily slot ({achievementType} and later ones)");
                }
                return false;
            }
            catch (System.Exception e)
            {
                Guard.Report("Daily run (achievements)", e);
                return true;
            }
        }
    }

    /// <summary>Before the game loads a saved run (the title screen's play, a slot switch): a daily run in progress ends
    /// instead (closing the game mid-map would otherwise heal the player and replay the map).</summary>
    [HarmonyPatch(typeof(Level.LevelManager), nameof(Level.LevelManager.LoadGame))]
    public static class NoContinuingADailyRun
    {
        private static void Prefix() => Guard.Run("Daily run (no continuing)", DailyRun.RefuseContinue);
    }

    [HarmonyPatch(typeof(GameResources.GearReference), nameof(GameResources.GearReference.dlcPurchased), MethodType.Getter)]
    public static class NoDlcGearInTheDailySlot
    {
        private static void Postfix(GameResources.GearReference __instance, ref bool __result)
        {
            try
            {
                if (__result && DailyRun.InDailySlot && __instance.dlcTitle != Platforms.Dlc.DlcTitle.None)
                    __result = false;
            }
            catch (System.Exception e)
            {
                Guard.Report("Daily run (DLC gear)", e);
            }
        }
    }
}
