using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using DamageInsight.Patches;
using GameResources;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DamageInsight.Lang;
using Object = UnityEngine.Object;
using PauseMenu = global::UI.Pause.Menu;
using PausePanel = global::UI.Pause.Panel;
using PauseSelection = global::UI.Pause.Selection;

namespace DamageInsight.UI;

/// <summary>
/// The mod's settings page, opened with the "SkulInsight QoL" button under the game's own "Settings" in the pause
/// menu. It is built from copies of the game's option widgets (the Controls page frame, headings, arrow rows,
/// buttons), so it looks, sounds and navigates like the game's pages with keyboard, mouse and controller.
/// Left (0.16, Umut 2026-10-30): the features in groups (what changes the game, what only shows, helpers, online,
/// general), each with its main switch (<see cref="SettingsCatalog.Features"/>). Hovering or choosing one shows it on
/// the right: what it does and its settings in their own list, the selected setting's help below. Enter goes into the
/// feature's settings, Esc back. Changes apply at once and are saved to the config file at once.
/// </summary>
public sealed class SettingsPage : global::UI.Dialogue
{
    public const string ButtonText = "SkulInsight QoL";
    private const string ModsButtonName = "SkulInsight Mods";
    private const string PageName = "SkulInsight QoL Settings";

    // Layout in the pause canvas (1920x1080, y from the top), matching the game's Settings page.
    private const float ListLeft = -735f, ListTop = -219f, ListWidth = 705f, ListHeight = 676f;
    private const float RowX = 243f, RowPitch = 53f, RowHalf = 26.5f;
    private const float HeaderHeight = 60f, LineBlock = 9f, FirstRowGap = 35f, SectionGap = 24f;
    private const float HelpLeft = 55f, HelpWidth = 680f;
    // The right side: the feature's description, its settings list, the selected setting's help (y from the top).
    private const float FeatureTop = 308f, FeatureHeight = 92f, RightTop = 405f, RightHeight = 6 * RowPitch, HelpTop = 745f, HelpHeight = 150f;
    private const float SubHeadingHeight = 40f;

    // The game's pixel colours: heading lines (three 1-pixel rows at 3x) and the dim heading text.
    private static readonly Color[] LineColors =
    {
        new Color32(94, 63, 46, 255), new Color32(16, 13, 11, 255), new Color32(43, 30, 23, 255),
    };
    private static readonly Color Dim = new Color32(0xAE, 0x97, 0x85, 0xFF);
    private static readonly Color Bright = new Color32(0xD8, 0xC5, 0xB4, 0xFF);

    /// <summary>True while the page is open (our hotkeys stay quiet then, e.g. while a new key is pressed).</summary>
    public static bool IsOpen => _current != null && _current.isActiveAndEnabled;

    private static SettingsPage _current;

    private PauseMenu _menu;
    private PausePanel _panel;
    private Button _menuButton, _return;
    private RectTransform _viewport, _content;
    private ScrollRect _scroll;
    private TMP_Text _helpTitle, _helpBody, _helpDefault;
    private float _contentHeight;
    private bool _starsInFont = true;

    private readonly List<Row> _rows = new();
    /// <summary>Section headings with their place in the list (y from the top), see <see cref="ShowHeadingsInView"/>.</summary>
    private readonly List<(TMP_Text text, float top, float bottom)> _headings = new();
    private readonly Dictionary<GameObject, Row> _rowsByObject = new();

    // The right side (0.16): the shown feature's settings in their own list.
    private RectTransform _rightViewport, _rightContent;
    private ScrollRect _rightScroll;
    private TMP_Text _featureBody;
    private readonly List<FeaturePanel> _features = new();
    private FeaturePanel _shown;

    /// <summary>A feature: its row on the left and its settings on the right (shown while it is selected).</summary>
    private sealed class FeaturePanel
    {
        public SettingsCatalog.Feature Feature;
        public Row Left;
        public RectTransform Panel;
        public readonly List<Row> Rows = new();
        public float Height;
        /// <summary>A feature without a switch: its name (lit while chosen) and its normal colour.</summary>
        public TMP_Text PlainLabel;
        public Color PlainColor;
    }

    private GameObject _lastSelected;
    private float _scrollTarget = -1f;
    private Vector3 _lastMouse;
    private float _mouseActiveUntil;
    private int _topSinceFrame = -1;

    private Row _capturing;
    private int _captureFrame, _ignoreClicksUntil, _restoreNavigationAt = -1;
    private bool _navigationWas = true;

    private sealed class Row
    {
        public SettingItem Item;
        public GameObject Root;
        public PauseSelection Selection;
        public TMP_Text ValueText;
        public Image Icon;
        public List<object> Values = new();
        public int Index;
        public float ViewTop, ViewBottom; // content y range to show when selected (the heading, for a section's first row)
        /// <summary>The feature it belongs to; Left: the feature's own row on the left side.</summary>
        public FeaturePanel Owner;
        public bool Left;
    }

    public override bool closeWithPauseKey => false;

    // ---------------------------------------------------------------- the pause menu button

    /// <summary>Adds our button below the game's "Settings" button (Harmony postfix on Menu.Awake).</summary>
    internal static void AttachTo(PauseMenu menu)
    {
        Button original = menu._settings;
        if (original == null || menu._panel == null)
            return;
        Transform grid = original.transform.parent;
        if (grid.Find(ButtonText) != null)
            return;

        var go = Object.Instantiate(original.gameObject, grid, false);
        go.name = ButtonText;
        go.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
        StripGameScripts(go);
        var label = go.GetComponent<TMP_Text>();
        if (label != null)
        {
            label.text = ButtonText;
            label.enableWordWrapping = false; // shrink to fit instead of breaking into two lines
        }

        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent(); // drop anything copied from the original
        var link = go.AddComponent<Link>();
        link.Menu = menu;
        link.Button = button;
        button.onClick.AddListener(() => Guard.Run("Settings page", () => link.Open(-1)));

        // The mod manager (0.14) right below: a window of its own over the menu.
        if (Plugin.ModManagerEnabled?.Value != false)
        {
            var mods = Object.Instantiate(original.gameObject, grid, false);
            mods.name = ModsButtonName;
            mods.transform.SetSiblingIndex(go.transform.GetSiblingIndex() + 1);
            StripGameScripts(mods);
            var modsLabel = mods.GetComponent<TMP_Text>();
            if (modsLabel != null)
            {
                modsLabel.text = Loc.T("Mods");
                modsLabel.enableWordWrapping = false;
            }
            var modsButton = mods.GetComponent<Button>();
            modsButton.onClick = new Button.ButtonClickedEvent();
            modsButton.onClick.AddListener(() => Guard.Run("Mod manager", ModManagerWindow.Open));
        }

        // Restart room (0.16) has no menu button (Umut 2026-10-30: "It kind of just works with F5"): the key only.
        FitGrid(grid, Plugin.ModManagerEnabled?.Value != false ? 2 : 1);
        LinkButtons(grid);
        Plugin.Log.LogInfo("Settings: button added to the pause menu.");
    }

