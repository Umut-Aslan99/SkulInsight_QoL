using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageInsight.History;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>
/// The co-op run history (0.17, docs/COOP_PLAN.md §4.11): the co-op run record (party, parts, party events), its file
/// next to the solo runs, merging the parts every game sends, and the pages' data (party, timeline, damage shares).
/// </summary>
public class CoopRunHistoryTests
{
    private const string RunId = "2026-11-02_20-00-00";
    private static readonly string[] Names = { "DocRun", "Mia", "Kai", "Ren" };
    private static readonly string[] Skulls = { "weapon:Werewolf_3", "weapon:Gargoyle", "weapon:Hunter", "weapon:RavenLord" };

    /// <summary>One player's own run, as their game records it (the solo shape).</summary>
    private static RunRecord PartRun(int slot, double dealt, int kills, RunResult result = RunResult.Ending)
    {
        var r = new RunRecord
        {
            Id = RunId, Seed = 42, Started = new DateTime(2026, 11, 2, 20, 0, 0), Ended = new DateTime(2026, 11, 2, 20, 40, 0),
            Result = result, StartPlay = 10, EndPlay = 2410, ModVersion = "0.17.0", EndStageName = "Gate",
        };
        r.Numbers[RunNumbers.Kills] = kills;
        r.Build.Add(new RunGear { Kind = "skull", Id = "weapon:Skul", Name = "Skul", Extra = "|" });
        r.Build.Add(new RunGear { Kind = "skull", Id = Skulls[slot], Name = Names[slot] + "'s skull", Rarity = "Legendary", Extra = "held|Skill" });
        r.Build.Add(new RunGear { Kind = "essence", Id = "essence:Succubus", Name = "Succubus" });
        r.Build.Add(new RunGear { Kind = "item", Id = "item:Ring", Name = "Ring" });
        r.Build.Add(new RunGear { Kind = "item", Id = "item:Sword", Name = "Sword" });
        r.Build.Add(new RunGear { Kind = "insc", Id = "Arms", Name = "Arms", Value = 2, Step = 1 });
        r.Damage.Add(new RunDamage { Group = "Items", Name = "Ring", Amount = dealt, Hits = 10, Biggest = 100 });
        r.DamageTaken["Fanatic"] = (50 * (slot + 1), 2);
        r.Maps.Add(new RunMap { T = 10, Chapter = "Chapter1", Stage = 0, StageName = "Gate", Map = "22", Type = "Normal", Seconds = 60, Tracked = true, Kills = kills });
        r.Events.Add(new RunEvent { T = 11, Kind = RunEvents.Start, Id = "weapon:Skul", Name = "Skul" });
        return r;
    }

    /// <summary>A game of the party: its own run (the file's record) with the co-op data attached.</summary>
    private static (RunRecord own, CoopRunRecord coop) Game(int self, int players)
    {
        var coop = new CoopRunRecord { Id = RunId, Seed = 42, Started = new DateTime(2026, 11, 2, 20, 0, 0), Self = self };
        for (int s = 0; s < players; s++)
            coop.AddPlayer(s, "player" + s, Names[s], s, host: s == 0);
        var own = PartRun(self, 1000 * (self + 1), 10 * (self + 1));
        coop.Attach(own);
        var mine = coop.Part(self);
        mine.RoomOf(0).Dealt = 400 * (self + 1);
        mine.RoomOf(1).Dealt = 600 * (self + 1);
        mine.RoomOf(1).Kills = 5;
        // What every game knows: the rooms and the door vote (the same keys everywhere).
        coop.Add(CoopEvent.Entered(0, 0, "Chapter1", 0, "Gate", "22", "Normal"));
        coop.Add(CoopEvent.DoorResult(0, 60, new[] { 0, 1, -1, -1 }.Take(Math.Max(players, 2)).ToArray(), 1, flip: true, timedOut: false, chance: 0.5,
            doors: new[] { "Item", "Gold" }));
        coop.Add(CoopEvent.Entered(1, 62, "Chapter1", 0, "Gate", "31", "Normal"));
        // What only this game knows: its own reward.
        coop.Add(new CoopEvent { Kind = CoopEvents.Reward, Room = 0, T = 55 + self, Player = self, Id = "item:Ring", Name = "Ring", How = "item" });
        coop.OwnPart(own); // version 1
        return (own, coop);
    }

