using Xunit;
using System;
using System.IO;
using DamageInsight.Describe;

namespace DamageInsight.Tests;

/// <summary>Loads real gear scans: the fixtures copied into the test project, or the full scan next to the repo.</summary>
internal static class Fixture
{
    /// <summary>Whether the game-data fixtures are present (they are not in the public repository).</summary>
    public static bool Available =>
        Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures")) &&
        Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures"), "*.json").Length > 0;

    public const string SkipReason = "Needs game data in DamageInsight.Tests/Fixtures (not in the public repository, see docs/BUILDING.md).";

    public static GearDoc Load(string fileName) =>
        GearDoc.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", fileName)));

    public static Breakdown Analyze(string fileName) => GearAnalyzer.Analyze(Load(fileName));

    /// <summary>The full gear scan (SkulModding\GearScan), or null if it isn't on this machine.</summary>
    public static string FullScanFolder()
    {
        for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "GearScan");
            if (File.Exists(Path.Combine(candidate, "index.tsv")))
                return candidate;
        }
        return null;
    }
}

/// <summary>
/// A test that needs serialized game data from Fixtures/. That data isn't in the public repository (it's the
/// game's content), so without it the test is skipped instead of failing. See docs/BUILDING.md.
/// </summary>
public sealed class FixtureFactAttribute : FactAttribute
{
    public FixtureFactAttribute()
    {
        if (!Fixture.Available)
            Skip = Fixture.SkipReason;
    }
}

/// <summary>Like <see cref="FixtureFactAttribute"/>, for theories.</summary>
public sealed class FixtureTheoryAttribute : TheoryAttribute
{
    public FixtureTheoryAttribute()
    {
        if (!Fixture.Available)
            Skip = Fixture.SkipReason;
    }
}
