// Not in Thunderstore's build: plugins updating themselves is not permitted there (see DamageInsight.csproj).
#if !THUNDERSTORE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DamageInsight.Describe;

namespace DamageInsight.Mods;

/// <summary>
/// SkulInsight QoL updates itself (0.14, Umut: "an auto update function every time it starts with BepInEx"): its
/// newest version comes from its own Thunderstore package (the reviewed one, nothing else), the new DLL is checked
/// (an assembly named DamageInsight with the version Thunderstore names) and put in place. The running DLL is loaded,
/// and Windows lets a loaded file be renamed but not overwritten: it is renamed (*.replaced, deleted at the next start
/// by ModFolders.CleanUp) and the new one takes its name, so the new version runs from the next start. A copy of the
/// old DLL stays in the backup folder. Plain logic, no Unity (ModService runs it).
/// </summary>
public static class SelfUpdate
{
    public const string ApiUrl = "https://thunderstore.io/api/experimental/package/DocRun/SkulInsight_QoL/";
    public const string DllName = "DamageInsight.dll";
    private const int BackupsKept = 3;

    /// <summary>The newest version on Thunderstore and its download, from the package API; null when unreadable.</summary>
    public static (string version, string url)? Latest(string json)
    {
        try
        {
            var latest = GearDoc.ParseNode(json).Child("latest");
            string version = latest.Str("version_number") ?? "", url = latest.Str("download_url") ?? "";
            return version.Length > 0 && url.StartsWith("https://thunderstore.io/", StringComparison.Ordinal) ? (version, url) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The mod's DLL in the package (plugins/DamageInsight.dll), or null.</summary>
    public static byte[] DllIn(IEnumerable<ZipReader.Entry> entries)
    {
        var dlls = entries.Where(e => !e.IsFolder && e.Path.Split('/').Last().Equals(DllName, StringComparison.OrdinalIgnoreCase)).ToList();
        return (dlls.FirstOrDefault(e => e.Path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)) ?? dlls.FirstOrDefault())?.Data;
    }

    /// <summary>
    /// Puts <paramref name="dll"/> in the place of the running <paramref name="current"/> DLL, after checking that it is
    /// the mod in <paramref name="version"/>. Returns null when done, else why not (nothing was changed then).
    /// </summary>
    public static string Install(string current, byte[] dll, string version, string runningVersion, string backup)
    {
        if (dll == null || dll.Length < 64 * 1024)
            return "the package has no DamageInsight.dll";
        string folder = Path.GetDirectoryName(current);
        string download = Path.Combine(folder, DllName + ".download");
        try
        {
            File.WriteAllBytes(download, dll);
            var name = AssemblyName.GetAssemblyName(download);
            if (!name.Name.Equals("DamageInsight", StringComparison.Ordinal))
                return "the downloaded DLL is " + name.Name;
            if (name.Version == null || name.Version.ToString(3) != Normal(version))
                return $"the downloaded DLL is version {name.Version}, Thunderstore said {version}";

            Directory.CreateDirectory(backup);
            File.Copy(current, Path.Combine(backup, $"DamageInsight-{runningVersion}.dll"), overwrite: true);
            foreach (var old in Directory.GetFiles(backup, "DamageInsight-*.dll").OrderByDescending(File.GetLastWriteTimeUtc).Skip(BackupsKept))
                File.Delete(old);

            string replaced = current + ModFolders.Replaced;
            for (int i = 2; File.Exists(replaced); i++)
                replaced = current + ModFolders.Replaced + i;
            File.Move(current, replaced);   // allowed while loaded (Windows) and for any open file (macOS)
            try
            {
                File.Move(download, current);
            }
            catch (Exception)
            {
                File.Move(replaced, current); // the old one back: never leave the game without the mod
                throw;
            }
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            if (File.Exists(download))
                try { File.Delete(download); } catch (Exception) { /* next time */ }
        }
    }

    /// <summary>"0.14" → "0.14.0" (an assembly version has at least three parts here).</summary>
    private static string Normal(string version)
    {
        var parts = (version ?? "").Split('.').ToList();
        while (parts.Count < 3)
            parts.Add("0");
        return string.Join(".", parts.Take(3));
    }
}
#endif
