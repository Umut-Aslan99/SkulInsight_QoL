using System.Linq;
using Characters;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

public class DamageStatsTests
{
    [Fact]
    public void Shares_AreSortedLargestFirstAndAddUpTo100()
    {
        var records = new[]
        {
            Hit.Make(amount: 10, source: DamageSource.Basic),
            Hit.Make(amount: 30, source: DamageSource.Skill),
            Hit.Make(amount: 20, source: DamageSource.Basic),
            Hit.Make(amount: 40, source: DamageSource.Item),
        };

        var shares = DamageStats.Shares(records, r => r.Source);

        Assert.Equal(new[] { DamageSource.Item, DamageSource.Basic, DamageSource.Skill }, shares.Select(s => s.Key));
        Assert.Equal(new[] { 40.0, 30.0, 30.0 }, shares.Select(s => s.Value));
        Assert.Equal(100.0, shares.Sum(s => s.Percent), precision: 6);
        Assert.Equal(40.0, shares[0].Percent, precision: 6);
    }

    [Fact]
    public void Shares_ByDamageType()
    {
        var records = new[]
        {
            Hit.Make(amount: 75, attribute: Damage.Attribute.Magic),
            Hit.Make(amount: 25, attribute: Damage.Attribute.Physical),
        };

        var shares = DamageStats.Shares(records, r => r.Attribute);

        Assert.Equal(Damage.Attribute.Magic, shares[0].Key);
        Assert.Equal(75.0, shares[0].Percent, precision: 6);
    }

    [Fact]
    public void Shares_OfNothingIsEmpty()
    {
        Assert.Empty(DamageStats.Shares(new DamageRecord[0], r => r.Source));
    }
}
