using System.Collections.Generic;
using System.Linq;
using DamageInsight.Codex;
using DamageInsight.Daily;
using Platforms;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The daily run (0.13): the server's answers, the local state, the evened-out profile, the texts.</summary>
public class DailyTests
{
    [Fact]
    public void The_days_run_from_the_server()
    {
        var info = DailyInfo.FromJson("{\"date\":\"2026-10-17\",\"seed\":-123456,\"mode\":\"darkmirror\",\"level\":7,\"formula\":1,\"secondsLeft\":3600}");
        Assert.Equal("2026-10-17", info.Date);
        Assert.Equal(-123456, info.Seed);
        Assert.True(info.DarkMirror);
        Assert.Equal(7, info.Level);
        Assert.Equal(3600, info.SecondsLeft);
        Assert.Null(DailyInfo.FromJson("<html>proxy error</html>"));
        Assert.Null(DailyInfo.FromJson(""));
    }

    [Fact]
    public void A_board_from_the_server()
    {
        var board = Board.FromJson("{\"date\":\"2026-10-17\",\"mode\":\"normal\",\"level\":0,\"count\":2,\"entries\":[" +
            "{\"rank\":1,\"name\":\"Winner\",\"points\":31000,\"result\":\"win\",\"playSeconds\":1300,\"stagesReached\":16," +
            "\"bossKills\":10,\"endBosses\":[\"First Hero\",\"Alexander\"],\"skull\":\"Pikeman Captain\",\"kills\":850}," +
            "{\"rank\":2,\"name\":\"Me\",\"points\":900,\"result\":\"death\",\"playSeconds\":52,\"you\":true}]," +
            "\"you\":{\"rank\":2,\"name\":\"Me\",\"points\":900,\"result\":\"death\",\"you\":true}}");
        Assert.Equal(2, board.Count);
        Assert.Equal(new[] { "First Hero", "Alexander" }, board.Entries[0].EndBosses);
        Assert.True(board.Entries[0].Won);
        Assert.True(board.Entries[1].You);
        Assert.Equal(2, board.You.Rank);
        Assert.Null(Board.FromJson("{\"error\":\"too_many_requests\"}"));

        var answer = FinishAnswer.FromJson("{\"ok\":true,\"name\":\"Me\",\"points\":900,\"rank\":2,\"of\":2}");
        Assert.Equal((900, 2, 2), (answer.Points, answer.Rank, answer.Of));
        Assert.Null(FinishAnswer.FromJson("{\"detail\":\"already_played\"}"));
    }

    [Fact]
    public void The_state_on_this_pc_round_trips_and_forgets_old_days()
    {
        var state = new DailyState { Name = "Doktor Run", Origin = 3 };
        state.EnsurePlayer();
        var t = state.Ensure("2026-10-17");
        t.State = "ended";
        t.Seed = -5;
        t.Mode = "darkmirror";
        t.Level = 10;
        t.Numbers = "{\"result\":\"win\"}";
        t.RunId = "2026-10-17_10-00-00";
        t.Run = "{\"id\":\"2026-10-17_10-00-00\",\"result\":\"Death\"}";
        state.Ensure("2026-07-01").State = "finished";

        var back = DailyState.FromJson(state.ToJson());
        Assert.Equal(state.PlayerId, back.PlayerId);
        Assert.Equal(32, back.PlayerId.Length);
        Assert.Equal(3, back.Origin);
        var bt = back.TryOf("2026-10-17");
        Assert.Equal(("ended", -5, "darkmirror", 10), (bt.State, bt.Seed, bt.Mode, bt.Level));
        Assert.Equal("{\"result\":\"win\"}", bt.Numbers);
        Assert.Equal(t.Run, bt.Run);
        Assert.Equal(t.RunId, bt.RunId);
        Assert.True(bt.Played);

        back.Trim("2026-10-17");
        Assert.Null(back.TryOf("2026-07-01"));
        Assert.NotNull(back.TryOf("2026-10-17"));
    }

