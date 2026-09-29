using System.Linq;
using DamageInsight.Describe;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Fairy Tale's Oberon (linked prefabs from the gear scan's linked/ folder).</summary>
public class OberonTests
{
    [FixtureFact]
    public void Oberon_HasThreeAttacksWithCooldowns_BasedOnSkullDamage()
    {
        var b = Fixture.Analyze("Oberon.json");
        Assert.Equal(new[] { "Galaxy beam (every 10 s)", "Spirit thunder (every 25 s)", "Spirit bomb (every 75 s)" }, b.Sections.Select(s => s.Title));
        var beam = b.Sections[0].Steps.Single().Hits.Single();
        Assert.Equal(8.4, beam.MultMin, precision: 3);
        Assert.True(beam.UsesSkullDamage);
        Assert.Equal("Magic", beam.Attribute);
        Assert.False(beam.CanCrit); // motion type Item
    }

    [FixtureFact]
    public void DarkAndSuperOberons_HitHarder()
    {
        var dark = Fixture.Analyze("DarkOberon.json");
        Assert.Equal(new[] { 1.0, 10.0 }, dark.Sections.Single(s => s.Key == "_thunderOperationRunner").Steps.Single().Hits.Select(h => h.MultMin).OrderBy(x => x));
        var super = Fixture.Analyze("SuperLightOberon.json");
        Assert.Equal(10, super.Sections.Single(s => s.Key == "_attackOperationRunner").Steps.Single().Hits.Single().MultMin, precision: 3);
    }
}
