using System;
using System.IO;
using System.Linq;
using DamageInsight.Mods;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>SkulInsight updating itself (0.14): Thunderstore's answer, the DLL in the package, the checked swap.</summary>
public class SelfUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"self-update-{Guid.NewGuid():N}");
    private string Plugins => Path.Combine(_root, "plugins", "DamageInsight");
    private string Backup => Path.Combine(_root, "Backup");
    private static string OurVersion => typeof(Plugin).Assembly.GetName().Version.ToString(3);

    public SelfUpdateTests() => Directory.CreateDirectory(Plugins);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Latest_ReadsThunderstoresAnswer_AndOnlyTrustsThunderstore()
    {
        var latest = SelfUpdate.Latest("{\"full_name\":\"DocRun-SkulInsight_QoL\",\"latest\":{\"version_number\":\"0.14.1\"," +
                                         "\"download_url\":\"https://thunderstore.io/package/download/DocRun/SkulInsight_QoL/0.14.1/\"}}");
        Assert.Equal(("0.14.1", "https://thunderstore.io/package/download/DocRun/SkulInsight_QoL/0.14.1/"), latest);
        Assert.Null(SelfUpdate.Latest("{\"latest\":{\"version_number\":\"9.9.9\",\"download_url\":\"https://example.com/evil.zip\"}}"));
        Assert.Null(SelfUpdate.Latest("not json"));
    }

    [Fact]
    public void DllIn_FindsTheModInThePackage()
    {
        var entries = new[]
        {
            new ZipReader.Entry { Path = "manifest.json", Data = new byte[] { 1 } },
            new ZipReader.Entry { Path = "plugins/DamageInsight.dll", Data = new byte[] { 2 } },
        };
        Assert.Equal(new byte[] { 2 }, SelfUpdate.DllIn(entries));
        Assert.Null(SelfUpdate.DllIn(entries.Take(1)));
    }

    [Fact]
    public void Install_SwapsTheDll_AndKeepsTheOldOne()
    {
        string current = Path.Combine(Plugins, SelfUpdate.DllName);
        File.WriteAllBytes(current, new byte[100_000]); // the "old" DLL
        byte[] fresh = File.ReadAllBytes(typeof(Plugin).Assembly.Location);

        Assert.Null(SelfUpdate.Install(current, fresh, OurVersion, "0.13.1", Backup));
        Assert.Equal(fresh, File.ReadAllBytes(current));
        Assert.True(File.Exists(current + ModFolders.Replaced));                  // deleted at the next start (CleanUp)
        Assert.True(File.Exists(Path.Combine(Backup, "DamageInsight-0.13.1.dll")));
        Assert.False(File.Exists(current + ".download"));
    }

    [Fact]
    public void Install_RefusesAWrongVersionOrAnotherFile_AndChangesNothing()
    {
        string current = Path.Combine(Plugins, SelfUpdate.DllName);
        File.WriteAllBytes(current, new byte[100_000]);
        byte[] fresh = File.ReadAllBytes(typeof(Plugin).Assembly.Location);
        byte[] other = File.ReadAllBytes(typeof(Assert).Assembly.Location);

        Assert.Contains("version", SelfUpdate.Install(current, fresh, "99.0.0", "0.13.1", Backup));
        Assert.Contains("xunit", SelfUpdate.Install(current, other, OurVersion, "0.13.1", Backup));
        Assert.NotNull(SelfUpdate.Install(current, new byte[10], OurVersion, "0.13.1", Backup));
        Assert.Equal(new byte[100_000], File.ReadAllBytes(current));
        Assert.Equal(new[] { SelfUpdate.DllName }, Directory.GetFiles(Plugins).Select(Path.GetFileName));
    }
}
