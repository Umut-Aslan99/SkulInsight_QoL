using DamageInsight.Describe;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>
/// A real scenario from testing: 161% physical attack with Arms 1/6 and Courage 1/4, Hope Slasher on the ground
/// (+60% physical attack, Arms + Courage). After picking it up the game showed 319.2%.
/// </summary>
public class PickupPreviewTests
{
    private static readonly int[] ArmsSteps = { 0, 2, 4, 6 };
    private static readonly int[] CourageSteps = { 0, 2, 4 };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(6, 3)]
    [InlineData(9, 3)]
    public void StepFor_FollowsTheThresholds(int count, int step) => Assert.Equal(step, PickupMath.StepFor(ArmsSteps, count));

    [Fact]
    public void HopeSlasher_ActivatesArms2AndCourage2_AndGives319Percent()
    {
        var added = PickupMath.AddedCounts("Arms", "Brave");
        Assert.Equal(1, PickupMath.StepFor(ArmsSteps, 1 + added["Arms"]));      // Arms 2/6 → step index 1
        Assert.Equal(1, PickupMath.StepFor(CourageSteps, 1 + added["Brave"]));  // Courage 2/4 → step index 1

        var physical = new StatModel { PercentPoint = 0.61 };                   // today: 161%
        Assert.Equal(1.61, physical.Final, precision: 6);
        physical.Apply("PercentPoint", 0.6);                                     // Hope Slasher's own stat
        physical.Apply("PercentPoint", PickupMath.StatValue("PercentPoint", 45)); // Arms step 2: +45%
        physical.Apply("Percent", PickupMath.StatValue("Percent", 20));           // Courage step 2: amplified by 20%
        Assert.Equal(3.192, physical.Final, precision: 6);                       // what the game showed after pickup
    }

    [Fact]
    public void AddedCounts_CountsDoubleInscriptions()
    {
        var added = PickupMath.AddedCounts("Arms", "Arms", "None");
        Assert.Equal(2, added["Arms"]);
        Assert.False(added.ContainsKey("None"));
    }
}

public class SwapPreviewTests
{
    [Fact]
    public void CountDeltas_AddTheNewItemAndRemoveTheReplacedOne()
    {
        // Warrior's Gloves (Masterpiece, Courage) replacing Rejuvenating Root (Revenge, Mutant).
        var deltas = PickupMath.CountDeltas(new[] { "Masterpiece", "Brave" }, new[] { "Revenge", "Mutation" });
        Assert.Equal(1, deltas["Masterpiece"]);
        Assert.Equal(1, deltas["Brave"]);
        Assert.Equal(-1, deltas["Revenge"]);
        Assert.Equal(-1, deltas["Mutation"]);

        // Same inscription on both items: no change.
        Assert.Equal(0, PickupMath.CountDeltas(new[] { "Arms", "Brave" }, new[] { "Arms", "Duel" })["Arms"]);
    }
}
