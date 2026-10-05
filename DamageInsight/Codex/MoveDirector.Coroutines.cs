#if DEV // a developer tool: release builds don't lead bosses
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Characters.AI;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// Bosses run by their own code (the Leiana sisters, Awakened Leiana, Chimera): their AI picks a pattern and calls an
/// attack coroutine (CastRush, CastBite). There is no tree to lead, so the call itself is changed: when the AI starts
/// an attack that is filmed already, the first missing attack of the same kind starts instead. Of the same kind: started
/// by the same dispatcher (Chimera's RunPattern), or, when the conductor sits elsewhere (the sisters' master AI), in the
/// same fight part ("Pair phase", "Single phase"). Attacks started inside another attack (the twin meteor's meteor) are
/// left alone, and so are moves outside the patterns (intro, awakening, death).
/// </summary>
public static partial class MoveDirector
{
    private const int SwapTries = 3;        // swapped in this often and still not on film: put aside

    private sealed class Swapper
    {
        public Led Led;
        public FightRecorder.Directed Film;
        public readonly Dictionary<MethodBase, AttackGraph.Unit> Units = new();
    }

    private static readonly Dictionary<object, Swapper> SwapOwners = new();   // AI component → its boss's attacks
    private static readonly HashSet<MethodBase> SwapPatched = new();
    private static Harmony _swapHarmony;
    private static bool _swapping;

    /// <summary>Refresh: the boss's attack coroutines on its AI may be swapped (hooked once per method).</summary>
    private static void WatchCoroutines(FightRecorder.Directed film, Led led)
    {
        foreach (var attack in film.Graph.Attacks.Where(a => !a.IsTail))
            foreach (var unit in attack.Units)
            {
                if (unit.Method is not MethodInfo method || method.IsStatic || method.ReturnType != typeof(IEnumerator) ||
                    unit.Owner.Value is not AIController owner || owner == null)
                    continue;
                if (!SwapOwners.TryGetValue(owner, out var swapper))
                    SwapOwners[owner] = swapper = new Swapper { Led = led, Film = film };
                swapper.Units[method] = unit;
                if (SwapPatched.Add(method))
                {
                    try
                    {
                        _swapHarmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID + ".director");
                        _swapHarmony.Patch(method, prefix: new HarmonyMethod(typeof(MoveDirector), nameof(SwapPrefix)));
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Move director: can't swap {unit}: {e.Message}");
                    }
                }
            }
    }

    /// <summary>Before an attack coroutine of a watched AI is made: maybe the coroutine of a missing attack instead.</summary>
    private static bool SwapPrefix(object __instance, MethodBase __originalMethod, object[] __args, ref IEnumerator __result)
    {
        if (_swapping || !MovePicker.On || SwapOwners.Count == 0)
            return true;
        try
        {
            if (SwapFor(__instance, __originalMethod, __args) is not { } swap)
                return true;
            _swapping = true;
            __result = (IEnumerator)swap.method.Invoke(__instance, swap.args);
            return __result == null;
        }
        catch (Exception e)
        {
            Patches.Guard.Report("Move director (swap)", e);
            return true;
        }
        finally
        {
            _swapping = false;
        }
    }

    private static (MethodInfo method, object[] args)? SwapFor(object owner, MethodBase called, object[] args)
    {
        if (!SwapOwners.TryGetValue(owner, out var s) || !s.Units.TryGetValue(called, out var from) || Patches.AttackTracker.InsideAttack())
            return null;
        var graph = s.Film.Graph;
        var fromAttack = graph.AttackOf(from);
        if (fromAttack == null || fromAttack.IsTail || s.Film.Missing.Contains(fromAttack.Label))
            return null; // a move still missing anyway: let it come
        float now = Time.unscaledTime;
        var led = s.Led;
        if (led.Waiting != null)
            return null; // the last swapped-in move is still being filmed
        foreach (var label in s.Film.Missing)
        {
            if (led.Aside.TryGetValue(label, out float until) && now < until)
                continue;
            var attack = graph.Attacks.FirstOrDefault(a => a.Label == label && !a.IsTail);
            var to = attack?.Units
                .Where(u => u != from && u.Method is MethodInfo m && s.Units.ContainsKey(m) && ReferenceEquals(u.Owner.Value, owner) &&
                            Alike(from, fromAttack, u, attack))
                .OrderByDescending(u => u.Steps.Count).FirstOrDefault();
            if (to == null)
                continue;
            led.Swaps.TryGetValue(label, out int tries);
            tries++;
            led.Swaps[label] = tries >= SwapTries ? 0 : tries;
            Wait(led, label);
            if (tries >= SwapTries)
                led.Aside[label] = now + AsideLong;
            led.SwapText = $"Director: {label} instead of {fromAttack.Label}";
            Plugin.Log.LogInfo($"Move director: {led.Key}: {label} instead of {fromAttack.Label}" +
                               (tries > 1 ? $" (try {tries}{(tries >= SwapTries ? ", then put aside" : "")})." : "."));
            var method = (MethodInfo)to.Method;
            return (method, ArgsFor(method, args));
        }
        return null;
    }

    /// <summary>Two attacks of the same kind: the same dispatcher starts them, or else the same fight part.</summary>
    private static bool Alike(AttackGraph.Unit a, AttackGraph.Attack aa, AttackGraph.Unit b, AttackGraph.Attack ba)
    {
        var callersA = a.CalledBy.Where(c => c.Dispatcher).ToList();
        var callersB = b.CalledBy.Where(c => c.Dispatcher).ToList();
        if (callersA.Count > 0 || callersB.Count > 0)
            return callersA.Intersect(callersB).Any();
        string sa = Section(aa.Label), sb = Section(ba.Label);
        return sa != null && sa == sb;
    }

    private static string Section(string label)
    {
        int i = label.IndexOf(" · ", StringComparison.Ordinal);
        return i > 0 ? label.Substring(0, i) : null;
    }

    /// <summary>The swapped-in attack's arguments: the called one's where the type fits ("left"), else the defaults.</summary>
    private static object[] ArgsFor(MethodInfo method, object[] args)
    {
        var parameters = method.GetParameters();
        var result = new object[parameters.Length];
        var used = new bool[args?.Length ?? 0];
        for (int i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            int found = -1;
            for (int j = 0; j < used.Length && found < 0; j++)
                if (!used[j] && args[j] != null && type.IsInstanceOfType(args[j]))
                    found = j;
            if (found >= 0)
            {
                used[found] = true;
                result[i] = args[found];
            }
            else
                result[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        return result;
    }
}
#endif
