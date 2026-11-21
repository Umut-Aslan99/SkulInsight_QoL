using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using DamageInsight.Coop.Testing;
using DamageInsight.Patches;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Co-op test copies (package F): the start options, the per-copy data folder and the save redirect.</summary>
public class CoopLaunchTests
{
    [Fact]
    public void NoOptionsIsTheNormalGame()
    {
        foreach (var args in new[] { null, new string[0], new[] { "Skul.exe", "-screen-fullscreen", "0", "-logFile", "x.log" } })
        {
            var launch = CoopLaunch.Parse(args);
            Assert.Equal(1, launch.Instance);
            Assert.False(launch.IsTestCopy);
            Assert.False(launch.FromLauncher);
            Assert.Equal(CoopRole.None, launch.Role);
            Assert.Equal(CoopTransportKind.None, launch.Transport);
            Assert.Equal(CoopLaunch.DefaultPort, launch.Port);
            Assert.False(launch.SimulatesLag);
            Assert.False(launch.Bot);
            Assert.Null(launch.Name);
        }
    }

    [Fact]
    public void ReadsEveryOption()
    {
        var launch = CoopLaunch.Parse(new[]
        {
            "C:/Skul/Skul.exe", "-screen-width", "960", "--skulinsight-instance", "2", "--skulinsight-coop", "join",
            "--skulinsight-transport", "udp", "--skulinsight-port", "28000", "--skulinsight-lag", "100",
            "--skulinsight-jitter", "20", "--skulinsight-loss", "2.5", "--skulinsight-bot", "--skulinsight-name", "Bot Two",
        });
        Assert.Equal(2, launch.Instance);
        Assert.True(launch.IsTestCopy);
        Assert.True(launch.FromLauncher);
        Assert.Equal(CoopRole.Join, launch.Role);
        Assert.Equal(CoopTransportKind.Udp, launch.Transport);
        Assert.Equal(28000, launch.Port);
        Assert.Equal(100, launch.LagMs);
        Assert.Equal(20, launch.JitterMs);
        Assert.Equal(2.5f, launch.LossPercent);
        Assert.True(launch.SimulatesLag);
        Assert.True(launch.Bot);
        Assert.Equal("Bot Two", launch.Name);
        Assert.Equal("instance 2, join, udp :28000, lag 100 ms +-20, loss 2.5 %, bot, name Bot Two", launch.ToString());
    }

    [Fact]
    public void EqualsFormAndAnyCase()
    {
        var launch = CoopLaunch.Parse(new[] { "--SkulInsight-Instance=3", "--skulinsight-COOP=Host", "--skulinsight-transport=STEAM", "--skulinsight-loss=5%" });
        Assert.Equal(3, launch.Instance);
        Assert.Equal(CoopRole.Host, launch.Role);
        Assert.Equal(CoopTransportKind.Steam, launch.Transport);
        Assert.Equal(5f, launch.LossPercent);
    }

    [Theory]
    [InlineData("--skulinsight-instance", "5")]
    [InlineData("--skulinsight-instance", "0")]
    [InlineData("--skulinsight-instance", "two")]
    [InlineData("--skulinsight-port", "70000")]
    [InlineData("--skulinsight-lag", "-5")]
    [InlineData("--skulinsight-lag", "5000")]
    [InlineData("--skulinsight-loss", "101")]
    [InlineData("--skulinsight-coop", "spectate")]
    [InlineData("--skulinsight-transport", "tcp")]
    [InlineData("--skulinsight-unknown", "1")]
    public void BadValuesKeepTheDefaults(string option, string value)
    {
        var launch = CoopLaunch.Parse(new[] { option, value });
        Assert.Equal(1, launch.Instance);
        Assert.Equal(CoopLaunch.DefaultPort, launch.Port);
        Assert.Equal(0, launch.LagMs);
        Assert.Equal(0f, launch.LossPercent);
        Assert.Equal(CoopRole.None, launch.Role);
        Assert.Equal(CoopTransportKind.None, launch.Transport);
    }

    [Fact]
    public void AnOptionIsNeverTakenAsTheValueOfAnother()
    {
        var launch = CoopLaunch.Parse(new[] { "--skulinsight-port", "--skulinsight-instance", "2", "--skulinsight-name" });
        Assert.Equal(CoopLaunch.DefaultPort, launch.Port);
        Assert.Equal(2, launch.Instance);
        Assert.Null(launch.Name);
    }

