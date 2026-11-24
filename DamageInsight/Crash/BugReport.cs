using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Tools;
using UnityEngine;

namespace DamageInsight.Crash;

/// <summary>
/// "Report a bug or idea" (Umut 2026-11-21 13:20, T.md Open 0b ORDER 2b): one button packs this game's logs into
/// <c>BepInEx/DamageInsight/BugReports/SkulInsight-report-&lt;time&gt;.zip</c> (BepInEx's LogOutput.log, Unity's
/// Player.log and Player-prev.log, the newest session reports, the newest co-op traces, the mod's settings file, a
/// report.txt with versions, mods and the co-op state), opens that folder and the public repository's new-issue page
/// with the versions filled in; the player drags the zip into the page. Since the report form (REPORTS.md, UI/ReportWindow)
/// this is the way when the server can't be reached or General/ReportsToServer is off; the form sends the same files
/// (<see cref="Parts"/>) to skul.umut-aslan.dev on the player's click. Steam ids and the account name are masked.
/// </summary>
internal static class BugReport
{
    public const string IssuePage = "https://github.com/Umut-Aslan99/SkulInsight_QoL/issues/new";
    private const long MaxZipBytes = 24L * 1024 * 1024; // GitHub takes 25 MB per file
    private const int KeepReports = 5;
    private const int PrefillMax = 1500;

    public static string Folder => ModData.Folder("BugReports");

