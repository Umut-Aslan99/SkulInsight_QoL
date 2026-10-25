using System;
using DamageInsight.History;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Heals missed (0.14): only orbs that would have healed count, and the run history adds them up.</summary>
public class HealOrbTests
{
    [Theory]
    [InlineData(100, 100, 3, 0, 0)]       // full health: nothing missed
    [InlineData(95, 100, 3, 1, 5)]        // 5 short: one orb would have filled it
    [InlineData(70, 100, 3, 3, 30)]       // 30 short: all three
    [InlineData(50, 100, 3, 3, 30)]       // 50 short: all three, their 30
    [InlineData(0, 120, 2, 2, 20)]        // a death on the map: the whole bar was missing
    public void Missed_CountsOnlyTheOrbsThatWouldHaveHealed(double hp, double max, int orbs, int missed, double health)
    {
        var heals = new double[orbs];
        for (int i = 0; i < orbs; i++)
            heals[i] = 10;
        Assert.Equal((missed, health), HealOrbMath.Missed(heals, hp, max));
    }

    [Fact]
    public void Missed_TakesTheBiggestOrbsFirst()
    {
        Assert.Equal((1, 20), HealOrbMath.Missed(new double[] { 10, 30, 10 }, 80, 100));
        Assert.Equal((0, 0), HealOrbMath.Missed(Array.Empty<double>(), 10, 100));
    }

    [Fact]
    public void Records_AddUpTheRunsThatCountedThem()
    {
        RunRecord Run(string id, double missed, double health, bool counted = true)
        {
            var r = new RunRecord { Id = id, Started = new DateTime(2026, 10, 25), Result = RunResult.Death };
            if (counted)
            {
                r.Numbers[RunNumbers.HealsMissed] = missed;
                r.Numbers[RunNumbers.HealthMissed] = health;
            }
            return r;
        }
        var records = RunRecords.Of(new[] { Run("a", 3, 40), Run("b", 1, 10), Run("old", 0, 0, counted: false) });
        Assert.Equal(2, records.RunsWithHeals);
        Assert.Equal(4, records.HealsMissed);
        Assert.Equal(50, records.HealthMissed);
    }
}
