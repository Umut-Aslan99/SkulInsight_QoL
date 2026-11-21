using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace DamageInsight.Preloader;

/// <summary>
/// Finds every read of LevelManager.player in Assembly-CSharp and rewrites the ones that aren't Local:
/// <c>manager.player</c> becomes <c>SkulInsightCoop.PlayerReads.Resolve&lt;Kind&gt;(manager, this, siteId)</c>.
/// The injected class asks the plugin's delegate (set at runtime by reflection) and falls back to the original
/// <c>manager.player</c> while none is set, so the game behaves as before until co-op takes over. Passing <c>this</c>
/// (the item, the enemy's AI, the coroutine or lambda object) lets the plugin find whose code runs, also in later
/// frames, instead of one global "current owner" switch like earlier co-op mods.
/// </summary>
public static class PlayerReads
{
    public const string InjectedNamespace = "SkulInsightCoop";
    public const string InjectedName = "PlayerReads";

    /// <summary>Raised when the injected API changes in a way the plugin must know.</summary>
    public const int ApiVersion = 1;

    public static bool IsGetPlayer(Instruction ins) =>
        (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) && ins.Operand is MethodReference m &&
        m.Name == "get_player" && !m.HasParameters && m.DeclaringType.FullName == "Level.LevelManager";