    [Fact]
    public void The_daily_profile_evens_out_a_save()
    {
        var save = new SaveFileModel();
        save.SetInt("HardmodeProgress/clearedLevel", 10); // the player's own: more than the day's
        save.SetInt("Currency/darkQuartz/balance", 9000);
        save.SetBool("Save/hasSave", true);
        save.SetInt("Progress/WitchMastery/skull/0", 1);
        var facts = new DailyProfileFacts { TopDarkMirrorLevel = 5 }; // a Dark Mirror 5 day
        facts.GearUnlockKeys.Add("gear/unlocked/Weapon/Skeleton_Spear_4");
        facts.WitchTop["Progress/WitchMastery/skull/0"] = 5;
        facts.Darktech.Add("LuckyMeasuringInstrument");

        DailyProfile.Even(save, facts);

        Assert.True(save.GetBool("gear/unlocked/Weapon/Skeleton_Spear_4"));
        Assert.Equal(5, save.GetInt("Progress/WitchMastery/skull/0"));
        Assert.True(save.GetBool("HardmodeProgress/_unlocked/LuckyMeasuringInstrument"));
        Assert.True(save.GetBool("HardmodeProgress/_activated/LuckyMeasuringInstrument"));
        Assert.Equal(5, save.GetInt("HardmodeProgress/clearedLevel")); // exactly the day's: Darktech up to it
        Assert.Equal(DailyProfile.StartDarkQuartz, save.GetInt("Currency/darkQuartz/balance"));
        Assert.Equal(15_000, DailyProfile.StartDarkQuartz);
        Assert.Equal(0, save.GetInt("Currency/gold/balance"));
        Assert.False(save.GetBool("Save/hasSave"));
        Assert.True(save.GetBool("Progress/foxRescued"));
        Assert.True(save.GetBool("BoolDataEnumArray/강화두개골제조기_Intro")); // the skull machine of Dark Mirror 7+ works at once
        Assert.True(save.GetBool("BoolDataEnumArray/dwarfEngineer_First"));
    }

    [Fact]
    public void A_board_line_with_its_bonuses_likes_and_run()
    {
        var board = Board.FromJson("{\"date\":\"2026-10-18\",\"count\":1,\"factor\":2.5,\"entries\":[" +
            "{\"rank\":1,\"id\":\"0123456789abcdef\",\"hasRun\":true,\"name\":\"Winner\",\"points\":19500,\"base\":12000,\"result\":\"win\"," +
            "\"likes\":3,\"liked\":true,\"streak\":4,\"bonuses\":[{\"key\":\"fastest\",\"points\":7500,\"value\":1300}]}]}");
        var e = board.Entries[0];
        Assert.Equal(2.5, board.Factor);
        Assert.Equal(("0123456789abcdef", true, 12000, 3, true, 4), (e.Id, e.HasRun, e.Base, e.Likes, e.Liked, e.Streak));
        Assert.Equal(("fastest", 7500, 1300), e.Bonuses[0]);

        var detail = RunDetail.FromJson("{\"date\":\"2026-10-18\",\"mode\":\"darkmirror\",\"level\":10,\"factor\":2.5," +
            "\"entry\":{\"rank\":1,\"id\":\"0123456789abcdef\",\"name\":\"Winner\",\"points\":19500,\"base\":12000,\"result\":\"win\"," +
            "\"bonuses\":[{\"key\":\"fastest\",\"points\":7500,\"value\":1300},{\"key\":\"streak\",\"points\":1875,\"value\":4}]}," +
            "\"breakdown\":[{\"key\":\"win\",\"value\":1,\"points\":10000},{\"key\":\"taken\",\"value\":200,\"points\":-400}]," +
            "\"run\":{\"id\":\"2026-10-18_10-00-00\",\"result\":\"Ending\",\"picture\":true,\"favourite\":true}}");
        Assert.Equal(("darkmirror", 10, 2.5), (detail.Mode, detail.Level, detail.Factor));
        Assert.Equal(-400, detail.Breakdown[1].points);
        Assert.NotNull(detail.Run);
        Assert.False(detail.Run.Picture); // someone else's run: no picture of theirs on this PC
        Assert.False(detail.Run.Favourite);

        string page = PageBlocks.Flatten(DailyPages.PointsPage(detail, null));
        Assert.Contains("+10,000", page);
        Assert.Contains("-400", page);
        Assert.Contains("21:40", page);           // the fastest win's time
        Assert.Contains("4 days in a row", page);
        Assert.Contains("19,500", page);
        Assert.Contains("1 per 2,000 damage", page);

        var old = RunDetail.FromJson("{\"date\":\"2026-10-17\",\"entry\":{\"rank\":1,\"name\":\"DocRun\",\"points\":31828},\"breakdown\":[],\"run\":null}");
        Assert.Null(old.Run);
        Assert.Contains("older version", PageBlocks.Flatten(DailyPages.PointsPage(old, null)));
        Assert.Null(RunDetail.FromJson("{\"detail\":\"not_found\"}"));
    }

