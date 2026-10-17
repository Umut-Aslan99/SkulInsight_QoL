using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Lang;
using DamageInsight.Recording;

namespace DamageInsight.SaveSlots;

/// <summary>
/// Save slots (docs/SAVE_SLOTS.md): every slot is a whole game of its own (story, rescued NPCs, the Witch, Dark Mirror,
/// currencies, the run in progress) with its own Codex. The slot being played uses the game's own save file and the
/// Codex folder; the others wait in BepInEx/DamageInsight/SaveSlots/slotN: save.json (the game's save data) and
/// Codex/ (that slot's Codex folder). Plain logic, no Unity.
/// </summary>
public static class SlotStore
{
    public const int MaxSlots = 5;

    /// <summary>
    /// The daily run's slot (0.13): hidden from the settings page, made from an evened-out copy of the player's game for
    /// each day's run (Daily/DailyProfile). It borrows the Codex of the slot it was entered from.
    /// </summary>
    public const int DailySlot = 9;

    /// <summary>Set by the plugin at start (tests use a temporary folder).</summary>
    public static string Folder = "";

    public static string SlotFolder(int slot) => Path.Combine(Folder, "slot" + slot.ToString(CultureInfo.InvariantCulture));
    public static string SavePath(int slot) => Path.Combine(SlotFolder(slot), "save.json");
    public static string CodexPath(int slot) => Path.Combine(SlotFolder(slot), "Codex");
    private static string StatePath => Path.Combine(Folder, "slots.json");

    /// <summary>The slot being played (1 until the first switch: the game you had before is slot 1).</summary>
    public static int Active { get; private set; } = 1;

    private static readonly Dictionary<int, DateTime> LastPlayed = new();

    /// <summary>Reads slots.json (which slot is played, when the others were left).</summary>
    public static void Load()
    {
        Active = 1;
        LastPlayed.Clear();
        try
        {
            if (!File.Exists(StatePath))
                return;
            var root = GearDoc.ParseNode(File.ReadAllText(StatePath, Encoding.UTF8));
            int active = (int)root.Num("active", 1);
            Active = active is >= 1 and <= MaxSlots || active == DailySlot ? active : 1;
            if (root.Raw("lastPlayed") is Dictionary<string, object> played)
                foreach (var pair in played)
                    if (int.TryParse(pair.Key, out int slot) && pair.Value is string text
                        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                        LastPlayed[slot] = at;
        }
        catch (Exception)
        {
            // An unreadable slots.json: the game being played stays slot 1.
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Folder);
        var sb = new StringBuilder();
        sb.Append("{\n  \"active\": ").Append(Active).Append(",\n  \"lastPlayed\": {");
        bool first = true;
        foreach (var pair in LastPlayed.OrderBy(p => p.Key))
        {
            sb.Append(first ? "\n" : ",\n").Append("    \"").Append(pair.Key).Append("\": ")
                .Append(LogJson.Quote(pair.Value.ToString("o", CultureInfo.InvariantCulture)));
            first = false;
        }
        sb.Append("\n  }\n}\n");
        WriteFile(StatePath, sb.ToString());
    }

    /// <summary>The game now plays <paramref name="slot"/>; the slot left behind was last played <paramref name="leftAt"/>.</summary>
    public static void SetActive(int slot, DateTime leftAt)
    {
        LastPlayed[Active] = leftAt;
        Active = slot;
        Save();
    }

    /// <summary>When a slot that isn't being played was left (null: never, or it is the one being played).</summary>
    public static DateTime? LastPlayedAt(int slot) => slot != Active && LastPlayed.TryGetValue(slot, out var at) ? at : null;

    /// <summary>Whether the slot holds a game (the slot being played always does).</summary>
    public static bool HasGame(int slot) => slot == Active || File.Exists(SavePath(slot));

    /// <summary>A waiting slot's game save (the game's save data as JSON), or null when the slot is empty.</summary>
    public static string ReadSave(int slot) => File.Exists(SavePath(slot)) ? File.ReadAllText(SavePath(slot), Encoding.UTF8) : null;

    public static void WriteSave(int slot, string json)
    {
        Directory.CreateDirectory(SlotFolder(slot));
        WriteFile(SavePath(slot), json);
    }

    /// <summary>Empties a slot that isn't being played (its game save and its Codex).</summary>
    public static void Clear(int slot)
    {
        if (slot == Active)
            throw new InvalidOperationException("The slot being played can't be cleared.");
        if (Directory.Exists(SlotFolder(slot)))
            Directory.Delete(SlotFolder(slot), recursive: true);
        LastPlayed.Remove(slot);
        Save();
    }

    /// <summary>Copies a folder with everything in it (a Codex folder into a slot).</summary>
    public static void CopyFolder(string from, string to)
    {
        if (Directory.Exists(to))
            Directory.Delete(to, recursive: true);
        if (!Directory.Exists(from))
            return;
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), overwrite: true);
    }

    /// <summary>Writes through a temporary file, so a crash never leaves half a file.</summary>
    private static void WriteFile(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }
}

