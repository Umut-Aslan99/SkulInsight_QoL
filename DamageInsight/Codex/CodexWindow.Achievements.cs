using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DamageInsight.Achievements;
using DamageInsight.History;
using DamageInsight.Lang;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The Achievements tab of the You section (0.14, V013_SPEC part C): Skul's achievements as this save slot has them.
/// Left: a grid of tiles in the game's order (earned in colour, not yet in grey, the story's secrets veiled until held
/// for 4 s), the filter All / Earned / Not earned, this slot's and Steam's counts, and slot 1's one-time "take over
/// Steam's achievements". Right: the selected one (how to earn it, progress, a tip, this slot, Steam).
/// </summary>
public sealed partial class CodexWindow
{
    private enum AchievementFilter { All, Earned, NotEarned }

    private sealed class AchievementTile
    {
        public GameObject Root;
        public Image Background, Icon, Badge, HoldFill;
        public Outline Border;
        public TextMeshProUGUI Name, Text, Mark;
        public HoldToReveal Hold;
        public string Id;
    }

    private const float AchievementTileWidth = 326f, AchievementTileHeight = 84f, AchievementGap = 6f;
    private static readonly Color Gold = new Color32(0xE8, 0xB8, 0x4A, 0xFF);
    private static readonly Color Bone = new Color32(0xF2, 0xEC, 0xDC, 0xFF);

    private RectTransform _achievementsPanel, _achievementsContent;
    private ScrollRect _achievementsScroll;
    private readonly Dictionary<AchievementFilter, UiKit.Chip> _achievementFilterChips = new();
    private readonly List<AchievementTile> _achievementTiles = new();
    private readonly List<TextMeshProUGUI> _achievementHeaders = new();
    private TextMeshProUGUI _achievementsCount, _achievementsNote;
    private UiKit.Chip _takeOverChip;
    private AchievementFilter _achievementFilter = AchievementFilter.All;
    private string _takeOverMessage = "";
    private int _achievementsDrawnVersion = -1;
    private float _nextIconPoll;
    private HoldToReveal _portraitHold;
    private Image _portraitHoldFill;

    private void ForgetAchievementWidgets()
    {
        _achievementFilterChips.Clear();
        _achievementTiles.Clear();
        _achievementHeaders.Clear();
        _achievementsCount = _achievementsNote = null;
        _takeOverChip = null;
        _achievementsPanel = _achievementsContent = null;
        _achievementsScroll = null;
        _portraitHold = null;
        _portraitHoldFill = null;
    }

    private bool OnAchievementsTab => _nav.Section == CodexSection.You && _category == CodexCategory.Achievements;

