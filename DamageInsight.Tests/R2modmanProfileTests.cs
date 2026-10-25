using System;
using System.IO;
using System.Linq;
using DamageInsight.Mods;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>r2modman's mods.yml kept right by the mod manager and SkulInsight's own update (the sample is a real
/// profile's list from 2026-10-25, "Release test").</summary>
public class R2modmanProfileTests
{
    private const string Sample =
        "- manifestVersion: 1\n" +
        "  name: BepInEx-BepInExPack_Skul\n" +
        "  authorName: BepInEx\n" +
        "  websiteUrl: https://thunderstore.io/c/skul-the-hero-slayer/p/BepInEx/BepInExPack_Skul/\n" +
        "  displayName: BepInExPack_Skul\n" +
        "  description: >-\n" +
        "    BepInEx pack for Skul: the Hero Slayer. Includes unstripped core and Unity\n" +
        "    libraries. Ready to use.\n" +
        "  gameVersion: '0'\n" +
        "  networkMode: both\n" +
        "  packageType: other\n" +
        "  installMode: managed\n" +
        "  installedAtTime: 1790683791812\n" +
        "  loaders: []\n" +
        "  dependencies: []\n" +
        "  incompatibilities: []\n" +
        "  optionalDependencies: []\n" +
        "  versionNumber:\n" +
        "    major: 5\n" +
        "    minor: 4\n" +
        "    patch: 2100\n" +
        "  enabled: true\n" +
        "  onlineSource: true\n" +
        "  trustedPackage: false\n" +
        "- manifestVersion: 1\n" +
        "  name: DocRun-SkulInsight_QoL\n" +
        "  authorName: DocRun\n" +
        "  websiteUrl: https://thunderstore.io/c/skul-the-hero-slayer/p/DocRun/SkulInsight_QoL/\n" +
        "  displayName: SkulInsight_QoL\n" +
        "  description: >-\n" +
        "    A Codex bestiary with boss move lists and fight films.\n" +
        "  gameVersion: '0'\n" +
        "  networkMode: both\n" +
        "  packageType: other\n" +
        "  installMode: managed\n" +
        "  installedAtTime: 1790842901934\n" +
        "  loaders: []\n" +
        "  dependencies:\n" +
        "    - BepInEx-BepInExPack_Skul-5.4.2100\n" +
        "  incompatibilities: []\n" +
        "  optionalDependencies: []\n" +
        "  versionNumber:\n" +
        "    major: 0\n" +
        "    minor: 10\n" +
        "    patch: 0\n" +
        "  enabled: true\n" +
        "  onlineSource: true\n" +
        "  trustedPackage: false\n";

    [Fact]
    public void SetVersion_ChangesOnlyThatModsVersion()
    {
        string after = R2modmanProfile.SetVersion(Sample, "DocRun-SkulInsight_QoL", "0.14.1");
        Assert.Contains("    major: 0\n    minor: 14\n    patch: 1\n  enabled: true", after);
        Assert.Contains("    major: 5\n    minor: 4\n    patch: 2100", after);       // the loader stays
        Assert.Equal(Sample.Split('\n').Length, after.Split('\n').Length);
        Assert.Null(R2modmanProfile.SetVersion(Sample, "Nobody-Here", "1.0.0"));
        Assert.Null(R2modmanProfile.SetVersion(Sample, "DocRun-SkulInsight_QoL", "1.0"));   // not x.y.z
    }

    [Fact]
    public void SetEnabled_AndRemove_TouchOnlyThatMod()
    {
        string off = R2modmanProfile.SetEnabled(Sample, "DocRun-SkulInsight_QoL", false);
        Assert.Contains("    patch: 0\n  enabled: false", off);
        Assert.Contains("    patch: 2100\n  enabled: true", off);

        string removed = R2modmanProfile.Remove(Sample, "DocRun-SkulInsight_QoL");
        Assert.False(R2modmanProfile.Has(removed, "DocRun-SkulInsight_QoL"));
        Assert.True(R2modmanProfile.Has(removed, "BepInEx-BepInExPack_Skul"));
        Assert.Equal("[]\n", R2modmanProfile.Remove(R2modmanProfile.Remove(Sample, "DocRun-SkulInsight_QoL"), "BepInEx-BepInExPack_Skul"));
    }

    [Fact]
    public void Add_WritesAnEntryLikeR2modman()
    {
        var package = new Package { FullName = "MrBacanudo-LegendaryStart", Owner = "MrBacanudo", Name = "LegendaryStart",
            Url = "https://thunderstore.io/c/skul-the-hero-slayer/p/MrBacanudo/LegendaryStart/" };
        var version = new PackageVersion { Version = "0.0.1", Description = "Start with a legendary skull, it's 'fun'" };
        version.Dependencies.Add("BepInEx-BepInExPack_Skul-5.4.2100");
        package.Versions.Add(version);

        string after = R2modmanProfile.Add(Sample, package, 1790900000000);
        Assert.StartsWith(Sample, after);
        Assert.True(R2modmanProfile.Has(after, "MrBacanudo-LegendaryStart"));
        Assert.Contains("  description: 'Start with a legendary skull, it''s ''fun'''\n", after);
        Assert.Contains("  dependencies:\n    - BepInEx-BepInExPack_Skul-5.4.2100\n", after);
        Assert.Contains("  versionNumber:\n    major: 0\n    minor: 0\n    patch: 1\n  enabled: true\n", after);
        Assert.Contains("  installedAtTime: 1790900000000\n", after);

        // again (an update): one entry, the new version
        version.Version = "0.0.2";
        string twice = R2modmanProfile.Add(after, package, 1790900000001);
        Assert.Single(twice.Split('\n'), l => l == "  name: MrBacanudo-LegendaryStart");
        Assert.Contains("    patch: 2\n", twice);
        Assert.StartsWith("- manifestVersion: 1\n  name: MrBacanudo", R2modmanProfile.Add("[]\n", package, 1));
    }

    [Fact]
    public void ListOf_IsTheProfilesModsYml()
    {
        string profile = Path.Combine(Path.GetTempPath(), $"r2-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(profile, "BepInEx"));
            Assert.Null(R2modmanProfile.ListOf(Path.Combine(profile, "BepInEx")));
            File.WriteAllText(Path.Combine(profile, "mods.yml"), Sample);
            Assert.Equal(Path.Combine(profile, "mods.yml"), R2modmanProfile.ListOf(Path.Combine(profile, "BepInEx")));
            Assert.True(R2modmanProfile.Change(Path.Combine(profile, "BepInEx"), y => R2modmanProfile.SetVersion(y, "DocRun-SkulInsight_QoL", "0.14.0"), out var problem));
            Assert.Null(problem);
            Assert.Contains("minor: 14", File.ReadAllText(Path.Combine(profile, "mods.yml")));
            Assert.False(File.Exists(Path.Combine(profile, "mods.yml.skulinsight")));
        }
        finally
        {
            Directory.Delete(profile, recursive: true);
        }
    }
}
