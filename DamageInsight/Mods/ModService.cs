using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using DamageInsight.Lang;
using UnityEngine;
using UnityEngine.Networking;

namespace DamageInsight.Mods;

/// <summary>
/// The mod manager's game side (0.14, docs/MOD_MANAGER.md): Thunderstore's Skul list (asked when the Mods window
/// opens, kept on disk for offline), the packages' icons, installs with their dependencies, what is loaded now and
/// what needs a restart, and the restart through Steam. Nothing goes online before the window is opened.
/// </summary>
public sealed class ModService : MonoBehaviour
{
    public const string SteamGameUrl = "steam://rungameid/1147560";
    private const float ListMaxAge = 30 * 60f;

    public static ModService Instance { get; private set; }

    public static string Folder => Path.Combine(Paths.BepInExRootPath, "DamageInsight", "Mods");
    public static string Backup => Path.Combine(Folder, "Backup");
    private static string ListCache => Path.Combine(Folder, "packages.json");
    private static string IconCache => Path.Combine(Folder, "Icons");

    /// <summary>Increases with every change (the window redraws).</summary>
    public static int Version { get; private set; }

    public static List<Package> Packages { get; private set; } = new();
    public static Dictionary<string, Package> ByName { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public static List<InstalledMod> Installed { get; private set; } = new();
    /// <summary>What the window says now: a download in progress, the last result or problem.</summary>
    public static string Status { get; private set; } = "";
    public static bool Busy { get; private set; }
    public static bool ListLoading { get; private set; }
    public static DateTime ListFetched { get; private set; }
    /// <summary>The game runs from a mod manager's profile (r2modman, Gale, ...): installing is left to it.</summary>
    public static string Manager { get; private set; }

    // How every mod was when the game started (BepInEx loaded that): id → enabled, version.
    private static readonly Dictionary<string, (bool enabled, string version)> AtStart = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ChangedNow = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> IconsAsked = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<(string id, string url)> IconQueue = new();
    private float _listAskedAt = -ListMaxAge;
    private int _iconsLoading;

    private static void Changed() => Version++;

    private void Awake()
    {
        Instance = this;
        Manager = ModFolders.ManagerOf(Paths.BepInExRootPath);
#if !THUNDERSTORE
        try
        {
            int cleaned = ModFolders.CleanUp(Paths.BepInExRootPath, Backup);
            if (cleaned > 0)
                Plugin.Log.LogInfo($"Mods: finished {cleaned} removal(s) / replacement(s) from the last session");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Mods: clean-up of the last session failed: {e.Message}");
        }
#endif
        Rescan();
        foreach (var mod in Installed)
            AtStart[mod.Id] = (mod.Enabled && !mod.Removed, mod.Version);
        LoadListCache();
#if !THUNDERSTORE
        if (Plugin.AutoUpdate?.Value != false && WhyNoSelfUpdate() == null)
            StartCoroutine(UpdateSelfLater());
#endif
    }

#if THUNDERSTORE
    /// <summary>
    /// Thunderstore's build: the window only shows mods (Thunderstore's rules: "plugins updating themselves or other
    /// packages is not permitted"); mod managers install and update.
    /// </summary>
    public static readonly bool CanChange = false;
#else
    /// <summary>The window can install, update, remove and switch mods (not in Thunderstore's build).</summary>
    public static readonly bool CanChange = true;
#endif

    // ------------------------------------------------------------------ SkulInsight updates itself

    /// <summary>The version this session installed of SkulInsight itself (it runs from the next start), or null.</summary>
    public static string SelfUpdated { get; private set; }
    public static bool SelfUpdating { get; private set; }

    /// <summary>Why SkulInsight can't update itself here, or null.</summary>
    public static string WhyNoSelfUpdate()
    {
#if THUNDERSTORE
        return Loc.T("Thunderstore's Skul mods and yours. Install, update and turn mods on or off with your mod manager (r2modman, Gale).");
#elif DEV
        return Loc.T("This is a developer build: it isn't replaced by Thunderstore's version.");
#else
        return File.Exists(typeof(Plugin).Assembly.Location) ? null : Loc.T("SkulInsight's file wasn't found.");
#endif
    }

#if !THUNDERSTORE
    /// <summary>Every start (setting General/AutoUpdate): a little after the game is up, so the start isn't slower.</summary>
    private IEnumerator UpdateSelfLater()
    {
        yield return new WaitForSecondsRealtime(15f);
        yield return UpdateSelf(automatic: true);
    }

    /// <summary>The Mods window's Update on SkulInsight's own row.</summary>
#endif
    public void UpdateSelfNow()
    {
#if !THUNDERSTORE
        if (!SelfUpdating && SelfUpdated == null)
            StartCoroutine(UpdateSelf(automatic: false));
#endif
    }

#if !THUNDERSTORE

    private IEnumerator UpdateSelf(bool automatic)
    {
        string why = WhyNoSelfUpdate();
        if (why != null)
        {
            Status = why;
            Changed();
            yield break;
        }
        SelfUpdating = true;
        using var ask = UnityWebRequest.Get(SelfUpdate.ApiUrl);
        ask.timeout = 20;
        ask.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return ask.SendWebRequest();
        var latest = ask.result == UnityWebRequest.Result.Success ? SelfUpdate.Latest(ask.downloadHandler.text) : null;
        if (latest == null || !ThunderstoreCatalog.IsNewer(latest.Value.version, MyPluginInfo.PLUGIN_VERSION))
        {
            SelfUpdating = false;
            Plugin.Log.LogInfo(latest == null ? $"Update check: Thunderstore didn't answer ({ask.error})"
                : $"Update check: {MyPluginInfo.PLUGIN_VERSION} is up to date (Thunderstore: {latest.Value.version})");
            if (!automatic)
            {
                Status = latest == null ? Loc.T("Thunderstore didn't answer. Are you online?") : Loc.T("SkulInsight QoL is up to date.");
                Changed();
            }
            yield break;
        }

        var (version, url) = latest.Value;
        Status = Loc.F("Downloading {0} {1} ({2} of {3})…", "SkulInsight QoL", version, 1, 1);
        Changed();
        using var download = UnityWebRequest.Get(url);
        download.timeout = 120;
        download.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return download.SendWebRequest();
        SelfUpdating = false;
        string problem = download.result != UnityWebRequest.Result.Success ? download.error ?? "?" : null;
        if (problem == null)
        {
            try
            {
                var entries = ZipReader.Read(download.downloadHandler.data);
                string dll = typeof(Plugin).Assembly.Location;
                problem = SelfUpdate.Install(dll, SelfUpdate.DllIn(entries), version, MyPluginInfo.PLUGIN_VERSION, Backup);
                if (problem == null)
                    KeepManagerRecords(Path.GetDirectoryName(dll), entries, version);
            }
            catch (Exception e)
            {
                problem = e.Message;
            }
        }
        if (problem != null)
        {
            Status = Loc.F("Could not install {0}: {1}", "SkulInsight QoL " + version, problem);
            Plugin.Log.LogWarning($"Update: SkulInsight QoL {version} not installed: {problem}");
            Changed();
            yield break;
        }
        SelfUpdated = version;
        Status = Loc.F("SkulInsight QoL {0} is installed: it runs from the next start.", version);
        Plugin.Log.LogInfo($"Update: SkulInsight QoL {MyPluginInfo.PLUGIN_VERSION} → {version} installed (runs from the next start; the old DLL is in {Backup})");
        DamageInsight.UI.Notice.Show("self-update",
            Loc.F("SkulInsight QoL {0} is installed and runs from the next start of the game (or now: Esc, Mods, Restart game).", version), 15f);
        Changed();
    }

    /// <summary>
    /// A mod manager's profile (Umut: most players install through r2modman): after SkulInsight updated itself, the
    /// package folder gets the new manifest (and readme, changelog, icon) and r2modman's mods.yml the new version.
    /// </summary>
    private static void KeepManagerRecords(string folder, List<ZipReader.Entry> entries, string version)
    {
        if (File.Exists(Path.Combine(folder, "manifest.json")))
            foreach (var entry in entries.Where(e => !e.IsFolder && !e.Path.Contains('/')))
                if (entry.Path is "manifest.json" or "README.md" or "CHANGELOG.md" or "icon.png")
                    try { File.WriteAllBytes(Path.Combine(folder, entry.Path), entry.Data); } catch (Exception) { /* only for show */ }
        Record(y => R2modmanProfile.SetVersion(y, ThunderstoreCatalog.OwnPackage, version), "SkulInsight " + version);
    }
#endif

    /// <summary>The profile's mod list (r2modman, Thunderstore Mod Manager) is kept with the changes made here.</summary>
    public static bool ManagerListKept => Manager != null && R2modmanProfile.ListOf(Paths.BepInExRootPath) != null;

    private static void Record(Func<string, string> change, string what)
    {
        if (R2modmanProfile.Change(Paths.BepInExRootPath, change, out string problem))
            Plugin.Log.LogInfo($"Mods: {Manager}'s mod list updated ({what})");
        else if (problem != null)
            Plugin.Log.LogWarning($"Mods: {Manager}'s mod list not updated ({what}): {problem}");
    }

    // ------------------------------------------------------------------ installed

    public static void Rescan()
    {
        try
        {
            Installed = ModFolders.Scan(Paths.BepInExRootPath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Mods: could not read BepInEx/plugins: {e.Message}");
        }
        Changed();
    }

    /// <summary>The plugins BepInEx loaded at the start that live in this mod's files (name, version).</summary>
    public static List<BepInEx.PluginInfo> PluginsOf(InstalledMod mod)
    {
        var list = new List<BepInEx.PluginInfo>();
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            string location = info.Location ?? "";
            if (mod.Loose ? mod.Dlls.Any(d => SamePath(d, location))
                    : location.StartsWith(mod.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                      || mod.PatcherFolder.Length > 0 && location.StartsWith(mod.PatcherFolder, StringComparison.OrdinalIgnoreCase))
                list.Add(info);
        }
        return list;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>This mod (SkulInsight QoL): it can't be turned off or removed from inside itself.</summary>
    public static bool IsThisMod(InstalledMod mod) =>
        mod.Id.Equals(ThunderstoreCatalog.OwnPackage, StringComparison.OrdinalIgnoreCase)
        || mod.Dlls.Any(d => Path.GetFileName(d).StartsWith("DamageInsight.dll", StringComparison.OrdinalIgnoreCase));

    /// <summary>Changed since the game started (installed, updated, turned on or off, removed): needs a restart.</summary>
    public static bool NeedsRestart(InstalledMod mod)
    {
        if (IsThisMod(mod))
            return SelfUpdated != null;
        if (!AtStart.TryGetValue(mod.Id, out var start))
            return true; // put there while the game ran
        return start.enabled != (mod.Enabled && !mod.Removed) || start.version != mod.Version;
    }

    public static bool AnyRestartNeeded =>
        Installed.Any(NeedsRestart) || AtStart.Keys.Any(id => Installed.All(m => !m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The Thunderstore package of an installed mod (by its folder name), or null.</summary>
    public static Package PackageOf(InstalledMod mod) => ByName.TryGetValue(mod.Id, out var p) ? p : null;

    public static InstalledMod InstalledOf(Package package) =>
        Installed.FirstOrDefault(m => m.Id.Equals(package.FullName, StringComparison.OrdinalIgnoreCase) && !m.Removed)
        ?? (package.FullName.Equals(ThunderstoreCatalog.OwnPackage, StringComparison.OrdinalIgnoreCase) ? Installed.FirstOrDefault(IsThisMod) : null);

    /// <summary>The installed version of a package ("" when it doesn't say), or null when not installed.</summary>
    public static string InstalledVersion(Package package)
    {
        var mod = InstalledOf(package);
        if (mod == null)
            return null;
        if (mod.Version.Length > 0)
            return mod.Version;
        return PluginsOf(mod).Select(p => p.Metadata.Version.ToString()).FirstOrDefault() ?? "";
    }

    public static bool UpdateAvailable(Package package)
    {
        if (package.FullName.Equals(ThunderstoreCatalog.OwnPackage, StringComparison.OrdinalIgnoreCase))
            return SelfUpdated == null && ThunderstoreCatalog.IsNewer(package.Latest.Version, MyPluginInfo.PLUGIN_VERSION);
        string installed = InstalledVersion(package);
        return installed != null && installed.Length > 0 && ThunderstoreCatalog.IsNewer(package.Latest.Version, installed);
    }

    public static void SetEnabled(InstalledMod mod, bool enabled)
    {
#if THUNDERSTORE
        return;
#else
        if (IsThisMod(mod))
            return;
        try
        {
            ModFolders.SetEnabled(mod, enabled);
            ChangedNow.Add(mod.Id);
            Record(y => R2modmanProfile.SetEnabled(y, mod.Id, enabled), mod.Id + (enabled ? " on" : " off"));
            Status = enabled ? Loc.F("{0} turned on: active after a restart.", mod.Title) : Loc.F("{0} turned off: gone after a restart.", mod.Title);
            Plugin.Log.LogInfo($"Mods: {mod.Id} {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception e)
        {
            Status = Loc.F("Could not change {0}: {1}", mod.Title, e.Message);
            Plugin.Log.LogWarning($"Mods: {mod.Id} not changed: {e}");
        }
        Rescan();
#endif
    }

    public static void Remove(InstalledMod mod)
    {
#if THUNDERSTORE
        return;
#else
        if (IsThisMod(mod))
            return;
        try
        {
            ModFolders.Remove(mod, Backup);
            ChangedNow.Add(mod.Id);
            Record(y => R2modmanProfile.Remove(y, mod.Id), mod.Id + " removed");
            Status = Loc.F("{0} removed (kept in the backup folder): gone after a restart.", mod.Title);
            Plugin.Log.LogInfo($"Mods: {mod.Id} removed");
        }
        catch (Exception e)
        {
            Status = Loc.F("Could not change {0}: {1}", mod.Title, e.Message);
            Plugin.Log.LogWarning($"Mods: {mod.Id} not removed: {e}");
        }
        Rescan();
#endif
    }

    // ------------------------------------------------------------------ Thunderstore

    /// <summary>The window opened: ask Thunderstore for the list (again after 30 minutes).</summary>
    public void RefreshList(bool force = false)
    {
        if (ListLoading || !force && Time.unscaledTime - _listAskedAt < ListMaxAge && Packages.Count > 0)
            return;
        _listAskedAt = Time.unscaledTime;
        StartCoroutine(LoadList());
    }

    private IEnumerator LoadList()
    {
        ListLoading = true;
        Changed();
        using var request = UnityWebRequest.Get(ThunderstoreCatalog.ListUrl);
        request.timeout = 20;
        request.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return request.SendWebRequest();
        ListLoading = false;
        if (request.result == UnityWebRequest.Result.Success && TakeList(request.downloadHandler.text))
        {
            ListFetched = DateTime.Now;
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(ListCache, request.downloadHandler.text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Mods: the list was not cached: {e.Message}");
            }
            Plugin.Log.LogInfo($"Mods: Thunderstore lists {Packages.Count} Skul packages");
        }
        else
        {
            Status = Packages.Count > 0 ? Loc.T("Thunderstore didn't answer: this is the list from last time.") : Loc.T("Thunderstore didn't answer. Are you online?");
            Plugin.Log.LogWarning($"Mods: Thunderstore's list failed: {request.error}");
        }
        Changed();
    }

    private void LoadListCache()
    {
        try
        {
            if (File.Exists(ListCache))
            {
                TakeList(File.ReadAllText(ListCache, Encoding.UTF8));
                ListFetched = File.GetLastWriteTime(ListCache);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Mods: the cached list is unreadable: {e.Message}");
        }
    }

    private static bool TakeList(string json)
    {
        try
        {
            var list = ThunderstoreCatalog.Parse(json);
            if (list.Count == 0)
                return false;
            Packages = list;
            ByName = list.GroupBy(p => p.FullName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Mods: Thunderstore's list is unreadable: {e.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------ icons

    /// <summary>A package's icon (the disk cache, else downloaded once; null until it is there).</summary>
    public static Sprite IconOf(string fullName, string url, string localFile = null)
    {
        if (string.IsNullOrEmpty(fullName))
            return null;
        if (Icons.TryGetValue(fullName, out var sprite))
            return sprite;
        if (!IconsAsked.Add(fullName))
            return null;
        string cached = Path.Combine(IconCache, fullName + ".png");
        foreach (var file in new[] { localFile, cached })
            if (!string.IsNullOrEmpty(file) && File.Exists(file) && LoadIcon(fullName, File.ReadAllBytes(file)) is { } loaded)
                return loaded;
        if (!string.IsNullOrEmpty(url) && Instance != null)
            IconQueue.Enqueue((fullName, url));
        return null;
    }

    private void Update()
    {
        while (IconQueue.Count > 0 && _iconsLoading < 4)
        {
            var (id, url) = IconQueue.Dequeue();
            StartCoroutine(LoadIcon(id, url));
        }
    }

    private IEnumerator LoadIcon(string id, string url)
    {
        _iconsLoading++;
        using var request = UnityWebRequest.Get(url);
        request.timeout = 20;
        yield return request.SendWebRequest();
        _iconsLoading--;
        if (request.result != UnityWebRequest.Result.Success)
            yield break;
        byte[] png = request.downloadHandler.data;
        if (LoadIcon(id, png) == null)
            yield break;
        try
        {
            Directory.CreateDirectory(IconCache);
            File.WriteAllBytes(Path.Combine(IconCache, id + ".png"), png);
        }
        catch (Exception)
        {
            // shown anyway; asked again next time
        }
        Changed();
    }

    private static Sprite LoadIcon(string id, byte[] png)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(png))
            return null;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        Icons[id] = sprite;
        return sprite;
    }

    // ------------------------------------------------------------------ install

    /// <summary>What installing a package brings (its dependencies first), and dependencies Thunderstore doesn't know.</summary>
    public static List<Package> InstallOrder(Package package, List<string> missing) =>
        ThunderstoreCatalog.InstallOrder(package, ByName, name => Installed.Any(m => m.Id.Equals(name, StringComparison.OrdinalIgnoreCase) && !m.Removed), missing);

    public void Install(Package package)
    {
#if !THUNDERSTORE
        if (Busy || package == null)
            return;
        StartCoroutine(InstallAll(InstallOrder(package, new List<string>())));
#endif
    }

#if !THUNDERSTORE
    private IEnumerator InstallAll(List<Package> order)
    {
        Busy = true;
        for (int i = 0; i < order.Count; i++)
        {
            var package = order[i];
            var version = package.Latest;
            Status = Loc.F("Downloading {0} {1} ({2} of {3})…", package.Title, version.Version, i + 1, order.Count);
            Changed();
            using var request = UnityWebRequest.Get(version.DownloadUrl);
            request.timeout = 120;
            request.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Status = Loc.F("Could not download {0}: {1}", package.Title, request.error ?? "?");
                Plugin.Log.LogWarning($"Mods: download of {package.FullName} failed: {request.error}");
                break;
            }
            try
            {
                var entries = ZipReader.Read(request.downloadHandler.data);
                var notes = ModFolders.Install(Paths.BepInExRootPath, package.FullName, entries, Backup);
                ChangedNow.Add(package.FullName);
                long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                Record(y => R2modmanProfile.Has(y, package.FullName)
                    ? R2modmanProfile.SetVersion(y, package.FullName, version.Version)
                    : R2modmanProfile.Add(y, package, now), package.FullName + " " + version.Version);
                Plugin.Log.LogInfo($"Mods: installed {package.FullName} {version.Version} ({entries.Count} files)" +
                                   (notes.Count > 0 ? ": " + string.Join("; ", notes) : ""));
            }
            catch (Exception e)
            {
                Status = Loc.F("Could not install {0}: {1}", package.Title, e.Message);
                Plugin.Log.LogWarning($"Mods: install of {package.FullName} failed: {e}");
                break;
            }
            if (i == order.Count - 1)
                Status = order.Count == 1
                    ? Loc.F("{0} installed: active after a restart.", package.Title)
                    : Loc.F("{0} installed with {1} more: active after a restart.", package.Title, order.Count - 1);
        }
        Busy = false;
        Rescan();
    }
#endif

    // ------------------------------------------------------------------ restart

    /// <summary>Why the game can't restart now (a daily run would end), or null.</summary>
    public static string CannotRestart() =>
        DamageInsight.Daily.DailyRun.InDailySlot ? Loc.T("You are in the daily run's slot: go back to your game first (Codex, Daily, Back to my game).") : null;

    /// <summary>
    /// Closes the game and starts it again through Steam (with its launch options, so the Mac's BepInEx start and
    /// Proton stay as they are): a small script waits until this game is gone, then opens steam://rungameid.
    /// </summary>
    public static bool Restart()
    {
#if THUNDERSTORE
        return false;
#else
        if (CannotRestart() != null)
            return false;
        try
        {
            int pid = Process.GetCurrentProcess().Id;
            Directory.CreateDirectory(Folder);
            if (Tools.PlatformPaths.IsWindows)
            {
                // Windows, and Linux / Steam Deck through Proton (Wine has cmd, tasklist and start). The game's start
                // arguments go along: r2modman and Gale start it with doorstop arguments that point at their profile.
                string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                bool wine = DamageInsight.Crash.CrashReporter.WineVersion() != null;
                string launch = LaunchLine(wine ? null : SteamExe(), args);
                string script = Path.Combine(Folder, "restart.cmd");
                File.WriteAllText(script, RestartScriptWindows(pid, launch), new UTF8Encoding(false));
                Plugin.Log.LogInfo($"Mods: restart with: {launch}");
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            else
            {
                string script = Path.Combine(Folder, "restart.sh");
                File.WriteAllText(script, RestartScriptMac(pid), new UTF8Encoding(false));
                Process.Start(new ProcessStartInfo("/bin/sh", "\"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true });
            }
            Plugin.Log.LogInfo("Mods: restarting the game through Steam");
            Application.Quit();
            return true;
        }
        catch (Exception e)
        {
            Status = Loc.F("Could not restart: {0}. Close the game and start it again.", e.Message);
            Plugin.Log.LogWarning($"Mods: restart failed: {e}");
            Changed();
            return false;
        }
#endif
    }

#if !THUNDERSTORE

    /// <summary>Steam's program on Windows (the registry's SteamExe), or null.</summary>
    private static string SteamExe()
    {
        try
        {
            string exe = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
            return !string.IsNullOrEmpty(exe) && File.Exists(exe) ? exe : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The script's line that starts the game again: with the start arguments (an r2modman / Gale profile) through
    /// steam.exe -applaunch, as r2modman does; under Proton (no steam.exe) through steam://run with the arguments;
    /// without arguments through steam://rungameid. "%" is doubled for cmd.
    /// </summary>
    internal static string LaunchLine(string steamExe, string[] args)
    {
        string line = string.Join(" ", args.Select(QuoteArgument));
        if (args.Length == 0)
            return $"start \"\" \"{SteamGameUrl}\"";
        if (steamExe != null)
            return $"start \"\" \"{steamExe.Replace("%", "%%")}\" -applaunch 1147560 {line.Replace("%", "%%")}";
        return $"start \"\" \"steam://run/1147560//{Uri.EscapeDataString(line).Replace("%", "%%")}/\"";
    }

    /// <summary>A command line argument as Windows reads it back (quoted when it has spaces or quotes).</summary>
    internal static string QuoteArgument(string arg) =>
        arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

    /// <summary>Waits (up to a minute) until the game's process is gone, then starts it again (<paramref name="launch"/>).</summary>
    internal static string RestartScriptWindows(int pid, string launch) =>
        "@echo off\r\n" +
        "chcp 65001 >nul\r\n" +
        "set /a n=0\r\n" +
        ":wait\r\n" +
        $"tasklist /FI \"PID eq {pid}\" 2>nul | find \" {pid} \" >nul\r\n" +
        "if errorlevel 1 goto go\r\n" +
        "set /a n+=1\r\n" +
        "if %n% geq 60 goto go\r\n" +
        "ping -n 2 127.0.0.1 >nul\r\n" +
        "goto wait\r\n" +
        ":go\r\n" +
        "ping -n 3 127.0.0.1 >nul\r\n" +
        launch + "\r\n";

    internal static string RestartScriptMac(int pid) =>
        "#!/bin/sh\n" +
        "n=0\n" +
        $"while kill -0 {pid} 2>/dev/null && [ $n -lt 120 ]; do sleep 0.5; n=$((n+1)); done\n" +
        "sleep 2\n" +
        $"open \"{SteamGameUrl}\" 2>/dev/null || xdg-open \"{SteamGameUrl}\"\n";
#endif

    public static void OpenPluginsFolder()
    {
        string folder = ModFolders.Plugins(Paths.BepInExRootPath);
        Directory.CreateDirectory(folder);
        Application.OpenURL(Tools.PlatformPaths.FolderUrl(folder, Tools.PlatformPaths.IsWindows));
    }
}
