using System.Linq;
using DamageInsight.Describe;
using DamageInsight.Recording;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

public class SummonTests
{
    private readonly ITestOutputHelper _out;
    public SummonTests(ITestOutputHelper output) => _out = output;

    [FixtureFact]
    public void DwarfTurret_AttacksUseItsOwnDamage()
    {
        var b = Fixture.Analyze("DwarfTurret.json");
        foreach (var s in b.Sections)
            _out.WriteLine($"{s.Key}: {LogText.Plain(DescriptionFormatter.Section(s, StatSnapshot.Neutral))}");

        Assert.Contains(b.Sections, s => s.Key == "Attack");
        Assert.All(b.Sections.SelectMany(s => s.Steps).SelectMany(st => st.Hits),
            h => Assert.Equal((7.0, 13.0), (h.BaseMin, h.BaseMax)));
    }

    [FixtureFact]
    public void AllSummons_AnalyzeWithoutErrors()
    {
        string folder = Fixture.FullScanFolder();
        if (folder == null)
            return;
        int withHits = 0, total = 0;
        foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(folder, "characters"), "*.json"))
        {
            total++;
            if (GearAnalyzer.Analyze(GearDoc.Parse(System.IO.File.ReadAllText(file))).Sections.Any(s => s.HasHits))
                withHits++;
        }
        _out.WriteLine($"{withHits}/{total} summons have damage numbers");
    }
}
