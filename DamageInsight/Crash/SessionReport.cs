using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DamageInsight.Crash;

/// <summary>One distinct error of a session (the same message at the same place counts up).</summary>
public sealed class ErrorRecord
{
    public int Number;
    /// <summary>"Exception", "Error", "Assert", "Logged" (a BepInEx error line), "Caught" (SkulInsight's Guard).</summary>
    public string Kind = "";
    public string Message = "";
    public List<string> Frames = new();
    public Classification Class;
    public int Count;
    public DateTime First, Last;
    /// <summary>The newest occurrences (to see which errors came right before an incident).</summary>
    public readonly List<DateTime> Recent = new();

    /// <summary>Caught by SkulInsight itself: the game went on without that feature, it can't stop the game.</summary>
    public bool Caught => Kind == "Caught";

    public bool HappenedBetween(DateTime from, DateTime to) => Recent.Any(t => t >= from && t <= to);
}

/// <summary>
/// The crash report of one game session (0.12.2, after a colleague's black screen we could not explain): a text file in
/// BepInEx/DamageInsight/CrashReports written as things happen, so it survives a hard crash. Header, timeline (events,
/// every distinct error with its stack trace, incidents), and at the end a verdict: whose code the errors before an
/// incident were in. A session that never wrote its end is finished at the next start (<see cref="FinishPrevious"/>).
/// Plain logic (no Unity); thread-safe (Unity reports errors from any thread).
/// </summary>
public sealed class SessionReport
{
    public const string EndMarker = "=== Session end";
    /// <summary>The incident the next start tells the player about (unless it resolved itself).</summary>
    public const string StuckTitle = "the screen stayed black";
    private const string IncidentMark = "!!! INCIDENT: ", ResolvedMark = "INCIDENT RESOLVED (";
    public const string UnexpectedMarker = "=== Session ended unexpectedly";
    public const int KeepReports = 20;
    /// <summary>Errors this long before an incident count as "right before it".</summary>
    public static readonly TimeSpan IncidentWindow = TimeSpan.FromSeconds(60);
    private const int MaxDistinctErrors = 300;
    private const int LoggedOccurrences = 3;
    private const long MaxFileBytes = 5L * 1024 * 1024;
    private const int MaxFrames = 30;

    private readonly object _lock = new();
    private readonly ErrorClassifier _classifier;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, ErrorRecord> _errors = new();
    private sealed class IncidentEntry
    {
        public DateTime Time, From;
        public string Title, Resolved;
    }

    private readonly List<IncidentEntry> _incidents = new();
    private StreamWriter _writer;
    private long _written;
    private DateTime _lastSnapshot = DateTime.MinValue, _lastFlush = DateTime.MinValue;
    private bool _snapshotDue;
    private int _skipped;

    public string Folder { get; }
    public string FilePath { get; private set; }
    public DateTime Started { get; }
    public bool Ended { get; private set; }

    public SessionReport(string folder, ErrorClassifier classifier, Func<DateTime> clock = null)
    {
        Folder = folder;
        _classifier = classifier;
        _clock = clock ?? (() => DateTime.Now);
        Started = _clock();
    }

    public static string FileName(DateTime start) => start.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".txt";
    public static string SnapshotPath(string reportPath) => Path.ChangeExtension(reportPath, ".summary.txt");

