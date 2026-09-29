using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DamageInsight.Recording;
using Scenes;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// A few lines of the combat log right above the minimap, with a tiny pie chart at the right.
/// Uses the big log's filters. No background; older lines are fainter; hides itself when no
/// new damage came in for a few seconds.
/// </summary>
public sealed class MiniLog : MonoBehaviour
{
    private const float LineHeight = 19f;
    private const float PieSize = 40f;
    private const float FadeInSeconds = 0.2f;
    private const float FadeOutSeconds = 0.6f;
    private const float FallbackWidth = 314f;

    private Canvas _canvas;
    private RectTransform _root;
    private CanvasGroup _group;
    private TextMeshProUGUI _text;
    private RawImage _pieImage;
    private PieChart _pie;

    private readonly List<(DamageRecord record, float arrived)> _lines = new();
    private int _cursor;
    private int _rebuildsSeen = -1;
    private float _lastNew = -999f;
    private bool _dirty;
    private float _nextPlacement, _nextPie;
    private int _pieVersion = -1;
    private bool _loggedPlacement;

    private void Update()
    {
        if (!ShouldShow())
        {
            if (_canvas != null && _canvas.enabled)
                _canvas.enabled = false;
            return;
        }
        if (_canvas == null)
            Build();
        _canvas.enabled = true;

        float now = Time.unscaledTime;
        CollectNewLines(now);

        float idle = now - _lastNew;
        float idleLimit = Mathf.Max(0.5f, Plugin.MiniLogIdleSeconds.Value);
        _group.alpha = idle < idleLimit ? 1f : Mathf.Clamp01(1f - (idle - idleLimit) / FadeOutSeconds);
        if (_group.alpha <= 0f)
            return;

        if (now >= _nextPlacement)
        {
            _nextPlacement = now + 0.5f;
            Place();
        }
        if (_dirty || _lines.Any(l => now - l.arrived < FadeInSeconds))
        {
            _dirty = false;
            DrawLines(now);
        }
        if (Plugin.MiniLogPie.Value && CombatLogWindow.Feed.Version != _pieVersion && now >= _nextPie)
        {
            _nextPie = now + 0.5f;
            _pieVersion = CombatLogWindow.Feed.Version;
            DrawPie();
        }
    }

    private static bool ShouldShow()
    {
        if (!Plugin.MiniLogEnabled.Value || CombatLogWindow.IsOpen)
            return false;
        if (Singleton<Service>.Instance?.levelManager?.player == null)
            return false;
        var ui = Scene<GameBase>.instance?.uiManager;
        return ui != null && ui.hideOption != global::UI.UIManager.HideOption.HideAll;
    }

    private void CollectNewLines(float now)
    {
        CombatLogWindow.UpdateFeed();
        var feed = CombatLogWindow.Feed;
        if (feed.Rebuilds != _rebuildsSeen)
        {
            // Filters changed or the log was cleared: start fresh instead of replaying old hits.
            _rebuildsSeen = feed.Rebuilds;
            _cursor = feed.Shown.Count;
            _lines.Clear();
            _dirty = true;
        }

        int maxLines = Mathf.Clamp(Plugin.MiniLogLines.Value, 1, 8);
        for (; _cursor < feed.Shown.Count; _cursor++)
        {
            _lines.Add((feed.Shown[_cursor], now));
            _lastNew = now;
            _dirty = true;
        }
        if (_lines.Count > maxLines)
            _lines.RemoveRange(0, _lines.Count - maxLines);
    }

