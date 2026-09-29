using HarmonyLib;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// A small icon drawn to the left of a damage number. Damage numbers are pooled and reused
/// for other texts (heals, buffs...), so the icon is hidden again whenever a FloatingText
/// is re-initialized.
/// </summary>
[HarmonyPatch(typeof(FloatingText))]
public static class PopupIcon
{
    private const string ChildName = "DamageInsight_Icon";
    private const float Gap = 0.08f;

    /// <summary>Shows <paramref name="sprite"/> left of the text. Returns the renderer (for fading).</summary>
    public static SpriteRenderer Show(FloatingText floatingText, Sprite sprite)
    {
        var text = floatingText._text;
        Transform parent = text.transform;
        Transform child = parent.Find(ChildName);
        SpriteRenderer renderer;
        if (child == null)
        {
            var go = new GameObject(ChildName);
            go.layer = text.gameObject.layer;
            go.transform.SetParent(parent, worldPositionStays: false);
            renderer = go.AddComponent<SpriteRenderer>();
        }
        else
        {
            renderer = child.GetComponent<SpriteRenderer>();
        }

        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingLayerID = text.sortingLayerID;
        renderer.sortingOrder = text.sortingOrder;

        // Measure the digits themselves (in the text's local space), not the whole line box,
        // and make the icon exactly as tall as they are, just left of them.
        text.ForceMeshUpdate();
        float top = float.MinValue, bottom = float.MaxValue, left = float.MaxValue;
        var info = text.textInfo;
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible)
                continue;
            top = Mathf.Max(top, c.topLeft.y);
            bottom = Mathf.Min(bottom, c.bottomLeft.y);
            left = Mathf.Min(left, c.bottomLeft.x);
        }
        if (left == float.MaxValue)
        {
            Bounds b = text.textBounds;
            top = b.max.y;
            bottom = b.min.y;
            left = b.min.x;
        }

        float height = Mathf.Max(top - bottom, 0.05f);
        float scale = height / Mathf.Max(sprite.bounds.size.y, 0.001f);
        renderer.transform.localScale = new Vector3(scale, scale, 1f);
        float iconWidth = sprite.bounds.size.x * scale;
        renderer.transform.localPosition = new Vector3(left - Gap - iconWidth / 2f, (top + bottom) / 2f, 0f);

        renderer.gameObject.SetActive(true);
        return renderer;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(FloatingText.Initialize), typeof(string), typeof(Vector3))]
    private static void HideOnReuse(FloatingText __instance)
    {
        var child = __instance._text != null ? __instance._text.transform.Find(ChildName) : null;
        if (child != null)
            child.gameObject.SetActive(false);
    }
}