    [Fact]
    public void An_old_solo_run_file_loads_and_is_written_back_unchanged()
    {
        // A run file as 0.16 wrote it (no "coop" field).
        string old =
            "{\n\"format\":1,\"id\":\"2026-10-16_14-03-22\",\"seed\":7,\"started\":\"2026-10-16T14:03:22\",\"ended\":\"2026-10-16T14:51:00\"," +
            "\"result\":\"Death\",\"darkMirror\":false,\"level\":0,\"easy\":false,\"daily\":\"\",\"favourite\":false,\"shrunk\":false,\"mod\":\"0.16.2\"," +
            "\n\"startPlay\":40,\"endPlay\":2900,\"endChapter\":\"Chapter1\",\"endStage\":0,\"endStageName\":\"Gate\",\"killer\":\"Fanatic\"," +
            "\"killerId\":\"\",\"ending\":\"\",\"screenTitle\":\"\",\"screenSubtitle\":\"\",\"screenStage\":\"\",\"screenPanel\":\"\",\"lastBoss\":\"\"," +
            "\"lastBossId\":\"\",\"endBosses\":\"\",\"picture\":false," +
            "\n\"numbers\":{\"kills\":12}" +
            ",\n\"skullSeconds\":{\"weapon:Skul\":100},\"skullNames\":{\"weapon:Skul\":\"Skul\"}" +
            ",\n\"build\":[\n {\"kind\":\"skull\",\"id\":\"weapon:Skul\",\"name\":\"Skul\",\"rarity\":\"Common\",\"extra\":\"held|\",\"value\":0,\"step\":0}]" +
            ",\n\"damage\":[\n {\"group\":\"Items\",\"name\":\"Ring\",\"amount\":500,\"hits\":5,\"biggest\":200}]" +
            ",\n\"damageTaken\":[\n {\"name\":\"Fanatic\",\"amount\":90,\"hits\":3}]" +
            ",\n\"maps\":[\n {\"t\":40,\"chapter\":\"Chapter1\",\"stage\":0,\"stageName\":\"Gate\",\"map\":\"22\",\"type\":\"Normal\",\"seconds\":61,\"lost\":90,\"hits\":3,\"kills\":12}]" +
            ",\n\"events\":[\n {\"t\":41,\"kind\":\"start\",\"id\":\"weapon:Skul\",\"name\":\"Skul\"}]\n}\n";

        var run = RunRecord.FromJson(old);

        Assert.NotNull(run);
        Assert.Null(run.Coop);
        Assert.False(CoopPages.IsCoop(run));
        Assert.Equal(old, run.ToJson());                 // a solo run's file stays byte for byte the same
        Assert.Equal(old, run.ToJson(withCoop: false));
        Assert.Null(run.Summary().Coop);
        Assert.Empty(CoopPages.ListRow(run));
    }

