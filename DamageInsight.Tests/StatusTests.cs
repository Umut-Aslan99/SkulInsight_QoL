using System;
using System.IO;
using DamageInsight.Describe;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Status texts from the game's real status settings (Fixtures/statuses.json).</summary>
public class StatusTests
{
    private static Node Settings => GearDoc.ParseNode(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "statuses.json")));

    private static string Text(string status, StatSnapshot stats = null) =>
        LogText.Plain(StatusDescriptions.Describe(Settings, status, stats ?? StatSnapshot.Neutral));

    [FixtureFact]
    public void Poison_TicksFromTheGameData()
    {
        Assert.Equal("Poison: 4 Physical every 0.33 s for 3 s (~9 ticks = 36 · can't crit)", Text("Poison"));
    }

    [FixtureFact]
    public void Poison_TickSpeedStatShortensTheInterval()
    {
        Assert.Contains("every 0.2 s for 3 s (~15 ticks", Text("Poison", new StatSnapshot { PoisonTickReduction = 0.13 }));
    }

    [FixtureFact]
    public void Burn_HasTargetTicksAndEmberSplash()
    {
        string text = Text("Burn", new StatSnapshot { EmberDamage = 1.5 });
        Assert.Contains("Burn: 8 Magic", text);
        Assert.Contains("every 2 s for 10 s", text);
        Assert.Contains("Burn splash (ember): 24 Magic", text);   // 16 x 150% ember damage
        Assert.Contains("within 3 m", text);        // radius 2 x 150%
    }

    [FixtureFact]
    public void Bleed_CritsOnlyWithExcessiveBleeding4_FreezeAndStunDurations()
    {
        Assert.Contains("can't crit", Text("Bleed"));
        Assert.StartsWith("Bleed (on the 2nd application): 40 Physical", Text("Bleed"));
        Assert.Contains("Super bleed (x1.55): 62 Physical", Text("Bleed"));

        // Excessive Bleeding 2+ (+60% bleed damage) and 4 (bleed can crit), 150% crit damage.
        string full = Text("Bleed", new StatSnapshot { BleedDamage = 1.6, BleedCanCrit = true, CritDamage = 1.5 });
        Assert.StartsWith("Bleed (on the 2nd application): 64 Physical", full);
        Assert.Contains("crit 96 (x1.5 crit dmg)", full);
        Assert.Contains("Super bleed (x1.55): 100 Physical", full); // 40 x 1.6 x 1.55 = 99.2
        Assert.Contains("crit 149 (x1.5 crit dmg)", full);
        Assert.DoesNotContain("can't crit", full);
        Assert.Equal("Freeze: 3.5 s", Text("Freeze", new StatSnapshot { FreezeBonus = 0.5 }));
        Assert.Equal("Stun: 1.5 s", Text("Stun"));
    }
}
