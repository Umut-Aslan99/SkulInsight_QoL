using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageInsight.Codex;
using DamageInsight.History;
using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Run history pages (0.13 batch 2): the index and shrinking, the page blocks, the pixel icons, the texts.</summary>
public class RunPagesTests
{
    private static RunRecord Run(string id, int day, RunResult result = RunResult.Death, bool favourite = false)
    {
        var r = new RunRecord
        {
            Id = id,
            Seed = day,
            Started = new DateTime(2026, 10, day, 12, 0, 0),
            Ended = new DateTime(2026, 10, day, 13, 0, 0),
            Result = result,
            Favourite = favourite,
            StartPlay = 10,
            EndPlay = 610,
            Killer = "Fanatic",
            EndStageName = "Gate",
        };
        r.Numbers[RunNumbers.Kills] = 120;
        r.Build.Add(new RunGear { Kind = "skull", Id = "weapon:Skul", Name = "Skul", Extra = "held|" });
        r.Build.Add(new RunGear { Kind = "item", Id = "item:Ring", Name = "Ring" });
        r.Events.Add(new RunEvent { T = 20, Kind = RunEvents.ItemTaken, Id = "item:Ring", Name = "Ring" });
        r.Maps.Add(new RunMap { T = 12, Chapter = "Chapter1", Stage = 0, StageName = "Gate", Map = "22", Type = "Normal", Seconds = 60 });
        r.Damage.Add(new RunDamage { Group = "Items", Name = "Ring", Amount = 500, Hits = 5, Biggest = 200 });
        r.DamageTaken["Fanatic"] = (90, 3);
        r.Picture = true;
        return r;
    }

    [Fact]
    public void A_shrunk_run_keeps_its_row_and_numbers_only()
    {
        var full = Run("a", 1);
        var small = full.Shrink();

        Assert.True(small.Shrunk);
        Assert.False(small.Picture);
        Assert.Empty(small.Events);
        Assert.Empty(small.Maps);
        Assert.Empty(small.Damage);
        Assert.Empty(small.DamageTaken);
        Assert.Equal(120, small.Number(RunNumbers.Kills));
        Assert.Equal(2, small.Build.Count);
        Assert.Equal("Fanatic", small.Killer);
        // The index's summary says what the file still has.
        var summary = full.Summary();
        Assert.False(summary.Shrunk);
        Assert.True(summary.Picture);
        Assert.Empty(summary.Events);
    }

    [Fact]
    public void Old_runs_without_a_star_shrink_after_the_newest_ones()
    {
        var runs = new List<RunRecord>
        {
            Run("d1", 1), Run("d2", 2, favourite: true), Run("d3", 3), Run("d4", 4), Run("d5", 5, RunResult.InProgress),
        };
        runs.Single(r => r.Id == "d1").Shrunk = false;

        var ids = RunIndex.ToShrink(runs, keepFull: 2);

        // Newest finished first: d4, d3 kept; d2 starred; d1 shrinks; d5 is running.
        Assert.Equal(new[] { "d1" }, ids);
        Assert.Empty(RunIndex.ToShrink(runs.Select(r => { if (r.Id == "d1") r.Shrunk = true; return r; }).ToList(), 2));
    }

    [Fact]
    public void The_index_follows_the_files()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skulinsight_runs_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a.json"), Run("a", 1).ToJson());
            File.WriteAllText(Path.Combine(folder, "b.json"), Run("b", 2, RunResult.Ending).ToJson());

            var index = RunIndex.Load(folder, out bool built);
            Assert.True(built);
            Assert.Equal(new[] { "b", "a" }, index.Select(r => r.Id));
            Assert.True(File.Exists(Path.Combine(folder, RunIndex.FileName)));
            Assert.Empty(index[0].Events); // summaries only

            // A file removed by hand leaves the list; a new one joins it.
            File.Delete(Path.Combine(folder, "a.json"));
            File.WriteAllText(Path.Combine(folder, "c.json"), Run("c", 3).ToJson());
            index = RunIndex.Load(folder, out bool changed);
            Assert.True(changed);
            Assert.Equal(new[] { "c", "b" }, index.Select(r => r.Id));

