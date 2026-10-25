using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.Achievements;

/// <summary>How a slot got an achievement.</summary>
public enum EarnedHow
{
    Run,    // the game gave it while you played (in a run or in the castle)
    Save,   // the slot's save proved it (a slot older than 0.14: a boss's stage reached, a Dark Mirror level cleared, ...)
    Steam,  // taken over from the Steam account (once, in slot 1)
}

public sealed class EarnedAchievement
{
    public string Id = "";
    public DateTime At;       // UTC
    public EarnedHow How;
    public string Run = "";   // the run it came in (RunRecord.Id), "" outside a run
}

/// <summary>
/// The achievements of the save slot being played (0.14, V013_SPEC part C), in Codex/achievements.json, so they move
/// with the slot like the rest of the Codex: what was earned (when, how, in which run), which veils were lifted by
/// holding, whether the save's proofs ran and whether Steam's achievements were taken over. Steam gives an
/// achievement only once; a slot gets it again. Plain logic, no Unity.
/// </summary>
public static class AchievementStore
{
    public const int Format = 1;

    /// <summary>The Codex folder (set by the plugin; tests use a temporary folder).</summary>
    public static string Folder = "";

    private static string FilePath => Path.Combine(Folder, "achievements.json");

    private static Dictionary<string, EarnedAchievement> _earned;
    private static readonly HashSet<string> Revealed = new();
    private static bool _proofsChecked;
    private static DateTime? _steamTaken;

    /// <summary>Increases on every change (the book redraws).</summary>
    public static int Version { get; private set; }

    private static Dictionary<string, EarnedAchievement> Earned
    {
        get
        {
            if (_earned == null)
                Load();
            return _earned;
        }
    }

    public static bool IsEarned(string id) => id != null && Earned.ContainsKey(id);

    public static EarnedAchievement Get(string id) => id != null && Earned.TryGetValue(id, out var earned) ? earned : null;

    public static IEnumerable<EarnedAchievement> All => Earned.Values;

    /// <summary>How many of the catalog's achievements this slot has (all, or Steam's only).</summary>
    public static int Count(bool steamOnly = false) =>
        AchievementCatalog.All.Count(a => (!steamOnly || a.OnSteam) && Earned.ContainsKey(a.Id));

    /// <summary>The achievements earned in a run, in the order they came.</summary>
    public static List<EarnedAchievement> OfRun(string runId) =>
        string.IsNullOrEmpty(runId) ? new List<EarnedAchievement>() : Earned.Values.Where(e => e.Run == runId).OrderBy(e => e.At).ToList();

    /// <summary>Records an achievement; true when the slot didn't have it (the notice shows then).</summary>
    public static bool Earn(string id, DateTime atUtc, EarnedHow how, string runId = "")
    {
        if (string.IsNullOrEmpty(id) || Earned.ContainsKey(id))
            return false;
        Earned[id] = new EarnedAchievement { Id = id, At = atUtc, How = how, Run = runId ?? "" };
        Changed();
        return true;
    }

    public static bool IsRevealed(string id)
    {
        _ = Earned;
        return id != null && Revealed.Contains(id);
    }

    /// <summary>A veil lifted by holding the mouse button on it (stays lifted in this slot).</summary>
    public static void Reveal(string id)
    {
        _ = Earned;
        if (Revealed.Add(id))
            Changed();
    }

    public static bool ProofsChecked
    {
        get
        {
            _ = Earned;
            return _proofsChecked;
        }
    }

    public static void MarkProofsChecked()
    {
        _ = Earned;
        if (_proofsChecked)
            return;
        _proofsChecked = true;
        Changed();
    }

    /// <summary>When Steam's achievements were taken over into this slot (null: not yet).</summary>
    public static DateTime? SteamTaken
    {
        get
        {
            _ = Earned;
            return _steamTaken;
        }
    }

    public static void MarkSteamTaken(DateTime atUtc)
    {
        _ = Earned;
        _steamTaken = atUtc;
        Changed();
    }

    /// <summary>Save slot switch: the next slot's Codex folder is in place; its file is read afresh.</summary>
    public static void ForgetSlot()
    {
        _earned = null;
        Revealed.Clear();
        _proofsChecked = false;
        _steamTaken = null;
        Version++;
    }

    private static void Changed()
    {
        Version++;
        Save();
    }

    // ------------------------------------------------------------------ the file

    private static void Load()
    {
        _earned = new Dictionary<string, EarnedAchievement>();
        Revealed.Clear();
        _proofsChecked = false;
        _steamTaken = null;
        try
        {
            if (!File.Exists(FilePath))
                return;
            var root = GearDoc.ParseNode(File.ReadAllText(FilePath, Encoding.UTF8));
            _proofsChecked = root.Bool("proofsChecked");
            _steamTaken = Date(root.Str("steamTaken"));
            if (root.Raw("earned") is Dictionary<string, object> earned)
                foreach (var pair in earned)
                {
                    if (pair.Value is not Dictionary<string, object> fields)
                        continue;
                    _earned[pair.Key] = new EarnedAchievement
                    {
                        Id = pair.Key,
                        At = Date(fields.TryGetValue("at", out var at) ? at as string : null) ?? DateTime.MinValue,
                        How = (fields.TryGetValue("how", out var how) ? how as string : null) switch
                        {
                            "save" => EarnedHow.Save,
                            "steam" => EarnedHow.Steam,
                            _ => EarnedHow.Run,
                        },
                        Run = fields.TryGetValue("run", out var run) ? run as string ?? "" : "",
                    };
                }
            if (root.Raw("revealed") is List<object> revealed)
                foreach (var id in revealed.OfType<string>())
                    Revealed.Add(id);
        }
        catch (Exception)
        {
            // An unreadable file: kept as .bak, the slot starts again (the save's proofs and the game give most back).
            try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch (Exception) { /* nothing to keep */ }
            _earned.Clear();
            Revealed.Clear();
        }
    }

    private static DateTime? Date(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at : null;

    private static string Iso(DateTime at) => at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    internal static string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"format\": ").Append(Format)
          .Append(",\n  \"proofsChecked\": ").Append(_proofsChecked ? "true" : "false")
          .Append(",\n  \"steamTaken\": ").Append(LogJson.Quote(_steamTaken.HasValue ? Iso(_steamTaken.Value) : ""))
          .Append(",\n  \"earned\": {");
        bool first = true;
        foreach (var e in Earned.Values.OrderBy(e => AchievementCatalog.Get(e.Id)?.Order ?? int.MaxValue).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            sb.Append(first ? "\n" : ",\n").Append("    ").Append(LogJson.Quote(e.Id)).Append(": {\"at\": ").Append(LogJson.Quote(Iso(e.At)))
              .Append(", \"how\": ").Append(LogJson.Quote(e.How.ToString().ToLowerInvariant()));
            if (e.Run.Length > 0)
                sb.Append(", \"run\": ").Append(LogJson.Quote(e.Run));
            sb.Append('}');
            first = false;
        }
        sb.Append(first ? "}" : "\n  }").Append(",\n  \"revealed\": [")
          .Append(string.Join(", ", Revealed.OrderBy(id => id, StringComparer.Ordinal).Select(LogJson.Quote))).Append("]\n}\n");
        return sb.ToString();
    }

    private static void Save()
    {
        if (string.IsNullOrEmpty(Folder))
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(temp, FilePath);
        }
        catch (Exception)
        {
            // Kept in memory; the next change tries again.
        }
    }
}
