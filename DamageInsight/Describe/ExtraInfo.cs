using Characters.Gear;
using Characters.Gear.Items;
using DamageInsight.Lang;
using DamageInsight.Patches;
using HarmonyLib;
using Scenes;
using UI.GearPopup;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DamageInsight.Describe;

/// <summary>
/// Hold to show (player feedback, Umut 2026-10-11): with Descriptions/HoldToShow on, the mod's numbers in the game's own
/// description boxes (inventory, items on the ground and in shops, the swap menu) only show while Descriptions/HoldKey
/// is held, like the game's own "hold for details"; otherwise the box says which key shows them. The boxes refill the
/// moment the key goes down or up. The Codex always shows its numbers (it is the mod's own book).
/// </summary>
public static class ExtraInfo
{
    /// <summary>Numbers on whatever the key does (the QA capture tool).</summary>
    public static bool Forced;

    private static bool _shownBefore = true;

    /// <summary>
    /// Whether the game's boxes show the mod's numbers right now: the hold key, or the game's own "details" keys (the
    /// inventory's detail view, the ground popup's detail view): asking the game for details shows ours too, so no
    /// two keys at once (Umut 2026-10-12: "hold E and Shift feels awkward").
    /// </summary>
    public static bool Shown => Forced || !(Plugin.DescriptionsHoldToShow?.Value ?? false) || KeyHeld() || GameDetailsHeld();

    private static bool GameDetailsHeld()
    {
        try
        {
            var map = UserInput.KeyMapper.Map;
            return map != null && (map.UiInteraction3.IsPressed || map.Down.Value > 0.5f);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>The hold key is down (also used by the Codex: Shift shows your run's numbers there).</summary>
    public static bool HoldKeyDown() => KeyHeld();

    private static bool KeyHeld()
    {
        var shortcut = Plugin.DescriptionsHoldKey.Value;
        if (shortcut.IsPressed())
            return true;
        // Either Shift (Ctrl, Alt) does when the key is the left one.
        return shortcut.MainKey switch
        {
            KeyCode.LeftShift => Input.GetKey(KeyCode.RightShift),
            KeyCode.LeftControl => Input.GetKey(KeyCode.RightControl),
            KeyCode.LeftAlt => Input.GetKey(KeyCode.RightAlt),
            _ => false,
        };
    }

    /// <summary>The key's name for the hint ("Shift" for either Shift key).</summary>
    public static string KeyName() => Plugin.DescriptionsHoldKey.Value.MainKey switch
    {
        KeyCode.LeftShift or KeyCode.RightShift => "Shift",
        KeyCode.LeftControl or KeyCode.RightControl => "Ctrl",
        KeyCode.LeftAlt or KeyCode.RightAlt => "Alt",
        var key => key.ToString(),
    };

    /// <summary>"Hold Shift for damage numbers" under a gear's main description while the numbers are hidden.</summary>
    public static string Hint(Gear gear)
    {
        if (DescriptionPatches.Plain || Shown || !Plugin.DescriptionNumbers.Value || gear == null || !GearDescriptions.HasNumbers(gear))
            return "";
        return $"\n\n<color={DescriptionFormatter.DimColor}>{Loc.F("Hold {0} for damage numbers", KeyName())}</color>";
    }

    /// <summary>The same hint for a skill, swap or inscription text that has numbers to show (the detail views).</summary>
    public static string HintIf(bool hasNumbers)
    {
        if (!hasNumbers || DescriptionPatches.Plain || Shown || !Plugin.DescriptionNumbers.Value)
            return "";
        return $"\n\n<color={DescriptionFormatter.DimColor}>{Loc.F("Hold {0} for damage numbers", KeyName())}</color>";
    }

    /// <summary>Called every frame: refills the open boxes when the numbers appear or disappear.</summary>
    public static void Tick()
    {
        bool shown = Shown;
        if (shown == _shownBefore)
            return;
        _shownBefore = shown;
        Guard.Run("Hold to show: refresh", Refresh);
    }

    /// <summary>Fills the open description boxes again (their texts are read once when they open).</summary>
    public static void Refresh()
    {
        var ui = Scene<GameBase>.instance?.uiManager;
        if (ui == null)
            return;

        var inventory = ui.inventory;
        if (inventory != null && inventory.gameObject.activeInHierarchy)
        {
            inventory.UpdateGearInfo(); // the inscription list (its texts carry numbers too)
            var selected = EventSystem.current?.currentSelectedGameObject;
            selected?.GetComponent<global::UI.Inventory.GearElement>()?.onSelected?.Invoke();
        }

        var popup = ui.gearPopupCanvas?.gearPopup;
        if (popup != null && popup.gameObject.activeInHierarchy && PopupGear.Last != null)
        {
            // Set(gear) also writes the interaction label; keep the one shown (other callers set their own after it).
            string label = popup._interactionDescription != null ? popup._interactionDescription.text : null;
            bool guide = popup._interactionGuide != null && popup._interactionGuide.activeSelf;
            popup.Set(PopupGear.Last);
            if (label != null)
                popup._interactionDescription.text = label;
            if (popup._interactionGuide != null)
                popup._interactionGuide.SetActive(guide);
        }

        var select = ui.itemSelect;
        if (select != null && select.gameObject.activeInHierarchy && select._fieldItem != null)
        {
            select._fieldGearPopup.Set(select._fieldItem);
            if (SwapMenuSelection.Last >= 0)
                select.OnItemSelected(SwapMenuSelection.Last);
        }
    }

    /// <summary>The gear the ground / shop popup shows, to fill it again.</summary>
    [HarmonyPatch(typeof(GearPopup), nameof(GearPopup.Set), typeof(Gear))]
    public static class PopupGear
    {
        public static Gear Last;

        private static void Postfix(Gear gear) => Last = gear;
    }

    /// <summary>The inventory item selected in the swap menu (full inventory), to fill it again.</summary>
    [HarmonyPatch(typeof(ItemSelect), "OnItemSelected")]
    public static class SwapMenuSelection
    {
        public static int Last = -1;

        private static void Postfix(int index) => Last = index;
    }
}
