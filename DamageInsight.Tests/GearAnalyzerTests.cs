using System.Linq;
using DamageInsight.Describe;
using Xunit;
using GearHit = DamageInsight.Describe.Hit;

namespace DamageInsight.Tests;

/// <summary>Checks the analyzer against real gear scans (values verified by reading the scan data).</summary>
public class GearAnalyzerTests
{
    private static GearHit Only(Section s) => Assert.Single(s.Steps.SelectMany(st => st.Hits));

    [FixtureFact]
    public void LittleBone_ComboSkillAndSwap()
    {
        var b = Fixture.Analyze("Skul.json");
        Assert.Equal((7.0, 11.0), (b.BaseMin, b.BaseMax));

        var combo = b.Find("Basic");
        Assert.Equal(new[] { "Hit 1", "Hit 2" }, combo.Steps.Select(s => s.Label));
        Assert.Equal(0.6, combo.Steps[0].Hits.Single().MultMin, precision: 3);
        Assert.Equal(0.8, combo.Steps[1].Hits.Single().MultMin, precision: 3);

        var throwing = b.Find("Skill", "SkullThrowing");
        Assert.Equal(6.0, throwing.Cooldown, precision: 3);
        var head = Only(throwing);
        Assert.Equal(("Magic", "Skill", "Projectile"), (head.Attribute, head.MotionType, head.AttackType));
        Assert.Equal(2.5, head.MultMin, precision: 3);

        var swap = Only(b.Find("Swap"));
        Assert.Equal(6, swap.Count);
        Assert.Equal(0.85, swap.MultMin, precision: 3);
    }

    [FixtureFact]
    public void Gargoyle_StormFollowsTheHiddenAction()
    {
        var b = Fixture.Analyze("Gargoyle.json");
        var storm = b.Sections.First(s => s.Key == "StormWind_Air");
        var tornado = Only(storm);
        Assert.Equal(3.7, tornado.MultMin, precision: 3);
        Assert.Equal("Magic", tornado.Attribute);
        Assert.Equal(4.4, Only(b.Find("Swap")).MultMin, precision: 3);
    }

    [FixtureFact]
    public void GunpowderSword_ExplosionCantCrit()
    {
        var hit = Only(Fixture.Analyze("GunpowderSword.json").Find("Effect"));
        Assert.Equal((8.0, 12.0), (hit.BaseMin, hit.BaseMax));
        Assert.Equal("Item", hit.MotionType);
        Assert.False(hit.CanCrit);
    }

    [FixtureFact]
    public void EarthSpiritGnome_UsesTheSpiritsOwnDamage()
    {
        var hit = Only(Fixture.Analyze("GnomeEarthSpirit.json").Find("Effect"));
        Assert.Equal((10.0, 12.0), (hit.BaseMin, hit.BaseMax));
        Assert.Equal(3.6, hit.MultMin, precision: 3);
        Assert.Equal("Magic", hit.Attribute);
    }
}

public class SelfReferenceTests
{
    /// <summary>
    /// The Eternal Werewolf's Hunt has a ToObject operation targeting the skull's own object; following it pulled
    /// every other action (swap, combo, other skills) into Hunt and the passive. Hunt is one bite of 400%.
    /// </summary>
    [FixtureFact]
    public void EternalWerewolf_HuntIsOnlyItsBite()
    {
        var b = Fixture.Analyze("WereWolf_4.json");
        var hunt = System.Linq.Enumerable.Single(b.Sections, s => s.Key == "Hunting_4");
        var hit = System.Linq.Enumerable.Single(System.Linq.Enumerable.SelectMany(hunt.Steps, st => st.Hits));
        Assert.Equal(4.0, hit.MultMin, precision: 3);
        var passive = System.Linq.Enumerable.Single(b.Sections, s => s.Kind == "Passive");
        Assert.Equal(2, System.Linq.Enumerable.Count(System.Linq.Enumerable.SelectMany(passive.Steps, st => st.Hits)));
    }
}

/// <summary>The 0.12 damage-scan leftovers, checked on the full gear scan (skipped when it isn't on this machine).</summary>
public class DamageScanLeftoverTests
{
    private static Breakdown Scan(string file)
    {
        string folder = Fixture.FullScanFolder();
        return folder == null ? null : GearAnalyzer.Analyze(GearDoc.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, file))));
    }

    private static GearHit[] Hits(Section s) => s.Steps.SelectMany(st => st.Hits).ToArray();

    [FixtureFact]
    public void Genie_LampPunchIsTheFollowUpAction()
    {
        var b = Scan("weapons/Genie.json");
        if (b == null)
            return;
        var punch = Assert.Single(Hits(b.Find("Skill", "FairyLamp")));
        Assert.Equal(2.5, punch.MultMin, precision: 3);
    }

    [FixtureFact]
    public void GoldenGargoyle_StatueBurstsWhenItEnds()
    {
        var b = Scan("weapons/Gargoyle_3.json");
        if (b == null)
            return;
        var burst = Assert.Single(Hits(b.Find("Skill", "Stone_3")));
        Assert.Equal((4.3, "Magic"), (System.Math.Round(burst.MultMin, 3), burst.Attribute));
    }

    [FixtureFact]
    public void DavyJones_SkillsDealTheirCannonballsDamage()
    {
        var b = Scan("weapons/DavyJones.json");
        if (b == null)
            return;
        var anchor = b.Find("Skill", "DropTheAnchor");
        Assert.Contains(anchor.Steps[0].Hits, h => System.Math.Abs(h.MultMin - 8.64) < 0.001);
        Assert.Equal("Enhanced cannonball", anchor.Steps[1].Label);
        Assert.Contains(anchor.Steps[1].Hits, h => System.Math.Abs(h.MultMin - 21.6) < 0.001);
        // The normal attack fires only normal cannonballs (it listed every cannonball type before).
        var fire = b.Sections.First(s => s.Kind == "Basic" && s.Steps.Count == 2);
        Assert.All(Hits(fire), h => Assert.Equal("Basic", h.MotionType));
    }

    [FixtureFact]
    public void ChosenMagesBadge_FireballsAndTheirExplosions()
    {
        var b = Scan("items/TheChosenMagesBadge.json");
        if (b == null)
            return;
        var hits = Hits(b.Find("Effect"));
        Assert.Equal(2, hits.Length);
        Assert.All(hits, h => Assert.Equal((3, "Magic"), (h.Count, h.Attribute)));
        Assert.Equal(new[] { 1.8, 4.0 }, hits.Select(h => System.Math.Round(h.MultMin, 3)).OrderBy(x => x));
    }
}