    [Fact]
    public void This_pc_follows_the_server_on_a_days_try()
    {
        var state = new DailyState();
        state.Ensure("2026-10-17").State = "finished";
        Assert.False(state.FollowServer("2026-10-17", "finished"));
        Assert.False(state.FollowServer("2026-10-17", "running"));
        Assert.True(state.FollowServer("2026-10-17", "none"));        // the admin removed it: play the day again
        Assert.Null(state.TryOf("2026-10-17"));

        var waiting = state.Ensure("2026-10-18");
        waiting.State = "finished";
        waiting.Pending = "{}";                                         // not handed over yet (no connection)
        Assert.False(state.FollowServer("2026-10-18", "none"));
        waiting.Pending = "";
        waiting.State = "started";                                      // a run in progress whose start was lost
        Assert.False(state.FollowServer("2026-10-18", "none"));
        Assert.False(state.FollowServer("2026-10-19", "none"));        // no try at all
    }

    [Fact]
    public void A_daily_run_left_during_the_run_cannot_be_continued()
    {
        var tries = new[]
        {
            new DailyTry { Date = "2026-10-17", State = "finished" },
            new DailyTry { Date = "2026-10-18", State = "started" },
        };
        Assert.Equal("2026-10-18", DailyRun.Interrupted(true, tries).Date);
        Assert.Null(DailyRun.Interrupted(false, tries));                              // the player's own slots: untouched
        Assert.Null(DailyRun.Interrupted(true, new[] { new DailyTry { Date = "2026-10-18", State = "armed" } })); // not begun yet
        Assert.Null(DailyRun.Interrupted(true, new[] { new DailyTry { Date = "2026-10-18", State = "ended" } }));  // over, waits for its name
        Assert.Contains("can't be continued", PageBlocks.Flatten(DailyPages.TodayPage(
            new DailyInfo { Date = "2026-10-18" }, null, enabled: true, "", false)));
    }

    [Fact]
    public void A_runs_numbers_for_the_server()
    {
        var result = new DailyResult { Result = "win", PlaySeconds = 1268, Kills = 855, BossKills = 10, StagesReached = 16, TotalDamage = 6581805, Skull = "Pikeman \"Captain\"" };
        result.EndBosses.Add("Alexander");
        var n = DailyInfo.Parse(result.NumbersJson());
        Assert.Equal("win", n.Str("result"));
        Assert.Equal(6581805, n.Num("totalDamage"));
        Assert.Equal("Pikeman \"Captain\"", n.Str("skull"));
        Assert.Equal(new object[] { "Alexander" }, (List<object>)n.Raw("endBosses"));
    }

