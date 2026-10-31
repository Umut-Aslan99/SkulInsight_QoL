using System;
using System.IO;
using System.Linq;
using DamageInsight.Achievements;
using DamageInsight.Codex;
using DamageInsight.Rewards;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The rewards (0.15): a reward for every achievement, what they add up to, the slot's coins and quests.</summary>
public class RewardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rewards-{Guid.NewGuid():N}");
    private readonly string _previousFolder = RewardStore.Folder;

    public RewardTests()
    {
        RewardStore.Folder = _folder;
        RewardStore.ForgetSlot();
    }

    public void Dispose()
    {
        RewardStore.Folder = _previousFolder;
        RewardStore.ForgetSlot();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static string Name(string page) => page.Replace("enemy:", "");

    [Fact]
    public void Catalog_EveryAchievementOfTheGame_HasAReward_TheDailyOnesNone()
    {
        Assert.Equal(103, AchievementCatalog.All.Count);
        Assert.All(AchievementCatalog.All, a => Assert.NotNull(RewardCatalog.For(a.Id)));
        Assert.All(AchievementCatalog.Daily, a => Assert.Null(RewardCatalog.For(a.Id)));
        Assert.All(RewardCatalog.All, r => Assert.Contains(r.Group, RewardCatalog.Groups));
        Assert.All(RewardCatalog.All, r => Assert.True(r.Stats.Count > 0 || r.Target.Length > 0, r.AchievementId));
        Assert.Equal(44, RewardCatalog.All.Count(r => r.Group == "skull"));
        Assert.Equal(AchievementCatalog.All.Where(a => a.Kind == AchievementKind.Skull).Select(a => a.Id).OrderBy(i => i),
            RewardCatalog.All.Where(r => r.Group == "skull").Select(r => r.AchievementId).OrderBy(i => i));
    }

    [Fact]
    public void Catalog_Bosses_MoreDamageToThatBoss()
    {
        var ent = RewardCatalog.For("SavedElderEnt");
        Assert.Equal(("boss", "enemy:Yggdrasil", 1.03), (ent.Group, ent.Target, ent.Multiplier));
        var noHit = RewardCatalog.For("SavedElderEnt_2");
        Assert.Equal(("nohit", "enemy:Yggdrasil", 1.10), (noHit.Group, noHit.Target, noHit.Multiplier));
        Assert.Equal(RewardCatalog.DarkMirrorPrefix + "enemy:FirstHero1", RewardCatalog.For("WorldsBest").Target);
        Assert.Equal("enemy:Alexander1", RewardCatalog.For("TruePeace_2").Target);
        Assert.Equal(7, RewardCatalog.All.Count(r => r.Group == "boss"));
        Assert.Equal(8, RewardCatalog.All.Count(r => r.Group == "nohit"));
    }

    [Fact]
    public void Sum_StatsAddUp_TargetsMultiply()
    {
        var totals = RewardCatalog.Sum(new[] { "SavedElderEnt", "SavedElderEnt_2", "TheLegendBegins", "Concentration", "BrokenWorld", "Samurai", "Samurai", "nonsense" });
        Assert.Equal(6, totals.Count); // the repeated one and the unknown one don't count
        Assert.Equal(1.03 * 1.10, totals.Target("enemy:Yggdrasil"), 6);
        Assert.Equal(1.0, totals.Target("enemy:Chimera"));
        Assert.Equal(-2, totals.Stat(RewardStat.DamageTaken));
        Assert.Equal(1, totals.Stat(RewardStat.PhysicalAttack));
        Assert.Equal(1, totals.Stat(RewardStat.MagicAttack));
        Assert.Equal(0.5, totals.Stat(RewardStat.AttackSpeed)); // Samurai is a Speed skull
    }

    [Fact]
    public void Sum_EveryAchievement_StaysSmall()
    {
        var all = RewardCatalog.Sum(AchievementCatalog.All.Select(a => a.Id));
        Assert.Equal(103, all.Count);
        Assert.Equal(18.5, all.Stat(RewardStat.PhysicalAttack), 6);
        Assert.Equal(18.5, all.Stat(RewardStat.MagicAttack), 6);
        Assert.Equal(7, all.Stat(RewardStat.AttackSpeed), 6);
        Assert.Equal(-3, all.Stat(RewardStat.DamageTaken), 6);
        Assert.Equal(5, all.Stat(RewardStat.Gold), 6);
        Assert.Equal(8, all.Stat(RewardStat.MovementSpeed), 6);
        Assert.Equal(9, all.Stat(RewardStat.CooldownSpeed), 6);
        Assert.Equal(4, all.Stat(RewardStat.CriticalChance), 6);
        Assert.Equal(3, all.Stat(RewardStat.CriticalDamage), 6);
        Assert.Equal(5, all.Stat(RewardStat.Healing), 6);
        Assert.Equal(1.05, all.Target(RewardCatalog.Adventurers), 6);
        Assert.Equal(1.10, all.Target(RewardCatalog.LittleBone), 6);
        Assert.Equal(1.03 * 1.10, all.Target("enemy:FirstHero1"), 6);
        Assert.Equal(1.10, all.Target(RewardCatalog.DarkMirrorPrefix + "enemy:FirstHero1"), 6);
    }

    [Fact]
    public void Texts_OneLinePerReward()
    {
        Assert.Equal("Damage to Yggdrasil +3 %", RewardCatalog.Text(RewardCatalog.For("SavedElderEnt"), Name));
        Assert.Equal("Damage to Yggdrasil +10 %", RewardCatalog.Text(RewardCatalog.For("SavedElderEnt_2"), Name));
        Assert.Equal("Damage to FirstHero1 in the Dark Mirror +10 %", RewardCatalog.Text(RewardCatalog.For("WorldsBest"), Name));
        Assert.Equal("Physical and magic attack +1 %", RewardCatalog.Text(RewardCatalog.For("BrokenWorld"), Name));
        Assert.Equal("Damage taken -1 %", RewardCatalog.Text(RewardCatalog.For("Concentration"), Name));
        Assert.Equal("Attack speed +0.5 %", RewardCatalog.Text(RewardCatalog.For("Samurai"), Name));
        Assert.Equal("Damage while holding Little Bone +10 %", RewardCatalog.Text(RewardCatalog.For("SkeletonKing"), Name));
        Assert.Equal("13.3", RewardCatalog.Number((1.03 * 1.10 - 1) * 100));
    }

    [Fact]
    public void Store_Coins_SummonsAndTheRun_SurviveTheFile()
    {
        RewardStore.BeginRun("run-1");
        RewardStore.AddCoin("skull:Samurai");
        RewardStore.AddCoin("skull:Samurai");
        RewardStore.AddCoin("item:TheChosenWarriorsArmor");
        RewardStore.AddOmenCoins(2);
        Assert.True(RewardStore.UseOmenCoin("item:Deicide"));
        Assert.True(RewardStore.UseCoin("skull:Samurai"));
        Assert.False(RewardStore.UseCoin("skull:Nobody"));
        Assert.True(RewardStore.NoteMaxed("Arms"));
        Assert.False(RewardStore.NoteMaxed("Arms")); // once per run
        Assert.Equal(0, RewardStore.Activations);     // paid when the run ends, not at once
        Assert.Equal(1, RewardStore.PayMaxed());
        Assert.Equal(0, RewardStore.PayMaxed());      // once
        Assert.False(RewardStore.NoteMaxed("Brave")); // after the end screen nothing more counts
        Assert.True(RewardStore.UseActivation("Heritage"));

        RewardStore.ForgetSlot(); // read back from the file
        Assert.Equal(1, RewardStore.Coins("skull:Samurai"));
        Assert.Equal(1, RewardStore.Coins("item:TheChosenWarriorsArmor"));
        Assert.Equal(1, RewardStore.OmenCoins);
        Assert.Equal(0, RewardStore.Activations);
        Assert.Equal(new[] { "Heritage" }, RewardStore.ActivatedThisRun);
        Assert.Equal(2, RewardStore.SummonsThisRunTotal);
        Assert.Equal(1, RewardStore.SummonsThisRun(CoinKind.Omen));  // each kind counts on its own
        Assert.Equal(1, RewardStore.SummonsThisRun(CoinKind.Skull));
        Assert.Equal(0, RewardStore.SummonsThisRun(CoinKind.Item));
        Assert.Equal((5, 2), RewardStore.CoinTotals);
        // Summoned gear earns no coin back in its run (no free Omen item every run).
        Assert.True(RewardStore.SummonedThisRun("item:Deicide"));
        Assert.True(RewardStore.SummonedThisRun("skull:Samurai"));

        RewardStore.BeginRun("run-1"); // a continued run keeps its summons and activations
        Assert.Equal(2, RewardStore.SummonsThisRunTotal);
        Assert.False(RewardStore.NoteMaxed("Arms"));
        RewardStore.BeginRun("run-2");
        Assert.Equal(0, RewardStore.SummonsThisRunTotal);
        Assert.Empty(RewardStore.ActivatedThisRun);
        Assert.False(RewardStore.SummonedThisRun("item:Deicide"));
        Assert.True(RewardStore.NoteMaxed("Arms"));
    }

    [Fact]
    public void Store_Quests_PayEveryStep_AndKeepTheRest()
    {
        Assert.Equal(0, RewardStore.CountKill("enemy:CarleonRecruit", 10, 9));
        Assert.Equal(1, RewardStore.CountKill("enemy:CarleonRecruit", 10, 3));
        Assert.Equal(2, RewardStore.CountKill("enemy:CarleonRecruit", 10, 20));
        var q = RewardStore.Quest("enemy:CarleonRecruit");
        Assert.Equal((2, 3), (q.Kills, q.Done));
        RewardStore.AddPaid(30, 90);
        RewardStore.ForgetSlot();
        Assert.Equal((3, 30L, 90L), RewardStore.QuestTotals);
        Assert.Equal(2, RewardStore.Quest("enemy:CarleonRecruit").Kills);
    }

    [Fact]
    public void Store_KillsAreSavedAFewSecondsLater_NotEachOne()
    {
        double now = 100;
        var clock = RewardStore.Clock;
        RewardStore.Clock = () => now;
        try
        {
            string file = Path.Combine(_folder, "rewards.json");
            RewardStore.CountKill("enemy:CarleonRecruit", 50, 1);
            Assert.False(File.Exists(file)); // not each kill (0.15.0 wrote the whole file every kill: a stutter)
            now += 1;
            RewardStore.Tick();
            Assert.False(File.Exists(file));
            now += 3;
            RewardStore.Tick();
            Assert.True(File.Exists(file));
            RewardStore.CountKill("enemy:CarleonRecruit", 50, 1);
            RewardStore.Flush(); // a slot switch or quitting writes it at once
            RewardStore.ForgetSlot();
            Assert.Equal(2, RewardStore.Quest("enemy:CarleonRecruit").Kills);
            RewardStore.AddOmenCoins(1); // a coin is saved at once
            Assert.Contains("\"omenCoins\": 1", File.ReadAllText(file));
        }
        finally
        {
            RewardStore.Clock = clock;
        }
    }

    [Fact]
    public void Store_AdventurerCoins_EveryFifthKill()
    {
        for (int i = 1; i <= 4; i++)
            Assert.False(RewardStore.CountAdventurerKill("Warrior", 5));
        Assert.True(RewardStore.CountAdventurerKill("Warrior", 5));
        Assert.Equal(0, RewardStore.AdventurerProgress("Warrior"));
        Assert.False(RewardStore.CountAdventurerKill("Warrior", 5));
        Assert.Equal(1, RewardStore.AdventurerProgress("Warrior"));
    }

    [Fact]
    public void Store_AnUnreadableFile_IsKeptAsBak()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "rewards.json"), "{ not json");
        RewardStore.ForgetSlot();
        Assert.Equal(0, RewardStore.OmenCoins);
        Assert.True(File.Exists(Path.Combine(_folder, "rewards.json.bak")));
    }

    [Fact]
    public void Pages_AchievementRewards_ShowTotals_EveryGroup_AndHideVeiledOnes()
    {
        var earned = new[] { "SavedElderEnt", "SavedElderEnt_2", "Concentration" };
        var c = new RewardContext
        {
            Earned = new System.Collections.Generic.HashSet<string>(earned),
            Totals = RewardCatalog.Sum(earned),
            Name = Name,
            Veiled = info => info.Secret,
        };
        string text = RewardPages.Text(RewardPages.Page.Achievements, c);
        Assert.Contains("Damage taken -1 %", text);
        Assert.Contains("Damage to <link=\"enemy:Yggdrasil\">", text); // the boss is a link
        Assert.Contains("+13.3 %", text);                                // beaten and no hit multiply
        Assert.Contains("Hidden achievement", text);                     // a veiled spoiler keeps its reward hidden
        Assert.DoesNotContain("Damage to DarkSkul1", text);
        foreach (string group in RewardCatalog.Groups)
            Assert.Contains(RewardCatalog.GroupName(group), text);
        var (_, headline, _) = RewardPages.Row(RewardPages.Page.Achievements, c);
        Assert.Equal("3 / 103", headline);
    }

    [Fact]
    public void Pages_Coins_SummonLinks_OnlyWhenTheyCanBeUsed()
    {
        for (int i = 0; i < 3; i++)
            RewardStore.AddCoin("skull:Samurai");   // a summon takes 3 (Coins per summon)
        RewardStore.AddOmenCoins(3);
        var c = new RewardContext { Name = Name, OmenChoices = { "item:Deicide" } };
        string text = RewardPages.Text(RewardPages.Page.Coins, c);
        Assert.Contains("3 / 3", text);
        Assert.Contains($"<link=\"{RewardPages.SummonLink}skull:Samurai\">", text);
        Assert.Contains($"<link=\"{RewardPages.OmenLink}item:Deicide\">", text);
        Assert.True(RewardPages.IsAction(RewardPages.SummonLink + "skull:Samurai"));
        Assert.False(RewardPages.IsAction("skull:Samurai"));

        c.WhyNoSummon = _ => "Coins work during a run.";
        text = RewardPages.Text(RewardPages.Page.Coins, c);
        Assert.DoesNotContain(RewardPages.SummonLink, text);
        Assert.Contains("Coins work during a run.", text);
        Assert.Contains("TheChosenWarriorsArmor", text); // the adventurers' coins and what they summon
    }

    [Fact]
    public void Pages_Quests_AndActivations()
    {
        RewardStore.CountKill("enemy:CarleonRecruit", 50, 37);
        var c = new RewardContext
        {
            Name = Name, QuestStep = _ => 50,
            Enemies = { ("enemy:CarleonRecruit", "Carleon Recruit"), ("enemy:Nobody", "Nobody") },
            ActivationChoices = { ("Arms", "Arms") },
        };
        string quests = RewardPages.Text(RewardPages.Page.Quests, c);
        Assert.Contains("Carleon Recruit", quests);
        Assert.Contains("37 / 50", quests);
        Assert.DoesNotContain("Nobody", quests); // no kills yet: not listed

        c.WhyNoActivation = "No activation left.";
        Assert.DoesNotContain(RewardPages.ActivateLink, RewardPages.Text(RewardPages.Page.Activations, c));
        c.WhyNoActivation = "";
        Assert.Contains(RewardPages.ActivateLink + "Arms", RewardPages.Text(RewardPages.Page.Activations, c));
    }

    [Fact]
    public void Coins_HaveKinds_EachWithItsOwnSection()
    {
        Assert.Equal(CoinKind.Skull, RewardRules.KindOf("skull:Samurai"));
        Assert.Equal(CoinKind.Adventurer, RewardRules.KindOf("item:TheChosenWarriorsArmor"));
        Assert.Equal(CoinKind.Item, RewardRules.KindOf("item:Deicide"));
        for (int i = 0; i < 3; i++)
        {
            RewardStore.AddCoin("skull:Samurai");
            RewardStore.AddCoin("item:HopeCutter");
        }
        RewardStore.AddCoin("item:TheChosenWarriorsArmor"); // one is a summon
        var c = new RewardContext { Name = Name, WhyNoSummon = k => k == CoinKind.Adventurer ? "Dark Mirror only" : "" };
        string text = RewardPages.Text(RewardPages.Page.Coins, c);
        Assert.Contains(RewardPages.KindName(CoinKind.Skull), text);
        Assert.Contains(RewardPages.KindName(CoinKind.Item), text);
        Assert.Contains($"<link=\"{RewardPages.SummonLink}skull:Samurai\">", text);
        Assert.Contains($"<link=\"{RewardPages.SummonLink}item:HopeCutter\">", text);
        Assert.DoesNotContain($"<link=\"{RewardPages.SummonLink}item:TheChosenWarriorsArmor\">", text); // not usable here
        Assert.Contains("Dark Mirror only", text);
    }

    [Fact]
    public void Coins_ASummonTakesSeveral_AnAdventurersOneIsEnough()
    {
        Assert.Equal(3, RewardRules.CoinCost(CoinKind.Skull, 3));
        Assert.Equal(3, RewardRules.CoinCost(CoinKind.Omen, 3));
        Assert.Equal(1, RewardRules.CoinCost(CoinKind.Adventurer, 3));   // already 5 wins each
        Assert.Equal(1, RewardRules.CoinCost(CoinKind.Item, 0));

        RewardStore.AddCoin("item:HopeCutter");
        RewardStore.AddCoin("item:HopeCutter");
        var c = new RewardContext { Name = Name };
        string text = RewardPages.Text(RewardPages.Page.Coins, c);
        Assert.Contains("2 / 3", text);
        Assert.DoesNotContain($"<link=\"{RewardPages.SummonLink}item:HopeCutter\">", text); // dim: one more needed
        Assert.Contains("A summon takes 3 coins", text);                                        // the page says what it takes
        Assert.False(RewardStore.UseCoin("item:HopeCutter", 3));
        RewardStore.AddCoin("item:HopeCutter");
        Assert.True(RewardStore.UseCoin("item:HopeCutter", 3));
        Assert.Equal(0, RewardStore.Coins("item:HopeCutter"));
    }

    [Fact]
    public void Pages_QuestMeter_IsASquareWithTheRewardInTheMiddle()
    {
        var blocks = PageBlocks.Parse("Killed 12\n" + PageBlocks.Meter(RewardPages.RewardCenter(10, 30, icons: true), "Kill quest  2 / 14", 2 / 14.0));
        var meter = blocks.SelectMany(b => b.Items).Single(i => i.Kind == PageBlocks.Kind.Meter);
        Assert.Contains("Others/DarkQuartz_Icon", meter.Value);
        Assert.Contains("+30", meter.Value);
        Assert.Equal("Kill quest  2 / 14", meter.Text);
        Assert.Equal(2 / 14.0, meter.Fill, 3);
        Assert.DoesNotContain("<sprite", RewardPages.RewardCenter(10, 30, icons: false));
    }

    [Theory]
    [InlineData("AdventurerWarrior", "Warrior")]
    [InlineData("VeteranHero", "Hero")]
    [InlineData("AdventurerHero_Conversation", "Hero")]
    [InlineData("VeteranArcher", "Archer")]
    [InlineData("CarleonRecruit", "")]
    [InlineData("Hero", "")]
    public void Rules_WhichAdventurer(string name, string adventurer)
    {
        Assert.Equal(adventurer, RewardRules.AdventurerOf(name));
        if (adventurer.Length > 0)
            Assert.StartsWith("TheChosen", RewardRules.ChosenItem(adventurer));
    }

    [Fact]
    public void Rules_BaseSkull_AndQuestStep()
    {
        Assert.Equal("Warrior", RewardRules.BaseSkull("Warrior_3"));
        Assert.Equal("Samurai", RewardRules.BaseSkull("Samurai"));
        Assert.Equal("Recruit", RewardRules.BaseSkull("Recruit_4", "Recruit"));
        Assert.Equal(10, RewardRules.QuestStep(2, 2));   // a rare enemy: at least 10
        Assert.Equal(60, RewardRules.QuestStep(30, 2));
        Assert.Equal(150, RewardRules.QuestStep(200, 2)); // at most 150
        Assert.Equal(30, RewardRules.QuestStep(200, 2, developer: true)); // developer builds: a fifth, the ratio kept
        // A full run: a common enemy finishes half a quest (two runs' worth), a rare one (2 a run, 10 kills) a fifth.
        Assert.Equal(0.5 + 0.2, RewardRules.QuestsPerRun(new[] { 22.0, 2.0, 0.0 }, 2), 3);
        Assert.Equal(9, RewardRules.QuestStep(21.9, 2, developer: true));  // Ent (44 kills)
        Assert.Equal(3, RewardRules.QuestStep(6.8, 2, developer: true));   // Root Ent (14 kills)
        Assert.Equal(2, RewardRules.QuestStep(3.3, 2, developer: true));   // Forest Keeper (10 kills)
    }
}
