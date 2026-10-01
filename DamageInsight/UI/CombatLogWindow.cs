using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Characters;
using DamageInsight.Recording;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// The combat log: a window (toggled with L) listing every recorded hit, with filters and a pie chart
/// of the damage shares. Movable by its header, resizable by its corners, updated live.
/// Built from code the first time it is opened.
/// </summary>
public sealed class CombatLogWindow : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    /// <summary>The filter and filtered view, shared with the mini log.</summary>
    public static readonly LogFilter Filter = new();
    public static readonly LogFeed Feed = new(Filter);
    private static int _feedFrame = -1;

    /// <summary>Brings the shared feed up to date (at most once per frame, whoever asks first).</summary>
    public static void UpdateFeed()
    {
        if (_feedFrame == Time.frameCount)
            return;
        _feedFrame = Time.frameCount;
        Feed.Update(DamageLog.Records, DamageLog.TotalAdded, DamageLog.ClearCount, DamageLog.CurrentRoom);
    }

    private enum ChartMode { Source, Type }

    private ChartMode _chartMode = ChartMode.Source;
    private int _minDamageIndex;

    private const int MaxLines = 250;
    private const float TextInterval = 0.1f;  // log text refresh (10x per second)
    private const float ChartInterval = 0.3f; // pie chart refresh
    private const float DefaultWidth = 920f, DefaultHeight = 720f;

    private Canvas _canvas;
    private WindowFrame _frame;
    private RectTransform _panel;
    private ScrollRect _scroll;
    private TextMeshProUGUI _logText;
    private TextMeshProUGUI _legendText;
    private TextMeshProUGUI _pieCenterText;
    private RectTransform _logFrame, _chartFrame, _pieRect, _legendRect;
    private PieChart _pie;
    private readonly List<Action> _chipRefreshers = new();
    private readonly List<ChipGroup> _groups = new();
    private int _textVersion = -1, _chartVersion = -1;
    private ChartMode _chartModeShown;
    private float _nextText, _nextChart;

    // Calculation tooltip: which text line shows which hit, the hovered line and a pinned (clicked) hit.
    private TraceTooltip _tooltip;
    private readonly List<int> _lineMap = new();
    private int _hoverLine = -1;
    private DamageRecord? _pinned;

    private sealed class ChipGroup
    {
        public TextMeshProUGUI Label;
        public readonly List<(UiKit.Chip chip, float width)> Chips = new();
    }

    private void Update()
    {
        if (!DamageInsight.Codex.CodexWindow.IsTyping && (Plugin.CombatLogKey.Value.IsDown() || (IsOpen && Input.GetKeyDown(KeyCode.Escape))))
            SetOpen(!IsOpen);
        if (!IsOpen)
            return;

        UpdateFeed();
        UpdateTooltip();
        float now = Time.unscaledTime;
        if (Feed.Version != _textVersion && now >= _nextText)
        {
            _nextText = now + TextInterval;
            _textVersion = Feed.Version;
            DrawLog();
        }
        if ((Feed.Version != _chartVersion || _chartMode != _chartModeShown) && now >= _nextChart)
        {
            _nextChart = now + ChartInterval;
            _chartVersion = Feed.Version;
            _chartModeShown = _chartMode;
            DrawChart();
        }
    }

    private void SetOpen(bool open)
    {
        if (open && _canvas == null)
        {
            try
            {
                Build();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not build the combat log window: {e}");
                return;
            }
        }
        IsOpen = open;
        _canvas.gameObject.SetActive(open);
        if (open)
        {
            EnsureEventSystem();
            RefreshChips();
            _textVersion = _chartVersion = -1;
            _nextText = _nextChart = 0f;
        }
    }

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _canvas = UiKit.OverlayCanvas("DamageInsight_CombatLog", 5000);
        _canvas.gameObject.AddComponent<GraphicRaycaster>();

        var panel = UiKit.Image("Panel", _canvas.transform, new Color32(0x1B, 0x12, 0x24, 0xEB));
        _panel = panel.rectTransform;
        ApplyBackground(panel);

        Vector2 screen = UiKit.CanvasSize;
        if (!WindowFrame.TryParse(Plugin.CombatLogWindowRect.Value, out var initial))
            initial = new Edges(screen.x - 40f - DefaultWidth, (screen.y - DefaultHeight) / 2f, DefaultWidth, DefaultHeight);
        _frame = WindowFrame.Attach(_panel, initial, minWidth: 560f, minHeight: 440f);
        Transform p = _panel;

        // Header
        UiKit.Text("Title", p, "Combat Log", 30).rectTransform.Place(28, 12, 360, 40);
        UiKit.Text("Hint", p, $"Drag here to move · corners to resize · {Plugin.CombatLogKey.Value}/Esc to close", 13,
            TextAlignmentOptions.TopLeft, UiKit.DimTextColor).rectTransform.Place(28, 48, 520, 20);
        var clear = new UiKit.Chip(p, "Clear", 0, 0, 80, () => { DamageLog.Clear(); Filter.Changed(); });
        clear.Background.rectTransform.PlaceTopRight(28, 16, 80, 28);
        var mini = new UiKit.Chip(p, "Mini log", 0, 0, 100, () =>
        {
            Plugin.MiniLogEnabled.Value = !Plugin.MiniLogEnabled.Value;
            RefreshChips();
        });
        mini.Background.rectTransform.PlaceTopRight(28 + 80 + 8, 16, 100, 28);
        _chipRefreshers.Add(() => mini.SetOn(Plugin.MiniLogEnabled.Value));

        // Filters (positions are set by Relayout, so they flow with the window width)
        var show = Group(p, "Show");
        Radio(show, p, new[] { ("Dealt", LogDirection.Dealt), ("Taken", LogDirection.Taken), ("All", LogDirection.All) },
            () => Filter.Direction, v => Filter.Direction = v);

        var scope = Group(p, "Scope");
        Radio(scope, p, new[] { ("This room", LogScope.Room), ("All rooms", LogScope.All) },
            () => Filter.Scope, v => Filter.Scope = v);

        var extra = Group(p, "Filter");
        Toggle(extra, p, "Crits only", () => Filter.CritsOnly, v => Filter.CritsOnly = v);
        var minChip = AddChip(extra, p, "Min: any", 100, () =>
        {
            _minDamageIndex = (_minDamageIndex + 1) % LogFilter.MinDamageSteps.Length;
            Filter.MinDamage = LogFilter.MinDamageSteps[_minDamageIndex];
            Filter.Changed();
            RefreshChips();
        });
        _chipRefreshers.Add(() =>
        {
            int min = LogFilter.MinDamageSteps[_minDamageIndex];
            minChip.Label.text = min == 0 ? "Min: any" : $"Min: {min}";
            minChip.SetOn(min > 0);
        });

        var enemy = Group(p, "Enemy");
        foreach (var (label, kinds) in new[]
        {
            ("Regular", new[] { EntityKind.TrashMob }),
            ("Elites", new[] { EntityKind.Elite }),
            ("Adventurers", new[] { EntityKind.Adventurer }),
            ("Bosses", new[] { EntityKind.Boss }),
            ("Summons", new[] { EntityKind.Summoned }),
            ("Other", new[] { EntityKind.Trap, EntityKind.Other, EntityKind.PlayerMinion }),
        })
        {
            Toggle(enemy, p, label, () => kinds.All(Filter.EnemyKinds.Contains), on =>
            {
                foreach (var k in kinds)
                    Set(Filter.EnemyKinds, k, on);
            });
        }

        var type = Group(p, "Type");
        foreach (Damage.Attribute attribute in Enum.GetValues(typeof(Damage.Attribute)))
            Toggle(type, p, attribute.ToString(), () => Filter.Attributes.Contains(attribute),
                on => Set(Filter.Attributes, attribute, on), DamageSources.AttributeColor(attribute));

        var chart = Group(p, "Chart");
        Radio(chart, p, new[] { ("By source", ChartMode.Source), ("By type", ChartMode.Type) },
            () => _chartMode, v => _chartMode = v, changesFilter: false);

        var source = Group(p, "Source");
        foreach (DamageSource s in Enum.GetValues(typeof(DamageSource)))
            Toggle(source, p, DamageSources.Title(s), () => Filter.Sources.Contains(s),
                on => Set(Filter.Sources, s, on), DamageSources.Color(s));

        // Body: log on the left, pie chart on the right (positions set by Relayout)
        BuildLog(p);
        _tooltip = new TraceTooltip(_canvas);
        BuildChart(p);

        _frame.AddCornerGrips();
        _frame.Resized += _ => Relayout();
        _frame.DragEnded += e => Plugin.CombatLogWindowRect.Value = WindowFrame.Serialize(e);
        Relayout();
        RefreshChips();
        _canvas.gameObject.SetActive(false);
    }

    /// <summary>Positions filters and body for the current window size.</summary>
    private void Relayout()
    {
        float width = _frame.Current.Width;
        const float left = 28f, labelWidth = 70f, rowHeight = 34f, gap = 6f, groupGap = 22f;
        float right = width - 28f;
        float x = left, y = 80f;
        bool lineEmpty = true;

        foreach (var group in _groups)
        {
            float groupWidth = labelWidth + group.Chips.Sum(c => c.width + gap);
            if (!lineEmpty && x + groupGap + groupWidth > right)
            {
                x = left;
                y += rowHeight;
                lineEmpty = true;
            }
            if (!lineEmpty)
                x += groupGap;

            group.Label.rectTransform.Place(x, y, labelWidth, 28);
            x += labelWidth;
            float chipsStart = x;
            foreach (var (chip, w) in group.Chips)
            {
                if (x + w > right && x > chipsStart)
                {
                    x = chipsStart;
                    y += rowHeight;
                }
                chip.Background.rectTransform.Place(x, y, w, 28);
                x += w + gap;
            }
            lineEmpty = false;
        }

        float bodyTop = y + rowHeight + 10f;
        float chartWidth = Mathf.Clamp(width * 0.3f, 180f, 260f);
        _logFrame.Stretch(28f, bodyTop, 28f + chartWidth + 16f, 24f);
        _chartFrame.anchorMin = new Vector2(1f, 0f);
        _chartFrame.anchorMax = new Vector2(1f, 1f);
        _chartFrame.pivot = new Vector2(1f, 0.5f);
        _chartFrame.offsetMin = new Vector2(-(28f + chartWidth), 24f);
        _chartFrame.offsetMax = new Vector2(-28f, -bodyTop);

        float pieSize = Mathf.Min(chartWidth - 32f, 220f);
        _pieRect.Place((chartWidth - pieSize) / 2f, 14f, pieSize, pieSize);
        _legendRect.Stretch(14f, pieSize + 26f, 14f, 10f);
    }

    private void BuildLog(Transform parent)
    {
        var frame = UiKit.Image("LogFrame", parent, new Color32(0x0E, 0x09, 0x14, 0x99));
        _logFrame = frame.rectTransform;

        var viewport = UiKit.Rect("Viewport", frame.transform).Fill(8);
        viewport.gameObject.AddComponent<RectMask2D>();
        // An invisible image so the mouse wheel is caught anywhere over the log.
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);

        _logText = UiKit.Text("Lines", viewport, "", 16);
        var content = _logText.rectTransform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(-8f, 0f);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scroll = frame.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport;
        _scroll.content = content;
        _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;
    }

    private void BuildChart(Transform parent)
    {
        var frame = UiKit.Image("ChartFrame", parent, new Color32(0x0E, 0x09, 0x14, 0x99));
        _chartFrame = frame.rectTransform;

        _pie = new PieChart();
        _pieRect = UiKit.Rect("Pie", frame.transform);
        var raw = _pieRect.gameObject.AddComponent<RawImage>();
        raw.texture = _pie.Texture;
        raw.raycastTarget = false;

        _pieCenterText = UiKit.Text("Total", _pieRect, "", 18, TextAlignmentOptions.Center);
        _pieCenterText.rectTransform.Fill();

        _legendText = UiKit.Text("Legend", frame.transform, "", 15);
        _legendRect = _legendText.rectTransform;
    }

    /// <summary>The largest image in the NPC dialogue box (its background), or null if not found.</summary>
    public static Image FindDialogueBackground()
    {
        Image best = null;
        foreach (var conversation in Resources.FindObjectsOfTypeAll<global::UI.NpcConversation>())
        {
            if (conversation._container == null)
                continue;
            foreach (var image in conversation._container.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null)
                    continue;
                var size = image.rectTransform.rect.size;
                if (best == null || size.x * size.y > best.rectTransform.rect.width * best.rectTransform.rect.height)
                    best = image;
            }
        }
        return best;
    }

    /// <summary>Uses the NPC dialogue box sprite as the panel background, if we can find it.</summary>
    private static void ApplyBackground(Image panel)
    {
        if (!Plugin.CombatLogUseDialogueBackground.Value)
            return;
        try
        {
            Image best = FindDialogueBackground();
            if (best == null)
            {
                Plugin.Log.LogInfo("Combat log: dialogue background not found, using a plain panel.");
                return;
            }
            panel.sprite = best.sprite;
            panel.type = best.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            var c = best.color;
            c.a *= Mathf.Clamp01(Plugin.CombatLogOpacity.Value);
            panel.color = c;
            Plugin.Log.LogInfo($"Combat log: using dialogue background '{best.sprite.name}' (border {best.sprite.border})");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Combat log: could not use dialogue background: {e.Message}");
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;
        var go = new GameObject("DamageInsight_EventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
        Plugin.Log.LogInfo("Combat log: created an EventSystem for mouse input.");
    }

    // ---------------------------------------------------------------- filter widgets

    private ChipGroup Group(Transform parent, string label)
    {
        var group = new ChipGroup
        {
            Label = UiKit.Text($"Label {label}", parent, label, 16, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor),
        };
        _groups.Add(group);
        return group;
    }

    private static UiKit.Chip AddChip(ChipGroup group, Transform parent, string label, float width, Action onClick, Color? onColor = null)
    {
        var chip = new UiKit.Chip(parent, label, 0, 0, width, onClick, onColor);
        group.Chips.Add((chip, width));
        return chip;
    }

    private void Toggle(ChipGroup group, Transform parent, string label, Func<bool> get, Action<bool> set, Color? onColor = null)
    {
        var chip = AddChip(group, parent, label, UiKit.ChipWidth(label), () =>
        {
            set(!get());
            Filter.Changed();
            RefreshChips();
        }, onColor);
        _chipRefreshers.Add(() => chip.SetOn(get()));
    }

    private void Radio<T>(ChipGroup group, Transform parent, (string label, T value)[] options, Func<T> get, Action<T> set,
        bool changesFilter = true)
    {
        foreach (var (label, value) in options)
        {
            var chip = AddChip(group, parent, label, UiKit.ChipWidth(label), () =>
            {
                set(value);
                if (changesFilter)
                    Filter.Changed();
                RefreshChips();
            });
            _chipRefreshers.Add(() => chip.SetOn(EqualityComparer<T>.Default.Equals(get(), value)));
        }
    }

    private void RefreshChips()
    {
        foreach (var refresh in _chipRefreshers)
            refresh();
    }

    private static void Set<T>(HashSet<T> set, T value, bool on)
    {
        if (on) set.Add(value); else set.Remove(value);
    }

    // ---------------------------------------------------------------- content

    private void DrawLog()
    {
        bool wasAtBottom = _scroll.verticalNormalizedPosition <= 0.01f || _logText.text.Length == 0;
        _logText.text = LogText.Build(Feed.Shown, DamageLog.Rooms, MaxLines, _lineMap);
        _hoverLine = -1;
        if (wasAtBottom)
        {
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f; // keep following new entries
        }
    }

    /// <summary>Hovering a log line explains its hit; a click pins the explanation, another click unpins it.</summary>
    private void UpdateTooltip()
    {
        if (_tooltip == null)
            return;
        int index = RecordUnderMouse();
        if (Input.GetMouseButtonDown(0))
        {
            _pinned = index >= 0 && (_pinned == null || _hoverLine != _pinnedLine) ? Feed.Shown[index] : (DamageRecord?)null;
            _pinnedLine = _pinned != null ? _hoverLine : -1;
            if (_pinned != null)
                _tooltip.Show(_pinned.Value, Input.mousePosition);
        }
        if (_pinned != null)
            return;
        if (index >= 0)
            _tooltip.Show(Feed.Shown[index], Input.mousePosition);
        else if (_tooltip.Visible)
            _tooltip.Hide();
    }

    private int _pinnedLine = -1;

    /// <summary>The index in Feed.Shown of the log line under the mouse, or -1.</summary>
    private int RecordUnderMouse()
    {
        _hoverLine = -1;
        if (_logText == null || !RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport, Input.mousePosition, null))
            return -1;
        int tmpLine = TMP_TextUtilities.FindIntersectingLine(_logText, Input.mousePosition, null);
        var info = _logText.textInfo;
        if (tmpLine < 0 || tmpLine >= info.lineCount)
            return -1;
        // TMP lines can wrap: count the real line breaks before this line's first character.
        int first = info.lineInfo[tmpLine].firstCharacterIndex, line = 0;
        for (int i = 0; i < first && i < info.characterCount; i++)
            if (info.characterInfo[i].character == '\n')
                line++;
        _hoverLine = line;
        int index = line < _lineMap.Count ? _lineMap[line] : -1;
        return index >= 0 && index < Feed.Shown.Count ? index : -1;
    }

    private void DrawChart()
    {
        var slices = _chartMode == ChartMode.Source
            ? DamageStats.FromTotals(Feed.TotalBySource)
                .Select(s => (label: DamageSources.Title(s.Key), s.Value, s.Percent, color: DamageSources.Color(s.Key))).ToList()
            : DamageStats.FromTotals(Feed.TotalByAttribute)
                .Select(s => (label: s.Key.ToString(), s.Value, s.Percent, color: DamageSources.AttributeColor(s.Key))).ToList();

        _pie.Draw(slices.Select(s => (s.Value, s.color)).ToList());
        double total = slices.Sum(s => s.Value);
        _pieCenterText.text = total > 0 ? $"<size=70%><color=#A89F94>Total</color></size>\n{total:N0}" : "";

        var sb = new StringBuilder();
        foreach (var s in slices)
        {
            string hex = ColorUtility.ToHtmlStringRGB(s.color);
            sb.Append($"<color=#{hex}>{s.label}</color><pos=48%>{s.Percent:0.0}%<pos=72%><color=#A89F94>{s.Value:N0}</color>\n");
        }
        _legendText.text = sb.ToString();
    }
}