    /// <summary>Called every frame while the book is open: redraw when the slot's achievements changed or icons arrive.</summary>
    private bool AchievementsDirty()
    {
#if DEV
        // Developer builds: F7 shows the notice of the selected achievement (to look at it without earning one).
        if (OnAchievementsTab && Input.GetKeyDown(KeyCode.F7) && AchievementPages.InfoOf(_selectedId) is { } shown)
            AchievementToast.Show(shown);
#endif
        if (AchievementStore.Version != _achievementsDrawnVersion)
            return true;
        if (OnAchievementsTab && AchievementSteam.AnyWaiting && Time.unscaledTime >= _nextIconPoll)
        {
            _nextIconPoll = Time.unscaledTime + 0.5f;
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- building

    private void BuildAchievements(RectTransform panel)
    {
        float fx = 32;
        foreach (AchievementFilter filter in Enum.GetValues(typeof(AchievementFilter)))
        {
            string label = filter switch
            {
                AchievementFilter.Earned => Loc.T("Earned"),
                AchievementFilter.NotEarned => Loc.T("Not earned"),
                _ => Loc.T("All"),
            };
            float width = UiKit.ChipWidth(label);
            var chip = new UiKit.Chip(panel, label, fx, 124, width, () =>
            {
                _achievementFilter = filter;
                _listDirty = true;
                _nextRefresh = 0f;
                foreach (var pair in _achievementFilterChips)
                    pair.Value.SetOn(pair.Key == _achievementFilter);
                if (_achievementsScroll != null)
                    _achievementsScroll.verticalNormalizedPosition = 1f;
            });
            chip.SetOn(filter == _achievementFilter);
            _achievementFilterChips[filter] = chip;
            fx += width + 6;
        }
        _achievementsCount = Label(panel, "", 16, InkDim, TextAlignmentOptions.MidlineRight);
        _achievementsCount.rectTransform.Place(fx + 10, 124, PageWidth - 32 - fx - 10, 28);
        _achievementsCount.enableWordWrapping = false;
        _achievementsCount.overflowMode = TextOverflowModes.Ellipsis;

        float height = BookHeight - 24 - 160 - 32 - 40;
        var frame = UiKit.Image("Achievements", panel, PageShade).rectTransform.Place(32, 160, PageWidth - 64, height);
        var viewport = UiKit.Rect("Viewport", frame).Fill(4);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // catches the mouse wheel
        _achievementsContent = UiKit.Rect("Content", viewport);
        _achievementsContent.anchorMin = new Vector2(0, 1);
        _achievementsContent.anchorMax = new Vector2(1, 1);
        _achievementsContent.pivot = new Vector2(0.5f, 1);
        _achievementsContent.sizeDelta = Vector2.zero;
        // Tiles are placed by hand (DrawAchievementsList): two columns, a header line above each group.
        foreach (var _ in new[] { "Skul", "SkulInsight" })
        {
            var header = Label(_achievementsContent, "", 17, Ink, TextAlignmentOptions.BottomLeft);
            header.enableWordWrapping = false;
            header.overflowMode = TextOverflowModes.Ellipsis;
            _achievementHeaders.Add(header);
        }
        _achievementsScroll = frame.gameObject.AddComponent<ScrollRect>();
        _achievementsScroll.viewport = viewport;
        _achievementsScroll.content = _achievementsContent;
        _achievementsScroll.horizontal = false;
        _achievementsScroll.movementType = ScrollRect.MovementType.Clamped;
        _achievementsScroll.scrollSensitivity = 40f;

        float bottom = 160 + height + 8;
        _achievementsNote = Label(panel, "", 14, InkDim, TextAlignmentOptions.MidlineLeft);
        _achievementsNote.rectTransform.Place(32, bottom, PageWidth - 64, 28);
        _achievementsNote.overflowMode = TextOverflowModes.Ellipsis;
        string takeOver = Loc.T("Take over Steam's achievements");
        float takeOverWidth = UiKit.ChipWidth(takeOver) + 12;
        _takeOverChip = new UiKit.Chip(panel, takeOver, PageWidth - 32 - takeOverWidth, bottom, takeOverWidth, () =>
        {
            int added = AchievementTracker.TakeOverSteam();
            _takeOverMessage = Loc.P("Took over {0} achievement from Steam.", "Took over {0} achievements from Steam.", added);
            _listDirty = true;
            _nextRefresh = 0f;
        });
    }

    private AchievementTile NewAchievementTile(AchievementInfo info)
    {
        var bg = UiKit.Image("Achievement", _achievementsContent, new Color(0, 0, 0, 0));
        var tile = new AchievementTile { Root = bg.gameObject, Background = bg, Id = AchievementPages.IdOf(info) };
        tile.Border = bg.gameObject.AddComponent<Outline>();
        tile.Border.effectDistance = new Vector2(1.5f, -1.5f);
        var t = bg.transform;

        var iconFrame = UiKit.Image("Icon frame", t, TileShade);
        iconFrame.rectTransform.Place(10, 10, 64, 64);
        iconFrame.raycastTarget = false;
        tile.Icon = UiKit.Image("Icon", iconFrame.transform, Color.white);
        tile.Icon.preserveAspect = true;
        tile.Icon.raycastTarget = false;
        tile.Icon.rectTransform.Fill(2);
        tile.Mark = Label(iconFrame.transform, "?", 40, Bone, TextAlignmentOptions.Center);
        tile.Mark.rectTransform.Fill();
        tile.Badge = UiKit.Image("Badge", iconFrame.transform, Color.white, PixelIcons.Get("crown"));
        tile.Badge.preserveAspect = true;
        tile.Badge.raycastTarget = false;
        tile.Badge.rectTransform.Place(44, 44, 22, 22);

        tile.Name = Label(t, "", 17, Ink, TextAlignmentOptions.TopLeft);
        tile.Name.rectTransform.Place(84, 5, AchievementTileWidth - 92, 28);
        tile.Name.enableWordWrapping = false;
        tile.Name.overflowMode = TextOverflowModes.Ellipsis;
        tile.Text = Label(t, "", 13, InkDim, TextAlignmentOptions.TopLeft);
        tile.Text.rectTransform.Place(84, 33, AchievementTileWidth - 92, 44);
        tile.Text.overflowMode = TextOverflowModes.Ellipsis;

        tile.HoldFill = UiKit.Image("Hold", t, Gold);
        tile.HoldFill.raycastTarget = false;
        tile.HoldFill.rectTransform.Place(0, AchievementTileHeight - 5, 0, 5);

        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            _nav.Select(tile.Id);
            _listDirty = true;
            _nextRefresh = 0f;
            RefreshSections(); // the Back button
            EventSystem.current?.SetSelectedGameObject(null);
        });
        tile.Hold = bg.gameObject.AddComponent<HoldToReveal>();
        tile.Hold.Progress = f =>
        {
            if (tile.HoldFill != null)
                tile.HoldFill.rectTransform.sizeDelta = new Vector2(AchievementTileWidth * f, 5);
        };
        tile.Hold.Done = () => Reveal(info);
        return tile;
    }

