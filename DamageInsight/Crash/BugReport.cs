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
/// with the versions filled in; the player drags the zip into the page. Nothing is sent by the mod itself (a GitHub
/// account is needed; a later version may send it to skul.umut-aslan.dev instead, with consent).
/// </summary>
internal static class BugReport
{
    public const string IssuePage = "https://github.com/Umut-Aslan99/SkulInsight_QoL/issues/new";
    private const long MaxZipBytes = 24L * 1024 * 1024; // GitHub takes 25 MB per file
    private const int KeepReports = 5;

    public static string Folder => ModData.Folder("BugReports");

    /// <summary>Makes the zip, opens its folder and (<paramref name="openIssue"/>) the issue page. Returns the zip's
    /// path, or null (with <paramref name="problem"/>) when it could not be written.</summary>
    public static string MakeAndOpen(bool openIssue, out string problem)
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
            Application.OpenURL(IssueUrl(zip != null ? Path.GetFileName(zip) : null));
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
    public static string IssueUrl(string zipName)
    {
        string coop = CoopState();
        string title = coop.Length > 0 ? "[Co-op] " : "";
        var body = new StringBuilder();
        body.AppendLine("**What happened?** (or: what is your idea?)").AppendLine().AppendLine();
        body.AppendLine("**What did you expect?**").AppendLine().AppendLine();
        body.AppendLine("**Where** (chapter, room, skulls, what you did just before):").AppendLine().AppendLine();
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
        Coop.CoopTrace.Flush();
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string path = Path.Combine(Folder, "SkulInsight-report-" + stamp + ".zip");
        string bepinex = BepInEx.Paths.BepInExRootPath;
        var files = new List<(string name, string path)>();
        void Take(string file, string into)
        {
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
                files.Add((into + "/" + Path.GetFileName(file), file));
        }
        Take(Path.Combine(bepinex, "LogOutput.log"), "BepInEx");
        string unityLog = Application.consoleLogPath;
        Take(unityLog, "Unity");
        if (!string.IsNullOrEmpty(unityLog))
            Take(Path.Combine(Path.GetDirectoryName(unityLog) ?? "", "Player-prev.log"), "Unity");
        Take(Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID + ".cfg"), "config");
        foreach (string file in Newest(CrashReporter.Folder, "*.txt", 4))
            Take(file, "SessionReports");
        var traces = Newest(ModData.Folder("CoopTrace"), "*.jsonl", 2);
        foreach (string file in traces)
            Take(file, "CoopTrace");

        // the newest trace stays, the older one goes when the zip would be too big for GitHub
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipWriter(stream))
            {
                zip.Add("report.txt", Encoding.UTF8.GetBytes(Summary(files)), DateTime.Now);
                foreach (var (name, file) in files)
                {
                    byte[] data = ReadShared(file);
                    if (data == null)
                        continue;
                    LogPrivacy.MaskSteamIds(data); // a public issue: no Steam ids of anyone
                    zip.Add(name, data, File.GetLastWriteTime(file));
                }
            }
            if (new FileInfo(path).Length <= MaxZipBytes || traces.Count < 2)
                break;
            files.RemoveAll(f => f.path == traces[1]);
        }
        Plugin.Log.LogInfo($"bug report: {path} ({new FileInfo(path).Length / 1024} KB, {files.Count + 1} files)");
        return path;
    }

    private static string Summary(List<(string name, string path)> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SkulInsight bug report, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.Append(CrashReporter.Header());
        string coop = CoopState();
        sb.AppendLine("Co-op: " + (coop.Length > 0 ? coop : "not in a session"));
        sb.AppendLine("Mods: " + string.Join(", ", BepInEx.Bootstrap.Chainloader.PluginInfos.Values
            .Select(p => p.Metadata.Name + " " + p.Metadata.Version)));
        sb.AppendLine("Files: " + string.Join(", ", files.Select(f => f.name)));
        return sb.ToString();
    }

    /// <summary>A file another part of the game still writes (the logs, the open trace).</summary>
    private static byte[] ReadShared(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("bug report: " + Path.GetFileName(file) + " not read: " + e.Message);
            return null;
        }
    }

    private static List<string> Newest(string folder, string pattern, int count)
    {
        if (!Directory.Exists(folder))
            return new List<string>();
        return Directory.GetFiles(folder, pattern).OrderByDescending(File.GetLastWriteTimeUtc).Take(count).ToList();
    }

    private static void Prune()
    {
        foreach (string old in Newest(Folder, "SkulInsight-report-*.zip", int.MaxValue).Skip(KeepReports - 1))
        {
            try { File.Delete(old); }
            catch (Exception) { /* in use: next time */ }
        }
    }
}
