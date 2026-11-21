using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DamageInsight.Preloader;
using Mono.Cecil;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>
/// The co-op preloader (DamageInsight.Preloader): its config gate, and its rewrite of the player reads run in memory
/// on the real Assembly-CSharp.dll of the Skul install (read-only). The list of reads is pinned in
/// docs/coop/PLAYER_READS.md: a game update that adds, moves or reclassifies a read fails here. To refresh the list
/// after reviewing the change: set UPDATE_PLAYER_READS=1 and run the tests.
/// </summary>
public class CoopPreloaderTests
{
    // ---------------------------------------------------------------- the gate

    [Theory]
    [InlineData("[Coop]\nEnabled = true\n", true)]
    [InlineData("[Coop]\r\n## Online co-op.\r\n# Setting type: Boolean\r\nEnabled = True\r\n", true)]
    [InlineData("[General]\nX = 1\n\n[ Coop ]\n  Enabled=true  \n[Daily]\nEnabled = false\n", true)]
    [InlineData("[Coop]\nEnabled = false\n", false)]
    [InlineData("[Coop]\nEnabled = yes\n", false)]
    [InlineData("[Coop]\n# Enabled = true\n", false)]
    [InlineData("[Daily]\nEnabled = true\n", false)]
    [InlineData("Enabled = true\n", false)]
    [InlineData("[Coop]\nEnabledLater = true\n", false)]
    [InlineData("[Coop\nEnabled = true\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Gate_reads_only_a_clear_true_in_the_Coop_section(string text, bool on)
    {
        Assert.Equal(on, CoopGate.IsOnText(text, out string why));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void Gate_is_off_without_a_config_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "skulinsight-no-such-folder-" + Guid.NewGuid(), CoopGate.ConfigFileName);
        Assert.False(CoopGate.IsOn(path, out string why));
        Assert.Contains("not found", why);
    }

    [Fact]
    public void Gate_reads_the_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "skulinsight-gate-" + Guid.NewGuid() + ".cfg");
        try
        {
            File.WriteAllText(path, "[Coop]\nEnabled = true\n");
            Assert.True(CoopGate.IsOn(path, out _));
            File.WriteAllText(path, "[Coop]\nEnabled = false\n");
            Assert.False(CoopGate.IsOn(path, out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Patcher_has_BepInEx_preloader_shape()
    {
        // BepInEx 5 finds a patcher by a public static TargetDLLs (IEnumerable<string>) and Patch(AssemblyDefinition).
        var t = typeof(Patcher);
        Assert.Equal(typeof(IEnumerable<string>), t.GetProperty("TargetDLLs", BindingFlags.Public | BindingFlags.Static)?.PropertyType);
        var patch = t.GetMethod("Patch", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(patch);
        Assert.Equal(typeof(void), patch.ReturnType);
        Assert.Equal(typeof(AssemblyDefinition), patch.GetParameters().Single().ParameterType);
        Assert.Equal(new[] { "Assembly-CSharp.dll" }, Patcher.TargetDLLs);
        // Never the game's code: the preloader runs before it is loaded.
        Assert.DoesNotContain(t.Assembly.GetReferencedAssemblies(), a => a.Name.StartsWith("Assembly-CSharp") || a.Name.StartsWith("UnityEngine") || a.Name == "0Harmony");
    }

    // ---------------------------------------------------------------- the real game

    private static readonly Lazy<byte[]> GameImage = new(() => File.ReadAllBytes(Path.Combine(GameManaged(), "Assembly-CSharp.dll")));

    private static string GameManaged()
    {
        string managed = typeof(CoopPreloaderTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SkulManaged")?.Value;
        return !string.IsNullOrEmpty(managed) && File.Exists(Path.Combine(managed, "Assembly-CSharp.dll"))
            ? managed
            : AppDomain.CurrentDomain.BaseDirectory; // the copy next to the tests
    }

    private static ModuleDefinition ReadGame(byte[] image = null)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(GameManaged());
        return ModuleDefinition.ReadModule(new MemoryStream(image ?? GameImage.Value), new ReaderParameters { AssemblyResolver = resolver });
    }

    private static readonly Lazy<List<PlayerReadSite>> Sites = new(() => PlayerReads.Scan(ReadGame()));

    [Fact]
    public void Every_player_read_is_classified()
    {
        var sites = Sites.Value;
        Assert.True(sites.Count > 300, $"only {sites.Count} reads of LevelManager.player found");
        var missing = sites.Where(s => !s.Classified).Select(s => s.Key).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} reads have no rule (add one to PlayerReadRules): " + string.Join(" | ", missing));
        Assert.Equal(sites.Count, sites.Select(s => s.Key).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, sites.Count), sites.Select(s => s.Id));
    }

    [Fact]
    public void Every_rule_is_used()
    {
        // An override nothing matches anymore means the game changed there (or a typo): review it. Namespace rules
        // may be unused today: they are the defaults for reads a game update adds.
        var used = new HashSet<string>(Sites.Value.Select(s => s.Rule));
        var stale = PlayerReadRules.Overrides.Keys.Where(k => !used.Contains("override " + k)).ToList();
        Assert.True(stale.Count == 0, "Overrides that match no read: " + string.Join(" | ", stale));
    }

    [Fact]
    public void Counts_per_kind()
    {
        Assert.Equal(ExpectedCounts, Counts(Sites.Value));
    }

    private static string Counts(List<PlayerReadSite> sites) =>
        string.Join(", ", Enum.GetValues(typeof(PlayerReadKind)).Cast<PlayerReadKind>().Select(k => $"{k} {sites.Count(s => s.Kind == k)}"));

    // The Skul install of 2026-11-03 (390 reads in 317 methods).
    private const string ExpectedCounts = "Local 230, Owner 63, Target 46, World 51";

    [Fact]
    public void The_game_reads_the_player_only_through_calls()
    {
        // A method group, a token or a property read by reflection would slip past the rewrite.
        var module = ReadGame();
        var other = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions.Select(i => (m, i)))
            .Where(x => x.i.Operand is MethodReference r && r.Name == "get_player" && r.DeclaringType.FullName == "Level.LevelManager" && !PlayerReads.IsGetPlayer(x.i))
            .Select(x => $"{x.m.FullName}: {x.i.OpCode}").ToList();
        Assert.True(other.Count == 0, string.Join(" | ", other));
    }

    [Fact]
    public void List_matches_docs_coop_PLAYER_READS()
    {
        string path = Path.Combine(RepoRoot(), "docs", "coop", "PLAYER_READS.md");
        string expected = Report(Sites.Value);
        if (Environment.GetEnvironmentVariable("UPDATE_PLAYER_READS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }
        Assert.True(File.Exists(path), "docs/coop/PLAYER_READS.md is missing (run the tests with UPDATE_PLAYER_READS=1)");
        string actual = File.ReadAllText(path).Replace("\r\n", "\n");
        if (actual != expected)
        {
            var a = actual.Split('\n');
            var e = expected.Split('\n');
            int i = Enumerable.Range(0, Math.Min(a.Length, e.Length)).FirstOrDefault(n => a[n] != e[n]);
            Assert.Fail($"docs/coop/PLAYER_READS.md is out of date (a game update or a rule change?). Review, then run the tests with UPDATE_PLAYER_READS=1. " +
                        $"First difference, line {i + 1}: file '{(i < a.Length ? a[i] : "")}', game '{(i < e.Length ? e[i] : "")}'");
        }
    }

    [Fact]
    public void Patch_rewrites_every_read_but_the_local_ones_and_writes_valid_metadata()
    {
        var module = ReadGame();
        var sites = PlayerReads.Patch(module);
        Assert.NotNull(sites);
        Assert.Null(PlayerReads.Patch(module)); // twice is a no-op

        // The injected API the plugin uses.
        var api = module.GetType(PlayerReads.InjectedNamespace, PlayerReads.InjectedName);
        Assert.NotNull(api);
        Assert.True(api.IsPublic && api.IsAbstract && api.IsSealed);
        foreach (string kind in new[] { "Owner", "Target", "World" })
        {
            var field = api.Fields.Single(f => f.Name == kind);
            Assert.True(field.IsPublic && field.IsStatic && !field.IsInitOnly);
            Assert.Equal("System.Func`4<Level.LevelManager,System.Object,System.Int32,Characters.Character>", field.FieldType.FullName);
            var resolve = api.Methods.Single(m => m.Name == "Resolve" + kind);
            Assert.Equal("Characters.Character SkulInsightCoop.PlayerReads::Resolve" + kind + "(Level.LevelManager,System.Object,System.Int32)", resolve.FullName);
        }
        Assert.Equal(sites.Count, (int)api.Fields.Single(f => f.Name == "SiteCount").Constant);
        Assert.Equal(sites.Count, ((string)api.Fields.Single(f => f.Name == "Kinds").Constant).Length);
        Assert.Equal(sites.Select(s => s.Key), ((string)api.Fields.Single(f => f.Name == "SiteList").Constant).Split('\n'));
        Assert.Equal(PlayerReads.Hash(sites), api.Fields.Single(f => f.Name == "SitesHash").Constant);
        // The plugin uses the injected API only with the reviewed list: update both together (E.md, "Test / refresh").
        Assert.Equal(DamageInsight.Coop.PlayerReadsBridge.ReviewedSitesHash, PlayerReads.Hash(sites));
        Assert.Equal(0, api.Fields.Single(f => f.Name == "UnclassifiedSites").Constant);

        // Only Local reads still call get_player; every other one calls its resolver with its own id.
        var after = PlayerReads.Scan(module);
        Assert.Equal(sites.Count(s => s.Kind == PlayerReadKind.Local), after.Count);
        Assert.All(after, s => Assert.Equal(PlayerReadKind.Local, s.Kind));
        var calls = module.GetTypes().Where(t => t != api).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.Operand is MethodReference r && r.DeclaringType.FullName == "SkulInsightCoop.PlayerReads")
            .Select(i => (kind: ((MethodReference)i.Operand).Name.Substring("Resolve".Length), id: IntOf(i.Previous)))
            .ToList();
        var rewritten = sites.Where(s => s.Kind != PlayerReadKind.Local).ToList();
        Assert.Equal(rewritten.Select(s => (s.Kind.ToString(), s.Id)).OrderBy(x => x.Id), calls.OrderBy(c => c.id));

        // The patched module must be valid metadata: written and read back.
        var image = new MemoryStream();
        module.Write(image);
        var reread = ReadGame(image.ToArray());
        Assert.Equal(rewritten.Count, reread.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Count(i => i.Operand is MethodReference r && r.DeclaringType.FullName == "SkulInsightCoop.PlayerReads"));
    }

    [Fact]
    public void The_games_mscorlib_has_the_delegate_type()
    {
        // Func`4 must come from the game's own mscorlib (Unity's Mono), where the patched code runs.
        string corlib = Path.Combine(GameManaged(), "mscorlib.dll");
        if (!File.Exists(corlib))
            return;
        using var module = ModuleDefinition.ReadModule(corlib);
        Assert.NotNull(module.GetType("System.Func`4"));
    }

    [Fact]
    public void Patched_code_compiles_and_the_resolvers_work()
    {
        // The patched DLL runs in its own AppDomain (it has the game's assembly name): the injected resolvers are
        // called, and every rewritten game method is compiled by the JIT (bad IL = InvalidProgramException).
        // Outside the game some methods can't be compiled at all (a Unity type, a security check): those are compared
        // with the unpatched DLL, so only what the rewrite changed counts.
        var module = ReadGame();
        var sites = PlayerReads.Patch(module);
        var rewritten = sites.Where(s => s.Kind != PlayerReadKind.Local).Select(s => s.Method)
            .Where(m => !m.HasGenericParameters && !HasGenericOwner(m.DeclaringType)).Select(m => m.FullName).Distinct().ToList();
        var image = new MemoryStream();
        module.Write(image);
        string[] patched = Probe(image.ToArray(), rewritten, true);
        string[] original = Probe(GameImage.Value, rewritten, false);

        Assert.True(patched[0] == "ok", patched[0]);
        var worse = new List<string>();
        int compiled = 0;
        for (int i = 0; i < rewritten.Count; i++)
        {
            if (patched[i + 1] == "ok")
                compiled++;
            else if (patched[i + 1] != original[i + 1])
                worse.Add($"{rewritten[i]}: {patched[i + 1]} (unpatched: {original[i + 1]})");
        }
        Assert.True(worse.Count == 0, "The rewrite broke methods: " + string.Join(" | ", worse));
        Assert.True(compiled > 0, "no rewritten method could be compiled here: " + string.Join(" | ", patched.Skip(1).Distinct().Take(5)));
    }

    private static string[] Probe(byte[] image, List<string> methods, bool callResolvers)
    {
        var module = ReadGame(image);
        var byName = module.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, m => m.MetadataToken.ToInt32());
        int[] tokens = methods.Select(m => byName[m]).ToArray();
        var setup = new AppDomainSetup
        {
            ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
            ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile,
        };
        var domain = AppDomain.CreateDomain("patched-game", null, setup);
        try
        {
            var probe = (PatchedGameProbe)domain.CreateInstanceAndUnwrap(typeof(PatchedGameProbe).Assembly.FullName, typeof(PatchedGameProbe).FullName);
            return probe.Run(image, tokens, callResolvers);
        }
        finally { AppDomain.Unload(domain); }
    }

    /// <summary>The constant an ldc.i4 pushes, in any of its short forms.</summary>
    private static int IntOf(Mono.Cecil.Cil.Instruction i)
    {
        var c = i.OpCode.Code;
        if (c >= Mono.Cecil.Cil.Code.Ldc_I4_0 && c <= Mono.Cecil.Cil.Code.Ldc_I4_8)
            return c - Mono.Cecil.Cil.Code.Ldc_I4_0;
        return c switch
        {
            Mono.Cecil.Cil.Code.Ldc_I4_M1 => -1,
            Mono.Cecil.Cil.Code.Ldc_I4_S => (sbyte)i.Operand,
            Mono.Cecil.Cil.Code.Ldc_I4 => (int)i.Operand,
            _ => throw new InvalidOperationException($"not a constant: {i}"),
        };
    }

    private static bool HasGenericOwner(TypeDefinition t)
    {
        for (; t != null; t = t.DeclaringType)
            if (t.HasGenericParameters)
                return true;
        return false;
    }

    // ---------------------------------------------------------------- docs/coop/PLAYER_READS.md

    private static string Report(List<PlayerReadSite> sites)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append('\n');
        string Count(PlayerReadKind k) => $"{k} {sites.Count(s => s.Kind == k)}";

        Line("# Player reads in Skul's code, by kind (co-op)");
        Line();
        Line("Generated by `DamageInsight.Tests/CoopPreloaderTests.cs` from the game's Assembly-CSharp.dll; don't edit by hand.");
        Line("After a game update the test fails when a read was added, moved or reclassified: review the change, adjust");
        Line("`DamageInsight.Preloader/PlayerReadRules.cs` if needed, then run the tests with `UPDATE_PLAYER_READS=1`.");
        Line("Plan: docs/COOP_PLAN.md §6. Research: docs/coop/GAME_FLOW.md (appendix A).");
        Line();
        Line($"**{sites.Count} reads** of `LevelManager.player` in {sites.Select(s => s.Method).Distinct().Count()} methods: " +
             $"{Count(PlayerReadKind.Local)}, {Count(PlayerReadKind.Owner)}, {Count(PlayerReadKind.Target)}, {Count(PlayerReadKind.World)}; " +
             $"unclassified {sites.Count(s => !s.Classified)}. List hash `{PlayerReads.Hash(sites)}` (`SkulInsightCoop.PlayerReads.SitesHash`).");
        Line();
        Line("## What the preloader does");
        Line();
        Line("Only with `[Coop] Enabled = true` in `BepInEx/config/docrun.skulinsight_qol.cfg` (read as text at game start;");
        Line("anything else = the game's code untouched). It adds `public static class SkulInsightCoop.PlayerReads` to");
        Line("Assembly-CSharp and replaces each non-Local `manager.player` with");
        Line("`PlayerReads.Resolve<Kind>(manager, self, siteId)`; `self` is the object whose code runs (`this`: the item, the");
        Line("enemy's AI, the coroutine or lambda object; null in static methods and constructors). The plugin sets");
        Line("`public static Func<LevelManager, object, int, Character> Owner, Target, World` by reflection; while a field");
        Line("is null its resolver returns the original `manager.player`. Constants: `ApiVersion`, `SiteCount`,");
        Line("`UnclassifiedSites`, `Kinds` (one letter per site id: L, O, T, W), `SiteList` (the keys by id, one per line),");
        Line("`SitesHash`.");
        Line();
        Line("| Kind | Meaning | What the plugin returns |");
        Line("|---|---|---|");
        Line("| Local | HUD, UI, camera, input, menus, saves, achievements, private pickups and services | unchanged: this game's player |");
        Line("| Owner | gear, abilities, and code shared by players and enemies (operations, projectiles) | the character whose code runs (`self` → its owner); an enemy → its target |");
        Line("| Target | enemy and boss code only | that enemy's target (threat table for bosses) |");
        Line("| World | level, map, NPC, cutscene and runnable scripts | decided per case (host's player, nearest, every player, or this game's) |");
        Line();
        Line("## Rules (first match wins)");
        Line();
        Line("1. `override Type::Method`: a method (the source method of a lambda or coroutine) that differs from its type.");
        Line("2. `pattern`: the read only feeds `cameraController.StartTrack(player.transform)` or `player.cinematic` → Local.");
        Line("3. `override Type`: a type that differs from its namespace.");
        Line("4. `namespace X`: the longest matching namespace of the top-level type.");
        Line();
        Line("Lambdas, coroutines and local functions (compiler-made nested types) count as their outer type and method.");
        Line();
        Line("| Rule | Kind | Reads | Why |");
        Line("|---|---|---|---|");
        foreach (var g in sites.GroupBy(s => s.Rule ?? "(none)").OrderBy(g => RuleOrder(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal))
            Line($"| {g.Key} | {g.First().Kind} | {g.Count()} | {PlayerReadRules.Why(g.Key == "(none)" ? null : g.Key)} |");
        Line();
        Line("## Riskiest sites");
        Line();
        var deferred = sites.Where(s => s.Kind != PlayerReadKind.Local && s.Deferred != "").ToList();
        var noSelf = sites.Where(s => s.Kind != PlayerReadKind.Local && (!s.Method.HasThis || s.Method.IsConstructor || s.Method.DeclaringType.IsValueType)).ToList();
        Line($"- **Reads that run later** ({deferred.Count} non-Local reads in coroutines, lambdas, local functions): `self` is the");
        Line("  compiler's object; the plugin follows its `<>4__this` field to the outer object (cache the field per type).");
        Line($"- **Reads without `self`** ({noSelf.Count}, static methods or constructors): the plugin can't see whose code runs" +
             (noSelf.Count > 0 ? ": " + string.Join(", ", noSelf.Select(s => $"#{s.Id}")) + "." : "."));
        Line("- `Characters.Operations.Attack.PlayerAttack`: an attack applied straight to the player; on a client it must");
        Line("  only ever hit the local player (victim-side hits, COOP_PLAN §5.5).");
        Line("- `SpawnCharacter`, `SpawnRandomCharacter`, `SpawnRandomEnemy`: spawned enemies should inherit the spawner's target.");
        Line("- `Runnables.Target`: the `Type.Player` target of every Runnable in the game's prefabs (one site, many uses).");
        Line("- `Runnables.ShowHardmodeEndingCredit` (removes items), `CutScenes.Shots.Sequences.GetWeapon` (ForceEquipAt),");
        Line("  `CutScenes.Shots.Events.DestroyPlayer`: World reads with lasting effects; keep this game's player unless sure.");
        Line("- `Level.Chapter2.Elevator`, `Level.OverridedStartPositionSetter`, `Runnables.AttachStartPositionOverrider`: move");
        Line("  or place the player: in co-op every player must be carried or placed.");
        Line("- Boss scripts are Target, but their camera follows and cinematic locks stay Local (rule 2 and overrides).");
        Line("- Not covered by this list: layer 9 overlaps, `Character.type == Player` checks, `PlayerInput.blocked`,");
        Line("  `playerComponents` (GAME_FLOW.md appendix A).");
        Line();
        Line("## The list");
        Line();
        Line("| Id | Kind | Type | Method | Runs | Rule |");
        Line("|---|---|---|---|---|---|");
        foreach (var s in sites)
        {
            int cut = s.Key.IndexOf("::", StringComparison.Ordinal);
            Line($"| {s.Id} | {s.Kind} | {Md(s.Key.Substring(0, cut))} | {Md(s.Key.Substring(cut + 2))} | {s.Deferred} | {s.Rule ?? "(none)"} |");
        }
        return sb.ToString();
    }

    private static int RuleOrder(string rule) =>
        rule.StartsWith("override ") && rule.Contains("::") ? 0 : rule.StartsWith("pattern") ? 1 : rule.StartsWith("override ") ? 2 : rule.StartsWith("namespace ") ? 3 : 4;

    private static string Md(string s) => "`" + s.Replace("|", "\\|") + "`";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DamageInsight", "Plugin.cs")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repository root");
    }
}

