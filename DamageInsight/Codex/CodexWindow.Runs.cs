using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageInsight.History;
using DamageInsight.Lang;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The You section's tabs and the run history pages (0.13; Umut's reference: League of Legends' match history).
/// Left: the newest runs, one row each (result and kind, the boss or killer with a crown / bones badge, the skulls and
/// items, kills · damage · gold, stage, time, date, a star). Right: the run with tabs Overview, Damage, Timeline, the
/// death cam picture in the frame and a star that keeps the run whole forever.
/// </summary>
public sealed partial class CodexWindow
{
    private sealed class RunRow
    {
        public GameObject Root;
        public Image Background, Portrait, Badge, Star;
        public Outline Frame;
        public TextMeshProUGUI Result, Mode, Where, Numbers, Time, Date, PortraitMark;
        public readonly List<Image> Top = new(), Items = new();
        public string Id;
    }

    private enum RunFilter { All, Wins, Starred }

    private const int RunsPerPage = 20, TopIcons = 8, ItemIcons = 9;
    private const float RunRowHeight = 100f;

    private RectTransform _runsPanel, _runsContent;
    private ScrollRect _runsScroll;
    private readonly Dictionary<CodexCategory, UiKit.Chip> _youTabs = new();
    private readonly Dictionary<RunFilter, UiKit.Chip> _runFilterChips = new();
    private readonly List<RunRow> _runRows = new();
    private readonly Dictionary<RunPages.Tab, UiKit.Chip> _runTabChips = new();
    private UiKit.Chip _starChip, _moreChip, _endScreenChip, _likeChip;
    /// <summary>The run on the right page (one's own or a daily board's): the End screen button shows it.</summary>
    private RunRecord _shownRun;
    private TextMeshProUGUI _runsCount, _runsEmpty;
    private RunFilter _runFilter = RunFilter.All;
    private RunPages.Tab _runTab = RunPages.Tab.Overview;
    private int _runsShown = RunsPerPage, _runsDrawnVersion = -1;
    private List<RunRecord> _runList = new();
    private Texture2D _runPictureTexture;
    private Sprite _runPicture;
    private string _runPictureId;

    private void ForgetRunWidgets()
    {
        _youTabs.Clear();
        _runFilterChips.Clear();
        _runRows.Clear();
        _runTabChips.Clear();
        _starChip = _moreChip = _endScreenChip = _likeChip = null;
        _shownRun = null;
        _runsCount = _runsEmpty = null;
        _runsPanel = _runsContent = null;
        _runsScroll = null;
        ForgetRunPicture();
    }

    // ---------------------------------------------------------------- building

    private void BuildYouTabs(RectTransform page)
    {
        float x = 32;
        foreach (var category in CodexNavigation.YouTabs)
        {
            string label = TabLabel(category);
            float width = UiKit.ChipWidth(label) + 12;
            var chip = new UiKit.Chip(page, label, x, 84, width, () =>
            {
                _nav.SwitchCategory(category);
                OnNavigated();
            });
            _youTabs[category] = chip;
            x += width + 6;
        }
    }

    private void BuildRuns(RectTransform panel)
    {
        float fx = 32;
        foreach (RunFilter filter in Enum.GetValues(typeof(RunFilter)))
        {
            string label = filter switch { RunFilter.Wins => Loc.T("Victories"), RunFilter.Starred => "★ " + Loc.T("Starred"), _ => Loc.T("All") };
            float width = UiKit.ChipWidth(label);
            var chip = new UiKit.Chip(panel, label, fx, 124, width, () =>
            {
                _runFilter = filter;
                _runsShown = RunsPerPage;
                _listDirty = true;
                _nextRefresh = 0f;
                foreach (var pair in _runFilterChips)
                    pair.Value.SetOn(pair.Key == _runFilter);
            });
            chip.SetOn(filter == _runFilter);
            _runFilterChips[filter] = chip;
            fx += width + 6;
        }
        _runsCount = Label(panel, "", 16, InkDim, TextAlignmentOptions.MidlineRight);
        _runsCount.rectTransform.Place(fx + 10, 124, PageWidth - 32 - fx - 10, 28);
        _runsCount.enableWordWrapping = false;

        var frame = UiKit.Image("Runs", panel, PageShade).rectTransform.Place(32, 160, PageWidth - 64, BookHeight - 24 - 160 - 32);
        var viewport = UiKit.Rect("Viewport", frame).Fill(4);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        _runsContent = UiKit.Rect("Content", viewport);
        _runsContent.anchorMin = new Vector2(0, 1);
        _runsContent.anchorMax = new Vector2(1, 1);
        _runsContent.pivot = new Vector2(0.5f, 1);
        _runsContent.sizeDelta = Vector2.zero;
        var layout = _runsContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 4;
        _runsContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _runsScroll = frame.gameObject.AddComponent<ScrollRect>();
        _runsScroll.viewport = viewport;
        _runsScroll.content = _runsContent;
        _runsScroll.horizontal = false;
        _runsScroll.movementType = ScrollRect.MovementType.Clamped;
        _runsScroll.scrollSensitivity = 40f;

        _runsEmpty = Label(frame, Loc.T("No runs yet. Every run you play from now on is kept here."), 20, InkDim, TextAlignmentOptions.Center);
        _runsEmpty.rectTransform.Fill(40);
    }

