using HarmonyLib;
using Platforms;

namespace DamageInsight.Patches;

/// <summary>
/// The slot's achievements (0.14): every achievement the game gives passes PlatformManager.SetAchievement, also when
/// Steam has it already (the game never asks Steam first), so the slot sees each one again and keeps it.
/// </summary>
public static class AchievementPatches
{
    [HarmonyPatch(typeof(PlatformManager), nameof(PlatformManager.SetAchievement))]
    [HarmonyPriority(Priority.First)]
    public static class SlotAchievements
    {
        private static void Prefix(Achievement.Type achievementType) =>
            Guard.Run("Achievements", () => DamageInsight.Achievements.AchievementTracker.OnSet(achievementType));
    }
}
