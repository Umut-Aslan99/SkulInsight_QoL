using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DamageInsight.Mods;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The mod manager (0.14): our zip reader, where a package's files go, turning mods off and removing them,
/// Thunderstore's list (a real copy from 2026-10-25), what an install brings along, the restart scripts.</summary>
public class ModManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mods-{Guid.NewGuid():N}");
    private string BepInEx => Path.Combine(_root, "BepInEx");
    private string Backup => Path.Combine(_root, "Backup");

    public ModManagerTests() => Directory.CreateDirectory(Path.Combine(BepInEx, "plugins"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static byte[] Zip(params (string path, string text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, text) in files)
            {
                var entry = zip.CreateEntry(path, path.EndsWith(".txt") ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        return memory.ToArray();
    }

    [Fact]
    public void Zip_ReadsDeflatedAndStoredFiles_AndWindowsBackslashes()
    {
        string big = string.Concat(Enumerable.Repeat("Skul! ", 2000));
        var entries = ZipReader.Read(Zip(("manifest.json", "{\"name\":\"A\"}"), ("plugins\\Mod.dll", big), ("notes.txt", "stored")));
        Assert.Equal(new[] { "manifest.json", "plugins/Mod.dll", "notes.txt" }, entries.Select(e => e.Path));
        Assert.Equal(big, Encoding.UTF8.GetString(entries[1].Data));
        Assert.Equal("stored", Encoding.UTF8.GetString(entries[2].Data));
        Assert.Throws<InvalidDataException>(() => ZipReader.Read(Encoding.UTF8.GetBytes("not a zip at all, sorry")));
    }

    [Fact]
    public void Install_PutsFilesWhereR2modmanWould_AndLeavesOutWhatIsUnsafe()
    {
        Directory.CreateDirectory(Path.Combine(BepInEx, "config"));
        File.WriteAllText(Path.Combine(BepInEx, "config", "mine.cfg"), "my settings");
        var entries = ZipReader.Read(Zip(
            ("manifest.json", "{\"name\":\"Cool_Mod\",\"version_number\":\"1.2.3\",\"description\":\"Cool.\"}"),
            ("icon.png", "png"), ("Cool.dll", "root dll"), ("plugins/Sub/Helper.dll", "helper"), ("BepInEx/patchers/Early.dll", "patcher"),
            ("config/mine.cfg", "theirs"), ("config/new.cfg", "new"), ("BepInEx/core/BepInEx.dll", "no!"), ("../evil.dll", "no!"),
            ("assets/sprite.bin", "data")));
        var notes = ModFolders.Install(BepInEx, "Owner-Cool_Mod", entries, Backup);

        string plugin = Path.Combine(BepInEx, "plugins", "Owner-Cool_Mod");
        Assert.True(File.Exists(Path.Combine(plugin, "Cool.dll")));
        Assert.True(File.Exists(Path.Combine(plugin, "Sub", "Helper.dll")));
        Assert.True(File.Exists(Path.Combine(plugin, "assets", "sprite.bin")));
        Assert.True(File.Exists(Path.Combine(plugin, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(BepInEx, "patchers", "Owner-Cool_Mod", "Early.dll")));
        Assert.Equal("my settings", File.ReadAllText(Path.Combine(BepInEx, "config", "mine.cfg")));  // kept
        Assert.Equal("new", File.ReadAllText(Path.Combine(BepInEx, "config", "new.cfg")));
        Assert.False(File.Exists(Path.Combine(BepInEx, "core", "BepInEx.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.dll")));
        Assert.Equal(3, notes.Count);

        var mod = ModFolders.Scan(BepInEx).Single(m => m.Id == "Owner-Cool_Mod");
        Assert.Equal("1.2.3", mod.Version);
        Assert.Equal("Cool Mod", mod.Title);
        Assert.Equal("Owner", mod.Owner);
        Assert.Equal(3, mod.Dlls.Count); // two plugins, one patcher
        Assert.True(mod.Enabled);
        Assert.Throws<ArgumentException>(() => ModFolders.Install(BepInEx, "..\\up", entries, Backup));
    }

    [Fact]
    public void Update_MovesTheOldVersionToTheBackup()
    {
        ModFolders.Install(BepInEx, "Owner-Mod", ZipReader.Read(Zip(("Mod.dll", "v1"), ("old.txt", "x"))), Backup);
        ModFolders.Install(BepInEx, "Owner-Mod", ZipReader.Read(Zip(("Mod.dll", "v2"))), Backup);
        string plugin = Path.Combine(BepInEx, "plugins", "Owner-Mod");
        Assert.Equal("v2", File.ReadAllText(Path.Combine(plugin, "Mod.dll")));
        Assert.False(File.Exists(Path.Combine(plugin, "old.txt")));
        Assert.Single(Directory.GetDirectories(Backup));
    }

    [Fact]
    public void TurningOff_RenamesTheDlls_AndRemoving_KeepsABackup()
    {
        ModFolders.Install(BepInEx, "Owner-Mod", ZipReader.Read(Zip(("Mod.dll", "v1"))), Backup);
        File.WriteAllText(Path.Combine(BepInEx, "plugins", "Loose.dll"), "by hand");
        var mods = ModFolders.Scan(BepInEx);
        var mod = mods.Single(m => m.Id == "Owner-Mod");
        var loose = mods.Single(m => m.Id == "Loose");
        Assert.True(loose.Loose);

        ModFolders.SetEnabled(mod, false);
        Assert.True(File.Exists(Path.Combine(BepInEx, "plugins", "Owner-Mod", "Mod.dll.old")));
        Assert.False(ModFolders.Scan(BepInEx).Single(m => m.Id == "Owner-Mod").Enabled);
        ModFolders.SetEnabled(mod, true);
        Assert.True(File.Exists(Path.Combine(BepInEx, "plugins", "Owner-Mod", "Mod.dll")));

        ModFolders.Remove(mod, Backup);
        ModFolders.Remove(loose, Backup);
        Assert.DoesNotContain(ModFolders.Scan(BepInEx), m => m.Id is "Owner-Mod" or "Loose");
        Assert.Equal(2, Directory.GetFileSystemEntries(Backup).Length);
    }

    [Fact]
    public void CleanUp_FinishesWhatALoadedModLeftForTheNextStart()
    {
        string plugin = Path.Combine(BepInEx, "plugins", "Owner-Busy");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "Busy.dll.old"), "renamed while loaded");
        File.WriteAllText(Path.Combine(plugin, ModFolders.RemovedMark), "");
        string other = Path.Combine(BepInEx, "plugins", "Owner-Updated");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Mod.dll"), "new");
        File.WriteAllText(Path.Combine(other, "Mod.dll" + ModFolders.Replaced), "old, was loaded");
        Assert.True(ModFolders.Scan(BepInEx).Single(m => m.Id == "Owner-Busy").Removed);

        Assert.Equal(2, ModFolders.CleanUp(BepInEx, Backup));
        Assert.False(Directory.Exists(plugin));
        Assert.Equal(new[] { "Mod.dll" }, Directory.GetFiles(other).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("C:/Program Files (x86)/Steam/steamapps/common/Skul/BepInEx", null)]
    [InlineData("C:/Users/A/AppData/Roaming/r2modmanPlus-local/Skul/profiles/Default/BepInEx", "r2modman")]
    [InlineData("C:/Users/A/AppData/Roaming/com.kesomannen.gale/skul-the-hero-slayer/profiles/Default/BepInEx", "Gale")]
    public void ModManagers_AreRecognisedByTheirProfileFolders(string bepinex, string manager)
    {
        Assert.Equal(manager, ModFolders.ManagerOf(bepinex));
    }

    [Fact]
    public void Catalog_ReadsThunderstoresRealList()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "thunderstore_skul.json");
        if (!File.Exists(path))
            return;
        var all = ThunderstoreCatalog.Parse(File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(56, all.Count);
        Assert.All(all, p => Assert.StartsWith("https://", p.Latest.DownloadUrl));
        var own = all.Single(p => p.FullName == ThunderstoreCatalog.OwnPackage);
        Assert.Equal("SkulInsight QoL", own.Title);

        var shown = ThunderstoreCatalog.Filter(all, "", "", ThunderstoreCatalog.Sort.Downloads);
        Assert.DoesNotContain(shown, p => p.Deprecated || p.Nsfw || ThunderstoreCatalog.IsDesktopTool(p.FullName));
        Assert.Equal("BepInEx-BepInExPack_Skul", shown[0].FullName); // pinned first
        Assert.True(shown.Skip(1).Zip(shown.Skip(2), (a, b) => a.Downloads >= b.Downloads).All(x => x));
        Assert.Contains("Mods", ThunderstoreCatalog.Categories(all));
        Assert.NotEmpty(ThunderstoreCatalog.Filter(all, "legendary", "", ThunderstoreCatalog.Sort.Name));

        // A mod pack brings its mods, without the loader.
        var byName = all.ToDictionary(p => p.FullName);
        var pack = all.FirstOrDefault(p => p.Categories.Contains("Modpacks") && p.Latest.Dependencies.Count > 1);
        if (pack != null)
        {
            var order = ThunderstoreCatalog.InstallOrder(pack, byName, _ => false, new List<string>());
            Assert.Equal(pack, order.Last());
            Assert.DoesNotContain(order, p => ThunderstoreCatalog.IsLoader(p.FullName));
            Assert.True(order.Count > 1);
        }
    }

    [Fact]
    public void InstallOrder_DependenciesFirst_EachOnce_InstalledOnesLeftOut()
    {
        Package Make(string name, params string[] deps)
        {
            var p = new Package { FullName = name, Owner = name.Split('-')[0], Name = name.Split('-')[1] };
            var v = new PackageVersion { Version = "1.0.0" };
            v.Dependencies.AddRange(deps);
            p.Versions.Add(v);
            return p;
        }
        var lib = Make("A-Lib", "BepInEx-BepInExPack_Skul-5.4.2100");
        var core = Make("A-Core", "A-Lib-1.0.0");
        var mod = Make("B-Mod", "A-Core-1.0.0", "A-Lib-1.0.0", "C-Gone-2.0.0");
        var byName = new[] { lib, core, mod }.ToDictionary(p => p.FullName);
        var missing = new List<string>();
        Assert.Equal(new[] { "A-Lib", "A-Core", "B-Mod" }, ThunderstoreCatalog.InstallOrder(mod, byName, _ => false, missing).Select(p => p.FullName));
        Assert.Equal(new[] { "C-Gone" }, missing);
        Assert.Equal(new[] { "A-Core", "B-Mod" },
            ThunderstoreCatalog.InstallOrder(mod, byName, n => n == "A-Lib", new List<string>()).Select(p => p.FullName));
        Assert.Equal("A-Lib", ThunderstoreCatalog.PackageOf("A-Lib-1.0.0"));
        Assert.True(ThunderstoreCatalog.IsNewer("1.10.0", "1.9.2"));
        Assert.False(ThunderstoreCatalog.IsNewer("1.0.0", "1.0.0"));
        Assert.False(ThunderstoreCatalog.IsNewer("x", "1.0.0"));
    }

    [Fact]
    public void Restart_ScriptsWaitForTheGame_ThenAskSteam()
    {
        string windows = ModService.RestartScriptWindows(4242, ModService.LaunchLine(null, new string[0]));
        Assert.Contains("PID eq 4242", windows);
        Assert.Contains("start \"\" \"steam://rungameid/1147560\"", windows);
        Assert.DoesNotContain("\n\n", windows);

        // r2modman starts the game with doorstop arguments pointing at its profile: they go along.
        var r2 = new[] { "--doorstop-enable", "true", "--doorstop-target", @"C:\Users\A\r2modmanPlus-local\Skul\profiles\Release test\BepInEx\core\BepInEx.Preloader.dll" };
        Assert.Equal(@"start """" ""C:\Steam\steam.exe"" -applaunch 1147560 --doorstop-enable true --doorstop-target ""C:\Users\A\r2modmanPlus-local\Skul\profiles\Release test\BepInEx\core\BepInEx.Preloader.dll""",
            ModService.LaunchLine(@"C:\Steam\steam.exe", r2));
        string proton = ModService.LaunchLine(null, r2);
        Assert.StartsWith("start \"\" \"steam://run/1147560//--doorstop-enable%%20true", proton);
        Assert.DoesNotContain(" ", proton.Substring("start \"\" \"".Length));
        string mac = ModService.RestartScriptMac(4242);
        Assert.Contains("kill -0 4242", mac);
        Assert.Contains("open \"steam://rungameid/1147560\"", mac);
    }
}
