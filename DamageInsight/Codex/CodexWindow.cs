using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Characters.Gear.Synergy.Inscriptions;
using DamageInsight.UI;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DamageInsight.Lang;

namespace DamageInsight.Codex;

/// <summary>
/// The Codex book (key K): two open pages. Left: search, category tabs and the entry list ("???" for what you
/// haven't discovered yet). Right: the selected entry with its portrait/icon, what you know about it and what
/// unlocks the next tier. Built from code the first time it is opened; pauses the game while open.
/// </summary>
public sealed class CodexWindow : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    /// <summary>The live instance (runs the animation capture coroutine).</summary>
    public static CodexWindow Instance { get; private set; }

    private bool _rebuild;

    private void Awake()
    {
        Instance = this;
        Loc.Changed += () => _rebuild = true; // labels are built once: build the book again in the new language
    }

    /// <summary>Builds the book again in the current language, on the same page (keeps whether it is open).</summary>
    private void Rebuild()
    {
        _rebuild = false;
        if (_canvas == null)
            return;
        bool open = IsOpen;
        if (open)
            SetOpen(false);
        Destroy(_canvas.gameObject);
        _canvas = null;
        _search = null;
        _query = "";
        _rows.Clear();
        _tabs.Clear();
        _showChips.Clear();
        _textShownFor = null;
        _animatedId = null;
        _listDirty = true;
        if (open)
            SetOpen(true);
    }

    /// <summary>True while the search field has focus (our hotkeys must not fire while typing).</summary>
    public static bool IsTyping => IsOpen && _search != null && _search.isFocused;

    // Book colours: leather cover, parchment pages, ink.
    private static readonly Color Cover = new Color32(0x3A, 0x24, 0x1A, 0xFF);
    private static readonly Color CoverEdge = new Color32(0x24, 0x15, 0x0E, 0xFF);
    private static readonly Color Page = new Color32(0xEA, 0xDD, 0xC3, 0xFF);
    private static readonly Color PageShade = new Color32(0xD9, 0xC8, 0xA8, 0xFF);
    private static readonly Color Ink = new Color32(0x4A, 0x33, 0x24, 0xFF);
    private static readonly Color InkDim = new Color32(0x8A, 0x70, 0x58, 0xFF);
    private static readonly Color Accent = new Color32(0x9C, 0x3B, 0x2A, 0xFF);
    private static readonly Color RowSelected = new Color32(0xC9, 0xB2, 0x8A, 0xFF);

    private const float BookWidth = 1560f, BookHeight = 880f, PageWidth = 740f;

    private static TMP_InputField _search;

    private Canvas _canvas;
    private TextMeshProUGUI _progressText;
    private RectTransform _listContent;
    private ScrollRect _listScroll;
    private readonly List<Row> _rows = new();
    private readonly Dictionary<CodexCategory, UiKit.Chip> _tabs = new();

    private Image _portrait;
    private TextMeshProUGUI _portraitMark, _title, _subtitle, _nextText, _stats, _description;
    private Image _barFill;
    private ScrollRect _descriptionScroll;

    private enum ShowFilter { All, Found, Unknown }
    private enum SortOrder { Book, Name, Kills }

    private ShowFilter _show = ShowFilter.All;
    private SortOrder _sort = SortOrder.Book;
    private readonly Dictionary<ShowFilter, UiKit.Chip> _showChips = new();
    private UiKit.Chip _sortChip;

    // Animated portrait: the clips allowed at the current tier, the one playing and its frame.
    private List<CodexClip> _clips = new();
    private int _clipCount, _clipIndex, _frame;
    private float _frameTime;
    private string _animatedId;
    private TextMeshProUGUI _clipLabel;
    private UiKit.Chip _clipPrev, _clipNext;

    private CodexCategory _category = CodexCategory.Enemies;
    private string _query = "";
    private string _selectedId;
    private List<CodexEntry> _shown = new();
    private int _drawnVersion = -1;
    private bool _listDirty = true;
    private float _nextRefresh;

    private sealed class Row
    {
        public GameObject Root;
        public Image Background, Icon;
        public TextMeshProUGUI Name, Count;
        public string Id;
    }

    private void Update()
    {
        if (_rebuild)
            DamageInsight.Patches.Guard.Run("Codex (language)", Rebuild);
        bool typing = IsTyping;
        if (!typing && !DamageInsight.UI.SettingsPage.IsOpen && Plugin.CodexKey.Value.IsDown())
            SetOpen(!IsOpen);
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            if (typing)
                EventSystem.current?.SetSelectedGameObject(null);
            else
                SetOpen(false);
        }
        // Pictures noted on kills are developed in the background all the time (about 1.5 ms per frame here,
        // the heavy part on another thread), whether the book is open or not.
        try
        {
            CodexDeveloper.Pump(1.5);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: developing failed: {e.Message}");
        }
        if (!IsOpen)
            return;

        int waiting = CodexAnimations.Waiting;
        if (waiting != _waitingShown)
        {
            _waitingShown = waiting;
            _listDirty = true;
        }

        if ((_listDirty || CodexTracker.Version != _drawnVersion) && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            _drawnVersion = CodexTracker.Version;
            _listDirty = false;
            DrawList();
            DrawDetail();
        }
        Animate();
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
                Plugin.Log.LogError($"Could not build the Codex: {e}");
                return;
            }
        }
        if (_canvas == null)
            return;
        IsOpen = open;
        _canvas.gameObject.SetActive(open);
        try
        {
            if (open)
                Chronometer.global.AttachTimeScale(this, 0f); // pause the game while reading
            else
                Chronometer.global.DetachTimeScale(this);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not (un)pause: {e.Message}");
        }
        if (open)
        {
            EnsureEventSystem();
            _listDirty = true;
            _nextRefresh = 0f;
        }
        else
        {
            CodexTracker.SaveIfNeeded(force: true);
        }
    }

    private void OnDisable()
    {
        if (IsOpen)
            SetOpen(false);
    }

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _canvas = UiKit.OverlayCanvas("DamageInsight_Codex", 5100);
        _canvas.gameObject.AddComponent<GraphicRaycaster>();

        // Dim the game behind the book; clicking next to the book does nothing.
        UiKit.Image("Dim", _canvas.transform, new Color(0, 0, 0, 0.6f)).rectTransform.Fill();

        var coverImage = UiKit.Image("Book", _canvas.transform, CoverEdge);
        var cover = coverImage.rectTransform;
        cover.anchorMin = cover.anchorMax = new Vector2(0.5f, 0.5f);
        cover.sizeDelta = new Vector2(BookWidth + 16, BookHeight + 16);
        var innerCover = UiKit.Image("Cover", cover, Cover);
        innerCover.rectTransform.Fill(8);

        var left = MakePage(cover, 24, "Left page");
        var right = MakePage(cover, BookWidth / 2 + 8 + 4, "Right page");
        ApplyGameBookArt(coverImage, innerCover, left.GetComponent<Image>(), right.GetComponent<Image>());
        UiKit.Image("Spine", cover, CoverEdge).rectTransform.Place(BookWidth / 2 + 4, 16, 8, BookHeight - 16);

        BuildLeft(left);
        BuildRight(right);
    }

    /// <summary>
    /// Uses the inventory book's own artwork: its biggest image as the book, and an image named like a page (if
    /// any) for the pages. Every candidate is logged once, so the choice can be refined. Falls back to the drawn book.
    /// </summary>
    private static void ApplyGameBookArt(Image book, Image innerCover, Image leftPage, Image rightPage)
    {
        try
        {
            var inventory = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.inventory;
            if (inventory == null)
            {
                Plugin.Log.LogInfo("Codex: inventory book not found, using the drawn book.");
                return;
            }
            var images = inventory.GetComponentsInChildren<Image>(true)
                .Where(i => i.sprite != null)
                .OrderByDescending(i => i.rectTransform.rect.width * i.rectTransform.rect.height)
                .ToList();
            foreach (var i in images.Take(20))
                Plugin.Log.LogInfo($"Codex book art candidate: '{i.name}' sprite '{i.sprite.name}' {i.rectTransform.rect.width:0}x{i.rectTransform.rect.height:0} " +
                                   $"border {i.sprite.border} type {i.type} color {i.color}");
            if (images.Count == 0)
                return;

            Use(book, images[0]);
            innerCover.color = new Color(0, 0, 0, 0); // the artwork has its own frame

            var page = images.FirstOrDefault(i => i.sprite.name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  i.sprite.name.IndexOf("paper", StringComparison.OrdinalIgnoreCase) >= 0);
            if (page != null)
            {
                Use(leftPage, page);
                Use(rightPage, page);
            }
            Plugin.Log.LogInfo($"Codex: book art '{images[0].sprite.name}', pages '{page?.sprite.name ?? "(drawn)"}'.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not use the game's book art: {e.Message}");
        }

        static void Use(Image target, Image source)
        {
            target.sprite = source.sprite;
            target.type = source.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            target.color = source.color;
        }
    }

    private static RectTransform MakePage(RectTransform cover, float x, string name)
    {
        var shade = UiKit.Image(name + " shade", cover, PageShade).rectTransform.Place(x, 20, PageWidth + 6, BookHeight - 24);
        var page = UiKit.Image(name, shade, Page).rectTransform;
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = new Vector2(0, 6);
        page.offsetMax = new Vector2(-6, 0);
        return page;
    }

    private void BuildLeft(RectTransform page)
    {
        Label(page, Loc.T("Codex"), 40, Ink, TextAlignmentOptions.TopLeft).rectTransform.Place(32, 22, 400, 50);
        _progressText = Label(page, "", 17, InkDim, TextAlignmentOptions.TopRight);
        _progressText.rectTransform.Place(PageWidth - 32 - 360, 36, 360, 26);

        // Search field
        var box = UiKit.Image("Search", page, PageShade).rectTransform.Place(32, 84, PageWidth - 64, 40);
        var area = UiKit.Rect("Text Area", box).Fill(8);
        area.gameObject.AddComponent<RectMask2D>();
        var text = Label(area, "", 20, Ink, TextAlignmentOptions.MidlineLeft);
        text.rectTransform.Fill();
        text.enableWordWrapping = false;
        var placeholder = Label(area, Loc.T("Search…  (e.g. \"recruit\", \"hope\", \"chim\")"), 20, InkDim, TextAlignmentOptions.MidlineLeft);
        placeholder.rectTransform.Fill();
        placeholder.fontStyle = FontStyles.Italic;
        _search = box.gameObject.AddComponent<TMP_InputField>();
        _search.textViewport = area;
        _search.textComponent = text;
        _search.placeholder = placeholder;
        _search.fontAsset = text.font;
        _search.pointSize = 20;
        _search.caretColor = Ink;
        _search.selectionColor = new Color(0.6f, 0.4f, 0.2f, 0.4f);
        _search.onValueChanged.AddListener(v =>
        {
            _query = v ?? "";
            _listDirty = true;
            _nextRefresh = 0f;
        });

        // Category tabs
        float x = 32;
        foreach (CodexCategory category in Enum.GetValues(typeof(CodexCategory)))
        {
            string label = TabLabel(category);
            float width = UiKit.ChipWidth(label);
            var chip = new UiKit.Chip(page, label, x, 136, width, () =>
            {
                _category = category;
                _selectedId = null;
                _listDirty = true;
                _nextRefresh = 0f;
                RefreshTabs();
                _listScroll.verticalNormalizedPosition = 1f;
            });
            _tabs[category] = chip;
            x += width + 6;
        }
        RefreshTabs();

        // Filter (all / found / unknown) and sort order
        float fx = 32;
        foreach (ShowFilter show in Enum.GetValues(typeof(ShowFilter)))
        {
            string label = show switch { ShowFilter.Found => Loc.T("Found"), ShowFilter.Unknown => Loc.T("Unknown"), _ => Loc.T("All") };
            float width = UiKit.ChipWidth(label);
            var chip = new UiKit.Chip(page, label, fx, 172, width, () =>
            {
                _show = show;
                _listDirty = true;
                _nextRefresh = 0f;
                RefreshFilterChips();
            });
            _showChips[show] = chip;
            fx += width + 6;
        }
        _sortChip = new UiKit.Chip(page, Loc.F("Sort: {0}", SortName(SortOrder.Book)), 0, 0, 150, () =>
        {
            _sort = (SortOrder)(((int)_sort + 1) % Enum.GetValues(typeof(SortOrder)).Length);
            _listDirty = true;
            _nextRefresh = 0f;
            RefreshFilterChips();
        });
        float sortWidth = Mathf.Max(150f, Enum.GetValues(typeof(SortOrder)).Cast<SortOrder>().Max(s => UiKit.ChipWidth(Loc.F("Sort: {0}", SortName(s)))));
        _sortChip.Background.rectTransform.Place(PageWidth - 32 - sortWidth, 172, sortWidth, 28);
        RefreshFilterChips();

        // Entry list
        var frame = UiKit.Image("List", page, PageShade).rectTransform.Place(32, 210, PageWidth - 64, BookHeight - 24 - 210 - 28);
        var viewport = UiKit.Rect("Viewport", frame).Fill(4);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // catches the mouse wheel
        _listContent = UiKit.Rect("Content", viewport);
        _listContent.anchorMin = new Vector2(0, 1);
        _listContent.anchorMax = new Vector2(1, 1);
        _listContent.pivot = new Vector2(0.5f, 1);
        _listContent.sizeDelta = Vector2.zero;
        var layout = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2;
        _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _listScroll = frame.gameObject.AddComponent<ScrollRect>();
        _listScroll.viewport = viewport;
        _listScroll.content = _listContent;
        _listScroll.horizontal = false;
        _listScroll.movementType = ScrollRect.MovementType.Clamped;
        _listScroll.scrollSensitivity = 40f;
    }

    /// <summary>
    /// Right page, top to bottom: name (centred title), what it is, a big picture box to about the middle of the page,
    /// the move switcher, the progress to the next tier, and a scrollable text that follows the selected move
    /// (what it does and how to deal with it; the stats while it idles).
    /// </summary>
    private void BuildRight(RectTransform page)
    {
        float width = PageWidth - 64;
        _title = Label(page, "", 34, Ink, TextAlignmentOptions.Center);
        _title.rectTransform.Place(32, 22, width, 46);
        _title.enableWordWrapping = false;
        _title.overflowMode = TextOverflowModes.Ellipsis;
        _subtitle = Label(page, "", 18, InkDim, TextAlignmentOptions.Center);
        _subtitle.rectTransform.Place(32, 70, width, 26);

        var frame = UiKit.Image("Portrait frame", page, PageShade).rectTransform.Place(32, 102, width, 330);
        _portrait = UiKit.Image("Portrait", frame, Color.white);
        _portrait.rectTransform.Fill(12);
        _portrait.preserveAspect = true;
        _portraitMark = Label(frame, "?", 150, InkDim, TextAlignmentOptions.Center);
        _portraitMark.rectTransform.Fill();
        // "Refilm" (only on films): replace this take the next time the boss does the move.
        float filmWidth = Mathf.Max(110f, UiKit.ChipWidth(Loc.T("Refilm: on")));
        _filmChip = new UiKit.Chip(frame, Loc.T("Refilm"), 0, 0, filmWidth, ToggleRefilm);
        _filmChip.Background.rectTransform.PlaceTopRight(8, 8, filmWidth, 28);
        // "Normal" / "Dark Mirror": which version of the moves to show (only when a Dark Mirror version exists).
        float modeWidth = Mathf.Max(150f, UiKit.ChipWidth(Loc.T("Dark Mirror")));
        _modeChip = new UiKit.Chip(frame, Loc.T("Normal"), 0, 0, modeWidth, ToggleMode);
        _modeChip.Background.rectTransform.Place(8, 8, modeWidth, 28);
        // "Short Hair" / "Long Hair": which member of a shared page (the Leiana sisters) is shown.
        _memberChip = new UiKit.Chip(frame, "", 0, 0, 150, ToggleMember);
        _memberChip.Background.rectTransform.Place(8, 42, 150, 28);
        // "Animations" / "Attacks (filmed)": the posed pictures or the fight films (only once there are films).
        float categoryWidth = Mathf.Max(190f, UiKit.ChipWidth(Loc.T("Attacks (filmed)")));
        _categoryChip = new UiKit.Chip(frame, Loc.T("Animations"), 0, 0, categoryWidth, ToggleCategory);
        _categoryChip.Background.rectTransform.Place(8, 330 - 8 - 28, categoryWidth, 28);

        // Move switcher: < Fist slam  2/11 >
        _clipPrev = new UiKit.Chip(page, "<", 32, 440, 40, () => SwitchClip(-1));
        _clipNext = new UiKit.Chip(page, ">", 32 + width - 40, 440, 40, () => SwitchClip(+1));
        _clipLabel = Label(page, "", 18, Ink, TextAlignmentOptions.Center);
        _clipLabel.rectTransform.Place(32 + 46, 440, width - 92, 28);
        _clipLabel.enableWordWrapping = false;
        _clipLabel.overflowMode = TextOverflowModes.Ellipsis;

        _nextText = Label(page, "", 16, Ink, TextAlignmentOptions.TopLeft);
        _nextText.rectTransform.Place(32, 476, width, 26);
        var bar = UiKit.Image("Bar", page, PageShade).rectTransform.Place(32, 502, width, 10);
        _barFill = UiKit.Image("Fill", bar, Accent);
        _barFill.rectTransform.anchorMin = Vector2.zero;
        _barFill.rectTransform.anchorMax = new Vector2(0, 1);
        _barFill.rectTransform.offsetMin = _barFill.rectTransform.offsetMax = Vector2.zero;

        _stats = Label(page, "", 1, Ink, TextAlignmentOptions.TopLeft); // unused (kept for the empty state)
        _stats.rectTransform.Place(0, 0, 0, 0);

        var textFrame = UiKit.Rect("Text", page).Place(32, 524, width, BookHeight - 24 - 524 - 24);
        var viewport = UiKit.Rect("Viewport", textFrame).Fill();
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        _description = Label(viewport, "", 18, Ink, TextAlignmentOptions.TopLeft);
        var content = _description.rectTransform;
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _descriptionScroll = textFrame.gameObject.AddComponent<ScrollRect>();
        _descriptionScroll.viewport = viewport;
        _descriptionScroll.content = content;
        _descriptionScroll.horizontal = false;
        _descriptionScroll.movementType = ScrollRect.MovementType.Clamped;
        _descriptionScroll.scrollSensitivity = 30f;
    }

    private string _textShownFor;

    /// <summary>The scrollable text: the selected move's notes, or (idle, not animated) the stats and description.</summary>
    private string DetailText(CodexEntry entry, EntryProgress p, int tier)
    {
        var clip = IsEnemy(entry) && _clipCount > 0 && _stage >= 3 ? _clips[_clipIndex] : null;
        string when = clip != null && entry.Category == CodexCategory.Bosses && tier >= 2 && Plugin.CodexMoveHints?.Value != false &&
                      MoveHints.Get(_currentKey, clip.Label) is { } hint
            ? $"\n\n<color=#9C3B2A><b>{Loc.T("When:")}</b></color> {MoveHints.Localize(hint)}"
            : "";
        if (clip != null && Hidden(clip))
            return $"<color=#8A7058>{Loc.T("Not seen yet. This move is revealed once you have seen the boss use it.")}</color>" + when;
        if (clip != null && !IsIdleLike(clip.Label, _showingFilms ? -1 : _clipIndex))
        {
            var sb = new StringBuilder();
            var move = CodexContent.Move(entry.Key, clip.Label);
            if (move != null)
            {
                sb.Append(move.Value.what);
                if (move.Value.tip.Length > 0)
                    sb.Append($"\n\n<color=#9C3B2A><b>{Loc.T("How to deal with it")}</b></color>\n").Append(move.Value.tip);
            }
            else
            {
                sb.Append($"<color=#8A7058>{Loc.T("No notes on this move yet.")}</color>");
            }
            sb.Append(when);
            float seconds = clip.Durations.Sum();
            sb.Append($"\n\n<size=85%><color=#8A7058>{Loc.P("{0} frame · {1} s", "{0} frames · {1} s", clip.Count, seconds.ToString("0.0", CultureInfo.InvariantCulture))}</color></size>");
            return sb.ToString();
        }
        string about = CodexContent.About(entry.Key);
        string text = Stats(entry, p, tier);
        string description = Description(entry, tier);
        if (about.Length > 0 && tier >= 2)
            text = about + "\n\n" + text;
        return description.Length > 0 ? text + "\n" + description : text;
    }

    private static bool IsIdleLike(string label, int index) =>
        index == 0 || System.Text.RegularExpressions.Regex.IsMatch(label, @"\b(idle|sleep|appearance|dead|cut scene)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void RefreshFilterChips()
    {
        foreach (var pair in _showChips)
            pair.Value.SetOn(pair.Key == _show);
        _sortChip.Label.text = Loc.F("Sort: {0}", SortName(_sort));
        _sortChip.SetOn(_sort != SortOrder.Book);
    }

    // ---------------------------------------------------------------- animated portrait

    /// <summary>
    /// How much of an enemy's picture is shown: 0 nothing, 1 an ink outline (met), 2 a black silhouette (killed
    /// once; bosses skip it), 3 in colour (tier 2+).
    /// </summary>
    private static int Stage(CodexEntry entry, EntryProgress p, int tier)
    {
        if (tier == 0)
            return 0;
        if (tier >= 2)
            return 3;
        return entry.Category == CodexCategory.Enemies && (p?.Kills ?? 0) > 0 ? 2 : 1;
    }

    private int _stage;
    private int _waitingShown;

    /// <summary>Which animations the tier allows: a still first frame (outline/silhouette), idle + walk, or all.</summary>
    private void SetUpAnimation(CodexEntry entry, int tier)
    {
        bool enemy = IsEnemy(entry);
        // A page showing several entries (the Leiana sisters): the chip picks whose pictures and films are shown.
        var members = CodexCatalog.MembersOf(entry);
        if (_memberIndex >= members.Count)
            _memberIndex = 0;
        var shown = members[_memberIndex];
        _memberChip.Background.gameObject.SetActive(members.Count > 1 && tier > 0);
        if (members.Count > 1)
            _memberChip.Label.text = MemberName(shown, members);
        // Normal or Dark Mirror version: the one the switch asks for, or the only one there is.
        bool hasNormal = enemy && CodexMode.HasCaptures(shown.Key, false);
        bool hasDark = enemy && CodexMode.HasCaptures(shown.Key, true);
        bool dark = hasDark && (_darkMirror || !hasNormal);
        string storage = CodexMode.StorageKey(shown.Key, dark);
        if (entry.Id != _animatedId || dark != _animatedDark || _memberIndex != _animatedMember)
        {
            _animatedId = entry.Id;
            _animatedDark = dark;
            _animatedMember = _memberIndex;
            _clipIndex = 0;
            _frame = 0;
            _frameTime = 0f;
        }
        _modeChip.Background.gameObject.SetActive(hasDark && tier > 0);
        _modeChip.Label.text = dark ? Loc.T("Dark Mirror") : Loc.T("Normal");
        _modeChip.SetOn(dark);
        var posed = enemy && tier > 0 ? new List<CodexClip>(CodexAnimations.Load(storage)) : new List<CodexClip>();
        _partClips.Clear();
        if (enemy && tier > 0)
        {
            // Pieces built twice (Pope's left and right dark crystals have the same pictures) are shown once, under the
            // name they share ("Dark Crystal").
            var pieces = PartsOfFight(entry).SelectMany(part => CodexAnimations.Load(CodexMode.StorageKey(part.Key, dark))
                .Select(clip => (part.Name, clip, id: clip.Label + "|" + CodexAnimations.SheetId(clip.File)))).ToList();
            foreach (var same in pieces.GroupBy(p => p.id))
            {
                var names = same.Select(p => p.Name).Distinct().ToList();
                _partClips.Add(PartClip(names.Count > 1 ? SharedName(names) : names[0], same.First().clip));
            }
        }
        posed.AddRange(_partClips);
        // A boss's moves the player hasn't seen yet are a black "???" (the list is complete, the moves are earned).
        _seenMoves = entry.Category == CodexCategory.Bosses && !CodexTiers.RevealAllMet
            ? new HashSet<string>(CodexTracker.Page(entry.Id)?.MovesSeen(dark) ?? Enumerable.Empty<string>())
            : null;
        var films = enemy && tier >= 3 ? CodexAnimations.Load(storage, replays: true) : new List<CodexClip>();
        _films = new HashSet<CodexClip>(films);
        // Two lists instead of one mixed one: the posed pictures, or the films of attacks (in the same move order;
        // entrances like "Appearance" aren't attacks, as in the move counter).
        bool canFilm = films.Any(f => FightRecorder.IsMove(f.Label));
        _showingFilms = _showFilms && canFilm;
        _categoryChip.Background.gameObject.SetActive(canFilm);
        _categoryChip.Label.text = _showingFilms ? Loc.T("Attacks (filmed)") : Loc.T("Animations");
        _categoryChip.SetOn(_showingFilms);
        _clips = Ordered(posed, films)
            .Where(c => _showingFilms ? _films.Contains(c) && FightRecorder.IsMove(c.Label) : !_films.Contains(c)).ToList();
        _clipCount = tier >= 3 ? _clips.Count : tier == 2 ? Math.Min(2, _clips.Count) : Math.Min(1, _clips.Count);
        if (_keepLabel != null)
        {
            // Switched category: stay on the same move if the other list has it.
            _clipIndex = Math.Max(0, _clips.FindIndex(c => c.Label == _keepLabel));
            _keepLabel = null;
        }
        if (_clipIndex >= _clipCount)
            _clipIndex = 0;
        _currentKey = storage;

        // On a film: the "Refilm" box marks it for a new take the next time the boss does this move.
        bool onFilm = _stage >= 3 && _clipCount > 0 && _films.Contains(_clips[_clipIndex]);
        _filmChip.Background.gameObject.SetActive(onFilm);
        if (onFilm)
        {
            bool marked = CodexAnimations.Refilm(CodexAnimations.ReplayFolderOf(storage)).Contains(_clips[_clipIndex].Label);
            _filmChip.Label.text = marked ? Loc.T("Refilm: on") : Loc.T("Refilm");
            _filmChip.SetOn(marked);
        }

        bool switchable = _clipCount > 1;
        _clipPrev.Background.gameObject.SetActive(switchable);
        _clipNext.Background.gameObject.SetActive(switchable);
        if (_clipCount == 0)
            _clipLabel.text = "";
        else if (_stage == 1)
            _clipLabel.text = entry.Category == CodexCategory.Bosses
                ? $"<color=#8A7058>{Loc.T("Defeat it to see it in colour")}</color>"
                : $"<color=#8A7058>{Loc.T("Kill it to see its silhouette")}</color>";
        else if (_stage == 2)
            _clipLabel.text = $"<color=#8A7058>{Loc.T("Kill 5 to see it move")}</color>";
        else
            _clipLabel.text = $"{(Hidden(_clips[_clipIndex]) ? "???" : Loc.Name(_clips[_clipIndex].Label))}  <color=#8A7058>{_clipIndex + 1}/{_clipCount}" +
                              (tier < 3 && _clips.Count > _clipCount ? " · " + Loc.F("{0} more at ★★★", _clips.Count - _clipCount) : "") + "</color>";
    }

    private HashSet<CodexClip> _films = new();
    private readonly HashSet<CodexClip> _partClips = new();
    private HashSet<string> _seenMoves;          // null: everything is shown (not a boss, or developer reveal)

    /// <summary>A boss's move not seen yet: shown as a black silhouette named "???".</summary>
    private bool Hidden(CodexClip clip) =>
        _seenMoves != null && clip != null && !_films.Contains(clip) && !_partClips.Contains(clip) &&
        FightRecorder.IsMove(clip.Label) && !_seenMoves.Contains(clip.Label);
    private string _currentKey;                 // storage key of what is shown ("Yggdrasil" or "Yggdrasil@DM")
    private UiKit.Chip _filmChip, _modeChip;
    private bool _darkMirror, _animatedDark;    // the Normal / Dark Mirror switch; the version shown

    /// <summary>Entries that are pieces of this boss's fight (Pope's dark crystals).</summary>
    private static List<CodexEntry> PartsOfFight(CodexEntry entry) =>
        CodexCatalog.Entries.Where(e => CodexTracker.Progress.Peek(e.Id)?.PartOf == entry.Id).ToList();

    /// <summary>Ordinary enemies that appeared in this boss's fights (its summons).</summary>
    private static List<CodexEntry> SummonsOf(CodexEntry entry) =>
        CodexCatalog.Entries.Where(e => CodexTracker.Progress.Peek(e.Id)?.IsSummonedBy(entry.Id) == true).ToList();

    private static readonly Dictionary<string, CodexClip> PartClips = new();

    /// <summary>A fight piece's clip under "Dark crystal left · Activate" (a copy: the loaded list is shared).</summary>
    /// <summary>The words several names share at the start ("Dark Crystal Left" + "... Right"), else at the end, else the first.</summary>
    private static string SharedName(List<string> names)
    {
        var words = names.Select(n => n.Split(' ')).ToList();
        int lead = 0, tail = 0, shortest = words.Min(w => w.Length);
        while (lead < shortest && words.All(w => w[lead] == words[0][lead]))
            lead++;
        while (tail < shortest && words.All(w => w[w.Length - 1 - tail] == words[0][words[0].Length - 1 - tail]))
            tail++;
        return lead > 0 ? string.Join(" ", words[0].Take(lead))
            : tail > 0 ? string.Join(" ", words[0].Skip(words[0].Length - tail))
            : names[0];
    }

    private static CodexClip PartClip(string partName, CodexClip clip)
    {
        string label = $"{partName} · {clip.Label}";
        string key = clip.File + "|" + label;
        if (!PartClips.TryGetValue(key, out var copy))
            PartClips[key] = copy = new CodexClip
            {
                Label = label, File = clip.File, CellWidth = clip.CellWidth, CellHeight = clip.CellHeight,
                Columns = clip.Columns, Count = clip.Count, Durations = clip.Durations,
            };
        return copy;
    }

    private UiKit.Chip _memberChip;
    private int _memberIndex, _animatedMember;

    /// <summary>"Short Hair" for LeianaShortHair on the Leiana sisters page: what sets the member apart.</summary>
    private static string MemberName(CodexEntry member, List<CodexEntry> members)
    {
        string name = Recording.OwnerNames.Humanize(member.Key);
        var words = name.Split(' ');
        int common = 0;
        while (common < words.Length - 1 && members.All(m => Recording.OwnerNames.Humanize(m.Key).Split(' ').ElementAtOrDefault(common) == words[common]))
            common++;
        return Loc.Name(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(" ", words.Skip(common))));
    }

    private void ToggleMember()
    {
        _memberIndex++;
        _listDirty = true;
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private UiKit.Chip _categoryChip;
    private bool _showFilms, _showingFilms;     // the Animations / Attacks (filmed) switch; what is shown
    private string _keepLabel;                  // the move to stay on after switching category

    private void ToggleCategory()
    {
        _keepLabel = _clipIndex < _clips.Count ? _clips[_clipIndex].Label : null;
        _showFilms = !_showingFilms;
        _frame = 0;
        _frameTime = 0f;
        _listDirty = true;
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void ToggleMode()
    {
        _darkMirror = !_darkMirror;
        _listDirty = true;
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private CodexClip ShownClip() => _clips[_clipIndex];

    private static readonly System.Text.RegularExpressions.Regex SectionPrefix = new(@"^(.+?) · ");
    private static readonly System.Text.RegularExpressions.Regex PhaseName = new(@"^Phase (\d+)$");

    /// <summary>The fight part a move belongs to: "Phase 2", "Pair phase", a fight piece's name, or "" (the start).</summary>
    private static string SectionOf(string label)
    {
        var m = SectionPrefix.Match(label);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// The switcher order: first what isn't an attack (idle first, sleeping, appearing, dying...), then the attacks
    /// by fight part (unmarked first, then "Phase N" in order, then other parts like "Pair phase" in the order they
    /// appear), each move directly followed by its film if there is one; films without a posed animation go to the end
    /// of their part.
    /// </summary>
    private static List<CodexClip> Ordered(List<CodexClip> posed, List<CodexClip> films)
    {
        var result = new List<CodexClip>();
        result.AddRange(posed.Where(c => IsIdleLike(c.Label, -1)).OrderBy(c => c.Label.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ? 0 : 1));
        var moves = posed.Where(c => !IsIdleLike(c.Label, -1)).ToList();
        var looseFilms = films.Where(f => moves.All(m => m.Label != f.Label)).ToList();
        var sections = moves.Select(m => SectionOf(m.Label)).Concat(looseFilms.Select(f => SectionOf(f.Label))).Distinct().ToList();
        int Rank(string section) =>
            section.Length == 0 ? 0 : PhaseName.Match(section) is { Success: true } p ? int.Parse(p.Groups[1].Value) * 1000 : 100000 + sections.IndexOf(section);
        foreach (var section in sections.OrderBy(Rank))
        {
            foreach (var move in moves.Where(m => SectionOf(m.Label) == section))
            {
                result.Add(move);
                var film = films.FirstOrDefault(f => f.Label == move.Label);
                if (film != null)
                    result.Add(film);
            }
            result.AddRange(looseFilms.Where(f => SectionOf(f.Label) == section));
        }
        return result;
    }

    private void ToggleRefilm()
    {
        if (_currentKey == null || _clipIndex >= _clips.Count || !_films.Contains(_clips[_clipIndex]))
            return;
        string folder = CodexAnimations.ReplayFolderOf(_currentKey), label = _clips[_clipIndex].Label;
        bool on = !CodexAnimations.Refilm(folder).Contains(label);
        CodexAnimations.SetRefilm(folder, label, on);
        FightRecorder.Refilm(_currentKey, label, on);
        _listDirty = true;
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void SwitchClip(int delta)
    {
        if (_clipCount <= 1)
            return;
        _clipIndex = (_clipIndex + delta + _clipCount) % _clipCount;
        _frame = 0;
        _frameTime = 0f;
        _listDirty = true; // redraws the label
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    /// <summary>Plays the current clip (unscaled time: the game is paused while the book is open).</summary>
    private void Animate()
    {
        if (_clipCount == 0 || _portrait == null || _stage < 3)
            return;
        var clip = ShownClip();
        if (!clip.Ready)
        {
            CodexAnimations.EnsureFrames(clip); // loads in the background; the still picture shows meanwhile
            return;
        }
        if (_portrait.sprite != clip.Frames[_frame % clip.Frames.Length] && _frameTime == 0f)
            _portrait.sprite = clip.Frames[_frame % clip.Frames.Length]; // just finished loading
        if (clip.Frames.Length <= 1)
            return;
        _frameTime += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.04f, clip.Durations[_frame % clip.Durations.Length]);
        if (_frameTime < duration)
            return;
        _frameTime -= duration;
        _frame = (_frame + 1) % clip.Frames.Length;
        if (_frame == 0)
            _frameTime -= 0.35f; // a short pause before the animation repeats
        _portrait.sprite = clip.Frames[_frame];
    }

    private void RefreshTabs()
    {
        foreach (var pair in _tabs)
            pair.Value.SetOn(pair.Key == _category);
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        var label = UiKit.Text("Text", parent, text, size, alignment, color);
        label.enableWordWrapping = true;
        return label;
    }

    // ---------------------------------------------------------------- content

    private void DrawList()
    {
        var progress = CodexTracker.Progress;
        // Pieces of a boss fight (Pope's dark crystals) live on the boss's page, not in the list.
        var all = CodexCatalog.Entries.Where(e => !(progress.Peek(e.Id)?.PartOf.Length > 0) && !CodexCatalog.TwinOf.ContainsKey(e.Id)).ToList();
        var inCategory = all.Where(e => e.Category == _category).OrderBy(e => e.Order).ToList();

        // Search only what the player may know by name: undiscovered entries stay "???" and can't be found.
        bool searching = !string.IsNullOrWhiteSpace(_query);
        _shown = searching
            ? FuzzySearch.Filter(all.Where(e => CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0), _query, e => e.Name)
            : inCategory;
        if (_show != ShowFilter.All)
            _shown = _shown.Where(e => (CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0) == (_show == ShowFilter.Found)).ToList();
        if (!searching && _sort == SortOrder.Name)
            _shown = _shown.OrderBy(e => CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0 ? 0 : 1).ThenBy(e => e.Name).ToList();
        else if (!searching && _sort == SortOrder.Kills)
            _shown = _shown.OrderByDescending(e => IsEnemy(e) ? CodexTracker.Page(e.Id)?.Kills ?? 0 : CodexTracker.Page(e.Id)?.PickedUp ?? 0).ThenBy(e => e.Order).ToList();

        int known = all.Count(e => CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0);
        int knownHere = inCategory.Count(e => CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0);
        string developing = _waitingShown > 0 ? $"  ·  <color=#9C3B2A>{Loc.P("developing {0} picture…", "developing {0} pictures…", _waitingShown)}</color>" : "";
        _progressText.text = $"{TabLabel(_category)}: {knownHere} / {inCategory.Count}\n<size=80%>{Loc.F("All: {0} / {1} discovered", known, all.Count)}{developing}</size>";

        while (_rows.Count < _shown.Count)
            _rows.Add(NewRow());
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.Root.SetActive(i < _shown.Count);
            if (i >= _shown.Count)
                continue;
            var entry = _shown[i];
            var p = CodexTracker.Page(entry.Id);
            int tier = CodexTiers.Tier(entry.Category, p, entry.Id);
            row.Id = entry.Id;
            row.Name.text = tier > 0 ? entry.Name : "???";
            row.Name.color = tier > 0 ? Ink : InkDim;
            row.Count.text = tier == 0 ? "" : Counter(entry, p);
            row.Background.color = entry.Id == _selectedId ? RowSelected : new Color(0, 0, 0, 0);
            var (icon, iconColor) = Picture(entry, p, tier);
            row.Icon.sprite = icon;
            row.Icon.color = icon == null ? new Color(0, 0, 0, 0) : iconColor;
        }

        if (_selectedId == null && _shown.Count > 0)
            _selectedId = _shown[0].Id;
    }

    private static string Counter(CodexEntry entry, EntryProgress p)
    {
        string stars = new string('★', CodexTiers.Tier(entry.Category, p, entry.Id)) + new string('☆', CodexTiers.Max - CodexTiers.Tier(entry.Category, p, entry.Id));
        if (IsEnemy(entry))
            return $"{Loc.P("{0} kill", "{0} kills", p?.Kills ?? 0)}  {stars}";
        if (entry.Category == CodexCategory.Inscriptions)
            return $"{Loc.F("{0}x completed", p?.PickedUp ?? 0)}  {stars}";
        return $"{Loc.F("{0}x taken", p?.PickedUp ?? 0)}  {stars}";
    }

    private Row NewRow()
    {
        var bg = UiKit.Image("Row", _listContent, new Color(0, 0, 0, 0));
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
        var row = new Row { Root = bg.gameObject, Background = bg };
        row.Icon = UiKit.Image("Icon", bg.transform, Color.white);
        row.Icon.preserveAspect = true;
        row.Icon.rectTransform.anchorMin = row.Icon.rectTransform.anchorMax = new Vector2(0, 0.5f);
        row.Icon.rectTransform.pivot = new Vector2(0, 0.5f);
        row.Icon.rectTransform.anchoredPosition = new Vector2(6, 0);
        row.Icon.rectTransform.sizeDelta = new Vector2(28, 28);
        row.Name = Label(bg.transform, "", 19, Ink, TextAlignmentOptions.MidlineLeft);
        row.Name.enableWordWrapping = false;
        row.Name.overflowMode = TextOverflowModes.Ellipsis;
        var nameRect = row.Name.rectTransform;
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(0.62f, 1);
        nameRect.offsetMin = new Vector2(42, 0);
        nameRect.offsetMax = Vector2.zero;
        row.Count = Label(bg.transform, "", 16, InkDim, TextAlignmentOptions.MidlineRight);
        row.Count.enableWordWrapping = false;
        var countRect = row.Count.rectTransform;
        countRect.anchorMin = new Vector2(0.62f, 0);
        countRect.anchorMax = new Vector2(1, 1);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = new Vector2(-10, 0);

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            _selectedId = row.Id;
            _listDirty = true;
            _nextRefresh = 0f;
            EventSystem.current?.SetSelectedGameObject(null);
        });
        return row;
    }

    private void DrawDetail()
    {
        var entry = CodexCatalog.Entries.FirstOrDefault(e => e.Id == _selectedId);
        if (entry == null)
        {
            _title.text = "";
            _subtitle.text = "";
            _stats.text = "";
            _description.text = "";
            _nextText.text = "";
            _portrait.color = new Color(0, 0, 0, 0);
            _portraitMark.text = "";
            _barFill.rectTransform.anchorMax = new Vector2(0, 1);
            return;
        }

        var p = CodexTracker.Page(entry.Id);
        int tier = CodexTiers.Tier(entry.Category, p, entry.Id);
        bool enemy = IsEnemy(entry);

        // Portrait: nothing, a black silhouette, or in colour; enemies are animated.
        _stage = enemy ? Stage(entry, p, tier) : (tier > 0 ? 3 : 0);
        SetUpAnimation(entry, tier);
        Sprite sprite;
        Color color;
        if (enemy && _clipCount > 0 && _stage >= 3 && ShownClip().Ready)
            (sprite, color) = (ShownClip().Frames[_frame % ShownClip().Frames.Length], Hidden(ShownClip()) ? Color.black : Color.white);
        else
            (sprite, color) = Picture(entry, p, tier);
        _portrait.sprite = sprite;
        _portrait.color = sprite == null ? new Color(0, 0, 0, 0) : color;
        _portraitMark.text = sprite != null ? "" : tier == 0 ? "?" : enemy ? $"<size=22%>{Loc.T("No picture yet.\nKill it once more to capture it.")}</size>" : "";

        _title.text = tier > 0 ? entry.Name : "???";
        string stars = new string('★', tier) + new string('☆', CodexTiers.Max - tier);
        _subtitle.text = tier > 0 ? $"{Loc.Name(entry.Group)} · {CategoryName(entry.Category)}   <color=#9C3B2A>{stars}</color>" : $"{CategoryName(entry.Category)}   {stars}";

        var next = CodexTiers.Next(entry.Category, p, entry.Id);
        _nextText.text = next == null ? Loc.T("<b>Mastered.</b> Everything about it is revealed.") : $"{next.Value.text}  ({Math.Min(next.Value.have, next.Value.need)} / {next.Value.need})";
        float fill = next == null ? 1f : next.Value.need > 0 ? Mathf.Clamp01((float)next.Value.have / next.Value.need) : 0f;
        _barFill.rectTransform.anchorMax = new Vector2(fill, 1);

        _description.text = tier == 0 ? "" : DetailText(entry, p, tier);
        string shownFor = entry.Id + "#" + (_showingFilms ? "film" : "") + _clipIndex;
        if (shownFor != _textShownFor)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f; // back to the top for a new entry or move
        }
    }

    private static string Stats(CodexEntry entry, EntryProgress p, int tier)
    {
        var sb = new StringBuilder();
        void Line(string label, string value) => sb.Append(label).Append("<pos=45%>").Append(value).Append('\n');
        string N(double v) => v.ToString("N0", CultureInfo.InvariantCulture);

        if (IsEnemy(entry))
        {
            Line(Loc.T("Encountered"), N(p.Seen));
            Line(Loc.T("Killed"), N(p.Kills));
            if (tier >= 2)
            {
                if (p.MaxHp > 0) Line(Loc.T("Health (highest seen)"), N(p.MaxHp));
                Line(Loc.T("Your damage to it"), N(p.DamageDealt));
                if (p.BestHit > 0) Line(Loc.T("Your biggest hit"), N(p.BestHit));
                Line(Loc.T("Damage it did to you"), N(p.DamageTaken));
            }
            if (tier >= 3)
            {
                if (p.WorstHit > 0) Line(Loc.T("Its biggest hit on you"), N(p.WorstHit));
                Line(Loc.T("Times it killed you"), N(p.DeathsBy));
            }
            else if (tier == 2)
            {
                sb.Append($"<color=#8A7058>{Loc.T("Kill more to reveal its hardest hits.")}</color>");
            }
            // What else is in this fight: its pieces (Pope's dark crystals) and the enemies it summons.
            var progress = CodexTracker.Progress;
            // Adventurers and their stronger chapter 4 versions point to each other.
            if (CodexCatalog.OtherVersion(entry) is { } other)
            {
                bool known = CodexTiers.Tier(other.Category, CodexTracker.Page(other.Id), other.Id) > 0;
                Line(entry.Key.StartsWith("Veteran") ? Loc.T("Weaker version") : Loc.T("Stronger version"), known ? other.Name : "???");
            }
            var parts = PartsOfFight(entry);
            if (parts.Count > 0)
            {
                sb.Append($"\n<color=#9C3B2A><b>{Loc.T("In this fight")}</b></color>\n");
                foreach (var part in parts)
                {
                    var pp = progress.Peek(part.Id);
                    Line(part.Name, pp != null && pp.MaxHp > 0 ? Loc.F("{0} HP", N(pp.MaxHp)) : "");
                }
            }
            var summons = SummonsOf(entry).Where(e => CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0).ToList();
            if (summons.Count > 0)
                sb.Append($"\n<color=#9C3B2A><b>{Loc.T("Summons")}</b></color>\n").Append(string.Join(", ", summons.Select(e => e.Name))).Append('\n');
        }
        else if (entry.Category == CodexCategory.Inscriptions)
        {
            Line(Loc.T("Collected in runs"), N(p.Seen));
            Line(Loc.T("Completed (max step)"), N(p.PickedUp));
        }
        else
        {
            Line(Loc.T("Seen"), N(p.Seen));
            Line(Loc.T("Taken"), N(p.PickedUp));
            var gear = CodexCatalog.GearOf(entry);
            if (gear is GameResources.ItemReference item && tier >= 2)
                Line(Loc.T("Inscriptions"), $"{Inscription.GetName(item.prefabKeyword1)}, {Inscription.GetName(item.prefabKeyword2)}");
        }
        return sb.ToString();
    }

    private static string Description(CodexEntry entry, int tier)
    {
        switch (entry.Category)
        {
            case CodexCategory.Enemies:
            case CodexCategory.Bosses:
                return tier >= 2 ? "" : $"<color=#8A7058>{Loc.T("Defeat it to learn more about it.")}</color>";
            case CodexCategory.Inscriptions:
            {
                if (tier < 2)
                    return $"<color=#8A7058>{Loc.T("Complete it once (reach its highest step) to read its full description.")}</color>";
                var inscriptions = Singleton<Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy?.inscriptions;
                if (!Enum.TryParse(entry.Key, out Inscription.Key key) || inscriptions == null)
                    return "";
                var inscription = inscriptions[key];
                var sb = new StringBuilder();
                for (int step = 1; inscription.steps != null && step < inscription.steps.Count; step++)
                    sb.Append($"<b>{inscription.steps[step]}</b>  {inscription.GetDescription(step)}\n\n");
                if (tier >= 3 && inscription.canBeSuperType)
                    sb.Append($"<b>{Loc.T("Tuned")}</b>  {inscription.GetSuperDescription()}");
                return sb.ToString();
            }
            case CodexCategory.DarkAbilities:
                return tier < 2 ? $"<color=#8A7058>{Loc.T("Take it once to read what it does.")}</color>" : CodexCatalog.DarkDescription(entry);
            default:
            {
                if (tier < 2)
                    return $"<color=#8A7058>{Loc.T("Pick it up to read its description.")}</color>";
                var (description, flavor) = CodexCatalog.GearTexts(entry);
                return tier >= 3 && flavor.Length > 0 ? $"{description}\n\n<i>{flavor}</i>" : description;
            }
        }
    }

    /// <summary>The still picture of an entry at its discovery stage: outline, black silhouette or colour.</summary>
    private static (Sprite sprite, Color color) Picture(CodexEntry entry, EntryProgress p, int tier)
    {
        if (tier == 0)
            return (null, Color.white);
        if (!IsEnemy(entry))
            return (IconOf(entry), Color.white);
        int stage = Stage(entry, p, tier);
        var (still, outline) = CodexAnimations.Portrait(
            CodexMode.HasCaptures(entry.Key, false) || !CodexMode.HasCaptures(entry.Key, true) ? entry.Key : CodexMode.StorageKey(entry.Key, true));
        if (still == null)
            return (CodexPortraits.Get(entry.Key), stage >= 3 ? Color.white : Color.black);
        return stage switch
        {
            1 => (outline ?? still, outline != null ? Color.white : Color.black),
            2 => (still, Color.black),
            _ => (still, Color.white),
        };
    }

    private static Sprite IconOf(CodexEntry entry)
    {
        switch (entry.Category)
        {
            case CodexCategory.Enemies:
            case CodexCategory.Bosses:
                return CodexPortraits.Get(entry.Key);
            case CodexCategory.Inscriptions:
                return Enum.TryParse(entry.Key, out Inscription.Key key) ? SafeInscriptionIcon(key) : null;
            case CodexCategory.DarkAbilities:
                return CodexCatalog.DarkIcon(entry);
            default:
                return CodexCatalog.GearIcon(entry);
        }
    }

    private static Sprite SafeInscriptionIcon(Inscription.Key key)
    {
        try
        {
            return Inscription.GetActiveIcon(key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsEnemy(CodexEntry entry) => entry.Category is CodexCategory.Enemies or CodexCategory.Bosses;

    private static string CategoryName(CodexCategory category) => category switch
    {
        CodexCategory.Enemies => Loc.T("Enemy"),
        CodexCategory.Bosses => Loc.T("Boss"),
        CodexCategory.Skulls => Loc.T("Skull"),
        CodexCategory.Items => Loc.T("Item"),
        CodexCategory.Essences => Loc.T("Essence"),
        CodexCategory.DarkAbilities => Loc.T("Dark ability"),
        _ => Loc.T("Inscription"),
    };

    private static string TabLabel(CodexCategory category) => category switch
    {
        CodexCategory.Enemies => Loc.T("Enemies"),
        CodexCategory.Bosses => Loc.T("Bosses"),
        CodexCategory.Skulls => Loc.T("Skulls"),
        CodexCategory.Items => Loc.T("Items"),
        CodexCategory.Essences => Loc.T("Essences"),
        CodexCategory.DarkAbilities => Loc.T("Dark"),
        _ => Loc.T("Inscriptions"),
    };

    private static string SortName(SortOrder sort) => sort switch
    {
        SortOrder.Name => Loc.T("Name"),
        SortOrder.Kills => Loc.T("Kills"),
        _ => Loc.T("Book"),
    };

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;
        var go = new GameObject("DamageInsight_EventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}
