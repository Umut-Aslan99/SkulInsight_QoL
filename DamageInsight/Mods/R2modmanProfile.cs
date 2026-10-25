using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DamageInsight.Mods;

/// <summary>
/// r2modman's list of a profile's mods (&lt;profile&gt;/mods.yml, also the Thunderstore Mod Manager's): the mod
/// manager and SkulInsight's own update keep it right (Umut 2026-10-25: most players install through
/// r2modman), so r2modman shows the versions that are really there and knows mods installed from the game. An entry
/// is a YAML list item ("- manifestVersion: 1" at the line start) with name, versionNumber (major / minor / patch) and
/// enabled, as r2modman writes them (checked on a real profile). Only those lines are changed; an entry is added in
/// r2modman's own layout. Plain logic, no Unity: the texts in, the changed text out (null: nothing to change).
/// </summary>
public static class R2modmanProfile
{
    /// <summary>The profile's mods.yml next to its BepInEx folder, or null (not an r2modman-style profile).</summary>
    public static string ListOf(string bepinex)
    {
        try
        {
            string profile = Path.GetDirectoryName(Path.GetFullPath(bepinex).TrimEnd(Path.DirectorySeparatorChar, '/'));
            string list = profile == null ? null : Path.Combine(profile, "mods.yml");
            return list != null && File.Exists(list) ? list : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<string> Lines(string yaml) =>
        (yaml ?? "").Replace("\r\n", "\n").Split('\n').ToList();

    private static string Text(List<string> lines, string original) =>
        string.Join((original ?? "").Contains("\r\n") ? "\r\n" : "\n", lines);

    /// <summary>The lines [start, end) of the entry named <paramref name="name"/>, or (-1, -1).</summary>
    private static (int start, int end) Entry(List<string> lines, string name)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("- ", StringComparison.Ordinal))
                continue;
            int end = i + 1;
            while (end < lines.Count && !lines[end].StartsWith("- ", StringComparison.Ordinal))
                end++;
            for (int j = i; j < end; j++)
            {
                string line = lines[j].TrimStart('-', ' ');
                if (line.Equals("name: " + name, StringComparison.OrdinalIgnoreCase))
                    return (i, end);
            }
            i = end - 1;
        }
        return (-1, -1);
    }

    public static bool Has(string yaml, string name) => Entry(Lines(yaml), name).start >= 0;

    /// <summary>The entry's versionNumber set to <paramref name="version"/> ("0.14.1").</summary>
    public static string SetVersion(string yaml, string name, string version)
    {
        var parts = Parts(version);
        var lines = Lines(yaml);
        var (start, end) = Entry(lines, name);
        if (start < 0 || parts == null)
            return null;
        bool changed = false;
        for (int i = start; i < end; i++)
        {
            if (lines[i].Trim() != "versionNumber:")
                continue;
            string[] keys = { "major", "minor", "patch" };
            for (int k = 0; k < 3 && i + 1 + k < end; k++)
            {
                string line = lines[i + 1 + k];
                string indent = line.Substring(0, line.Length - line.TrimStart().Length);
                if (!line.TrimStart().StartsWith(keys[k] + ":", StringComparison.Ordinal))
                    return null;
                lines[i + 1 + k] = indent + keys[k] + ": " + parts[k].ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
            break;
        }
        return changed ? Text(lines, yaml) : null;
    }

    /// <summary>The entry's "enabled" set (r2modman's switch; the DLLs are renamed *.old by ModFolders like r2modman does).</summary>
    public static string SetEnabled(string yaml, string name, bool enabled)
    {
        var lines = Lines(yaml);
        var (start, end) = Entry(lines, name);
        if (start < 0)
            return null;
        for (int i = start; i < end; i++)
            if (lines[i].TrimStart().StartsWith("enabled:", StringComparison.Ordinal))
            {
                string indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                lines[i] = indent + "enabled: " + (enabled ? "true" : "false");
                return Text(lines, yaml);
            }
        return null;
    }

    public static string Remove(string yaml, string name)
    {
        var lines = Lines(yaml);
        var (start, end) = Entry(lines, name);
        if (start < 0)
            return null;
        lines.RemoveRange(start, end - start);
        if (lines.All(l => l.Trim().Length == 0))
            return "[]\n";
        return Text(lines, yaml);
    }

    /// <summary>A package installed from the game, as r2modman writes one (an existing entry is replaced).</summary>
    public static string Add(string yaml, Package package, long installedAtMs)
    {
        var parts = Parts(package.Latest.Version) ?? new[] { 0, 0, 0 };
        var lines = Lines(Remove(yaml, package.FullName) ?? yaml);
        if (lines.All(l => l.Trim().Length == 0 || l.Trim() == "[]"))
            lines = new List<string>(); // an empty list ("[]")
        while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);
        var latest = package.Latest;
        lines.Add("- manifestVersion: 1");
        lines.Add("  name: " + package.FullName);
        lines.Add("  authorName: " + package.Owner);
        lines.Add("  websiteUrl: " + Quote(package.Url));
        lines.Add("  displayName: " + Quote(package.Name));
        lines.Add("  description: " + Quote(latest.Description));
        lines.Add("  gameVersion: '0'");
        lines.Add("  networkMode: both");
        lines.Add("  packageType: other");
        lines.Add("  installMode: managed");
        lines.Add("  installedAtTime: " + installedAtMs.ToString(CultureInfo.InvariantCulture));
        lines.Add("  loaders: []");
        if (latest.Dependencies.Count == 0)
            lines.Add("  dependencies: []");
        else
        {
            lines.Add("  dependencies:");
            lines.AddRange(latest.Dependencies.Select(d => "    - " + d));
        }
        lines.Add("  incompatibilities: []");
        lines.Add("  optionalDependencies: []");
        lines.Add("  versionNumber:");
        lines.Add("    major: " + parts[0].ToString(CultureInfo.InvariantCulture));
        lines.Add("    minor: " + parts[1].ToString(CultureInfo.InvariantCulture));
        lines.Add("    patch: " + parts[2].ToString(CultureInfo.InvariantCulture));
        lines.Add("  enabled: true");
        lines.Add("  onlineSource: true");
        lines.Add("  trustedPackage: false");
        lines.Add("");
        return Text(lines, yaml);
    }

    /// <summary>A YAML single-quoted text (one line; ' doubled).</summary>
    private static string Quote(string text) =>
        "'" + (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("'", "''") + "'";

    private static int[] Parts(string version)
    {
        var parts = (version ?? "").Split('.');
        if (parts.Length != 3)
            return null;
        var numbers = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        return numbers;
    }

    /// <summary>Changes the profile's list (when there is one): read, change, write in one go. False when nothing changed.</summary>
    public static bool Change(string bepinex, Func<string, string> change, out string problem)
    {
        problem = null;
        string list = ListOf(bepinex);
        if (list == null)
            return false;
        try
        {
            string before = File.ReadAllText(list, Encoding.UTF8);
            string after = change(before);
            if (after == null || after == before)
                return false;
            string temp = list + ".skulinsight";
            File.WriteAllText(temp, after, new UTF8Encoding(false));
            File.Copy(temp, list, overwrite: true);
            File.Delete(temp);
            return true;
        }
        catch (Exception e)
        {
            problem = e.Message;
            return false;
        }
    }
}
