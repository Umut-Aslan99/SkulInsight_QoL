using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DamageInsight.Coop;

/// <summary>
/// What each game of a co-op test does, written down so the two games can be compared without watching them
/// (Umut 2026-11-03: "Your test shall log things that happen, so its almost like you can see what happens"): every
/// enemy move (name, attack, target), every hit (who, how much, health before → after) and what the other side
/// received of it, deaths, waves, rooms, hits on players, characters that aren't synced (bosses) and puppets that move
/// on their own. One JSON line per event, times on the host's clock (both files line up), in
/// `<copy data>/CoopTrace/<start>_<host|client>.jsonl`. In test copies from tools/coop/start-instances.ps1, and in a
/// player's game with the setting Coop/DetailedLog (for a bug report; newest 10 files, 50 MB each at most);
/// tools/coop/compare-trace.py puts the host's and a client's file side by side and lists what differs.
/// </summary>
internal static class CoopTrace
{
    private static StreamWriter _file;
    private static readonly StringBuilder Line = new(256);
    private static Func<double> _clock = () => 0;
    private static string _side = "";
    private static double _nextFlush;

    public static bool On => _file != null;

    /// <summary>The trace file's path without ".jsonl" (the probe and the test screenshots sit next to it), or null.</summary>
    public static string BasePath { get; private set; }

    /// <summary>A player's own game (the setting Coop/DetailedLog, LG.md): the newest <see cref="KeepFiles"/> traces
    /// are kept and one stops at <see cref="MaxBytes"/>. Real traces are 0.5-3 MB per chapter (CoopRuns, 2026-11-16),
    /// so the cap only stops a runaway; it was 10 x 200 MB in LG.md, lowered so a forgotten setting can't fill a disk.</summary>
    public const int KeepFiles = 10;
    public const long MaxBytes = 50L * 1024 * 1024;

    private static long _bytes;
    private static bool _capped;

    public static void Start(string side, Func<double> sessionClock)
    {
        bool launcher = Testing.CoopLaunch.Current.FromLauncher;
        if (_file != null || !(launcher || Plugin.CoopDetailedLog?.Value == true))
            return;
        try
        {
            string folder = ModData.Folder("CoopTrace");
            Directory.CreateDirectory(folder);
            if (!launcher)
                Prune(folder, KeepFiles - 1);
            string path = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_" + side + ".jsonl");
            _file = new StreamWriter(path, false, new UTF8Encoding(false));
            BasePath = path.Substring(0, path.Length - ".jsonl".Length);
            _clock = sessionClock;
            _side = side;
            _bytes = 0;
            _capped = !launcher; // test copies (sweeps of 3 h) are never cut
            Plugin.Log.LogInfo("[Coop] trace: " + path);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] trace not started: " + e.Message);
        }
    }

    /// <summary>One event. <paramref name="kind"/> says what happened; the rest is optional detail.</summary>
    public static void Write(string kind, uint room = 0, int id = -1, string name = null, string what = null,
        double amount = double.NaN, double before = double.NaN, double after = double.NaN, int slot = -1)
    {
        if (_file == null)
            return;
        try
        {
            Line.Clear();
            Line.Append("{\"t\":").Append(_clock().ToString("0.000", CultureInfo.InvariantCulture));
            Line.Append(",\"side\":\"").Append(_side).Append("\",\"kind\":\"").Append(kind).Append('"');
            if (room != 0)
                Line.Append(",\"room\":\"").Append(room.ToString("x8")).Append('"');
            if (id >= 0)
                Line.Append(",\"id\":").Append(id);
            if (name != null)
                Text("name", name);
            if (what != null)
                Text("what", what);
            Number("amount", amount);
            Number("before", before);
            Number("after", after);
            if (slot >= 0)
                Line.Append(",\"slot\":").Append(slot);
            Line.Append('}');
            _file.WriteLine(Line.ToString());
            _bytes += Line.Length + 2;
            if (_capped && _bytes >= MaxBytes)
            {
                _file.WriteLine("{\"kind\":\"trace-full\",\"what\":\"the detailed co-op log stopped at its size limit\"}");
                Plugin.Log.LogInfo("[Coop] trace: stopped at " + MaxBytes / (1024 * 1024) + " MB");
                Stop();
                return;
            }
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now >= _nextFlush)
            {
                _nextFlush = now + 1;
                _file.Flush();
            }
        }
        catch (Exception)
        {
            // a trace never breaks the game
        }
    }

    private static void Text(string key, string value)
    {
        Line.Append(",\"").Append(key).Append("\":\"");
        foreach (char c in value)
        {
            if (c == '"' || c == '\\')
                Line.Append('\\').Append(c);
            else if (c >= ' ')
                Line.Append(c);
        }
        Line.Append('"');
    }

    private static void Number(string key, double value)
    {
        if (!double.IsNaN(value))
            Line.Append(",\"").Append(key).Append("\":").Append(value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> traces of a player's game (host and client files).</summary>
    internal static void Prune(string folder, int keep)
    {
        try
        {
            var old = new DirectoryInfo(folder).GetFiles("*.jsonl")
                .Where(f => f.Name.EndsWith("_host.jsonl", StringComparison.Ordinal) || f.Name.EndsWith("_client.jsonl", StringComparison.Ordinal))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(0, keep));
            foreach (var f in old)
                f.Delete();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] trace: old files not cleaned up: " + e.Message);
        }
    }

    /// <summary>
    /// This game's role changed while the trace is open (matchmaking: queued as a possible host, then matched as a
    /// client; Norman's client trace was named "_host", T.md STATE 13:10): later lines carry the new side, the file is
    /// renamed to it (the report tools pick files by that name) and a "side-changed" line says so.
    /// </summary>
    public static void SwitchSide(string side)
    {
        if (_file == null || string.IsNullOrEmpty(side) || side == _side)
            return;
        string old = _side, from = BasePath + ".jsonl";
        _side = side;
        try
        {
            string to = (BasePath.EndsWith("_" + old, StringComparison.Ordinal)
                ? BasePath.Substring(0, BasePath.Length - old.Length) : BasePath + "_") + side;
            _file.Flush();
            _file.Dispose();
            _file = null;
            File.Move(from, to + ".jsonl");
            BasePath = to;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] trace: not renamed for the new side: " + e.Message);
        }
        try
        {
            _file ??= new StreamWriter(BasePath + ".jsonl", true, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] trace: stopped (not reopened after the side change): " + e.Message);
            return;
        }
        Write("side-changed", what: old + " -> " + side);
    }

    /// <summary>Writes what is buffered (the bug report zip reads the file while it is open).</summary>
    public static void Flush()
    {
        try { _file?.Flush(); }
        catch (Exception) { /* a trace never breaks the game */ }
    }

    public static void Stop()
    {
        try
        {
            _file?.Flush();
            _file?.Dispose();
        }
        catch (Exception)
        {
            // closing at quit
        }
        _file = null;
    }

    /// <summary>A spot from the map's origin for the trace: "x 12.3 y -4.5" (always with a dot; German Windows
    /// writes commas).</summary>
    public static string Spot(UnityEngine.Vector2 at) =>
        "x " + at.x.ToString("0.#", CultureInfo.InvariantCulture) + " y " + at.y.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A character's name for the trace: the Codex/combat-log name and its object name.</summary>
    public static string NameOf(Characters.Character c) =>
        c == null ? "" : DamageInsight.Recording.DamageLog.NameOf(c) + " (" + Loadout.GearName(c.name) + ")";
}
