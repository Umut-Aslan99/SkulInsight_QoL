using System;
using System.IO;
using System.Linq;
using DamageInsight.Achievements;
using DamageInsight.Lang;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The slot's achievements (0.14): the table against the game's list, the per-slot file, the page texts.</summary>
public class AchievementTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"achievements-{Guid.NewGuid():N}");
    private readonly string _previousFolder = AchievementStore.Folder;

    public AchievementTests()
    {
        AchievementStore.Folder = _folder;
        AchievementStore.ForgetSlot();
    }

    public void Dispose()
    {
        AchievementStore.Folder = _previousFolder;
        AchievementStore.ForgetSlot();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Catalog_HasEveryAchievementOfTheGame_InItsOrder()
    {
        var game = Enum.GetValues(typeof(Platforms.Achievement.Type)).Cast<Platforms.Achievement.Type>().Select(t => t.ToString()).ToList();
        var ours = AchievementCatalog.All.Select(a => a.Id).ToList();
        Assert.Equal(game, ours);
        Assert.Equal(103, ours.Count);
        Assert.Equal(101, AchievementCatalog.SteamCount);
        Assert.Equal(new[] { "TruePeace_2", "TrueSalvation_2" }, AchievementCatalog.All.Where(a => !a.OnSteam).Select(a => a.Id));
    }

    [Fact]
    public void Catalog_EverySteamAchievement_HasItsTextInAllElevenLanguages()
    {
        foreach (var info in AchievementCatalog.All.Where(a => a.OnSteam))
            foreach (var (code, _) in Loc.Languages)
            {
                Assert.True(info.Names.TryGetValue(code, out var name) && name.Length > 0, $"{info.Id}: no {code} name");
                Assert.True(info.Descriptions.TryGetValue(code, out var text) && text.Length > 0, $"{info.Id}: no {code} description");
            }
    }

    [Fact]
    public void Catalog_Kinds_TipsAndVeils()
    {
        Assert.Equal(44, AchievementCatalog.All.Count(a => a.Kind == AchievementKind.Skull));
        Assert.Equal(8, AchievementCatalog.All.Count(a => a.Kind == AchievementKind.NoHit));
        Assert.All(AchievementCatalog.All.Where(a => a.Kind == AchievementKind.NoHit), a => Assert.NotEqual("", a.Tip));
        Assert.Equal(10, AchievementCatalog.Get("ImpossibleIsNothing").DarkMirrorLevel);
        Assert.Equal(100, AchievementCatalog.Get("HeroSlayer").Target);
        Assert.True(AchievementCatalog.Get("TrueSalvation").Secret);
        Assert.Equal("enemy:DarkSkul1", AchievementCatalog.Get("TrueSalvation").LiftedBy);
        Assert.False(AchievementCatalog.Get("SavedElderEnt").Secret);
        Assert.Equal("enemy:Pope", AchievementCatalog.Get("LeoniasFall_2").Boss);
        Assert.Equal(AchievementKind.Other, AchievementCatalog.Get("HallofGods").Kind);
    }

    [Theory]
    [InlineData("Skeleton_Sword_4", "Skeleton_Sword")]
    [InlineData("Warrior_3", "Warrior")]
    [InlineData("Yaksha_Polymorph", "Yaksha")]
    [InlineData("Mummy_3", "Mummy")]
    [InlineData("GuanYu_3", "GuanYu")]
    [InlineData("Dominator", "Dominator")]
    [InlineData("Balrog_Head", null)]
    public void Catalog_ALegendarySkull_GivesWhatTheGameGives(string skull, string achievement)
    {
        Assert.Equal(achievement, AchievementCatalog.ForSkull(skull)?.Id);
    }

    [Fact]
    public void Extras_AreNamedAfterTheirSteamAchievement()
    {
        var extra = AchievementCatalog.Get("TruePeace_2");
        var steam = AchievementCatalog.Get("TruePeace");
        Assert.Equal($"{steam.Name} (no hit)", extra.Name);
        Assert.Equal($"{steam.Description} without getting hit", extra.Description);
        Assert.Equal("TruePeace", extra.BaseId);
    }

    [Fact]
    public void Texts_FollowTheGamesLanguage()
    {
        Loc.With("de", () =>
        {
            Assert.Equal("Die Legende beginnt", AchievementCatalog.Get("TheLegendBegins").Name);
            Assert.EndsWith("(ohne Treffer)", AchievementCatalog.Get("TruePeace_2").Name);
        });
    }

    [Fact]
    public void Store_KeepsTheFirstEarn_PerSlotFile()
    {
        var at = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(AchievementStore.Earn("SavedElderEnt", at, EarnedHow.Run, "2026-10-25_13-00-00"));
        Assert.False(AchievementStore.Earn("SavedElderEnt", at.AddHours(1), EarnedHow.Run)); // the game calls it again and again
        Assert.True(AchievementStore.Earn("GoHome", at, EarnedHow.Save));
        AchievementStore.Reveal("TrueSalvation");
        AchievementStore.MarkProofsChecked();

        AchievementStore.ForgetSlot(); // read back from the file
        var earned = AchievementStore.Get("SavedElderEnt");
        Assert.Equal(at, earned.At);
        Assert.Equal(EarnedHow.Run, earned.How);
        Assert.Equal("2026-10-25_13-00-00", earned.Run);
        Assert.Equal(EarnedHow.Save, AchievementStore.Get("GoHome").How);
        Assert.True(AchievementStore.IsRevealed("TrueSalvation"));
        Assert.True(AchievementStore.ProofsChecked);
        Assert.Null(AchievementStore.SteamTaken);
        Assert.Equal(2, AchievementStore.Count());
        Assert.Single(AchievementStore.OfRun("2026-10-25_13-00-00"));
    }

    [Fact]
    public void Store_AnUnreadableFile_IsKeptAsBak_AndTheSlotStartsAgain()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "achievements.json"), "{ not json");
        AchievementStore.ForgetSlot();
        Assert.Equal(0, AchievementStore.Count());
        Assert.True(File.Exists(Path.Combine(_folder, "achievements.json.bak")));
    }

    [Fact]
    public void Page_ShowsTheSlot_Steam_TheTipAndTheProgress()
    {
        var info = AchievementCatalog.Get("FirstHeroLastBattle_2");
        var view = new AchievementView
        {
            Info = info, OnSteamAccount = true, SteamAt = new DateTime(2021, 3, 12, 10, 0, 0, DateTimeKind.Utc), Percent = 7.6,
            BossName = "First Hero",
        };
        string text = DamageInsight.Codex.PageBlocks.Flatten(AchievementPages.Text(view));
        Assert.Contains(info.Description, text);
        Assert.Contains("A hit counts when it takes at least 1 health", text);
        Assert.Contains("Not earned in this slot yet", text);
        Assert.Contains("Your Steam account has it (since 2021-03-12", text);
        Assert.Contains("7.6 % of all players", text);
        Assert.Contains("First Hero", text);
        Assert.Equal("Not earned in this slot yet", AchievementPages.Subtitle(view));

        var counted = new AchievementView { Info = AchievementCatalog.Get("HeroSlayer"), Progress = (37, 100), OnSteamAccount = false };
        Assert.Contains("37 / 100", DamageInsight.Codex.PageBlocks.Flatten(AchievementPages.Text(counted)));

        var extra = new AchievementView { Info = AchievementCatalog.Get("TrueSalvation_2") };
        Assert.Contains("Not a Steam achievement", AchievementPages.Text(extra));
    }

    [Fact]
    public void Page_AVeiledOne_TellsHowToRevealIt_AndNothingElse()
    {
        var info = AchievementCatalog.Get("TrueSalvation");
        var view = new AchievementView { Info = info, Unveiled = false };
        Assert.Equal("Hidden achievement", AchievementPages.Title(view));
        string text = AchievementPages.Text(view);
        Assert.Contains("4 seconds", text);
        Assert.DoesNotContain(info.Description, text);
        Assert.DoesNotContain(info.Name, text);
    }

    [Fact]
    public void Page_TheRunAnAchievementCameIn_IsALink()
    {
        var view = new AchievementView
        {
            Info = AchievementCatalog.Get("Mummy"),
            Earned = new EarnedAchievement { Id = "Mummy", At = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), Run = "2026-10-25_13-00-00" },
            RunDate = "2026-10-25 13:00",
        };
        Assert.Contains("<link=\"run:2026-10-25_13-00-00\">", AchievementPages.Text(view));
        Assert.StartsWith("Earned in this slot on 2026-10-25", AchievementPages.Subtitle(view));
    }

    [Fact]
    public void Daily_LikesAndTheBestStreak_EarnTheModsOwnAchievements_Once()
    {
        Assert.Equal(9, AchievementCatalog.Daily.Count);
        Assert.Equal("Receive 25 thumbs up on your daily runs", AchievementCatalog.Get("DailyLikes25").Description);
        Assert.Equal("Play the daily run 7 days in a row", AchievementCatalog.Get("DailyStreak7").Description);

        var state = new DamageInsight.Daily.DailyState();
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { "DailyLikes5", "DailyLikes10", "DailyStreak3" }, state.UpdateCounters(12, 2, 4, 9, now));
        Assert.Empty(state.UpdateCounters(12, 0, 0, 9, now.AddDays(1))); // nothing twice; a broken streak keeps the best
        Assert.Equal(4, state.BestStreak);

        var back = DamageInsight.Daily.DailyState.FromJson(state.ToJson());
        Assert.Equal(12, back.LikesReceived);
        Assert.Equal(4, back.BestStreak);
        Assert.Equal(now, back.Achievements["DailyLikes10"]);
        Assert.Equal(3, back.Achievements.Count);
    }

    [Fact]
    public void Page_ADailyOne_SaysItIsTheDailyPlayers_AndNeedsDailyRuns()
    {
        var view = new AchievementView { Info = AchievementCatalog.Get("DailyStreak14"), Progress = (4, 14), DailyEnabled = false };
        string text = DamageInsight.Codex.PageBlocks.Flatten(AchievementPages.Text(view));
        Assert.Contains("4 / 14", text);
        Assert.Contains("Turn on Daily runs (online)", text);
        Assert.Contains("the same in every save slot", text);
        Assert.DoesNotContain("Steam account", text);
        Assert.Equal("Not earned yet", AchievementPages.Subtitle(view));
    }
}