    /// <summary>Every read, sorted by key, with its id and kind.</summary>
    public static List<PlayerReadSite> Scan(ModuleDefinition module)
    {
        var sites = new List<PlayerReadSite>();
        foreach (var type in module.GetTypes())
        {
            if (type.Namespace == InjectedNamespace)
                continue;
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                int n = 0;
                foreach (var ins in method.Body.Instructions)
                    if (IsGetPlayer(ins))
                        sites.Add(new PlayerReadSite(method, ins, n++));
            }
        }
        sites.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        for (int i = 0; i < sites.Count; i++)
        {
            sites[i].Id = i;
            PlayerReadRules.Classify(sites[i], out var kind, out var rule);
            sites[i].Kind = kind;
            sites[i].Rule = rule;
        }
        return sites;
    }

    /// <summary>FNV-1a over "key=kind" lines: the plugin can compare it with the reviewed list.</summary>
    public static string Hash(IEnumerable<PlayerReadSite> sites)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(string.Concat(sites.Select(s => $"{s.Key}={Letter(s.Kind)}\n"))))
            h = (h ^ b) * 1099511628211UL;
        return h.ToString("x16");
    }

    public static char Letter(PlayerReadKind kind) => kind.ToString()[0];

    /// <summary>Injects SkulInsightCoop.PlayerReads and rewrites the reads; returns the sites (null if already patched).</summary>
    public static List<PlayerReadSite> Patch(ModuleDefinition module)
    {
        if (module.GetType(InjectedNamespace, InjectedName) != null)
            return null;
        var sites = Scan(module);
        var api = Inject(module, sites);
        foreach (var group in sites.Where(s => s.Kind != PlayerReadKind.Local).GroupBy(s => s.Method))
        {
            var body = group.Key.Body;
            body.SimplifyMacros(); // short branches may no longer reach after the inserts
            var il = body.GetILProcessor();
            bool self = group.Key.HasThis && !group.Key.IsConstructor && !group.Key.DeclaringType.IsValueType;
            foreach (var site in group)
            {
                // [manager] get_player  →  [manager] this|null, siteId, Resolve<Kind>. The original instruction becomes
                // the first new one, so branches that land on it still land on the read.
                var ins = site.Instruction;
                ins.OpCode = self ? OpCodes.Ldarg_0 : OpCodes.Ldnull;
                ins.Operand = null;
                var id = Instruction.Create(OpCodes.Ldc_I4, site.Id);
                il.InsertAfter(ins, id);
                il.InsertAfter(id, Instruction.Create(OpCodes.Call, api[site.Kind]));
            }
            body.OptimizeMacros();
        }
        return sites;
    }

    /// <summary>
    /// The injected class: <c>public static Func&lt;LevelManager, object, int, Character&gt; Owner, Target, World</c>,
    /// <c>Resolve&lt;Kind&gt;(LevelManager manager, object self, int siteId)</c>, and constants describing the list
    /// (ApiVersion, SiteCount, Kinds = one letter per site id, SiteList = the keys by id, one per line, SitesHash,
    /// UnclassifiedSites).
    /// </summary>
    private static Dictionary<PlayerReadKind, MethodReference> Inject(ModuleDefinition module, List<PlayerReadSite> sites)
    {
        var levelManager = module.GetType("Level.LevelManager");
        var getPlayer = levelManager.Methods.Single(m => m.Name == "get_player" && !m.HasParameters);
        var character = getPlayer.ReturnType;

        var cls = new TypeDefinition(InjectedNamespace, InjectedName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(cls);

        void Const(string name, TypeReference type, object value) =>
            cls.Fields.Add(new FieldDefinition(name, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, type) { Constant = value });
        Const("ApiVersion", module.TypeSystem.Int32, ApiVersion);
        Const("SiteCount", module.TypeSystem.Int32, sites.Count);
        Const("UnclassifiedSites", module.TypeSystem.Int32, sites.Count(s => !s.Classified));
        Const("Kinds", module.TypeSystem.String, new string(sites.Select(s => Letter(s.Kind)).ToArray()));
        Const("SiteList", module.TypeSystem.String, string.Join("\n", sites.Select(s => s.Key)));
        Const("SitesHash", module.TypeSystem.String, Hash(sites));

        // System.Func`4<LevelManager, object, int, Character> and its Invoke(!0, !1, !2) : !3.
        var func = new TypeReference("System", "Func`4", module, module.TypeSystem.CoreLibrary);
        for (int i = 0; i < 4; i++)
            func.GenericParameters.Add(new GenericParameter(func));
        var funcType = new GenericInstanceType(func);
        funcType.GenericArguments.Add(levelManager);
        funcType.GenericArguments.Add(module.TypeSystem.Object);
        funcType.GenericArguments.Add(module.TypeSystem.Int32);
        funcType.GenericArguments.Add(character);
        var invoke = new MethodReference("Invoke", func.GenericParameters[3], funcType) { HasThis = true };
        for (int i = 0; i < 3; i++)
            invoke.Parameters.Add(new ParameterDefinition(func.GenericParameters[i]));

        var api = new Dictionary<PlayerReadKind, MethodReference>();
        foreach (var kind in new[] { PlayerReadKind.Owner, PlayerReadKind.Target, PlayerReadKind.World })
        {
            var field = new FieldDefinition(kind.ToString(), FieldAttributes.Public | FieldAttributes.Static, funcType);
            cls.Fields.Add(field);

            var resolve = new MethodDefinition("Resolve" + kind, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, character);
            resolve.Parameters.Add(new ParameterDefinition("manager", ParameterAttributes.None, levelManager));
            resolve.Parameters.Add(new ParameterDefinition("self", ParameterAttributes.None, module.TypeSystem.Object));
            resolve.Parameters.Add(new ParameterDefinition("siteId", ParameterAttributes.None, module.TypeSystem.Int32));
            resolve.Body.InitLocals = true;
            resolve.Body.Variables.Add(new VariableDefinition(funcType));
            var il = resolve.Body.GetILProcessor();
            var original = il.Create(OpCodes.Ldarg_0);
            // var f = Owner; if (f != null) return f(manager, self, siteId); return manager.player;
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Brfalse_S, original);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, invoke);
            il.Emit(OpCodes.Ret);
            il.Append(original);
            il.Emit(OpCodes.Callvirt, getPlayer);
            il.Emit(OpCodes.Ret);
            cls.Methods.Add(resolve);
            api[kind] = resolve;
        }
        return api;
    }
}