/// <summary>
/// Runs in its own AppDomain with a game DLL (patched or not): calls the injected resolvers and JIT-compiles the
/// given methods. Returns [status of the resolvers, then "ok" or the error per method].
/// </summary>
public sealed class PatchedGameProbe : MarshalByRefObject
{
    private static object _self;
    private static int _siteId = -1;

    public static object Record(object manager, object self, int siteId)
    {
        _self = self;
        _siteId = siteId;
        return null;
    }

    public string[] Run(byte[] image, int[] tokens, bool callResolvers)
    {
        var asm = Assembly.Load(image);
        var results = new List<string> { callResolvers ? Resolvers(asm) : "ok" };
        foreach (int token in tokens)
        {
            try
            {
                RuntimeHelpers.PrepareMethod(asm.ManifestModule.ResolveMethod(token).MethodHandle);
                results.Add("ok");
            }
            catch (Exception e) { results.Add($"{e.GetType().Name}: {e.Message}"); }
        }
        return results.ToArray();
    }

    private static string Resolvers(Assembly asm)
    {
        var api = asm.GetType("SkulInsightCoop.PlayerReads", true);
        var owner = api.GetField("Owner");
        var resolve = api.GetMethod("ResolveOwner");
        string status = "ok";

        // No delegate: the original manager.player (on a null manager: the NullReferenceException the game would get).
        try
        {
            resolve.Invoke(null, new object[] { null, null, 1 });
            status = "ResolveOwner without a delegate didn't read manager.player";
        }
        catch (TargetInvocationException e) when (e.InnerException is NullReferenceException) { }

        // With a delegate: it gets the arguments, its answer is returned.
        var p = owner.FieldType.GetMethod("Invoke").GetParameters().Select(x => Expression.Parameter(x.ParameterType)).ToArray();
        var body = Expression.Convert(Expression.Call(typeof(PatchedGameProbe).GetMethod(nameof(Record)),
            Expression.Convert(p[0], typeof(object)), p[1], p[2]), resolve.ReturnType);
        owner.SetValue(null, Expression.Lambda(owner.FieldType, body, p).Compile());
        object answer = resolve.Invoke(null, new object[] { null, "me", 42 });
        if (answer != null || !"me".Equals(_self) || _siteId != 42)
            status = $"ResolveOwner with a delegate: self {_self}, site {_siteId}, answer {answer}";
        if ((int)api.GetField("SiteCount").GetValue(null) <= 0)
            status = "SiteCount missing";
        return status;
    }
}
