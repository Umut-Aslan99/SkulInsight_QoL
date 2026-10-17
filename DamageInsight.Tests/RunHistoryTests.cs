using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.History;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Run history (0.13): the run file, the build comparison (timeline events), continuing a run, the damage rows.</summary>
public class RunHistoryTests
{
    private static BuildSnapshot.Piece P(string id, string name = null, string next = "", int level = 0) =>
        new() { Id = id, Name = name ?? id, Next = next, Level = level };

    private static BuildSnapshot Build(string held, string[] skulls, string[] items = null, string[] essences = null)
    {
        var s = new BuildSnapshot { Held = held };
        s.Skulls.AddRange(skulls.Select(id => P(id)));
        s.Items.AddRange((items ?? Array.Empty<string>()).Select(id => P(id)));
        s.Essences.AddRange((essences ?? Array.Empty<string>()).Select(id => P(id)));
        return s;
    }

    private static List<RunEvent> Diff(BuildSnapshot a, BuildSnapshot b, ICollection<(string, string)> changed = null, Dictionary<string, int> steps = null) =>
        BuildDiff.Compare(a, b, 100, changed ?? new List<(string, string)>(), steps ?? new Dictionary<string, int>());

    [Fact]
    public void Run_file_round_trip()
    {
        var run = new RunRecord
        {
            Id = "2026-10-16_14-03-22",
            Seed = -12345,
            Started = new DateTime(2026, 10, 16, 14, 3, 22),
            Ended = new DateTime(2026, 10, 16, 14, 51, 0),
            Result = RunResult.Death,
            DarkMirror = true,
            Level = 7,
            Easy = true,
            Favourite = true,
            ModVersion = "0.13.0",
            StartPlay = 40,
            EndPlay = 2900,
            EndChapter = "HardmodeChapter3",
            EndStage = 1,
            EndStageName = "Fanatic \"Grounds\"",
            Killer = "Veteran Hero",
            KillerId = "enemy:2001",
            Picture = true,
        };
        run.Numbers[RunNumbers.Kills] = 412;
        run.Numbers[RunNumbers.TotalDamage] = 1234567.5;
        run.SkullSeconds["weapon:Skul"] = 120;
        run.SkullSeconds["weapon:Werewolf_3"] = 2000;
        run.SkullNames["weapon:Werewolf_3"] = "Werewolf";
        run.Build.Add(new RunGear { Kind = "skull", Id = "weapon:Werewolf_3", Name = "Werewolf", Rarity = "Legendary", Extra = "held|Predation · Rush" });
        run.Build.Add(new RunGear { Kind = "insc", Id = "Arms", Name = "Arms", Value = 4, Step = 2 });
        run.Damage.Add(new RunDamage { Group = "Items", Name = "Ring", Amount = 3000, Hits = 12, Biggest = 400 });
        run.DamageTaken["Fanatic"] = (250, 5);
        run.Maps.Add(new RunMap { T = 40, Chapter = "HardmodeChapter1", Stage = 0, StageName = "Gate", Map = "22", Type = "Normal", Seconds = 61 });
        run.Events.Add(new RunEvent { T = 41, Kind = RunEvents.Start, Id = "weapon:Skul", Name = "Skul" });
        run.Events.Add(new RunEvent { T = 300, Kind = RunEvents.SkullTaken, Id = "weapon:Werewolf", Name = "Werewolf", Other = "weapon:Skul", OtherName = "Skul" });
        run.Events.Add(new RunEvent { T = 900, Kind = RunEvents.Inscription, Id = "Arms", Name = "Arms", Value = 2 });

        var back = RunRecord.FromJson(run.ToJson());

        Assert.NotNull(back);
        Assert.Equal(run.ToJson(), back.ToJson());
        Assert.Equal(RunResult.Death, back.Result);
        Assert.Equal(-12345, back.Seed);
        Assert.True(back.Easy);
        Assert.True(back.Favourite);
        Assert.False(back.Shrunk);
        Assert.Equal("Fanatic \"Grounds\"", back.EndStageName);
        Assert.Equal(2860, back.PlaySeconds);
        Assert.Equal(1234567.5, back.Number(RunNumbers.TotalDamage));
        Assert.Equal((250d, 5), back.DamageTaken["Fanatic"]);
        Assert.Equal("weapon:Skul", back.Events[1].Other);
        Assert.Equal(2, back.Events[2].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"format\":1}")]
    public void A_broken_file_is_no_run(string json) => Assert.Null(RunRecord.FromJson(json));

