using System;
using System.IO;

namespace DamageInsight.Codex;

/// <summary>
/// Normal mode and Dark Mirror are kept apart for pictures, films and attack reports: bosses and adventurers move
/// differently in the Dark Mirror (extra steps, other timings), even where the move names are the same. What is
/// captured during a Dark Mirror run is stored under "&lt;key&gt;@DM"; the book has a switch between both versions.
/// Progress (kills, damage) stays shared.
/// </summary>
public static class CodexMode
{
    public const string DarkMirrorSuffix = "@DM";

    /// <summary>Whether the current run is a Dark Mirror run.</summary>
    public static bool DarkMirrorNow
    {
        get
        {
            try { return Data.GameData.HardmodeProgress.hardmode; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Where an entry's captures of one mode are stored ("LeianaShortHair" / "LeianaShortHair@DM").</summary>
    public static string StorageKey(string key, bool darkMirror) => darkMirror ? key + DarkMirrorSuffix : key;

    /// <summary>The storage key for what is captured right now.</summary>
    public static string Current(string key) => StorageKey(key, DarkMirrorNow);

    /// <summary>Whether anything (pictures or films) was captured for this entry in that mode.</summary>
    public static bool HasCaptures(string key, bool darkMirror)
    {
        string storage = StorageKey(key, darkMirror);
        return CodexAnimations.Has(storage) || File.Exists(Path.Combine(CodexAnimations.ReplayFolderOf(storage), "animations.json"));
    }
}
