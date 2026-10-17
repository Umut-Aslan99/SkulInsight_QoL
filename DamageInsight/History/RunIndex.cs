using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Describe;

namespace DamageInsight.History;

/// <summary>
/// The run list without reading every run file: <c>Codex/Runs/index.json</c> holds every run's summary (no timeline,
/// maps or damage rows). Kept in step with the files (a missing summary is read from its file, a summary without a
/// file is dropped), so copying or deleting run files by hand can't break it. Also decides which old runs shrink
/// (Umut 2026-10-16: starred runs stay forever, old ones make room). Plain logic and file access, no Unity.
/// </summary>
public static class RunIndex
{
    public const string FileName = "index.json";

    /// <summary>The index's format: an older index is built again from the files (2: summaries keep the end bosses).</summary>
    public const int Format = 2;

    public static string ToJson(IEnumerable<RunRecord> summaries) =>
        "{\"format\":" + Format + ",\"runs\":[\n" + string.Join(",\n", summaries.Select(s => s.ToJson().Trim())) + "\n]}\n";

    public static List<RunRecord> FromJson(string json)
    {
        var list = new List<RunRecord>();
        if (string.IsNullOrWhiteSpace(json))
            return list;
        try
        {
            var root = GearDoc.ParseNode(json);
            if (root.Num("format") < Format)
                return list; // made by an older version: built again from the files
            foreach (var n in root.List("runs"))
                if (RunRecord.FromNode(n) is { } run)
                    list.Add(run);
        }
        catch (Exception)
        {
            list.Clear(); // a broken index is rebuilt from the files
        }
        return list;
    }

    /// <summary>The run files' ids in a folder (their names), without the index and temporary files.</summary>
    public static List<string> FileIds(string folder) =>
        !Directory.Exists(folder)
            ? new List<string>()
            : Directory.GetFiles(folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => !id.Equals(Path.GetFileNameWithoutExtension(FileName), StringComparison.OrdinalIgnoreCase))
                .ToList();

    /// <summary>
    /// The index of a folder, brought in step with its files; saved again when it had to change. Newest first.
    /// <paramref name="changed"/> says whether the index was rebuilt or corrected.
    /// </summary>
    public static List<RunRecord> Load(string folder, out bool changed)
    {
        string path = Path.Combine(folder, FileName);
        var index = File.Exists(path) ? FromJson(File.ReadAllText(path, Encoding.UTF8)) : new List<RunRecord>();
        var files = new HashSet<string>(FileIds(folder), StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(index.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        changed = index.RemoveAll(r => !files.Contains(r.Id)) > 0;
        foreach (var id in files.Where(id => !known.Contains(id)))
        {
            try
            {
                if (RunRecord.FromJson(File.ReadAllText(Path.Combine(folder, id + ".json"), Encoding.UTF8)) is { } run)
                {
                    index.Add(run.Summary());
                    changed = true;
                }
            }
            catch (Exception)
            {
                // an unreadable file stays out of the list
            }
        }
        index = Sorted(index);
        if (changed)
            Save(folder, index);
        return index;
    }

    public static void Save(string folder, IEnumerable<RunRecord> index)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName), temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(index), new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }

    /// <summary>Puts a run's summary into the index (replacing its old one). Returns the index, newest first.</summary>
    public static List<RunRecord> Upsert(List<RunRecord> index, RunRecord run)
    {
        index.RemoveAll(r => r.Id == run.Id);
        index.Add(run.Summary());
        return Sorted(index);
    }

    /// <summary>
    /// The runs to shrink: everything after the newest <paramref name="keepFull"/> finished runs that isn't starred,
    /// in progress or shrunk already.
    /// </summary>
    public static List<string> ToShrink(IEnumerable<RunRecord> runs, int keepFull) =>
        Sorted(runs)
            .Where(r => r.Result != RunResult.InProgress)
            .Skip(Math.Max(0, keepFull))
            .Where(r => !r.Favourite && !r.Shrunk)
            .Select(r => r.Id)
            .ToList();

    public static List<RunRecord> Sorted(IEnumerable<RunRecord> runs) =>
        runs.OrderByDescending(r => r.Started).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToList();
}