    /// <summary>Creates the file (old reports beyond <see cref="KeepReports"/> go) and writes the header.</summary>
    public void Open(string header)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Folder);
            Prune(Folder, KeepReports - 1);
            string name = FileName(Started);
            FilePath = Path.Combine(Folder, name);
            for (int i = 2; File.Exists(FilePath); i++)
                FilePath = Path.Combine(Folder, Path.GetFileNameWithoutExtension(name) + "_" + i + ".txt");
            _writer = new StreamWriter(FilePath, append: false, new UTF8Encoding(false));
            Write($"SkulInsight QoL session report (started {Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)})");
            Write("Send this file with a bug report. The verdict (whose code the errors were in) is at the end.");
            Write("");
            Write(header.TrimEnd());
            Write("");
            Write("--- Timeline ---");
            Flush();
            WriteSnapshot();
        }
    }

    /// <summary>A timeline line ("map loaded: ...").</summary>
    public void Event(string text)
    {
        lock (_lock)
        {
            if (_writer == null || Ended)
                return;
            Write($"{Stamp(_clock())}  {text}");
            FlushSoon();
        }
    }

    /// <summary>
    /// An error. The first occurrence is written with its stack trace, the next few as one line, the rest only counted.
    /// <paramref name="forced"/>: the blame is known (SkulInsight's own Guard caught it at <paramref name="forcedDetail"/>).
    /// </summary>
    public ErrorRecord Error(string kind, string message, string stackTrace, Blame? forced = null, string forcedDetail = null)
    {
        lock (_lock)
        {
            if (_writer == null || Ended)
                return null;
            DateTime now = _clock();
            string firstLine = FirstLine(message);
            var frames = ErrorClassifier.Frames(stackTrace).Take(MaxFrames).ToList();
            string key = kind + "|" + firstLine + "|" + string.Join("|", frames.Take(4));
            bool isNew = !_errors.TryGetValue(key, out var record);
            if (isNew)
            {
                if (_errors.Count >= MaxDistinctErrors)
                {
                    _skipped++;
                    return null;
                }
                var c = _classifier.Classify(stackTrace);
                if (forced.HasValue)
                    c = new Classification(forced.Value, forcedDetail ?? c.Where, c.Where);
                record = new ErrorRecord
                {
                    Number = _errors.Count + 1, Kind = kind, Message = Truncate(firstLine, 400), Frames = frames, Class = c, First = now,
                };
                _errors[key] = record;
            }
            record.Count++;
            record.Last = now;
            record.Recent.Add(now);
            if (record.Recent.Count > 5)
                record.Recent.RemoveAt(0);

            if (isNew)
            {
                Write($"{Stamp(now)}  ERROR #{record.Number} [{Label(record)}] {kind}: {record.Message}");
                foreach (string frame in frames)
                    Write("              at " + frame);
                if (record.Class.Blame != Blame.Game && record.Class.Detail.Length > 0)
                    Write("              -> " + Explain(record));
                Flush();
                WriteSnapshot();
            }
            else if (record.Count <= LoggedOccurrences)
            {
                Write($"{Stamp(now)}  ERROR #{record.Number} again (x{record.Count})");
                FlushSoon();
                _snapshotDue = true;
            }
            else
            {
                _snapshotDue = true;
            }
            return record;
        }
    }

    /// <summary>Something went visibly wrong (the screen stayed black). Written at once, with the verdict for it.</summary>
    public string Incident(string title, string details, DateTime? since = null)
    {
        lock (_lock)
        {
            DateTime now = _clock();
            // Errors from a little before the trouble began (the screen went black) count, else the last minute.
            DateTime from = since.HasValue ? since.Value - TimeSpan.FromSeconds(10) : now - IncidentWindow;
            _incidents.Add(new IncidentEntry { Time = now, Title = title, From = from });
            if (_writer == null || Ended)
                return "";
            var verdict = VerdictFor(from, now);
            Write("");
            Write($"{Stamp(now)}  {IncidentMark}{title}");
            foreach (string line in (details ?? "").TrimEnd().Split('\n'))
                Write("              " + line.TrimEnd());
            Write($"              VERDICT ({title}): {verdict.Text}");
            Write("");
            Flush();
            WriteSnapshot();
            return verdict.Text;
        }
    }

    /// <summary>The newest summary on disk every so often (what <see cref="FinishPrevious"/> uses after a hard crash).</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (_writer == null || Ended)
                return;
            DateTime now = _clock();
            if ((_snapshotDue && now - _lastSnapshot > TimeSpan.FromSeconds(2)) || now - _lastSnapshot > TimeSpan.FromSeconds(30))
                WriteSnapshot();
            if (now - _lastFlush > TimeSpan.FromSeconds(2))
                Flush();
        }
    }

    /// <summary>Everything written so far onto the disk now, the summary too (before a crash on purpose).</summary>
    public void Save()
    {
        lock (_lock)
        {
            if (_writer == null || Ended)
                return;
            Flush();
            WriteSnapshot();
        }
    }

    /// <summary>The game closes normally: the summary and the end marker.</summary>
    public void End()
    {
        lock (_lock)
        {
            if (_writer == null || Ended)
                return;
            Ended = true;
            DateTime now = _clock();
            Write("");
            Write($"{EndMarker} (normal) {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ===");
            Write("");
            _writer.Write(Summary(now, ended: true));
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
            TryDelete(SnapshotPath(FilePath));
        }
    }

    /// <summary>Lets the file go without its end, like a crash does (tests).</summary>
    internal void Abandon()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>The last open incident with this title went away by itself (the screen came back: a slow load).</summary>
    public void Resolve(string title, string how)
    {
        lock (_lock)
        {
            var open = _incidents.LastOrDefault(i => i.Title == title && i.Resolved == null);
            if (open == null)
                return;
            open.Resolved = how;
            if (_writer == null || Ended)
                return;
            Write($"{Stamp(_clock())}  {ResolvedMark}{title}): {how}");
            Flush();
            WriteSnapshot();
        }
    }

    /// <summary>Incidents so far that didn't resolve themselves.</summary>
    public int OpenIncidents
    {
        get { lock (_lock) return _incidents.Count(i => i.Resolved == null); }
    }

    // ------------------------------------------------------------------ verdict

    public readonly struct Verdict
    {
        public readonly Blame? Blame; // null: no error right before it
        public readonly string Text;

        public Verdict(Blame? blame, string text)
        {
            Blame = blame;
            Text = text;
        }
    }

    /// <summary>Whose code the uncaught errors between <paramref name="from"/> and <paramref name="to"/> were in.</summary>
    public Verdict VerdictFor(DateTime from, DateTime to)
    {
        lock (_lock)
            return Judge(_errors.Values.Where(e => !e.Caught && e.HappenedBetween(from, to)).ToList());
    }

    /// <summary>The verdict for a set of errors: the worst blame decides (mod, hooked, other mod, game).</summary>
    public static Verdict Judge(IReadOnlyCollection<ErrorRecord> errors)
    {
        if (errors.Count == 0)
            return new Verdict(null, "NO ERROR right before it. Nothing points at a mod; the game may still be loading, or it waits for something " +
                                     "that never came (a game bug without an error message, a slow disk, the PC).");
        var worst = errors.OrderByDescending(e => e.Class.Blame).ThenBy(e => e.First).First();
        int game = errors.Count(e => e.Class.Blame == Blame.Game);
        string others = errors.Count > 1 ? $" ({errors.Count} distinct errors right before it, {game} only in game code)" : "";
        return worst.Class.Blame switch
        {
            Blame.Mod => new Verdict(Blame.Mod, $"LIKELY SKULINSIGHT: its own code is in error #{worst.Number} ({worst.Class.Detail}){others}."),
            Blame.Hooked => new Verdict(Blame.Hooked, $"POSSIBLY SKULINSIGHT: error #{worst.Number} passed through {worst.Class.Detail}, a game method " +
                                                      $"SkulInsight hooks into; SkulInsight's own code is not in it{others}."),
            Blame.OtherMod => new Verdict(Blame.OtherMod, $"LIKELY ANOTHER MOD: error #{worst.Number} is in {worst.Class.Detail}{others}."),
            _ => new Verdict(Blame.Game, $"LIKELY THE GAME ITSELF: the errors right before it are only in the game's own code " +
                                         $"(first: #{worst.Number}, {(worst.Class.Where.Length > 0 ? "at " + worst.Class.Where : worst.Message)}); no mod code is in them{others}."),
        };
    }

    /// <summary>The summary written at the end (or kept as a snapshot until then).</summary>
    public string Summary(DateTime now, bool ended)
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Verdict ===");
            if (_incidents.Count == 0 && ended)
                sb.AppendLine("No freeze was noticed in this session.");
            foreach (var incident in _incidents)
                sb.AppendLine(incident.Resolved == null
                    ? $"VERDICT ({incident.Title}, {Stamp(incident.Time)}): {VerdictFor(incident.From, incident.Time).Text}"
                    : $"Resolved by itself ({incident.Title}, {Stamp(incident.Time)}): {incident.Resolved}");
            if (!ended)
                sb.AppendLine($"VERDICT (the last minute, if the game ends here without its normal end): {VerdictFor(now - IncidentWindow, now).Text}");

            var all = _errors.Values.ToList();
            sb.AppendLine();
            sb.AppendLine($"Errors this session: {all.Count} distinct, {all.Sum(e => e.Count)} in all" +
                          (_skipped > 0 ? $" ({_skipped} more distinct ones not kept)" : "") + ".");
            foreach (Blame blame in new[] { Blame.Mod, Blame.Hooked, Blame.OtherMod, Blame.Game })
            {
                var group = all.Where(e => e.Class.Blame == blame).ToList();
                if (group.Count > 0)
                    sb.AppendLine($"  {Heading(blame)}: {group.Count} distinct, {group.Sum(e => e.Count)} in all" +
                                  (blame == Blame.Mod && group.Any(e => e.Caught) ? $" ({group.Count(e => e.Caught)} caught: the game went on without that feature)" : ""));
            }
            if (all.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("=== Errors (most important first) ===");
                foreach (var e in all.OrderByDescending(e => e.Class.Blame).ThenBy(e => e.Caught).ThenBy(e => e.Number))
                {
                    sb.AppendLine($"#{e.Number} [{Label(e)}] x{e.Count}  first {Stamp(e.First)}  last {Stamp(e.Last)}");
                    sb.AppendLine($"    {e.Kind}: {e.Message}");
                    sb.AppendLine($"    where: {e.Class.Where}");
                    if (e.Class.Blame != Blame.Game)
                        sb.AppendLine("    " + Explain(e));
                }
            }
            if (!ended)
                sb.AppendLine().AppendLine($"(snapshot {Stamp(now)})");
            return sb.ToString();
        }
    }

    private static string Heading(Blame blame) => blame switch
    {
        Blame.Mod => "SkulInsight's own code",
        Blame.Hooked => "game code SkulInsight hooks into",
        Blame.OtherMod => "other mods",
        _ => "game code only",
    };

    private static string Label(ErrorRecord e) => e.Class.Blame switch
    {
        Blame.Mod => e.Caught ? "MOD, caught" : "MOD",
        Blame.Hooked => "HOOKED",
        Blame.OtherMod => "OTHER MOD",
        _ => "GAME",
    };

    private static string Explain(ErrorRecord e) => e.Class.Blame switch
    {
        Blame.Mod when e.Caught => $"SkulInsight caught this in its own code ({e.Class.Detail}); the game went on without that feature.",
        Blame.Mod => $"SkulInsight's code: {e.Class.Detail}",
        Blame.Hooked => $"passed through {e.Class.Detail}, a game method SkulInsight hooks into (its own code is not in the trace)",
        Blame.OtherMod => $"another mod: {e.Class.Detail}",
        _ => "",
    };

    // ------------------------------------------------------------------ the previous session

    /// <summary>What <see cref="FinishPrevious"/> found about the session before this one.</summary>
    public sealed class Previous
    {
        public string FilePath;
        public bool EndedUnexpectedly;
        /// <summary>Black screens that never came back (the next start says so).</summary>
        public int StuckIncidents;
        /// <summary>The last verdict line of the report (for the notice).</summary>
        public string Verdict = "";
    }

    /// <summary>What the game side found about a session that ended unexpectedly (Unity's log, a crash dump).</summary>
    public sealed class Extras
    {
        public readonly List<(string title, string text)> Sections = new();
        /// <summary>The native crash's stack (Unity's "OUTPUTTING STACK TRACE" block), if the game crashed.</summary>
        public string CrashStack;
    }

    /// <summary>The newest report in <paramref name="folder"/> (call it before this session's report exists), or null.</summary>
    public static string NewestReport(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        return new DirectoryInfo(folder).GetFiles("*.txt")
            .Where(f => !f.Name.EndsWith(".summary.txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Looks at the report of the session before this one. If it never wrote its end (a hard crash, a killed game),
    /// the verdict from the crash's stack (if the game crashed), the last summary snapshot and the
    /// <paramref name="extra"/> sections (Unity's log of that session, the crash dump) are added under "ended
    /// unexpectedly". <paramref name="classifier"/> should know this session's hooks (they are the same mod).
    /// </summary>
    public static Previous FinishPrevious(string reportPath, DateTime now, ErrorClassifier classifier = null, Func<DateTime, Extras> extra = null)
    {
        if (string.IsNullOrEmpty(reportPath) || !File.Exists(reportPath))
            return null;
        var newest = new FileInfo(reportPath);
        string text = File.ReadAllText(newest.FullName);
        var result = new Previous
        {
            FilePath = newest.FullName,
            StuckIncidents = Math.Max(0, CountOf(text, IncidentMark + StuckTitle) - CountOf(text, ResolvedMark + StuckTitle)),
        };
        if (text.Contains(EndMarker) || text.Contains(UnexpectedMarker))
        {
            result.Verdict = LastVerdict(text);
            return result;
        }

        result.EndedUnexpectedly = true;
        string snapshotPath = SnapshotPath(newest.FullName);
        string snapshot = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : "(no summary was saved)";
        DateTime started = StartOf(newest.Name) ?? newest.CreationTime;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"{UnexpectedMarker} (found at the next start, {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}) ===");
        var extras = extra?.Invoke(started);
        Verdict? crash = extras?.CrashStack != null ? JudgeCrash(classifier ?? new ErrorClassifier(""), extras.CrashStack) : null;
        sb.AppendLine(crash != null
            ? "The game crashed (Unity wrote a crash report; its stack is below)."
            : "The game closed without its normal end: a crash without a crash report, a freeze that was force-closed, or the process was killed.");
        sb.AppendLine($"Last file write: {newest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}.");
        if (crash != null)
            sb.AppendLine($"VERDICT (the crash, from its stack): {crash.Value.Text}");
        sb.AppendLine();
        sb.Append(snapshot.TrimEnd()).AppendLine();
        foreach (var (title, body) in extras?.Sections ?? new List<(string, string)>())
        {
            sb.AppendLine();
            sb.AppendLine($"=== {title} ===");
            sb.AppendLine((body ?? "").TrimEnd());
        }
        File.AppendAllText(newest.FullName, sb.ToString(), new UTF8Encoding(false));
        TryDelete(snapshotPath);
        result.Verdict = crash?.Text ?? LastVerdict(snapshot);
        return result;
    }

    /// <summary>
    /// Whose code a native crash's stack shows. Unity lists the managed frames as "(Mono JIT Code) Type:Method": those
    /// are classified like an error's trace (our code, a hooked game method, another mod, the game). No managed frame:
    /// the crash is in native code only (the engine, a driver, Windows).
    /// </summary>
    public static Verdict JudgeCrash(ErrorClassifier classifier, string stack)
    {
        const string Jit = "(Mono JIT Code)";
        var managed = new List<string>();
        string firstModule = null;
        foreach (string raw in (stack ?? "").Split('\n'))
        {
            string line = raw.Trim();
            int jit = line.IndexOf(Jit, StringComparison.Ordinal);
            if (jit >= 0)
            {
                managed.Add(line.Substring(jit + Jit.Length).Trim());
                continue;
            }
            int open = line.IndexOf(" (", StringComparison.Ordinal);
            int close = open >= 0 ? line.IndexOf(')', open) : -1;
            if (firstModule == null && line.StartsWith("0x", StringComparison.Ordinal) && close > open)
                firstModule = line.Substring(open + 2, close - open - 2);
        }
        if (managed.Count == 0)
            return new Verdict(Blame.Game, $"LIKELY THE GAME OR THE PC: the crash is in native code ({firstModule ?? "unknown module"}); " +
                                           "no game script and no mod code is on its stack.");
        var c = classifier.Classify(string.Join("\n", managed));
        return c.Blame switch
        {
            Blame.Mod => new Verdict(Blame.Mod, $"LIKELY SKULINSIGHT: its own code is on the crash's stack ({c.Detail})."),
            Blame.Hooked => new Verdict(Blame.Hooked, $"POSSIBLY SKULINSIGHT: the crash's stack goes through {c.Detail}, a game method " +
                                                      "SkulInsight hooks into; SkulInsight's own code is not on it."),
            Blame.OtherMod => new Verdict(Blame.OtherMod, $"LIKELY ANOTHER MOD: {c.Detail} is on the crash's stack."),
            _ => new Verdict(Blame.Game, $"LIKELY THE GAME ITSELF: only the game's own code is on the crash's stack (first: {c.Where})."),
        };
    }

    /// <summary>
    /// From Unity's log of a session: the lines worth reading (exceptions with their next lines, the crash line and
    /// its stack, the last lines) without the crash handler's module list, and the crash's stack on its own.
    /// </summary>
    public static (string excerpt, string crashStack) ReadUnityLog(IReadOnlyList<string> lines)
    {
        bool Module(string l) => l.Contains("SymType:") || l.StartsWith("SymInit:", StringComparison.Ordinal) || l.StartsWith("OS-Version:", StringComparison.Ordinal);
        var keep = new HashSet<int>();
        int stackStart = -1, stackEnd = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string l = lines[i];
            if (l.Contains("Exception"))
                for (int j = i; j < Math.Min(lines.Count, i + 8); j++)
                    keep.Add(j);
            if (l.StartsWith("Crash!!!", StringComparison.Ordinal))
                keep.Add(i);
            if (stackStart < 0 && l.Contains("OUTPUTTING STACK TRACE"))
                stackStart = i;
            if (stackStart >= 0 && stackEnd < 0 && l.Contains("END OF STACKTRACE"))
                stackEnd = i;
        }
        string crashStack = null;
        if (stackStart >= 0)
        {
            int end = stackEnd >= 0 ? stackEnd : Math.Min(lines.Count - 1, stackStart + 80);
            for (int i = stackStart; i <= end; i++)
                keep.Add(i);
            crashStack = string.Join("\n", lines.Skip(stackStart + 1).Take(Math.Max(0, end - stackStart - 1)));
        }
        int tail = 0;
        for (int i = lines.Count - 1; i >= 0 && tail < 40; i--)
            if (!Module(lines[i]))
            {
                keep.Add(i);
                tail++;
            }
        var sb = new StringBuilder();
        int printed = 0, last = -2;
        foreach (int i in keep.OrderBy(i => i))
        {
            if (Module(lines[i]) || printed >= 1500)
                continue;
            if (i != last + 1 && printed > 0)
                sb.AppendLine("...");
            sb.AppendLine(lines[i]);
            last = i;
            printed++;
        }
        return (sb.ToString(), crashStack);
    }

    /// <summary>The session start from a report's file name ("2026-10-15_20-14-03.txt").</summary>
    public static DateTime? StartOf(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length >= 19 && DateTime.TryParseExact(stem.Substring(0, 19), "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start))
            return start;
        return null;
    }

    /// <summary>
    /// The sentence of the last "VERDICT (...): ..." line in a report text: after a hard crash the last minute's, after
    /// a freeze that one's ("" if none).
    /// </summary>
    public static string LastVerdict(string text)
    {
        string last = "";
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("VERDICT (", StringComparison.Ordinal))
                continue;
            int at = line.IndexOf("): ", StringComparison.Ordinal);
            if (at >= 0)
                last = line.Substring(at + 3).Trim();
        }
        return last;
    }

    /// <summary>The Blame a verdict sentence stands for (null: no error).</summary>
    public static Blame? BlameOf(string verdict)
    {
        if (string.IsNullOrEmpty(verdict) || verdict.Contains("NO ERROR"))
            return null;
        if (verdict.Contains("LIKELY SKULINSIGHT"))
            return Blame.Mod;
        if (verdict.Contains("POSSIBLY SKULINSIGHT"))
            return Blame.Hooked;
        if (verdict.Contains("ANOTHER MOD"))
            return Blame.OtherMod;
        return Blame.Game;
    }

    // ------------------------------------------------------------------ helpers

    private static void Prune(string folder, int keep)
    {
        var reports = new DirectoryInfo(folder).GetFiles("*.txt")
            .Where(f => !f.Name.EndsWith(".summary.txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        foreach (var old in reports.Skip(Math.Max(0, keep)))
        {
            TryDelete(old.FullName);
            TryDelete(SnapshotPath(old.FullName));
        }
    }

    private void WriteSnapshot()
    {
        _lastSnapshot = _clock();
        _snapshotDue = false;
        try
        {
            string path = SnapshotPath(FilePath);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Summary(_lastSnapshot, ended: false), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception)
        {
            // the report itself still has the timeline
        }
    }

    private void Write(string line)
    {
        if (_writer == null)
            return;
        if (_written > MaxFileBytes && !line.Contains("INCIDENT") && !line.StartsWith("=", StringComparison.Ordinal))
            return;
        _writer.WriteLine(line);
        _written += line.Length + 2;
    }

    private void Flush()
    {
        _writer?.Flush();
        _lastFlush = _clock();
    }

    private void FlushSoon()
    {
        if (_clock() - _lastFlush > TimeSpan.FromSeconds(2))
            Flush();
    }

    private string Stamp(DateTime time)
    {
        var since = time - Started;
        if (since < TimeSpan.Zero)
            since = TimeSpan.Zero;
        return $"{time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} +{(int)since.TotalHours}:{since.Minutes:00}:{since.Seconds:00}";
    }

    private static string FirstLine(string message)
    {
        string m = (message ?? "").Trim();
        int nl = m.IndexOf('\n');
        return (nl >= 0 ? m.Substring(0, nl) : m).Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "...";

    private static int CountOf(string text, string what)
    {
        int count = 0;
        for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // next time
        }
    }
}
