using System;
using System.IO;
using System.Linq;
using DamageInsight.Describe;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

public class EssenceRefinerTests
{
    private readonly ITestOutputHelper _out;
    public EssenceRefinerTests(ITestOutputHelper output) => _out = output;

    [FixtureFact]
    public void Debug_ListEssencesWithoutHits()
    {
        string folder = Fixture.FullScanFolder();
        if (folder == null)
            return;
        foreach (string file in Directory.GetFiles(Path.Combine(folder, "essences"), "*.json").OrderBy(f => f))
        {
            var b = GearAnalyzer.Analyze(GearDoc.Parse(File.ReadAllText(file)));
            bool hasHits = b.Sections.Any(s => s.HasHits);
            _out.WriteLine($"{Path.GetFileNameWithoutExtension(file)}: hits={hasHits} sections=[{string.Join(",", b.Sections.Select(s => s.Kind + (s.HasHits ? "*" : "")))}]");
        }
    }
}