    private void Reveal(AchievementInfo info)
    {
        AchievementStore.Reveal(info.Id);
        MenuSounds.Play(MenuSounds.Kind.Select);
        Plugin.Log.LogInfo($"Achievements: {info.Id} revealed by holding");
        _listDirty = true;
        _nextRefresh = 0f;
    }

    // ---------------------------------------------------------------- the list

    private void DrawAchievementsList()
    {
        _achievementsDrawnVersion = AchievementStore.Version;
        AchievementTracker.CheckProofs();
        var all = AchievementCatalog.All;
        var every = all.Concat(AchievementCatalog.Daily).ToList();
        while (_achievementTiles.Count < every.Count)
            _achievementTiles.Add(NewAchievementTile(every[_achievementTiles.Count]));

        // Two groups, each under its header: Skul's achievements (this slot's), then SkulInsight's daily ones.
        var shown = new List<AchievementInfo>();
        AchievementTile selectedTile = null;
        float y = 2;
        for (int group = 0; group < 2; group++)
        {
            var infos = group == 0 ? all : AchievementCatalog.Daily;
            int offset = group == 0 ? 0 : all.Count, column = 0, before = shown.Count;
            var header = _achievementHeaders[group];
            float headerY = y;
            y += 34;
            for (int i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                var tile = _achievementTiles[offset + i];
                bool earned = AchievementTracker.EarnedOf(info) != null;
                bool visible = _achievementFilter switch
                {
                    AchievementFilter.Earned => earned,
                    AchievementFilter.NotEarned => !earned,
                    _ => true,
                };
                tile.Root.SetActive(visible);
                if (!visible)
                    continue;
                shown.Add(info);
                PaintTile(tile, info, earned);
                ((RectTransform)tile.Root.transform).Place(2 + column * (AchievementTileWidth + AchievementGap), y, AchievementTileWidth, AchievementTileHeight);
                if (tile.Id == _selectedId)
                    selectedTile = tile;
                if (++column == 2)
                {
                    column = 0;
                    y += AchievementTileHeight + AchievementGap;
                }
            }
            if (column > 0)
                y += AchievementTileHeight + AchievementGap;
            bool any = shown.Count > before;
            header.gameObject.SetActive(any);
            if (!any)
            {
                y = headerY;
                continue;
            }
            int earnedHere = infos.Count(a => AchievementTracker.EarnedOf(a) != null);
            string title = group == 0 ? "Skul: The Hero Slayer" : Loc.T("Daily runs") + " · SkulInsight";
            header.text = $"{title}  <color={DamageInsight.History.RunPages.DimColor}>{earnedHere} / {infos.Count}</color>";
            header.rectTransform.Place(8, headerY, AchievementTileWidth * 2 - 12, 30);
            y += 8;
        }
        _achievementsContent.sizeDelta = new Vector2(0, y);

        int steam = -1;
        foreach (var info in all.Where(a => a.OnSteam))
            if (AchievementSteam.TryState(info.Id, out bool has, out _))
                steam = Math.Max(steam, 0) + (has ? 1 : 0);
        _achievementsCount.text = AchievementPages.Count(AchievementStore.Count(), all.Count, steam, AchievementCatalog.SteamCount);
        bool canTakeOver = AchievementTracker.CanTakeOverSteam && steam > 0;
        _takeOverChip.Background.gameObject.SetActive(canTakeOver);
        _achievementsNote.rectTransform.sizeDelta = new Vector2(PageWidth - 64 - (canTakeOver ? _takeOverChip.Background.rectTransform.sizeDelta.x + 12 : 0), 28);
        _achievementsNote.text = canTakeOver ? Loc.T("Slot 1 is the game you had before: it can take over your Steam account's achievements once.")
            : _takeOverMessage.Length > 0 ? _takeOverMessage
            : Loc.T("Each save slot earns its own achievements, also the ones Steam already has.");

        if (AchievementPages.InfoOf(_selectedId) == null && shown.Count > 0)
            _nav.SelectQuietly(AchievementPages.IdOf(shown[0]));
        if (_scrollToSelected && selectedTile != null)
            ScrollAchievementsTo(selectedTile, y);
    }

