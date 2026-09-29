using System;
using System.Linq;
using System.Text.RegularExpressions;
using Characters;
using UnityEngine;
using Xunit;

namespace DamageInsight.Tests;

public class DamageSourcesTests
{
    private static readonly DamageSource[] AllSources = (DamageSource[])Enum.GetValues(typeof(DamageSource));

    private static Damage MakeDamage(Damage.MotionType motion) =>
        new Damage(default(Attacker), 10, new Vector2(0f, 0f), Damage.Attribute.Physical, Damage.AttackType.Melee, motion);

    [Theory]
    [InlineData(Damage.MotionType.Basic, DamageSource.Basic)]
    [InlineData(Damage.MotionType.Skill, DamageSource.Skill)]
    [InlineData(Damage.MotionType.Item, DamageSource.Item)]
    [InlineData(Damage.MotionType.Quintessence, DamageSource.Quintessence)]
    [InlineData(Damage.MotionType.Dash, DamageSource.Dash)]
    [InlineData(Damage.MotionType.Swap, DamageSource.Swap)]
    [InlineData(Damage.MotionType.DarkAbility, DamageSource.DarkAbility)]
    [InlineData(Damage.MotionType.Status, DamageSource.Status)]
    [InlineData(Damage.MotionType.None, DamageSource.Other)]
    public void Classify_MapsTheGamesMotionType(Damage.MotionType motion, DamageSource expected)
    {
        DamageSources.CurrentStatus = null;
        Assert.Equal(expected, DamageSources.Classify(MakeDamage(motion)));
    }

    [Fact]
    public void Classify_UsesTheStatusThatIsDealingDamage()
    {
        try
        {
            DamageSources.CurrentStatus = DamageSource.Poison;
            Assert.Equal(DamageSource.Poison, DamageSources.Classify(MakeDamage(Damage.MotionType.Status)));
            // Only status damage is affected.
            Assert.Equal(DamageSource.Basic, DamageSources.Classify(MakeDamage(Damage.MotionType.Basic)));
        }
        finally
        {
            DamageSources.CurrentStatus = null;
        }
    }

    [Fact]
    public void EverySource_HasALabelTitleAndValidColour()
    {
        foreach (var source in AllSources)
        {
            Assert.False(string.IsNullOrEmpty(DamageSources.Title(source)), $"{source} has no title");
            if (source != DamageSource.Other)
                Assert.False(string.IsNullOrEmpty(DamageSources.Label(source)), $"{source} has no label");
            Assert.Matches(new Regex("^#[0-9A-F]{6}$"), DamageSources.ColorHex(source));
        }
    }

    [Fact]
    public void EverySource_HasItsOwnColour()
    {
        var colours = AllSources.Select(DamageSources.ColorHex).ToList();
        Assert.Equal(colours.Count, colours.Distinct().Count());
    }
}