    [Fact]
    public void BotCanBeSwitchedOffAndNamesAreCut()
    {
        Assert.False(CoopLaunch.Parse(new[] { "--skulinsight-bot=0" }).Bot);
        Assert.True(CoopLaunch.Parse(new[] { "--skulinsight-bot=1" }).Bot);
        Assert.Equal(24, CoopLaunch.Parse(new[] { "--skulinsight-name", new string('x', 40) }).Name.Length);
    }
}

[CollectionDefinition("ModData", DisableParallelization = true)]
public class ModDataCollection { }

[Collection("ModData")]
public class ModDataTests
{
    [Fact]
    public void NormalGameKeepsTheOldFolder()
    {
        string root = Path.Combine("C:", "Skul", "BepInEx");
        Assert.Equal(Path.Combine(root, "DamageInsight"), ModData.RootFor(root, 1));
        Assert.Equal(Path.Combine(root, "DamageInsight"), ModData.RootFor(root, 0));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TestCopiesGetTheirOwnFolder(int instance)
    {
        string root = Path.Combine("C:", "Skul", "BepInEx");
        Assert.Equal(Path.Combine(root, "DamageInsight", "CoopTest", "instance" + instance), ModData.RootFor(root, instance));
    }

    [Fact]
    public void RootFollowsTheInjectedBepInExFolderAndIsMadeOnDemand()
    {
        string bepInEx = Path.Combine(Path.GetTempPath(), "SkulInsightTests", "ModData-" + Guid.NewGuid().ToString("N"));
        try
        {
            ModData.BepInExRoot = bepInEx;
            ModData.Use(1);
            Assert.Equal(Path.Combine(bepInEx, "DamageInsight"), ModData.Root);
            Assert.Equal(Path.Combine(bepInEx, "DamageInsight", "Codex"), ModData.Folder("Codex"));

            ModData.Use(3);
            string copy = Path.Combine(bepInEx, "DamageInsight", "CoopTest", "instance3");
            Assert.Equal(copy, ModData.Root);
            Assert.True(Directory.Exists(copy));
            Assert.Equal(Path.Combine(copy, "CrashReports"), ModData.Folder("CrashReports"));
            Assert.Equal(3, ModData.Instance);
            Assert.True(ModData.IsSeparateCopy);

            // A second normal game (it must not write the real data) gets a folder of its own too.
            ModData.Use(1, "duplicate");
            Assert.Equal(Path.Combine(bepInEx, "DamageInsight", "CoopTest", "duplicate"), ModData.Root);
            Assert.True(ModData.IsSeparateCopy);
            ModData.Use(1);
            Assert.False(ModData.IsSeparateCopy);
            // Read-only things stay shared.
            Assert.Equal(Path.Combine(bepInEx, "DamageInsight", "Lang"), ModData.SharedFolder("Lang"));
        }
        finally
        {
            ModData.BepInExRoot = null;
            ModData.Use(1);
            try { Directory.Delete(bepInEx, recursive: true); } catch (IOException) { }
        }
    }
}

public class SaveIsolationTests
{
    private static string TempFolder() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "SkulInsightTests", "Save-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void PathsOfACopy()
    {
        string root = Path.Combine("C:", "Skul", "BepInEx", "DamageInsight", "CoopTest", "instance2");
        string save = SaveIsolation.SavePathFor(root, 2);
        Assert.Equal(Path.Combine(root, "save", "instance2.dat"), save);
        Assert.Equal((save, save + ".tmp", save + ".bak"), SaveIsolation.PathsFor(save));
    }