/// <summary>What the settings page says about a slot, from the game's save data (key, value as text). Plain logic.</summary>
public static class SlotSummary
{
    /// <summary>The value of the save's Data object with this key (the game's keys ignore case), or null.</summary>
    public delegate string Lookup(string key);

    private static int Int(Lookup value, string key) =>
        int.TryParse(value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static bool Bool(Lookup value, string key) => string.Equals(value(key), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The slot's lines for the help panel. <paramref name="place"/> names where a run stands (chapter, stage, room);
    /// <paramref name="witchLevels"/> is the sum of the Witch's upgrade levels.
    /// </summary>
    public static List<string> Lines(int slot, bool playing, DateTime? lastPlayed, Lookup value, int witchLevels,
        Func<int, int, int, string> place)
    {
        var lines = new List<string> { playing ? Loc.F("Slot {0} (playing now)", slot) : Loc.F("Slot {0}", slot) };
        if (!Bool(value, "Generic/tutorialPlayed") && Int(value, "Progress/deaths") == 0 && !Bool(value, "Save/hasSave"))
        {
            lines.Add(Loc.T("A new game: the tutorial isn't done yet."));
            return lines;
        }
        lines.Add(Bool(value, "Save/hasSave")
            ? Loc.F("Run in progress: {0}", place(Int(value, "Save/chapterIndex"), Int(value, "Save/stageIndex"), Int(value, "Save/pathIndex")))
            : Loc.T("No run in progress."));
        lines.Add(Bool(value, "Generic/normalEnding") ? Loc.T("Story: the ending was reached.") : Loc.T("Story: the ending wasn't reached yet."));
        int rescued = new[] { "fox", "ogre", "druid", "deathknight" }.Count(npc => Bool(value, $"Progress/{npc}Rescued"));
        lines.Add(Loc.F("NPCs rescued: {0} of 4", rescued));
        lines.Add(Loc.F("Witch upgrades: {0} levels", witchLevels));
        lines.Add(Loc.F("Dark quartz: {0}", Int(value, "Currency/darkQuartz/balance")));
        if (Int(value, "HardmodeProgress/clearedCount") > 0)
            lines.Add(Loc.F("Dark Mirror: cleared up to level {0}", Int(value, "HardmodeProgress/clearedLevel")));
        lines.Add(Loc.F("Deaths: {0}", Int(value, "Progress/deaths")));
        if (lastPlayed != null)
            lines.Add(Loc.F("Last played: {0}", lastPlayed.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        return lines;
    }

    /// <summary>The sum of the Witch's upgrade levels (keys "Progress/WitchMastery/&lt;tree&gt;/&lt;n&gt;").</summary>
    public static int WitchLevels(IEnumerable<(string key, string value)> values) =>
        values.Where(v => v.key.StartsWith("Progress/WitchMastery/", StringComparison.OrdinalIgnoreCase))
            .Sum(v => int.TryParse(v.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0);
}