    private static Sprite AchievementIcon(AchievementInfo info, bool grey) =>
        info.Kind == AchievementKind.Daily ? PixelIcons.Get(info.Pixel) : AchievementSteam.Icon(info.OnSteam ? info.Id : info.BaseId, grey);

    private void PaintTile(AchievementTile tile, AchievementInfo info, bool earned)
    {
        bool unveiled = AchievementTracker.Unveiled(info);
        bool selected = tile.Id == _selectedId;
        tile.Hold.Armed = !unveiled;
        tile.Badge.gameObject.SetActive(earned);
        if (!unveiled)
        {
            tile.Background.color = new Color32(0x3A, 0x24, 0x1A, 0xE8);
            tile.Border.effectColor = selected ? Gold : (Color)new Color32(0x24, 0x15, 0x0E, 0xFF);
            tile.Icon.color = new Color(0, 0, 0, 0);
            tile.Mark.text = "?";
            tile.Name.text = Loc.T("Hidden achievement");
            tile.Name.color = Bone;
            tile.Text.text = Loc.T("Hold the mouse button on it to reveal it.");
            tile.Text.color = Bone with { a = 0.7f };
            return;
        }
        var view = new AchievementView { Info = info, Unveiled = true };
        var icon = AchievementIcon(info, grey: !earned);
        tile.Icon.sprite = icon;
        tile.Icon.color = icon == null ? new Color(0, 0, 0, 0) : earned ? Color.white : NotEarnedTint(info);
        tile.Mark.text = icon == null ? "…" : "";
        tile.Name.text = AchievementPages.Title(view);
        tile.Text.text = AchievementPages.TileText(view);
        if (earned)
        {
            tile.Background.color = selected ? RowSelected : SectionShade;
            tile.Border.effectColor = selected ? Accent : (Color)new Color32(0xA8, 0x74, 0x24, 0xFF);
            tile.Name.color = Ink;
            tile.Text.color = InkDim;
        }
        else
        {
            tile.Background.color = selected ? RowSelected : new Color(0, 0, 0, 0.05f);
            tile.Border.effectColor = selected ? Accent : SectionBorder with { a = 0.5f };
            tile.Name.color = InkDim;
            tile.Text.color = InkDim with { a = 0.8f };
        }
    }

    /// <summary>Not earned: Steam's icons are greyed already (a little faded here), the mod's pixel icons are tinted grey.</summary>
    private static Color NotEarnedTint(AchievementInfo info) =>
        info.Kind == AchievementKind.Daily ? new Color(0.55f, 0.5f, 0.45f, 0.8f) : new Color(1, 1, 1, 0.85f);

    private void ScrollAchievementsTo(AchievementTile tile, float content)
    {
        _scrollToSelected = false;
        float view = ((RectTransform)_achievementsScroll.viewport).rect.height;
        if (content <= view)
            return;
        float top = -((RectTransform)tile.Root.transform).anchoredPosition.y - 40;
        _achievementsScroll.verticalNormalizedPosition = Mathf.Clamp01(1f - top / (content - view));
    }

