using System;
using System.IO;

namespace DamageInsight.Codex;

/// <summary>
/// "Reset Codex" on the settings page: everything the Codex has gathered (progress, films, posed pictures, move
/// hints, AI reports) is deleted at the next game start, before anything reads it, so nothing that is loaded or
/// being filmed has to be cleared while the game runs. The move notes are part of the mod and stay. The request is a
/// file (reset-requested.txt) and can be withdrawn until the game restarts.
/// </summary>
public static class CodexReset
{
    private static string MarkerPath => Path.Combine(CodexTracker.Folder, "reset-requested.txt");

    public static bool Requested => File.Exists(MarkerPath);

    public static void Request()
    {
        Directory.CreateDirectory(CodexTracker.Folder);
        File.WriteAllText(MarkerPath, DateTime.Now.ToString("s"));
        Plugin.Log.LogInfo("Codex: reset requested, it happens at the next game start.");
    }

    public static void Withdraw()
    {
        if (File.Exists(MarkerPath))
            File.Delete(MarkerPath);
        Plugin.Log.LogInfo("Codex: reset withdrawn.");
    }

    /// <summary>At startup, before the Codex reads anything: deletes the gathered data if a reset was requested.</summary>
    public static void RunIfRequested()
    {
        try
        {
            if (!Requested)
                return;
            int removed = Delete(CodexTracker.Folder);
            File.Delete(MarkerPath);
            Plugin.Log.LogInfo($"Codex: reset done ({removed} files and folders deleted; the move notes are part of the mod and stay).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: the reset failed, the Codex stays as it is: {e.Message}");
        }
    }

    /// <summary>
    /// Deletes the gathered data in a Codex folder: progress.json (and its copies), Replays, Animations, Hints, Debug
    /// and backup folders ("_..."); keeps migrations.txt and the reset request itself. Returns how many were deleted.
    /// </summary>
    public static int Delete(string folder)
    {
        int removed = 0;
        foreach (var dir in Directory.GetDirectories(folder))
        {
            string name = Path.GetFileName(dir);
            if (name is "Replays" or "Animations" or "Hints" or "Debug" || name.StartsWith("_"))
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
        }
        foreach (var file in Directory.GetFiles(folder, "progress.json*"))
        {
            File.Delete(file);
            removed++;
        }
        return removed;
    }
}