    /// <summary>Keeps the button column as tall as before: the gaps get smaller so our buttons fit the frame.</summary>
    private static void FitGrid(Transform grid, int added)
    {
        var layout = grid.GetComponent<GridLayoutGroup>();
        int count = grid.Cast<Transform>().Count(t => t.gameObject.activeSelf);
        int before = count - added;
        if (layout == null || before < 2)
            return;
        float cell = layout.cellSize.y, gap = layout.spacing.y;
        float span = before * cell + (before - 1) * gap;
        layout.spacing = new Vector2(layout.spacing.x, Mathf.Max(0f, (span - count * cell) / (count - 1)));
    }

    /// <summary>Up/down between the menu buttons in their order, going round at the ends (as the game does).</summary>
    private static void LinkButtons(Transform grid)
    {
        var buttons = grid.Cast<Transform>().Where(t => t.gameObject.activeSelf)
            .Select(t => t.GetComponent<Button>()).Where(b => b != null).ToList();
        for (int i = 0; i < buttons.Count; i++)
        {
            var nav = buttons[i].navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = buttons[(i - 1 + buttons.Count) % buttons.Count];
            nav.selectOnDown = buttons[(i + 1) % buttons.Count];
            buttons[i].navigation = nav;
        }
    }

    /// <summary>
    /// Esc / Cancel on our page (Harmony prefix on Panel.Return, which the pause panel calls for those keys):
    /// cancels a key capture or goes back to the menu. False when the page isn't the one in front.
    /// </summary>
    internal static bool HandleBack()
    {
        var page = _current;
        if (page == null || !page.isActiveAndEnabled || !page.focused || page._topSinceFrame < 0 || page._topSinceFrame >= Time.frameCount)
            return false;
        if (page._capturing != null)
            page.EndCapture(null);
        else
            page.Back();
        return true;
    }

    /// <summary>
    /// Sits on our menu button: makes the page on first use, and a new one when the language changed (every text
    /// on the page is set once, while it is built from the game's widgets).
    /// </summary>
    private sealed class Link : MonoBehaviour
    {
        public PauseMenu Menu;
        public Button Button;
        private SettingsPage _page;

        /// <param name="row">The row to select (-1: the first).</param>
        public void Open(int row, string feature = null)
        {
            if (_page != null && (_page._language != Loc.Version || _page._builtAll != (Plugin.ShowAllSettings?.Value == true)))
            {
                Destroy(_page.gameObject);
                _page = null;
            }
            if (_page == null)
            {
                _page = Create(Menu, Button);
                _page._link = this;
            }
            _page.Show();
            var again = feature == null ? null : _page._features.FirstOrDefault(f => f.Feature.Key == feature);
            if (again != null)
                _page.Select(again.Left);
            else if (row > 0 && row < _page._rows.Count)
                _page.Select(_page._rows[row]);
        }
    }

    private Link _link;
    private int _language; // Loc.Version the texts were made in
    private bool _builtAll; // built with "All settings" on

