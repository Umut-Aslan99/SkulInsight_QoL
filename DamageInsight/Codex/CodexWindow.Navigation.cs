using System;
using System.Collections.Generic;
using DamageInsight.Lang;
using DamageInsight.UI;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The book's sections (You / Bestiary / Arsenal) with their sub-tabs, the Back button, clickable names in page texts
/// and the right page's layouts (big picture for creatures, small picture and long text for everything else).
/// </summary>
public sealed partial class CodexWindow
{
    private RectTransform _listPanel, _youPanel, _portraitFrame, _bar, _textFrame;
    private readonly Dictionary<CodexSection, UiKit.Chip> _sectionChips = new();
    private UiKit.Chip _backChip;
    private bool _scrollToSelected;

    private enum PageLayout { Creature, Gear, Sheet, Run }
    private PageLayout? _layout;

    private void ForgetPageWidgets()
    {
        _sectionChips.Clear();
        _backChip = null;
        _layout = null;
        ForgetYouWidgets();
        ForgetRunWidgets();
        ForgetDailyWidgets();
        ForgetRecordWidgets();
        ForgetAchievementWidgets();
        ForgetRewardWidgets();
    }

    private void BuildSectionRow(RectTransform page)
    {
        float x = 32 + 170;
        foreach (CodexSection section in Enum.GetValues(typeof(CodexSection)))
        {
            string label = SectionLabel(section);
            float width = UiKit.ChipWidth(label) + 16;
            var chip = new UiKit.Chip(page, label, x, 32, width, () =>
            {
                _nav.SwitchSection(section);
                _scrollToSelected = true;
                OnNavigated();
            });
            chip.Background.rectTransform.sizeDelta = new Vector2(width, 32);
            chip.Label.fontSize = 18;
            _sectionChips[section] = chip;
            x += width + 6;
        }
        string back = "< " + Loc.T("Back");
        float backWidth = UiKit.ChipWidth(back);
        _backChip = new UiKit.Chip(page, back, 0, 0, backWidth, GoBack);
        _backChip.Background.rectTransform.Place(PageWidth - 32 - backWidth, 34, backWidth, 28);
    }

    private static string SectionLabel(CodexSection section) => section switch
    {
        CodexSection.You => Loc.T("You"),
        CodexSection.Daily => Loc.T("Daily"),
        CodexSection.Bestiary => Loc.T("Bestiary"),
        _ => Loc.T("Arsenal"),
    };

    /// <summary>Shows the current section's panel and sub-tabs, marks the current chips, and the Back button.</summary>
    private void RefreshSections()
    {
        foreach (var pair in _sectionChips)
            pair.Value.SetOn(pair.Key == _nav.Section);
        bool you = _nav.Section == CodexSection.You, runs = you && _nav.Category == CodexCategory.Runs,
            records = you && _nav.Category == CodexCategory.Records, achievements = you && _nav.Category == CodexCategory.Achievements,
            rewards = you && _nav.Category == CodexCategory.Rewards;
        bool daily = _nav.Section == CodexSection.Daily;
        _listPanel?.gameObject.SetActive(!you && !daily);
        _dailyPanel?.gameObject.SetActive(daily);
        foreach (var pair in _dailyTabs)
        {
            pair.Value.Background.gameObject.SetActive(daily);
            pair.Value.SetOn(pair.Key == _nav.Category);
        }
        _youPanel?.gameObject.SetActive(you && !runs && !records && !achievements && !rewards);
        _runsPanel?.gameObject.SetActive(runs);
        _recordsPanel?.gameObject.SetActive(records);
        _achievementsPanel?.gameObject.SetActive(achievements);
        _rewardsPanel?.gameObject.SetActive(rewards);
        foreach (var pair in _youTabs)
        {
            pair.Value.Background.gameObject.SetActive(you);
            pair.Value.SetOn(pair.Key == _nav.Category);
        }
        foreach (var pair in _tabs)
            pair.Value.Background.gameObject.SetActive(CodexNavigation.SectionOf(pair.Key) == _nav.Section);
        _backChip?.Background.gameObject.SetActive(_nav.CanGoBack);
        RefreshTabs();
    }