    [Fact]
    public void Formula_3_numbers_flawless_rooms_and_the_boss_rooms()
    {
        var run = new DamageInsight.History.RunRecord { Id = "2026-11-02_10-00-00" };
        run.Maps.Add(new DamageInsight.History.RunMap { T = 0, Tracked = false, Kills = 0 });                // before 0.16: never flawless
        run.Maps.Add(new DamageInsight.History.RunMap { T = 0, Tracked = true, Kills = 0 });                 // the entry: no fight
        run.Maps.Add(new DamageInsight.History.RunMap { T = 10, Tracked = true, Kills = 12, Lost = 0 });     // flawless
        run.Maps.Add(new DamageInsight.History.RunMap { T = 60, Tracked = true, Kills = 9, Lost = 14, Hits = 2 });
        run.Maps.Add(new DamageInsight.History.RunMap { T = 120, Tracked = true, Kills = 1, Lost = 0, Seconds = 75 }); // the boss room
        run.Events.Add(new DamageInsight.History.RunEvent { T = 150, Kind = DamageInsight.History.RunEvents.Boss, Id = "enemy:Yggdrasil" });
        run.Numbers[DamageInsight.History.RunNumbers.BonesLeft] = 85;
        var result = new DailyResult();
        DailyRun.FillRooms(result, run);
        Assert.Equal((3, 2, 85L), (result.FightRooms, result.FlawlessRooms, result.BonesLeft));
        Assert.Equal(("enemy:Yggdrasil", 0L, 75, true), result.BossRooms.Single());

        var n = DailyInfo.Parse(result.NumbersJson());
        Assert.Equal((3, 2, 85), ((int)n.Num("fightRooms"), (int)n.Num("flawlessRooms"), (int)n.Num("bonesLeft")));
        var room = (Dictionary<string, object>)((List<object>)n.Raw("bossRooms")).Single();
        Assert.Equal(("enemy:Yggdrasil", true), ((string)room["id"], (bool)room["tracked"]));

        // The run file keeps the rooms' numbers; a run from before 0.16 reads back untracked.
        var back = DamageInsight.History.RunRecord.FromJson(run.ToJson());
        Assert.Equal(new[] { false, true, true, true, true }, back.Maps.Select(m => m.Tracked));
        Assert.Equal(14, back.Maps[3].Lost);
        Assert.True(back.Maps[2].Flawless);
    }

    [Fact]
    public void The_days_achievements_the_champions_and_flawless_chapters()
    {
        const string json = "{\"date\":\"2026-11-02\",\"formula\":3,\"secondsLeft\":60,\"boards\":[{\"board\":\"dm10\",\"mode\":\"darkmirror\"," +
            "\"level\":10,\"seed\":5,\"achievements\":[{\"id\":\"WorldsBest\",\"points\":1500},{\"id\":\"skull:WereWolf\",\"points\":1500}," +
            "{\"id\":\"chapter:2\",\"points\":1500}]}],\"champions\":[{\"board\":\"dm10\",\"name\":\"Tythos<b>\"}]}";
        var day = DailyInfo.ListFromJson(json).Single();
        Assert.Equal(new[] { "WorldsBest", "skull:WereWolf", "chapter:2" }, day.Achievements.Select(a => a.id));
        Assert.Equal(("dm10", "Tythosb"), DailyInfo.ChampionsFromJson(json).Single()); // no markup from a name
        Assert.Empty(DailyInfo.ChampionsFromJson("{\"date\":\"x\"}"));

        var skullName = DailyPages.SkullName;
        DailyPages.SkullName = id => id == "skull:WereWolf" ? "Werewolf" : null;
        try
        {
            Assert.Contains("Werewolf", DailyPages.AchievementText("skull:WereWolf"));
            Assert.Contains("chapter 2", DailyPages.AchievementText("chapter:2"));
            var t = new DailyTry { Date = "2026-11-02", Board = "dm10", State = "started" };
            t.Achievements.Add("WorldsBest");
            string page = PageBlocks.Flatten(DailyPages.TodayPage(day, t, enabled: true, "", true));
            Assert.Contains("Today's achievements", page);
            Assert.Contains("done", page);
        }
        finally
        {
            DailyPages.SkullName = skullName;
        }

        // The try keeps the game's achievements of its run.
        var state = new DailyState();
        state.Ensure("2026-11-02", "dm10").Achievements.Add("SkeletonKing");
        Assert.Equal(new[] { "SkeletonKing" }, DailyState.FromJson(state.ToJson()).TryOf("2026-11-02", "dm10").Achievements);

        // Chapters cleared without health lost: chapter 1 clean, chapter 2 hurt, chapter 3 not cleared (the run died there).
        var run = new DamageInsight.History.RunRecord { Id = "r", Result = DamageInsight.History.RunResult.Death };
        foreach (var (chapter, lost) in new[] { ("HardmodeChapter1", 0), ("HardmodeChapter1", 0), ("HardmodeChapter2", 5), ("HardmodeChapter3", 0) })
            run.Maps.Add(new DamageInsight.History.RunMap { Chapter = chapter, Tracked = true, Kills = 3, Lost = lost });
        Assert.Equal(new[] { 1 }, DailyRun.FlawlessChapters(run));
        Assert.Equal("zh-Hans", DamageInsight.Patches.WinnerNamesPatch.ScriptOf("李逍遥"));
        Assert.Equal("", DamageInsight.Patches.WinnerNamesPatch.ScriptOf("Tythos"));
    }

