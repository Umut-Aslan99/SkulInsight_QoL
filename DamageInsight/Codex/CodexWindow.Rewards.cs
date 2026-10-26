using System.Collections.Generic;
using System.Linq;
using DamageInsight.Achievements;
using DamageInsight.Daily;
using DamageInsight.Lang;
using DamageInsight.Rewards;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The Rewards tab of the You section (0.15, docs/V015_SPEC.md part D): a row per kind of reward on the left
/// (achievement rewards, coins, kill quests, inscription activations) with a headline number, its page on the right
/// (RewardPages). The pages' Summon / +1 links use a coin or an activation right away; what happened shows at the
/// top of the page.
/// </summary>
public sealed partial class CodexWindow
{
    private RectTransform _rewardsPanel;
    private readonly List<RecordRow> _rewardRows = new();
    private TextMeshProUGUI _rewardsCount, _rewardsNote;
    private string _rewardMessage = "";

    private void ForgetRewardWidgets()
    {
        _rewardRows.Clear();
        _rewardsCount = _rewardsNote = null;
        _rewardsPanel = null;
    }

    private void BuildRewards(RectTransform panel)
    {
        _rewardsCount = Label(panel, "", 16, InkDim, TextAlignmentOptions.MidlineRight);
        _rewardsCount.rectTransform.Place(32, 124, PageWidth - 64, 28);
        _rewardsCount.enableWordWrapping = false;

        var frame = UiKit.Image("Rewards", panel, PageShade).rectTransform.Place(32, 160, PageWidth - 64, BookHeight - 24 - 160 - 32);
        float y = 6;
        foreach (RewardPages.Page page in System.Enum.GetValues(typeof(RewardPages.Page)))
        {
            _rewardRows.Add(NewRecordRow(frame, RewardPages.IdOf(page), y));
            y += RecordRowHeight + 4;
        }
        _rewardsNote = Label(frame, "", 14, InkDim, TextAlignmentOptions.TopLeft);
        _rewardsNote.rectTransform.Place(12, y + 4, PageWidth - 64 - 24, BookHeight - 24 - 160 - 32 - y - 8);
    }

    /// <summary>What the pages need from the game right now.</summary>
    private RewardContext RewardsNow() => new()
    {
        Off = Plugin.RewardsEnabled?.Value != true ? Loc.T("Rewards are off (settings, Rewards).")
            : DailyRun.InDailySlot ? Loc.T("Rewards are never used in daily runs.") : "",
        Earned = new HashSet<string>(AchievementStore.All.Select(e => e.Id)),
        Totals = RewardApplier.Totals,
        Name = id => CodexPages.Entry(id)?.Name ?? id.Substring(id.IndexOf(':') + 1),
        Veiled = info => !AchievementTracker.Unveiled(info),
        WhyNoSummon = RewardService.WhyNoSummon,
        WhyNoActivation = RewardService.WhyNoActivation(),
        SummonLimit = Plugin.RewardsSummonsPerRun.Value,
        CoinsPerSummon = Plugin.RewardsCoinsPerSummon.Value,
        ActivationLimit = Plugin.RewardsActivationsPerRun.Value,
        AdventurerKills = Plugin.RewardsAdventurerKills.Value,
        OmenChoices = RewardService.OmenChoices(),
        ActivationChoices = RewardService.ActivationChoices()
            .Select(k => (k.ToString(), CodexPages.Entry(CodexCatalog.InscriptionId(k))?.Name ?? k.ToString())).ToList(),
        Enemies = CodexCatalog.Entries.Where(e => e.Category == CodexCategory.Enemies).Select(e => (e.Id, e.Name)).ToList(),
        QuestStep = RewardService.QuestStepOf,
        QuestDarkQuartz = Plugin.RewardsQuestDarkQuartz.Value,
        QuestGold = Plugin.RewardsQuestGold.Value,
        QuestsPerRun = RewardService.QuestsPerRun(),
        Message = _rewardMessage,
    };

