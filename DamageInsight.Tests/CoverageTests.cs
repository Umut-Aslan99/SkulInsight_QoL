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

    /// <summary>
    /// The damage scan (0.12 plan, CODEX_PLAN C): which gear seems to deal damage (its description says so, or it has
    /// its own AttackDamage) but gets no damage numbers, and which skull sections (basic attack, skills, swap, ...)
    /// have none. Writes damage-scan.tsv next to the test binaries; docs/DAMAGE_SCAN.md summarizes it.
    /// </summary>
    [FixtureFact]
    public void Damage_scan_lists_gear_without_numbers()
    {
        string folder = Fixture.FullScanFolder();
        if (folder == null)
            return;
        var rows = new List<string> { "category\tname\tdisplay name\tpart\tstatus\tdescription" };
        int damaging = 0, covered = 0, sections = 0, sectionsCovered = 0;
        foreach (string category in new[] { "weapons", "items", "essences" })
            foreach (string file in Directory.GetFiles(Path.Combine(folder, category), "*.json").OrderBy(f => f))
            {
                var doc = GearDoc.Parse(File.ReadAllText(file));
                var meta = GearDoc.ParseNode(File.ReadAllText(file)).Child("meta");
                string name = Path.GetFileNameWithoutExtension(file);
                string display = meta.Str("displayName") ?? "";
                string text = ((meta.Str("description") ?? "") + " " + (meta.Str("activeDescription") ?? "")).Replace("\t", " ").Replace("\n", " ");
                string plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
                Breakdown b;
                try { b = GearAnalyzer.Analyze(doc); }
                catch (Exception) { continue; }
                if (category == "weapons")
                {
                    foreach (var section in b.Sections.Where(x => x.Kind is "Basic" or "Jump" or "Skill" or "Swap" or "Dash" or "Active"))
                    {
                        sections++;
                        if (section.HasHits)
                            sectionsCovered++;
                        else
                            rows.Add($"{category}\t{name}\t{display}\t{section.Kind} {section.Key} {section.Title}".TrimEnd() + "\tno numbers\t");
                    }
                    continue;
                }
                bool says = plain.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!says && b.BaseMax <= 0)
                    continue;
                damaging++;
                if (b.Sections.Any(x => x.HasHits))
                    covered++;
                else
                    rows.Add($"{category}\t{name}\t{display}\t\t{(says ? "says damage, no numbers" : "own damage, no numbers")}\t{plain.Trim()}");
            }
        // Transformed bodies (StartWeaponPolymorph): the skull descriptions list their attacks since 0.11.
        var bodies = Directory.GetFiles(Path.Combine(folder, "weapons"), "*Polymorph*.json").Where(f => !Path.GetFileName(f).StartsWith("Ref_")).ToList();
        int bodiesWithHits = bodies.Count(f => GearAnalyzer.Analyze(GearDoc.Parse(File.ReadAllText(f))).Sections.Any(x => x.HasHits && x.Kind != "Passive"));
        _out.WriteLine($"transformed bodies with attack numbers: {bodiesWithHits}/{bodies.Count}");
        File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "damage-scan.tsv"), rows);
        _out.WriteLine($"items+essences dealing damage: {covered}/{damaging} with numbers; skull sections: {sectionsCovered}/{sections} with numbers");
    }

    [FixtureFact]
    public void Short_descriptions_show_only_the_totals()
    {
        string folder = Fixture.FullScanFolder();
        if (folder == null)
            return;
        var b = GearAnalyzer.Analyze(GearDoc.Parse(File.ReadAllText(Path.Combine(folder, "weapons", "Skul.json"))));
        var basic = b.Sections.First(s => s.Kind == "Basic" && s.HasHits);
        string full = DescriptionFormatter.Section(basic, StatSnapshot.Neutral);
        DescriptionFormatter.Short = true;
        try
        {
            string plain = System.Text.RegularExpressions.Regex.Replace(DescriptionFormatter.Section(basic, StatSnapshot.Neutral), "<[^>]+>", "");
            Assert.DoesNotContain("%", plain);
            Assert.Contains("Physical", plain);
            Assert.True(plain.Length < System.Text.RegularExpressions.Regex.Replace(full, "<[^>]+>", "").Length);
        }
        finally
        {
            DescriptionFormatter.Short = false;
        }
    }
}