            RunIndex.Load(folder, out bool again);
            Assert.False(again);

            var starred = Run("b", 2, RunResult.Ending, favourite: true);
            index = RunIndex.Upsert(index, starred);
            Assert.True(index.Single(r => r.Id == "b").Favourite);
            Assert.Equal(2, index.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_broken_index_is_rebuilt() => Assert.Empty(RunIndex.FromJson("{\"runs\":[{\"id\""));

    [Fact]
    public void An_index_of_an_older_format_is_built_again() =>
        Assert.Empty(RunIndex.FromJson("{\"format\":1,\"runs\":[" + Run("a", 1).Summary().ToJson() + "]}"));

    [Fact]
    public void Page_blocks_icons_bars_and_lines()
    {
        string text = PageBlocks.Section("Build", "inscription:Arms") +
                      PageBlocks.Icons(new[] { ("codex:item:Ring", "item:Ring"), ("pix:star", "") }) +
                      PageBlocks.Bar("Ring", "500 (40%)", 0.4) +
                      PageBlocks.Bar("Too much", "x", 7) +
                      PageBlocks.Line("pix:heart", "0:20  Revived");

        var block = Assert.Single(PageBlocks.Parse(text));
        Assert.Equal("Build", block.Title);
        Assert.Equal(4, block.Items.Count);
        Assert.Equal(PageBlocks.Kind.Icons, block.Items[0].Kind);
        Assert.Equal(new[] { ("codex:item:Ring", "item:Ring"), ("pix:star", "") }, block.Items[0].Icons);
        Assert.Equal(PageBlocks.Kind.Bar, block.Items[1].Kind);
        Assert.Equal("500 (40%)", block.Items[1].Value);
        Assert.Equal(0.4, block.Items[1].Fill, 3);
        Assert.Equal(1.0, block.Items[2].Fill, 3);
        Assert.Equal(PageBlocks.Kind.Line, block.Items[3].Kind);
        Assert.Equal("pix:heart", block.Items[3].Icon);
        Assert.Contains("Revived", PageBlocks.Flatten(text));
    }

    [Fact]
    public void Pixel_icons_are_square_and_the_empty_star_is_an_outline()
    {
        foreach (var name in PixelIcons.Names)
        {
            var pixels = PixelIcons.Pixels(name);
            Assert.NotNull(pixels);
            Assert.Equal(11, pixels.GetLength(0));
            Assert.Equal(11, pixels.GetLength(1));
        }
        var star = PixelIcons.Pixels("star");
        var empty = PixelIcons.Pixels("star_empty");
        int filled = Count(star), outline = Count(empty);
        Assert.InRange(outline, 1, filled - 1);
        Assert.Equal(0, empty[5, 5].a); // the middle is see-through
        Assert.Null(PixelIcons.Pixels("nope"));

        static int Count(UnityEngine.Color32[,] p) => Enumerable.Range(0, 11).Sum(y => Enumerable.Range(0, 11).Count(x => p[y, x].a > 0));
    }

    [Fact]
    public void Run_pages_say_what_happened()
    {
        var run = Run("a", 1);
        run.KillerId = "enemy:Fanatic";
        run.Events.Add(new RunEvent { T = 40, Kind = RunEvents.SkullTaken, Id = "weapon:Werewolf", Name = "Werewolf", Other = "weapon:Skul", OtherName = "Skul" });
        run.Events.Add(new RunEvent { T = 50, Kind = RunEvents.Inscription, Id = "Arms", Name = "Arms", Value = 2 });
        run.SkullSeconds["weapon:Skul"] = 30;
        run.SkullSeconds["weapon:Werewolf"] = 570;
        var look = new RunLookup { Name = id => id == "weapon:Werewolf" ? "Loup-garou" : null, HasPage = id => id.StartsWith("weapon:") };

        string overview = PageBlocks.Flatten(RunPages.Page(run, RunPages.Tab.Overview, look));
        Assert.Contains("Fanatic", overview);
        Assert.Contains("10:00", overview); // run time 610 - 10
        string timeline = RunPages.Page(run, RunPages.Tab.Timeline, look);
        string flat = PageBlocks.Flatten(timeline);
        Assert.Contains("Loup-garou", flat);                         // the Codex name in the current language
        Assert.Contains("<link=\"weapon:Werewolf\">", timeline);     // and a link to its page
        Assert.Contains("Skul", flat);
        Assert.Contains("Arms", flat);
        Assert.Contains("Gate", flat);                               // the stage's section
        Assert.Contains("0:30", flat);                               // event times from the run's start
        string damage = PageBlocks.Flatten(RunPages.Page(run, RunPages.Tab.Damage, look));
        Assert.Contains("Ring", damage);
        Assert.Contains("Fanatic", damage);

        var shrunk = run.Shrink();
        Assert.DoesNotContain("Loup-garou", PageBlocks.Flatten(RunPages.Page(shrunk, RunPages.Tab.Timeline, look)));
    }

    [Fact]
    public void A_daily_board_run_shows_the_same_pages_as_the_run_history()
    {
        // The run as the player's run history keeps it, sent with the result and given back by the server
        // (GET /api/runs/detail): the board's run page must be the run history's page, links and all.
        var run = Run("a", 1);
        run.Daily = "2026-10-18";
        run.KillerId = "enemy:Fanatic";
        run.Events.Add(new RunEvent { T = 40, Kind = RunEvents.SkullTaken, Id = "weapon:Werewolf", Name = "Werewolf", Other = "weapon:Skul", OtherName = "Skul" });
        run.Events.Add(new RunEvent { T = 50, Kind = RunEvents.Inscription, Id = "Arms", Name = "Arms", Value = 2 });
        run.SkullSeconds["weapon:Werewolf"] = 570;
        run.Picture = true;
        var look = new RunLookup { Name = id => id == "weapon:Werewolf" ? "Loup-garou" : null, HasPage = id => id.StartsWith("weapon:") };
        AssertSamePages(run, look);

        // Optional: a real run file (RUN_FILE=path), e.g. a run of the game's own run history.
        string real = System.Environment.GetEnvironmentVariable("RUN_FILE");
        if (!string.IsNullOrEmpty(real) && File.Exists(real))
            AssertSamePages(RunRecord.FromJson(File.ReadAllText(real)), look);
    }

    private static void AssertSamePages(RunRecord run, RunLookup look)
    {
        string detail = "{\"date\":\"2026-10-18\",\"mode\":\"darkmirror\",\"level\":10,\"factor\":2.5," +
                        "\"entry\":{\"rank\":1,\"id\":\"0123456789abcdef\",\"name\":\"Other\",\"points\":1}," +
                        "\"breakdown\":[],\"run\":" + run.ToJson() + "}";
        var back = DamageInsight.Daily.RunDetail.FromJson(detail).Run;
        Assert.NotNull(back);
        foreach (RunPages.Tab tab in new[] { RunPages.Tab.Overview, RunPages.Tab.Damage, RunPages.Tab.Timeline })
            Assert.Equal(RunPages.Page(run, tab, look), RunPages.Page(back, tab, look));
        Assert.Equal(RunPages.ResultWord(run), RunPages.ResultWord(back));
        Assert.Equal(RunPages.Mode(run), RunPages.Mode(back));
        Assert.Equal(RunPages.Decider(run), RunPages.Decider(back));   // the boss / killer in the frame
        Assert.Equal(run.Build.Count, back.Build.Count);
        Assert.Equal(run.Events.Count, back.Events.Count);
        Assert.Equal(run.Damage.Count, back.Damage.Count);
        Assert.Equal(run.ScreenTitle, back.ScreenTitle);              // the End screen button's panel
        Assert.False(back.Picture);                                    // their death cam picture isn't on this PC
    }

    [Theory]
    [InlineData(59, "0:59")]
    [InlineData(610, "10:10")]
    [InlineData(3725, "1:02:05")]
    public void Run_times(int seconds, string text) => Assert.Equal(text, RunPages.Time(seconds));

    [Theory]
    [InlineData(950, "950")]
    [InlineData(12_400, "12.4K")]
    [InlineData(250_000, "250K")]
    [InlineData(3_140_000, "3.1M")]
    public void Short_numbers(double value, string text) => Assert.Equal(text, RunPages.Short(value));

    [Fact]
    public void The_kind_of_run()
    {
        Assert.Equal("Dark Mirror 10", RunPages.Mode(new RunRecord { DarkMirror = true, Level = 10 }));
        Assert.Equal("Rookie mode", RunPages.Mode(new RunRecord { Easy = true }));
        Assert.Equal("Normal", RunPages.Mode(new RunRecord()));
        Assert.Equal("pix:crown", RunPages.ResultIcon(new RunRecord { Result = RunResult.Ending }));
        Assert.Equal(("boss:x", "X"), RunPages.Decider(new RunRecord { Result = RunResult.Ending, LastBossId = "boss:x", LastBoss = "X" }));
    }

    [Theory]
    // death: normal / Dark Mirror
    [InlineData(false, false, 0, 0, false, RunPages.KeyTitle, RunPages.KeySubtitle, "", "normal")]
    [InlineData(false, true, 10, 9, false, RunPages.KeyTitle, RunPages.KeyHardSubtitle, "", "hardmode")]
    // endings: normal, a Dark Mirror level the first time / again, the emperor below and at the top level
    [InlineData(true, false, 0, 0, false, RunPages.KeyEndingTitle, RunPages.KeyEndingSubtitle, RunPages.KeyEndingStage, "normal")]
    [InlineData(true, true, 4, 3, false, RunPages.KeyEndingTitle, RunPages.KeyHardEndingFirst, RunPages.KeyEndingStage, "hardmode")]
    [InlineData(true, true, 4, 9, false, RunPages.KeyEndingTitle, RunPages.KeyHardEnding, RunPages.KeyEndingStage, "hardmode")]
    [InlineData(true, true, 7, 9, true, RunPages.KeyEndingTitle, RunPages.KeyEmperorEnding, RunPages.KeyEndingStage, "hardmode")]
    [InlineData(true, true, 10, 10, true, RunPages.KeyEndingTitle, RunPages.KeyEmperor10Ending, RunPages.KeyEmperor10Stage, "emperor10")]
    public void The_end_screen_is_the_one_the_game_shows(bool ending, bool hardmode, int level, int cleared, bool emperor,
        string title, string subtitle, string stage, string panel)
    {
        var s = RunPages.ScreenFor(ending, hardmode, level, 10, cleared, emperor);
        Assert.Equal(title, s.Title);
        Assert.Equal(subtitle, s.Subtitle);
        Assert.Equal(stage, s.Stage);
        Assert.Equal(panel, s.Panel);
        Assert.Equal(!ending, s.YourEnemy);
    }

    [Fact]
    public void Runs_kept_before_the_screen_keys_get_theirs_worked_out()
    {
        // Umut's DM 10 win of 2026-10-16 (recorded by batch 1): the emperor's top-level ending.
        var win = new RunRecord { Result = RunResult.Ending, DarkMirror = true, Level = 10, Ending = "emperor" };
        win.Events.Add(new RunEvent { Kind = RunEvents.Boss, Id = "enemy:Alexander1", Name = "Emperor_Renewal" });
        win.Events.Add(new RunEvent { Kind = RunEvents.Boss, Id = "enemy:Alexander4", Name = "Emperor4" });
        var s = RunPages.Screen(win);
        Assert.Equal(RunPages.KeyEmperor10Ending, s.Subtitle);
        Assert.Equal("emperor10", s.Panel);
        Assert.Equal("enemy:Alexander1", RunPages.Decider(win).id); // the last boss from the timeline, as its first phase
        // Kept keys win over working them out.
        win.ScreenSubtitle = "x";
        win.ScreenTitle = "y";
        Assert.Equal("x", RunPages.Screen(win).Subtitle);
    }

    [Fact]
    public void Obtained_counts_add_what_the_run_holds()
    {
        // Umut's DM 10 death of 2026-10-16: the game showed skulls 1, items 4 (its essence line showed the item count, 1).
        var run = new RunRecord();
        run.Numbers[RunNumbers.SkullsSeen] = 0;
        run.Numbers[RunNumbers.ItemsSeen] = 1;
        run.Numbers[RunNumbers.EssencesSeen] = 0;
        run.Build.Add(new RunGear { Kind = "skull", Id = "skull:Skul" });
        for (int i = 0; i < 3; i++)
            run.Build.Add(new RunGear { Kind = "item", Id = "item:" + i });
        Assert.Equal((1, 4, 0), RunPages.Obtained(run));
    }

    [Theory]
    [InlineData("ThornTrap", "Thorn Trap")]
    [InlineData("Spike_Trap(Clone)", "Spike Trap")]
    [InlineData("", "")]
    public void Readable_names(string raw, string text) => Assert.Equal(text, RunPages.Readable(raw));

    [Theory]
    // Umut's DM 10 run (2026-10-16): rows the combat log only knew by code names.
    [InlineData("Skeleton spear 4 endless rush", "skull")]
    [InlineData("Unbreakable pride", "pride")]
    [InlineData("Mystery bright light spirit attack", "mystery")]
    [InlineData("08", null)]
    [InlineData("Skeleton", null)]
    public void Code_names_find_their_gear(string origin, string expected)
    {
        var match = new DamageInsight.Recording.OriginMatch<string>(new[]
        {
            ("Skeleton_Spear_4", "skull"), ("Skeleton_Spear", "shorter"), ("UnbreakablePride", "pride"), ("Mystery", "mystery"), ("Ow", "too short"),
        });
        Assert.Equal(expected, match.Find(origin));
    }

    [Fact]
    public void Other_rows_of_old_runs_move_to_their_gear()
    {
        var look = new RunLookup
        {
            Origin = name => name switch
            {
                "Skeleton spear 4 endless rush" => (DamageInsight.Recording.GearGroup.Skulls, "Pikeman Captain"),
                "Unbreakable pride" => (DamageInsight.Recording.GearGroup.Items, "Indomitable Will"),
                _ => null,
            },
        };
        var rows = RunPages.Regroup(new[]
        {
            new RunDamage { Group = "Skulls", Name = "Pikeman Captain: Dash attack", Amount = 900, Hits = 9, Biggest = 200 },
            new RunDamage { Group = "Other", Name = "Skeleton spear 4 endless rush", Amount = 100, Hits = 4, Biggest = 50 },
            new RunDamage { Group = "Items", Name = "Indomitable Will", Amount = 10, Hits = 1, Biggest = 10 },
            new RunDamage { Group = "Other", Name = "Unbreakable pride", Amount = 5, Hits = 1, Biggest = 5 },
            new RunDamage { Group = "Other", Name = "08", Amount = 3, Hits = 1, Biggest = 3 },
        }, look);

        Assert.Equal(new[] { "Pikeman Captain: Dash attack", "Pikeman Captain: Skills", "Indomitable Will", "08" }, rows.Select(r => r.Name));
        var will = rows.Single(r => r.Name == "Indomitable Will");
        Assert.Equal(15, will.Amount);
        Assert.Equal(2, will.Hits);
        Assert.Equal("Other", rows.Last().Group);
    }

    [Fact]
    public void A_win_names_its_bosses()
    {
        // Umut's DM 10 win: Dark Skul (optional) and King Alexander's phases.
        var win = new RunRecord { Id = "2026-10-16_10-14-19", Result = RunResult.Ending, EndStageName = "Demon King Castle Top Floor" };
        foreach (var (id, name) in new[]
                 {
                     ("enemy:Yggdrasil", "ElderEnt"), ("enemy:FirstHero1", "x"), ("enemy:FirstDarkHero", "x"), ("enemy:DarkSkul1", "DarkSkeleton_Phase1"),
                     ("enemy:DarkSkul2", "DarkSkeleton_Phase2"), ("enemy:Alexander1", "Emperor_Renewal"), ("enemy:Alexander2_Heart", "EmperorBody"),
                     ("enemy:Alexander4", "Emperor4"),
                 })
            win.Events.Add(new RunEvent { Kind = RunEvents.Boss, Id = id, Name = name });
        var look = new RunLookup
        {
            Name = id => id switch
            {
                "enemy:Yggdrasil" => "Elder Ent", "enemy:FirstHero1" => "First Hero", "enemy:DarkSkul1" or "enemy:DarkSkul2" => "Dark Skeleton",
                _ when id.StartsWith("enemy:Alexander") => "King Alexander", _ => null,
            },
        };

        // Only the bosses that end a run (Umut: "we don't need all bosses"), each once, by its first phase; the First
        // Hero's dark form is the same fight (Umut 2026-10-17: "we see first hero twice").
        Assert.Equal(new[] { "First Hero", "Dark Skeleton", "King Alexander" }, RunPages.Bosses(win, look).Select(b => b.name));
        Assert.Equal(new[] { "enemy:FirstHero1", "enemy:DarkSkul1", "enemy:Alexander1" }, RunPages.Bosses(win, look).Select(b => b.id));
        Assert.Equal("First Hero · Dark Skeleton · King Alexander", RunPages.RowLine(win, look));
        // The list's rows are summaries without a timeline: they keep the end bosses.
        var summary = win.Summary();
        Assert.Empty(summary.Events);
        Assert.Equal("First Hero · Dark Skeleton · King Alexander", RunPages.RowLine(summary, look));
        Assert.Equal("enemy:Alexander1", RunPages.Decider(summary).id);
        Assert.Equal("enemy:Alexander1", RunPages.Decider(win).id); // the portrait: King Alexander's first phase
        Assert.Null(RunPages.EndBossOf("enemy:Yggdrasil"));
        Assert.Equal("enemy:FirstHero1", RunPages.EndBossOf("enemy:FirstHero3"));
        Assert.Contains("Dark Skeleton", PageBlocks.Flatten(RunPages.Page(win, RunPages.Tab.Overview, look)));
        Assert.Equal("Demon King Castle Top Floor", RunPages.RowLine(new RunRecord { Result = RunResult.Ending, EndStageName = "Demon King Castle Top Floor" }, look));
    }

    [Fact]
    public void The_overview_shows_the_fragment_the_tuned_inscription_and_the_stats()
    {
        var run = new RunRecord { Result = RunResult.Ending };
        run.Build.Add(new RunGear { Kind = "fragment", Id = "fragment:RibsOfTerror", Name = "Rib of Horror" });
        run.Build.Add(new RunGear { Kind = "insc", Id = "Poisoning", Name = "Poisoning", Value = 4, Step = 3, Extra = "tuned" });
        run.Numbers[RunNumbers.StatPrefix + "CriticalChance"] = 1.35;
        run.Numbers[RunNumbers.StatPrefix + "CriticalDamage"] = 1.8;
        run.Numbers[RunNumbers.StatPrefix + "PhysicalAttackDamage"] = 2.5;
        var look = new RunLookup { StatName = key => key == "criticalChance" ? "Critical Rate" : null };

        string text = PageBlocks.Flatten(RunPages.Page(run, RunPages.Tab.Overview, look));
        Assert.Contains("Rib of Horror", text);
        Assert.Contains("Poisoning 4", text);
        Assert.Contains("Tuned", text);
        Assert.Contains("Critical Rate", text);   // the game's own name
        Assert.Contains("+35%", text);
        Assert.Contains("x1.80", text);
        Assert.Contains("250%", text);
        Assert.Contains("Stats at the end", text);
    }

    [Fact]
    public void The_you_tabs_open_on_the_build()
    {
        var nav = new CodexNavigation();
        nav.SwitchSection(CodexSection.You);
        Assert.Equal(CodexCategory.YouBuild, nav.Category);
        nav.SwitchCategory(CodexCategory.Runs);
        Assert.Null(nav.SelectedId);
        nav.SelectQuietly("2026-10-16_12-00-00");
        nav.SwitchCategory(CodexCategory.YouBuild);
        Assert.Equal(CodexNavigation.YouDefault, nav.SelectedId);
        Assert.True(nav.Back());
        Assert.Equal(CodexCategory.Runs, nav.Category);
    }
}