    // ---------------------------------------------------------------- the page

    private void DrawAchievementDetail()
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

        var info = AchievementPages.InfoOf(_selectedId);
        EnsurePortraitHold();
        if (info == null)
        {
            _title.text = "";
            _subtitle.text = "";
            _portrait.color = new Color(0, 0, 0, 0);
            _portraitHold.Armed = false;
            SetPage("");
            return;
        }
        var view = ViewOf(info);
        bool earned = view.Earned != null;
        var icon = view.Unveiled ? AchievementIcon(info, grey: !earned) : null;
        _portrait.sprite = icon;
        _portrait.color = icon == null ? new Color(0, 0, 0, 0) : earned ? Color.white : NotEarnedTint(info);
        _portraitMark.text = view.Unveiled ? icon == null ? "…" : "" : "?";
        _portraitHold.Armed = !view.Unveiled;
        // The frame is the same on every Codex page: reveal only while this achievement is still the one shown.
        _portraitHold.Done = () =>
        {
            if (OnAchievementsTab && AchievementPages.InfoOf(_selectedId) == info)
                Reveal(info);
        };
        _title.text = AchievementPages.Title(view);
        _title.color = earned ? Ink : InkDim;
        _subtitle.text = AchievementPages.Subtitle(view);
        SetPage(AchievementPages.Text(view));
        string shownFor = "achievement#" + info.Id;
        if (shownFor != _textShownFor)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>The picture frame holds to reveal too (a bar fills along its bottom edge).</summary>
    private void EnsurePortraitHold()
    {
        if (_portraitHold != null)
            return;
        _portraitHoldFill = UiKit.Image("Hold", _portraitFrame, Gold);
        _portraitHoldFill.raycastTarget = false;
        _portraitHoldFill.rectTransform.anchorMin = _portraitHoldFill.rectTransform.anchorMax = _portraitHoldFill.rectTransform.pivot = Vector2.zero;
        _portraitHoldFill.rectTransform.sizeDelta = new Vector2(0, 6);
        _portraitHold = _portraitFrame.gameObject.AddComponent<HoldToReveal>();
        _portraitHold.Progress = f =>
        {
            if (_portraitHoldFill != null)
                _portraitHoldFill.rectTransform.sizeDelta = new Vector2(_portraitFrame.rect.width * f, 6);
        };
    }

    /// <summary>Everything the page shows of one achievement, read from the slot, Steam, the save and the Codex.</summary>
    private static AchievementView ViewOf(AchievementInfo info)
    {
        var view = new AchievementView
        {
            Info = info, Earned = AchievementTracker.EarnedOf(info), Unveiled = AchievementTracker.Unveiled(info),
            DailyEnabled = DamageInsight.Daily.DailyService.Enabled,
        };
        if (info.OnSteam && AchievementSteam.TryState(info.Id, out bool has, out var at))
        {
            view.OnSteamAccount = has;
            view.SteamAt = at;
        }
        view.Percent = info.OnSteam ? AchievementSteam.Percent(info.Id) : null;
        view.Progress = AchievementTracker.Progress(info);
        if (info.Boss.Length > 0 && Known(info.Boss) is { } boss)
            view.BossName = boss.Name;
        if (info.Kind == AchievementKind.Skull)
        {
            var skull = CodexCatalog.Entries.FirstOrDefault(e => e.Category == CodexCategory.Skulls && e.Group == "Legendary"
                                                                 && AchievementCatalog.ForSkull(e.Key) == info);
            if (skull != null && Known(skull.Id) != null)
            {
                view.SkullPage = skull.Id;
                view.SkullName = skull.Name;
            }
        }
        string run = view.Earned?.Run ?? "";
        if (run.Length > 0 && File.Exists(RunJournal.PathOf(run))
            && DateTime.TryParseExact(run, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var started))
            view.RunDate = RunPages.Date(started);
        return view;
    }

    /// <summary>A Codex page the player has discovered (a link to "???" would give it away), else null.</summary>
    private static CodexEntry Known(string id)
    {
        var entry = CodexPages.Entry(id);
        return entry != null && CodexTiers.Tier(entry.Category, CodexTracker.Page(entry.Id), entry.Id) > 0 ? entry : null;
    }
}
