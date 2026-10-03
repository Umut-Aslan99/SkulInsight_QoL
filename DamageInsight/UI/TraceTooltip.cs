using System.Collections.Generic;
using DamageInsight.Recording;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DamageInsight.Lang;

namespace DamageInsight.UI;

/// <summary>
/// The panel that explains one hit of the combat log: a header (amount, crit, what dealt it) and one row per
/// step of the calculation, with the icon of the item/inscription/dark ability behind it, its factor and the
/// running total.
/// </summary>
public sealed class TraceTooltip
{
    private const float Width = 520f, IconSize = 22f;

    private readonly RectTransform _root;
    private readonly RectTransform _canvasRect;
    private readonly TextMeshProUGUI _header;
    private readonly List<(GameObject go, Image icon, TextMeshProUGUI text)> _rows = new();

    public bool Visible => _root.gameObject.activeSelf;

    public TraceTooltip(Canvas canvas)
    {
        _canvasRect = (RectTransform)canvas.transform;
        var background = UiKit.Image("Calculation", canvas.transform, new Color32(0x14, 0x0D, 0x1C, 0xF5));
        _root = background.rectTransform;
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 3;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = background.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _header = UiKit.Text("Header", _root, "", 17);
        _header.gameObject.AddComponent<LayoutElement>().preferredWidth = Width;
        _root.gameObject.SetActive(false);
    }

    public void Show(in DamageRecord record, Vector2 screenPosition)
    {
        var trace = record.Trace;
        string crit = record.Critical ? $" <color=#FFE14D>{Loc.T("CRIT")}</color>" : "";
        string origin = string.IsNullOrEmpty(trace?.Origin) ? "" : "  " + Loc.F("<color=#A89F94>by</color> {0}", Loc.Name(trace.Origin));
        _header.text = $"<b>{record.Amount:N0}</b> <color={DamageSources.AttributeColorHex(record.Attribute)}>{DamageInsight.Describe.DescriptionFormatter.AttributeName(record.Attribute.ToString())}</color>{crit}{origin}" +
                       $"\n<size=80%><color=#A89F94>{record.Attacker} → {record.Target}</color></size>";

        var rows = trace?.Explain() ?? new List<TraceRow> { new(Loc.T("No calculation recorded for this hit"), "", record.Amount) };
        while (_rows.Count < rows.Count)
            _rows.Add(NewRow());
        for (int i = 0; i < _rows.Count; i++)
        {
            var (go, icon, text) = _rows[i];
            go.SetActive(i < rows.Count);
            if (i >= rows.Count)
                continue;
            var row = rows[i];
            var sprite = row.Icon as Sprite;
            icon.sprite = sprite;
            icon.color = sprite != null ? Color.white : new Color(0, 0, 0, 0);
            string effect = row.Effect.Length == 0 ? "" : $"<color=#FFD27A>{row.Effect}</color>";
            // Intermediate totals keep one decimal: the game only rounds (up) at the end.
            text.text = $"{row.Label}<pos=66%>{effect}<pos=80%><color=#A89F94>= {row.Total.ToString("#,0.#", System.Globalization.CultureInfo.InvariantCulture)}</color>";
        }

        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
        Place(screenPosition);
    }

    public void Hide() => _root.gameObject.SetActive(false);

    /// <summary>Next to the mouse, flipped to the left/top when it would leave the screen.</summary>
    private void Place(Vector2 screenPosition)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition, null, out var local);
        Vector2 half = _canvasRect.rect.size / 2f;
        Vector2 size = _root.rect.size;
        bool left = local.x + 24 + size.x > half.x;
        bool up = local.y - 16 - size.y < -half.y;
        _root.pivot = new Vector2(left ? 1f : 0f, up ? 0f : 1f);
        _root.anchoredPosition = local + new Vector2(left ? -16 : 24, up ? 16 : -16);
    }

    private (GameObject, Image, TextMeshProUGUI) NewRow()
    {
        var row = UiKit.Rect("Row", _root);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var icon = UiKit.Image("Icon", row, Color.white);
        icon.preserveAspect = true;
        var iconLayout = icon.gameObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = iconLayout.preferredHeight = IconSize;
        iconLayout.minWidth = iconLayout.minHeight = IconSize;

        var text = UiKit.Text("Text", row, "", 15);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.preferredWidth = Width - IconSize - 8;
        return (row.gameObject, icon, text);
    }
}