    /// <summary>Makes the zip, opens its folder and (<paramref name="openIssue"/>) the issue page. Returns the zip's
    /// path, or null (with <paramref name="problem"/>) when it could not be written.</summary>
    public static string MakeAndOpen(bool openIssue, out string problem, ReportForm form = null)
    {
        problem = null;
        string zip;
        try
        {
            zip = Make();
        }
        catch (Exception e)
        {
            problem = e.Message;
            Plugin.Log.LogWarning("bug report: the zip could not be written: " + e);
            zip = null;
        }
        try
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL(PlatformPaths.FolderUrl(Folder, PlatformPaths.IsWindows));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("bug report: the folder did not open: " + e.Message);
        }
        if (openIssue)
            Application.OpenURL(IssueUrl(zip != null ? Path.GetFileName(zip) : null, form));
        return zip;
    }

    /// <summary>The test copies (coop-test.py --report, COOP_LOG 64): the zip alone, nothing opened. Trace
    /// "bug-report" with its size and file count (or the problem).</summary>
    internal static void MakeForTest()
    {
        try
        {
            string zip = Make();
            Coop.CoopTrace.Write("bug-report", Coop.CoopWorld.CurrentRoom(), name: Path.GetFileName(zip), what: zip,
                amount: new FileInfo(zip).Length);
        }
        catch (Exception e)
        {
            Coop.CoopTrace.Write("bug-report", Coop.CoopWorld.CurrentRoom(), what: "failed: " + e.Message);
        }
    }

    /// <summary>The new-issue page with a short form and the versions filled in.</summary>
    public static string IssueUrl(string zipName, ReportForm form = null)
    {
        string coop = CoopState();
        string title = (coop.Length > 0 ? "[Co-op] " : "") + (form != null ? ReportPayload.Clean(form.Title, false) : "");
        var body = new StringBuilder();
        string text = form != null ? ReportPayload.Clean(form.Text, true) : "";
        if (text.Length > 0)
        {
            // A long text would make the address too long for the browser: the form put all of it in the clipboard.
            body.AppendLine(text.Length > PrefillMax ? text.Substring(0, PrefillMax) + " [...]" : text).AppendLine();
        }
        else
        {
            body.AppendLine("**What happened?** (or: what is your idea?)").AppendLine().AppendLine();
            body.AppendLine("**What did you expect?**").AppendLine().AppendLine();
            body.AppendLine("**Where** (chapter, room, skulls, what you did just before):").AppendLine().AppendLine();
        }
        body.AppendLine(zipName != null
            ? $"**Logs:** the game wrote `{zipName}` (its folder opened next to this page): please drag it here."
            : "**Logs:** please attach `BepInEx/LogOutput.log` and the newest files of `BepInEx/DamageInsight/CrashReports`.");
        body.AppendLine().AppendLine("---");
        body.AppendLine($"- SkulInsight {MyPluginInfo.PLUGIN_VERSION}, Skul {Application.version}");
        body.AppendLine($"- System: {SystemInfo.operatingSystem}");
        if (coop.Length > 0)
            body.AppendLine("- Co-op: " + coop);
        return IssuePage + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body.ToString());
    }

    private static string CoopState()
    {
        var world = Coop.CoopWorld.Current;
        if (world?.Active != true)
            return "";
        string trace = Coop.CoopTrace.BasePath != null ? ", log " + Path.GetFileName(Coop.CoopTrace.BasePath) : "";
        return (world.IsHost ? "host" : "client") + trace;
    }

    private static string Make()
    {
        Directory.CreateDirectory(Folder);
        Prune();
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string path = Path.Combine(Folder, "SkulInsight-report-" + stamp + ".zip");
        byte[] zip = ReportZip.Build(Parts(), MaxZipBytes, Environment.UserName, DateTime.Now);
        File.WriteAllBytes(path, zip);
        Plugin.Log.LogInfo($"bug report: {path} ({zip.Length / 1024} KB)");
        return path;
    }

    /// <summary>
    /// The files of a report (call on the game's thread; ReportZip packs them anywhere): report.txt with versions, mods
    /// and the co-op state, BepInEx's LogOutput.log, Unity's Player.log (and Player-prev.log), the mod's settings, the
    /// newest session reports and co-op traces. The older trace and Player-prev.log go first when the zip is too big;
    /// then the logs keep only their end. <paramref name="extra"/> comes first (the form's report.json).
    /// </summary>
    internal static List<ZipPart> Parts(params ZipPart[] extra)
    {
        Coop.CoopTrace.Flush();
        string bepinex = BepInEx.Paths.BepInExRootPath;
        var parts = new List<ZipPart>(extra);
        void Take(string file, string into, bool optional = false, bool cuttable = true)
        {
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
                parts.Add(new ZipPart { Name = into + "/" + Path.GetFileName(file), Path = file, Optional = optional, Cuttable = cuttable });
        }
        Take(Path.Combine(bepinex, "LogOutput.log"), "BepInEx");
        string unityLog = Application.consoleLogPath;
        Take(unityLog, "Unity");
        if (!string.IsNullOrEmpty(unityLog))
            Take(Path.Combine(Path.GetDirectoryName(unityLog) ?? "", "Player-prev.log"), "Unity", optional: true);
        Take(Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID + ".cfg"), "config", cuttable: false);
        foreach (string file in ReportZip.Newest(CrashReporter.Folder, "*.txt", 4))
            Take(file, "SessionReports");
        var traces = ReportZip.Newest(ModData.Folder("CoopTrace"), "*.jsonl", 2);
        for (int i = 0; i < traces.Count; i++)
            Take(traces[i], "CoopTrace", optional: i > 0);
        string summary = Summary(parts.Where(p => p.Path != null).Select(p => p.Name).ToList());
        parts.Insert(extra.Length, new ZipPart { Name = "report.txt", Data = Encoding.UTF8.GetBytes(summary) });
        return parts;
    }

    private static string Summary(List<string> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SkulInsight bug report, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.Append(CrashReporter.Header());
        string coop = CoopState();
        sb.AppendLine("Co-op: " + (coop.Length > 0 ? coop : "not in a session"));
        sb.AppendLine("Mods: " + string.Join(", ", BepInEx.Bootstrap.Chainloader.PluginInfos.Values
            .Select(p => p.Metadata.Name + " " + p.Metadata.Version)));
        sb.AppendLine("Files: " + string.Join(", ", files));
        return sb.ToString();
    }

    private static void Prune()
    {
        foreach (string old in ReportZip.Newest(Folder, "SkulInsight-report-*.zip", int.MaxValue).Skip(KeepReports - 1))
        {
            try { File.Delete(old); }
            catch (Exception) { /* in use: next time */ }
        }
    }
}
