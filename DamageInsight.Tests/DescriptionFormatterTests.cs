using DamageInsight.Describe;
using DamageInsight.Recording;
using Xunit;
using GearHit = DamageInsight.Describe.Hit;

namespace DamageInsight.Tests;

public class DescriptionFormatterTests
{
    private static GearHit Explosion => new() { BaseMin = 8, BaseMax = 12, Attribute = "Physical", MotionType = "Item", AttackType = "Melee" };

    [Fact]
    public void FinalRange_AppliesTheGamesStatFormula()
    {
        // 8–12 × 160% physical attack, rounded up like Damage.amount.
        var stats = new StatSnapshot { Physical = 1.6 };
        Assert.Equal((13.0, 20.0), DescriptionFormatter.FinalRange(Explosion, stats));
    }

    [Fact]
    public void FinalRange_AddsCategoryBonusesBeforeMultiplying()
    {
        // Skill projectile: 1 + (1.9−1) magic + (1.2−1) projectile + (1.1−1) skill = 2.2, then × 1.5 attack damage.
        var hit = new GearHit { BaseMin = 10, BaseMax = 10, Attribute = "Magic", MotionType = "Skill", AttackType = "Projectile" };
        var stats = new StatSnapshot { Magic = 1.9, Projectile = 1.2, Skill = 1.1, AttackDamage = 1.5 };
        Assert.Equal(3.3, stats.FactorFor(hit), precision: 6);
        Assert.Equal((33.0, 33.0), DescriptionFormatter.FinalRange(hit, stats));
    }

    [Fact]
    public void FixedDamage_IgnoresStats()
    {
        var hit = new GearHit { BaseMin = 45, BaseMax = 45, Attribute = "Fixed", MotionType = "Item" };
        Assert.Equal((45.0, 45.0), DescriptionFormatter.FinalRange(hit, new StatSnapshot { Physical = 3, AttackDamage = 2 }));
    }

    [Fact]
    public void HitLine_ReadsLikeATooltip()
    {
        string line = LogText.Plain(DescriptionFormatter.HitLine(Explosion, new StatSnapshot { Physical = 1.6 }));
        Assert.Equal("13–20 Physical (8–12 x 160% phys. atk) · can't crit", line);
    }

    [Fact]
    public void HitLine_BorrowedSkullDamage()
    {
        var hit = new GearHit { BaseSource = "skull", MultMin = 1.5, MultMax = 1.5, Attribute = "Magic", MotionType = "Item" };
        Assert.Equal("15–23 Magic (10–15 skull dmg x 150%) · can't crit",
            LogText.Plain(DescriptionFormatter.HitLine(hit, new StatSnapshot { SkullMin = 10, SkullMax = 15 })));
        Assert.Equal("150% of skull damage, Magic · can't crit",
            LogText.Plain(DescriptionFormatter.HitLine(hit, StatSnapshot.Neutral)));
    }
}
