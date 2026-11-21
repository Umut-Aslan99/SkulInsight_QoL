using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// The very first thing Plugin.Awake does: reads the start options, picks this copy's data folder and, for a co-op
/// test copy (instance 2-4), redirects its save, keeps it from writing the shared config file and lets the next copy
/// start (<see cref="SingleInstance"/>). For the normal game (no --skulinsight- options) it changes nothing.
/// Daily runs are off in test copies (DailyService.Enabled), so a copy never talks to the daily server. Safety net: only
/// one normal game per Windows session may write the real save (a named lock); a second one gets no save at all.
/// </summary>
public static class CoopTesting
{
    /// <summary>Held by the one normal game that may write the real save (for the whole session).</summary>
    private static System.Threading.Mutex _realSaveLock;

    internal static void Start(ConfigFile config)
    {
        var launch = CoopLaunch.Current;
        ModData.Use(launch.Instance);
        if (!launch.IsTestCopy && !TakeRealSaveLock())
        {
            // A second normal game while one runs: only possible once a test copy opened Unity's single instance lock
            // (e.g. Steam restarted a launcher copy without its options). It must not write the real save or data.
            ModData.Use(1, "duplicate");
            SaveIsolation.Block(new Harmony(MyPluginInfo.PLUGIN_GUID + ".cooptest"),
                "another normal Skul is already running and owns the real save");
            try { config.SaveOnConfigSet = false; }
            catch (Exception) { /* only a safety measure */ }
            return;
        }
        if (!launch.FromLauncher)
            return;
        Plugin.Log.LogInfo($"[CoopTest] Start options: {launch}; data folder {ModData.Root}");
        if (launch.IsTestCopy)
        {
            SaveIsolation.Install(new Harmony(MyPluginInfo.PLUGIN_GUID + ".cooptest"), launch.Instance, ModData.Root);
            // Copies share BepInEx/config with the normal game: a copy's setting changes stay in memory only.
            try { config.SaveOnConfigSet = false; }
            catch (Exception e) { Plugin.Log.LogWarning("[CoopTest] config: " + e.Message); }
        }
        // Skul allows one running game (Unity's single instance); a copy started by the launcher lets the next one start.
        int released = SingleInstance.Release();
        // The launcher waits for this file before it starts the next copy (2026-11-20: a fixed 6 s was too short
        // while Windows Defender scanned; the next copy stopped with "Another instance is already running").
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "SkulInsight-released-" + System.Diagnostics.Process.GetCurrentProcess().Id), released.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[CoopTest] the lock's marker file: " + e.Message);
        }
    }

    /// <summary>False when another normal game of this Windows session holds the real save already.</summary>
    private static bool TakeRealSaveLock()
    {
        try
        {
            _realSaveLock = new System.Threading.Mutex(false, "SkulInsight-RealSaveLock", out bool createdNew);
            return createdNew;
        }
        catch (Exception)
        {
            return true; // no lock possible (another OS): the game's own single instance rule still applies
        }
    }
}
