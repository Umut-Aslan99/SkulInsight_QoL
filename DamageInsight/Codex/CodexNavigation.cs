using System;
using System.Collections.Generic;

namespace DamageInsight.Codex;

/// <summary>The book's sections (the top row of chips); each has its own sub-tabs.</summary>
public enum CodexSection
{
    You,
    Bestiary,
    Arsenal,
}

/// <summary>
/// Which page the book shows and the way back: a section, its sub-tab (category) and the selected page. Links and
/// list clicks go to a page and put the page before on the history; Back returns to it. Each section remembers its
/// last page, so switching sections returns to where you were. Plain logic, no Unity.
/// </summary>
public sealed class CodexNavigation
{
    public readonly struct Place : IEquatable<Place>
    {
        public readonly CodexSection Section;
        public readonly CodexCategory Category;   // the sub-tab (ignored in the You section)
        public readonly string Id;                // the selected page: an entry id, a You slot ("you:stats"), or null

        public Place(CodexSection section, CodexCategory category, string id)
        {
            Section = section;
            Category = category;
            Id = id;
        }

        public bool Equals(Place other) => Section == other.Section && Category == other.Category && Id == other.Id;
        public override bool Equals(object obj) => obj is Place other && Equals(other);
        public override int GetHashCode() => ((int)Section * 397) ^ ((int)Category * 31) ^ (Id?.GetHashCode() ?? 0);
        public override string ToString() => $"{Section}/{Category}/{Id ?? "-"}";
    }

    public const int MaxHistory = 30;

    /// <summary>The You page's first selection: the skull you hold.</summary>
    public const string YouDefault = "you:skull0";

    public static readonly CodexCategory[] BestiaryTabs = { CodexCategory.Enemies, CodexCategory.Bosses, CodexCategory.Elites };

    public static readonly CodexCategory[] ArsenalTabs =
        { CodexCategory.Skulls, CodexCategory.Items, CodexCategory.Essences, CodexCategory.Inscriptions, CodexCategory.DarkAbilities };

    private readonly List<Place> _history = new();
    private readonly Dictionary<CodexSection, Place> _lastIn = new();

    public Place Current { get; private set; } = new(CodexSection.Bestiary, CodexCategory.Enemies, null);

    public CodexSection Section => Current.Section;
    public CodexCategory Category => Current.Category;
    public string SelectedId => Current.Id;
    public bool CanGoBack => _history.Count > 0;
    public int HistoryCount => _history.Count;

    public static CodexSection SectionOf(CodexCategory category) =>
        category is CodexCategory.Enemies or CodexCategory.Bosses or CodexCategory.Elites ? CodexSection.Bestiary : CodexSection.Arsenal;

    /// <summary>The sub-tabs of a section (none for You).</summary>
    public static IReadOnlyList<CodexCategory> TabsOf(CodexSection section) => section switch
    {
        CodexSection.Bestiary => BestiaryTabs,
        CodexSection.Arsenal => ArsenalTabs,
        _ => Array.Empty<CodexCategory>(),
    };

    /// <summary>Where a section opens the first time: its first sub-tab (You: the skull you hold).</summary>
    public static Place DefaultOf(CodexSection section) => section == CodexSection.You
        ? new Place(CodexSection.You, CodexCategory.Skulls, YouDefault)
        : new Place(section, TabsOf(section)[0], null);

    /// <summary>
    /// Shows a page. The page before goes on the history, unless it had nothing selected yet (a list that just
    /// opened: its first entry is picked by <see cref="SelectQuietly"/>, which is not a page of its own).
    /// </summary>
    public void Go(Place place)
    {
        if (place.Equals(Current))
            return;
        if (Current.Id != null)
        {
            _history.Add(Current);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }
        SetCurrent(place);
    }

    /// <summary>Opens an entry's page in its own section and sub-tab (a link).</summary>
    public void Open(CodexCategory category, string id) => Go(new Place(SectionOf(category), category, id));

    /// <summary>Selects a page in the current list or on the You page.</summary>
    public void Select(string id) => Go(new Place(Current.Section, Current.Category, id));

    /// <summary>Switches to a section, on the page it showed last.</summary>
    public void SwitchSection(CodexSection section)
    {
        if (section == Current.Section)
            return;
        Go(_lastIn.TryGetValue(section, out var last) ? last : DefaultOf(section));
    }

    /// <summary>Switches the sub-tab; its list then picks its first entry quietly.</summary>
    public void SwitchCategory(CodexCategory category)
    {
        if (category == Current.Category && SectionOf(category) == Current.Section)
            return;
        Go(new Place(SectionOf(category), category, null));
    }

    /// <summary>Selects without making a history step (the first entry of a list that just opened).</summary>
    public void SelectQuietly(string id) => SetCurrent(new Place(Current.Section, Current.Category, id));

    /// <summary>Returns to the page before; false if there is none.</summary>
    public bool Back()
    {
        if (_history.Count == 0)
            return false;
        var place = _history[_history.Count - 1];
        _history.RemoveAt(_history.Count - 1);
        SetCurrent(place);
        return true;
    }

    private void SetCurrent(Place place)
    {
        Current = place;
        _lastIn[place.Section] = place;
    }
}