    /// <summary>A run in the list: built like League's match history row, sized for the book's page.</summary>
    private RunRow NewRunRow()
    {
        var bg = UiKit.Image("Run", _runsContent, new Color(0, 0, 0, 0));
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = RunRowHeight;
        var row = new RunRow { Root = bg.gameObject, Background = bg };
        var t = bg.transform;

        var portraitFrame = UiKit.Image("Portrait frame", t, TileShade);
        portraitFrame.rectTransform.Place(8, 12, 76, 76);
        var outline = portraitFrame.gameObject.AddComponent<Outline>();
        outline.effectColor = SectionBorder;
        outline.effectDistance = new Vector2(2f, -2f);
        row.Frame = outline;
        row.Portrait = UiKit.Image("Portrait", portraitFrame.transform, Color.white);
        row.Portrait.preserveAspect = true;
        row.Portrait.rectTransform.Fill(4);
        row.PortraitMark = Label(portraitFrame.transform, "", 34, InkDim, TextAlignmentOptions.Center);
        row.PortraitMark.rectTransform.Fill();
        row.Badge = UiKit.Image("Badge", portraitFrame.transform, Color.white);
        row.Badge.preserveAspect = true;
        row.Badge.rectTransform.Place(50, 50, 30, 30);

        row.Result = Label(t, "", 24, Ink, TextAlignmentOptions.TopLeft);
        row.Result.rectTransform.Place(94, 8, 180, 32);
        row.Result.enableWordWrapping = false;
        row.Mode = Label(t, "", 15, Ink, TextAlignmentOptions.TopLeft);
        row.Mode.rectTransform.Place(94, 40, 180, 22);
        row.Mode.enableWordWrapping = false;
        row.Mode.overflowMode = TextOverflowModes.Ellipsis;
        row.Where = Label(t, "", 14, InkDim, TextAlignmentOptions.TopLeft);
        row.Where.rectTransform.Place(94, 62, 180, 34);
        row.Where.overflowMode = TextOverflowModes.Ellipsis;

        for (int i = 0; i < TopIcons; i++)
            row.Top.Add(SmallIcon(t, 282 + i * 37, 6, 34));
        for (int i = 0; i < ItemIcons; i++)
            row.Items.Add(SmallIcon(t, 282 + i * 33, 44, 30));
        row.Numbers = Label(t, "", 13, Ink, TextAlignmentOptions.TopLeft);
        row.Numbers.rectTransform.Place(282, 78, 300, 20);
        row.Numbers.enableWordWrapping = false;
        row.Numbers.overflowMode = TextOverflowModes.Ellipsis;

        row.Time = Label(t, "", 20, Ink, TextAlignmentOptions.TopRight);
        row.Time.rectTransform.Place(584, 8, 78, 28);
        row.Time.enableWordWrapping = false;
        row.Date = Label(t, "", 13, InkDim, TextAlignmentOptions.TopRight);
        row.Date.rectTransform.Place(584, 38, 78, 36);

        row.Star = UiKit.Image("Star", t, Color.white);
        row.Star.preserveAspect = true;
        row.Star.rectTransform.Place(640, 72, 22, 22);
        var starButton = row.Star.gameObject.AddComponent<Button>();
        starButton.transition = Selectable.Transition.None;
        starButton.navigation = new Navigation { mode = Navigation.Mode.None };
        starButton.onClick.AddListener(() => DamageInsight.Patches.Guard.Run("Codex run star", () =>
        {
            var run = _runList.FirstOrDefault(r => r.Id == row.Id);
            if (run == null)
                return;
            MenuSounds.Play(MenuSounds.Kind.Select);
            RunJournal.SetFavourite(run.Id, !run.Favourite);
            _listDirty = true;
            _nextRefresh = 0f;
            EventSystem.current?.SetSelectedGameObject(null);
        }));

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            _nav.Select(row.Id);
            _listDirty = true;
            _nextRefresh = 0f;
            EventSystem.current?.SetSelectedGameObject(null);
        });
        UiKit.OnHover(bg.gameObject, on =>
        {
            if (bg != null)
                bg.color = row.Id == _selectedId ? RowSelected : on ? new Color32(0xD9, 0xC8, 0xA8, 0xFF) : new Color(0, 0, 0, 0);
        });
        return row;
    }

    private static Image SmallIcon(Transform parent, float x, float y, float size)
    {
        var square = UiKit.Image("Slot", parent, TileShade);
        square.rectTransform.Place(x, y, size, size);
        var icon = UiKit.Image("Icon", square.transform, Color.white);
        icon.preserveAspect = true;
        icon.rectTransform.Fill(2);
        return icon;
    }

    // ---------------------------------------------------------------- the list

    private void DrawRunsList()
    {
        var all = RunJournal.Runs();
        var shown = _runFilter switch
        {
            RunFilter.Wins => all.Where(r => r.Result == RunResult.Ending).ToList(),
            RunFilter.Starred => all.Where(r => r.Favourite).ToList(),
            _ => all,
        };
        _runList = shown.Take(_runsShown).ToList();
        int wins = all.Count(r => r.Result == RunResult.Ending);
        _runsCount.text = all.Count == 0 ? "" : Loc.F("{0} runs · {1} won", all.Count, wins);
        _runsEmpty.gameObject.SetActive(_runList.Count == 0);
        if (_runList.Count == 0)
            _runsEmpty.text = all.Count == 0 ? Loc.T("No runs yet. Every run you play from now on is kept here.") : Loc.T("No run fits this filter.");

        while (_runRows.Count < _runList.Count)
            _runRows.Add(NewRunRow());
        for (int i = 0; i < _runRows.Count; i++)
        {
            var row = _runRows[i];
            row.Root.SetActive(i < _runList.Count);
            if (i < _runList.Count)
                FillRunRow(row, _runList[i]);
        }

        // "Show more" under the newest 20 (every run stays on disk).
        if (_moreChip == null)
        {
            var holder = UiKit.Rect("More", _runsContent);
            holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            string label = Loc.T("Show more");
            float width = UiKit.ChipWidth(label) + 20;
            _moreChip = new UiKit.Chip(holder, label, (PageWidth - 64 - 8 - width) / 2, 6, width, () =>
            {
                _runsShown += RunsPerPage;
                _listDirty = true;
                _nextRefresh = 0f;
            });
        }
        _moreChip.Background.transform.parent.SetAsLastSibling();
        _moreChip.Background.transform.parent.gameObject.SetActive(shown.Count > _runList.Count);

        if (_selectedId == null && _runList.Count > 0)
            _nav.SelectQuietly(_runList[0].Id);
    }

    private void FillRunRow(RunRow row, RunRecord run)
    {
        row.Id = run.Id;
        row.Background.color = run.Id == _selectedId ? RowSelected : new Color(0, 0, 0, 0);
        row.Result.text = $"<color={RunPages.ResultColor(run)}><b>{RunPages.ResultWord(run).ToUpperInvariant()}</b></color>";
        row.Mode.text = RunPages.Mode(run);
        row.Where.text = RunPages.RowLine(run, Lookup);

        var (id, _) = RunPages.Decider(run);
        var portrait = RunIcon(id);
        // The boss or the killer in a frame of the result's colour (Umut 2026-10-17: the crossed bones over the golem were
        // too much; the word says the result); without a picture, the result's own icon fills the box.
        row.Portrait.sprite = portrait ?? PixelIcons.Get(RunPages.ResultIcon(run).Substring("pix:".Length));
        row.Portrait.color = Color.white;
        row.Badge.color = new Color(0, 0, 0, 0);
        row.Frame.effectColor = portrait != null && ColorUtility.TryParseHtmlString(RunPages.ResultColor(run), out var frame) ? frame : SectionBorder;
        row.PortraitMark.text = "";

        var (top, items) = RunPages.Gear(run);
        SetIcons(row.Top, top);
        SetIcons(row.Items, items);
        row.Numbers.text = RunPages.NumbersLine(run);
        row.Time.text = $"<b>{RunPages.Time(run.PlaySeconds)}</b>";
        string date = RunPages.Date(run.Started);
        row.Date.text = date.Replace(' ', '\n');
        row.Star.sprite = PixelIcons.Get(run.Favourite ? "star" : "star_empty");
    }

    private static void SetIcons(List<Image> slots, List<RunGear> gear)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var square = slots[i].transform.parent.gameObject;
            square.SetActive(i < gear.Count);
            if (i >= gear.Count)
                continue;
            var sprite = RunIcon(gear[i].Id);
            slots[i].sprite = sprite;
            slots[i].color = sprite == null ? new Color(0, 0, 0, 0) : Color.white;
        }
    }

    /// <summary>The icon of anything a run names: a Codex page's picture, or a fragment's icon (fragments have no page).</summary>
    private static Sprite RunIcon(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        try
        {
            if (id.StartsWith("fragment:", StringComparison.Ordinal))
                return CodexCatalog.FragmentIcon(id);
            return CodexPages.Entry(id) is { } entry ? IconOf(entry) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A fragment's name in the current language (fragments have no Codex page).</summary>
    private static string FragmentName(string id)
    {
        try
        {
            return CodexCatalog.FragmentOf(id)?.displayName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- the run page

    /// <summary>The run page's buttons. <paramref name="board"/>: a daily board's run (Points tab and a thumbs up instead of
    /// the star); <paramref name="hasRun"/>: the run itself came with it (a run sent by an older version has only points).</summary>
    private void ShowRunChips(bool show, bool board = false, bool hasRun = true)
    {
        if (_runTabChips.Count == 0 && show)
            BuildRunChips();
        foreach (var pair in _runTabChips)
            pair.Value.Background.gameObject.SetActive(show && (pair.Key == RunPages.Tab.Points ? board : hasRun));
        _starChip?.Background.gameObject.SetActive(show && !board);
        _likeChip?.Background.gameObject.SetActive(show && board);
        _endScreenChip?.Background.gameObject.SetActive(show && hasRun);
    }

    private void BuildRunChips()
    {
        var page = (RectTransform)_title.transform.parent;
        float x = 32;
        foreach (RunPages.Tab tab in Enum.GetValues(typeof(RunPages.Tab)))
        {
            string label = tab switch
            {
                RunPages.Tab.Damage => Loc.T("Damage"), RunPages.Tab.Timeline => Loc.T("Timeline"), RunPages.Tab.Points => Loc.T("Points"),
                _ => Loc.T("Overview"),
            };
            float width = UiKit.ChipWidth(label) + 10;
            var chip = new UiKit.Chip(page, label, x, 262, width, () =>
            {
                _runTab = tab;
                _pageText = null;
                _textShownFor = null;
                DamageInsight.Patches.Guard.Run("Codex run page", DrawDetail);
            });
            _runTabChips[tab] = chip;
            x += width + 6;
        }
        string star = "★ " + Loc.T("Starred");
        float starWidth = Math.Max(UiKit.ChipWidth(star), UiKit.ChipWidth("☆ " + Loc.T("Star"))) + 6;
        _starChip = new UiKit.Chip(page, star, 0, 0, starWidth, () =>
        {
            if (RunJournal.Runs().FirstOrDefault(r => r.Id == _selectedId) is { } run)
                RunJournal.SetFavourite(run.Id, !run.Favourite);
            _listDirty = true;
            _nextRefresh = 0f;
        }, new Color32(0xC9, 0x9A, 0x3A, 0xFF));
        _starChip.Background.rectTransform.PlaceTopRight(32, 262, starWidth, 28);

        // A thumbs up for a daily board's run (Umut 2026-10-18: once per run, not one's own), where the star sits.
        float likeWidth = Math.Max(UiKit.ChipWidth(Loc.T("Like") + "  999"), Math.Max(UiKit.ChipWidth(Loc.T("Liked") + "  999"),
            UiKit.ChipWidth(Loc.T("Likes") + "  999"))) + 24;
        _likeChip = new UiKit.Chip(page, Loc.T("Like"), 0, 0, likeWidth, LikeBoardRun, new Color32(0xC9, 0x9A, 0x3A, 0xFF));
        _likeChip.Background.rectTransform.PlaceTopRight(32, 262, likeWidth, 28);
        var thumb = UiKit.Image("Thumb", _likeChip.Background.transform, Color.white);
        thumb.sprite = PixelIcons.Get("thumb");
        thumb.preserveAspect = true;
        thumb.rectTransform.Place(7, 5, 18, 18);
        _likeChip.Label.rectTransform.offsetMin = new Vector2(22, 0);

        // The game's own end screen with this run's numbers (Umut: "something along the lines of this").
        string endLabel = Loc.T("End screen");
        float endWidth = UiKit.ChipWidth(endLabel) + 10;
        _endScreenChip = new UiKit.Chip(_portraitFrame, endLabel, 8, 8, endWidth, () =>
        {
            var run = _shownRun;
            if (run != null && !EndScreenView.Open(run, RunIcon(RunPages.Decider(run).id)))
                Plugin.Log.LogInfo("End screen view: not available here (no game scene).");
        });
    }

    private void DrawRunDetail()
    {
        ApplyLayout(PageLayout.Run);
        _stage = 3;
        _clipCount = 0;
        _clipPrev.Background.gameObject.SetActive(false);
        _clipNext.Background.gameObject.SetActive(false);
        _clipLabel.text = "";
        foreach (var chip in new[] { _filmChip, _modeChip, _memberChip, _categoryChip })
            chip.Background.gameObject.SetActive(false);
        _nextText.text = "";
        _portraitMark.text = "";

        var run = _selectedId == null ? null : RunJournal.Load(_selectedId);
        _shownRun = run;
        if (_runTab == RunPages.Tab.Points)
            _runTab = RunPages.Tab.Overview; // only a daily board's run has it
        ShowRunChips(run != null);
        if (run == null)
        {
            _portrait.color = new Color(0, 0, 0, 0);
            _title.text = Loc.T("Runs");
            _title.color = Ink;
            _subtitle.text = "";
            SetPage($"<color=#8A7058>{Loc.T("No runs yet. Every run you play from now on is kept here.")}</color>");
            return;
        }
        foreach (var pair in _runTabChips)
            pair.Value.SetOn(pair.Key == _runTab);
        _starChip.Label.text = run.Favourite ? "★ " + Loc.T("Starred") : "☆ " + Loc.T("Star");
        _starChip.SetOn(run.Favourite);

        // The frame: the death cam picture, or the boss / killer, or the result's icon.
        var picture = RunPicture(run);
        var (deciderId, _) = RunPages.Decider(run);
        var portrait = picture ?? RunIcon(deciderId) ?? PixelIcons.Get(RunPages.ResultIcon(run).Substring("pix:".Length));
        _portrait.sprite = portrait;
        _portrait.color = Color.white;

        // The colour on the label itself: a colour tag at the start of this shortened title showed no text at all.
        _title.text = RunPages.ResultWord(run);
        _title.color = ColorUtility.TryParseHtmlString(RunPages.ResultColor(run), out var resultColor) ? resultColor : Ink;
        _subtitle.text = $"{RunPages.Mode(run)}  ·  {RunPages.Where(run)}  ·  {RunPages.Time(run.PlaySeconds)}  ·  {RunPages.Date(run.Started)}";
        SetPage(RunPages.Page(run, _runTab, Lookup));
        string shownFor = "run#" + run.Id + "#" + _runTab;
        if (shownFor != _textShownFor)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    private static readonly RunLookup Lookup = new()
    {
        Name = id => id.StartsWith("fragment:", StringComparison.Ordinal) ? FragmentName(id) : CodexPages.Entry(id)?.Name,
        HasPage = id => CodexPages.Entry(id) != null,
        Origin = CodexOrigins.Resolve,
        StatName = key => CodexCatalog.Localize($"stat/{key}/name") is { Length: > 0 } text ? text : null,
        Title = CodexCatalog.BossTitle,
    };

    /// <summary>The death cam picture saved with the run (loaded once per run shown).</summary>
    private Sprite RunPicture(RunRecord run)
    {
        if (!run.Picture)
            return null;
        if (_runPictureId == run.Id)
            return _runPicture;
        ForgetRunPicture();
        _runPictureId = run.Id;
        try
        {
            string path = RunJournal.PathOf(run.Id, ".jpg");
            if (!File.Exists(path))
                return null;
            _runPictureTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!_runPictureTexture.LoadImage(File.ReadAllBytes(path)))
                return null;
            _runPicture = Sprite.Create(_runPictureTexture, new Rect(0, 0, _runPictureTexture.width, _runPictureTexture.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: run picture not loaded: {e.Message}");
        }
        return _runPicture;
    }

    private void ForgetRunPicture()
    {
        if (_runPicture != null)
            Destroy(_runPicture);
        if (_runPictureTexture != null)
            Destroy(_runPictureTexture);
        _runPicture = null;
        _runPictureTexture = null;
        _runPictureId = null;
    }

    // ---------------------------------------------------------------- page blocks of the run pages

    /// <summary>A row of gear icons; a click opens its Codex page.</summary>
    private void AddIconRow(Transform parent, PageBlocks.Item item)
    {
        var row = UiKit.Rect("Icons", parent);
        // A grid that wraps: as many squares per line as fit (Umut 2026-10-17: a long row ran off the page).
        var layout = row.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(50, 50);
        layout.spacing = new Vector2(6, 6);
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = Math.Max(1, (int)((PageWidth - 64 - 24 + 6) / 56f));
        foreach (var (spec, link) in item.Icons)
        {
            var square = UiKit.Image("Square", row, TileShade);
            var outline = square.gameObject.AddComponent<Outline>();
            outline.effectColor = SectionBorder;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var sprite = PageIcon(spec);
            if (sprite != null)
            {
                var icon = UiKit.Image("Icon", square.transform, Color.white);
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.rectTransform.Fill(4);
            }
            if (link.Length == 0)
                continue;
            var button = square.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            string target = link;
            button.onClick.AddListener(() => DamageInsight.Patches.Guard.Run("Codex link", () => FollowLink(target)));
            UiKit.HoverLight(square);
        }
    }

    /// <summary>A bar row: the label on a bar filled to its share, the value on the right.</summary>
    private void AddBarRow(Transform parent, PageBlocks.Item item)
    {
        var row = UiKit.Image("Bar", parent, new Color32(0xD9, 0xC8, 0xA8, 0x80));
        var size = row.gameObject.AddComponent<LayoutElement>();
        size.minHeight = size.preferredHeight = 26;
        var fill = UiKit.Image("Fill", row.transform, new Color32(0x9C, 0x3B, 0x2A, 0x55));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2((float)item.Fill, 1);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        var label = Label(row.transform, item.Text, 16, Ink, TextAlignmentOptions.MidlineLeft);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = new Vector2(0.64f, 1);
        label.rectTransform.offsetMin = new Vector2(8, 0);
        label.rectTransform.offsetMax = Vector2.zero;
        AddLinkClicks(label);
        var value = Label(row.transform, item.Value, 16, Ink, TextAlignmentOptions.MidlineRight);
        value.enableWordWrapping = false;
        value.rectTransform.anchorMin = new Vector2(0.64f, 0);
        value.rectTransform.anchorMax = Vector2.one;
        value.rectTransform.offsetMin = Vector2.zero;
        value.rectTransform.offsetMax = new Vector2(-8, 0);
    }

    /// <summary>A timeline line: a small icon (or a gap) and the text.</summary>
    private void AddLine(Transform parent, PageBlocks.Item item)
    {
        var row = UiKit.Rect("Line", parent);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var icon = UiKit.Image("Icon", row, Color.white);
        icon.preserveAspect = true;
        var iconSize = icon.gameObject.AddComponent<LayoutElement>();
        iconSize.minWidth = iconSize.preferredWidth = iconSize.minHeight = iconSize.preferredHeight = 26;
        var sprite = PageIcon(item.Icon);
        icon.sprite = sprite;
        icon.color = sprite == null ? new Color(0, 0, 0, 0) : Color.white;
        var text = Label(row, item.Text, 17, Ink, TextAlignmentOptions.MidlineLeft);
        text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AddLinkClicks(text);
    }
}