    [Fact]
    public void Taking_a_skull_drops_the_other()
    {
        var events = Diff(Build("a", new[] { "a", "b" }), Build("c", new[] { "a", "c" }));
        var e = Assert.Single(events);
        Assert.Equal(RunEvents.SkullTaken, e.Kind);
        Assert.Equal("c", e.Id);
        Assert.Equal("b", e.Other);
    }

    [Fact]
    public void A_second_skull_into_an_empty_slot_drops_nothing()
    {
        var e = Assert.Single(Diff(Build("a", new[] { "a" }), Build("a", new[] { "a", "b" })));
        Assert.Equal(RunEvents.SkullTaken, e.Kind);
        Assert.Equal("", e.Other);
    }

    [Fact]
    public void Awakening_is_its_own_event()
    {
        var before = new BuildSnapshot { Held = "w1" };
        before.Skulls.Add(P("w1", "Werewolf", next: "w2"));
        before.Skulls.Add(P("x"));
        var after = new BuildSnapshot { Held = "w2" };
        after.Skulls.Add(P("w2", "Werewolf+"));
        after.Skulls.Add(P("x"));

        var e = Assert.Single(Diff(before, after));
        Assert.Equal(RunEvents.SkullAwakened, e.Kind);
        Assert.Equal("w2", e.Id);
        Assert.Equal("w1", e.Other);
    }

    [Fact]
    public void Swapping_skulls_is_no_event() =>
        Assert.Empty(Diff(Build("a", new[] { "a", "b" }), Build("b", new[] { "b", "a" })));

