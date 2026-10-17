using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Codex;
using DamageInsight.History;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The Records page (0.13 batch 3): the run history added up, the game's own records, the page texts.</summary>
public class RecordsTests
{
    private static RunRecord Run(string id, int day, RunResult result, int seconds, bool dark = false, int level = 0)
    {
        var r = new RunRecord
        {
            Id = id,
            Started = new DateTime(2026, 10, day, 12, 0, 0),
            Result = result,
            DarkMirror = dark,
            Level = level,
            StartPlay = 100,
            EndPlay = 100 + seconds,
        };
        r.Build.Add(new RunGear { Kind = "skull", Id = "skull:Skul", Name = "Skul", Extra = "held|" });
        r.SkullSeconds["skull:Skul"] = seconds;
        r.SkullNames["skull:Skul"] = "Skul";
        return r;
    }

    private static void Numbers(RunRecord r, double kills, double damage, double best, double gold = 0)
    {
        r.Numbers[RunNumbers.Kills] = kills;
        r.Numbers[RunNumbers.TotalDamage] = damage;
        r.Numbers[RunNumbers.BestDamage] = best;
        r.Numbers[RunNumbers.Gold] = gold;
    }

    /// <summary>Umut's three runs of 2026-10-16 (index summaries: no timeline), plus a normal win, an abandoned run and one in progress.</summary>
    private static List<RunRecord> Runs()
    {
        var win = Run("2026-10-16_10-14-19", 16, RunResult.Ending, 1268, dark: true, level: 10);
        Numbers(win, 855, 6581805, 74567, 35909);
        win.EndBosses = "enemy:FirstHero1,enemy:DarkSkul1,enemy:Alexander1";
        win.LastBossId = "enemy:Alexander4";
        win.Build.Clear();
        win.Build.Add(new RunGear { Kind = "skull", Id = "skull:AquaSkull", Name = "Water Skull", Extra = "|Rising Tide" });
        win.Build.Add(new RunGear { Kind = "skull", Id = "skull:Skeleton_Spear_4", Name = "Pikeman Captain", Extra = "held|Forward" });
        win.SkullSeconds.Clear();
        win.SkullSeconds["skull:Skul"] = 21;
        win.SkullSeconds["skull:Skeleton_Spear_4"] = 1245;
        win.SkullSeconds["skull:AquaSkull"] = 1;
        win.SkullNames["skull:Skeleton_Spear_4"] = "Pikeman Captain";
        win.SkullNames["skull:AquaSkull"] = "Water Skull";

        var trap = Run("2026-10-16_10-45-55", 16, RunResult.Death, 52, dark: true, level: 10);
        trap.Started = trap.Started.AddHours(1);
        Numbers(trap, 32, 37526, 3200, 821);
        trap.Killer = "ThornTrap";
        trap.EndChapter = "HardmodeChapter1";
        trap.EndStageName = "The Deep Woods";

        var golem = Run("2026-10-16_19-05-05", 16, RunResult.Death, 1126, dark: true, level: 10);
        golem.Started = golem.Started.AddHours(7);
        Numbers(golem, 419, 217344, 1122, 13307);
        golem.Killer = "Armored Golem";
        golem.KillerId = "enemy:ArmoredGolem";
        golem.EndChapter = "HardmodeChapter3";
        golem.EndStageName = "Bio Alchemy Lab";

        // A normal win (a whole run: its end bosses come from the timeline) and a normal death in chapter 1's first stage.
        var normal = Run("2026-10-17_09-00-00", 17, RunResult.Ending, 1500);
        Numbers(normal, 700, 900000, 20000, 20000);
        normal.Events.Add(new RunEvent { Kind = RunEvents.Boss, Id = "enemy:Yggdrasil", Name = "Yggdrasil" });
        normal.Events.Add(new RunEvent { Kind = RunEvents.Boss, Id = "enemy:FirstHero3", Name = "First Hero" });
        var woods = Run("2026-10-17_10-00-00", 17, RunResult.Death, 300);
        woods.Started = woods.Started.AddHours(1);
        Numbers(woods, 50, 30000, 900);
        woods.Killer = "Armored Golem";
        woods.KillerId = "enemy:ArmoredGolem";
        woods.EndChapter = "Chapter1";
        woods.EndStageName = "The Deep Woods";

        var left = Run("2026-10-17_11-00-00", 17, RunResult.Abandoned, 200);
        left.Started = left.Started.AddHours(2);
        var playing = Run("2026-10-17_12-00-00", 17, RunResult.InProgress, 999);
        playing.Started = playing.Started.AddHours(3);
        Numbers(playing, 9999, 9999999, 99999);
        // Newest first, like RunJournal.Runs().
        return new List<RunRecord> { playing, left, woods, normal, golem, trap, win };
    }

