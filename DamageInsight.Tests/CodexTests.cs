using System.Linq;
using DamageInsight.Codex;
using Xunit;

namespace DamageInsight.Tests;

public class CodexTests
{
    [Theory]
    [InlineData(10001, CodexCategory.Enemies, "Chapter 1")]          // Carleon Recruit
    [InlineData(15002, CodexCategory.Enemies, "Chapter 1 · more")]   // Cannon Specialist
    [InlineData(45308, CodexCategory.Enemies, "Chapter 4 · more")]   // Awakened
    [InlineData(2005, CodexCategory.Bosses, "Boss")]                 // Chimera
    [InlineData(1003, CodexCategory.Enemies, "Adventurer mages")]    // Silentist
    [InlineData(50001, CodexCategory.Enemies, "Special")]            // Sentinel
    public void Groups_FollowTheKeyNumbers(int key, CodexCategory category, string group)
    {
        var result = CodexGroups.ForEnemy(key);
        Assert.NotNull(result);
        Assert.Equal(category, result.Value.category);
        Assert.Equal(group, result.Value.group);
    }

    [Theory]
    [InlineData(1)]      // Player1
    [InlineData(101)]    // SmallProp
    [InlineData(201)]    // BrutalityAltar
    [InlineData(501)]    // MinionCarleonRecruit
    public void Groups_SkipNonEnemies(int key) => Assert.Null(CodexGroups.ForEnemy(key));

    [Fact]
    public void Tiers_EnemiesUnlockBySeeingAndKilling()
    {
        Assert.Equal(0, CodexTiers.Tier(CodexCategory.Enemies, null));
        Assert.Equal(1, CodexTiers.Tier(CodexCategory.Enemies, new EntryProgress { Seen = 1 }));
        Assert.Equal(1, CodexTiers.Tier(CodexCategory.Enemies, new EntryProgress { Seen = 3, Kills = 4 }));
        Assert.Equal(2, CodexTiers.Tier(CodexCategory.Enemies, new EntryProgress { Seen = 3, Kills = 5 }));
        Assert.Equal(3, CodexTiers.Tier(CodexCategory.Enemies, new EntryProgress { Kills = 25 }));
        Assert.Equal(("Kill 5 to learn more", 4, 5), CodexTiers.Next(CodexCategory.Enemies, new EntryProgress { Seen = 1, Kills = 4 }));
        Assert.Null(CodexTiers.Next(CodexCategory.Enemies, new EntryProgress { Kills = 30 }));
    }

    [Fact]
    public void Tiers_BossesNeedFewerKills_GearNeedsPickups()
    {
        Assert.Equal(2, CodexTiers.Tier(CodexCategory.Bosses, new EntryProgress { Seen = 1, Kills = 1 }));
        Assert.Equal(3, CodexTiers.Tier(CodexCategory.Bosses, new EntryProgress { Kills = 3 }));
        Assert.Equal(1, CodexTiers.Tier(CodexCategory.Items, new EntryProgress { Seen = 2 }));
        Assert.Equal(2, CodexTiers.Tier(CodexCategory.Items, new EntryProgress { Seen = 2, PickedUp = 1 }));
        Assert.Equal(("Pick it up 5 times to master it", 2, 5), CodexTiers.Next(CodexCategory.Skulls, new EntryProgress { PickedUp = 2 }));
    }

    [Fact]
    public void Progress_SurvivesSaveAndLoad()
    {
        var progress = new CodexProgress();
        var recruit = progress.Get("enemy:CarleonRecruit");
        recruit.Seen = 12;
        recruit.Kills = 9;
        recruit.DamageDealt = 1234.5;
        recruit.WorstHit = 17;
        progress.Get("item:HopeCutter").PickedUp = 2;

        var loaded = CodexProgress.FromJson(progress.ToJson());
        Assert.Equal(9, loaded.Peek("enemy:CarleonRecruit").Kills);
        Assert.Equal(1234.5, loaded.Peek("enemy:CarleonRecruit").DamageDealt);
        Assert.Equal(17, loaded.Peek("enemy:CarleonRecruit").WorstHit);
        Assert.Equal(2, loaded.Peek("item:HopeCutter").PickedUp);
        Assert.Null(loaded.Peek("enemy:Chimera"));
        Assert.Empty(CodexProgress.FromJson("").Entries);
    }

    [Fact]
    public void FuzzySearch_FindsTyposAndAbbreviations_BestFirst()
    {
        var names = new[] { "Carleon Recruit", "Carleon Archer", "Chimera", "Recruit in Cannon", "Hope Slasher" };
        Assert.Equal("Carleon Recruit", FuzzySearch.Filter(names, "crlrc", n => n).First());
        Assert.Equal(new[] { "Carleon Recruit", "Recruit in Cannon" }, FuzzySearch.Filter(names, "recruit", n => n).OrderBy(n => n));
        Assert.Equal("Chimera", FuzzySearch.Filter(names, "chim", n => n).First());
        Assert.Equal("Hope Slasher", FuzzySearch.Filter(names, "hope sl", n => n).Single());
        Assert.Empty(FuzzySearch.Filter(names, "xyz", n => n));
        Assert.Equal(names.Length, FuzzySearch.Filter(names, "", n => n).Count);
    }
}
