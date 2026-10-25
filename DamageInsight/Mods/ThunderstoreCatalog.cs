using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DamageInsight.Describe;

namespace DamageInsight.Mods;

public sealed class PackageVersion
{
    public string Version = "", Description = "", Icon = "", DownloadUrl = "";
    public long Downloads, FileSize;
    /// <summary>Dependency strings as Thunderstore writes them: "Owner-Name-1.2.3".</summary>
    public readonly List<string> Dependencies = new();
}

/// <summary>A package on Thunderstore's Skul page (API v1: every version, newest first).</summary>
public sealed class Package
{
    /// <summary>"Owner-Name" (the folder name r2modman, Gale and this mod install it under).</summary>
    public string FullName = "", Owner = "", Name = "", Url = "";
    public DateTime Updated;
    public int Rating;
    public bool Pinned, Deprecated, Nsfw;
    public readonly List<string> Categories = new();
    public readonly List<PackageVersion> Versions = new();

    public PackageVersion Latest => Versions.Count > 0 ? Versions[0] : new PackageVersion();
    public long Downloads => Versions.Sum(v => v.Downloads);
    /// <summary>The name to show: underscores as spaces ("SkulInsight_QoL" → "SkulInsight QoL").</summary>
    public string Title => Name.Replace('_', ' ');
}

/// <summary>
/// Thunderstore's list of Skul packages (0.14 mod manager, docs/MOD_MANAGER.md): parsing, search, filters, sorting and
/// what an install brings along (its dependencies first). Plain logic, no Unity.
/// </summary>
public static class ThunderstoreCatalog
{
    public const string ListUrl = "https://thunderstore.io/c/skul-the-hero-slayer/api/v1/package/";
    public const string OwnPackage = "DocRun-SkulInsight_QoL";

    public enum Sort { Updated, Downloads, Rating, Name }

    /// <summary>The loader itself (installed with BepInEx; BepInExPack_Skul on Windows, BepInEx macOS on a Mac).</summary>
    public static bool IsLoader(string fullName) => fullName.StartsWith("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase);

    /// <summary>Packages that are programs for the desktop, not mods for the game (Gale, a mod manager).</summary>
    public static bool IsDesktopTool(string fullName) => fullName.Equals("Kesomannen-GaleModManager", StringComparison.OrdinalIgnoreCase);

    public static List<Package> Parse(string json)
    {
        var list = new List<Package>();
        if (GearDoc.ParseRaw(json) is not List<object> packages)
            return list;
        foreach (var raw in packages.OfType<Dictionary<string, object>>())
        {
            var p = new Package
            {
                FullName = Str(raw, "full_name"), Owner = Str(raw, "owner"), Name = Str(raw, "name"), Url = Str(raw, "package_url"),
                Rating = (int)Num(raw, "rating_score"), Pinned = Bool(raw, "is_pinned"), Deprecated = Bool(raw, "is_deprecated"),
                Nsfw = Bool(raw, "has_nsfw_content"), Updated = Date(Str(raw, "date_updated")),
            };
            if (raw.TryGetValue("categories", out var cats) && cats is List<object> categories)
                p.Categories.AddRange(categories.OfType<string>());
            if (raw.TryGetValue("versions", out var vs) && vs is List<object> versions)
                foreach (var v in versions.OfType<Dictionary<string, object>>())
                {
                    var version = new PackageVersion
                    {
                        Version = Str(v, "version_number"), Description = Str(v, "description"), Icon = Str(v, "icon"),
                        DownloadUrl = Str(v, "download_url"), Downloads = (long)Num(v, "downloads"), FileSize = (long)Num(v, "file_size"),
                    };
                    if (v.TryGetValue("dependencies", out var ds) && ds is List<object> dependencies)
                        version.Dependencies.AddRange(dependencies.OfType<string>());
                    p.Versions.Add(version);
                }
            if (p.FullName.Length > 0 && p.Versions.Count > 0)
                list.Add(p);
        }
        return list;
    }

    /// <summary>The packages to show: search (name, owner, description), a category, no deprecated / NSFW ones, sorted.</summary>
    public static List<Package> Filter(IEnumerable<Package> all, string query, string category, Sort sort)
    {
        var shown = all.Where(p => !p.Deprecated && !p.Nsfw && !IsDesktopTool(p.FullName));
        if (!string.IsNullOrEmpty(category))
            shown = shown.Where(p => p.Categories.Contains(category));
        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            shown = shown.Where(p => p.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Owner.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Latest.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        // Pinned ones (the loader) first, like on Thunderstore.
        var ordered = shown.OrderByDescending(p => p.Pinned);
        return (sort switch
        {
            Sort.Downloads => ordered.ThenByDescending(p => p.Downloads),
            Sort.Rating => ordered.ThenByDescending(p => p.Rating).ThenByDescending(p => p.Downloads),
            Sort.Name => ordered.ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase),
            _ => ordered.ThenByDescending(p => p.Updated),
        }).ToList();
    }

    /// <summary>The categories in use (for the filter chips), most used first.</summary>
    public static List<string> Categories(IEnumerable<Package> all) =>
        all.Where(p => !p.Deprecated && !p.Nsfw).SelectMany(p => p.Categories).GroupBy(c => c)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToList();

    /// <summary>"Owner-Name" of a dependency string "Owner-Name-1.2.3" (names may contain "-"? Thunderstore's don't).</summary>
    public static string PackageOf(string dependency)
    {
        if (string.IsNullOrEmpty(dependency))
            return "";
        int last = dependency.LastIndexOf('-');
        return last > 0 && dependency.Substring(last + 1).Contains('.') ? dependency.Substring(0, last) : dependency;
    }

    /// <summary>
    /// What installing <paramref name="package"/> brings: its dependencies first (depth first, each once), then itself.
    /// The loader and packages already installed are left out; unknown dependencies are reported in <paramref name="missing"/>.
    /// </summary>
    public static List<Package> InstallOrder(Package package, IReadOnlyDictionary<string, Package> byName, Func<string, bool> installed,
        List<string> missing)
    {
        var order = new List<Package>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(Package p, bool root)
        {
            if (!seen.Add(p.FullName))
                return;
            foreach (var dependency in p.Latest.Dependencies)
            {
                string name = PackageOf(dependency);
                if (IsLoader(name) || installed(name))
                    continue;
                if (byName.TryGetValue(name, out var found))
                    Visit(found, false);
                else if (!missing.Contains(name))
                    missing.Add(name);
            }
            if (root || !installed(p.FullName))
                order.Add(p);
        }
        Visit(package, true);
        return order;
    }

    /// <summary>Is <paramref name="newer"/> a higher version than <paramref name="older"/> ("1.10.0" > "1.9.2")?</summary>
    public static bool IsNewer(string newer, string older)
    {
        if (!System.Version.TryParse(Pad(newer), out var a) || !System.Version.TryParse(Pad(older), out var b))
            return false;
        return a > b;
    }

    private static string Pad(string v) => string.IsNullOrEmpty(v) ? "" : v.Count(c => c == '.') == 0 ? v + ".0" : v;

    private static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is string s ? s : "";

    private static double Num(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is double n ? n : 0;

    private static bool Bool(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is bool b && b;

    private static DateTime Date(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d : DateTime.MinValue;
}
