using System.Linq;
using DamageInsight.Describe;
using DamageInsight.Recording;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

public class ChargeTests
{
    private readonly ITestOutputHelper _out;
    public ChargeTests(ITestOutputHelper output) => _out = output;

    [FixtureFact]
    public void Warrior_HasUnchargedAndChargedDamage()
    {
        var b = Fixture.Analyze("Warrior.json");
        foreach (var s in b.Sections)
            _out.WriteLine($"[{s.Kind}] {s.Key}\n{LogText.Plain(DescriptionFormatter.Section(s, StatSnapshot.Neutral))}");

        var charged = b.Sections.Where(s => s.Steps.Any(st => st.Label.Contains("harged"))).ToList();
        Assert.NotEmpty(charged);
        // A fully charged release hits harder than an early release.
        foreach (var s in charged)
        {
            var early = s.Steps.FirstOrDefault(st => st.Label == "Uncharged");
            var full = s.Steps.FirstOrDefault(st => st.Label == "Charged");
            if (early == null || full == null || early.Hits.Count == 0 || full.Hits.Count == 0)
                continue;
            Assert.True(full.Hits.Max(h => h.MultMax * h.Count) >= early.Hits.Max(h => h.MultMax * h.Count), s.Key);
        }
    }
}

public class WeaponLabelTests
{
    [FixtureTheory]
    [InlineData("PowerbombJumpAttack", "Powerbomb jump attack")]
    [InlineData("Fire_Upward", "Fire upward")]
    [InlineData("ComboAttack(3)", "Combo attack")]
    public void Humanize(string raw, string expected) => Assert.Equal(expected, DamageInsight.Describe.WeaponRefiner.Humanize(raw));

    [FixtureFact]
    public void Balrog_JumpAttacksAreLabelled()
    {
        var b = Fixture.Analyze("Balrog_Head.json");
        var jumps = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(b.Sections, s => s.Kind == "Jump"));
        Assert.True(jumps.Count > 1);
        Assert.All(jumps, s => Assert.False(string.IsNullOrEmpty(s.Title)));
        Assert.Contains(jumps, s => s.Title.StartsWith("Powerbomb jump attack"));
    }
}

public class WarlordTests
{
    [FixtureFact]
    public void Warlord_SkillsHaveOneLinePerChargeLevel_AndTheGuardBonus()
    {
        var b = Fixture.Analyze("Warrior_3.json");
        var power = System.Linq.Enumerable.Single(b.Sections, s => s.Key == "ChargeAttack_3"); // Power Strike
        Assert.Equal(new[] { "Uncharged", "Charge 1", "Charge 2" }, System.Linq.Enumerable.Select(power.Steps, st => st.Label));
        Assert.Equal(5.8, System.Linq.Enumerable.Single(power.Steps[0].Hits).MultMin, precision: 3);
        Assert.Equal(11.2, System.Linq.Enumerable.Single(power.Steps[1].Hits).MultMin, precision: 3);
        Assert.Equal(15.8, System.Linq.Enumerable.Single(power.Steps[2].Hits).MultMin, precision: 3);
        Assert.Contains(power.Notes, n => n.StartsWith("+40% per hit you take while it runs (max 5 = +200%"));
    }
}