    [Fact]
    public void Totals_add_up_the_finished_runs()
    {
        var r = RunRecords.Of(Runs());

        Assert.Equal(6, r.Runs); // the run being played joins when it ends
        Assert.Equal(2, r.Wins);
        Assert.Equal(3, r.Deaths);
        Assert.Equal(1, r.Abandoned);
        Assert.Equal(1268 + 52 + 1126 + 1500 + 300 + 200, r.PlaySeconds);
        Assert.Equal(855 + 32 + 419 + 700 + 50, r.Kills);
        Assert.Equal(6581805 + 37526 + 217344 + 900000 + 30000, r.Damage);
        Assert.Equal(35909 + 821 + 13307 + 20000, r.Gold);
        Assert.Equal(0.4, r.WinRate, 3); // 2 of 5 ended runs: an abandoned run never ended
        Assert.Equal(new DateTime(2026, 10, 16, 12, 0, 0), r.Since);
    }

    [Fact]
    public void Bests_name_the_run_that_set_them()
    {
        var runs = Runs();
        var late = Run("2026-10-17_13-00-00", 17, RunResult.Ending, 600);
        late.Numbers[RunNumbers.JoinedLate] = 1; // found halfway: its time is only part of the run
        runs.Insert(0, late);

        var r = RunRecords.Of(runs);

        Assert.Equal(1268, r.FastestWin.Value);
        Assert.Equal("2026-10-16_10-14-19", r.FastestWin.Run.Id);
        Assert.Equal(10, r.HighestLevelWon.Value);
        Assert.Equal(6581805, r.MostDamage.Value);
        Assert.Equal(74567, r.BiggestHit.Value);
        Assert.Equal(855, r.MostKills.Value);
        Assert.Equal(1500, r.LongestRun.Value);
        Assert.Equal("2026-10-17_09-00-00", r.LongestRun.Run.Id);

        // A tie: the run that got there first keeps the record.
        var again = Run("2026-10-18_09-00-00", 18, RunResult.Ending, 1268, dark: true, level: 10);
        r = RunRecords.Of(runs.Prepend(again));
        Assert.Equal("2026-10-16_10-14-19", r.FastestWin.Run.Id);
        Assert.Equal("2026-10-16_10-14-19", r.HighestLevelWon.Run.Id);
    }

    [Fact]
    public void Skulls_count_time_runs_and_wins_held_at_the_end()
    {
        var r = RunRecords.Of(Runs());

        var skul = r.Skulls[0]; // most played first
        Assert.Equal("skull:Skul", skul.Id);
        Assert.Equal("skull:Skeleton_Spear_4", r.Skulls[1].Id);
        var captain = r.Skulls[1];
        Assert.Equal("Pikeman Captain", captain.Name);
        Assert.Equal(1245, captain.Seconds);
        Assert.Equal(1, captain.Runs);
        Assert.Equal(1, captain.Wins);
        Assert.Equal(21 + 52 + 1126 + 1500 + 300 + 200, skul.Seconds);
        Assert.Equal(6, skul.Runs);
        Assert.Equal(1, skul.Wins);   // the normal win; in the Dark Mirror win the captain was held
        Assert.Equal(1, r.Skulls.Single(s => s.Id == "skull:AquaSkull").Runs);
        Assert.Equal(r.Skulls.Sum(s => s.Seconds), r.SkullSeconds);
    }

    [Fact]
    public void End_bosses_count_defeats_and_the_deaths_they_caused()
    {
        var runs = Runs();
        var hero = Run("2026-10-18_09-00-00", 18, RunResult.Death, 900, dark: true, level: 3);
        hero.KillerId = "enemy:FirstDarkHero"; // the First Hero's dark form: the same boss
        hero.Killer = "First Dark Hero";
        runs.Insert(0, hero);

        var r = RunRecords.Of(runs);

        Assert.Equal(new[] { "enemy:FirstHero1", "enemy:DarkSkul1", "enemy:Alexander1" }, r.Bosses.Select(b => b.Id));
        var first = r.Bosses[0];
        Assert.Equal(2, first.Defeated);   // the summary's end bosses and the whole run's timeline (FirstHero3 is his phase)
        Assert.Equal(1, first.KilledYou);
        Assert.Equal(1, r.Bosses.Single(b => b.Id == "enemy:Alexander1").Defeated);
    }

