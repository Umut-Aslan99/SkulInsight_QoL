using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DamageInsight.Codex;

/// <summary>
/// One-time fixes of saved Codex data when the move analysis renames moves, so a book filled with an older version
/// shows no moves that don't exist any more. Each fix runs once per game folder (Codex/migrations.txt).
/// </summary>
public static class CodexMigrations
{
    /// <summary>
    /// 0.11: Dark Skul's long and short dashes are one "Dash"; Dark Skul 2's press downs are one "Special move"; the
    /// setup at the start of a fight ("Initialize") is no move. Per boss (both modes): old label → new label, null: gone.
    /// </summary>
    public static readonly Dictionary<string, Dictionary<string, string>> MovesOf011 = new()
    {
        ["DarkSkul1"] = new() { ["Long dash"] = "Dash", ["Short dash"] = "Dash", ["Initialize"] = null },
        ["DarkSkul2"] = new()
        {
            ["Long dash"] = "Dash", ["Short dash"] = "Dash", ["Initialize"] = null,
            ["Press down ready"] = "Special move", ["Press down"] = "Special move", ["Super press down"] = "Special move",
        },
        ["Alexander1"] = new() { ["Initialize"] = null },
    };

    private static string DonePath => Path.Combine(CodexTracker.Folder, "migrations.txt");

    public static void Run()
    {
        try
        {
            var done = File.Exists(DonePath) ? new HashSet<string>(File.ReadAllLines(DonePath)) : new HashSet<string>();
            Directory.CreateDirectory(CodexTracker.Folder);
            if (!done.Contains("0.11 moves"))
            {
                int changed = RenameMoves(MovesOf011);
                File.AppendAllText(DonePath, "0.11 moves" + Environment.NewLine, new UTF8Encoding(false));
                if (changed > 0)
                    Plugin.Log.LogInfo($"Codex: {changed} boss pages updated to the 0.11 move names.");
            }
            if (!done.Contains("0.11 no entrance films"))
            {
                int dropped = DropNonMoveFilms();
                File.AppendAllText(DonePath, "0.11 no entrance films" + Environment.NewLine, new UTF8Encoding(false));
                if (dropped > 0)
                    Plugin.Log.LogInfo($"Codex: removed the entrance, sleep and death films of {dropped} bosses (the book shows only moves).");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: updating saved moves to the new names failed (they stay as they are): {e.Message}");
        }
    }

    /// <summary>
    /// Seen moves get their new name; films of renamed moves are removed (the whole move is filmed anew), so are their
    /// "Refilm" marks. Returns how many bosses changed.
    /// </summary>
    private static int RenameMoves(Dictionary<string, Dictionary<string, string>> renames)
    {
        int changed = 0;
        foreach (var pair in renames)
        {
            bool any = false;
            if (CodexTracker.Progress.Peek("enemy:" + pair.Key) is { } page)
            {
                string normal = Renamed(page.Moves, pair.Value), dark = Renamed(page.MovesDark, pair.Value);
                any = normal != page.Moves || dark != page.MovesDark;
                page.Moves = normal;
                page.MovesDark = dark;
            }
            foreach (var key in new[] { pair.Key, pair.Key + "@DM" })
                any |= FightRecorder.DropFilms(CodexAnimations.ReplayFolderOf(key), pair.Value.ContainsKey);
            if (any)
                changed++;
        }
        if (changed > 0)
            CodexTracker.MarkChanged();
        return changed;
    }

    /// <summary>
    /// Films of what isn't a move (entrances, sleeping, deaths: FightRecorder.IsMove) are removed: the book never shows
    /// them and they are no longer filmed. Returns how many film folders changed.
    /// </summary>
    private static int DropNonMoveFilms()
    {
        string replays = Path.Combine(CodexTracker.Folder, "Replays");
        if (!Directory.Exists(replays))
            return 0;
        return Directory.GetDirectories(replays).Count(folder => FightRecorder.DropFilms(folder, label => !FightRecorder.IsMove(label)));
    }

    /// <summary>A "|"-separated move list with the new names (each once, in the old order); null-mapped moves left out.</summary>
    public static string Renamed(string moves, IReadOnlyDictionary<string, string> renames)
    {
        var result = new List<string>();
        foreach (var move in moves.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = renames.TryGetValue(move, out var to) ? to : move;
            if (name != null && !result.Contains(name))
                result.Add(name);
        }
        return string.Join("|", result);
    }
}
