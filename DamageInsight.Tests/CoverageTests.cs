using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageInsight.Describe;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

/// <summary>
/// Runs the analyzer over the full gear scan (if present on this machine): no gear may crash it,
/// and it reports how much gear gets damage numbers.
/// </summary>
public class CoverageTests
{
    private readonly ITestOutputHelper _out;
    public CoverageTests(ITestOutputHelper output) => _out = output;

    [FixtureFact]
    public void AllScannedGear_AnalyzesWithoutErrors()
    {
        string folder = Fixture.FullScanFolder();
        if (folder == null)
            return; // full scan not available here

        var errors = new List<string>();
        var stats = new Dictionary<string, (int total, int withHits)>();
        var weaponSections = new Dictionary<string, (int total, int withHits)>();
        foreach (string category in new[] { "weapons", "items", "essences" })
        {
            foreach (string file in Directory.GetFiles(Path.Combine(folder, category), "*.json"))
            {
                try
                {
                    var b = GearAnalyzer.Analyze(GearDoc.Parse(File.ReadAllText(file)));
                    var (t, w) = stats.TryGetValue(category, out var v) ? v : (0, 0);
                    stats[category] = (t + 1, w + (b.Sections.Any(s => s.HasHits) ? 1 : 0));
                    if (category == "weapons")
                        foreach (var s in b.Sections)
                        {
                            var (st, sw) = weaponSections.TryGetValue(s.Kind, out var x) ? x : (0, 0);
                            weaponSections[s.Kind] = (st + 1, sw + (s.HasHits ? 1 : 0));
                        }
                }
                catch (Exception e)
                {
                    errors.Add($"{category}/{Path.GetFileName(file)}: {e.GetType().Name}: {e.Message}");
                }
            }
        }

        foreach (var kv in stats)
            _out.WriteLine($"{kv.Key}: {kv.Value.withHits}/{kv.Value.total} have damage numbers");
        foreach (var kv in weaponSections)
            _out.WriteLine($"  weapon sections {kv.Key}: {kv.Value.withHits}/{kv.Value.total} with hits");
        foreach (var e in errors.Take(20))
            _out.WriteLine("ERROR " + e);
        Assert.Empty(errors);
    }
}
