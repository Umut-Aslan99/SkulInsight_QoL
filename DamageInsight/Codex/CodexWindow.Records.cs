using System;
using System.Collections.Generic;
using DamageInsight.History;
using DamageInsight.Lang;
using DamageInsight.UI;
using Platforms;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The Records tab of the You section (0.13, spec B): the slot's run history added up, next to the game's own
/// permanent records. Left: a row per kind of record (totals, bests, skulls, bosses, deaths, the game's records) with
/// a headline number, and the filter All / Normal / Dark Mirror. Right: the selected kind in full (RecordPages), with
/// links to Codex pages and to the runs that set a best.
/// </summary>
public sealed partial class CodexWindow
{
    private sealed class RecordRow
    {
        public Image Background, Icon;
        public TextMeshProUGUI Title, Detail, Headline, Caption;
        public string Id;
    }

    private const float RecordRowHeight = 92f;

    private RectTransform _recordsPanel;
    private readonly Dictionary<RecordFilter, UiKit.Chip> _recordFilterChips = new();
    private readonly List<RecordRow> _recordRows = new();
    private TextMeshProUGUI _recordsCount, _recordsNote;
    private RecordFilter _recordFilter = RecordFilter.All;
    private RunRecords _records;
    private int _recordsVersion = -1;
    private GameRecords _gameRecords;

    private void ForgetRecordWidgets()
    {
        _recordFilterChips.Clear();
        _recordRows.Clear();
        _recordsCount = _recordsNote = null;
        _recordsPanel = null;
    }

    // ---------------------------------------------------------------- building

    private void BuildRecords(RectTransform panel)
    {
        float fx = 32;
        foreach (RecordFilter filter in Enum.GetValues(typeof(RecordFilter)))
        {
            string label = RecordPages.FilterName(filter);
            float width = UiKit.ChipWidth(label);
            var chip = new UiKit.Chip(panel, label, fx, 124, width, () =>
            {
                _recordFilter = filter;
                _listDirty = true;
                _nextRefresh = 0f;
                foreach (var pair in _recordFilterChips)
                    pair.Value.SetOn(pair.Key == _recordFilter);
            });
            chip.SetOn(filter == _recordFilter);
            _recordFilterChips[filter] = chip;
            fx += width + 6;
        }
        _recordsCount = Label(panel, "", 16, InkDim, TextAlignmentOptions.MidlineRight);
        _recordsCount.rectTransform.Place(fx + 10, 124, PageWidth - 32 - fx - 10, 28);
        _recordsCount.enableWordWrapping = false;

        // Six rows fit the page: no scrolling, the note about where the numbers come from below them.
        var frame = UiKit.Image("Records", panel, PageShade).rectTransform.Place(32, 160, PageWidth - 64, BookHeight - 24 - 160 - 32);
        float y = 6;
        foreach (RecordPages.Page page in Enum.GetValues(typeof(RecordPages.Page)))
        {
            _recordRows.Add(NewRecordRow(frame, page, y));
            y += RecordRowHeight + 4;
        }
        _recordsNote = Label(frame, "", 14, InkDim, TextAlignmentOptions.TopLeft);
        _recordsNote.rectTransform.Place(12, y + 4, PageWidth - 64 - 24, BookHeight - 24 - 160 - 32 - y - 8);
    }

