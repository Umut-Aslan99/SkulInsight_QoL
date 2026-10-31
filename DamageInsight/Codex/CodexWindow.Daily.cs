using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Daily;
using DamageInsight.History;
using DamageInsight.Lang;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The Daily section (0.13, docs/DAILY_RUN.md): left the boards (today's and the month's, your line marked), right
/// today's run and your try with the button that fits: turn daily runs on, play today's run, back to my game, or
/// (after the run) the name for the board.
/// </summary>
public sealed partial class CodexWindow
{
    private sealed class DailyRow
    {
        public GameObject Root;
        public Image Background, Icon, Thumb;
        public TextMeshProUGUI Rank, Name, Detail, Points, Likes;
        /// <summary>What a click selects: "daily" (today's run), "dailyrun:&lt;entry id&gt;" (a run on the board), "" (nothing).</summary>
        public string Id = "";
        public Color Base;
    }

    private const string TodayId = CodexNavigation.DailyPage, BoardRunPrefix = "dailyrun:";

    private RectTransform _dailyPanel, _dailyContent;
    private ScrollRect _dailyScroll;
    private readonly Dictionary<CodexCategory, UiKit.Chip> _dailyTabs = new();
    private readonly List<DailyRow> _dailyRows = new();
    private DailyRow _dailyTodayRow;
    /// <summary>The board run on the right page (its day and line): the thumbs up goes to it.</summary>
    private BoardEntry _boardEntry;
    private string _boardDate = "";
    private TextMeshProUGUI _dailyCount, _dailyEmpty;
    private UiKit.Chip _dailyEnableChip, _dailyPlayChip, _dailyLeaveChip, _dailySendChip, _dailySkipChip;
    private TMP_InputField _dailyName;
    private readonly Dictionary<string, UiKit.Chip> _boardChips = new();
    private UiKit.Chip _dlcChip;
    private int _dailyDrawnVersion = -1;
    private string _dailyOpenedFor = "";

    private void ForgetDailyWidgets()
    {
        _dailyTabs.Clear();
        _dailyRows.Clear();
        _boardChips.Clear();
        _dlcChip = null;
        _dailyTodayRow = null;
        _boardEntry = null;
        _dailyPanel = _dailyContent = null;
        _dailyScroll = null;
        _dailyCount = _dailyEmpty = null;
        _dailyEnableChip = _dailyPlayChip = _dailyLeaveChip = _dailySendChip = _dailySkipChip = null;
        _dailyName = null;
    }

    // ---------------------------------------------------------------- left page: the boards

