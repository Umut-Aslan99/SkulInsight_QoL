using TMPro;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// The seconds label of the cooldown ticker: a centred, auto-sized TMP text stretched over a HUD icon (ability
/// icons, skills, quintessence, swap). Created on first use and found again by name; opacity follows the setting.
/// </summary>
public static class CooldownLabel
{
    private const string Name = "DamageInsight Cooldown";

    /// <summary>Shows text on the icon. An empty text clears an existing label and never creates one.</summary>
    public static void Set(Transform icon, string text, TMP_FontAsset font)
    {
        var existing = icon.Find(Name);
        var label = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (label == null)
        {
            if (text.Length == 0)
                return;
            label = Create(icon, font);
        }
        if (label.text != text)
            label.text = text;
        float alpha = Mathf.Clamp01(Plugin.CooldownTickerOpacity.Value);
        if (!Mathf.Approximately(label.color.a, alpha))
            label.color = new Color(1f, 1f, 1f, alpha);
    }

    private static TextMeshProUGUI Create(Transform icon, TMP_FontAsset font)
    {
        var go = new GameObject(Name, typeof(RectTransform));
        go.transform.SetParent(icon, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
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