    /// <summary>After any page change: redraw soon, with the right panel, tabs and Back button.</summary>
    private void OnNavigated()
    {
        RefreshSections();
        _listDirty = true;
        _nextRefresh = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void GoBack()
    {
        if (!_nav.Back())
            return;
        _scrollToSelected = true;
        OnNavigated();
    }

    /// <summary>A name in a page text was clicked: open that page (in its own section and tab).</summary>
    private void FollowLink(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;
        if (id.StartsWith("url:https://", StringComparison.Ordinal))
        {
            Application.OpenURL(id.Substring(4)); // the daily page's shoutout
            return;
        }
        if (DamageInsight.Rewards.RewardPages.IsAction(id))
        {
            UseReward(id); // a Summon / +1 link on the Rewards page
            return;
        }
        if (id.StartsWith("you:"))
        {
            _nav.Go(new CodexNavigation.Place(CodexSection.You, CodexCategory.YouBuild, id));
            OnNavigated();
            return;
        }
        if (id.StartsWith(DamageInsight.Achievements.AchievementPages.Prefix, StringComparison.Ordinal))
        {
            // An achievement earned in a run (the run's page): its page in the Achievements tab.
            _nav.Go(new CodexNavigation.Place(CodexSection.You, CodexCategory.Achievements, id));
            _scrollToSelected = true;
            OnNavigated();
            return;
        }
        if (id.StartsWith(DamageInsight.History.RecordPages.RunLink, StringComparison.Ordinal))
        {
            // A best on the Records page: its run's page in the Runs tab.
            _nav.Go(new CodexNavigation.Place(CodexSection.You, CodexCategory.Runs, id.Substring(DamageInsight.History.RecordPages.RunLink.Length)));
            OnNavigated();
            return;
        }
        var entry = CodexPages.Entry(id);
        if (entry == null)
            return;
        if (CodexCatalog.TwinOf.TryGetValue(entry.Id, out var shared) && CodexPages.Entry(shared) is { } page)
            entry = page;
        if (_query.Length > 0 && _search != null)
            _search.text = ""; // the list should show the target's tab, not search results
        _nav.Open(entry.Category, entry.Id);
        _scrollToSelected = true;
        OnNavigated();
    }

    /// <summary>Makes the names inside a text clickable (TMP &lt;link&gt; tags carry the page id).</summary>
    private void AddLinkClicks(TextMeshProUGUI text)
    {
        text.raycastTarget = true;
        var clicks = text.gameObject.AddComponent<CodexLinkClicks>();
        clicks.Text = text;
        clicks.Open = id => DamageInsight.Patches.Guard.Run("Codex link", () => FollowLink(id));
    }

    /// <summary>Scrolls the entry list so the row at <paramref name="index"/> is in view (after a link or Back).</summary>
    private void ScrollListTo(int index)
    {
        _scrollToSelected = false;
        if (index < 0 || _listScroll == null)
            return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);
        float total = _listContent.rect.height, view = _listScroll.viewport.rect.height;
        if (total <= view)
            return;
        float y = index * 36f - view / 2f + 18f;
        _listScroll.verticalNormalizedPosition = 1f - Mathf.Clamp(y, 0f, total - view) / (total - view);
    }

    /// <summary>The right page: a big picture with films (creatures), or a small picture and a long text.</summary>
    private void ApplyLayout(PageLayout layout)
    {
        if (_layout == layout || _portraitFrame == null)
            return;
        _layout = layout;
        float width = PageWidth - 64;
        bool creature = layout == PageLayout.Creature;
        float frameHeight = creature ? 330 : 150;
        _portraitFrame.Place(32, 102, width, frameHeight);
        float next = creature ? 476 : 262;
        _nextText.rectTransform.Place(32, next, width, 26);
        _bar.Place(32, next + 26, width, 10);
        _bar.gameObject.SetActive(layout is PageLayout.Creature or PageLayout.Gear);
        _nextText.gameObject.SetActive(layout != PageLayout.Run);
        float textTop = creature ? 524 : layout == PageLayout.Gear ? 306 : layout == PageLayout.Run ? 300 : 264;
        _textFrame.Place(32, textTop, width, BookHeight - 24 - textTop - 24);
        _portraitMark.fontSize = creature ? 150 : 90;
    }

    /// <summary>During a run the book opens on the You page (setting Codex/OpenOnYou); Back returns to the page before.</summary>
    private void OnOpened()
    {
        if (Plugin.CodexOpenOnYou?.Value == false || !InRun())
            return;
        _nav.SwitchSection(CodexSection.You);
        OnNavigated();
    }

    private static bool InRun()
    {
        try
        {
            var levelManager = Singleton<Service>.Instance?.levelManager;
            var chapter = levelManager?.currentChapter;
            return levelManager?.player != null && chapter != null &&
                   chapter.type is not (Level.Chapter.Type.Castle or Level.Chapter.Type.HardmodeCastle or Level.Chapter.Type.Test);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Opens the page behind a clicked &lt;link&gt; in a text.</summary>
public sealed class CodexLinkClicks : MonoBehaviour, IPointerClickHandler
{
    public TextMeshProUGUI Text;
    public Action<string> Open;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Text == null || eventData.button != PointerEventData.InputButton.Left)
            return;
        int index = TMP_TextUtilities.FindIntersectingLink(Text, eventData.position, null);
        if (index >= 0 && index < Text.textInfo.linkCount)
            Open?.Invoke(Text.textInfo.linkInfo[index].GetLinkID());
    }
}
