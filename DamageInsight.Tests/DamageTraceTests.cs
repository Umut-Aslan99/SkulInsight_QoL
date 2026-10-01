using System.Collections.Generic;
using System.Linq;
using Characters;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

public class DamageTraceTests
{
    private static DamageState State(double @base, double mult = 1, double percent = 1, double crit = 1.5) =>
        new() { Base = @base, Multiplier = mult, PercentMultiplier = percent, CritMultiplier = crit };

    /// <summary>A Warlord-like crit: base x stats, crit x1.7, Strike's +270% crit damage, a +100% item, the target's debuff.</summary>
    private static DamageTrace Example()
    {
        var trace = new DamageTrace { Origin = "Warlord", Start = State(100, mult: 1.6, crit: 1.7), Critical = true };
        trace.StatParts.Add(("Phys. atk", 1.6));
        trace.Add("Strike", "give", State(100, mult: 1.6, crit: 4.4));
        trace.Add("Unchanged handler", "give", State(100, mult: 1.6, crit: 4.4)); // no change → no row
        trace.Add("Soul reap", "give", State(100, mult: 1.6, percent: 2, crit: 4.4));
        trace.Add("Wolf's hunt", "take", State(100, mult: 1.6 * 1.3, percent: 2, crit: 4.4));
        trace.Dealt = System.Math.Ceiling(100 * 1.6 * 1.3 * 2 * 4.4);
        return trace;
    }

    [Fact]
    public void Explain_FactorsMultiplyUpToTheDealtDamage()
    {
        var rows = Example().Explain();
        Assert.Equal(new[] { "Base damage", "Phys. atk 160%", "Critical hit", "Strike", "Soul reap", "Wolf's hunt (on target)", "Rounded up" },
            rows.Select(r => r.Label));
        Assert.Equal(new[] { "", "x1.6", "x1.7", "x2.59", "x2", "x1.3", "" }, rows.Select(r => r.Effect));
        Assert.Equal(100 * 1.6 * 1.3 * 2 * 4.4, rows[rows.Count - 2].Total, precision: 6);
    }

    [Fact]
    public void Explain_NotesWhenLessWasDealtThanComputed()
    {
        var trace = new DamageTrace { Start = State(100) };
        trace.Dealt = 40; // e.g. the enemy only had 40 health left
        var last = trace.Explain().Last();
        Assert.Equal("Dealt: only 40 HP were left", last.Label);
        Assert.Equal("overkill", last.Effect);
        Assert.Equal(40, last.Total);
    }

    [Fact]
    public void LogJson_ContainsTheCalculation()
    {
        var record = new DamageRecord(1.5f, 2, "You", EntityKind.Player, "Scarecraw", EntityKind.TrashMob,
            DamageSource.Item, Damage.Attribute.Magic, 329, false, new DamageTrace { Origin = "Oberon \"attack\"", Start = State(100, mult: 3.29) });
        string json = LogJson.Line(record);
        Assert.StartsWith("{\"t\":1.5,\"room\":2,\"attacker\":\"You\",\"target\":\"Scarecraw\",\"source\":\"Item\",\"attribute\":\"Magic\",\"amount\":329,\"crit\":false", json);
        Assert.Contains("\"origin\":\"Oberon \\\"attack\\\"\"", json);
        Assert.Contains("[\"Base damage\",\"\",100],[\"Attack stats\",\"x3.29\",329]", json);
    }

    [Fact]
    public void LogText_MapsEveryLineToItsHit()
    {
        var shown = new[] { Hit.Make(room: 1), Hit.Make(room: 1), Hit.Make(room: 2) };
        var map = new List<int>();
        string text = LogText.Build(shown, new RoomInfo[0], 100, map);
        Assert.Equal(new[] { -1, 0, 1, -1, 2 }, map); // room header, hit, hit, room header, hit
        Assert.Equal(map.Count, text.TrimEnd('\n').Split('\n').Length);
    }
}

public class DamageTraceRoundingTests
{
    [Fact]
    public void Explain_KeepsDecimalsAndRoundsUpOnceAtTheEnd()
    {
        // Shadow Spirit Death: 7.5 base x 180% magic atk = 13.5 → the game deals 14.
        var trace = new DamageTrace { Start = new DamageState { Base = 7.5, Multiplier = 1.8, PercentMultiplier = 1, CritMultiplier = 1.5 }, Dealt = 14 };
        var rows = trace.Explain();
        Assert.Equal(13.5, rows[1].Total, precision: 6);
        Assert.Equal("Rounded up", rows.Last().Label);
        Assert.Equal(14, rows.Last().Total);
    }

    [Fact]
    public void LogLine_NamesTheItemInsteadOfYou()
    {
        var record = new DamageRecord(1f, 1, "You", EntityKind.Player, "Scarecraw", EntityKind.TrashMob,
            DamageSource.Item, Damage.Attribute.Magic, 14, false, new DamageTrace { Origin = "Shadow Spirit Death" });
        string line = System.Text.RegularExpressions.Regex.Replace(LogText.Line(record), "<[^>]+>", "");
        Assert.Contains("Shadow Spirit Death dealt 14 Item Magic damage to Scarecraw", line);
    }
}
