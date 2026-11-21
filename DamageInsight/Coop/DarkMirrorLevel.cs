using System;
using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// The Dark Mirror level in co-op (Umut 2026-11-07, COOP_LOG 35: "the host's DM level for both games"). The level
/// (`HardmodeProgress.hardmodeLevel`, 39 reads: enemy stats, curses, rewards) must be the same on every game, or the
/// enemies differ. A client borrows the host's level for a DM run (RunStart, or a catch-up into a DM run: HostRoom):
/// - its own choice comes back in a castle and when co-op ends, and never goes into its save (SaveAll writes its own);
/// - its own progress still counts, but only up to its own next level: a client whose best is 3 that clears the
///   host's level 8 unlocks its level 4 (what clearing its own highest level would unlock), never 8.
/// The host plays at its own level as usual.
/// </summary>
internal static class DarkMirrorLevel
{
    public const int FirstRun = 9, LastRun = 14; // Chapter.Type.HardmodeChapter1 .. HardmodeChapter6

    private static int? _own;          // this game's own level choice while the host's is borrowed
    private static int _clearedBefore; // its cleared level when it borrowed
    private static int _played;        // the host's level it plays

    public static bool Borrowed => _own.HasValue;

    /// <summary>A Dark Mirror run's chapter (not its castle).</summary>
    public static bool IsRunChapter(int chapter) => chapter >= FirstRun && chapter <= LastRun;

    /// <summary>The cleared level a client keeps while it plays the host's level <paramref name="played"/>:
    /// at most one above its cleared level from before (its own next level), never above the level played.</summary>
    public static int CapCleared(int cleared, int clearedBefore, int played) =>
        Math.Min(cleared, Math.Max(clearedBefore, Math.Min(played, clearedBefore + 1)));

    /// <summary>Client: the run is at the host's level (kept: its own choice from the first borrow).</summary>
    public static void Borrow(int hostLevel)
    {
        int level = Math.Max(0, Math.Min(Data.GameData.HardmodeProgress.maxLevel, hostLevel));
        if (!_own.HasValue)
        {
            _own = Data.GameData.HardmodeProgress.hardmodeLevel;
            _clearedBefore = Data.GameData.HardmodeProgress.clearedLevel;
        }
        if (Data.GameData.HardmodeProgress.hardmodeLevel != level)
            Plugin.Log.LogInfo($"[Coop] Dark Mirror level {level} (the host's) for this run; this game's own {_own.Value} comes back in the castle");
        Data.GameData.HardmodeProgress.hardmodeLevel = _played = level;
        CoopTrace.Write("dm-level", what: $"host {level}, own {_own.Value}, cleared {_clearedBefore}");
    }

    /// <summary>This game's own level choice again (in a castle, when co-op ends).</summary>
    public static void Restore()
    {
        if (!_own.HasValue)
            return;
        int own = _own.Value;
        _own = null;
        if (Data.GameData.HardmodeProgress.hardmodeLevel != own)
            Plugin.Log.LogInfo($"[Coop] Dark Mirror level back to this game's own {own}");
        Data.GameData.HardmodeProgress.hardmodeLevel = own;
    }

    /// <summary>The save gets this game's own level choice, never the borrowed one (a quit mid-run keeps it).</summary>
    [HarmonyPatch(typeof(Data.GameData.HardmodeProgress), nameof(Data.GameData.HardmodeProgress.SaveAll))]
    public static class SaveOwnLevel
    {
        private static void Prefix(out int? __state)
        {
            __state = null;
            if (!_own.HasValue)
                return;
            __state = Data.GameData.HardmodeProgress.hardmodeLevel;
            Data.GameData.HardmodeProgress.hardmodeLevel = _own.Value;
        }

        private static void Postfix(int? __state)
        {
            if (__state.HasValue)
                Data.GameData.HardmodeProgress.hardmodeLevel = __state.Value;
        }
    }

    /// <summary>The game's level counters (the ending's "cleared +1"): capped at this game's own next level.</summary>
    [HarmonyPatch(typeof(Runnables.ControlHardmodeLevel), nameof(Runnables.ControlHardmodeLevel.Run))]
    public static class CapProgress
    {
        private static void Postfix()
        {
            if (!_own.HasValue)
                return;
            int cleared = Data.GameData.HardmodeProgress.clearedLevel;
            int capped = CapCleared(cleared, _clearedBefore, _played); // the level now may be "next" already
            if (capped == cleared)
                return;
            Data.GameData.HardmodeProgress.clearedLevel = capped;
            Data.GameData.HardmodeProgress.SaveAll();
            Plugin.Log.LogInfo($"[Coop] Dark Mirror progress: cleared level {capped} (this game's next level), not the host's {cleared}");
        }
    }
}
