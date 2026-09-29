using System.Linq;
using DamageInsight.Describe;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

public class ItemRefinerTests
{
    private readonly ITestOutputHelper _out;
    public ItemRefinerTests(ITestOutputHelper output) => _out = output;

    [FixtureFact]
    public void GunpowderSword_TriggersOnCrit()
    {
        var effect = Fixture.Analyze("GunpowderSword.json").Find("Effect");
        Assert.StartsWith("On critical hit", effect.Trigger);
    }

    [FixtureFact]
    public void ShadowSpirit_AttacksEvery7Seconds()
    {
        var effect = Fixture.Analyze("DeathShadowSpirit.json").Find("Effect");
        Assert.StartsWith("Every 7 s", effect.Trigger);
    }

    [FixtureFact]
    public void AllItems_TriggerTexts()
    {
        string folder = Fixture.FullScanFolder();
        if (folder == null)
            return;
        int withHits = 0, withTrigger = 0;
        foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(folder, "items"), "*.json"))
        {
            var b = GearAnalyzer.Analyze(GearDoc.Parse(System.IO.File.ReadAllText(file)));
            var s = b.Find("Effect");
            if (s == null || !s.HasHits)
                continue;
            withHits++;
            var texts = s.Trigger.Length > 0 ? new[] { s.Trigger } : s.Steps.SelectMany(x => x.Hits).Select(h => h.Note).Distinct().ToArray();
            if (texts.Any(t => t.Length > 0))
                withTrigger++;
            _out.WriteLine($"{b.Name,-32} {string.Join(" | ", texts)}");
        }
        _out.WriteLine($"{withTrigger}/{withHits} damaging items have a trigger text");
    }
}

public class ItemUpgradeTests
{
    [FixtureFact]
    public void ShadowSpirit_BecomesDarkSpiritShade_WhenOwningLightSpirit()
    {
        var upgrade = Assert.Single(Fixture.Analyze("DeathShadowSpirit.json").Upgrades);
        Assert.Equal("ShadeDarkSpirit", upgrade.TargetName);
        Assert.Equal("LughLightSpirit", upgrade.OwnItemName);
    }

    [FixtureFact]
    public void GunpowderSword_HasNoUpgrade()
    {
        Assert.Empty(Fixture.Analyze("GunpowderSword.json").Upgrades);
    }
}