    [Fact]
    public void A_coop_run_file_round_trip_keeps_the_party_the_parts_and_the_events()
    {
        var (own, coop) = Game(0, 2);
        var (mia, miaCoop) = Game(1, 2);
        coop.MergePart(miaCoop.OwnPart(mia));
        coop.Add(new CoopEvent
        {
            Kind = CoopEvents.Boss, Room = 1, T = 300, Id = "enemy:Yggdrasil", Name = "Yggdrasil", Value = 95,
            Shares = new List<CoopShare> { new() { Player = 0, Dealt = 3000, Taken = 40, Hits = 2 }, new() { Player = 1, Dealt = 1000, Flawless = true } },
        });

        string json = own.ToJson();
        var back = RunRecord.FromJson(json);

        Assert.NotNull(back.Coop);
        Assert.Equal(json, back.ToJson());
        Assert.Equal(0, back.Coop.Self);
        Assert.Same(back, back.Coop.Part(0).Run);         // the local part is the file's record, not a copy
        // Two runs in the file: the own run (the record itself, not repeated in the parts) and Mia's.
        Assert.Equal(2, json.Split(new[] { "\"startPlay\":" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(2000, back.Coop.Part(1).Run.Damage.Single().Amount);
        Assert.Null(back.Coop.Part(1).Run.Coop);
        var door = back.Coop.Events.Single(e => e.Kind == CoopEvents.Door);
        Assert.Equal(new[] { 0, 1 }, door.Votes);
        Assert.True(door.Flip);
        Assert.Equal(1, door.Door);
        Assert.Equal(0.5, door.Chance);
        Assert.Equal(new[] { "Item", "Gold" }, door.Doors);
        var boss = back.Coop.Events.Single(e => e.Kind == CoopEvents.Boss);
        Assert.True(boss.Shares.Single(s => s.Player == 1).Flawless);
        Assert.Equal(new[] { "DocRun", "Mia" }, back.Coop.Players.Select(p => p.Name));
        Assert.True(back.Coop.Player(0).Host);
        // A part for the network carries no co-op data; read back it is the same part.
        var sent = CoopPart.FromJson(coop.Part(0).ToJson());
        Assert.Null(sent.Run.Coop);
        Assert.DoesNotContain("\"coop\"", coop.Part(0).ToJson());
        Assert.Equal(coop.Part(0).ToJson(), sent.ToJson());
        // And a whole record for the network.
        var wire = CoopRunRecord.FromJson(coop.ToJson());
        Assert.Equal(coop.ToJson(), wire.ToJson());
    }

    [Fact]
    public void The_rules_outputs_map_onto_party_events()
    {
        var door = CoopEvent.DoorResult(3, 120, new[] { 1, 1, -1, 0 }, 1, flip: true, timedOut: true, chance: 2 / 3.0);
        Assert.Equal(1, door.VoteOf(0));
        Assert.Equal(-1, door.VoteOf(2));
        Assert.Equal(-1, door.VoteOf(9));
        var down = CoopEvent.Downed(3, 130, 2, staysDown: true, "enemy:Fanatic", "Fanatic");
        var revive = CoopEvent.Revived(3, 140, 2, by: 0, 0.3);
        var back = CoopEvent.Returned(3, 150, 2, 0.1);
        var end = CoopEvent.RunEnd(3, 160, RunResult.Death);

        var c = new CoopRunRecord { Id = RunId };
        foreach (var e in new[] { door, down, revive, back, end })
            c.Add(e);

        Assert.Equal("door-3-1", door.Key);
        Assert.Equal("revive-3-p2-1", revive.Key);
        Assert.Equal(new[] { CoopEvents.Door, CoopEvents.Down, CoopEvents.Revive, CoopEvents.Back, CoopEvents.End }, c.Events.Select(e => e.Kind));
        Assert.Equal(RunResult.Death, c.Result);           // everyone down: the party's run ended as a death
        Assert.Equal(3, c.Rooms);
        var again = CoopRunRecord.FromJson(c.ToJson());
        Assert.Equal(0.3, again.Events.Single(e => e.Kind == CoopEvents.Revive).Health);
        Assert.Equal(0, again.Events.Single(e => e.Kind == CoopEvents.Revive).By);
        Assert.True(again.Events.Single(e => e.Kind == CoopEvents.Down).StaysDown);
        Assert.True(again.Events.Single(e => e.Kind == CoopEvents.Door).TimedOut);
    }

    [Fact]
    public void Merging_is_idempotent_and_the_order_does_not_matter()
    {
        var games = Enumerable.Range(0, 3).Select(s => Game(s, 3)).ToList();
        games[2].coop.Add(CoopEvent.Revived(1, 200, 2, by: 1, 0.3)); // Kai's game knows Mia revived him

        string MergedInOrder(params int[] order)
        {
            var c = new CoopRunRecord();
            foreach (int i in order)
                Assert.True(c.Merge(games[i].coop));
            return c.ToJson();
        }

        string first = MergedInOrder(0, 1, 2);
        Assert.Equal(first, MergedInOrder(2, 1, 0));
        Assert.Equal(first, MergedInOrder(1, 0, 2));
        Assert.Equal(first, MergedInOrder(0, 1, 2, 2, 1, 0, 0)); // the same again changes nothing

        var all = CoopRunRecord.FromJson(first);
        Assert.Equal(3, all.Parts.Count);
        Assert.Equal(3, all.Players.Count);
        Assert.Single(all.Events, e => e.Kind == CoopEvents.Door);       // every game recorded it: once
        Assert.Equal(2, all.Events.Count(e => e.Kind == CoopEvents.Room));
        Assert.Equal(3, all.Events.Count(e => e.Kind == CoopEvents.Reward));
        Assert.Single(all.Events, e => e.Kind == CoopEvents.Revive);

        // Each game ends with the whole run: its own part plus everyone else's.
        foreach (var (own, coop) in games)
            foreach (var other in games.Where(g => g.coop != coop))
                coop.Merge(other.coop);
        foreach (var (own, coop) in games)
        {
            Assert.Equal(3, coop.Parts.Count);
            var neutral = new CoopRunRecord();
            neutral.Merge(coop);
            Assert.Equal(first, neutral.ToJson());
        }
        // Merging a part twice is no change the second time.
        var part = games[1].coop.OwnPart(games[1].own);
        Assert.True(games[0].coop.MergePart(part));
        Assert.False(games[0].coop.MergePart(part));
    }

    [Fact]
    public void The_newest_part_of_each_player_wins_and_the_own_part_is_never_replaced()
    {
        var (own, coop) = Game(0, 2);
        var (mia, miaCoop) = Game(1, 2);
        var v1 = miaCoop.OwnPart(mia);                     // version 2 (Game sent 1)
        mia.Numbers[RunNumbers.Kills] = 99;
        var v2 = miaCoop.OwnPart(mia);                     // version 3

        Assert.True(coop.MergePart(v2));
        Assert.False(coop.MergePart(v1));                   // late, older: ignored
        Assert.Equal(99, coop.Part(1).Run.Number(RunNumbers.Kills));
        Assert.Equal(3, coop.Part(1).Version);

        // Mia's copy of DocRun's part never replaces DocRun's own run.
        var mine = CoopPart.FromJson(coop.Part(0).ToJson());
        mine.Version = 50;
        mine.Run.Numbers[RunNumbers.Kills] = 1;
        Assert.False(coop.MergePart(mine));
        Assert.Same(own, coop.Part(0).Run);
        Assert.Equal(10, own.Number(RunNumbers.Kills));
        // Another run's record is not merged.
        Assert.False(coop.Merge(new CoopRunRecord { Id = "2026-11-01_10-00-00" }));
    }

    [Fact]
    public void A_player_who_left_early_keeps_their_part_up_to_that_room()
    {
        var (own, coop) = Game(0, 3);
        var (kai, kaiCoop) = Game(2, 3);
        kai.Result = RunResult.InProgress;                  // Kai's game never reached an end
        coop.MergePart(kaiCoop.OwnPart(kai));
        coop.Add(new CoopEvent { Kind = CoopEvents.Left, Room = 1, T = 250, Player = 2, How = "quit" });
        coop.Add(CoopEvent.Entered(2, 300, "Chapter1", 1, "Hall", "40", "Normal"));
        coop.Add(CoopEvent.RunEnd(5, 900, RunResult.Ending));

        var kaiRow = CoopPages.Party(coop).Single(r => r.Slot == 2);
        Assert.True(kaiRow.Left);
        Assert.Equal(1, kaiRow.LeftRoom);
        Assert.Equal(RunResult.Abandoned, kaiRow.Result);   // the party won; Kai wasn't there
        Assert.Equal(3000, kaiRow.Dealt);                   // what he did until he left
        Assert.Equal(RunResult.Ending, coop.Result);
        var missing = CoopPages.Party(coop).Single(r => r.Slot == 1);
        Assert.True(missing.NoPart);                        // Mia's part never arrived: her name and slot only
        Assert.Equal("Mia", missing.Name);

        // He came back: in again.
        coop.Add(new CoopEvent { Kind = CoopEvents.Rejoin, Room = 3, T = 500, Player = 2 });
        Assert.False(coop.Player(2).HasLeft);
        Assert.Contains(CoopPages.Timeline(coop).SelectMany(b => b.Lines), l => CoopPages.English(l) == "Kai came back to the run");
    }

    [Fact]
    public void A_part_before_the_roster_does_not_change_the_players_facts()
    {
        var (kai, kaiCoop) = Game(2, 3);
        kaiCoop.Player(2).Joined = 300;                    // a late join
        var part = kaiCoop.OwnPart(kai);

        var partFirst = new CoopRunRecord();
        partFirst.MergePart(part);
        partFirst.Merge(kaiCoop);
        var rosterFirst = new CoopRunRecord();
        rosterFirst.Merge(kaiCoop);
        rosterFirst.MergePart(part);

        Assert.Equal(rosterFirst.ToJson(), partFirst.ToJson());
        Assert.Equal(300, partFirst.Player(2).Joined);
        Assert.Equal(2, partFirst.Player(2).Color);
    }

    [Fact]
    public void Party_events_count_once_and_the_fuller_copy_wins()
    {
        var a = new CoopRunRecord { Id = RunId };
        var b = new CoopRunRecord { Id = RunId };
        // Both games record the same revive: the same key.
        a.Add(CoopEvent.Revived(2, 100, 1, by: 0, 0.3));
        b.Add(CoopEvent.Revived(2, 101, 1, by: 0, 0.3));
        // The host's boss event has everyone's numbers; a client's copy has none.
        a.Add(new CoopEvent { Kind = CoopEvents.Boss, Room = 4, T = 400, Id = "enemy:Yggdrasil", Value = 80 });
        b.Add(new CoopEvent
        {
            Kind = CoopEvents.Boss, Room = 4, T = 400, Id = "enemy:Yggdrasil", Name = "Yggdrasil", Value = 80,
            Shares = new List<CoopShare> { new() { Player = 0, Dealt = 10 }, new() { Player = 1, Dealt = 30 } },
        });

        var ab = new CoopRunRecord();
        ab.Merge(a);
        ab.Merge(b);
        var ba = new CoopRunRecord();
        ba.Merge(b);
        ba.Merge(a);

        Assert.Equal(ab.ToJson(), ba.ToJson());
        Assert.Single(ab.Events, e => e.Kind == CoopEvents.Revive);
        Assert.Equal(2, ab.Events.Single(e => e.Kind == CoopEvents.Boss).Shares.Count);
        // A second revive of the same player in another room is another event.
        ab.Add(CoopEvent.Revived(3, 200, 1, by: 0, 0.3));
        Assert.Equal(2, ab.Events.Count(e => e.Kind == CoopEvents.Revive));
    }

    [Fact]
    public void Coop_runs_are_saved_next_to_solo_runs_and_the_index_keeps_the_party()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skulinsight_coop_runs_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var (own, coop) = Game(0, 2);
            var (mia, miaCoop) = Game(1, 2);
            coop.MergePart(miaCoop.OwnPart(mia));
            var solo = PartRun(0, 10, 1, RunResult.Death);
            solo.Id = "2026-11-01_10-00-00";
            solo.Started = new DateTime(2026, 11, 1, 10, 0, 0);
            File.WriteAllText(Path.Combine(folder, own.Id + ".json"), own.ToJson());
            File.WriteAllText(Path.Combine(folder, solo.Id + ".json"), solo.ToJson());

            var index = RunIndex.Load(folder, out _);

            Assert.Equal(new[] { RunId, solo.Id }, index.Select(r => r.Id));
            var row = index[0];
            Assert.True(CoopPages.IsCoop(row));
            Assert.False(CoopPages.IsCoop(index[1]));
            Assert.Empty(row.Coop.Parts);                    // the index keeps the party only
            Assert.Empty(row.Coop.Events);
            var entries = CoopPages.ListRow(row);
            Assert.Equal(new[] { "DocRun", "Mia" }, entries.Select(e => e.Name));
            Assert.Equal(new[] { "weapon:Werewolf_3", "weapon:Skul" }, entries[0].Skulls); // held first
            Assert.Equal("weapon:Gargoyle", entries[1].Skulls[0]);
            // The index read back again (no rebuild) still has it.
            var again = RunIndex.Load(folder, out bool changed);
            Assert.False(changed);
            Assert.Equal(entries.Select(e => e.Name), CoopPages.ListRow(again[0]).Select(e => e.Name));

            // The file has everything; a shrunk co-op run keeps everyone's numbers and builds, no timelines.
            var full = RunRecord.FromJson(File.ReadAllText(Path.Combine(folder, own.Id + ".json")));
            Assert.Equal(2, full.Coop.Parts.Count);
            var small = full.Shrink();
            Assert.True(small.Coop.Shrunk);
            Assert.Empty(small.Coop.Events);
            Assert.Empty(small.Coop.Part(1).Run.Events);
            Assert.Equal(2, small.Coop.Part(1).Run.Build.Count(g => g.Kind == "skull"));
            Assert.Same(small, small.Coop.Part(0).Run);
            Assert.Equal(20, CoopPages.Party(small.Coop).Single(r => r.Slot == 1).Kills);
            var reread = RunRecord.FromJson(small.ToJson());
            Assert.Equal(small.ToJson(), reread.ToJson());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Two_players_the_overview_and_the_party_timeline()
    {
        var (own, coop) = Game(0, 2);
        var (mia, miaCoop) = Game(1, 2);
        miaCoop.Add(CoopEvent.Downed(1, 150, 1, staysDown: false, "enemy:Fanatic", "Fanatic"));
        miaCoop.Add(CoopEvent.Revived(1, 160, 1, by: 0, 0.3));
        miaCoop.Add(new CoopEvent { Kind = CoopEvents.Shop, Room = 1, T = 170, Player = 1, Id = "item:Cup", Name = "Cup", Value = 120, How = "shop" });
        miaCoop.Add(new CoopEvent { Kind = CoopEvents.Upgrade, Room = 1, T = 180, Player = 1, Id = "weapon:Gargoyle_2", Name = "Gargoyle+", Other = "weapon:Gargoyle", OtherName = "Gargoyle", Value = 30 });
        coop.Merge(miaCoop);

        var party = CoopPages.Party(coop);
        Assert.Equal(new[] { "DocRun", "Mia" }, party.Select(r => r.Name));
        var doc = party[0];
        Assert.Equal(1000, doc.Dealt);                     // room numbers 400 + 600
        Assert.Equal(10, doc.Kills);
        Assert.Equal(2, doc.Items);
        Assert.Equal("weapon:Werewolf_3", doc.Skulls[0].Id);
        Assert.Equal("essence:Succubus", doc.Essences.Single().Id);
        Assert.Equal("Arms", doc.Inscriptions.Single().Id);
        Assert.Equal(50, doc.Taken);
        Assert.Equal(1, doc.Revives);
        Assert.Equal(1, party[1].Downs);
        Assert.Equal(1, party[1].Revived);
        Assert.Equal(100, party.Sum(r => r.Percent));
        Assert.Equal(33, doc.Percent);                     // 1000 of 3000
        Assert.Equal(1.0, party.Sum(r => r.Share), 6);

        var timeline = CoopPages.Timeline(coop);
        Assert.Equal(new[] { 0, 1 }, timeline.Select(b => b.Room));
        Assert.Equal("31", timeline[1].Map);
        var room0 = timeline[0].Lines.Select(CoopPages.English).ToList();
        Assert.Contains("DocRun voted for door Item", room0);
        Assert.Contains("Mia voted for door Gold", room0);
        Assert.Contains("Coin flip: door Gold won (50% chance)", room0);
        Assert.Contains("DocRun took Ring", room0);
        var room1 = timeline[1].Lines.Select(CoopPages.English).ToList();
        Assert.Equal(new[]
        {
            "Mia went down (Fanatic)", "DocRun revived Mia", "Mia bought Cup for 120 gold", "Mia upgraded Gargoyle into Gargoyle+",
        }, room1);
        var revive = timeline[1].Lines[1];
        Assert.Equal(new[] { 0, 1 }, revive.Slots);         // reviver, revived: their colours
        Assert.Equal(new[] { 400 * 1, 400 * 2 }, timeline[0].Shares.Select(s => (int)s.Dealt));
        Assert.Equal(new[] { 33, 67 }, timeline[0].Shares.Select(s => s.Percent));
        Assert.Equal(100, timeline[1].Shares.Sum(s => s.Percent));
    }

    [Fact]
    public void Four_players_boss_shares_and_flawless_per_player()
    {
        var games = Enumerable.Range(0, 4).Select(s => Game(s, 4)).ToList();
        var host = games[0].coop;
        foreach (var (_, coop) in games.Skip(1))
            host.Merge(coop);
        host.Add(new CoopEvent
        {
            Kind = CoopEvents.Boss, Room = 1, T = 600, Id = "enemy:Yggdrasil", Name = "Yggdrasil", Value = 125,
            Shares = new List<CoopShare>
            {
                new() { Player = 0, Dealt = 1000, Taken = 0, Flawless = true }, new() { Player = 1, Dealt = 1000, Taken = 30 },
                new() { Player = 2, Dealt = 1000, Taken = 0, Flawless = true }, new() { Player = 3, Dealt = 0, Taken = 80 },
            },
        });
        // A boss event without numbers falls back to the players' numbers for its room.
        host.Add(new CoopEvent { Kind = CoopEvents.Boss, Room = 0, T = 50, Id = "enemy:Mini", Name = "Mini boss", Value = 30 });

        var bosses = CoopPages.Bosses(host);
        var ygg = bosses.Single(b => b.Id == "enemy:Yggdrasil");
        Assert.Equal(4, ygg.Shares.Count);
        Assert.Equal(100, ygg.Shares.Sum(s => s.Percent));   // 33.3 x 3 + 0: largest remainders make 100
        Assert.Equal(new[] { 34, 33, 33, 0 }, ygg.Shares.Select(s => s.Percent));
        Assert.Equal(new[] { true, false, true, false }, ygg.Shares.Select(s => s.Flawless));
        var mini = bosses.Single(b => b.Id == "enemy:Mini");
        Assert.Equal(new[] { 10, 20, 30, 40 }, mini.Shares.Select(s => s.Percent)); // rooms 0: 400, 800, 1200, 1600
        Assert.Contains(CoopPages.Timeline(host).SelectMany(b => b.Lines), l => CoopPages.English(l) == "Yggdrasil defeated in 2:05");

        var party = CoopPages.Party(host);
        Assert.Equal(4, party.Count);
        Assert.Equal(new[] { 10, 20, 30, 40 }, party.Select(r => r.Percent));
        Assert.Equal(new[] { "DocRun", "Mia", "Kai", "Ren" }, CoopPages.ListRow(WithCoop(games[0].own)).Select(e => e.Name));
        Assert.Equal(Skulls, CoopPages.ListRow(games[0].own.Summary()).Select(e => e.Skulls[0]));
    }

    [Fact]
    public void Shares_without_damage_are_zero_and_never_divide_by_zero()
    {
        var shares = CoopPages.Shares(new[] { (0, 0.0, 0.0, false), (1, 0.0, 5.0, false) });
        Assert.All(shares, s => Assert.Equal(0, s.Percent));
        Assert.All(shares, s => Assert.Equal(0, s.Fraction));
        var thirds = CoopPages.Shares(new[] { (0, 1.0, 0.0, false), (1, 1.0, 0.0, false), (2, 1.0, 0.0, false) });
        Assert.Equal(100, thirds.Sum(s => s.Percent));
    }

    private static RunRecord WithCoop(RunRecord run) => RunRecord.FromJson(run.ToJson());
}
