using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Separate saves for co-op test copies (instance 2-4). The game (Steam build) keeps its save in
/// <c>persistentDataPath/&lt;SteamID&gt;.dat</c> (+ .tmp, .bak) through <c>Platforms.Steam.SteamSaveSystem</c>, whose
/// private path fields are set in its constructor; every copy runs under the same Steam account, so without this all
/// copies would write Umut's one real save. For a test copy the paths are pointed at
/// <c>&lt;ModData.Root&gt;/save/instanceN.dat</c>; the first time, the real save is copied there, so the copy starts
/// with his progress but never writes the real file. Fails safe: if the fields can't be found or set, the copy writes
/// no save at all and loads an empty one (never the real file), <see cref="IsSafe"/> stays false and co-op must refuse
/// to start. The normal game (instance 1) is never patched.
/// </summary>
public static class SaveIsolation
{
    public const string SaveSystemType = "Platforms.Steam.SteamSaveSystem";
    private static readonly string[] PathFields = { "_saveFilePath", "_tempFilePath", "_backupFilePath" };

    private static string _isolatedPath;
    private static bool _installed;

    /// <summary>True for the normal game and for a test copy whose save is redirected; co-op refuses test copies while false.</summary>
    public static bool IsSafe { get; private set; } = true;

    /// <summary>Why <see cref="IsSafe"/> is false, for the log and the co-op page.</summary>
    public static string Problem { get; private set; } = "";

    /// <summary>The test copy's own save file, or null.</summary>
    public static string IsolatedPath => _isolatedPath;

    /// <summary><c>&lt;root&gt;/save/instanceN.dat</c>.</summary>
    public static string SavePathFor(string modDataRoot, int instance) =>
        Path.Combine(Path.Combine(modDataRoot, "save"), "instance" + instance + ".dat");

    /// <summary>The three paths the game uses for a save file: the save, its temp file and its backup.</summary>
    public static (string save, string temp, string backup) PathsFor(string savePath) =>
        (savePath, savePath + ".tmp", savePath + ".bak");

    /// <summary>
    /// The first time a test copy starts, its save is a copy of the real one (when there is one). Never overwrites an
    /// existing copy save and never writes the real file. Returns true when it copied.
    /// </summary>
    public static bool SeedFrom(string realSavePath, string isolatedPath)
    {
        if (string.IsNullOrEmpty(isolatedPath) || File.Exists(isolatedPath))
            return false;
        if (string.IsNullOrEmpty(realSavePath) || !File.Exists(realSavePath) || SamePath(realSavePath, isolatedPath))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(isolatedPath));
        File.Copy(realSavePath, isolatedPath, overwrite: false);
        return true;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Plugin.Awake, before anything else: patches the save system for a test copy (instance 2-4) only.</summary>
    internal static void Install(Harmony harmony, int instance, string modDataRoot)
    {
        if (instance < 2 || _installed)
            return;
        _installed = true;
        IsSafe = false;
        Problem = "the save redirect isn't set up yet";
        try
        {
            _isolatedPath = SavePathFor(modDataRoot, instance);
            Directory.CreateDirectory(Path.GetDirectoryName(_isolatedPath));
            var type = AccessTools.TypeByName(SaveSystemType);
            if (type == null || Array.Exists(PathFields, f => AccessTools.Field(type, f) == null))
            {
                Fail($"{SaveSystemType} or its path fields were not found (a game update?)");
                return;
            }
            var result = Patches.PatchRegistry.Apply(harmony, typeof(SaveIsolationPatches));
            if (!result.Ok)
                Fail("the save system patch failed: " + (result.Error?.GetBaseException().Message ?? "no methods patched"));
            else
                Problem = "the game's save system hasn't started yet";
        }
        catch (Exception e)
        {
            Fail("setting up the save redirect failed: " + e.Message);
        }
    }

