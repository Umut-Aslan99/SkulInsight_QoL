using Characters.Gear.Items;
using DamageInsight.Describe;
using HarmonyLib;
using Services;
using Singletons;
using UI.GearPopup;

namespace DamageInsight.Patches;

/// <summary>
/// The popup of an item on the ground (GearPopup.Set(Item)) fills its description and inscription details in one
/// go. While it does, descriptions use the stats the player would have after picking the item up.
/// </summary>
[HarmonyPatch(typeof(GearPopup), nameof(GearPopup.Set), typeof(Item))]
public static class PickupPreviewPatch
{
    private static void Prefix(Item item)
    {
        var items = Singleton<Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.item?.items;
        if (item != null && items != null && !items.Contains(item))
            PickupPreview.Begin(item);
    }

    private static void Finalizer() => PickupPreview.End();
}
