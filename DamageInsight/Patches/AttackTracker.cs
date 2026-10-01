using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DamageInsight.Codex;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Tells the fight recorder when a boss starts and finishes an attack. The attack coroutines AttackGraph found in
/// the boss's AI are hooked (their compiled MoveNext) once a boss is filmed, BehaviorDesigner tasks by their
/// OnStart / OnEnd; remembering which coroutine waits for
/// which tells an attack apart from the helpers and smaller attacks it runs, and one pattern of a dispatcher
/// ("RunPattern": backstep, then meteor) apart from the next.
/// </summary>
public static class AttackTracker
{
    /// <summary>One running coroutine of a tracked unit.</summary>
    public sealed class Run
    {
        public AttackGraph Graph;
        public AttackGraph.Unit Unit;
        public object Parent;         // the coroutine that waits for this one, if known
    }

    private static Harmony _harmony;
    private static readonly HashSet<Type> Patched = new();
    private static readonly Dictionary<Type, (FieldInfo state, FieldInfo self)> Fields = new();
    private static readonly Dictionary<(object owner, Type machine), (AttackGraph graph, AttackGraph.Unit unit)> UnitOf = new();
    private static readonly Dictionary<object, Run> Live = new();
    private static readonly Dictionary<object, object> ParentOf = new();

    /// <summary>(run, coroutine): a tracked coroutine started or finished.</summary>
    public static event Action<Run, object> Started, Ended;

    public static bool TryGet(object coroutine, out Run run) => Live.TryGetValue(coroutine, out run);

    /// <summary>
    /// Hooks the attack and dispatcher coroutines of a boss (each compiled coroutine class once), and the
    /// BehaviorDesigner tasks of its tree (OnStart / OnEnd of each task class once).
    /// </summary>
    public static void Track(AttackGraph graph)
    {
        foreach (var key in UnitOf.Keys.Where(k => k.owner is UnityEngine.Object o && o == null).ToList())
            UnitOf.Remove(key);
        foreach (var unit in graph.Units.Where(u => u.Owner.Value is BehaviorDesigner.Runtime.Tasks.Task && (u.Entry || u.Dispatcher)))
            TrackTask(graph, unit);
        foreach (var unit in graph.Units)
        {
            if (unit.StateMachine == null || !(unit.Entry || unit.Dispatcher) || unit.Owner.Value == null)
                continue;
            UnitOf[(unit.Owner.Value, unit.StateMachine)] = (graph, unit);
            if (!Patched.Add(unit.StateMachine))
                continue;
            try
            {
                var moveNext = AccessTools.Method(unit.StateMachine, "MoveNext");
                var state = AccessTools.Field(unit.StateMachine, "<>1__state");
                var self = AccessTools.Field(unit.StateMachine, "<>4__this");
                if (moveNext == null || state == null || self == null)
                    continue;
                Fields[unit.StateMachine] = (state, self);
                _harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID + ".attacks");
                _harmony.Patch(moveNext, prefix: new HarmonyMethod(typeof(AttackTracker), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(AttackTracker), nameof(Postfix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Codex: could not follow {unit}: {e.Message}");
            }
        }
    }

    private static readonly HashSet<MethodBase> PatchedTaskMethods = new();

    private static void TrackTask(AttackGraph graph, AttackGraph.Unit unit)
    {
        var type = unit.Owner.Value.GetType();
        UnitOf[(unit.Owner.Value, type)] = (graph, unit);
        try
        {
            _harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID + ".attacks");
            if (AccessTools.Method(type, "OnStart") is { } start && PatchedTaskMethods.Add(start))
                _harmony.Patch(start, prefix: new HarmonyMethod(typeof(AttackTracker), nameof(TaskStarted)));
            if (AccessTools.Method(type, "OnEnd") is { } end && PatchedTaskMethods.Add(end))
                _harmony.Patch(end, prefix: new HarmonyMethod(typeof(AttackTracker), nameof(TaskEnded)));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not follow the task {unit}: {e.Message}");
        }
    }

    private static void TaskStarted(object __instance) => Guard.Run("Codex attack tracking", () =>
    {
        if (!UnitOf.TryGetValue((__instance, __instance.GetType()), out var tracked))
            return;
        if (Live.TryGetValue(__instance, out var old))
        {
            Live.Remove(__instance); // restarted without an end (aborted): finish the old run first
            Ended?.Invoke(old, __instance);
        }
        if (Live.Count > 256)
            Live.Clear();
        var run = new Run { Graph = tracked.graph, Unit = tracked.unit };
        Live[__instance] = run;
        Started?.Invoke(run, __instance);
    });

    private static void TaskEnded(object __instance) => Guard.Run("Codex attack tracking", () =>
    {
        if (Live.TryGetValue(__instance, out var run))
        {
            Live.Remove(__instance);
            Ended?.Invoke(run, __instance);
        }
    });

    /// <summary>Forgets the running coroutines of a boss that is gone.</summary>
    public static void Forget(AttackGraph graph)
    {
        foreach (var key in Live.Where(p => p.Value.Graph == graph).Select(p => p.Key).ToList())
            Live.Remove(key);
        if (Live.Count == 0)
            ParentOf.Clear();
    }

    private static void Prefix(object __instance) => Guard.Run("Codex attack tracking", () => Enter(__instance));

    private static void Postfix(object __instance, bool __result) => Guard.Run("Codex attack tracking", () => Leave(__instance, __result));

    private static void Enter(object coroutine)
    {
        if (Live.ContainsKey(coroutine) || !Fields.TryGetValue(coroutine.GetType(), out var f) || (int)f.state.GetValue(coroutine) != 0)
            return;
        var owner = f.self.GetValue(coroutine);
        if (owner == null || !UnitOf.TryGetValue((owner, coroutine.GetType()), out var tracked))
            return;
        ParentOf.TryGetValue(coroutine, out var parent);
        ParentOf.Remove(coroutine);
        if (Live.Count > 256)
            Live.Clear(); // coroutines stopped from outside never finish: don't let them pile up
        var run = new Run { Graph = tracked.graph, Unit = tracked.unit, Parent = parent };
        Live[coroutine] = run;
        Started?.Invoke(run, coroutine);
    }

    private static void Leave(object coroutine, bool more)
    {
        if (!Live.TryGetValue(coroutine, out var run))
            return;
        if (!more)
        {
            Live.Remove(coroutine);
            Ended?.Invoke(run, coroutine);
        }
        else if (((IEnumerator)coroutine).Current is IEnumerator child)
        {
            if (ParentOf.Count > 512)
                ParentOf.Clear();
            ParentOf[child] = coroutine;
        }
    }

    /// <summary>
    /// Where a started coroutine runs: inside another attack (nested: not an attack of its own), or in one pattern
    /// of a dispatcher (the dispatcher's coroutine; attacks of one pattern belong together).
    /// </summary>
    public static (bool nested, object pattern) Context(Run run)
    {
        for (var p = run.Parent; p != null && Live.TryGetValue(p, out var outer); p = outer.Parent)
        {
            if (outer.Unit.Dispatcher)
                return (false, outer.Graph.InTree(outer.Unit) ? null : p); // tree blocks pick one attack at a time
            if (outer.Unit.Entry)
                return (true, null);
        }
        return (false, null);
    }
}
