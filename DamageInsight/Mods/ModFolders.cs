using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Describe;

namespace DamageInsight.Mods;

/// <summary>A mod in BepInEx/plugins: a package folder ("Owner-Name", as r2modman / Gale / this mod install them), any
/// other folder, or a DLL lying in plugins itself.</summary>
public sealed class InstalledMod
{
    /// <summary>The folder name ("MrBacanudo-LegendaryStart"), or a loose DLL's name without ".dll".</summary>
    public string Id = "";
    /// <summary>plugins/&lt;Id&gt; ("" for a loose DLL), patchers/&lt;Id&gt; ("" when it has none).</summary>
    public string Folder = "", PatcherFolder = "";
    /// <summary>Its DLLs, enabled (*.dll) and disabled (*.dll.old).</summary>
    public readonly List<string> Dlls = new();
    /// <summary>From its manifest.json (a Thunderstore package), else empty.</summary>
    public string Name = "", Version = "", Description = "", Website = "";
    public bool Loose => Folder.Length == 0;
    public bool Enabled => Dlls.Any(d => d.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    public bool HasDlls => Dlls.Count > 0;
    /// <summary>Removed while the game ran (its DLLs couldn't move yet): gone at the next start.</summary>
    public bool Removed;
    public string Title => Name.Length > 0 ? Name.Replace('_', ' ') : Id.Contains('-') ? Id.Substring(Id.IndexOf('-') + 1).Replace('_', ' ') : Id;
    public string Owner => Id.Contains('-') ? Id.Substring(0, Id.IndexOf('-')) : "";
}

/// <summary>
/// The mod manager's files (0.14, docs/MOD_MANAGER.md): what is installed in BepInEx, installing a package's zip where
/// r2modman would put it (tools/mac/install-mac.sh place_package: plugins/&lt;Owner-Name&gt;, patchers/&lt;Owner-Name&gt;,
/// config without overwriting), disabling (*.dll → *.dll.old, r2modman's way), removing into a backup folder.
/// Windows doesn't let a loaded DLL be deleted, but renamed: what can't move while the game runs is renamed and
/// finished at the next start (<see cref="CleanUp"/>). Plain logic, no Unity: the BepInEx folder is passed in.
/// </summary>
public static class ModFolders
{
    public const string Disabled = ".old", Replaced = ".replaced", RemovedMark = ".skulinsight-removed";

    private static readonly string[] PackageFiles = { "manifest.json", "icon.png", "README.md", "CHANGELOG.md" };

    public static string Plugins(string bepinex) => Path.Combine(bepinex, "plugins");
    public static string Patchers(string bepinex) => Path.Combine(bepinex, "patchers");

