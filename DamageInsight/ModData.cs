using System.IO;

namespace DamageInsight;

/// <summary>
/// The one place for SkulInsight's data folder. Normally <c>BepInEx/DamageInsight</c>, as always. A co-op test copy
/// (the game started a second to fourth time on the same PC with <c>--skulinsight-instance N</c>, N 2-4) gets its own
/// <c>BepInEx/DamageInsight/CoopTest/instanceN</c> instead, so two copies never write the same Codex, run history,
/// save slots, crash reports, combat logs or daily state. Things that are only read (the translation override
/// folder, the cached achievement icons) stay in the shared folder (<see cref="SharedRoot"/>).
/// </summary>
public static class ModData
{
    /// <summary>The folder name under BepInEx (kept from the mod's first name).</summary>
    public const string FolderName = "DamageInsight";

    private static string _root, _sharedRoot;

    /// <summary>BepInEx's root folder; tests set it instead of BepInEx (null = BepInEx.Paths.BepInExRootPath).</summary>
    internal static string BepInExRoot { get; set; }

    /// <summary>The copy's number, 1 = the normal game (set once at start from the start options).</summary>
    public static int Instance { get; private set; } = 1;

    /// <summary>This copy's writable data folder (created when first asked for).</summary>
    public static string Root => _root ??= Created(RootFor(BepInExRootPath, Instance, _copyFolder));

    /// <summary>The folder every copy shares: <c>BepInEx/DamageInsight</c>. Only for things that are read, not written per player.</summary>
    public static string SharedRoot => _sharedRoot ??= RootFor(BepInExRootPath, 1);

    /// <summary>A sub folder of this copy's data folder, e.g. "Codex" (not created here).</summary>
    public static string Folder(string name) => Path.Combine(Root, name);

    /// <summary>A sub folder of the shared folder, e.g. "Lang".</summary>
    public static string SharedFolder(string name) => Path.Combine(SharedRoot, name);

    /// <summary>
    /// Called once at the very start (Plugin.Awake) with the copy's number from the start options. <paramref name="copyFolder"/>
    /// names a CoopTest sub folder instead (a second normal game that must not write the real data: "duplicate").
    /// </summary>
    public static void Use(int instance, string copyFolder = null)
    {
        Instance = instance < 2 ? 1 : instance;
        _copyFolder = copyFolder;
        _root = null;
        _sharedRoot = null;
    }

    private static string _copyFolder;

    /// <summary>This game keeps its data apart from the normal game (a test copy or a duplicate): no daily runs.</summary>
    public static bool IsSeparateCopy => Instance >= 2 || _copyFolder != null;

    /// <summary>The data folder of copy <paramref name="instance"/> under the BepInEx folder <paramref name="bepInExRoot"/>.</summary>
    public static string RootFor(string bepInExRoot, int instance, string copyFolder = null)
    {
        string shared = Path.Combine(bepInExRoot, FolderName);
        if (!string.IsNullOrEmpty(copyFolder))
            return Path.Combine(shared, "CoopTest", copyFolder);
        return instance < 2 ? shared : Path.Combine(shared, "CoopTest", "instance" + instance);
    }

    private static string BepInExRootPath => BepInExRoot ?? BepInEx.Paths.BepInExRootPath;

    private static string Created(string folder)
    {
        // The normal folder exists already for every player; a test copy's folder is made on demand.
        try { Directory.CreateDirectory(folder); }
        catch (IOException) { }
        catch (System.UnauthorizedAccessException) { }
        return folder;
    }
}