    [Fact]
    public void Deaths_count_killers_and_places_the_same_in_both_modes()
    {
        var r = RunRecords.Of(Runs());

        Assert.Equal("enemy:ArmoredGolem", r.Killers[0].Id);
        Assert.Equal(2, r.Killers[0].Count);
        Assert.Equal("ThornTrap", r.Killers[1].Name);
        Assert.Equal("", r.Killers[1].Id);
        // The Dark Mirror's chapter 1 is chapter 1's stages: one place.
        var woods = r.Places[0];
        Assert.Equal("Chapter1/0", woods.Id);
        Assert.Equal("The Deep Woods", woods.Name);
        Assert.Equal(2, woods.Count);
        Assert.Equal("Chapter3/0", RunRecords.PlaceKey(new RunRecord { EndChapter = "HardmodeChapter3" }));
        Assert.Equal("HardmodeChapter6/0", RunRecords.PlaceKey(new RunRecord { EndChapter = "HardmodeChapter6" }));
    }

    [Fact]
    public void Filters_split_normal_and_dark_mirror_runs()
    {
        var runs = Runs();
        var normal = RunRecords.Of(runs, RecordFilter.Normal);
        var dark = RunRecords.Of(runs, RecordFilter.DarkMirror);

        Assert.Equal(3, normal.Runs);
        Assert.Equal(1, normal.Wins);
        Assert.Null(normal.HighestLevelWon);
        Assert.Empty(normal.Levels);
        Assert.Equal(3, dark.Runs);
        Assert.Equal(1, dark.Wins);
        Assert.Equal(new DateTime(2026, 10, 16, 12, 0, 0), normal.Since); // the run history's start, whatever the filter

        var ten = Assert.Single(dark.Levels);
        Assert.Equal(10, ten.Level);
        Assert.Equal(3, ten.Runs);
        Assert.Equal(1, ten.Wins);
        Assert.Equal(1268, ten.FastestWin);
    }

    [Fact]
    public void The_games_best_stage_times_are_read_from_the_save_keys()
    {
        var game = new GameRecords();
        Assert.True(game.AddBestTime("Record/bestTime/HardmodeChapter2/1", 610));
        Assert.True(game.AddBestTime("Record/bestTime/Chapter3/0", 900));
        Assert.True(game.AddBestTime("Record/bestTime/Chapter1/1", 120));
        Assert.True(game.AddBestTime("Record/bestTime/Chapter1/1", 110)); // the same stage again replaces it
        Assert.False(game.AddBestTime("Record/bestTime/Chapter2/0", int.MaxValue)); // the game's "none"
        Assert.False(game.AddBestTime("Progress/deaths", 5));
        Assert.False(game.AddBestTime("Record/bestTime/Chapter2", 5));

        Assert.Equal(new[] { "Chapter1/1", "Chapter3/0", "HardmodeChapter2/1" }, game.BestTimes.Select(t => t.Chapter + "/" + t.Stage));
        Assert.Equal(110, game.BestTimes[0].Seconds);
        Assert.True(game.BestTimes[2].DarkMirror);
        // Stage names as the game's Chapter.stageName builds their keys.
        Assert.Equal("map/Chapter2/1", GameRecords.StageNameKey("HardmodeChapter2", 1));
        Assert.Equal("map/Chapter4/2", GameRecords.StageNameKey("Chapter4", 2));
        Assert.Equal("map/chapter6/0", GameRecords.StageNameKey("HardmodeChapter6", 1));
    }

    private static readonly RunLookup Look = new()
    {
        Name = id => id switch
        {
            "enemy:FirstHero1" => "First Hero",
            "enemy:DarkSkul1" => "Dark Skeleton",
            "enemy:Alexander1" => "Alexander",
            "enemy:ArmoredGolem" => "Armored Golem",
            "skull:Skeleton_Spear_4" => "Pikeman Captain",
            _ => null,
        },
        HasPage = id => id.StartsWith("skull:") || id.StartsWith("enemy:"),
        Title = id => id == "enemy:Alexander1" ? "Emperor of Carleon" : "",
    };