    /// <summary>Every mod in plugins (and the patchers with the same names), sorted by title.</summary>
    public static List<InstalledMod> Scan(string bepinex)
    {
        var mods = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        string plugins = Plugins(bepinex), patchers = Patchers(bepinex);
        if (Directory.Exists(plugins))
        {
            foreach (var folder in Directory.GetDirectories(plugins))
            {
                var mod = new InstalledMod { Id = Path.GetFileName(folder), Folder = folder };
                mod.Removed = File.Exists(Path.Combine(folder, RemovedMark));
                mod.Dlls.AddRange(DllsIn(folder));
                ReadManifest(mod, Path.Combine(folder, "manifest.json"));
                mods[mod.Id] = mod;
            }
            foreach (var file in Directory.GetFiles(plugins))
                if (IsDll(file))
                {
                    string id = DllName(file);
                    if (!mods.TryGetValue(id, out var mod))
                        mods[id] = mod = new InstalledMod { Id = id };
                    mod.Dlls.Add(file);
                }
        }
        if (Directory.Exists(patchers))
            foreach (var folder in Directory.GetDirectories(patchers))
            {
                string id = Path.GetFileName(folder);
                if (!mods.TryGetValue(id, out var mod))
                    mods[id] = mod = new InstalledMod { Id = id, Folder = "" };
                mod.PatcherFolder = folder;
                mod.Dlls.AddRange(DllsIn(folder));
                if (mod.Name.Length == 0)
                    ReadManifest(mod, Path.Combine(folder, "manifest.json"));
            }
        return mods.Values.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> DllsIn(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(IsDll);

    private static bool IsDll(string file) =>
        file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dll" + Disabled, StringComparison.OrdinalIgnoreCase);

    private static string DllName(string file)
    {
        string name = Path.GetFileName(file);
        return name.Substring(0, name.IndexOf(".dll", StringComparison.OrdinalIgnoreCase));
    }

    private static void ReadManifest(InstalledMod mod, string path)
    {
        try
        {
            if (!File.Exists(path))
                return;
            var node = GearDoc.ParseNode(File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿'));
            mod.Name = node.Str("name") ?? "";
            mod.Version = node.Str("version_number") ?? "";
            mod.Description = node.Str("description") ?? "";
            mod.Website = node.Str("website_url") ?? "";
        }
        catch (Exception)
        {
            // a broken manifest: the folder name stands in
        }
    }

    // ------------------------------------------------------------------ install

    /// <summary>
    /// Puts a package's files in place (an update replaces the old version). Returns notes for the log: files left
    /// out (unsafe paths, BepInEx's own core files), settings files kept.
    /// </summary>
    public static List<string> Install(string bepinex, string id, IEnumerable<ZipReader.Entry> entries, string backup)
    {
        var notes = new List<string>();
        if (!SafeName(id))
            throw new ArgumentException("not a package name: " + id);
        string plugins = Path.Combine(Plugins(bepinex), id), patchers = Path.Combine(Patchers(bepinex), id);
        Retire(plugins, backup, id);
        Retire(patchers, backup, id);
        Directory.CreateDirectory(plugins);
        foreach (var entry in entries)
        {
            if (entry.IsFolder)
                continue;
            var parts = entry.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0 || parts.Any(p => p == ".." || p == "." || p.Contains(':')))
            {
                notes.Add("left out (unsafe path): " + entry.Path);
                continue;
            }
            // A package may wrap everything in a "BepInEx" folder.
            if (parts.Count > 1 && parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
                parts.RemoveAt(0);
            string first = parts[0].ToLowerInvariant();
            string target;
            if (parts.Count == 1 && PackageFiles.Any(f => f.Equals(parts[0], StringComparison.OrdinalIgnoreCase)))
                target = Path.Combine(plugins, parts[0]);
            else if (parts.Count > 1 && first == "plugins")
                target = Path.Combine(plugins, Join(parts.Skip(1)));
            else if (parts.Count > 1 && first == "patchers")
                target = Path.Combine(patchers, Join(parts.Skip(1)));
            else if (parts.Count > 1 && first == "config")
            {
                target = Path.Combine(bepinex, "config", Join(parts.Skip(1)));
                if (File.Exists(target))
                {
                    notes.Add("kept your settings: " + Join(parts.Skip(1)));
                    continue;
                }
            }
            else if (parts.Count > 1 && first == "core")
            {
                notes.Add("left out (BepInEx's own files): " + entry.Path);
                continue;
            }
            else
                target = Path.Combine(plugins, Join(parts));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, entry.Data);
        }
        if (Directory.Exists(patchers) && !Directory.EnumerateFileSystemEntries(patchers).Any())
            Directory.Delete(patchers);
        return notes;
    }

    private static string Join(IEnumerable<string> parts) => string.Join(Path.DirectorySeparatorChar.ToString(), parts);

    /// <summary>"Owner-Name" with letters, digits, "_", "-" and "." only (no path tricks).</summary>
    public static bool SafeName(string id) =>
        !string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.') && !id.Contains("..");

    /// <summary>
    /// Moves an old version out of the way (into the backup folder). A folder whose DLLs Windows keeps (they are
    /// loaded) can't move: its DLLs are renamed (*.replaced, gone at the next start) and its other files deleted.
    /// </summary>
    private static void Retire(string folder, string backup, string id)
    {
        if (!Directory.Exists(folder))
            return;
        try
        {
            Directory.CreateDirectory(backup);
            Directory.Move(folder, Path.Combine(backup, BackupName(id, Path.GetFileName(Path.GetDirectoryName(folder)))));
            return;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Move(file, UniqueName(file + Replaced));
            else if (!file.EndsWith(Replaced, StringComparison.OrdinalIgnoreCase))
                TryDelete(file);
        }
    }

    private static string BackupName(string id, string kind) =>
        $"{id}.{kind}.{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}";

    private static string UniqueName(string path)
    {
        string candidate = path;
        for (int i = 2; File.Exists(candidate); i++)
            candidate = path + i.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    // ------------------------------------------------------------------ disable, remove

    /// <summary>Turns a mod on or off by renaming its DLLs (*.dll ↔ *.dll.old); works while the game runs.</summary>
    public static void SetEnabled(InstalledMod mod, bool enabled)
    {
        for (int i = 0; i < mod.Dlls.Count; i++)
        {
            string dll = mod.Dlls[i];
            bool on = dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (on == enabled)
                continue;
            string target = enabled ? dll.Substring(0, dll.Length - Disabled.Length) : dll + Disabled;
            if (File.Exists(target))
                File.Delete(target);
            File.Move(dll, target);
            mod.Dlls[i] = target;
        }
    }

    /// <summary>Removes a mod into the backup folder; when its DLLs can't move yet (loaded), it is marked and finished at the next start.</summary>
    public static void Remove(InstalledMod mod, string backup)
    {
        Directory.CreateDirectory(backup);
        if (mod.Loose)
        {
            foreach (var dll in mod.Dlls)
                MoveOrMark(dll, Path.Combine(backup, BackupName(mod.Id, "plugins") + "_" + Path.GetFileName(dll)));
            return;
        }
        foreach (var folder in new[] { mod.Folder, mod.PatcherFolder }.Where(f => f.Length > 0 && Directory.Exists(f)))
        {
            try
            {
                Directory.Move(folder, Path.Combine(backup, BackupName(mod.Id, Path.GetFileName(Path.GetDirectoryName(folder)))));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Loaded: its DLLs are renamed so BepInEx skips them, the folder goes at the next start.
                foreach (var dll in Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories))
                    File.Move(dll, UniqueName(dll + Disabled));
                File.WriteAllText(Path.Combine(folder, RemovedMark), "removed by SkulInsight QoL; moved to the backup at the next start\n");
                mod.Removed = true;
            }
        }
    }

    private static void MoveOrMark(string file, string target)
    {
        try
        {
            File.Move(file, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Move(file, UniqueName(file + Replaced));
        }
    }

    /// <summary>At the start (nothing of these is loaded now): finishes removals and deletes replaced DLLs.</summary>
    public static int CleanUp(string bepinex, string backup)
    {
        int done = 0;
        foreach (var root in new[] { Plugins(bepinex), Patchers(bepinex) }.Where(Directory.Exists))
        {
            foreach (var folder in Directory.GetDirectories(root).Where(f => File.Exists(Path.Combine(f, RemovedMark))))
            {
                try
                {
                    Directory.CreateDirectory(backup);
                    Directory.Move(folder, Path.Combine(backup, BackupName(Path.GetFileName(folder), Path.GetFileName(root))));
                    done++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // still busy: next time
                }
            }
            foreach (var file in Directory.GetFiles(root, "*" + Replaced + "*", SearchOption.AllDirectories))
                if (TryDelete(file))
                    done++;
        }
        return done;
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ mod managers

    /// <summary>
    /// The mod manager whose profile the game runs from (r2modman, Gale, Thunderstore Mod Manager), or null for a
    /// BepInEx in the game's own folder. Installing there would go past the manager's own records.
    /// </summary>
    public static string ManagerOf(string bepinex)
    {
        string path = (bepinex ?? "").Replace('\\', '/');
        if (path.IndexOf("/profiles/", StringComparison.OrdinalIgnoreCase) < 0)
            return null;
        if (path.IndexOf("r2modmanPlus-local", StringComparison.OrdinalIgnoreCase) >= 0)
            return "r2modman";
        if (path.IndexOf("Thunderstore Mod Manager", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Thunderstore Mod Manager";
        if (path.IndexOf("gale", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Gale";
        return "a mod manager";
    }
}
