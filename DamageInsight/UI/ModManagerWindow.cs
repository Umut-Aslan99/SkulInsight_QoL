using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.History;
using DamageInsight.Lang;
using DamageInsight.Mods;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// The mod manager window (0.14, Umut: "a menu that shows installed mods and displays mods on Thunderstore, with an
/// easy download / install button ... a little bit like tModLoader, or the Minecraft resource pack selection with a
/// left and right side"). Left: Thunderstore's Skul packages with search, categories and sorting, each with its
/// icon, author, downloads, likes, age and Install / Update. Right: your mods with Enabled / Disabled / Restart needed
/// badges, Turn on / off, Update, Remove. Bottom: turn all on or off, the mods folder, Restart game, Close. A card
/// over the window shows a package's details and asks before removing, installing with dependencies or restarting.
/// Opened from the pause menu (Mods); Esc closes the card, then the window.
/// </summary>
public sealed class ModManagerWindow : MonoBehaviour
{
    private const float Width = 1700f, Height = 940f, LeftWidth = 1040f, RightWidth = 604f, Gap = 16f; // 20 + 1040 + 16 + 604 + 20
    private const float StoreRow = 112f, MineRow = 96f;

    // Thunderstore's dark look (navy, blue frames, green for "on", orange for "needs a restart").
    private static readonly Color Back = new Color32(0x14, 0x11, 0x27, 0xF7);
    private static readonly Color Frame = new Color32(0x5A, 0x50, 0xC8, 0xFF);
    private static readonly Color Panel = new Color32(0x1C, 0x18, 0x38, 0xFF);
    private static readonly Color PanelEdge = new Color32(0x34, 0x2F, 0x66, 0xFF);
    private static readonly Color Row = new Color32(0x25, 0x21, 0x4A, 0xFF);
    private static readonly Color RowHover = new Color32(0x30, 0x2B, 0x5E, 0xFF);
    private static readonly Color Ink = new Color32(0xEC, 0xEA, 0xF7, 0xFF);
    private static readonly Color Dim = new Color32(0x9C, 0x97, 0xC4, 0xFF);
    private static readonly Color Green = new Color32(0x3D, 0xDC, 0x97, 0xFF);
    private static readonly Color Red = new Color32(0xE5, 0x62, 0x6A, 0xFF);
    private static readonly Color Orange = new Color32(0xF2, 0xA9, 0x3B, 0xFF);
    private static readonly Color Blue = new Color32(0x4A, 0x55, 0xC8, 0xFF);
    private static readonly Color Violet = new Color32(0x8B, 0x6B, 0xD8, 0xFF);
    private const string GreenHex = "#3DDC97", DimHex = "#9C97C4";

    private enum MineFilter { All, Enabled, Disabled }

    private sealed class StoreRowView
    {
        public GameObject Root;
        public Image Icon;
        public TextMeshProUGUI Title, By, Text, Downloads, Likes, Age, TagText;
        public Image Tag;
        public UiKit.Chip Action;
        public Package Package;
    }

    private sealed class MineRowView
    {
        public GameObject Root;
        public Image Icon, Badge;
        public TextMeshProUGUI Title, By, BadgeText;
        public UiKit.Chip Toggle, Remove, Update;
        public InstalledMod Mod;
    }

    private static ModManagerWindow _instance;
    public static bool IsOpen => _instance != null && _instance._canvas != null && _instance._canvas.gameObject.activeSelf;

    private Canvas _canvas;
    private RectTransform _window, _storeContent, _mineContent, _categoryHolder;
    private ScrollRect _storeScroll, _mineScroll;
    private TextMeshProUGUI _subtitle, _storeCount, _storeAge, _mineCount, _status, _storeEmpty;
    private Image _restartPill;
    private TextMeshProUGUI _restartPillText;
    private TMP_InputField _search;
    private UiKit.Chip _sortChip, _restartChip;
    private readonly Dictionary<string, UiKit.Chip> _categoryChips = new();
    private readonly Dictionary<MineFilter, UiKit.Chip> _mineChips = new();
    private readonly List<StoreRowView> _storeRows = new();
    private readonly List<MineRowView> _mineRows = new();
    private GameObject _card;
    private int _language = -1, _drawn = -1;
    private bool _dirty = true;
    private string _query = "", _category = "";
    private ThunderstoreCatalog.Sort _sort = ThunderstoreCatalog.Sort.Updated;
    private MineFilter _mineFilter = MineFilter.All;
    private float _nextDraw;

    // ---------------------------------------------------------------- open / close

    public static void Open()
    {
        if (_instance == null)
        {
            var go = new GameObject("DamageInsight_ModManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ModManagerWindow>();
        }
        _instance.Show();
    }

    private void Show()
    {
        if (_canvas != null && _language != Loc.Version)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
        }
        if (_canvas == null)
            Build();
        _canvas.gameObject.SetActive(true);
        _dirty = true;
        EventSystem.current?.SetSelectedGameObject(null);
        ModService.Rescan();
        ModService.Instance?.RefreshList();
        MenuSounds.Play(MenuSounds.Kind.Select);
    }

    private void Close()
    {
        CloseCard();
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
        if (_search != null)
            _search.DeactivateInputField();
        MenuSounds.Play(MenuSounds.Kind.Close);
    }