    [Fact]
    public void Record_pages_say_the_numbers_and_link_their_runs()
    {
        var r = RunRecords.Of(Runs());
        var game = new GameRecords { Deaths = 224, ClearedLevel = 10, ClearedCount = 12, HiddenBossLevel = 10, EmperorCleared = true };
        game.AddBestTime("Record/bestTime/HardmodeChapter6/0", 1200);
        game.AddBestTime("Record/bestTime/HardmodeChapter6/1", 1250);
        game.AddBestTime("Record/bestTime/Chapter2/0", 400);
        foreach (var t in game.BestTimes)
            t.Name = t.Chapter == "HardmodeChapter6" ? "Demon King Castle" : "";

        string totals = PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Totals, r, game, Look));
        Assert.Contains("40%", totals);              // win rate
        Assert.Contains("Dark Mirror 10", totals);   // the levels
        Assert.Contains("1 of 3 won", totals);
        Assert.Contains("2026-10-16", totals);       // where the numbers start

        string bests = RecordPages.Text(RecordPages.Page.Bests, r, game, Look);
        Assert.Contains("<link=\"run:2026-10-16_10-14-19\">", bests); // the fastest win's page
        Assert.Contains("21:08", PageBlocks.Flatten(bests));
        Assert.Contains("codex:skull:Skeleton_Spear_4", bests);       // the skull held at its end

        string skulls = PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Skulls, r, game, Look));
        Assert.Contains("Pikeman Captain", skulls);
        Assert.Contains("1 runs · 1 won", skulls);

        string bosses = PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Bosses, r, game, Look));
        Assert.Contains("Alexander", bosses);
        Assert.Contains("Emperor of Carleon", bosses);
        Assert.Contains("Defeated: 2 · killed you: 0", bosses);

        string deaths = PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Deaths, r, game, Look));
        Assert.Contains("Armored Golem", deaths);
        Assert.Contains("Thorn Trap", deaths); // an old run's code name, made readable
        Assert.Contains("224", deaths);        // the game's own counter

        string saved = PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Game, r, game, Look));
        Assert.Contains("Dark Skeleton defeated", saved);
        Assert.Contains("Demon King Castle · stage 2", saved); // its stages share one name
        Assert.Contains("Chapter 2 · stage 1", saved);         // no name: the chapter and stage
        string darkOnly = PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Game, RunRecords.Of(Runs(), RecordFilter.DarkMirror), game, Look));
        Assert.DoesNotContain("Chapter 2 · stage 1", darkOnly);

        var (detail, headline, caption) = RecordPages.Row(RecordPages.Page.Bests, r, game, Look);
        Assert.Equal("21:08", headline);
        Assert.Equal("Fastest win", caption);
        Assert.Contains("74,567", detail);
        Assert.Equal("10", RecordPages.Row(RecordPages.Page.Game, r, game, Look).headline);
        Assert.Equal("codex:skull:Skul", RecordPages.Icon(RecordPages.Page.Skulls, r));
        Assert.Equal("codex:enemy:FirstHero1", RecordPages.Icon(RecordPages.Page.Bosses, r));
    }

    [Fact]
    public void Without_runs_the_pages_say_so()
    {
        var none = RunRecords.Of(Array.Empty<RunRecord>());
        Assert.Equal(0, none.Runs);
        Assert.Null(none.FastestWin);
        Assert.Contains("No runs yet", RecordPages.Text(RecordPages.Page.Totals, none, new GameRecords(), Look));
        var noDarkMirror = RunRecords.Of(Runs().Where(x => !x.DarkMirror), RecordFilter.DarkMirror);
        Assert.Contains("No run fits this filter", RecordPages.Text(RecordPages.Page.Bests, noDarkMirror, new GameRecords(), Look));
        Assert.Equal("–", RecordPages.Row(RecordPages.Page.Bests, none, new GameRecords(), Look).headline);
        Assert.Contains("0.13", RecordPages.Note(none));
        // The game's records don't need runs.
        Assert.Contains("No best times yet", PageBlocks.Flatten(RecordPages.Text(RecordPages.Page.Game, none, new GameRecords(), Look)));
    }

    [Fact]
    public void The_records_tab_opens_on_the_totals()
    {
        foreach (RecordPages.Page page in Enum.GetValues(typeof(RecordPages.Page)))
            Assert.Equal(page, RecordPages.PageOf(RecordPages.IdOf(page)));
        Assert.Equal(RecordPages.Page.Totals, RecordPages.PageOf(null));
        Assert.Equal(CodexNavigation.RecordsDefault, RecordPages.IdOf(RecordPages.Page.Totals));

        var nav = new CodexNavigation();
        nav.SwitchSection(CodexSection.You);
        nav.SwitchCategory(CodexCategory.Records);
        Assert.Equal(CodexSection.You, nav.Section);
        Assert.Equal(CodexNavigation.RecordsDefault, nav.SelectedId);
        nav.Select(RecordPages.IdOf(RecordPages.Page.Bests));
        nav.Go(new CodexNavigation.Place(CodexSection.You, CodexCategory.Runs, "2026-10-16_10-14-19")); // a best's run link
        Assert.True(nav.Back());
        Assert.Equal(CodexCategory.Records, nav.Category);
        Assert.Equal("records:bests", nav.SelectedId);
    }
}