    /// <summary>A game that must not touch any save (a second normal game): loads an empty one, writes nothing.</summary>
    internal static void Block(Harmony harmony, string why)
    {
        if (_installed)
            return;
        _installed = true;
        _isolatedPath = null;
        Fail(why);
        var result = Patches.PatchRegistry.Apply(harmony, typeof(SaveIsolationPatches));
        if (!result.Ok)
            Plugin.Log?.LogError("[CoopTest] Blocking the save failed too: " +
                                 (result.Error?.GetBaseException().Message ?? "no methods patched") + ". CLOSE THIS GAME.");
    }

    /// <summary>Points one SteamSaveSystem at the copy's own file (from the patches); false = left alone (unsafe).</summary>
    internal static bool Redirect(object saveSystem) => Redirect(saveSystem, _isolatedPath);

    /// <summary>Points <paramref name="saveSystem"/>'s three path fields at <paramref name="isolatedPath"/> (+ .tmp, .bak).</summary>
    internal static bool Redirect(object saveSystem, string isolatedPath)
    {
        if (isolatedPath == null || saveSystem == null)
            return false;
        _isolatedPath = isolatedPath;
        try
        {
            var type = saveSystem.GetType();
            FieldInfo save = AccessTools.Field(type, PathFields[0]);
            FieldInfo temp = AccessTools.Field(type, PathFields[1]);
            FieldInfo backup = AccessTools.Field(type, PathFields[2]);
            if (save == null || temp == null || backup == null)
                return Fail("the save system's path fields are missing");
            string current = save.GetValue(saveSystem) as string;
            if (current != null && SamePath(current, _isolatedPath))
                return IsSafe;
            if (SeedFrom(current, _isolatedPath))
                Plugin.Log?.LogInfo($"[CoopTest] Test save made from the real save: {_isolatedPath}");
            var (s, t, b) = PathsFor(_isolatedPath);
            save.SetValue(saveSystem, s);
            temp.SetValue(saveSystem, t);
            backup.SetValue(saveSystem, b);
            if (!SamePath((string)save.GetValue(saveSystem), s))
                return Fail("the save path could not be changed");
            IsSafe = true;
            Problem = "";
            Plugin.Log?.LogInfo($"[CoopTest] Save redirected: {current} -> {s}");
            return true;
        }
        catch (Exception e)
        {
            return Fail("redirecting the save failed: " + e.Message);
        }
    }

    private static bool Fail(string why)
    {
        IsSafe = false;
        Problem = why;
        Plugin.Log?.LogError($"[CoopTest] SAVE ISOLATION FAILED: {why}. This test copy writes no save and co-op stays off.");
        return false;
    }
}

/// <summary>
/// The save system hooks for test copies. Applied by <see cref="SaveIsolation.Install"/> only for instance 2-4, never
/// for the normal game (PatchRegistry.ApplyAll leaves it out, see PatchRegistry.AppliedSeparately). The constructor
/// hook redirects at once; Load and Save redirect again in case the save system was made before the patch (and refuse
/// to touch the real file when that fails).
/// </summary>
[HarmonyPatch(typeof(Platforms.Steam.SteamSaveSystem))]
internal static class SaveIsolationPatches
{
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPostfix]
    private static void AfterCreate(Platforms.Steam.SteamSaveSystem __instance)
    {
        try { SaveIsolation.Redirect(__instance); }
        catch (Exception) { /* Redirect reports itself */ }
    }

    /// <summary>Unsafe (not redirected): load an empty save instead of reading (and maybe restoring) the real one.</summary>
    [HarmonyPatch(nameof(Platforms.Steam.SteamSaveSystem.Load))]
    [HarmonyPrefix]
    private static bool BeforeLoad(Platforms.Steam.SteamSaveSystem __instance, ref Platforms.SaveFileModel __result)
    {
        try
        {
            if (SaveIsolation.Redirect(__instance))
                return true;
            __result = new Platforms.SaveFileModel();
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Unsafe (not redirected): write nothing.</summary>
    [HarmonyPatch(nameof(Platforms.Steam.SteamSaveSystem.Save))]
    [HarmonyPrefix]
    private static bool BeforeSave(Platforms.Steam.SteamSaveSystem __instance)
    {
        try { return SaveIsolation.Redirect(__instance); }
        catch (Exception) { return false; }
    }
}