    [Fact]
    public void Formula_3_points_page()
    {
        var detail = RunDetail.FromJson("{\"date\":\"2026-11-02\",\"mode\":\"darkmirror\",\"level\":10,\"factor\":2.5,\"formula\":3," +
            "\"entry\":{\"rank\":1,\"name\":\"Winner\",\"points\":90000,\"base\":90000,\"result\":\"win\",\"streak\":5,\"monthBonus\":1250,\"bonuses\":[]}," +
            "\"breakdown\":[{\"key\":\"win\",\"value\":1,\"points\":20000},{\"key\":\"flawlessRooms\",\"value\":31,\"points\":4650}," +
            "{\"key\":\"extraBosses\",\"value\":2,\"points\":6000},{\"key\":\"bones\",\"value\":120,\"points\":240}]}");
        Assert.Equal(3, detail.Formula);
        Assert.Equal(1250, detail.Entry.MonthBonus);
        string page = PageBlocks.Flatten(DailyPages.PointsPage(detail, null));
        Assert.Contains("Flawless rooms", page);
        Assert.Contains("Optional bosses", page);
        Assert.Contains("Bones left", page);
        Assert.Contains("month board only", page);
        Assert.DoesNotContain("The day's records", page);
        Assert.DoesNotContain("1 per 2,000 damage", page); // formula 2's explanation
        Assert.Equal(2, RunDetail.FromJson("{\"date\":\"2026-10-18\",\"entry\":{\"rank\":1}}").Formula); // an older server
    }

    [Fact]
    public void Your_try_in_words()
    {
        Assert.Equal("Not played yet.", DailyPages.Status(null, false));
        Assert.Contains("leave the castle", DailyPages.Status(new DailyTry { State = "armed" }, true));
        Assert.Contains("name", DailyPages.Status(new DailyTry { State = "ended" }, true));
        Assert.Contains("no connection", DailyPages.Status(new DailyTry { State = "finished", Pending = "{}" }, false));
        Assert.Contains("place 3 of 40", DailyPages.Status(new DailyTry { State = "finished", Points = 1234, Rank = 3, Of = 40 }, false));
        // The live board wins over what the server said when the run was sent (others played since; bonuses; a removal).
        var sent = new DailyTry { Date = "2026-10-17", State = "finished", Points = 31828, Rank = 1, Of = 1 };
        var board = Board.FromJson("{\"date\":\"2026-10-17\",\"count\":5,\"entries\":[],\"you\":{\"rank\":2,\"points\":41828}}");
        Assert.Contains("41,828 points, place 2 of 5", DailyPages.Status(sent, false, board));
        var empty = Board.FromJson("{\"date\":\"2026-10-17\",\"count\":0,\"entries\":[],\"you\":null}");
        Assert.Contains("No longer on the board", DailyPages.Status(sent, false, empty));
        var yesterday = Board.FromJson("{\"date\":\"2026-10-18\",\"count\":0,\"entries\":[],\"you\":null}");
        Assert.Contains("place 1 of 1", DailyPages.Status(sent, false, yesterday)); // another day's board says nothing about it
        Assert.Equal("Dark Mirror 7", DailyPages.Mode("darkmirror", 7));
        Assert.Contains("random player id", PageBlocks.Flatten(DailyPages.TodayPage(null, null, enabled: false, "", false)));
    }

