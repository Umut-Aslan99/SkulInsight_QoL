using Characters;
using Characters.Abilities;
using DamageInsight.UI;
using HarmonyLib;
using TMPro;
using UI.Hud;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>
/// WoW-style cooldown numbers on the HUD's ability icons (items, inscriptions, buffs at the bottom of the
/// screen). AbilityIconDisplay.Update fills its icons in order from the abilities that have an icon; after it
/// ran, walk the same order and put the remaining seconds on each icon.
/// </summary>
[HarmonyPatch(typeof(AbilityIconDisplay), "Update")]
public static class CooldownTickerPatch
{
    private const string LabelName = "DamageInsight Cooldown";

    private static bool _failed;

    private static void Postfix(AbilityIcon[] ____icons, Character ____character)
    {
        if (_failed || ____icons == null || ____character == null)
            return;
        try
        {
            Update(____icons, ____character);
        }
        catch (System.Exception e)
        {
            // Runs every frame: switch off after the first error instead of spamming the log.
            _failed = true;
            Plugin.Log.LogWarning($"Cooldown ticker switched off after an error: {e}");
        }
    }

    private static void Update(AbilityIcon[] ____icons, Character ____character)
    {
        bool enabled = Plugin.CooldownTicker.Value;
        int index = 0;
        foreach (var icon in ____icons)
        {
            if (icon == null || !icon.gameObject.activeSelf)
                continue;
            IAbilityInstance instance = NextWithIcon(____character, ref index);
            string text = enabled && instance != null ? CooldownFormat.Format(CooldownReader.Remaining(instance)) : "";
            var label = LabelOf(icon, create: text.Length > 0);
            if (label != null && label.text != text)
                label.text = text;
        }
    }

    // Same order as AbilityIconDisplay.FindNextAbilityWithIcon.
    private static IAbilityInstance NextWithIcon(Character character, ref int index)
    {
        while (index < character.ability.Count)
        {
            var instance = character.ability[index++];
            if (instance.icon != null)
                return instance;
        }
        return null;
    }

    private static TextMeshProUGUI LabelOf(AbilityIcon icon, bool create)
    {
        var existing = icon.transform.Find(LabelName);
        if (existing != null)
            return existing.GetComponent<TextMeshProUGUI>();
        if (!create)
            return null;

        var go = new GameObject(LabelName, typeof(RectTransform));
        go.transform.SetParent(icon.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<TextMeshProUGUI>();
        if (icon._stackText != null)
            text.font = icon._stackText.font;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 4;
        text.fontSizeMax = 40;
        text.margin = new Vector4(1, 1, 1, 1);
        text.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Plugin.CooldownTickerOpacity.Value));
        text.outlineWidth = 0.25f;
        text.outlineColor = new Color32(0, 0, 0, 255);
        return text;
    }
}
