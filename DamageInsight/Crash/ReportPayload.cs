using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.Crash;

/// <summary>A report's kind: the form's two chips (REPORTS.md part A).</summary>
public enum ReportKind
{
    Bug,
    Idea,
}

/// <summary>What the player typed into the report window.</summary>
public sealed class ReportForm
{
    public ReportKind Kind = ReportKind.Bug;
    public string Title = "", Text = "", Contact = "";
    public bool AttachLogs = true;
}

/// <summary>The server's answer to a report: its ticket and, when the server opened one, the public issue.</summary>
public sealed class ReportAnswer
{
    public string Ticket = "", Issue = "", LogsRefused = "";
    public int IssueNumber;
    public bool Logs;
}

/// <summary>
/// What the report window and the co-op run logs send to the server (server/daily/app/reports.py), as plain logic:
/// the checks, the JSON, the server's answer, the key both games of a co-op run share. Tested (ReportTests).
/// </summary>
public static class ReportPayload
{
    public const int TitleMin = 3, TitleMax = 120, TextMax = 4000, ContactMax = 60;

    /// <summary>The server takes 8 MB per zip; a little below for safety.</summary>
    public const long ZipMax = 8L * 1024 * 1024 - 64 * 1024;

    public const string ReportsPath = "/api/reports";
    public const string RunLogsPath = "/api/coop/runlogs";

    public enum Problem
    {
        None,
        TitleShort,
        TitleLong,
        TextLong,
        ContactLong,
    }

    /// <summary>Text without control characters (with <paramref name="lines"/>: new lines and tabs kept, at most two
    /// empty lines in a row), trimmed; a single line has its runs of spaces made one.</summary>
    public static string Clean(string text, bool lines)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (lines && (c == '\n' || c == '\t'))
                sb.Append(c);
            else if (!lines && char.IsWhiteSpace(c))
                sb.Append(' ');
            else if (c == '‍' || !IsControlOrFormat(c))
                sb.Append(c);
        }
        string clean = sb.ToString().Trim();
        if (!lines)
            return string.Join(" ", clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        while (clean.Contains("\n\n\n\n"))
            clean = clean.Replace("\n\n\n\n", "\n\n\n");
        return clean;
    }

    private static bool IsControlOrFormat(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.PrivateUse;
    }

    /// <summary>What keeps the form from being sent (the server checks the same).</summary>
    public static Problem Check(ReportForm form)
    {
        string title = Clean(form.Title, false);
        if (title.Length < TitleMin)
            return Problem.TitleShort;
        if (title.Length > TitleMax)
            return Problem.TitleLong;
        if (Clean(form.Text, true).Length > TextMax)
            return Problem.TextLong;
        return Clean(form.Contact, false).Length > ContactMax ? Problem.ContactLong : Problem.None;
    }

    /// <summary>A version, build or language as the server takes it (letters, digits, " ._+()/-"), cut to <paramref name="max"/>.</summary>
    public static string Short(string text, int max)
    {
        var kept = new string((text ?? "").Where(c => c < 128 && (char.IsLetterOrDigit(c) || " ._+()/-".IndexOf(c) >= 0)).ToArray()).Trim();
        return kept.Length <= max ? kept : kept.Substring(0, max).Trim();
    }

    /// <summary>The report as the server's /api/reports takes it.</summary>
    public static string Json(ReportForm form, string mod, string game, string build, string language, bool coop)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"kind\":").Append(LogJson.Quote(form.Kind == ReportKind.Idea ? "idea" : "bug"))
          .Append(",\"title\":").Append(LogJson.Quote(Clean(form.Title, false)))
          .Append(",\"text\":").Append(LogJson.Quote(Clean(form.Text, true)))
          .Append(",\"contact\":").Append(LogJson.Quote(Clean(form.Contact, false)))
          .Append(",\"mod\":").Append(LogJson.Quote(Short(mod, 32)))
          .Append(",\"game\":").Append(LogJson.Quote(Short(game, 32)))
          .Append(",\"build\":").Append(LogJson.Quote(Short(build, 16)))
          .Append(",\"language\":").Append(LogJson.Quote(Short(language, 16)))
          .Append(",\"coop\":").Append(coop ? "true" : "false")
          .Append('}');
        return sb.ToString();
    }

    /// <summary>The server's answer, or null when it isn't one.</summary>
    public static ReportAnswer ParseAnswer(string json)
    {
        try
        {
            var node = GearDoc.ParseNode(json);
            string ticket = node.Str("ticket") ?? "";
            if (ticket.Length == 0)
                return null;
            string issue = node.Str("issue") ?? "";
            return new ReportAnswer
            {
                Ticket = ticket,
                Issue = issue.StartsWith("https://github.com/", StringComparison.Ordinal) ? issue : "",
                IssueNumber = (int)node.Num("issueNumber"),
                Logs = node.Bool("logs"),
                LogsRefused = node.Str("logsRefused") ?? "",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The field the server refused (its 400 answer's "detail"), or "".</summary>
    public static string RefusedField(string json)
    {
        try
        {
            return GearDoc.ParseNode(json).Str("detail") ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// The key both games of one co-op run send with their logs, so the server pairs them without a new network
    /// message: the run's seed (the host sends it with the run start), the run's first room (a room id is the same on
    /// every game) and the host's name. [DECISION] Not the first three rooms of the plan: a client that came a room
    /// late would never pair; seed and first room already tell runs apart.
    /// </summary>
    public static string RunKey(int seed, uint firstRoom, string hostName)
    {
        string source = seed.ToString(CultureInfo.InvariantCulture) + "|" + firstRoom.ToString(CultureInfo.InvariantCulture) + "|" + (hostName ?? "");
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
        return "run-" + string.Concat(hash.Take(14).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>One game's part of a co-op run (the zip's run.json).</summary>
    public static string RunJson(string key, bool host, int slot, string mod, string game, string mode, int level, int rooms,
        int seconds, int players, string end)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"key\":").Append(LogJson.Quote(key))
          .Append(",\"side\":").Append(LogJson.Quote(host ? "host" : "client"))
          .Append(",\"slot\":").Append(host ? 0 : Math.Max(1, Math.Min(3, slot)))
          .Append(",\"mod\":").Append(LogJson.Quote(Short(mod, 32)))
          .Append(",\"game\":").Append(LogJson.Quote(Short(game, 32)))
          .Append(",\"mode\":").Append(LogJson.Quote(Short(mode, 24)))
          .Append(",\"level\":").Append(Math.Max(0, Math.Min(10, level)))
          .Append(",\"rooms\":").Append(Math.Max(0, Math.Min(1000, rooms)))
          .Append(",\"seconds\":").Append(Math.Max(0, Math.Min(86400 * 2, seconds)))
          .Append(",\"players\":").Append(Math.Max(0, Math.Min(4, players)))
          .Append(",\"end\":").Append(LogJson.Quote(Short(end, 24)))
          .Append('}');
        return sb.ToString();
    }
}