    [Fact]
    public void Items_taken_lost_and_turned_into_another()
    {
        var before = Build("a", new[] { "a" }, new[] { "ring", "sword", "cup" });
        var after = Build("a", new[] { "a" }, new[] { "ring", "sword2", "boots" });
        var events = Diff(before, after, new List<(string, string)> { ("sword", "sword2") });

        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e.Kind == RunEvents.ItemChanged && e.Id == "sword2" && e.Other == "sword");
        Assert.Contains(events, e => e.Kind == RunEvents.ItemLost && e.Id == "cup");
        Assert.Contains(events, e => e.Kind == RunEvents.ItemTaken && e.Id == "boots");
    }

    [Fact]
    public void Two_of_the_same_item_count_as_two()
    {
        var events = Diff(Build("a", new[] { "a" }, new[] { "ring" }), Build("a", new[] { "a" }, new[] { "ring", "ring" }));
        var e = Assert.Single(events);
        Assert.Equal(RunEvents.ItemTaken, e.Kind);
    }

    [Fact]
    public void Dark_abilities_bought_and_levelled()
    {
        var before = new BuildSnapshot();
        before.Darks.Add(P("dark:A", level: 1));
        var after = new BuildSnapshot();
        after.Darks.Add(P("dark:A", level: 2));
        after.Darks.Add(P("dark:B", level: 1));

        var events = Diff(before, after);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Id == "dark:A" && e.Value == 2);
        Assert.Contains(events, e => e.Id == "dark:B" && e.Value == 1);
    }

    [Fact]
    public void An_inscription_step_counts_once_per_run()
    {
        var steps = new Dictionary<string, int>();
        var one = new BuildSnapshot();
        one.Inscriptions["Arms"] = ("Arms", 2, 1);
        var none = new BuildSnapshot();
        none.Inscriptions["Arms"] = ("Arms", 1, 0);

        var first = Diff(new BuildSnapshot(), one, steps: steps);
        var down = Diff(one, none, steps: steps);
        var again = Diff(none, one, steps: steps);

        var e = Assert.Single(first);
        Assert.Equal(RunEvents.Inscription, e.Kind);
        Assert.Equal(1, e.Value);
        Assert.Empty(down);
        Assert.Empty(again); // step 1 was reached before in this run
    }

    [Fact]
    public void An_inscription_taking_its_tuned_form_is_an_event()
    {
        var before = new BuildSnapshot();
        before.Inscriptions["Poisoning"] = ("Poisoning", 4, 3);
        var after = new BuildSnapshot();
        after.Inscriptions["Poisoning"] = ("Poisoning", 4, 3);
        after.Tuned.Add("Poisoning");

        var e = Assert.Single(Diff(before, after, steps: new Dictionary<string, int> { ["Poisoning"] = 3 }));
        Assert.Equal(RunEvents.InscriptionTuned, e.Kind);
        Assert.Equal("Poisoning", e.Name);
        Assert.Empty(Diff(after, after, steps: new Dictionary<string, int> { ["Poisoning"] = 3 }));
        // Kept in the build and read back for a continued run.
        var back = RunJournal.FromBuild(new[] { new RunGear { Kind = "insc", Id = "Poisoning", Name = "Poisoning", Value = 4, Step = 3, Extra = "tuned" } });
        Assert.Contains("Poisoning", back.Tuned);
    }

    [Fact]
    public void A_new_fragment_is_an_event()
    {
        var after = new BuildSnapshot { Fragment = P("fragment:Bone") };
        var e = Assert.Single(Diff(new BuildSnapshot(), after));
        Assert.Equal(RunEvents.Fragment, e.Kind);
        Assert.Empty(Diff(after, after));
    }

    [Fact]
    public void The_same_seed_continues_other_open_runs_are_abandoned()
    {
        var same = new RunRecord { Id = "b", Seed = 7, Started = new DateTime(2026, 10, 16, 12, 0, 0) };
        var other = new RunRecord { Id = "a", Seed = 3, Started = new DateTime(2026, 10, 15, 12, 0, 0) };
        var done = new RunRecord { Id = "c", Seed = 7, Result = RunResult.Death };

        var (resume, abandon) = RunResume.Decide(new[] { other, same, done }, 7);

        Assert.Same(same, resume);
        Assert.Equal(new[] { "a" }, abandon.Select(r => r.Id));
        var (none, all) = RunResume.Decide(new[] { other }, 7);
        Assert.Null(none);
        Assert.Single(all);
    }

    [Fact]
    public void Damage_rows_in_table_order_and_back()
    {
        var counter = new RunDamageCounter();
        counter.Add(GearGroup.Items, "Ring", 10);
        counter.Add(GearGroup.Skulls, "Skul: Basic attack", 5);
        counter.Add(GearGroup.Items, "Sword", 50);
        counter.Add(GearGroup.Items, "Ring", 30);

        var rows = counter.Rows();
        Assert.Equal(new[] { "Skul: Basic attack", "Sword", "Ring" }, rows.Select(r => r.Name));
        var ring = rows.Single(r => r.Name == "Ring");
        Assert.Equal(40, ring.Amount);
        Assert.Equal(2, ring.Hits);
        Assert.Equal(30, ring.Biggest);

        var again = new RunDamageCounter();
        again.Load(rows);
        again.Add(GearGroup.Items, "Ring", 1);
        Assert.Equal(41, again.Rows().Single(r => r.Name == "Ring").Amount);
    }

    [Fact]
    public void A_continued_run_starts_from_its_saved_build()
    {
        var build = new List<RunGear>
        {
            new() { Kind = "skull", Id = "a", Name = "A", Extra = "held|Skill 1 · Skill 2" },
            new() { Kind = "skull", Id = "b", Name = "B", Extra = "|" },
            new() { Kind = "item", Id = "ring", Name = "Ring" },
            new() { Kind = "dark", Id = "dark:A", Value = 2 },
            new() { Kind = "insc", Id = "Arms", Name = "Arms", Value = 4, Step = 2 },
        };
        var s = RunJournal.FromBuild(build);

        Assert.Equal("a", s.Held);
        Assert.Equal(new[] { "a", "b" }, s.Skulls.Select(p => p.Id));
        Assert.Equal("Skill 1 · Skill 2", s.Skulls[0].Extra);
        Assert.Equal(2, s.Darks.Single().Level);
        Assert.Equal(("Arms", 4, 2), s.Inscriptions["Arms"]);
        // Nothing changed while the game was closed: no events (the steps reached are known from the build).
        Assert.Empty(Diff(s, RunJournal.FromBuild(build), steps: new Dictionary<string, int> { ["Arms"] = 2 }));
    }
}
