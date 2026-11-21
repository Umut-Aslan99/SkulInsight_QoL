using System;
using System.IO;

namespace DamageInsight.Preloader;

/// <summary>
/// The co-op switch as the preloader sees it: [Coop] Enabled = true in docrun.skulinsight_qol.cfg. The preloader runs
/// before the plugin and BepInEx's config system, so it reads the file as plain text. Anything but a clear "true"
/// (no file, no section, false, a broken line) means off: the game's code stays untouched. Also on for a game started
/// with `--skulinsight-coop` (the co-op test launcher, tools/coop/start-instances.ps1): its copies need the player
/// reads resolved, and the config file is shared with the normal game, which must stay untouched.
/// </summary>
public static class CoopGate
{
    public const string ConfigFileName = "docrun.skulinsight_qol.cfg";
    public const string Section = "Coop";
    public const string Key = "Enabled";

    /// <summary>Reads the switch from the config file; <paramref name="why"/> says in one line what was found.</summary>
    public static bool IsOn(string configPath, out string why)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                why = $"{ConfigFileName} not found";
                return false;
            }
            return IsOnText(File.ReadAllText(configPath), out why);
        }
        catch (Exception e)
        {
            why = $"{ConfigFileName} unreadable ({e.GetType().Name})";
            return false;
        }
    }

    /// <summary>A co-op test copy: started with `--skulinsight-coop` (any form: "--skulinsight-coop host" or
    /// "--skulinsight-coop=join", any case). Plain logic, tested.</summary>
    public static bool FromStartOptions(System.Collections.Generic.IEnumerable<string> args, out string why)
    {
        foreach (string arg in args ?? Array.Empty<string>())
        {
            string a = (arg ?? "").Trim();
            if (a.Equals("--skulinsight-coop", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("--skulinsight-coop=", StringComparison.OrdinalIgnoreCase))
            {
                why = "started with --skulinsight-coop (test copy)";
                return true;
            }
        }
        why = "";
        return false;
    }

    /// <summary>The switch from the config file's text (plain logic, tested).</summary>
    public static bool IsOnText(string text, out string why)
    {
        bool inSection = false;
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            if (line.StartsWith("["))
            {
                inSection = line.EndsWith("]") && line.Substring(1, line.Length - 2).Trim().Equals(Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (!inSection || eq <= 0 || !line.Substring(0, eq).Trim().Equals(Key, StringComparison.OrdinalIgnoreCase))
                continue;
            string value = line.Substring(eq + 1).Trim();
            bool on = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            why = on ? $"[{Section}] {Key} = true" : $"[{Section}] {Key} = {value}";
            return on;
        }
        why = $"no [{Section}] {Key} in {ConfigFileName}";
        return false;
    }
}