    private void BuildDaily(RectTransform panel)
    {
        float x = 32;
        foreach (var category in CodexNavigation.DailyTabs)
        {
            string label = TabLabel(category);
            float width = UiKit.ChipWidth(label) + 12;
            var chip = new UiKit.Chip(panel, label, x, 84, width, () =>
            {
                _nav.SwitchCategory(category);
                OnNavigated();
            });
            _dailyTabs[category] = chip;
            x += width + 6;
        }
        _dailyCount = Label(panel, "", 16, InkDim, TextAlignmentOptions.MidlineRight);
        _dailyCount.rectTransform.Place(x + 10, 84, PageWidth - 32 - x - 10, 28);
        _dailyCount.enableWordWrapping = false;

        // The day's boards (0.15): Normal, the Dark Mirror at the day's level, Dark Mirror 10, and DLC on or off.
        float bx = 32;
        foreach (string baseName in DailyBoards.Bases)
        {
            string widest = baseName == "normal" ? Loc.T("Normal") : Loc.F("Dark Mirror {0}", 10);
            float width = UiKit.ChipWidth(widest) + 8;
            string name = baseName;
            _boardChips[baseName] = new UiKit.Chip(panel, BoardChipLabel(baseName), bx, 124, width, () =>
            {
                DailyService.SelectBoard(DailyBoards.Of(name, DailyBoards.HasDlc(DailyService.State.Board)));
                AfterBoardChange();
            });
            bx += width + 6;
        }
        _dlcChip = new UiKit.Chip(panel, "DLC", bx + 10, 124, UiKit.ChipWidth("DLC") + 8, () =>
        {
            string board = DailyService.State.Board;
            DailyService.SelectBoard(DailyBoards.Of(DailyBoards.BaseOf(board), !DailyBoards.HasDlc(board)));
            AfterBoardChange();
        });

        var frame = UiKit.Image("Board", panel, PageShade).rectTransform.Place(32, 160, PageWidth - 64, BookHeight - 24 - 160 - 32);
        var viewport = UiKit.Rect("Viewport", frame).Fill(4);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        _dailyContent = UiKit.Rect("Content", viewport);
        _dailyContent.anchorMin = new Vector2(0, 1);
        _dailyContent.anchorMax = new Vector2(1, 1);
        _dailyContent.pivot = new Vector2(0.5f, 1);
        _dailyContent.sizeDelta = Vector2.zero;
        var layout = _dailyContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2;
        _dailyContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _dailyScroll = frame.gameObject.AddComponent<ScrollRect>();
        _dailyScroll.viewport = viewport;
        _dailyScroll.content = _dailyContent;
        _dailyScroll.horizontal = false;
        _dailyScroll.movementType = ScrollRect.MovementType.Clamped;
        _dailyScroll.scrollSensitivity = 40f;
        _dailyEmpty = Label(frame, "", 20, InkDim, TextAlignmentOptions.Center);
        _dailyEmpty.rectTransform.Fill(40);
        // Today's run on top of both boards: the right page goes back to it (the try, the buttons).
        _dailyTodayRow = NewDailyRow();
        _dailyTodayRow.Id = TodayId;
    }

    /// <summary>A board chip's words: the Dark Mirror chip names the day's level once it is known.</summary>
    private static string BoardChipLabel(string baseName) => baseName switch
    {
        "normal" => Loc.T("Normal"),
        "dm10" => Loc.F("Dark Mirror {0}", 10),
        _ => DailyService.InfoOf("dm") is { } dm ? Loc.F("Dark Mirror {0}", dm.Level) : Loc.F("Dark Mirror {0}", "?"),
    };

