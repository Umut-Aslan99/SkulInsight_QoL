using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DamageInsight.Lang;

namespace DamageInsight.Coop;

/// <summary>
/// Other co-op mods and a missing co-op part (Umut 2026-11-24 after ticket #6: "a warning ... at the start of the game
/// and whenever the coop menu is opened, when one of the other coop mods is installed and ask to turn them off or
/// uninstall them. It should also stop them from hosting, and maybe even joining"). Ticket R-20261124-2: DuelManTeam's
/// OnlineCoopBeta was still in BepInEx/patchers; its preloader rewrote the game's 390 "the player" reads to its own
/// resolver, ours never loaded, and every enemy went for that game's player only (the boss stayed on the downed host).
/// Found here once at the start: loaded assemblies, BepInEx plugins (GUID / name) and DLL files in BepInEx/plugins and
/// BepInEx/patchers with a co-op name. While anything is found, or our preloader didn't load / doesn't fit, this game
/// can't host, join or search (<see cref="Blocker"/>): <see cref="UI.CoopConflictNotice"/> says why and what to do.
/// </summary>
public static class CoopConflicts
{
    public const string OnlineCoopBeta = "OnlineCoopBeta (DuelManTeam)";
    public const string FourPlayerCoop = "Skul4PlayerCoop (Shyukidev)";
    private static readonly Regex CoopName = new(@"onlinecoop|4playercoop|co-?op|multiplayer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The other co-op mods found at the start (display names, sorted).</summary>
    public static IReadOnlyList<string> Found { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// The name a file, assembly or plugin goes by in the warning, or null when it isn't another co-op mod. Plain logic,
    /// tested. SkulInsight's own files (DamageInsight*, anything named SkulInsight) never count.
    /// </summary>
    public static string Classify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string n = name.Trim();
        if (n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            n = n.Substring(0, n.Length - 4);
        if (n.IndexOf("SkulInsight", StringComparison.OrdinalIgnoreCase) >= 0
            || n.StartsWith("DamageInsight", StringComparison.OrdinalIgnoreCase))
            return null;
        if (n.StartsWith("SkulOnlineCoop", StringComparison.OrdinalIgnoreCase)
            || n.Equals("com.codex.skul.onlinecoop.v1", StringComparison.OrdinalIgnoreCase))
            return OnlineCoopBeta;
        if (n.IndexOf("4PlayerCoop", StringComparison.OrdinalIgnoreCase) >= 0)
            return FourPlayerCoop;
        return CoopName.IsMatch(n) ? n : null;
    }

    /// <summary>Once at the start (Plugin.Awake, after the plugins loaded).</summary>
    public static void Scan()
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var where = new List<string>();
        void Add(string name, string source)
        {
            string hit = Classify(name);
            if (hit == null)
                return;
            found.Add(hit);
            where.Add($"{source} '{name}'");
        }
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                Add(assembly.GetName().Name, "assembly");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[Coop] other co-op mods: assemblies not read (" + e.Message + ")"); }
        try
        {
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                Add(info?.Metadata?.GUID, "plugin");
                Add(info?.Metadata?.Name, "plugin");
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[Coop] other co-op mods: plugins not read (" + e.Message + ")"); }
        foreach (string root in new[] { BepInEx.Paths.PatcherPluginPath, BepInEx.Paths.PluginPath })
        {
            try
            {
                if (Directory.Exists(root))
                    foreach (string file in Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories))
                        Add(Path.GetFileName(file), "file " + Path.GetFileName(Path.GetDirectoryName(file)) + "/");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Coop] other co-op mods: " + root + " not read (" + e.Message + ")"); }
        }
        Found = found.ToArray();
        if (Found.Count > 0)
            Crash.CrashReporter.Note("other co-op mod(s) found: " + string.Join(", ", Found) + "; co-op hosting and joining blocked");
        if (Found.Count > 0)
            Plugin.Log.LogWarning("[Coop] other co-op mod(s) found: " + string.Join(", ", Found) + " (" + string.Join("; ", where.Distinct().Take(12)) +
                                  "). Hosting and joining are blocked until they are off.");
        else
            Plugin.Log.LogInfo("[Coop] other co-op mods: none found");
    }

    /// <summary>Test copies only (Testing/ConflictNoticeTest): pretend these were found.</summary>
    internal static void PretendFound(params string[] names) => Found = names;

    /// <summary>Our preloader didn't load, or what it injected doesn't fit (PlayerReadsBridge "missing" / "refused").</summary>
    public static bool PreloaderBroken => Plugin.CoopEnabled?.Value == true && PlayerReadsBridge.Reads == null
                                          && !PlayerReadsBridge.Status.StartsWith("off", StringComparison.Ordinal)
                                          && !PlayerReadsBridge.Status.StartsWith("not checked", StringComparison.Ordinal);

    /// <summary>Why this game may not host, join or search (in the player's language), or null.</summary>
    public static string Blocker => Found.Count > 0 ? Loc.T("Another co-op mod is installed: turn it off or uninstall it, then restart Skul.")
        : PreloaderBroken ? Loc.T("Co-op is not installed completely: reinstall SkulInsight QoL, then restart Skul.")
        : null;

    /// <summary>One line for the run logs / report header: what was found.</summary>
    public static string Summary => Found.Count > 0 ? "other co-op mods: " + string.Join(", ", Found)
        : "preloader: " + PlayerReadsBridge.Status;
}