    private void DrawLines(float now)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _lines.Count; i++)
        {
            int fromNewest = _lines.Count - 1 - i;
            // The further up (older), the fainter.
            float alpha = Mathf.Max(0.3f, 1f - fromNewest * 0.2f);
            // New lines fade in quickly.
            alpha *= Mathf.Clamp01((now - _lines[i].arrived) / FadeInSeconds);
            if (i > 0)
                sb.Append('\n');
            sb.Append(LogText.CompactLine(_lines[i].record, alpha));
        }
        _text.text = sb.ToString();
    }

    private void DrawPie()
    {
        var slices = DamageStats.FromTotals(CombatLogWindow.Feed.TotalBySource)
            .Select(s => (s.Value, DamageSources.Color(s.Key))).ToList();
        _pieImage.enabled = slices.Count > 0;
        _pie.Draw(slices);
    }

    // ---------------------------------------------------------------- building and placement

    private void Build()
    {
        _canvas = UiKit.OverlayCanvas("DamageInsight_MiniLog", 4000);
        _root = UiKit.Rect("MiniLog", _canvas.transform);
        _root.anchorMin = _root.anchorMax = _root.pivot = Vector2.zero;
        _group = _root.gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        _text = UiKit.Text("Lines", _root, "", 14, TextAlignmentOptions.BottomLeft);
        _text.enableWordWrapping = false;
        _text.overflowMode = TextOverflowModes.Ellipsis;
        _text.outlineWidth = 0.22f;
        _text.outlineColor = new Color32(0, 0, 0, 220);

        _pie = new PieChart(64);
        var pieRect = UiKit.Rect("Pie", _root);
        pieRect.anchorMin = pieRect.anchorMax = pieRect.pivot = new Vector2(1f, 0f);
        pieRect.anchoredPosition = Vector2.zero;
        pieRect.sizeDelta = new Vector2(PieSize, PieSize);
        _pieImage = pieRect.gameObject.AddComponent<RawImage>();
        _pieImage.texture = _pie.Texture;
        _pieImage.raycastTarget = false;
        _pieImage.enabled = false;

        Place();
    }

    /// <summary>Puts the mini log right above the minimap, as wide as the minimap.</summary>
    private void Place()
    {
        int lines = Mathf.Clamp(Plugin.MiniLogLines.Value, 1, 8);
        float height = lines * LineHeight + 4f;
        Vector2 screen = UiKit.CanvasSize;

        float left, bottom, width;
        if (TryGetMinimapRect(out Rect map))
        {
            left = map.xMin;
            width = map.width;
            bottom = map.yMax + Plugin.MiniLogOffsetY.Value;
        }
        else
        {
            width = FallbackWidth;
            left = screen.x - width - 18f;
            bottom = 190f + Plugin.MiniLogOffsetY.Value;
        }

        _root.anchoredPosition = new Vector2(left, bottom);
        _root.sizeDelta = new Vector2(width, height);
        bool pie = Plugin.MiniLogPie.Value;
        _pieImage.gameObject.SetActive(pie);
        _text.rectTransform.Stretch(0f, 0f, pie ? PieSize + 8f : 0f, 0f);

        if (!_loggedPlacement)
        {
            _loggedPlacement = true;
            Plugin.Log.LogInfo($"Mini log placed at ({left:0},{bottom:0}) size {width:0}x{height:0} (canvas {screen.x:0}x{screen.y:0})");
        }
    }

    /// <summary>The minimap's rectangle in our canvas units (origin bottom-left), if we can find it.</summary>
    private static bool TryGetMinimapRect(out Rect rect)
    {
        rect = default;
        try
        {
            var hud = Scene<GameBase>.instance?.uiManager?.headupDisplay;
            var group = hud != null ? hud._rightBottomWithMinimap : null;
            if (group == null || !group.activeInHierarchy)
                return false;

            // The minimap is drawn from a camera into a RawImage; take the largest one.
            RectTransform map = group.GetComponentsInChildren<RawImage>(false)
                .Select(r => r.rectTransform)
                .OrderByDescending(r => Area(r))
                .FirstOrDefault();
            if (map == null)
                return false;

            var corners = new Vector3[4];
            map.GetWorldCorners(corners);
            var canvas = map.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]) / UiKit.CanvasScale;
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]) / UiKit.CanvasScale;
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return rect.width > 20f && rect.height > 20f;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Mini log: could not find the minimap: {e.Message}");
            return false;
        }
    }

    private static float Area(RectTransform r) => r.rect.width * r.rect.height * r.lossyScale.x * r.lossyScale.y;
}