    private void DrawRewardsList()
    {
        var context = RewardsNow();
        _rewardsCount.text = context.Off;
        foreach (var row in _rewardRows)
        {
            var page = RewardPages.PageOf(row.Id);
            var (detail, headline, caption) = RewardPages.Row(page, context);
            row.Title.text = RewardPages.Title(page);
            row.Detail.text = detail;
            row.Headline.text = headline;
            row.Caption.text = caption;
            var icon = PageIcon(RewardPages.Icon(page));
            row.Icon.sprite = icon;
            row.Icon.color = icon == null ? new Color(0, 0, 0, 0) : Color.white;
            row.Background.color = row.Id == _selectedId ? RowSelected : new Color(0, 0, 0, 0);
        }
        _rewardsNote.text = Loc.T("Rewards belong to this save slot, like its achievements. Turn them off on the settings page (Rewards, at the top); everything earned is kept.");
        if (_selectedId == null)
            _nav.SelectQuietly(RewardPages.Default);
    }

    private void DrawRewardDetail()
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

        var page = RewardPages.PageOf(_selectedId);
        string shownFor = "rewards#" + page;
        bool newPage = shownFor != _textShownFor;
        if (newPage)
            _rewardMessage = ""; // what the last click did belongs to its page
        var context = RewardsNow();
        var icon = PageIcon(RewardPages.Icon(page));
        _portrait.sprite = icon;
        _portrait.color = icon == null ? new Color(0, 0, 0, 0) : Color.white;
        _title.text = RewardPages.Title(page);
        _title.color = Ink;
        _subtitle.text = RewardPages.Subtitle(page, context);
        SetPage(RewardPages.Text(page, context));
        if (newPage)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    private static TMP_SpriteAsset _currencySprites;
    private static bool _currencySearched;

    /// <summary>The game's sprite set with its currency icons ("Others/Gold_Icon", "Others/DarkQuartz_Icon"), or null.</summary>
    private static TMP_SpriteAsset CurrencySprites()
    {
        if (_currencySprites != null || _currencySearched)
            return _currencySprites;
        _currencySearched = true;
        try
        {
            var assets = new List<TMP_SpriteAsset>();
            if (TMP_Settings.defaultSpriteAsset != null)
                assets.Add(TMP_Settings.defaultSpriteAsset);
            assets.AddRange(Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>());
            _currencySprites = assets.FirstOrDefault(a => a != null && a.GetSpriteIndexFromName("Others/Gold_Icon") >= 0
                                                                && a.GetSpriteIndexFromName("Others/DarkQuartz_Icon") >= 0);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Codex: the game's currency icons not found: {e.Message}");
        }
        return _currencySprites;
    }

    /// <summary>
    /// A kill quest (Umut 2026-10-26: "a square that fills up with a blue bar and the reward shows in the middle"): the
    /// square fills up from the bottom, the reward in its middle, the quest's words next to it.
    /// </summary>
    private void AddMeterRow(Transform parent, PageBlocks.Item item)
    {
        var row = UiKit.Rect("Meter", parent);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14;
        layout.padding = new RectOffset(0, 0, 10, 4);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var square = UiKit.Image("Square", row, TileShade);
        var outline = square.gameObject.AddComponent<Outline>();
        outline.effectColor = SectionBorder;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        var size = square.gameObject.AddComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = 108;
        var inner = UiKit.Rect("Inner", square.transform);
        inner.Fill(4);
        var fill = UiKit.Image("Fill", inner, new Color32(0x5B, 0x9B, 0xD5, 0xD0));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(1, (float)item.Fill);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        var center = Label(square.transform, item.Value, 21, Ink, TextAlignmentOptions.Center);
        center.enableWordWrapping = false;
        center.rectTransform.Fill(4);
        if (CurrencySprites() is { } sprites)
            center.spriteAsset = sprites;

        var text = Label(row, item.Text, 18, Ink, TextAlignmentOptions.MidlineLeft);
        text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AddLinkClicks(text);
    }

    /// <summary>A Summon / +1 link: uses the coin or activation now and shows what happened.</summary>
    private void UseReward(string link)
    {
        string message = RewardService.Use(link);
        if (_category != CodexCategory.Rewards)
        {
            // A Summon on a gear's page: the Coins page says what happened.
            _nav.Go(new CodexNavigation.Place(CodexSection.You, CodexCategory.Rewards, RewardPages.IdOf(RewardPages.Page.Coins)));
            OnNavigated();
            _textShownFor = "rewards#" + RewardPages.Page.Coins; // keeps the message below
        }
        _rewardMessage = message;
        _listDirty = true;
        _nextRefresh = 0f;
        Invalidate();
    }
}
