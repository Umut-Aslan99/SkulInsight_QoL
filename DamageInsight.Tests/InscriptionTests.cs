using System.Linq;
using DamageInsight.Describe;
using Xunit;
using GearHit = DamageInsight.Describe.Hit;

namespace DamageInsight.Tests;

/// <summary>Inscription damage per step, from the real inscription data (values verified in the scan).</summary>
public class InscriptionTests
{
    private static GearHit Only(Section s) => Assert.Single(s.Steps.SelectMany(st => st.Hits));

    [FixtureFact]
    public void Brawl_ShockwaveOnStep1_EnhancedEvery5thOnStep2()
    {
        var b = Fixture.Analyze("Brawl.json");

        var wave = b.Sections.Single(s => s.Step == 1);
        var hit = Only(wave);
        Assert.Equal("Shockwave", wave.Title);
        Assert.Equal((8.0, 12.0), (hit.BaseMin, hit.BaseMax));   // Brawl's own AttackDamage
        Assert.Equal(0.15, hit.MultMin, precision: 3);
        Assert.True(hit.AdaptiveForce);                           // higher of physical / magic attack

        var enhanced = b.Sections.Single(s => s.Step == 2);
        Assert.Equal("Every 5th shockwave", enhanced.Trigger);
        Assert.Equal(1.2, Only(enhanced).MultMin, precision: 3);
    }

    [FixtureFact]
    public void Revenge_AttackOnBothSteps()
    {
        var b = Fixture.Analyze("Revenge.json");
        Assert.Equal(new[] { 1, 2 }, b.Sections.Select(s => s.Step).OrderBy(x => x));
        Assert.All(b.Sections, s => Assert.Equal(1.5, Only(s).MultMin, precision: 3));
    }
}

public class StrikeTests
{
    [FixtureFact]
    public void Strike_ExplainsTheExtraCritDamageRolls()
    {
        var b = Fixture.Analyze("Strike.json");
        var step2 = System.Linq.Enumerable.Single(b.Sections, s => s.Step == 2);
        Assert.Equal("30% of your hits: +270% crit damage if they crit (x1.7 becomes x4.4)", System.Linq.Enumerable.Single(step2.Notes));
        var super = System.Linq.Enumerable.Single(b.Sections, s => s.Step == Section.SuperStep);
        Assert.StartsWith("another 10% of your hits: +450% crit damage", System.Linq.Enumerable.Single(super.Notes));
    }
}

public class ArmsTests
{
    [FixtureFact]
    public void Arms_SplitsSwingsByStepAndShowsThemCompactly()
    {
        var b = Fixture.Analyze("Arms.json");
        var byStep = System.Linq.Enumerable.ToLookup(b.Sections, s => s.Step);
        Assert.Contains(byStep[2], s => s.Title == "Armament swing (ground)");
        Assert.Contains(byStep[2], s => s.Title == "Armament swing (air)");
        Assert.Contains(byStep[3], s => s.Title == "Enhanced swing (ground)" && s.Trigger.StartsWith("Every "));
        Assert.Contains(byStep[Section.SuperStep], s => s.Title.StartsWith("True form swing"));
        Assert.All(b.Sections, s => Assert.True(s.Compact));

        var stats = new StatSnapshot { Physical = 1.6, SkullMin = 7, SkullMax = 11 };
        string text = DescriptionFormatter.Section(System.Linq.Enumerable.First(byStep[2]), stats);
        Assert.True(text.Split('\n').Length <= 3, text); // title + one line (+ notes)
        var trueAir = System.Linq.Enumerable.Single(b.Sections, s => s.Title == "True form swing (air)");
        string plain = System.Text.RegularExpressions.Regex.Replace(DescriptionFormatter.Section(trueAir, stats), "<[^>]+>", "");
        Assert.Equal("3 hits = 415–652 Physical (700% + 2x 1500% of 7–11 skull dmg, x160% phys. atk) · can't crit", plain);
    }
}
