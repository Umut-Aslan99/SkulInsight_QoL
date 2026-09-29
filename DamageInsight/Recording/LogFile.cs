using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DamageInsight.Recording;

/// <summary>
/// Writes every recorded hit to BepInEx\DamageInsight\CombatLogs\&lt;session start&gt;.jsonl (one JSON object per
/// line, including the calculation), so a run can be looked at after the game is closed. Keeps the newest 30 files.
/// </summary>
public static class LogFile
{
    private const int KeepFiles = 30;
    private static StreamWriter _writer;
    private static DateTime _lastFlush;
    private static int _lastRoom = -1;

    public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "DamageInsight", "CombatLogs");

    public static void Attach()
    {
        DamageLog.Recorded += Write;
        UnityEngine.Application.quitting += Close;
    }

    private static void Write(DamageRecord record)
    {
        if (!Plugin.CombatLogToFile.Value)
            return;
        try
        {
            if (_writer == null)
                Open();
            if (record.Room != _lastRoom)
            {
                _lastRoom = record.Room;
                string label = DamageLog.Rooms.FirstOrDefault(r => r.Index == record.Room).Label ?? "";
                _writer.WriteLine($"{{\"room\":{record.Room},\"label\":{LogJson.Quote(label)}}}");
            }
            _writer.WriteLine(LogJson.Line(record));
            if ((DateTime.Now - _lastFlush).TotalSeconds > 2)
            {
                _writer.Flush();
                _lastFlush = DateTime.Now;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Combat log file: {e.Message}");
            Plugin.CombatLogToFile.Value = false;
        }
    }

    private static void Open()
    {
        Directory.CreateDirectory(Folder);
        foreach (var old in new DirectoryInfo(Folder).GetFiles("*.jsonl").OrderByDescending(f => f.Name).Skip(KeepFiles - 1))
            old.Delete();
        string path = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".jsonl");
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        _lastFlush = DateTime.Now;
        Plugin.Log.LogInfo($"Combat log file: {path}");
    }

    private static void Close()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch (Exception)
        {
            // quitting anyway
        }
        _writer = null;
    }
}

/// <summary>The JSON of one hit in the log file. Plain logic, no Unity.</summary>
public static class LogJson
{
    public static string Line(in DamageRecord r)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"t\":").Append(Num(r.Time)).Append(',');
        sb.Append("\"room\":").Append(r.Room).Append(',');
        sb.Append("\"attacker\":").Append(Quote(r.Attacker)).Append(',');
        sb.Append("\"target\":").Append(Quote(r.Target)).Append(',');
        sb.Append("\"source\":").Append(Quote(r.Source.ToString())).Append(',');
        sb.Append("\"attribute\":").Append(Quote(r.Attribute.ToString())).Append(',');
        sb.Append("\"amount\":").Append(Num(r.Amount)).Append(',');
        sb.Append("\"crit\":").Append(r.Critical ? "true" : "false");
        if (r.Trace != null)
        {
            sb.Append(",\"origin\":").Append(Quote(r.Trace.Origin));
            sb.Append(",\"calc\":[");
            bool first = true;
            foreach (var row in r.Trace.Explain())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('[').Append(Quote(row.Label)).Append(',').Append(Quote(row.Effect)).Append(',').Append(Num(row.Total)).Append(']');
            }
            sb.Append(']');
        }
        sb.Append('}');
        return sb.ToString();
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
