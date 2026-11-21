using HarmonyLib;
using Level;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Test copies (instance 2-4) start in the castle instead of continuing the run their save holds (it is a copy of the
/// real save, which may be mid-run): an unattended copy in a run gets killed, and the death screen waits for a key a
/// window without focus never gets. Only the copy's own isolated save changes (`hasSave` off, as after a run);
/// never the normal game, never a copy whose save isolation failed. [DECISION] 2026-11-03 for the phase-0 tests.
/// </summary>
[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.LoadGame))]
public static class CastleStart
{
    private static void Prefix()
    {
        if (!CoopLaunch.Current.IsTestCopy || !SaveIsolation.IsSafe || SaveIsolation.IsolatedPath == null)
            return;
        if (CoopLaunch.Current.TestFreshWorld)
        {
            // --skulinsight-test-fresh-world (COOP_LOG 62): this copy's world flags as in a young save (no scene seen,
            // no Darktech, nobody met or rescued), so a test shows whether the host's world is used (HostWorld)
            int cleared = 0;
            foreach (var (_, data) in HostWorld.Flags())
                if (data != null && data.value)
                {
                    data.value = false;
                    cleared++;
                }
            Plugin.Log.LogInfo($"[CoopTest] fresh world: {cleared} flag(s) of this copy's save cleared");
        }
        var save = Data.GameData.Save.instance;
        if (save == null || !save.hasSave)
            return;
        save.hasSave = false;
        Plugin.Log.LogInfo("[CoopTest] this test copy starts in the castle (the run in its save is left)");
    }
}
