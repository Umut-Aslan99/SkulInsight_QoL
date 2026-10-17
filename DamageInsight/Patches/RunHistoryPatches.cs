using Characters.Gear.Items;
using Characters.Gear.Quintessences;
using Characters.Player;
using DamageInsight.History;
using HarmonyLib;
using UI;

namespace DamageInsight.Patches;

/// <summary>
/// The run history's hooks (0.13): the end screen (a death or an ending closes the run) and the game's own reports of
/// an item or quintessence turning into another (so the timeline says "turned into" instead of "lost" + "taken").
/// </summary>
public static class RunHistoryPatches
{
    [HarmonyPatch(typeof(GameResult), nameof(GameResult.Show))]
    public static class DeathResultPatch
    {
        private static void Postfix() => Guard.Run("Run history: end screen", () => RunJournal.OnGameResult(ending: false));
    }

    [HarmonyPatch(typeof(GameResult), nameof(GameResult.ShowEndingResult))]
    public static class EndingResultPatch
    {
        private static void Postfix() => Guard.Run("Run history: ending screen", () => RunJournal.OnGameResult(ending: true));
    }

    /// <summary>The end screen's picture was just rendered (1.6 s after a death; the ending's last scene).</summary>
    [HarmonyPatch(typeof(PlayerCameraController), nameof(PlayerCameraController.RenderDeathCamera))]
    public static class PhotoPatch
    {
        private static void Postfix() => Guard.Run("Run history: picture", RunJournal.OnPhoto);
    }

    [HarmonyPatch(typeof(ItemInventory), nameof(ItemInventory.Change))]
    public static class ItemChangePatch
    {
        private static void Postfix(Item old, Item @new) => Guard.Run("Run history: item change", () => RunJournal.OnGearChanged(old, @new));
    }

    [HarmonyPatch(typeof(ItemInventory), nameof(ItemInventory.ChangeAt))]
    public static class ItemChangeAtPatch
    {
        private static void Prefix(ItemInventory __instance, int index, out Item __state)
        {
            __state = null;
            try
            {
                __state = index >= 0 && index < __instance.items.Count ? __instance.items[index] : null;
            }
            catch (System.Exception e)
            {
                Guard.Report("Run history: item change", e);
            }
        }

        private static void Postfix(Item @new, Item __state) => Guard.Run("Run history: item change", () => RunJournal.OnGearChanged(__state, @new));
    }

    [HarmonyPatch(typeof(QuintessenceInventory), nameof(QuintessenceInventory.Change))]
    public static class EssenceChangePatch
    {
        private static void Postfix(Quintessence old, Quintessence @new) => Guard.Run("Run history: quintessence change", () => RunJournal.OnGearChanged(old, @new));
    }

    [HarmonyPatch(typeof(QuintessenceInventory), nameof(QuintessenceInventory.ChangeAt))]
    public static class EssenceChangeAtPatch
    {
        private static void Prefix(QuintessenceInventory __instance, int index, out Quintessence __state)
        {
            __state = null;
            try
            {
                __state = index >= 0 && index < __instance.items.Count ? __instance.items[index] : null;
            }
            catch (System.Exception e)
            {
                Guard.Report("Run history: quintessence change", e);
            }
        }

        private static void Postfix(Quintessence @new, Quintessence __state) => Guard.Run("Run history: quintessence change", () => RunJournal.OnGearChanged(__state, @new));
    }
}