    private RecordRow NewRecordRow(RectTransform frame, RecordPages.Page page, float y)
    {
        var bg = UiKit.Image("Record", frame, new Color(0, 0, 0, 0));
        bg.rectTransform.Place(4, y, PageWidth - 64 - 8, RecordRowHeight);
        var row = new RecordRow { Background = bg, Id = RecordPages.IdOf(page) };
        var t = bg.transform;

        var iconFrame = UiKit.Image("Icon frame", t, TileShade);
        iconFrame.rectTransform.Place(8, 10, 72, 72);
        var outline = iconFrame.gameObject.AddComponent<Outline>();
        outline.effectColor = SectionBorder;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        row.Icon = UiKit.Image("Icon", iconFrame.transform, Color.white);
        row.Icon.preserveAspect = true;
        row.Icon.rectTransform.Fill(5);

        row.Title = Label(t, "", 24, Ink, TextAlignmentOptions.TopLeft);
        row.Title.rectTransform.Place(94, 8, 340, 32);
        row.Title.enableWordWrapping = false;
        row.Title.overflowMode = TextOverflowModes.Ellipsis;
        row.Detail = Label(t, "", 15, InkDim, TextAlignmentOptions.TopLeft);
        row.Detail.rectTransform.Place(94, 42, 350, 44);
        row.Detail.overflowMode = TextOverflowModes.Ellipsis;
        row.Headline = Label(t, "", 30, Ink, TextAlignmentOptions.TopRight);
        row.Headline.rectTransform.Place(450, 6, 210, 40);
        row.Headline.enableWordWrapping = false;
        row.Headline.overflowMode = TextOverflowModes.Ellipsis;
        row.Caption = Label(t, "", 14, InkDim, TextAlignmentOptions.TopRight);
        row.Caption.rectTransform.Place(450, 48, 210, 38);
        row.Caption.overflowMode = TextOverflowModes.Ellipsis;

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            _nav.Select(row.Id);
            _listDirty = true;
            _nextRefresh = 0f;
            RefreshSections(); // the Back button
            EventSystem.current?.SetSelectedGameObject(null);
        });
        UiKit.OnHover(bg.gameObject, on =>
        {
            if (bg != null)
                bg.color = row.Id == _selectedId ? RowSelected : on ? new Color32(0xD9, 0xC8, 0xA8, 0xFF) : new Color(0, 0, 0, 0);
        });
        return row;
    }

    // ---------------------------------------------------------------- the list

    /// <summary>The run history added up for the filter; again only when a run was written or the filter changed.</summary>
    private RunRecords Records()
    {
        if (_records == null || _recordsVersion != RunJournal.Version || _records.Filter != _recordFilter)
        {
            _records = RunRecords.Of(RunJournal.Runs(), _recordFilter);
            _recordsVersion = RunJournal.Version;
        }
        return _records;
    }

    private void DrawRecordsList()
    {
        var records = Records();
        // The game is paused while the book is open: reading the save once per drawing is enough.
        _gameRecords = ReadGameRecords();
        _recordsCount.text = records.Runs == 0 ? "" : Loc.F("{0} runs · {1} won", records.Runs, records.Wins);
        foreach (var row in _recordRows)
        {
            var page = RecordPages.PageOf(row.Id);
            var (detail, headline, caption) = RecordPages.Row(page, records, _gameRecords, Lookup);
            row.Title.text = $"<b>{RecordPages.Title(page)}</b>";
            row.Detail.text = detail;
            row.Headline.text = $"<b>{headline}</b>";
            row.Caption.text = caption;
            var icon = PageIcon(RecordPages.Icon(page, records));
            row.Icon.sprite = icon;
            row.Icon.color = icon == null ? new Color(0, 0, 0, 0) : Color.white;
            row.Background.color = row.Id == _selectedId ? RowSelected : new Color(0, 0, 0, 0);
        }
        _recordsNote.text = RecordPages.Note(records);
        if (_selectedId == null)
            _nav.SelectQuietly(RecordPages.IdOf(RecordPages.Page.Totals));
    }

    // ---------------------------------------------------------------- the page

    private void DrawRecordDetail()
    {
        ApplyLayout(PageLayout.Sheet);
        _stage = 3;
        _clipCount = 0;
        _clipPrev.Background.gameObject.SetActive(false);
        _clipNext.Background.gameObject.SetActive(false);
        _clipLabel.text = "";
        foreach (var chip in new[] { _filmChip, _modeChip, _memberChip, _categoryChip })
            chip.Background.gameObject.SetActive(false);
        _nextText.text = "";
        _portraitMark.text = "";
        ShowRunChips(false);

        var page = RecordPages.PageOf(_selectedId);
        var records = Records();
        _gameRecords ??= ReadGameRecords();
        var icon = PageIcon(RecordPages.Icon(page, records));
        _portrait.sprite = icon;
        _portrait.color = icon == null ? new Color(0, 0, 0, 0) : Color.white;
        _title.text = RecordPages.Title(page);
        _title.color = Ink;
        _subtitle.text = RecordPages.Subtitle(page, records);
        SetPage(RecordPages.Text(page, records, _gameRecords, Lookup));
        string shownFor = "records#" + page + "#" + _recordFilter;
        if (shownFor != _textShownFor)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>The game's own permanent records of this save (GameData; the save goes with the slot).</summary>
    private static GameRecords ReadGameRecords()
    {
        var game = new GameRecords();
        try
        {
            game.Deaths = Data.GameData.Progress.deaths;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Records: the save's deaths not read: {e.Message}");
        }
        try
        {
            game.ClearedLevel = Data.GameData.HardmodeProgress.clearedLevel;
            game.ClearedCount = Data.GameData.HardmodeProgress.clearedCount;
            var castle = Data.GameData.HardmodeProgress.instance.demonCastleDefense;
            game.HiddenBossLevel = castle.hiddenBossClearedLevel;
            game.EmperorCleared = castle.emperorCleared;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Records: the Dark Mirror records not read: {e.Message}");
        }
        try
        {
            // Every "Record/bestTime/<chapter>/<stage>" of the save in one pass (GetBestTime searches the list each time).
            foreach (var value in PersistentSingleton<PlatformManager>.Instance.platform.data.IntData)
                game.AddBestTime(value.Key, value.Value);
            foreach (var time in game.BestTimes)
                time.Name = CodexCatalog.Localize(GameRecords.StageNameKey(time.Chapter, time.Stage));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Records: the best stage times not read: {e.Message}");
        }
        return game;
    }
}