    private void AfterBoardChange()
    {
        _nav.SelectQuietly(TodayId);
        _listDirty = true;
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private DailyRow NewDailyRow()
    {
        var bg = UiKit.Image("Line", _dailyContent, new Color(0, 0, 0, 0));
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
        var row = new DailyRow { Root = bg.gameObject, Background = bg };
        row.Rank = Label(bg.transform, "", 22, Ink, TextAlignmentOptions.MidlineRight);
        row.Rank.rectTransform.Place(4, 0, 46, 52);
        row.Rank.enableWordWrapping = false;
        row.Icon = UiKit.Image("Result", bg.transform, Color.white);
        row.Icon.preserveAspect = true;
        row.Icon.rectTransform.Place(60, 12, 28, 28);
        row.Name = Label(bg.transform, "", 19, Ink, TextAlignmentOptions.TopLeft);
        row.Name.rectTransform.Place(98, 2, 400, 30);
        row.Name.enableWordWrapping = false;
        row.Name.overflowMode = TextOverflowModes.Ellipsis;
        row.Detail = Label(bg.transform, "", 13, InkDim, TextAlignmentOptions.TopLeft);
        row.Detail.rectTransform.Place(98, 29, 420, 20);
        row.Detail.enableWordWrapping = false;
        row.Detail.overflowMode = TextOverflowModes.Ellipsis;
        row.Points = Label(bg.transform, "", 21, Ink, TextAlignmentOptions.MidlineRight);
        row.Points.rectTransform.Place(500, 2, 160, 28);
        row.Points.enableWordWrapping = false;
        row.Likes = Label(bg.transform, "", 14, InkDim, TextAlignmentOptions.MidlineRight);
        row.Likes.rectTransform.Place(520, 30, 116, 20);
        row.Likes.enableWordWrapping = false;
        row.Thumb = UiKit.Image("Thumb", bg.transform, Color.white);
        row.Thumb.sprite = PixelIcons.Get("thumb");
        row.Thumb.preserveAspect = true;
        row.Thumb.rectTransform.Place(642, 31, 18, 18);

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            if (row.Id.Length == 0)
                return;
            MenuSounds.Play(MenuSounds.Kind.Select);
            _nav.Select(row.Id);
            _listDirty = true;
            _nextRefresh = 0f;
            EventSystem.current?.SetSelectedGameObject(null);
        });
        UiKit.OnHover(bg.gameObject, on =>
        {
            if (bg != null)
                bg.color = row.Id.Length > 0 && row.Id == _selectedId ? RowSelected
                    : on && row.Id.Length > 0 ? new Color32(0xD9, 0xC8, 0xA8, 0xFF) : row.Base;
        });
        return row;
    }

    private void PaintDailyRow(DailyRow row, Color baseColor)
    {
        row.Base = baseColor;
        row.Background.color = row.Id.Length > 0 && row.Id == _selectedId ? RowSelected : baseColor;
    }

    private void DrawDailyList()
    {
        bool month = _category == CodexCategory.DailyMonth;
        DailyService.RequestBoards();
        string selected = DailyService.State.Board;
        foreach (var pair in _boardChips)
        {
            pair.Value.Label.text = BoardChipLabel(pair.Key);
            pair.Value.SetOn(DailyBoards.BaseOf(selected) == pair.Key);
        }
        _dlcChip?.SetOn(DailyBoards.HasDlc(selected));
        var board = month ? DailyService.MonthBoard : DailyService.DayBoard;
        var entries = board?.Entries ?? new List<BoardEntry>();
        _dailyCount.text = board == null ? "" : month ? Loc.P("{0} player this month", "{0} players this month", board.Count)
            : Loc.P("{0} run today", "{0} runs today", board.Count);
        _dailyEmpty.gameObject.SetActive(entries.Count == 0);
        _dailyEmpty.text = !DailyService.Enabled ? Loc.T("Turn on daily runs on the right page to see the boards.")
            : board == null ? (DailyService.Problem.Length > 0 ? Loc.T("No connection to the daily run server.") : Loc.T("Loading the board..."))
            : Loc.T("Nobody on the board yet. Be the first!");

        if (_selectedId == null || (_selectedId != TodayId && !_selectedId.StartsWith(BoardRunPrefix, StringComparison.Ordinal)))
            _nav.SelectQuietly(TodayId);
        var day = DailyService.Today;
        var top = _dailyTodayRow;
        top.Root.SetActive(DailyService.Enabled);
        top.Root.transform.SetAsFirstSibling();
        PaintDailyRow(top, new Color32(0xE8, 0xD6, 0xAE, 0xFF));
        top.Rank.text = "";
        top.Icon.sprite = PixelIcons.Get("hourglass");
        top.Icon.color = Color.white;
        top.Name.text = $"<b>{Loc.T("Today's run")}</b>" + (day == null ? "" : $"  <color={RunPages.DimColor}>{DailyPages.BoardName(day)}</color>");
        top.Detail.text = DailyPages.Status(DailyRun.TodaysTry, DailyRun.InDailySlot, DailyService.DayBoard);
        top.Points.text = "";
        top.Likes.text = "";
        top.Thumb.gameObject.SetActive(false);
        // The empty-board note sits under today's run.
        _dailyEmpty.rectTransform.offsetMax = new Vector2(-40, DailyService.Enabled ? -96 : -40);

        while (_dailyRows.Count < entries.Count)
            _dailyRows.Add(NewDailyRow());
        for (int i = 0; i < _dailyRows.Count; i++)
        {
            var row = _dailyRows[i];
            row.Root.SetActive(i < entries.Count);
            if (i >= entries.Count)
                continue;
            var e = entries[i];
            row.Id = !month && e.Id.Length > 0 ? BoardRunPrefix + e.Id : "";
            PaintDailyRow(row, e.You ? new Color32(0xF0, 0xDE, 0xB4, 0xFF) : i % 2 == 0 ? new Color(0, 0, 0, 0.04f) : new Color(0, 0, 0, 0));
            row.Rank.text = $"<b>{e.Rank}</b>";
            row.Thumb.gameObject.SetActive(!month);
            row.Thumb.color = e.Likes > 0 ? Color.white : new Color(1, 1, 1, 0.3f);
            row.Likes.text = month || e.Likes == 0 ? "" : DailyPages.N(e.Likes);
            row.Name.text = DailyText(e.Name) + (e.You ? $"  <color={RunPages.DimColor}>({Loc.T("you")})</color>" : "");
            row.Points.text = $"<b>{DailyPages.N(e.Points)}</b>";
            if (month)
            {
                row.Icon.sprite = PixelIcons.Get("crown");
                row.Icon.color = e.Wins > 0 ? Color.white : new Color(1, 1, 1, 0.25f);
                row.Detail.text = Loc.F("{0} days · {1} won", e.Days, e.Wins);
            }
            else
            {
                row.Icon.sprite = PixelIcons.Get(e.Won ? "crown" : e.Result == "death" ? "bones" : "flag");
                row.Icon.color = Color.white;
                string bosses = e.EndBosses.Count > 0 ? "  ·  " + string.Join(" · ", e.EndBosses.Select(DailyText)) : "";
                row.Detail.text = $"{RunPages.Time(e.PlaySeconds)}  ·  {DailyText(e.Skull)}{bosses}";
            }
        }

    }

    /// <summary>Names from the server shown as plain text (no markup can come through).</summary>
    private static string DailyText(string text) => (text ?? "").Replace("<", "").Replace(">", "");

    // ---------------------------------------------------------------- right page: today's run, your try

    private void DrawDailyDetail()
    {
        if (_selectedId != null && _selectedId.StartsWith(BoardRunPrefix, StringComparison.Ordinal))
        {
            DrawBoardRun(_selectedId.Substring(BoardRunPrefix.Length));
            return;
        }
        _boardEntry = null;
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

        var day = DailyService.Today;
        var t = DailyRun.TodaysTry;
        _portrait.sprite = PixelIcons.Get("crown");
        _portrait.color = Color.white;
        _title.text = Loc.T("Daily run");
        _title.color = Ink;
        _subtitle.text = day == null ? "" : $"{day.Date}  ·  {DailyPages.BoardName(day)}  ·  {Loc.F("new run in {0}", DailyPages.Left(day.SecondsLeft))}";
        string others = string.Join(", ", DailyMods.Others().Select(m => DailyText(m.name)));
        SetPage(DailyPages.TodayPage(day, t, DailyService.Enabled, DailyService.Problem, DailyRun.InDailySlot, others, DailyService.DayBoard));
        ShowDailyControls(true);
        if (_textShownFor != "daily")
        {
            _textShownFor = "daily";
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>
    /// A run on today's board (Umut 2026-10-18: "see the other peoples runs, their builds, their skulls"): the run page
    /// of the run history with the run its player sent, a Points tab (part by part, the day's records, the streak) and a
    /// thumbs up instead of the star.
    /// </summary>
    private void DrawBoardRun(string id)
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

        string date = DailyService.DayBoard?.Date ?? DailyService.Today?.Date ?? "";
        string boardName = DailyService.DayBoard?.Name ?? DailyService.State.Board;
        var detail = DailyService.Detail(date, id, boardName);
        var entry = DailyService.DayBoard?.Entries.FirstOrDefault(e => e.Id == id) ?? detail?.Entry;
        _boardEntry = entry;
        _boardDate = detail?.Date ?? date;
        var run = detail?.Run;
        _shownRun = run;
        if (entry == null)
        {
            ShowRunChips(false);
            _portrait.color = new Color(0, 0, 0, 0);
            _title.text = Loc.T("Daily run");
            _title.color = Ink;
            _subtitle.text = "";
            SetPage($"<color={RunPages.DimColor}>{Loc.T("Loading the run...")}</color>");
            return;
        }
        // A run sent by an older version came without the run itself: only its points.
        if (detail != null && run == null)
            _runTab = RunPages.Tab.Points;
        ShowRunChips(true, board: true, hasRun: detail == null || run != null);
        foreach (var pair in _runTabChips)
            pair.Value.SetOn(pair.Key == _runTab);
        string likeWord = entry.You ? Loc.T("Likes") : entry.Liked ? Loc.T("Liked") : Loc.T("Like");
        _likeChip.Label.text = entry.Likes > 0 || entry.You ? $"{likeWord}  {DailyPages.N(entry.Likes)}" : likeWord;
        _likeChip.SetOn(!entry.You && !entry.Liked);

        string resultIcon = entry.Won ? "crown" : entry.Result == "death" ? "bones" : "flag";
        _portrait.sprite = (run == null ? null : RunIcon(RunPages.Decider(run).id)) ?? PixelIcons.Get(resultIcon);
        _portrait.color = Color.white;
        _title.text = DailyText(entry.Name) + (entry.You ? $"  ({Loc.T("you")})" : "");
        _title.color = ColorUtility.TryParseHtmlString(entry.Won ? RunPages.WinColor : entry.Result == "death" ? RunPages.DeathColor : RunPages.DimColor,
            out var color) ? color : Ink;
        string word = entry.Won ? Loc.T("Victory") : entry.Result == "death" ? Loc.T("Defeat") : Loc.T("Abandoned");
        string mode = (detail != null ? DailyPages.Mode(detail.Mode, detail.Level)
            : DailyService.Today is { } day ? DailyPages.Mode(day.Mode, day.Level) : "") + (DailyBoards.HasDlc(boardName) ? " · DLC" : "");
        _subtitle.text = $"{Loc.F("place {0}", entry.Rank)}  ·  {Loc.F("{0} points", DailyPages.N(entry.Points))}  ·  {word}  ·  {mode}  ·  {RunPages.Time(entry.PlaySeconds)}";

        if (_runTab == RunPages.Tab.Points)
            SetPage(detail == null ? $"<color={RunPages.DimColor}>{Loc.T("Loading the run...")}</color>" : DailyPages.PointsPage(detail, entry));
        else
            SetPage(run == null ? $"<color={RunPages.DimColor}>{Loc.T("Loading the run...")}</color>" : RunPages.Page(run, _runTab, Lookup));
        string shownFor = "board#" + id + "#" + _runTab + "#" + (detail != null);
        if (shownFor != _textShownFor)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>The thumbs up chip: likes the board run shown (once; never one's own).</summary>
    private void LikeBoardRun()
    {
        if (_boardEntry == null || _boardEntry.You || _boardEntry.Liked)
            return;
        DailyService.Like(_boardDate, _boardEntry, DailyService.DayBoard?.Name ?? DailyService.State.Board);
        _listDirty = true;
        _nextRefresh = 0f;
    }

    /// <summary>The right page's buttons for the state the daily run is in (hidden on every other page).</summary>
    private void ShowDailyControls(bool show)
    {
        if (show && _dailyPlayChip == null)
            BuildDailyControls();
        if (_dailyPlayChip == null)
            return;
        var ended = DailyRun.Ended;
        bool enabled = DailyService.Enabled;
        bool naming = show && enabled && ended != null;
        _dailyEnableChip.Background.gameObject.SetActive(show && !enabled);
        // Not after today's try (Umut 2026-10-18: "play today's run" still showed after playing).
        bool played = DailyRun.TodaysTry?.Played == true;
        _dailyPlayChip.Background.gameObject.SetActive(show && enabled && !naming && !DailyRun.InDailySlot && !played);
        _dailyLeaveChip.Background.gameObject.SetActive(show && enabled && !naming && DailyRun.InDailySlot);
        _dailyName.gameObject.SetActive(naming);
        _dailySendChip.Background.gameObject.SetActive(naming);
        _dailySkipChip.Background.gameObject.SetActive(naming);
        if (naming && string.IsNullOrEmpty(_dailyName.text))
            _dailyName.text = Plugin.DailyName?.Value is { Length: > 0 } configured ? configured : DailyService.State.Name;
    }

    private void BuildDailyControls()
    {
        var page = (RectTransform)_title.transform.parent;
        string enable = Loc.T("Turn on daily runs");
        _dailyEnableChip = new UiKit.Chip(page, enable, 32, 262, UiKit.ChipWidth(enable) + 16, () =>
        {
            Plugin.DailyEnabled.Value = true;
            DailyService.RefreshNow();
            _listDirty = true;
            _nextRefresh = 0f;
        }, new Color32(0xC9, 0x9A, 0x3A, 0xFF));
        _dailyEnableChip.SetOn(true);

        string play = Loc.T("Play today's run");
        _dailyPlayChip = new UiKit.Chip(page, play, 32, 262, UiKit.ChipWidth(play) + 16, () =>
        {
            string why = DailyRun.Play(() => SetOpen(false));
            if (why != null)
                Notice.Show("daily", why, 5f);
        }, new Color32(0xC9, 0x9A, 0x3A, 0xFF));
        _dailyPlayChip.SetOn(true);

        string leave = Loc.T("Back to my game");
        _dailyLeaveChip = new UiKit.Chip(page, leave, 32, 262, UiKit.ChipWidth(leave) + 16, () =>
        {
            string why = DailyRun.Leave(() => SetOpen(false));
            if (why != null)
                Notice.Show("daily", why, 5f);
        });

        // The name for the board (12 characters, the last one kept), then Send or Don't send.
        var box = UiKit.Image("Name", page, PageShade).rectTransform.Place(32, 262, 280, 28);
        var area = UiKit.Rect("Text Area", box).Fill(4);
        area.gameObject.AddComponent<RectMask2D>();
        var text = Label(area, "", 18, Ink, TextAlignmentOptions.MidlineLeft);
        text.rectTransform.Fill();
        text.enableWordWrapping = false;
        var placeholder = Label(area, Loc.T("Your name for the board"), 18, InkDim, TextAlignmentOptions.MidlineLeft);
        placeholder.rectTransform.Fill();
        placeholder.fontStyle = FontStyles.Italic;
        _dailyName = box.gameObject.AddComponent<TMP_InputField>();
        _dailyName.textViewport = area;
        _dailyName.textComponent = text;
        _dailyName.placeholder = placeholder;
        _dailyName.fontAsset = text.font;
        _dailyName.pointSize = 18;
        _dailyName.characterLimit = 12;
        _dailyName.richText = false;
        _dailyName.caretColor = Ink;
        _dailyName.selectionColor = new Color(0.6f, 0.4f, 0.2f, 0.4f);

        string send = Loc.T("Send");
        _dailySendChip = new UiKit.Chip(page, send, 32 + 288, 262, UiKit.ChipWidth(send) + 16, () =>
        {
            if (DailyRun.Ended is not { } t)
                return;
            string name = (_dailyName.text ?? "").Trim();
            DailyRun.Send(t, name.Length > 0 ? name : "Skul");
            EventSystem.current?.SetSelectedGameObject(null);
            _listDirty = true;
            _nextRefresh = 0f;
        }, new Color32(0xC9, 0x9A, 0x3A, 0xFF));
        _dailySendChip.SetOn(true);
        string skip = Loc.T("Don't send");
        _dailySkipChip = new UiKit.Chip(page, skip, 0, 0, UiKit.ChipWidth(skip) + 10, () =>
        {
            if (DailyRun.Ended is { } t)
                DailyRun.Skip(t);
            _listDirty = true;
            _nextRefresh = 0f;
        });
        _dailySkipChip.Background.rectTransform.PlaceTopRight(32, 262, UiKit.ChipWidth(skip) + 10, 28);
    }

    /// <summary>After a daily run, back in the castle: the book opens on the Daily section for the name (once per run).</summary>
    private void OpenForDailyResult()
    {
        if (IsOpen || !DailyRun.InDailySlot || DailyRun.Ended is not { } t || _dailyOpenedFor == t.Key || !DailyRun.InCastle())
            return;
        _dailyOpenedFor = t.Key;
        DailyService.SelectBoard(t.Board); // the board the ended run is sent to
        SetOpen(true);
        _nav.SwitchSection(CodexSection.Daily);
        OnNavigated();
    }
}
