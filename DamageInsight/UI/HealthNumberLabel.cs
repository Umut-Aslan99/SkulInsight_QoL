using Characters;
using TMPro;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// A text label we add on top of a game health bar. Shows "current / max" HP,
/// plus shield if there is any. Our first piece of custom UI.
/// </summary>
/// <remarks>
/// The bar's _container is only an invisible anchor (it can be 1920x0), so we can't just fill it.
/// Instead, every frame we measure where the health fill bar would be at 100% and center
/// the text on that, with a font size based on the bar's height.
/// </remarks>
public class HealthNumberLabel : MonoBehaviour
{
    private const float FallbackBarHeight = 24f;
    private const float MinFontSize = 14f;
    private const float MaxFontSize = 34f;

    private CharacterHealthBar _bar;
    private RectTransform _rect;
    private TextMeshProUGUI _text;
    private double _lastCurrent = -1, _lastMax = -1, _lastShield = -1;
    private bool _loggedGeometry;

    /// <summary>Creates the label as a child of the bar's container.</summary>
    public static HealthNumberLabel Attach(CharacterHealthBar bar, TMP_FontAsset font)
    {
        if (bar == null || bar._container == null || bar._healthBar == null
            || bar.GetComponentInChildren<HealthNumberLabel>(true) != null)
            return null;

        var go = new GameObject("DamageInsight_HealthNumbers", typeof(RectTransform));
        go.layer = bar._container.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(bar._container, worldPositionStays: false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.SetAsLastSibling(); // draw on top of the bar graphics

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.color = Color.white;
        // The outline lives in the font material, which a bar created during a cutscene may not have yet.
        if (text.font != null && text.fontSharedMaterial != null)
        {
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(0, 0, 0, 255);
        }

        var label = go.AddComponent<HealthNumberLabel>();
        label._bar = bar;
        label._rect = rect;
        label._text = text;
        return label;
    }

    private void LateUpdate()
    {
        bool enabled = Plugin.BossHealthNumbers.Value;
        if (_text.enabled != enabled)
            _text.enabled = enabled;
        if (!enabled)
            return;

        PlaceOverBar();
        UpdateText();
    }

    /// <summary>Centers the label on the health fill bar as it looks at 100% HP.</summary>
    private void PlaceOverBar()
    {
        RectTransform fill = _bar._healthBar;
        Vector3 fullScale = _bar._defaultHealthScale;
        if (fullScale.x == 0f)
            return; // bar not set up yet

        // Size of the fill bar at 100%, in world units (= screen pixels on an overlay canvas).
        Vector3 parentScale = fill.parent != null ? fill.parent.lossyScale : Vector3.one;
        float width = fill.rect.width * Mathf.Abs(fullScale.x) * parentScale.x;
        float height = fill.rect.height * Mathf.Abs(fullScale.y) * parentScale.y;

        // fill.position is its pivot point; walk from there to the center of the full bar.
        Vector3 center = fill.position + new Vector3(
            (0.5f - fill.pivot.x) * width,
            (0.5f - fill.pivot.y) * height,
            0f);
        _rect.position = center;

        Vector3 ownScale = _rect.lossyScale;
        float localHeight = height > 1f ? height / ownScale.y : FallbackBarHeight;
        _rect.sizeDelta = new Vector2(Mathf.Max(width / ownScale.x, 200f), localHeight);
        _text.fontSize = Mathf.Clamp(localHeight * 0.85f, MinFontSize, MaxFontSize);

        if (!_loggedGeometry)
        {
            _loggedGeometry = true;
            Plugin.Log.LogInfo(
                $"HP label on '{_bar.name}': fill rect={fill.rect.size} pivot={fill.pivot} fullScale={fullScale} " +
                $"-> bar {width:0}x{height:0} at {center}, font {_text.fontSize:0}");
        }
    }

    private void UpdateText()
    {
        var health = _bar._health;
        if (health == null)
        {
            _text.text = "";
            return;
        }

        double current = health.dead ? 0 : health.currentHealth;
        double max = health.maximumHealth;
        double shield = health.shield?.amount ?? 0;

        // Only rebuild the string when a value changed.
        if (current == _lastCurrent && max == _lastMax && shield == _lastShield)
            return;
        _lastCurrent = current;
        _lastMax = max;
        _lastShield = shield;

        string text = $"{System.Math.Ceiling(current):N0} / {System.Math.Ceiling(max):N0}";
        if (shield >= 1)
            text += $"  <color=#BFE9FF>(+{System.Math.Ceiling(shield):N0})</color>";
        _text.text = text;
    }
}