    /// <summary>The language changed while the page is open (e.g. on its own Language row): build it again, same row.</summary>
    private void Relanguage()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        int row = _rows.FindIndex(r => r.Root == selected);
        var link = _link;
        Close();
        link?.Open(row);
    }

    // ---------------------------------------------------------------- opening and closing

    private void Show()
    {
        _current = this;
        _menu.Close();
        Open();
    }

    private void Back()
    {
        if (!gameObject.activeSelf)
            return;
        // In a feature's settings: back to the feature on the left first.
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && _rowsByObject.TryGetValue(selected, out var at) && !at.Left && at.Owner?.Left != null)
        {
            Focus(at.Owner.Left.Root.GetComponent<Selectable>());
            return;
        }
        Close();
        if (_menu == null)
            return;
        // Come back to our button rather than the menu's first one.
        var focus = _menu._defaultFocus;
        _menu._defaultFocus = _menuButton;
        _menu.Open();
        _menu._defaultFocus = focus;
    }

    public override void OnEnable() // public: the game DLL is publicized at compile time; widening is allowed
    {
        _topSinceFrame = -1;
        _lastSelected = null;
        _scrollTarget = -1f;
        Guard.Run("Settings page (open)", () =>
        {
            foreach (var row in _rows)
                ShowValue(row);
            _content.anchoredPosition = Vector2.zero;
            ShowIntro();
            if (_features.Count > 0)
                ShowFeature(_features[0]); // the first feature is selected when the page opens
        });
        base.OnEnable();
    }

    public override void OnDisable()
    {
        if (_capturing != null)
            EndCapture(null);
        RestoreNavigation();
        base.OnDisable();
    }

    public override void Update()
    {
        base.Update();
        Guard.Run("Settings page", Tick);
    }

    private void Tick()
    {
        if (_panel == null || !_panel.isActiveAndEnabled)
        {
            gameObject.SetActive(false); // the pause menu went away under us
            return;
        }
        if (_language != Loc.Version && _capturing == null && _link != null)
        {
            Relanguage();
            return;
        }
        if (_builtAll != (Plugin.ShowAllSettings?.Value == true) && _capturing == null && _link != null)
        {
            // "All settings" switched: the page again with or without the fine-tuning, on the same switch.
            var link = _link;
            Close();
            link.Open(-1, SettingsCatalog.AllSettings);
            return;
        }
        if (focused)
        {
            if (_topSinceFrame < 0)
                _topSinceFrame = Time.frameCount;
        }
        else
        {
            _topSinceFrame = -1;
        }

        Vector3 mouse = Input.mousePosition;
        if ((mouse - _lastMouse).sqrMagnitude > 1f || Input.mouseScrollDelta.y != 0f)
            _mouseActiveUntil = Time.unscaledTime + 0.4f;
        _lastMouse = mouse;

        if (_restoreNavigationAt >= 0 && Time.frameCount >= _restoreNavigationAt)
            RestoreNavigation();
        if (_capturing != null)
        {
            Capture();
            return;
        }

        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != _lastSelected)
        {
            _lastSelected = selected;
            foreach (var feature in _features)
                if (feature.PlainLabel != null)
                {
                    bool lit = selected == feature.Left.Root;
                    feature.PlainLabel.color = lit ? Bright : feature.PlainColor;
                    feature.Left.ValueText.color = lit ? Bright : feature.PlainColor;
                }
            if (selected != null && _rowsByObject.TryGetValue(selected, out var row))
            {
                if (row.Left)
                    ShowFeature(row.Owner); // hovering or choosing a feature shows it on the right
                else
                    ShowHelp(row.Item);
                if (Time.unscaledTime >= _mouseActiveUntil)
                    ScrollTo(row); // keyboard/controller: keep the selected row in view (never chase the mouse)
            }
            else if (_return != null && selected == _return.gameObject)
            {
                ShowIntro();
            }
        }

        if (_scrollTarget >= 0f)
        {
            float y = _content.anchoredPosition.y;
            float next = Mathf.Lerp(y, _scrollTarget, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - _scrollTarget) < 0.5f)
            {
                next = _scrollTarget;
                _scrollTarget = -1f;
            }
            _content.anchoredPosition = new Vector2(0f, next);
        }
        ShowHeadingsInView();
    }

    /// <summary>
    /// A heading just below the list showed over the page frame (the game's heading text wasn't cut off by the list's
    /// mask). Headings are drawn only while they are wholly inside the list.
    /// </summary>
    private void ShowHeadingsInView()
    {
        float offset = _content.anchoredPosition.y, view = _viewport.rect.height;
        foreach (var (text, top, bottom) in _headings)
        {
            bool inside = top >= offset - 1f && bottom <= offset + view + 1f;
            if (text.enabled != inside)
                text.enabled = inside;
        }
    }

    private void ScrollTo(Row row)
    {
        if (!row.Left)
        {
            // The right list: straight to the row.
            float rightView = _rightViewport.rect.height, at = _rightContent.anchoredPosition.y;
            float to = row.ViewTop - 8f < at ? row.ViewTop - 8f : row.ViewBottom + 8f > at + rightView ? row.ViewBottom + 8f - rightView : at;
            _rightContent.anchoredPosition = new Vector2(0f, Mathf.Clamp(to, 0f, Mathf.Max(0f, (_shown?.Height ?? 0f) - rightView)));
            return;
        }
        float view = _viewport.rect.height;
        float offset = _scrollTarget >= 0f ? _scrollTarget : _content.anchoredPosition.y;
        float top = row.ViewTop - 8f, bottom = row.ViewBottom + 8f;
        float target = offset;
        if (top < offset)
            target = top;
        else if (bottom > offset + view)
            target = bottom - view;
        target = Mathf.Clamp(target, 0f, Mathf.Max(0f, _contentHeight - view));
        if (Mathf.Abs(target - _content.anchoredPosition.y) > 0.5f)
        {
            _scroll.StopMovement();
            _scrollTarget = target;
        }
    }

    // ---------------------------------------------------------------- building

    private static SettingsPage Create(PauseMenu menu, Button menuButton)
    {
        PausePanel panel = menu._panel;
        var go = new GameObject(PageName, typeof(RectTransform));
        go.SetActive(false); // nothing of the copies wakes up before we have changed it
        go.layer = panel.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(panel.transform.parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        // Right above the pause panel: its dark backdrop stays behind us, the game's confirm dialog in front.
        rect.SetSiblingIndex(panel.transform.GetSiblingIndex() + 1);

        var page = go.AddComponent<SettingsPage>();
        page._menu = menu;
        page._panel = panel;
        page._menuButton = menuButton;
        try
        {
            page.Build(panel._settings.transform, panel._controls.transform);
        }
        catch
        {
            Object.Destroy(go);
            throw;
        }
        return page;
    }

    private void Build(Transform settings, Transform controls)
    {
        Transform frame = Find(controls, "Frame"), title = Find(settings, "Title"), back = Find(settings, "Return");
        Transform heading = Find(settings, "Graphics Label"), choiceRow = Find(settings, "Graphics/Resolution");
        Transform buttonRow = Find(settings, "Data/Reset Data"), rowLabel = Find(choiceRow, "Label");

        Clone(frame, transform); // the Controls page frame: two clean columns, no fixed heading lines
        var titleText = Clone(title, transform).GetComponent<TMP_Text>();
        titleText.text = ButtonText;

        // Return keeps the game's own (translated) text.
        var returnGo = Clone(back, transform, keepLocalizer: true);
        _return = returnGo.GetComponent<Button>();
        _return.onClick = new Button.ButtonClickedEvent();
        _return.onClick.AddListener(() => Guard.Run("Settings page (return)", Back));

        BuildList();
        _starsInFont = HasStars(rowLabel.GetComponent<TMP_Text>());
        _language = Loc.Version;

        var items = SettingsCatalog.Build(Plugin.CodexKey.ConfigFile, InscriptionIconNames(), InscriptionName);
        _slotsHeading = items.FirstOrDefault(i => i.Entry?.Definition.Section == SettingsCatalog.SaveSlotsSection)?.Section;
        BuildRightList();
        float y = 0f;
        _builtAll = Plugin.ShowAllSettings?.Value == true;
        foreach (var group in SettingsCatalog.Features(items, _builtAll).GroupBy(f => f.Group))
        {
            if (y > 0f)
                y += SectionGap;
            float sectionTop = y;
            var head = Clone(heading, _content).GetComponent<TMP_Text>();
            head.text = SettingsCatalog.GroupHeading(group.Key);
            head.enableWordWrapping = false;
            head.maskable = true; // the game's heading is static text; in our list it must be cut at the edges
            _headings.Add((head, y, y + HeaderHeight));
            var hr = head.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = Vector2.zero;
            hr.sizeDelta = new Vector2(ListWidth - 40f, HeaderHeight);
            hr.anchoredPosition = new Vector2(10f, -(y + HeaderHeight));
            Lines(_content, new Vector2(0f, 1f), 10f, y + HeaderHeight, ListWidth - 40f);

            float center = y + HeaderHeight + LineBlock + FirstRowGap;
            bool first = true;
            foreach (var feature in group)
            {
                var panel = new FeaturePanel { Feature = feature };
                // A feature without a main switch is a plain row: choosing it opens its settings.
                var item = feature.Toggle ?? new SettingItem
                {
                    Section = feature.Title, ConfigSection = feature.Key, Label = feature.Title, Kind = SettingKind.Action,
                    Action = SettingsCatalog.OpenFeature, Help = feature.Description,
                    ButtonLabel = Loc.P("{0} setting", "{0} settings", feature.Items.Count),
                };
                var row = AddRow(item, item.Kind == SettingKind.Choice ? choiceRow : buttonRow, center, _content, _scroll, left: true);
                var label = Find(row.Root.transform, "Label").GetComponent<TMP_Text>();
                label.text = Stars(feature.Title);
                row.Owner = panel;
                panel.Left = row;
                if (feature.Toggle == null)
                {
                    // The game's button frame stood there empty (Umut 2026-10-30: "It looks weird"): see-through, still
                    // hit by the mouse; the name lights up like the switch rows' when chosen (Tick).
                    foreach (var image in row.Root.GetComponentsInChildren<Image>(true))
                        image.color = Color.clear;
                    panel.PlainLabel = label;
                    panel.PlainColor = label.color;
                    row.ValueText.color = label.color;
                }
                row.ViewTop = first ? sectionTop : center - RowHalf;
                row.ViewBottom = center + RowHalf;
                first = false;
                center += RowPitch;
                BuildPanel(panel, choiceRow, buttonRow, rowLabel);
                _features.Add(panel);
            }
            y = center - RowPitch + RowHalf;
        }
        _contentHeight = y + 16f;
        _content.sizeDelta = new Vector2(0f, _contentHeight);

        BuildHelp(heading, rowLabel);
        LinkRows();
        _defaultFocus = _features.Count > 0 ? (Selectable)_features[0].Left.Root.GetComponent<Selectable>() : _return;
        Plugin.Log.LogInfo($"Settings: page built with {_features.Count} features and {_rows.Count} rows.");
    }

    /// <summary>The right side's list: the shown feature's settings (each feature's own panel, one shown at a time).</summary>
    private void BuildRightList()
    {
        _rightViewport = UiKit.Rect("Feature settings", transform);
        Place(_rightViewport, new Vector2(0f, 1f), HelpLeft, RightTop, HelpWidth, RightHeight);
        _rightViewport.gameObject.AddComponent<Image>().color = Color.clear; // catches the mouse wheel between rows
        _rightViewport.gameObject.AddComponent<RectMask2D>();
        _rightContent = UiKit.Rect("Settings", _rightViewport);
        _rightContent.anchorMin = new Vector2(0f, 1f);
        _rightContent.anchorMax = new Vector2(1f, 1f);
        _rightContent.pivot = new Vector2(0.5f, 1f);
        _rightContent.anchoredPosition = Vector2.zero;
        _rightScroll = _rightViewport.gameObject.AddComponent<ScrollRect>();
        _rightScroll.content = _rightContent;
        _rightScroll.viewport = _rightViewport;
        _rightScroll.horizontal = false;
        _rightScroll.vertical = true;
        _rightScroll.movementType = ScrollRect.MovementType.Clamped;
        _rightScroll.inertia = false;
        _rightScroll.scrollSensitivity = 40f;

        var bar = UiKit.Image("Scroll bar", transform, LineColors[2]);
        Place(bar.rectTransform, new Vector2(0f, 1f), HelpLeft + HelpWidth + 6f, RightTop, 6f, RightHeight);
        var handle = UiKit.Image("Handle", bar.rectTransform, Dim);
        handle.rectTransform.Fill();
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.transition = Selectable.Transition.None;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        _rightScroll.verticalScrollbar = scrollbar;
        _rightScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    /// <summary>A feature's settings on the right, with a small heading where a group of them starts (damage icons).</summary>
    private void BuildPanel(FeaturePanel panel, Transform choiceRow, Transform buttonRow, Transform rowLabel)
    {
        var rect = UiKit.Rect(panel.Feature.Key, _rightContent);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        panel.Panel = rect;
        float y = 4f;
        string group = panel.Feature.Title;
        foreach (var item in panel.Feature.Items)
        {
            if (item.Section != group && item.Section != panel.Feature.Title)
            {
                group = item.Section;
                var sub = Clone(rowLabel, rect).GetComponent<TMP_Text>();
                sub.text = Stars(group);
                sub.enableAutoSizing = false;
                sub.fontSize = 24f;
                sub.color = Dim;
                sub.enableWordWrapping = false;
                sub.alignment = TextAlignmentOptions.BottomLeft;
                var sr = sub.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0f, 1f);
                sr.pivot = new Vector2(0f, 1f);
                sr.sizeDelta = new Vector2(HelpWidth - 20f, SubHeadingHeight);
                sr.anchoredPosition = new Vector2(10f, -y);
                y += SubHeadingHeight;
            }
            float center = y + RowHalf;
            var row = AddRow(item, item.Kind == SettingKind.Choice ? choiceRow : buttonRow, center, rect, _rightScroll);
            row.Owner = panel;
            row.ViewTop = center - RowHalf;
            row.ViewBottom = center + RowHalf;
            panel.Rows.Add(row);
            y += RowPitch;
        }
        panel.Height = y + 8f;
        rect.sizeDelta = new Vector2(0f, panel.Height);
        rect.gameObject.SetActive(false);
    }

    /// <summary>Shows a feature on the right: its title, what it does, its settings.</summary>
    private void ShowFeature(FeaturePanel panel)
    {
        if (panel == null || panel == _shown)
            return;
        if (_shown != null)
            _shown.Panel.gameObject.SetActive(false);
        _shown = panel;
        panel.Panel.gameObject.SetActive(true);
        _rightContent.sizeDelta = new Vector2(0f, panel.Height);
        _rightContent.anchoredPosition = Vector2.zero;
        _helpTitle.text = Stars(panel.Feature.Title);
        _featureBody.text = Stars(panel.Feature.Description ?? "").Replace('\\', '/');
        _helpBody.text = $"<color={DimHex}>" + (panel.Rows.Count == 0
            ? Loc.T("Nothing more to set: the switch on the left turns it on or off.")
            : Loc.T("Choose one of its settings (move the mouse there, or press Enter) to see what it does. Esc goes back.")) + "</color>";
        _helpDefault.text = "";
    }

    /// <summary>Into the shown feature's settings (Enter on a feature, or a click on a feature without a switch).</summary>
    private void EnterFeature(FeaturePanel panel)
    {
        ShowFeature(panel);
        if (panel?.Rows.Count > 0)
            Focus(panel.Rows[0].Root.GetComponent<Selectable>());
    }

    /// <summary>Selects a row (its feature shown first when it is on the right side).</summary>
    private void Select(Row row)
    {
        if (!row.Left)
            ShowFeature(row.Owner);
        Focus(row.Root.GetComponent<Selectable>());
    }

    private void BuildList()
    {
        _viewport = UiKit.Rect("List", transform);
        _viewport.anchorMin = _viewport.anchorMax = new Vector2(0.5f, 1f);
        _viewport.pivot = new Vector2(0f, 1f);
        _viewport.anchoredPosition = new Vector2(ListLeft, ListTop);
        _viewport.sizeDelta = new Vector2(ListWidth, ListHeight);
        _viewport.gameObject.AddComponent<Image>().color = Color.clear; // catches the mouse wheel between rows
        _viewport.gameObject.AddComponent<RectMask2D>();

        _content = UiKit.Rect("Rows", _viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;

        _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
        _scroll.content = _content;
        _scroll.viewport = _viewport;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.inertia = false;
        _scroll.scrollSensitivity = 40f;

        // A thin scroll bar between the list and the frame's middle line.
        var bar = UiKit.Image("Scroll bar", transform, LineColors[2]);
        var br = bar.rectTransform;
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 1f);
        br.pivot = new Vector2(0f, 1f);
        br.anchoredPosition = new Vector2(-24f, ListTop);
        br.sizeDelta = new Vector2(6f, ListHeight);
        var handle = UiKit.Image("Handle", br, Dim);
        handle.rectTransform.Fill();
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.transition = Selectable.Transition.None;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        _scroll.verticalScrollbar = scrollbar;
        _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void BuildHelp(Transform heading, Transform rowLabel)
    {
        _helpTitle = Clone(heading, transform).GetComponent<TMP_Text>();
        _helpTitle.enableWordWrapping = false;
        Place(_helpTitle.rectTransform, new Vector2(0f, 0f), HelpLeft, 279.5f, HelpWidth, HeaderHeight);
        Lines(transform, new Vector2(0.5f, 1f), HelpLeft, 279.5f, HelpWidth);

        _featureBody = Text(rowLabel, 24f, Bright);
        Place(_featureBody.rectTransform, new Vector2(0f, 1f), HelpLeft, FeatureTop, HelpWidth, FeatureHeight);
        _featureBody.enableAutoSizing = true; // a long description shrinks instead of running into the settings
        _featureBody.fontSizeMin = 16f;
        _featureBody.fontSizeMax = 24f;
        _featureBody.overflowMode = TextOverflowModes.Truncate;
        Lines(transform, new Vector2(0.5f, 1f), HelpLeft, HelpTop - 8f, HelpWidth);
        _helpBody = Text(rowLabel, 22f, Bright);
        Place(_helpBody.rectTransform, new Vector2(0f, 1f), HelpLeft, HelpTop, HelpWidth, HelpHeight);
        _helpBody.enableAutoSizing = true;
        _helpBody.fontSizeMin = 15f;
        _helpBody.fontSizeMax = 22f;
        _helpBody.overflowMode = TextOverflowModes.Truncate;
        _helpDefault = Text(rowLabel, 20f, Dim); // the default is part of the help now; kept empty
        Place(_helpDefault.rectTransform, new Vector2(0f, 1f), HelpLeft, HelpTop + HelpHeight, HelpWidth, 1f);
    }

    private static readonly string DimHex = "#" + ColorUtility.ToHtmlStringRGB(new Color32(0xAE, 0x97, 0x85, 0xFF));

    private TMP_Text Text(Transform template, float size, Color color)
    {
        var text = Clone(template, transform).GetComponent<TMP_Text>();
        text.enableAutoSizing = false;
        text.fontSize = size;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = color;
        text.richText = true; // the game's row label has it off (the help's colour tags showed as text)
        text.text = "";
        return text;
    }

    /// <summary>Places a rect in the page by its top-centre anchor; x from the centre, y down from the top.</summary>
    private static void Place(RectTransform rect, Vector2 pivot, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = pivot;
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    /// <summary>The game's heading underline: three 1-pixel rows at 3x scale.</summary>
    private static void Lines(Transform parent, Vector2 anchor, float x, float y, float width)
    {
        for (int i = 0; i < LineColors.Length; i++)
        {
            var line = UiKit.Image("Line", parent, LineColors[i]);
            line.raycastTarget = false;
            var r = line.rectTransform;
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(width, 3f);
            r.anchoredPosition = new Vector2(x, -(y + i * 3f));
        }
    }

    private Row AddRow(SettingItem item, Transform template, float center, RectTransform parent, ScrollRect list, bool left = false)
    {
        var go = Clone(template, parent);
        go.name = item.Label;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(RowX, -center);

        var row = new Row { Item = item, Root = go, Left = left };
        var label = Find(go.transform, "Label").GetComponent<TMP_Text>();
        label.text = Stars(item.Label);
        row.ValueText = Find(go.transform, "Content/Text").GetComponent<TMP_Text>();

        if (item.Kind == SettingKind.Choice)
        {
            row.Selection = go.GetComponent<PauseSelection>();
            row.Selection.onValueChanged += i => Guard.Run("Settings page (change)", () => Picked(row, i));
            if (left)
            {
                var into = go.AddComponent<SubmitIntoFeature>(); // Enter / A on a feature: into its settings
                into.Page = this;
                into.Row = row;
            }
            else if (item.Wraps && item.Steps.Count == 2)
                go.AddComponent<SubmitStepper>().Selection = row.Selection; // Enter / A flips on/off too
            if (item.ShowsIcon)
            {
                row.Icon = UiKit.Image("Icon", go.transform, Color.white);
                row.Icon.preserveAspect = true;
                row.Icon.raycastTarget = false;
                var ir = row.Icon.rectTransform;
                ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0.5f, 0.5f);
                ir.sizeDelta = new Vector2(36f, 36f);
                ir.anchoredPosition = new Vector2(46f, 0f); // between the label and the left arrow
                var lr = label.rectTransform;
                lr.sizeDelta = new Vector2(Mathf.Min(lr.sizeDelta.x, 250f), lr.sizeDelta.y);
            }
        }
        else
        {
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Guard.Run("Settings page (button)", () => Pressed(row)));
        }

        // The game's PlaySoundOnSelected on every row is an EventTrigger: it takes the mouse wheel and drops it,
        // so scrolling stuck whenever the pointer was over a row. Hand the wheel on to the list.
        go.AddComponent<WheelToList>().List = list;

        ShowValue(row);
        _rows.Add(row);
        _rowsByObject[go] = row;
        return row;
    }

    /// <summary>Up/down through the features (the first and last go on to Return and round), and through each feature's settings.</summary>
    private void LinkRows()
    {
        var features = _features.Select(f => f.Left.Root.GetComponent<Selectable>()).Where(s => s != null).ToList();
        features.Add(_return);
        Link(features);
        foreach (var panel in _features)
            Link(panel.Rows.Select(r => r.Root.GetComponent<Selectable>()).Where(s => s != null).ToList());

        static void Link(List<Selectable> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var nav = all[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = all[(i - 1 + all.Count) % all.Count];
                nav.selectOnDown = all[(i + 1) % all.Count];
                nav.selectOnLeft = nav.selectOnRight = null;
                all[i].navigation = nav;
            }
        }
    }

    // ---------------------------------------------------------------- values

    private void ShowValue(Row row)
    {
        var item = row.Item;
        switch (item.Kind)
        {
            case SettingKind.Choice:
                row.Values = item.Values();
                row.Index = SettingItem.IndexOf(row.Values, item.Entry.BoxedValue);
                row.Selection.SetTexts(row.Values.Select(item.Format).ToArray());
                row.Selection.SetValueWithoutNotify(row.Index);
                break;
            case SettingKind.Key:
                row.ValueText.text = item.Format(item.Entry.BoxedValue);
                break;
            case SettingKind.Action when item.Action == SettingsCatalog.ResetCodex && Codex.CodexReset.Requested:
                row.ValueText.text = Loc.T("At the next start");
                break;
            case SettingKind.Action when item.ButtonLabel != null:
                row.ValueText.text = item.ButtonLabel;
                break;
            default:
                row.ValueText.text = Localized("label/pause/settings/data/reset", "Reset");
                break;
        }
        ShowIcon(row);
    }

    private static void ShowIcon(Row row)
    {
        if (row.Icon == null)
            return;
        var sprite = IconLibrary.Resolve(row.Item.Entry.BoxedValue as string);
        row.Icon.sprite = sprite;
        row.Icon.enabled = sprite != null;
    }

    private void Picked(Row row, int index)
    {
        int count = row.Values.Count;
        // Selection goes round at the ends; numbers stop there instead.
        bool wrapped = count > 2 && ((row.Index == 0 && index == count - 1) || (row.Index == count - 1 && index == 0));
        if (wrapped && !row.Item.Wraps)
        {
            row.Selection.SetValueWithoutNotify(row.Index);
            return;
        }
        row.Index = index;
        row.Item.Entry.BoxedValue = row.Values[index]; // applies at once; BepInEx saves the file
        ShowIcon(row);
        if (row.Item.HelpProvider != null)
            ShowHelp(row.Item); // the save state slot: show what the newly chosen slot holds
        if (row.Item.Entry?.Definition.Section == SettingsCatalog.SaveSlotsSection)
            RefreshSlots(); // the number of slots changes the slot row's values
    }

    private void Pressed(Row row)
    {
        if (Time.frameCount <= _ignoreClicksUntil)
            return;
        switch (row.Item.Kind)
        {
            case SettingKind.Key:
                StartCapture(row);
                break;
            case SettingKind.Action when row.Item.Action == SettingsCatalog.ResetWindow:
                Plugin.CombatLogWindowRect.Value = "";
                break;
            case SettingKind.Action when row.Item.Action == SettingsCatalog.ResetAll:
                ConfirmResetAll(row);
                break;
            case SettingKind.Action when row.Item.Action == SettingsCatalog.ResetCodex:
                ConfirmResetCodex(row);
                break;
            case SettingKind.Action when row.Item.Action == SettingsCatalog.OpenCrashReports:
                Crash.CrashReporter.OpenFolder();
                break;
            case SettingKind.Action when row.Item.Action is SettingsCatalog.SlotPlay or SettingsCatalog.SlotCopy or SettingsCatalog.SlotClear:
                SlotAction(row);
                break;
            case SettingKind.Action when row.Item.Action == SettingsCatalog.OpenFeature:
                EnterFeature(row.Owner);
                break;
        }
    }

    // ---------------------------------------------------------------- save slots

    /// <summary>Play / Copy / Clear on the selected save slot; each asks first.</summary>
    private void SlotAction(Row row)
    {
        var focus = row.Root.GetComponent<Selectable>();
        void Refocus() => Focus(focus);
        int slot = SaveSlots.SlotSwitcher.Selected, playing = SaveSlots.SlotStore.Active;
        bool hasGame = SaveSlots.SlotStore.HasGame(slot);
        var confirm = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.confirm;
        void Ask(string question, Action yes)
        {
            if (confirm == null)
                yes();
            else
                confirm.Open(question, () => Guard.Run("Settings page (save slots)", yes), Refocus);
        }
        void Report(string message)
        {
            if (this == null || !isActiveAndEnabled)
                return; // a copy finished after the page was closed
            RefreshSlots();
            _helpBody.text = message + "\n\n" + row.Item.CurrentHelp;
            Refocus();
        }

        switch (row.Item.Action)
        {
            case SettingsCatalog.SlotPlay:
            {
                string why = SaveSlots.SlotSwitcher.CannotSwitch(slot);
                if (why != null)
                    Report(why);
                else
                    Ask(hasGame
                        ? Loc.F("Play slot {0}? Your current game stays in slot {1}. The game goes back to the title screen.", slot, playing)
                        : Loc.F("Start a new game in slot {0}? Your current game stays in slot {1}. The game goes back to the title screen.", slot, playing),
                        () =>
                        {
                            string message = SaveSlots.SlotSwitcher.Switch(slot, CloseAll);
                            if (message != null)
                                Report(message);
                        });
                break;
            }
            case SettingsCatalog.SlotCopy:
            {
                string why = SaveSlots.SlotSwitcher.CannotCopy(slot);
                if (why != null)
                    Report(why);
                else
                    Ask(hasGame
                        ? Loc.F("Replace slot {0} with a copy of your game (and its Codex)?", slot)
                        : Loc.F("Copy your game (and its Codex) into slot {0}?", slot),
                        () =>
                        {
                            _helpBody.text = Loc.F("Copying your game into slot {0}...", slot);
                            SaveSlots.SlotSwitcher.Copy(slot, Report);
                        });
                break;
            }
            case SettingsCatalog.SlotClear:
            {
                string why = SaveSlots.SlotSwitcher.CannotCopy(slot);
                if (why != null)
                    Report(why);
                else if (!hasGame)
                    Report(Loc.F("Slot {0} is empty.", slot));
                else
                    Ask(Loc.F("Clear slot {0}? Its game and its Codex are deleted.", slot), () => Report(SaveSlots.SlotSwitcher.Clear(slot)));
                break;
            }
        }
    }

    /// <summary>The page and the pause menu go away (a slot switch goes back to the title screen).</summary>
    private void CloseAll()
    {
        Close();
        if (_panel != null)
            _panel.gameObject.SetActive(false);
    }

    /// <summary>The save slot rows show the slots' current contents again (after Copy / Clear, or a new slot count).</summary>
    private void RefreshSlots()
    {
        foreach (var row in _rows)
            if (row.Item.Section == _slotsHeading)
                ShowValue(row);
    }

    private string _slotsHeading;

    /// <summary>Reset Codex: asks first; selected again while requested, it withdraws the request.</summary>
    private void ConfirmResetCodex(Row row)
    {
        var focus = row.Root.GetComponent<Selectable>();
        void Refocus() => Focus(focus);
        if (Codex.CodexReset.Requested)
        {
            Codex.CodexReset.Withdraw();
            ShowValue(row);
            return;
        }
        void Request()
        {
            Codex.CodexReset.Request();
            ShowValue(row);
            Refocus();
        }
        var confirm = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.confirm;
        if (confirm == null)
        {
            Request();
            return;
        }
        confirm.Open(Loc.T("Delete everything the Codex has gathered (progress, films, pictures) the next time the game starts?"),
            () => Guard.Run("Settings page (reset Codex)", Request), Refocus);
    }

    private void ConfirmResetAll(Row row)
    {
        var focus = row.Root.GetComponent<Selectable>();
        void Refocus() => Focus(focus);
        void ResetAll()
        {
            var config = Plugin.CodexKey.ConfigFile;
            bool saveEach = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false; // one save for all of them
            try
            {
                foreach (var entry in config.Select(p => p.Value).ToList())
                    entry.BoxedValue = entry.DefaultValue;
            }
            finally
            {
                config.SaveOnConfigSet = saveEach;
                config.Save();
            }
            foreach (var r in _rows)
                ShowValue(r);
            Refocus();
        }

        var confirm = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.confirm;
        if (confirm == null)
        {
            ResetAll();
            return;
        }
        confirm.Open(Loc.F("Reset all {0} settings to their defaults?", ButtonText),
            () => Guard.Run("Settings page (reset)", ResetAll), Refocus);
    }

    // ---------------------------------------------------------------- key capture

    private static readonly KeyCode[] Modifiers =
    {
        KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift,
        KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand,
    };

    private static readonly KeyCode[] KeyboardKeys = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>()
        .Where(k => k != KeyCode.None && k < KeyCode.Mouse0 && k != KeyCode.Escape && !Modifiers.Contains(k))
        .Distinct().ToArray();

    private void StartCapture(Row row)
    {
        _capturing = row;
        _captureFrame = Time.frameCount;
        row.ValueText.text = Loc.T("Press a key");
        // Keys pressed now are meant for us, not for moving through the page.
        if (EventSystem.current != null && _restoreNavigationAt < 0)
        {
            _navigationWas = EventSystem.current.sendNavigationEvents;
            EventSystem.current.sendNavigationEvents = false;
        }
        _restoreNavigationAt = int.MaxValue;
    }

    private void Capture()
    {
        if (Time.frameCount <= _captureFrame)
            return; // the key that started the capture
        foreach (var key in KeyboardKeys)
        {
            if (!Input.GetKeyDown(key))
                continue;
            var held = Modifiers.Where(Input.GetKey).ToArray();
            EndCapture(new KeyboardShortcut(key, held));
            return;
        }
    }

    /// <summary>Ends the key capture; <paramref name="shortcut"/> null keeps the old key.</summary>
    private void EndCapture(KeyboardShortcut? shortcut)
    {
        var row = _capturing;
        _capturing = null;
        _ignoreClicksUntil = Time.frameCount + 1; // Enter as the new key must not start another capture
        _restoreNavigationAt = Time.frameCount + 1;
        if (row == null)
            return;
        if (shortcut.HasValue)
            row.Item.Entry.BoxedValue = shortcut.Value;
        ShowValue(row);
        if (EventSystem.current != null && row.Root != null)
            EventSystem.current.SetSelectedGameObject(row.Root);
    }

    private void RestoreNavigation()
    {
        if (_restoreNavigationAt < 0)
            return;
        _restoreNavigationAt = -1;
        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = _navigationWas;
    }

    // ---------------------------------------------------------------- help

    private void ShowIntro()
    {
        if (_shown != null)
            _shown.Panel.gameObject.SetActive(false);
        _shown = null;
        _helpTitle.text = ButtonText;
        _featureBody.text = Loc.F("Version {0}.", MyPluginInfo.PLUGIN_VERSION) + " " +
                            Loc.T("Every setting of the mod in one place. Changes apply at once and are saved right away (BepInEx/config/docrun.skulinsight_qol.cfg).");
        _helpBody.text = Loc.T("Left: the features, each with its switch. Right: the chosen feature's settings.\nUp / down: choose. Left / right: switch on or off. Enter: its settings. Esc: back.");
        _helpDefault.text = "";
    }

    /// <summary>A setting on the right: its name, what it does and its default, under the feature's settings.</summary>
    private void ShowHelp(SettingItem item)
    {
        string help = Stars(item.CurrentHelp).Replace('\\', '/'); // TMP would read the "\r" in "Debug\runs" as a line break
        // No default for the save state slot: it picks a slot, it isn't a setting to put back.
        string fallback = item.Entry != null && item.HelpProvider == null ? $"\n<color={DimHex}>{Loc.F("Default: {0}", item.DefaultText)}</color>" : "";
        _helpBody.text = $"{Stars(item.Label)}: {help}{fallback}";
        _helpDefault.text = "";
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The game's inscription icon names (Arms, Mutation...), the damage icon choices.</summary>
    private static IEnumerable<string> InscriptionIconNames()
    {
        try
        {
            var icons = global::GameResources.CommonResource.instance?._keywordIcons;
            return icons == null ? Enumerable.Empty<string>() : icons.Where(s => s != null).Select(s => s.name).ToList();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Settings: no inscription icons ({e.Message}); the icon rows only offer the text tag.");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>An inscription's name in the game's language ("Arms" → "무장"), or null.</summary>
    private static string InscriptionName(string key) =>
        Localization.TryGetLocalizedString($"synergy/key/{key}/name", out var name) && !string.IsNullOrEmpty(name) ? name : null;

    private static bool HasStars(TMP_Text text)
    {
        try
        {
            return text != null && text.font != null && text.font.HasCharacter('★', true, true);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The Codex's ★★ / ★★★, spelled out if the game's option font has no star.</summary>
    private string Stars(string text) =>
        _starsInFont || text == null ? text : text.Replace("★★★", "3 stars").Replace("★★", "2 stars").Replace("★", "star");

    private static string Localized(string key, string fallback)
    {
        try
        {
            string text = Localization.GetLocalizedString(key);
            return string.IsNullOrWhiteSpace(text) || text == key ? fallback : text;
        }
        catch
        {
            return fallback;
        }
    }

    private static Transform Find(Transform parent, string path) =>
        parent.Find(path) ?? throw new InvalidOperationException($"the game's '{path}' under '{parent.name}' is missing");

    /// <summary>A copy of one of the game's widgets, without the scripts that would change it behind our back.</summary>
    private static GameObject Clone(Transform template, Transform parent, bool keepLocalizer = false)
    {
        var go = Object.Instantiate(template.gameObject, parent, false);
        go.SetActive(true);
        StripGameScripts(go, keepLocalizer);
        return go;
    }

    /// <summary>
    /// Removes the translator (it would put the game's text back on every opening) and the controller-bumper jumps
    /// to the game's own Settings widgets.
    /// </summary>
    private static void StripGameScripts(GameObject go, bool keepLocalizer = false)
    {
        if (!keepLocalizer)
            foreach (var localizer in go.GetComponentsInChildren<global::UI.TextLocalizer>(true))
                Object.DestroyImmediate(localizer);
        foreach (var jump in go.GetComponentsInChildren<global::UI.Pause.ControllerLeftRightNavigation>(true))
            Object.DestroyImmediate(jump);
    }

    /// <summary>Enter / controller A on a feature on the left: into its settings (left / right switch it on or off).</summary>
    private sealed class SubmitIntoFeature : MonoBehaviour, ISubmitHandler
    {
        public SettingsPage Page;
        public Row Row;

        public void OnSubmit(BaseEventData eventData) => Guard.Run("Settings page (feature)", () => Page.EnterFeature(Row.Owner));
    }

    /// <summary>Enter / controller A on an on/off row flips it (the game's own rows only use left/right).</summary>
    private sealed class SubmitStepper : MonoBehaviour, ISubmitHandler
    {
        public PauseSelection Selection;

        public void OnSubmit(BaseEventData eventData) => Selection?.MoveRight();
    }

    /// <summary>Passes the mouse wheel from a row on to the list (see AddRow).</summary>
    private sealed class WheelToList : MonoBehaviour, IScrollHandler
    {
        public ScrollRect List;

        public void OnScroll(PointerEventData eventData)
        {
            if (List != null)
                List.OnScroll(eventData);
        }
    }
}
