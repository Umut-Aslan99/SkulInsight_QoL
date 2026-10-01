using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Characters;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// Developer data for balancing the Codex progression: per run, every map with the enemies it holds (its waves,
/// random groups resolved), the enemies that appeared (summons included), the kills and the time spent there.
/// Written to <c>Codex/Debug/runs/run_&lt;start&gt;.json</c> after every map, so a crash loses nothing. Only when
/// the developer setting <c>Developer/CodexRunLog</c> is on.
/// </summary>
public static class RunRecorder
{
    private sealed class MapRecord
    {
        public string Chapter, Map, MapType;
        public int Stage;
        public float Started, Seconds;
        public readonly Dictionary<string, int> Planned = new(), Appeared = new(), Kills = new();
    }

    private static readonly List<MapRecord> Maps = new();
    private static MapRecord _current;
    private static string _file;
    private static bool _darkMirror;
    private static int _level;

    private static bool On => Plugin.CodexRunLog?.Value == true;

    /// <summary>A map's enemy waves were set up (<c>EnemyWaveContainer.Initialize</c>).</summary>
    public static void OnMapStarted(EnemyWaveContainer container)
    {
        if (!On)
            return;
        try
        {
            var chapter = Singleton<Service>.Instance.levelManager.currentChapter;
            if (chapter == null)
                return;
            Close();
            bool castle = chapter.type is Chapter.Type.Castle or Chapter.Type.HardmodeCastle or Chapter.Type.Test or Chapter.Type.Tutorial;
            if (castle)
            {
                // Back in the castle: the run is over (its file is already written).
                Maps.Clear();
                _file = null;
                return;
            }
            if (_file == null)
            {
                _file = Path.Combine(CodexTracker.Folder, "Debug", "runs", $"run_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
                _darkMirror = CodexMode.DarkMirrorNow;
                try { _level = _darkMirror ? Data.GameData.HardmodeProgress.hardmodeLevel : 0; } catch (Exception) { _level = 0; }
            }
            var map = chapter.map;
            _current = new MapRecord
            {
                Chapter = chapter.type.ToString(),
                Stage = chapter.stageIndex,
                Map = map != null ? map.name.Replace("(Clone)", "").Trim() : "?",
                MapType = map != null ? map._type.ToString() : "?",
                Started = Time.realtimeSinceStartup,
            };
            foreach (var enemy in container.GetAllEnemies())
                if (enemy != null && CodexTracker.EntryOf(enemy) is { } entry)
                    Add(_current.Planned, entry.id);
            Maps.Add(_current);
            Write();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: run log: {e.Message}");
        }
    }

    /// <summary>An enemy appeared (once per spawned enemy, summons included).</summary>
    public static void OnAppeared(string id)
    {
        if (On && _current != null)
            Add(_current.Appeared, id);
    }

    public static void OnKilled(string id)
    {
        if (!On || _current == null)
            return;
        Add(_current.Kills, id);
        if (Time.realtimeSinceStartup - _lastWrite > 5f) // a dying player never reaches the next map
            Write();
    }

    private static float _lastWrite;

    private static void Close()
    {
        if (_current == null)
            return;
        _current.Seconds = Time.realtimeSinceStartup - _current.Started;
        Write();
        _current = null;
    }

    private static void Add(Dictionary<string, int> counts, string id) =>
        counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;

    private static void Write()
    {
        if (_file == null)
            return;
        try
        {
            if (_current != null)
                _current.Seconds = Time.realtimeSinceStartup - _current.Started;
            string Counts(Dictionary<string, int> d) =>
                "{" + string.Join(",", d.OrderBy(p => p.Key).Select(p => $"\"{p.Key}\":{p.Value}")) + "}";
            var sb = new StringBuilder();
            sb.Append("{\"darkMirror\":").Append(_darkMirror ? "true" : "false").Append(",\"level\":").Append(_level)
              .Append(",\"maps\":[\n");
            for (int i = 0; i < Maps.Count; i++)
            {
                var m = Maps[i];
                sb.Append(i == 0 ? "" : ",\n")
                  .Append("{\"chapter\":\"").Append(m.Chapter).Append("\",\"stage\":").Append(m.Stage)
                  .Append(",\"map\":\"").Append(m.Map.Replace("\"", "'")).Append("\",\"type\":\"").Append(m.MapType)
                  .Append("\",\"seconds\":").Append(m.Seconds.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"planned\":").Append(Counts(m.Planned))
                  .Append(",\"appeared\":").Append(Counts(m.Appeared))
                  .Append(",\"kills\":").Append(Counts(m.Kills)).Append('}');
            }
            sb.Append("\n]}");
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            File.WriteAllText(_file, sb.ToString(), new UTF8Encoding(false));
            _lastWrite = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: run log not written: {e.Message}");
        }
    }
}