    [Fact]
    public void The_daily_section_opens_on_todays_board()
    {
        var nav = new CodexNavigation();
        nav.SwitchSection(CodexSection.Daily);
        Assert.Equal(CodexCategory.DailyToday, nav.Category);
        Assert.Equal(CodexNavigation.DailyPage, nav.SelectedId);
        nav.SwitchCategory(CodexCategory.DailyMonth);
        Assert.Equal(CodexNavigation.DailyPage, nav.SelectedId);
        Assert.All(CodexNavigation.TabsOf(CodexSection.Daily), c => Assert.Equal(CodexSection.Daily, CodexNavigation.SectionOf(c)));
    }

    [Fact]
    public void Every_board_of_the_day_from_the_server()
    {
        const string json = "{\"date\":\"2026-10-27\",\"formula\":2,\"secondsLeft\":3600,\"boards\":[" +
                            "{\"board\":\"normal\",\"dlc\":false,\"seed\":11,\"mode\":\"normal\",\"level\":0}," +
                            "{\"board\":\"dm\",\"dlc\":false,\"seed\":22,\"mode\":\"darkmirror\",\"level\":4}," +
                            "{\"board\":\"dm10+dlc\",\"dlc\":true,\"seed\":33,\"mode\":\"darkmirror\",\"level\":10}," +
                            "{\"board\":\"nonsense\",\"seed\":44}]}";
        var days = DailyInfo.ListFromJson(json);
        Assert.Equal(new[] { "normal", "dm", "dm10+dlc" }, days.Select(d => d.Board));   // an unknown board is left out
        Assert.True(days.All(d => d.Date == "2026-10-27" && d.SecondsLeft == 3600));
        Assert.Equal((22, 4, false), (days[1].Seed, days[1].Level, days[1].Dlc));
        Assert.True(days[2].Dlc && days[2].DarkMirror);
        Assert.Null(DailyInfo.ListFromJson("{\"date\":\"2026-10-27\",\"seed\":1}"));       // an older server: /api/daily
    }

    [Fact]
    public void A_try_is_kept_per_day_and_board()
    {
        Assert.Equal("2026-10-27", DailyBoards.Key("2026-10-27", "normal"));               // as before the boards
        Assert.Equal("2026-10-27|dm+dlc", DailyBoards.Key("2026-10-27", "dm+dlc"));
        Assert.Equal("2026-10-27", DailyBoards.DateOf("2026-10-27|dm+dlc"));
        Assert.Equal(("dm10", true), (DailyBoards.BaseOf("dm10+dlc"), DailyBoards.HasDlc("dm10+dlc")));
        Assert.False(DailyBoards.IsBoard("dm11"));

        var state = new DailyState { Board = "dm+dlc" };
        state.Ensure("2026-10-27").State = "finished";
        var dark = state.Ensure("2026-10-27", "dm");
        dark.State = "armed";
        dark.Level = 4;
        var back = DailyState.FromJson(state.ToJson());
        Assert.Equal("dm+dlc", back.Board);
        Assert.Equal("finished", back.TryOf("2026-10-27", "normal").State);
        Assert.Equal(("armed", 4, "dm"), (back.TryOf("2026-10-27", "dm").State, back.TryOf("2026-10-27|dm").Level, back.TryOf("2026-10-27|dm").Board));
        Assert.Null(back.TryOf("2026-10-27", "dm10"));
        back.Trim("2026-12-30");                                                           // 60 days later: both go
        Assert.Empty(back.Tries);
    }

    [Fact]
    public void The_server_forgets_a_boards_run_and_it_may_be_played_again()
    {
        var state = new DailyState();
        state.Ensure("2026-10-27", "dm10").State = "finished";
        state.Ensure("2026-10-27").State = "finished";
        Assert.True(state.FollowServer("2026-10-27", "none", "dm10"));
        Assert.Null(state.TryOf("2026-10-27", "dm10"));
        Assert.NotNull(state.TryOf("2026-10-27"));                                         // the other board stays
    }
}
