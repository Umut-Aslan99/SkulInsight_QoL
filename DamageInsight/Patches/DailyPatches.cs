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

    /// <summary>
    /// The game's map events that check for the DLC (Runnables.Triggers.CheckDLC): on a board without DLC they see it as
    /// not owned, so an owner plays the same run as everyone else.
    /// </summary>
    [HarmonyPatch(typeof(Runnables.Triggers.CheckDLC), "Check")]
    public static class NoDlcEventsInTheDailySlot
    {
        private static bool Prefix(Runnables.Triggers.CheckDLC __instance, ref bool __result)
        {
            try
            {
                if (!DailyRun.InDailySlot || DailyRun.DlcBoard)
                    return true;
                __result = __instance._condition == false; // as if the DLC weren't owned
                return false;
            }
            catch (System.Exception e)
            {
                Guard.Report("Daily run (DLC events)", e);
                return true;
            }
        }
    }

    /// <summary>The game drew a new run seed (a castle loads): in the daily slot the day's seed goes back at once.</summary>
    [HarmonyPatch(typeof(Data.GameData.Save), nameof(Data.GameData.Save.ResetAll))]
    public static class DaySeedAtTheCastle
    {
        private static void Postfix(Data.GameData.Save __instance) => Guard.Run("Daily run (seed)", () => DailyRun.KeepSeed(__instance));
    }

    /// <summary>The Holy Grail's blessing: from the day's seed in a daily run (the game uses its shared random).</summary>
    [HarmonyPatch(typeof(CutScenes.Shots.Events.Customs.ApplyBlessing), nameof(CutScenes.Shots.Events.Customs.ApplyBlessing.Run))]
    public static class DayBlessing
    {
        private static bool Prefix(CutScenes.Shots.Events.Customs.ApplyBlessing __instance)
        {
            try
            {
                return !DailyRun.SeededBlessing(__instance);
            }
            catch (System.Exception e)
            {
                Guard.Report("Daily run (blessing)", e);
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
                // Only the "+dlc" boards have DLC gear (and only their owners play them).
                if (__result && DailyRun.InDailySlot && !DailyRun.DlcBoard && __instance.dlcTitle != Platforms.Dlc.DlcTitle.None)
                    __result = false;
            }
            catch (System.Exception e)
            {
                Guard.Report("Daily run (DLC gear)", e);
            }
        }
    }
}
