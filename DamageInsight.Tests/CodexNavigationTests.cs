using DamageInsight.Codex;
using Xunit;

namespace DamageInsight.Tests;

public class CodexNavigationTests
{
    private static CodexNavigation.Place Arsenal(CodexCategory category, string id) => new(CodexSection.Arsenal, category, id);

    [Fact]
    public void A_fresh_list_picks_its_first_entry_without_a_history_step()
    {
        var nav = new CodexNavigation();
        nav.SelectQuietly("enemy:CarleonRecruit");
        Assert.Equal("enemy:CarleonRecruit", nav.SelectedId);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void Links_open_the_target_in_its_own_section_and_Back_returns()
    {
        var nav = new CodexNavigation();
        nav.SwitchSection(CodexSection.Arsenal);
        nav.SwitchCategory(CodexCategory.Items);
        nav.SelectQuietly("item:HopeCutter");

        nav.Open(CodexCategory.Inscriptions, "inscription:Arms");
        Assert.Equal(Arsenal(CodexCategory.Inscriptions, "inscription:Arms"), nav.Current);

        nav.Open(CodexCategory.Items, "item:BoneOfCourage");
        Assert.True(nav.Back());
        Assert.Equal(Arsenal(CodexCategory.Inscriptions, "inscription:Arms"), nav.Current);
        Assert.True(nav.Back());
        Assert.Equal(Arsenal(CodexCategory.Items, "item:HopeCutter"), nav.Current);
    }

    [Fact]
    public void Links_to_enemies_switch_to_the_bestiary()
    {
        var nav = new CodexNavigation();
        nav.SwitchSection(CodexSection.Arsenal);
        nav.SelectQuietly("skull:Skul");
        nav.Open(CodexCategory.Elites, "elite:Legion");
        Assert.Equal(CodexSection.Bestiary, nav.Section);
        Assert.Equal(CodexCategory.Elites, nav.Category);
    }

    [Fact]
    public void Each_section_remembers_its_last_page()
    {
        var nav = new CodexNavigation();
        nav.SelectQuietly("enemy:CarleonRecruit");
        nav.Select("enemy:CarleonArcher");
        nav.SwitchSection(CodexSection.You);
        Assert.Equal(CodexNavigation.YouDefault, nav.SelectedId);
        nav.Select("you:item3");
        nav.SwitchSection(CodexSection.Bestiary);
        Assert.Equal("enemy:CarleonArcher", nav.SelectedId);
        nav.SwitchSection(CodexSection.You);
        Assert.Equal("you:item3", nav.SelectedId);
    }

    [Fact]
    public void History_keeps_the_last_30_pages()
    {
        var nav = new CodexNavigation();
        nav.SelectQuietly("item:0");
        for (int i = 1; i <= 40; i++)
            nav.Select("item:" + i);
        Assert.Equal(CodexNavigation.MaxHistory, nav.HistoryCount);
        int back = 0;
        while (nav.Back())
            back++;
        Assert.Equal(30, back);
        Assert.Equal("item:10", nav.SelectedId);
    }

    [Fact]
    public void Selecting_the_same_page_again_adds_no_step()
    {
        var nav = new CodexNavigation();
        nav.SelectQuietly("item:a");
        nav.Select("item:a");
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void Sections_have_their_tabs()
    {
        Assert.Equal(new[] { CodexCategory.YouBuild, CodexCategory.Records, CodexCategory.Runs, CodexCategory.Achievements }, CodexNavigation.TabsOf(CodexSection.You));
        foreach (var category in CodexNavigation.TabsOf(CodexSection.You))
            Assert.Equal(CodexSection.You, CodexNavigation.SectionOf(category));
        Assert.Contains(CodexCategory.Elites, CodexNavigation.TabsOf(CodexSection.Bestiary));
        Assert.Contains(CodexCategory.DarkAbilities, CodexNavigation.TabsOf(CodexSection.Arsenal));
        foreach (var category in CodexNavigation.TabsOf(CodexSection.Arsenal))
            Assert.Equal(CodexSection.Arsenal, CodexNavigation.SectionOf(category));
    }

    [Fact]
    public void Skills_used_and_highest_step_survive_save_and_load()
    {
        var progress = new CodexProgress();
        var skull = progress.Get("skull:GraveDigger");
        Assert.True(skull.AddSkill("CorpseExplosion"));
        Assert.False(skull.AddSkill("CorpseExplosion"));
        Assert.True(skull.AddSkill("SummonGhoul"));
        progress.Get("inscription:Arms").MaxStep = 2;

        var loaded = CodexProgress.FromJson(progress.ToJson());
        Assert.True(loaded.Peek("skull:GraveDigger").UsedSkill("SummonGhoul"));
        Assert.True(loaded.Peek("skull:GraveDigger").UsedSkill("CorpseExplosion"));
        Assert.False(loaded.Peek("skull:GraveDigger").UsedSkill("Corpse"));
        Assert.Equal(2, loaded.Peek("inscription:Arms").MaxStep);
    }

    [Fact]
    public void Elite_tiers_follow_dark_enemy_kills()
    {
        Assert.Equal(0, CodexTiers.Tier(CodexCategory.Elites, new EntryProgress()));
        Assert.Equal(1, CodexTiers.Tier(CodexCategory.Elites, new EntryProgress { Seen = 1 }));
        Assert.Equal(2, CodexTiers.Tier(CodexCategory.Elites, new EntryProgress { Seen = 1, Kills = 1 }));
        Assert.Equal(3, CodexTiers.Tier(CodexCategory.Elites, new EntryProgress { Seen = 6, Kills = 5 }));
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2, 3, 7, 8, 9, 10 }, "0-3, 7-10")]
    [InlineData(new[] { 4 }, "4")]
    [InlineData(new[] { 0, 2, 3 }, "0, 2-3")]
    [InlineData(new int[0], "")]
    public void Dark_mirror_levels_are_shown_as_ranges(int[] levels, string expected) =>
        Assert.Equal(expected, CodexElites.Ranges(levels));
}
