using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// Small helpers for building Unity UI (uGUI) from code.
/// Positions are given from the parent's top-left corner, y pointing down, in reference pixels (1920x1080).
/// </summary>
public static class UiKit
{
    public static readonly Color TextColor = new Color32(0xF2, 0xE9, 0xD8, 0xFF);
    public static readonly Color DimTextColor = new Color32(0xA8, 0x9F, 0x94, 0xFF);
    public static readonly Color ChipOff = new Color32(0x2A, 0x22, 0x33, 0xE6);
    public static readonly Color ChipOn = new Color32(0x6B, 0x4A, 0x8A, 0xFF);

    private static TMP_FontAsset _font;

    /// <summary>
    /// Scale of our canvases (reference 1920x1080, matching width and height 50/50, like CanvasScaler).
    /// Computed directly so it is right even before the CanvasScaler has run.
    /// </summary>
    public static float CanvasScale => Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));

    /// <summary>Screen size in our canvas units.</summary>
    public static Vector2 CanvasSize => new Vector2(Screen.width, Screen.height) / CanvasScale;

    /// <summary>Creates a screen overlay canvas that scales like our reference layout.</summary>
    public static Canvas OverlayCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.layer = UiLayer;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    /// <summary>Anchors a rect to the parent's top-right and places it (x = distance from the right edge).</summary>
    public static RectTransform PlaceTopRight(this RectTransform rect, float right, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>Stretches a rect over its parent with distances from each edge.</summary>
    public static RectTransform Stretch(this RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    /// <summary>A width that fits a chip's label.</summary>
    public static float ChipWidth(string label) => Mathf.Max(64f, 22f + label.Sum(c => c >= 0x2E80 ? 16f : 8.6f));

    /// <summary>Unity's built-in "UI" layer (5), looked up by name in case the game renamed it.</summary>
    public static int UiLayer
    {
        get
        {
            int layer = LayerMask.NameToLayer("UI");
            return layer >= 0 ? layer : 5;
        }
    }

    /// <summary>The game's UI font, so our window matches Skul's look.</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            if (_font != null)
                return _font;
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _font = fonts.FirstOrDefault(f => f.name.Contains("NotoSans") && f.name.Contains("Bold"))
                ?? fonts.FirstOrDefault(f => f.name.Contains("NotoSans") && !f.name.Contains("Black"))
                ?? fonts.FirstOrDefault(f => f.name.Contains("NotoSans"))
                ?? fonts.FirstOrDefault();
            Plugin.Log.LogInfo($"UI font: {(_font != null ? _font.name : "none")} (available: {string.Join(", ", fonts.Select(f => f.name))})");
            return _font;
        }
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = UiLayer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, worldPositionStays: false);
        return rect;
    }

    /// <summary>Anchors a rect to the parent's top-left and places it at (x, y) with the given size.</summary>
    public static RectTransform Place(this RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>Stretches a rect to fill its parent, minus a margin.</summary>
    public static RectTransform Fill(this RectTransform rect, float margin = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
        return rect;
    }

    public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var rect = Rect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        }
        return image;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, string text, float size,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, Color? color = null)
    {
        var rect = Rect(name, parent);
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null)
            tmp.font = Font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = alignment;
        tmp.color = color ?? TextColor;
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    /// <summary>A small clickable toggle "chip" with a text label.</summary>
    public sealed class Chip
    {
        public readonly Image Background;
        public readonly TextMeshProUGUI Label;
        private readonly Color _onColor;

        public Chip(Transform parent, string label, float x, float y, float width, Action onClick, Color? onColor = null)
        {
            _onColor = onColor ?? ChipOn;
            Background = Image($"Chip {label}", parent, ChipOff);
            Background.rectTransform.Place(x, y, width, 28);
            var button = Background.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                onClick();
                // Don't keep the chip "selected": the game's Submit key would click it again.
                EventSystem.current?.SetSelectedGameObject(null);
            });

            Label = Text("Label", Background.transform, label, 16, TextAlignmentOptions.Center);
            Label.enableWordWrapping = false;
            Label.rectTransform.Fill();
        }

        public void SetOn(bool on)
        {
            Background.color = on ? _onColor : ChipOff;
            // Dark text on bright chips, light text on dark ones.
            bool bright = on && (_onColor.r * 0.3f + _onColor.g * 0.59f + _onColor.b * 0.11f) > 0.6f;
            Label.color = on ? (bright ? new Color32(0x1A, 0x14, 0x20, 0xFF) : TextColor) : DimTextColor;
        }
    }
}
