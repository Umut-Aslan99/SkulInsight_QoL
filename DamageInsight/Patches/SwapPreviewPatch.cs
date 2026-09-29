using System;
using DamageInsight.Describe;
using HarmonyLib;
using UI.GearPopup;

namespace DamageInsight.Patches;

/// <summary>
/// The swap menu (inventory full, ItemSelect): the game fills the ground item's panel once when the menu opens
/// and only updates the selected inventory item's panel afterwards. When an inventory item gets selected, fill the
/// ground item's panel again, with the stats you'd have after swapping that item for it.
/// </summary>
[HarmonyPatch(typeof(ItemSelect), "OnItemSelected")]
public static class SwapPreviewPatch
{
    private static void Postfix(ItemSelect __instance, int index)
    {
        if (!Plugin.PickupPreview.Value || __instance._fieldItem == null || __instance._itemInventory == null)
            return;
        try
        {
            var replacing = index >= 0 && index < __instance._itemInventory.items.Count ? __instance._itemInventory.items[index] : null;
            PickupPreview.Begin(__instance._fieldItem, replacing);
            __instance._fieldGearPopup.Set(__instance._fieldItem);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Swap preview failed: {e.Message}");
        }
        finally
        {
            PickupPreview.End();
        }
    }
}