    /// <summary>Esc / Cancel (PauseReturnPatch): closes the card, then the window. False when the window isn't open.</summary>
    internal static bool HandleBack()
    {
        if (!IsOpen)
            return false;
        if (_instance._card != null)
            _instance.CloseCard();
        else
            _instance.Close();
        return true;
    }

    private void Update()
    {
        if (!IsOpen)
            return;
        if ((_dirty || _drawn != ModService.Version) && Time.unscaledTime >= _nextDraw)
        {
            _dirty = false;
            _drawn = ModService.Version;
            _nextDraw = Time.unscaledTime + 0.15f;
            Patches.Guard.Run("Mod manager", Draw);
        }
    }

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _language = Loc.Version;
        _storeRows.Clear();
        _mineRows.Clear();
        _categoryChips.Clear();
        _mineChips.Clear();
        _canvas = UiKit.OverlayCanvas("DamageInsight_ModManager", 2150);
        _canvas.gameObject.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null)
        {
            var events = new GameObject("DamageInsight_EventSystem");
            DontDestroyOnLoad(events);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        var shade = UiKit.Image("Shade", _canvas.transform, new Color(0, 0, 0, 0.72f));
        shade.rectTransform.Fill();
        var window = UiKit.Image("Window", _canvas.transform, Back);
        _window = window.rectTransform;
        _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
        _window.sizeDelta = new Vector2(Width, Height);
        Outline(window, Frame, 2.5f);

        // Header: icon, title, what this is; the restart pill and Close on the right.
        var icon = UiKit.Image("Icon", _window, Color.white, PixelIcons.Get("chest"));
        icon.preserveAspect = true;
        icon.rectTransform.Place(24, 16, 48, 48);
        Text(_window, $"<b>{Loc.T("Mods")}</b>", 34, Ink).rectTransform.Place(84, 12, 400, 44);
        _subtitle = Text(_window, "", 15, Dim);
        _subtitle.rectTransform.Place(86, 52, 1000, 22);
        _restartPill = UiKit.Image("Restart pill", _window, Orange);
        _restartPill.rectTransform.Place(Width - 24 - 56 - 12 - 260, 22, 260, 36);
        _restartPillText = Text(_restartPill.transform, Loc.T("Restart needed"), 17, new Color32(0x26, 0x18, 0x06, 0xFF), TextAlignmentOptions.Center);
        _restartPillText.rectTransform.Fill();
        var pillButton = _restartPill.gameObject.AddComponent<Button>();
        pillButton.transition = Selectable.Transition.None;
        pillButton.onClick.AddListener(AskRestart);
        var close = new UiKit.Chip(_window, "X", Width - 24 - 56, 22, 56, Close, Red);
        close.Background.rectTransform.sizeDelta = new Vector2(56, 36);
        close.ClickSound = false;
        close.SetOn(true);

        BuildStore(PanelAt("Thunderstore", 20, 84, LeftWidth, Height - 84 - 96));
        BuildMine(PanelAt("Installed", 20 + LeftWidth + Gap, 84, RightWidth, Height - 84 - 96));
        BuildBottom();
    }

    private RectTransform PanelAt(string name, float x, float y, float w, float h)
    {
        var panel = UiKit.Image(name, _window, Panel);
        panel.rectTransform.Place(x, y, w, h);
        Outline(panel, PanelEdge, 1.5f);
        return panel.rectTransform;
    }

    private void BuildStore(RectTransform panel)
    {
        float w = LeftWidth;
        var storeTitle = Text(panel, "Thunderstore", 24, Ink);
        storeTitle.rectTransform.Place(16, 8, 300, 38);
        NoWrap(storeTitle);
        float titleWidth = storeTitle.GetPreferredValues(storeTitle.text).x;
        _storeCount = Text(panel, "", 16, Dim);
        _storeCount.rectTransform.Place(16 + titleWidth + 14, 16, 300, 26);
        _storeAge = Text(panel, "", 14, Dim, TextAlignmentOptions.TopRight);
        _storeAge.rectTransform.Place(w - 16 - 110 - 10 - 420, 16, 420, 24);
        var refresh = new UiKit.Chip(panel, Loc.T("Refresh"), w - 16 - 110, 12, 110, () => ModService.Instance?.RefreshList(force: true));
        refresh.SetOn(false);

        // Search and sorting
        _search = SearchField(panel, 16, 50, 640, 38);
        _search.onValueChanged.AddListener(v =>
        {
            _query = v ?? "";
            _dirty = true;
            _storeScroll.verticalNormalizedPosition = 1f;
        });
        float sortWidth = Enum.GetValues(typeof(ThunderstoreCatalog.Sort)).Cast<ThunderstoreCatalog.Sort>().Max(s => UiKit.ChipWidth(SortLabel(s))) + 20;
        _sortChip = new UiKit.Chip(panel, SortLabel(_sort), w - 16 - sortWidth, 55, sortWidth, () =>
        {
            _sort = (ThunderstoreCatalog.Sort)(((int)_sort + 1) % Enum.GetValues(typeof(ThunderstoreCatalog.Sort)).Length);
            _sortChip.Label.text = SortLabel(_sort);
            _dirty = true;
            _storeScroll.verticalNormalizedPosition = 1f;
        }, Blue);
        _sortChip.SetOn(true);

        // Categories (filled when the list is there)
        _categoryHolder = UiKit.Rect("Categories", panel);
        _categoryHolder.Place(16, 100, w - 32, 30);

        var frame = UiKit.Image("List", panel, new Color(0, 0, 0, 0.18f)).rectTransform.Place(12, 138, w - 24, Height - 84 - 96 - 138 - 12);
        _storeContent = ScrollList(frame, out _storeScroll);
        _storeEmpty = Text(frame, "", 20, Dim, TextAlignmentOptions.Center);
        _storeEmpty.rectTransform.Fill(40);
    }

    private void BuildMine(RectTransform panel)
    {
        float w = RightWidth;
        Text(panel, Loc.T("Your mods"), 24, Ink).rectTransform.Place(16, 10, 300, 32);
        _mineCount = Text(panel, "", 16, Dim, TextAlignmentOptions.TopRight);
        _mineCount.rectTransform.Place(w - 16 - 260, 15, 260, 26);
        float x = 16;
        foreach (MineFilter filter in Enum.GetValues(typeof(MineFilter)))
        {
            string label = filter switch { MineFilter.Enabled => Loc.T("Enabled"), MineFilter.Disabled => Loc.T("Disabled"), _ => Loc.T("All") };
            float cw = UiKit.ChipWidth(label);
            var chip = new UiKit.Chip(panel, label, x, 55, cw, () =>
            {
                _mineFilter = filter;
                foreach (var pair in _mineChips)
                    pair.Value.SetOn(pair.Key == _mineFilter);
                _dirty = true;
            });
            chip.SetOn(filter == _mineFilter);
            _mineChips[filter] = chip;
            x += cw + 6;
        }
        var frame = UiKit.Image("List", panel, new Color(0, 0, 0, 0.18f)).rectTransform.Place(12, 96, w - 24, Height - 84 - 96 - 96 - 12);
        _mineContent = ScrollList(frame, out _mineScroll);
    }

    private void BuildBottom()
    {
        float y = Height - 76;
        float x = 20;
        foreach (var (label, on) in ModService.CanChange ? new[] { (Loc.T("Turn all on"), true), (Loc.T("Turn all off"), false) } : new (string, bool)[0])
        {
            float cw = UiKit.ChipWidth(label) + 24;
            var chip = new UiKit.Chip(_window, label, x, y + 8, cw, () => SetAll(on));
            chip.Background.rectTransform.sizeDelta = new Vector2(cw, 40);
            x += cw + 8;
        }
        _status = Text(_window, "", 16, Dim);
        _status.rectTransform.Place(x + 12, y + 14, 700, 28);
        _status.enableWordWrapping = false;
        _status.overflowMode = TextOverflowModes.Ellipsis;

        float right = Width - 20;
        var closeLabel = Loc.T("Close");
        float closeWidth = UiKit.ChipWidth(closeLabel) + 30;
        var close = new UiKit.Chip(_window, closeLabel, right - closeWidth, y + 8, closeWidth, Close);
        close.Background.rectTransform.sizeDelta = new Vector2(closeWidth, 40);
        close.ClickSound = false;
        right -= closeWidth + 8;
        string restart = Loc.T("Restart game");
        float restartWidth = UiKit.ChipWidth(restart) + 40;
        _restartChip = new UiKit.Chip(_window, restart, right - restartWidth, y + 8, restartWidth, AskRestart, Orange);
        _restartChip.Background.rectTransform.sizeDelta = new Vector2(restartWidth, 40);
        _restartChip.Background.gameObject.SetActive(ModService.CanChange);
        if (ModService.CanChange)
            right -= restartWidth + 8;
        string folder = Loc.T("Open mods folder");
        float folderWidth = UiKit.ChipWidth(folder) + 24;
        var open = new UiKit.Chip(_window, folder, right - folderWidth, y + 8, folderWidth, ModService.OpenPluginsFolder);
        open.Background.rectTransform.sizeDelta = new Vector2(folderWidth, 40);
        _status.rectTransform.sizeDelta = new Vector2(right - folderWidth - 16 - (x + 12), 28);
    }

    // ---------------------------------------------------------------- drawing

    private void Draw()
    {
        string manager = ModService.Manager;
        _subtitle.text = !ModService.CanChange
            ? Loc.T("Thunderstore's Skul mods and yours. Install, update and turn mods on or off with your mod manager (r2modman, Gale).")
            : manager != null && ModService.ManagerListKept
            ? Loc.F("The game runs from your {0} profile: what you change here goes into {0}'s mod list too.", manager)
            : manager != null
            ? $"<color=#F2A93B>{Loc.F("The game runs from a {0} profile: installs from here go past {0}'s own list.", manager)}</color>"
            : Loc.T("Thunderstore's Skul mods, installed into BepInEx/plugins the way mod managers do. Changes need a restart of the game.");
        bool restartNeeded = ModService.CanChange && ModService.AnyRestartNeeded;
        _restartPill.gameObject.SetActive(restartNeeded);
        _restartChip.SetOn(restartNeeded);
        _status.text = ModService.Busy || ModService.Status.Length > 0 ? ModService.Status
            : Daily.DailyService.Enabled ? Loc.T("Daily runs only count without other mods.") : "";
        DrawCategories();
        DrawStore();
        DrawMine();
    }

    private void DrawCategories()
    {
        var holder = _categoryHolder;
        if (holder == null || _categoryChips.Count > 0 && ModService.Packages.Count == 0)
            return;
        var categories = ThunderstoreCatalog.Categories(ModService.Packages);
        if (_categoryChips.Count == categories.Count + 1)
        {
            foreach (var pair in _categoryChips)
                pair.Value.SetOn(pair.Key == _category);
            return;
        }
        foreach (Transform child in holder)
            Destroy(child.gameObject);
        _categoryChips.Clear();
        float x = 0;
        foreach (var category in new[] { "" }.Concat(categories))
        {
            string label = category.Length == 0 ? Loc.T("All") : CategoryLabel(category);
            float cw = UiKit.ChipWidth(label);
            string chosen = category;
            var chip = new UiKit.Chip(holder, label, x, 0, cw, () =>
            {
                _category = chosen;
                _dirty = true;
                _storeScroll.verticalNormalizedPosition = 1f;
            }, Green);
            chip.SetOn(category == _category);
            _categoryChips[category] = chip;
            x += cw + 6;
        }
    }

    private void DrawStore()
    {
        var shown = ThunderstoreCatalog.Filter(ModService.Packages, _query, _category, _sort);
        _storeCount.text = ModService.Packages.Count == 0 ? "" : Loc.P("{0} mod", "{0} mods", shown.Count);
        _storeAge.text = ModService.ListLoading ? Loc.T("Asking Thunderstore…")
            : ModService.ListFetched == default ? "" : Loc.F("List from {0}", RunPages.Date(ModService.ListFetched));
        _storeEmpty.text = shown.Count > 0 ? "" : ModService.ListLoading ? Loc.T("Asking Thunderstore…")
            : ModService.Packages.Count == 0 ? Loc.T("Thunderstore didn't answer. Are you online?") : Loc.T("No mod matches.");
        while (_storeRows.Count < shown.Count)
            _storeRows.Add(NewStoreRow());
        for (int i = 0; i < _storeRows.Count; i++)
        {
            var row = _storeRows[i];
            row.Root.SetActive(i < shown.Count);
            if (i < shown.Count)
                PaintStoreRow(row, shown[i]);
        }
    }

    private void PaintStoreRow(StoreRowView row, Package p)
    {
        row.Package = p;
        var latest = p.Latest;
        var icon = ModService.IconOf(p.FullName, latest.Icon);
        row.Icon.sprite = icon ?? PixelIcons.Get("chest");
        row.Icon.color = icon == null ? new Color(1, 1, 1, 0.25f) : Color.white;
        row.Title.text = $"{p.Title}  <size=70%><color={DimHex}>v{latest.Version}</color></size>";
        row.By.text = Loc.F("by {0}", $"<color={GreenHex}>{p.Owner}</color>");
        // The categories as a small tag after the author (Thunderstore's look).
        string cats = string.Join(" · ", p.Categories.Select(CategoryLabel));
        row.Tag.gameObject.SetActive(cats.Length > 0);
        if (cats.Length > 0)
        {
            row.TagText.text = cats;
            float byWidth = Mathf.Min(row.By.GetPreferredValues(row.By.text).x, row.By.rectTransform.sizeDelta.x);
            float tagWidth = row.TagText.GetPreferredValues(cats).x + 16;
            row.Tag.rectTransform.Place(116 + byWidth + 12, 42, tagWidth, 20);
        }
        row.Text.text = latest.Description;
        row.Downloads.text = RunPages.Short(p.Downloads);
        row.Likes.text = p.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture);
        row.Age.text = Ago(p.Updated);

        bool own = p.FullName.Equals(ThunderstoreCatalog.OwnPackage, StringComparison.OrdinalIgnoreCase);
        string installed = ModService.InstalledVersion(p);
        if (ThunderstoreCatalog.IsLoader(p.FullName))
            Action(row.Action, Loc.T("Mod loader"), PanelEdge, false);
        else if (!ModService.CanChange)
            // Thunderstore's build: nothing is installed from here; the button opens the details and the mod's page.
            Action(row.Action, own ? Loc.T("This mod") : installed != null ? Loc.T("Installed") : Loc.T("Thunderstore page"),
                own ? Violet : installed != null ? (ModService.UpdateAvailable(p) ? Orange : PanelEdge) : Blue, true);
        else if (own && ModService.SelfUpdated != null)
            Action(row.Action, Loc.T("Restart needed"), Orange, false);
        else if (own)
            Action(row.Action, ModService.UpdateAvailable(p) ? Loc.T("Update") : Loc.T("This mod"), ModService.UpdateAvailable(p) ? Green : Violet,
                ModService.UpdateAvailable(p) && !ModService.SelfUpdating);
        else if (installed != null)
            Action(row.Action, ModService.UpdateAvailable(p) ? Loc.T("Update") : Loc.T("Installed"), ModService.UpdateAvailable(p) ? Orange : PanelEdge,
                ModService.UpdateAvailable(p) && !ModService.Busy);
        else
            Action(row.Action, Loc.T("Install"), Green, !ModService.Busy);
    }

    private static void Action(UiKit.Chip chip, string label, Color color, bool clickable)
    {
        chip.Label.text = label;
        chip.SetColor(clickable ? color : color * new Color(0.75f, 0.75f, 0.75f, 0.9f),
            clickable && (color.r * 0.3f + color.g * 0.59f + color.b * 0.11f) > 0.5f ? new Color32(0x12, 0x10, 0x22, 0xFF) : Ink);
        chip.Background.GetComponent<Button>().interactable = clickable;
    }

    private void DrawMine()
    {
        var mods = ModService.Installed.Where(m => _mineFilter switch
        {
            MineFilter.Enabled => m.Enabled && !m.Removed,
            MineFilter.Disabled => !m.Enabled || m.Removed,
            _ => true,
        }).OrderByDescending(ModService.IsThisMod).ThenBy(m => m.Removed).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToList();
        int on = ModService.Installed.Count(m => m.Enabled && !m.Removed);
        _mineCount.text = Loc.F("{0} on · {1} off", on, ModService.Installed.Count - on);
        while (_mineRows.Count < mods.Count)
            _mineRows.Add(NewMineRow());
        for (int i = 0; i < _mineRows.Count; i++)
        {
            var row = _mineRows[i];
            row.Root.SetActive(i < mods.Count);
            if (i < mods.Count)
                PaintMineRow(row, mods[i]);
        }
    }

    private void PaintMineRow(MineRowView row, InstalledMod mod)
    {
        row.Mod = mod;
        var package = ModService.PackageOf(mod);
        bool self = ModService.IsThisMod(mod);
        string iconFile = mod.Folder.Length > 0 ? System.IO.Path.Combine(mod.Folder, "icon.png") : null;
        var icon = ModService.IconOf(package?.FullName ?? mod.Id, package?.Latest.Icon, iconFile);
        row.Icon.sprite = icon ?? PixelIcons.Get("chest");
        row.Icon.color = icon == null ? new Color(1, 1, 1, 0.25f) : mod.Enabled && !mod.Removed ? Color.white : new Color(1, 1, 1, 0.45f);

        string version = mod.Version.Length > 0 ? mod.Version : ModService.PluginsOf(mod).Select(p => p.Metadata.Version.ToString()).FirstOrDefault() ?? "";
        string title = self ? "SkulInsight QoL" : package?.Title ?? mod.Title;
        row.Title.text = title + (version.Length > 0 ? $"  <size=75%><color={DimHex}>v{version}</color></size>" : "");
        row.Title.color = mod.Enabled && !mod.Removed ? Ink : Dim;
        var plugins = ModService.PluginsOf(mod);
        string owner = package?.Owner ?? mod.Owner;
        row.By.text = (owner.Length > 0 ? Loc.F("by {0}", $"<color={GreenHex}>{owner}</color>") : Loc.T("added by hand"))
                      + (plugins.Count > 1 ? $"  <color={DimHex}>· {Loc.P("{0} plugin", "{0} plugins", plugins.Count)}</color>" : "");

        // The badge: what the game runs now and what the next start brings.
        bool restart = ModService.NeedsRestart(mod);
        (string text, Color color) badge = mod.Removed ? (Loc.T("Removed: restart needed"), Orange)
            : self && ModService.SelfUpdated != null ? (Loc.F("Updated to {0}: restart needed", ModService.SelfUpdated), Orange)
            : self ? (Loc.T("This mod"), Violet)
            : restart && mod.Enabled ? (Loc.T("On: restart needed"), Orange)
            : restart ? (Loc.T("Off: restart needed"), Orange)
            : mod.Enabled ? (Loc.T("Enabled"), Green)
            : (Loc.T("Disabled"), Red);
        row.BadgeText.text = badge.text;
        row.Badge.color = badge.color;
        float bw = row.BadgeText.GetPreferredValues(row.BadgeText.text).x + 24;
        row.Badge.rectTransform.sizeDelta = new Vector2(bw, 24);

        bool canChange = ModService.CanChange && !self && !mod.Removed && !ModService.Busy;
        row.Toggle.Background.gameObject.SetActive(ModService.CanChange && !self && !mod.Removed && mod.HasDlls);
        row.Remove.Background.gameObject.SetActive(ModService.CanChange && !self && !mod.Removed);
        Action(row.Toggle, mod.Enabled ? Loc.T("Turn off") : Loc.T("Turn on"), mod.Enabled ? PanelEdge : Green, canChange);
        Action(row.Remove, Loc.T("Remove"), Red, canChange);
        bool update = ModService.CanChange && package != null && !self && ModService.UpdateAvailable(package);
        row.Update.Background.gameObject.SetActive(update && !mod.Removed);
        Action(row.Update, Loc.T("Update"), Orange, update && !ModService.Busy);
    }

    // ---------------------------------------------------------------- rows

    private StoreRowView NewStoreRow()
    {
        float w = LeftWidth - 24 - 8 - 12;
        var bg = UiKit.Image("Package", _storeContent, Row);
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = StoreRow;
        var row = new StoreRowView { Root = bg.gameObject };
        var t = bg.transform;
        row.Icon = IconFrame(t, 12, 12, 88);
        row.Title = Text(t, "", 22, Ink);
        row.Title.rectTransform.Place(116, 8, w - 116 - 350, 32);
        NoWrap(row.Title);
        row.By = Text(t, "", 15, Dim);
        row.By.rectTransform.Place(116, 40, w - 116 - 520, 26);
        NoWrap(row.By);
        row.Tag = UiKit.Image("Tag", t, PanelEdge);
        row.Tag.raycastTarget = false;
        row.TagText = Text(row.Tag.transform, "", 13, Ink, TextAlignmentOptions.Center);
        row.TagText.rectTransform.Fill();
        NoWrap(row.TagText);
        row.Text = Text(t, "", 15, new Color32(0xC8, 0xC4, 0xE4, 0xFF));
        row.Text.rectTransform.Place(116, 62, w - 116 - 200, 44);
        row.Text.overflowMode = TextOverflowModes.Ellipsis;

        // Downloads, likes and age at the top right, the action under them.
        float sx = w - 340;
        row.Downloads = Stat(t, "download", sx, 12, 70);
        row.Likes = Stat(t, "thumb", sx + 100, 12, 50);
        row.Age = Stat(t, "hourglass", sx + 176, 12, 140);
        row.Action = new UiKit.Chip(t, "", w - 12 - 170, 64, 170, () => StoreAction(row.Package));
        row.Action.Background.rectTransform.sizeDelta = new Vector2(170, 36);
        row.Action.Label.fontSize = 17;

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => ShowDetails(row.Package));
        UiKit.OnHover(bg.gameObject, on => bg.color = on ? RowHover : Row);
        return row;
    }

    private MineRowView NewMineRow()
    {
        float w = RightWidth - 24 - 8 - 12;
        var bg = UiKit.Image("Mod", _mineContent, Row);
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = MineRow;
        var row = new MineRowView { Root = bg.gameObject };
        var t = bg.transform;
        row.Icon = IconFrame(t, 12, 12, 72);
        row.Title = Text(t, "", 20, Ink);
        row.Title.rectTransform.Place(98, 6, w - 98 - 140, 32);
        NoWrap(row.Title);
        row.By = Text(t, "", 14, Dim);
        row.By.rectTransform.Place(98, 34, w - 98 - 140, 26);
        NoWrap(row.By);
        row.Badge = UiKit.Image("Badge", t, Green);
        row.Badge.rectTransform.Place(98, 62, 120, 24);
        row.BadgeText = Text(row.Badge.transform, "", 13, new Color32(0x12, 0x10, 0x22, 0xFF), TextAlignmentOptions.Center);
        row.BadgeText.rectTransform.Fill();
        NoWrap(row.BadgeText);

        row.Update = new UiKit.Chip(t, "", w - 12 - 124, 6, 124, () => { if (ModService.PackageOf(row.Mod) is { } p) StoreAction(p); });
        row.Toggle = new UiKit.Chip(t, "", w - 12 - 124, 34, 124, () =>
        {
            if (row.Mod != null)
                ModService.SetEnabled(row.Mod, !row.Mod.Enabled);
        });
        row.Remove = new UiKit.Chip(t, "", w - 12 - 124, 62, 124, () => AskRemove(row.Mod));
        foreach (var chip in new[] { row.Update, row.Toggle, row.Remove })
        {
            chip.Label.fontSize = 14;
            chip.Background.rectTransform.sizeDelta = new Vector2(124, 26);
        }
        UiKit.OnHover(bg.gameObject, on => bg.color = on ? RowHover : Row);
        return row;
    }

    private static Image IconFrame(Transform parent, float x, float y, float size)
    {
        var frame = UiKit.Image("Icon frame", parent, new Color32(0x0F, 0x0C, 0x22, 0xFF));
        frame.rectTransform.Place(x, y, size, size);
        frame.raycastTarget = false;
        Outline(frame, PanelEdge, 1.5f);
        var icon = UiKit.Image("Icon", frame.transform, Color.white);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.rectTransform.Fill(2);
        return icon;
    }

    private static TextMeshProUGUI Stat(Transform parent, string pixel, float x, float y, float width)
    {
        var icon = UiKit.Image("Stat icon", parent, Dim, PixelIcons.Get(pixel));
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.rectTransform.Place(x, y + 3, 18, 18);
        var label = Text(parent, "", 15, Dim);
        label.rectTransform.Place(x + 24, y, width, 24);
        NoWrap(label);
        return label;
    }

    // ---------------------------------------------------------------- actions

    private void StoreAction(Package package)
    {
        if (package == null || ModService.Busy)
            return;
        if (!ModService.CanChange)
        {
            ShowDetails(package); // Thunderstore's build: details and the mod's page only
            return;
        }
        if (package.FullName.Equals(ThunderstoreCatalog.OwnPackage, StringComparison.OrdinalIgnoreCase))
        {
            // SkulInsight updates itself (or, in a developer build / a mod manager's profile, says why not).
            if (ModService.WhyNoSelfUpdate() == null)
                ModService.Instance?.UpdateSelfNow();
            else
                ShowCard("SkulInsight QoL", ModService.WhyNoSelfUpdate(), null,
                    (Loc.T("Thunderstore page"), Blue, () => Application.OpenURL(package.Url)), (Loc.T("Close"), PanelEdge, CloseCard));
            return;
        }
        if (ThunderstoreCatalog.IsLoader(package.FullName))
            return;
        var missing = new List<string>();
        var order = ModService.InstallOrder(package, missing);
        bool update = ModService.InstalledVersion(package) != null;
        bool warn = ModService.Manager != null && !ModService.ManagerListKept;
        if (order.Count <= 1 && missing.Count == 0 && !warn)
        {
            ModService.Instance?.Install(package);
            return;
        }
        // Several packages, unknown dependencies or a mod manager's profile: say what happens first.
        var text = new System.Text.StringBuilder();
        if (warn)
            text.Append($"<color=#F2A93B>{Loc.F("The game runs from a {0} profile: installs from here go past {0}'s own list.", ModService.Manager)}</color>\n\n");
        if (order.Count > 1)
            text.Append(Loc.T("It needs these mods too, they come along:")).Append('\n')
                .Append(string.Join("\n", order.Take(order.Count - 1).Select(p => $"» {p.Title} {p.Latest.Version} <color={DimHex}>{Loc.F("by {0}", p.Owner)}</color>"))).Append("\n\n");
        if (missing.Count > 0)
            text.Append($"<color=#E5626A>{Loc.F("Not on Thunderstore's Skul list (left out): {0}", string.Join(", ", missing))}</color>\n\n");
        long size = order.Sum(p => p.Latest.FileSize);
        text.Append($"<color={DimHex}>{Loc.F("Download: {0}", Size(size))}</color>");
        ShowCard(update ? Loc.F("Update {0} to {1}?", package.Title, package.Latest.Version) : Loc.F("Install {0} {1}?", package.Title, package.Latest.Version),
            text.ToString(), ModService.IconOf(package.FullName, package.Latest.Icon),
            (update ? Loc.T("Update") : Loc.T("Install"), Green, () => { CloseCard(); ModService.Instance?.Install(package); }),
            (Loc.T("Cancel"), PanelEdge, CloseCard));
    }

    private void AskRemove(InstalledMod mod)
    {
        if (mod == null || ModService.Busy)
            return;
        ShowCard(Loc.F("Remove {0}?", ModService.PackageOf(mod)?.Title ?? mod.Title),
            Loc.T("Its files go to the backup folder (BepInEx/DamageInsight/Mods/Backup), its settings in BepInEx/config stay, and mods it needed stay too. It is gone after a restart."),
            null, (Loc.T("Remove"), Red, () => { CloseCard(); ModService.Remove(mod); }), (Loc.T("Cancel"), PanelEdge, CloseCard));
    }

    private void AskRestart()
    {
        string why = ModService.CannotRestart();
        if (why != null)
        {
            ShowCard(Loc.T("Restart game"), why, null, (Loc.T("OK"), PanelEdge, CloseCard));
            return;
        }
        ShowCard(Loc.T("Restart the game now?"),
            Loc.T("The game closes and Steam starts it again, with your mods as they are now. A run in progress continues from the start of its map."),
            null, (Loc.T("Restart game"), Orange, () => { CloseCard(); ModService.Restart(); }), (Loc.T("Cancel"), PanelEdge, CloseCard));
    }

    private void SetAll(bool on)
    {
        if (ModService.Busy)
            return;
        foreach (var mod in ModService.Installed.Where(m => !ModService.IsThisMod(m) && !m.Removed && m.HasDlls && m.Enabled != on).ToList())
            ModService.SetEnabled(mod, on);
    }

    private void ShowDetails(Package package)
    {
        if (package == null)
            return;
        var latest = package.Latest;
        var text = new System.Text.StringBuilder();
        text.Append(Loc.F("by {0}", $"<color={GreenHex}>{package.Owner}</color>")).Append($"  <color={DimHex}>· v{latest.Version} · ")
            .Append(Loc.P("{0} download", "{0} downloads", (int)Math.Min(int.MaxValue, package.Downloads))).Append(" · ")
            .Append(Loc.P("{0} like", "{0} likes", package.Rating)).Append(" · ").Append(Ago(package.Updated)).Append("</color>\n\n");
        text.Append(latest.Description).Append("\n\n");
        var deps = latest.Dependencies.Select(ThunderstoreCatalog.PackageOf).Where(d => !ThunderstoreCatalog.IsLoader(d)).ToList();
        if (deps.Count > 0)
            text.Append(Loc.T("Needs")).Append(": ")
                .Append(string.Join(", ", deps.Select(d => ModService.ByName.TryGetValue(d, out var p) ? p.Title : d))).Append('\n');
        if (package.Categories.Count > 0)
            text.Append($"<color={DimHex}>{string.Join(" · ", package.Categories.Select(CategoryLabel))}</color>\n");
        if (Daily.DailyService.Enabled)
            text.Append($"\n<color={DimHex}>{Loc.T("Daily runs only count without other mods.")}</color>");

        bool own = package.FullName.Equals(ThunderstoreCatalog.OwnPackage, StringComparison.OrdinalIgnoreCase);
        bool installed = ModService.InstalledVersion(package) != null;
        bool loader = ThunderstoreCatalog.IsLoader(package.FullName);
        var buttons = new List<(string, Color, Action)>();
        if (ModService.CanChange && (own ? ModService.UpdateAvailable(package) : !loader && (!installed || ModService.UpdateAvailable(package))) && !ModService.Busy)
            buttons.Add((installed ? Loc.T("Update") : Loc.T("Install"), Green, () => { CloseCard(); StoreAction(package); }));
        buttons.Add((Loc.T("Thunderstore page"), Blue, () => Application.OpenURL(package.Url)));
        buttons.Add((Loc.T("Close"), PanelEdge, CloseCard));
        ShowCard(package.Title, text.ToString(), ModService.IconOf(package.FullName, latest.Icon), buttons.ToArray());
    }

    // ---------------------------------------------------------------- the card

    private void ShowCard(string title, string body, Sprite icon, params (string label, Color color, Action click)[] buttons)
    {
        CloseCard();
        var scrim = UiKit.Image("Card shade", _window, new Color(0, 0, 0, 0.55f));
        scrim.rectTransform.Fill();
        _card = scrim.gameObject;
        var card = UiKit.Image("Card", scrim.transform, Back);
        var rect = card.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(860, 560);
        Outline(card, Frame, 2.5f);
        float textX = 28;
        if (icon != null)
        {
            var image = IconFrame(rect, 28, 28, 128);
            image.sprite = icon;
            textX = 28 + 128 + 24;
        }
        var head = Text(rect, title, 30, Ink);
        head.rectTransform.Place(textX, 26, 860 - textX - 28, 80);
        head.overflowMode = TextOverflowModes.Ellipsis;

        var frame = UiKit.Rect("Text", rect).Place(textX, 100, 860 - textX - 28, 560 - 100 - 90);
        var content = ScrollList(frame, out _);
        Text(content, body, 17, new Color32(0xD6, 0xD3, 0xEE, 0xFF));

        float x = 860 - 28;
        foreach (var (text, color, click) in Enumerable.Reverse(buttons))
        {
            float width = UiKit.ChipWidth(text) + 40;
            x -= width;
            var chip = new UiKit.Chip(rect, text, x, 560 - 28 - 44, width, () => click(), color);
            chip.Background.rectTransform.sizeDelta = new Vector2(width, 44);
            chip.SetOn(true);
            chip.Label.fontSize = 18;
            x -= 10;
        }
    }

    private void CloseCard()
    {
        if (_card != null)
            Destroy(_card);
        _card = null;
    }

    // ---------------------------------------------------------------- helpers

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft) => UiKit.Text("Text", parent, text, size, alignment, color);

    private static void NoWrap(TextMeshProUGUI label)
    {
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void Outline(Image image, Color color, float distance)
    {
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private static RectTransform ScrollList(RectTransform frame, out ScrollRect scroll)
    {
        var viewport = UiKit.Rect("Viewport", frame).Fill(4);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // catches the mouse wheel
        var content = UiKit.Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 8;
        layout.padding = new RectOffset(4, 8, 4, 4);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll = frame.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45f;
        return content;
    }

    private static TMP_InputField SearchField(RectTransform parent, float x, float y, float w, float h)
    {
        var box = UiKit.Image("Search", parent, new Color32(0x0F, 0x0C, 0x22, 0xFF));
        box.rectTransform.Place(x, y, w, h);
        Outline(box, PanelEdge, 1.5f);
        var area = UiKit.Rect("Text Area", box.transform).Fill(8);
        area.gameObject.AddComponent<RectMask2D>();
        var text = UiKit.Text("Text", area, "", 19, TextAlignmentOptions.MidlineLeft, Ink);
        text.rectTransform.Fill();
        text.enableWordWrapping = false;
        var placeholder = UiKit.Text("Placeholder", area, Loc.T("Search mods…"), 19, TextAlignmentOptions.MidlineLeft, Dim);
        placeholder.rectTransform.Fill();
        placeholder.fontStyle = FontStyles.Italic;
        var input = box.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = text.font;
        input.pointSize = 19;
        input.caretColor = Ink;
        input.selectionColor = new Color(0.35f, 0.3f, 0.8f, 0.5f);
        return input;
    }

    private static string SortLabel(ThunderstoreCatalog.Sort sort) => sort switch
    {
        ThunderstoreCatalog.Sort.Downloads => Loc.T("Sort: most downloaded"),
        ThunderstoreCatalog.Sort.Rating => Loc.T("Sort: most liked"),
        ThunderstoreCatalog.Sort.Name => Loc.T("Sort: A–Z"),
        _ => Loc.T("Sort: last updated"),
    };

    /// <summary>Thunderstore's category names in the game's language.</summary>
    private static string CategoryLabel(string category) => category switch
    {
        "Mods" => Loc.T("Mods"),
        "Libraries" => Loc.T("Libraries"),
        "Tools" => Loc.T("Tools"),
        "Misc" => Loc.T("Misc"),
        "Audio" => Loc.T("Audio"),
        "Modpacks" => Loc.T("Modpacks"),
        _ => category,
    };

    /// <summary>"today", "3 days ago", "2 weeks ago", "5 months ago", "3 years ago".</summary>
    internal static string Ago(DateTime utc)
    {
        if (utc == DateTime.MinValue)
            return "";
        double days = (DateTime.UtcNow - utc).TotalDays;
        if (days < 1)
            return Loc.T("today");
        if (days < 7)
            return Loc.P("{0} day ago", "{0} days ago", (int)days);
        if (days < 31)
            return Loc.P("{0} week ago", "{0} weeks ago", (int)(days / 7));
        if (days < 365)
            return Loc.P("{0} month ago", "{0} months ago", (int)(days / 30.4));
        return Loc.P("{0} year ago", "{0} years ago", (int)(days / 365.25));
    }

    private static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB"
        : Math.Max(1, bytes / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " KB";
}