    [Fact]
    public void SeedCopiesTheRealSaveOnceAndNeverWritesIt()
    {
        string folder = TempFolder();
        try
        {
            string real = Path.Combine(folder, "76561198000000000.dat");
            File.WriteAllText(real, "REAL");
            string copy = Path.Combine(folder, "copy", "save", "instance2.dat");

            Assert.True(SaveIsolation.SeedFrom(real, copy));
            Assert.Equal("REAL", File.ReadAllText(copy));

            File.WriteAllText(copy, "PLAYED IN THE COPY");
            Assert.False(SaveIsolation.SeedFrom(real, copy)); // never overwrites the copy's progress
            Assert.Equal("PLAYED IN THE COPY", File.ReadAllText(copy));
            Assert.Equal("REAL", File.ReadAllText(real));

            Assert.False(SaveIsolation.SeedFrom(Path.Combine(folder, "missing.dat"), Path.Combine(folder, "other.dat")));
            Assert.False(File.Exists(Path.Combine(folder, "other.dat")));
            Assert.False(SaveIsolation.SeedFrom(real, real));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void RedirectPointsTheGamesSaveSystemAtTheCopy()
    {
        string folder = TempFolder();
        try
        {
            string real = Path.Combine(folder, "76561198000000000.dat");
            File.WriteAllText(real, "REAL");
            // The real constructor asks Steam for the account id; the fields are what matters here.
            var system = (Platforms.Steam.SteamSaveSystem)FormatterServices.GetUninitializedObject(typeof(Platforms.Steam.SteamSaveSystem));
            SetField(system, "_saveFilePath", real);
            SetField(system, "_tempFilePath", real + ".tmp");
            SetField(system, "_backupFilePath", real + ".bak");

            string copy = SaveIsolation.SavePathFor(Path.Combine(folder, "instance2"), 2);
            Assert.True(SaveIsolation.Redirect(system, copy));
            Assert.Equal(copy, GetField(system, "_saveFilePath"));
            Assert.Equal(copy + ".tmp", GetField(system, "_tempFilePath"));
            Assert.Equal(copy + ".bak", GetField(system, "_backupFilePath"));
            Assert.Equal("REAL", File.ReadAllText(copy));
            Assert.True(SaveIsolation.IsSafe);

            Assert.True(SaveIsolation.Redirect(system, copy)); // again (Load, Save): nothing changes
            Assert.Equal(copy, GetField(system, "_saveFilePath"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ASaveSystemWithoutTheFieldsIsUnsafe()
    {
        Assert.False(SaveIsolation.Redirect(new object(), Path.Combine(Path.GetTempPath(), "never.dat")));
        Assert.False(SaveIsolation.IsSafe);
        Assert.NotEqual("", SaveIsolation.Problem);
    }

    [Fact]
    public void TheSavePatchIsCheckedButOnlyAppliedByTestCopies()
    {
        Assert.Contains(typeof(SaveIsolationPatches), PatchRegistry.PatchClasses());
        Assert.True(PatchRegistry.AppliedSeparately(typeof(SaveIsolationPatches)));
        // the others: the puppet AI main-loop block (every AI class), applied when co-op starts; and what only a
        // co-op test copy gets (the probe's census of effects and projectiles, the test camera and its backgrounds)
        Assert.Equal(
            new[]
            {
                typeof(DamageInsight.Coop.BossSync.NoPuppetMainLoop), typeof(SaveIsolationPatches),
                typeof(CoopProbe.EffectCensus), typeof(CoopProbe.ProjectileCensus), typeof(CoopShots.CameraOnHost),
                typeof(CoopShots.SameBackground),
            }.OrderBy(t => t.FullName),
            PatchRegistry.PatchClasses().Where(PatchRegistry.AppliedSeparately).OrderBy(t => t.FullName));
    }

    private static void SetField(object target, string name, string value) =>
        HarmonyLib.AccessTools.Field(target.GetType(), name).SetValue(target, value);

    private static string GetField(object target, string name) =>
        (string)HarmonyLib.AccessTools.Field(target.GetType(), name).GetValue(target);
}

public class SingleInstanceTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutexW(IntPtr attributes, bool initialOwner, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenMutexW(uint access, bool inherit, string name);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [Fact]
    public void KnowsUnitysMutexByName()
    {
        Assert.True(SingleInstance.IsSingleInstanceMutex("Southpaw Games-Skul-SingleInstanceMutex-Default"));
        Assert.False(SingleInstance.IsSingleInstanceMutex("SomeOtherMutex"));
        Assert.False(SingleInstance.IsSingleInstanceMutex(null));
    }

    [Fact]
    public void ReleasesAMutexLikeUnitysInThisProcess()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return;
        // A mutex named like Unity's, held only by this process: after Release it is gone.
        string name = "SkulInsightTest-SingleInstanceMutex-" + Guid.NewGuid().ToString("N");
        IntPtr mutex = CreateMutexW(IntPtr.Zero, true, name);
        Assert.NotEqual(IntPtr.Zero, mutex);

        Assert.True(SingleInstance.Release() >= 1);

        const uint synchronize = 0x00100000;
        IntPtr again = OpenMutexW(synchronize, false, name);
        if (again != IntPtr.Zero)
            CloseHandle(again);
        Assert.Equal(IntPtr.Zero, again);
    }
}
