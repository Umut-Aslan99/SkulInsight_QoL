using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Characters.AI;
using Characters.AI.Behaviours.Yggdrasill;
using Characters.AI.YggdrasillElderEnt;
using HarmonyLib;
using Singletons;
using UnityEngine;
using AiBehaviour = Characters.AI.Behaviours.Behaviour;
using ChimeraAI = Characters.AI.Chimera.Chimera;
using ChimeraCombat = Characters.AI.Chimera.ChimeraCombat;
using ChimeraPattern = Characters.AI.Chimera.Pattern;
using DarkAideAI = Characters.AI.TwinSister.DarkAideAI;
using PopeB = Characters.AI.Behaviours.Pope;
using DarkRush = Characters.AI.TwinSister.DarkRush;
using GameAction = Characters.Actions.Action;
using RangeAttack = Characters.AI.TwinSister.RangeAttack;
using SequentialActivateObject = Characters.Operations.SequentialActivateObject;
using SubjectDrop = Characters.AI.Behaviours.Chimera.SubjectDrop;
using ThunderAttack = Characters.AI.TwinSister.ThunderAttack;
using VenomFall = Characters.AI.Behaviours.Chimera.VenomFall;

namespace DamageInsight.Coop;

/// <summary>
/// Bosses on clients (COOP_LOG 21/23; Umut 2026-11-07: "the boss attacks are totally desynced"). A boss's fight is
/// mostly not character moves: Yggdrasil's attacks are AI behaviours that play its own animations, and animation
/// events move the hands and fire the energy balls, so the motion replay of normal enemies saw nothing (0 moves from
/// the boss in a trace) and each game ran its own boss.
/// - Clients: no puppet's AI runs its own main loop (`CProcess`; many AIs start it in Awake, which a switched-off
///   component doesn't stop). Applied when co-op starts (it patches every AI class), not for every player.
/// - Host: when a behaviour of a type in <see cref="Replayed"/> starts, EnemySync sends it (its path below the enemy,
///   the boss's target, a random pick made for it here); a client runs the same behaviour on its puppet.
/// - Phases: the host's phase change is sent; a client's own (its health hitting 0 by this player's hits) is blocked.
/// - Calls (a second style, for bosses whose attacks go through one method): the host sends each call of a listed
///   method (Chimera.RunPattern) with its argument and a call number; a client runs the same call. Random spots
///   inside it come from a seed both games make from the room, the boss, the call number and the method.
/// - Helper calls (a call style for AI-side helpers with their own code between moves): the client runs the whole
///   helper itself (its moves included: the host's copies of those moves are skipped while it runs), with Unity's
///   Random seeded the same way on both games for every step of it (<see cref="Steps"/>).
/// Each boss is added here once checked in co-op (COOP_PLAN §10 phase 2): Yggdrasil, Chimera so far; Leiana's
/// sisters fight with moves (the motion replay), except the Dark Mirror Dark sister's helpers (DarkRush,
/// ThunderAttack, RangeAttack: COOP_LOG 35-36).
/// </summary>
internal static class BossSync
{
    /// <summary>Behaviour types replayed on clients (their work isn't a character move).</summary>
    internal static readonly Type[] Replayed =
    {
        typeof(Sweeping),
        typeof(PlayAnimations),
        typeof(Awakening),
    };

    /// <summary>A random pick inside a replayed behaviour: made on the host before it runs, sent as one byte (0 = none).</summary>
    private static readonly Dictionary<Type, (Func<AiBehaviour, byte> pick, Action<AiBehaviour, byte> force)> Picks = new()
    {
        [typeof(Sweeping)] = (Yggdrasil.PickSweepSide, Yggdrasil.ForceSweepSide),
    };

    /// <summary>Phase changes per AI type: what a client does when the host's boss changes phase.</summary>
    private static readonly Dictionary<Type, Action<AIController, byte>> Phases = new()
    {
        [typeof(YggdrasillElderEntAI)] = Yggdrasil.EnterPhase,
    };

    public static byte PickOnHost(AiBehaviour behaviour) =>
        Picks.TryGetValue(behaviour.GetType(), out var p) ? p.pick(behaviour) : (byte)0;

    public static void ForceOnClient(AiBehaviour behaviour, byte extra)
    {
        if (extra != 0 && Picks.TryGetValue(behaviour.GetType(), out var p))
            p.force(behaviour, extra);
    }

    /// <summary>Client: the host's boss changed phase; false when this boss has no phase handler.</summary>
    public static bool EnterPhase(AIController ai, byte phase)
    {
        if (ai == null || !Phases.TryGetValue(ai.GetType(), out var enter))
            return false;
        enter(ai, phase);
        return true;
    }

    /// <summary>A behaviour's path below <paramref name="root"/>, with its index among same-type components there.</summary>
    public static string PathOf(AiBehaviour behaviour, Transform root)
    {
        var same = behaviour.GetComponents(behaviour.GetType());
        return RemoteAvatar.PathOf(behaviour.transform, root) + "@" + Array.IndexOf(same, behaviour);
    }

    // ------------------------------------------------------------------ patches

    [HarmonyPatch]
    public static class HostBehaviourStart
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            Replayed.Select(t => (MethodBase)AccessTools.Method(t, nameof(AiBehaviour.CRun)));

        private static void Prefix(AiBehaviour __instance, AIController controller) =>
            EnemySync.Current?.OnHostBehaviour(__instance, controller);
    }

    /// <summary>Clients: a puppet's AI doesn't run its own main loop (applied when co-op starts: <see cref="EnsureApplied"/>).</summary>
    [HarmonyPatch]
    public static class NoPuppetMainLoop
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(AIController).Assembly.GetTypes()
                .Where(t => typeof(AIController).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => t.GetMethod("CProcess", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m != null && !m.IsAbstract)
                .Cast<MethodBase>();

        private static bool Prefix(AIController __instance, ref IEnumerator __result)
        {
            if (!EnemySync.IsPuppetAi(__instance) || MainLoopAllowed.Contains(__instance.GetType()))
                return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }

    /// <summary>AIs whose main loop has no fighting in it and is needed on clients too (Chimera's: its sleep
    /// animation and its death scene; its fight comes from Chapter3Script, see Chimera below).</summary>
    private static readonly HashSet<Type> MainLoopAllowed = new() { typeof(ChimeraAI) };

    // ------------------------------------------------------------------ calls

    /// <summary>A replayed boss method: a coroutine (it replaces the boss's running replay) or an instant call.</summary>
    internal sealed class CallInfo
    {
        public Type Ai;
        public Func<AIController, byte, float, IEnumerator> Routine;
        public Action<AIController, byte, float> Instant;
        /// <summary>The AI-side helper the routine runs: the moves it starts itself are its own on clients (the
        /// host's copies of them are skipped while it runs).</summary>
        public Func<AIController, Component> Helper;
    }

    internal static readonly Dictionary<string, CallInfo> Calls = new()
    {
        // Dark Mirror Leiana's Dark sister (COOP_LOG 35): code between her moves that the motion replay can't see.
        ["DarkRush.CRun"] = new CallInfo
        {
            Ai = typeof(DarkAideAI),
            Helper = ai => ((DarkAideAI)ai)._darkRush,
            Routine = (ai, _, _) => ((DarkAideAI)ai)._darkRush.CRun((DarkAideAI)ai),
        },
        ["ThunderAttack.CRun"] = new CallInfo
        {
            Ai = typeof(DarkAideAI),
            Helper = ai => ((DarkAideAI)ai)._thunderAttack,
            Routine = (ai, _, _) => ((DarkAideAI)ai)._thunderAttack.CRun(ai),
        },
        ["RangeAttack.CRun"] = new CallInfo
        {
            Ai = typeof(DarkAideAI),
            Helper = ai => ((DarkAideAI)ai)._rangeAttack,
            Routine = (ai, _, _) => ((DarkAideAI)ai)._rangeAttack.CRun(ai),
        },
        ["Chimera.RunPattern"] = new CallInfo
        {
            Ai = typeof(ChimeraAI),
            Routine = (ai, arg, _) => ((ChimeraAI)ai).RunPattern((ChimeraPattern)arg),
        },
        ["Chimera.SetAnimationSpeed"] = new CallInfo
        {
            Ai = typeof(ChimeraAI),
            Instant = (ai, _, value) => ((ChimeraAI)ai).SetAnimationSpeed(value),
        },
    };

    // The host's call number per boss (both games: the current one) and how often each seeded method ran in it.
    private static readonly Dictionary<Characters.Character, int> CallSeq = new();
    private static readonly Dictionary<(Characters.Character, string), (int seq, int count)> SeedUses = new();

    /// <summary>Host: the next call number of <paramref name="boss"/> (it becomes the current one).</summary>
    public static ushort NextSeq(Characters.Character boss)
    {
        CallSeq.TryGetValue(boss, out int seq);
        CallSeq[boss] = seq = (seq + 1) & 0xFFFF;
        return (ushort)seq;
    }

    /// <summary>Client: the host's call number that is running now.</summary>
    public static void SetSeq(Characters.Character boss, ushort seq) => CallSeq[boss] = seq;

    public static void ForgetCalls()
    {
        CallSeq.Clear();
        SeedUses.Clear();
    }

    /// <summary>The same seed on every game for the n-th use of <paramref name="method"/> in the boss's current call.</summary>
    private static int Seed(Characters.Character boss, string method)
    {
        CallSeq.TryGetValue(boss, out int seq);
        var key = (boss, method);
        SeedUses.TryGetValue(key, out var use);
        int count = use.seq == seq ? use.count : 0;
        SeedUses[key] = (seq, count + 1);
        uint hash = 2166136261;
        foreach (char c in $"{EnemySync.TraceRoom:x8}/{EnemySync.TraceIdOf(boss)}/{seq}/{method}/{count}")
        {
            hash ^= c;
            hash *= 16777619;
        }
        return (int)hash;
    }

    /// <summary>The boss whose random spots are shared (a bound enemy in co-op), or null (the game's own random).</summary>
    private static Characters.Character SeededBoss(Component component)
    {
        if (EnemySync.Current == null || CoopWorld.Current?.Active != true)
            return null;
        var boss = CoopTargeting.CharacterOf(component);
        return boss != null && EnemySync.TraceIdOf(boss) >= 0 ? boss : null;
    }

    /// <summary>Random spots of a boss pattern: drawn from the shared seed (Unity's Random, put back afterwards).</summary>
    [HarmonyPatch]
    public static class SeededSpots
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(VenomFall), "SetPoints");
            yield return AccessTools.Method(typeof(SubjectDrop), "SetPoints");
            yield return AccessTools.Method(typeof(SequentialActivateObject), "SetParent"); // DarkRush: which sign set
        }

        private static void Prefix(Component __instance, MethodBase __originalMethod, out UnityEngine.Random.State? __state)
        {
            __state = null;
            var boss = SeededBoss(__instance);
            if (boss == null)
                return;
            __state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Seed(boss, __originalMethod.DeclaringType.Name + "." + __originalMethod.Name));
        }

        private static void Postfix(UnityEngine.Random.State? __state)
        {
            if (__state.HasValue)
                UnityEngine.Random.state = __state.Value;
        }
    }

    // ------------------------------------------------------------------ Chimera (chapter 3)

    internal static class Chimera
    {
        /// <summary>Host: every pattern Chimera starts (the fight loop is Chapter3Script's ChimeraCombat.Combat).</summary>
        [HarmonyPatch(typeof(ChimeraAI), nameof(ChimeraAI.RunPattern))]
        public static class SendPattern
        {
            private static void Prefix(ChimeraAI __instance, ChimeraPattern pattern) =>
                EnemySync.Current?.OnHostCall(__instance, "Chimera.RunPattern", (byte)pattern, 0f);
        }

        [HarmonyPatch(typeof(ChimeraAI), nameof(ChimeraAI.SetAnimationSpeed))]
        public static class SendSpeed
        {
            private static void Prefix(ChimeraAI __instance, float speed) =>
                EnemySync.Current?.OnHostCall(__instance, "Chimera.SetAnimationSpeed", 0, speed);
        }

        /// <summary>Clients: Chapter3Script starts Chimera's fight loop on every game; a puppet's picks nothing.</summary>
        [HarmonyPatch(typeof(ChimeraCombat), nameof(ChimeraCombat.Combat))]
        public static class NoPuppetFight
        {
            private static bool Prefix(ChimeraCombat __instance, ref IEnumerator __result)
            {
                if (__instance._chimera == null || !EnemySync.IsPuppetAi(__instance._chimera))
                    return true;
                __result = OneFrame();
                return false;
            }

            private static IEnumerator OneFrame()
            {
                yield return null; // the caller loops while Chimera lives: one frame each time, never a tight loop
            }
        }

        /// <summary>The subject drop: its spots were shared (SetPoints), but the order of the drops is shuffled with a
        /// crypto RNG (no seed): co-op runs the same steps with the order from the shared seed (COOP_LOG 38).</summary>
        [HarmonyPatch(typeof(SubjectDrop), nameof(SubjectDrop.CRun))]
        public static class SeededDropOrder
        {
            private static bool Prefix(SubjectDrop __instance, AIController controller, ref IEnumerator __result)
            {
                var boss = SeededBoss(__instance);
                if (boss == null || controller == null)
                    return true;
                __result = Run(__instance, controller, boss);
                return false;
            }

            /// <summary>SubjectDrop.CRun with the seeded order.</summary>
            private static IEnumerator Run(SubjectDrop drop, AIController controller, Characters.Character boss)
            {
                drop.result = AiBehaviour.Result.Doing;
                drop._coolDown = drop.StartCoroutine(drop.CoolDown(controller.character.chronometer.animation));
                drop.SetPoints();
                var fires = drop._fireOperationInfos;
                var random = new System.Random(Seed(boss, "SubjectDrop.FireOrder"));
                for (int i = fires.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (fires[i], fires[j]) = (fires[j], fires[i]);
                }
                for (int i = 0; i < fires.Length; i++)
                {
                    fires[i].gameObject.SetActive(true);
                    fires[i].Run(controller.character);
                    yield return Chronometer.global.WaitForSeconds(0.5f);
                }
                drop.result = AiBehaviour.Result.Done;
            }
        }

        /// <summary>The order of the four venom falls: the game shuffles with a crypto RNG (no seed), co-op with the
        /// shared seed, so the falls come in the same order on every game.</summary>
        [HarmonyPatch(typeof(VenomFall), nameof(VenomFall.ShuffleOrder))]
        public static class SeededVenomOrder
        {
            private static bool Prefix(VenomFall __instance)
            {
                var boss = SeededBoss(__instance);
                if (boss == null)
                    return true;
                var random = new System.Random(Seed(boss, "VenomFall.ShuffleOrder"));
                int[] order = { 0, 1, 2, 3 };
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                __instance._order = new Queue<int>(order);
                __instance.SetPoints();
                return false;
            }
        }
    }

    // ------------------------------------------------------------------ helper calls (Dark Mirror Leiana)

    /// <summary>
    /// A coroutine run step by step with something around each step: the call's own Unity Random state (the same
    /// seed on every game) and, on clients, the "replaying" mark (its moves aren't the puppet's own). The coroutines
    /// it yields are wrapped the same way: Unity runs them as part of it (TeleportBehind's random distance is drawn
    /// in one of them).
    /// </summary>
    internal sealed class Steps : IEnumerator
    {
        internal sealed class Shared
        {
            public UnityEngine.Random.State? Random;
            public bool Replaying;
        }

        private readonly IEnumerator _inner;
        private readonly Shared _shared;
        private readonly Action _done;

        /// <summary>A seeded step is running now (shuffles inside it draw from its Random: SeededShuffles).</summary>
        public static int SeededDepth { get; private set; }

        public Steps(IEnumerator inner, Shared shared, Action done = null)
        {
            _inner = inner;
            _shared = shared;
            _done = done;
        }

        public object Current { get; private set; }

        public bool MoveNext()
        {
            UnityEngine.Random.State outer = default;
            bool seeded = _shared.Random.HasValue;
            if (seeded)
            {
                outer = UnityEngine.Random.state;
                UnityEngine.Random.state = _shared.Random.Value;
                SeededDepth++;
            }
            bool wasReplaying = _shared.Replaying && EnemySync.EnterReplay();
            try
            {
                if (!_inner.MoveNext())
                {
                    Current = null;
                    _done?.Invoke();
                    return false;
                }
                var current = _inner.Current;
                Current = current is IEnumerator nested && current is not YieldInstruction ? new Steps(nested, _shared) : current;
                return true;
            }
            finally
            {
                if (_shared.Replaying)
                    EnemySync.ExitReplay(wasReplaying);
                if (seeded)
                {
                    SeededDepth--;
                    _shared.Random = UnityEngine.Random.state;
                    UnityEngine.Random.state = outer;
                }
            }
        }

        public void Reset() => throw new NotSupportedException();
    }

    /// <summary>Every move a helper can start: its action fields and those of the behaviours it runs (TeleportBehind
    /// -> Teleport -> start/end), a few levels down.</summary>
    internal static HashSet<GameAction> ActionsOf(Component helper)
    {
        var found = new HashSet<GameAction>();
        Collect(helper, found, new HashSet<object>(), 3);
        return found;
    }

    private static void Collect(Component component, HashSet<GameAction> found, HashSet<object> seen, int depth)
    {
        if (component == null || depth < 0 || !seen.Add(component))
            return;
        for (var t = component.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object value;
                try { value = field.GetValue(component); }
                catch (Exception) { continue; }
                if (value is GameAction action && action != null)
                    found.Add(action);
                else if (value is AiBehaviour behaviour && behaviour != null)
                    Collect(behaviour, found, seen, depth - 1);
            }
        }
    }

    /// <summary>Both games: <paramref name="routine"/> run step by step with Unity's Random from the call's seed.</summary>
    internal static IEnumerator Seeded(IEnumerator routine, Characters.Character boss, string key)
    {
        var state = UnityEngine.Random.state;
        UnityEngine.Random.InitState(Seed(boss, key));
        var shared = new Steps.Shared { Random = UnityEngine.Random.state };
        UnityEngine.Random.state = state;
        return new Steps(routine, shared);
    }

    /// <summary>
    /// Shuffles in boss code use the game's crypto RNG (no seed). Inside a seeded step (<see cref="Steps"/>) they draw
    /// from the step's Unity Random instead, so both games shuffle the same way; elsewhere they stay the game's own.
    /// The listed methods get their Shuffle call swapped (the Pope's platform picks: Divine Cross, Purification).
    /// </summary>
    [HarmonyPatch]
    public static class SeededShuffles
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Level.Chapter4.PlatformContainer), nameof(Level.Chapter4.PlatformContainer.RandomTakeTo));
            yield return AccessTools.Method(typeof(Level.Chapter4.PlatformContainer), nameof(Level.Chapter4.PlatformContainer.NoTentacleTakeTo));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo m
                    && m.IsGenericMethod && m.Name == "Shuffle" && m.DeclaringType?.Name == "ExtensionMethods")
                    instruction.operand = AccessTools.Method(typeof(SeededShuffles), nameof(Shuffle)).MakeGenericMethod(m.GetGenericArguments());
                yield return instruction;
            }
        }

        public static void Shuffle<T>(IList<T> list)
        {
            if (Steps.SeededDepth == 0)
            {
                ExtensionMethods.Shuffle(list);
                return;
            }
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    // ------------------------------------------------------------------ helper behaviours (the Pope)

    /// <summary>The key of a helper behaviour's call: this + its path below the enemy (BossSync.PathOf).</summary>
    internal const string BehaviourKey = "@";

    /// <summary>
    /// Boss behaviours with code between their moves (random line angles, platform picks, orb pools), run as helper
    /// calls: the host sends each start (key "@" + path); a client runs the same behaviour on its puppet with the
    /// call's seed and owns its moves. The Pope's attacks (R.md 3c); their movement (MoveHandler -> Fly/Teleport) runs
    /// inside them. Only top-level attacks are listed (a nested helper call would stop the one it runs in).
    /// </summary>
    internal static readonly HashSet<Type> HelperBehaviours = new()
    {
        typeof(PopeB.Baptism), typeof(PopeB.Choice), typeof(PopeB.Consecration), typeof(PopeB.DivineCross),
        typeof(PopeB.DivineImpact), typeof(PopeB.DivineLight), typeof(PopeB.Embrace), typeof(PopeB.Grace),
        typeof(PopeB.Nervousness), typeof(PopeB.Parade), typeof(PopeB.PriestSummon), typeof(PopeB.Purification),
        typeof(PopeB.Sacrament), typeof(PopeB.SoulChase), typeof(PopeB.SummonEscort), typeof(PopeB.SuperBaptism),
        typeof(PopeB.TentacleSummon), typeof(PopeB.Worship),
    };

    [HarmonyPatch]
    public static class HelperBehaviourStart
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            HelperBehaviours.Select(t => (MethodBase)AccessTools.DeclaredMethod(t, nameof(AiBehaviour.CRun))).Where(m => m != null);

        private static void Prefix(AiBehaviour __instance, AIController controller) =>
            EnemySync.Current?.OnHostHelperBehaviour(__instance, controller);

        private static void Postfix(AiBehaviour __instance, ref IEnumerator __result)
        {
            var boss = SeededBoss(__instance);
            if (boss != null && __result != null)
                __result = Seeded(__result, boss, BehaviourKey + __instance.GetType().Name);
        }
    }

    internal static class DarkSister
    {
        internal static string KeyOf(MethodBase method) => method.DeclaringType.Name + "." + method.Name;

        /// <summary>Host: the call goes to clients. Both games: the helper's steps draw from the call's seed.</summary>
        [HarmonyPatch]
        public static class Helpers
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(DarkRush), nameof(DarkRush.CRun));
                yield return AccessTools.Method(typeof(ThunderAttack), nameof(ThunderAttack.CRun));
                yield return AccessTools.Method(typeof(RangeAttack), nameof(RangeAttack.CRun));
            }

            private static void Prefix(object[] __args, MethodBase __originalMethod) =>
                EnemySync.Current?.OnHostCall(__args.Length > 0 ? __args[0] as AIController : null, KeyOf(__originalMethod), 0, 0f);

            private static void Postfix(Component __instance, MethodBase __originalMethod, ref IEnumerator __result)
            {
                var boss = SeededBoss(__instance);
                if (boss == null || __result == null)
                    return;
                if (__instance is DarkRush)
                    RepickSigns(boss);
                __result = Seeded(__result, boss, KeyOf(__originalMethod));
            }
        }

        /// <summary>The rush's sign set and order were picked when the sister appeared, by each game alone: at each rush
        /// both games pick them again, from the call's seed (SetParent: seeded spots; SetOrder: below).</summary>
        private static void RepickSigns(Characters.Character boss)
        {
            var root = boss.GetComponentInParent<Characters.Enemy>();
            var holder = root != null ? root.gameObject : boss.gameObject;
            foreach (var signs in holder.GetComponentsInChildren<SequentialActivateObject>(true))
                if (signs != null && signs._indics != null)
                    signs._currentIndex = signs._indics.Length;
        }

        /// <summary>The sign order: the game shuffles with a crypto RNG (no seed); co-op with the call's seed.</summary>
        [HarmonyPatch(typeof(SequentialActivateObject), "SetOrder")]
        public static class SeededSignOrder
        {
            private static bool Prefix(SequentialActivateObject __instance)
            {
                if ((int)__instance._order != 0) // Increase or Decrease: nothing random
                    return true;
                var boss = SeededBoss(__instance);
                var order = __instance._indics;
                if (boss == null || order == null)
                    return true;
                for (int i = 0; i < order.Length; i++)
                    order[i] = i;
                var random = new System.Random(Seed(boss, "SequentialActivateObject.SetOrder"));
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                return false;
            }
        }
    }

    /// <summary>
    /// The Pope's fanatics sacrifice themselves below a share of their health (`SacrificeCharacter.Update`, a script
    /// beside the AI, so a puppet's still ran it) or on the Pope's command (`Run(force)`, from a replayed move). A
    /// client's puppet started its own sacrifice and was dead 0.3 s later, 3-4 s before the host's fanatic (n4,
    /// CoopRuns/2026-11-11_15-14-52: 19 x "alive only on the host", every later move "skipped: dead"). On a client
    /// the host's fanatic decides: its sacrifice comes as moves and its death as the host's death.
    /// </summary>
    [HarmonyPatch]
    public static class NoPuppetSacrifice
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SacrificeCharacter), "Update");
            yield return AccessTools.Method(typeof(SacrificeCharacter), nameof(SacrificeCharacter.Run));
        }

        private static bool Prefix(SacrificeCharacter __instance) =>
            !EnemySync.IsPuppetAi(__instance._aiController) && !EnemySync.IsRunByHost(__instance._character);
    }

    // ------------------------------------------------------------------ Leiana (chapter 2): the map script

    /// <summary>
    /// Leiana's fight is directed by her map script (Chapter2Script), which runs on every game. On a client its intro
    /// stopped with an error (COOP_LOG 36, Player log): the sisters' landing (`GoldenAideAI.CIntro`) ran before the
    /// host's wave switched them on there. So the rest never ran on clients: the HUD and input coming back, the boss
    /// health bars, the phase-2 bar, the outro (the chest and dark quartz at the end).
    /// - The landing on a client waits for the host's spawn (its move comes from the host).
    /// - The fight's director part (`Combat`: the master ordering the sisters' attacks) stays the host's; a client runs
    ///   only what the director does for the screen: at the awakening the golden sister hides, the music and the
    ///   phase-2 bar change, and the Dark sister's death starts the outro.
    /// </summary>
    internal static class Leiana
    {
        private static bool IsPuppetFight(Characters.AI.TwinSister.Chapter2Script script) =>
            script != null && (EnemySync.IsPuppetAi(script._shortHair) || EnemySync.IsPuppetAi(script._longHair));

        [HarmonyPatch(typeof(Characters.AI.TwinSister.GoldenAideAI), nameof(Characters.AI.TwinSister.GoldenAideAI.CIntro))]
        public static class LandingFromHost
        {
            private static bool Prefix(Characters.AI.TwinSister.GoldenAideAI __instance, ref IEnumerator __result)
            {
                if (!EnemySync.IsPuppetAi(__instance))
                    return true;
                __result = WaitForLanding(__instance);
                return false;
            }

            private static IEnumerator WaitForLanding(Characters.AI.TwinSister.GoldenAideAI aide)
            {
                var character = aide.character;
                float waited = 0f;
                while (character != null && !character.gameObject.activeInHierarchy && waited < 5f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                yield return null; // the host's landing move arrives with (or just after) the wave
                waited = 0f;
                while (character != null && aide._introLanding != null && aide._introLanding.running && !character.health.dead && waited < 5f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        [HarmonyPatch(typeof(Characters.AI.TwinSister.Chapter2Script), "Combat")]
        public static class DirectorOnHost
        {
            private static bool Prefix(Characters.AI.TwinSister.Chapter2Script __instance, ref IEnumerator __result)
            {
                if (!IsPuppetFight(__instance))
                    return true;
                CoopTrace.Write("leiana-client", EnemySync.TraceRoom, what: "fight directed by the host");
                __result = ClientFight(__instance);
                return false;
            }
        }

        /// <summary>Client: what the director does for the screen (WaitForAwakeningPrepare, SpawnDarkAide).</summary>
        private static IEnumerator ClientFight(Characters.AI.TwinSister.Chapter2Script script)
        {
            var dark = script.darkAide;
            bool musicStopped = false;
            while (script != null && dark != null && dark.character != null && !dark.character.gameObject.activeInHierarchy)
            {
                // A golden sister fell: the host stops the music for the awakening.
                if (!musicStopped && script.twinSisterMasterAI != null && script.twinSisterMasterAI.goldenAideDiedCount > 0)
                {
                    musicStopped = true;
                    PersistentSingleton<SoundManager>.Instance.StopBackGroundMusic();
                }
                yield return null;
            }
            if (script == null || dark == null || dark.character == null)
                yield break;
            // The host switched the Dark sister on (EnemySync.ActivateLikeHost): the awakening is done there.
            script.twinSisterMasterAI?.RemovePlayerHitReaction();
            dark.character.health.onDiedTryCatch += script._outro.StartOutro;
            foreach (var golden in new[] { script._shortHair, script._longHair })
                if (golden != null && golden.character != null && !golden.character.health.dead)
                    golden.Hide(); // the one that awakened (the host hides her when the Dark sister appears)
            script._leftGoldenAide?.Hide();
            script._rightGoldenAide?.Hide();
            CoopTrace.Write("leiana-awakened", EnemySync.TraceRoom, EnemySync.TraceIdOf(dark.character), CoopTrace.NameOf(dark.character));
            PersistentSingleton<SoundManager>.Instance.PlayBackgroundMusic(script._awakenMusicInfo);
            var bars = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.headupDisplay?.bossHealthBar;
            bars?.CloseAll();
            yield return Chronometer.global.WaitForSeconds(1f);
            if (dark != null && dark.character != null)
                bars?.Open(global::UI.BossHealthbarController.Type.Chapter2_Phase2, dark.character);
        }
    }

    private static bool _applied;

    /// <summary>Applies the patches that only co-op needs and that are too wide for every player's start.</summary>
    public static void EnsureApplied()
    {
        if (_applied)
            return;
        _applied = true;
        var result = Patches.PatchRegistry.Apply(new Harmony(MyPluginInfo.PLUGIN_GUID + ".coop"), typeof(NoPuppetMainLoop));
        if (result.Ok)
            Plugin.Log.LogInfo($"[Coop] puppet AI main loops blocked ({result.PatchedMethods} AI classes)");
        else
            Plugin.Log.LogWarning("[Coop] puppet AI main loops NOT blocked: " + (result.Error?.Message ?? "no AI class found"));
    }

    // ------------------------------------------------------------------ Yggdrasil (chapter 1)

    internal static class Yggdrasil
    {
        // Sweep hands whose side was set by co-op (host: picked ahead; client: the host's): their own Select keeps it.
        private static readonly HashSet<SweepHandController> KeepSide = new();

        public static byte PickSweepSide(AiBehaviour behaviour)
        {
            var hands = ((Sweeping)behaviour)._sweepHandcontroller;
            if (hands == null)
                return 0;
            hands.Select();
            KeepSide.Add(hands);
            return hands.left ? (byte)1 : (byte)2;
        }

        public static void ForceSweepSide(AiBehaviour behaviour, byte side)
        {
            var hands = ((Sweeping)behaviour)._sweepHandcontroller;
            if (hands == null)
                return;
            hands.left = side == 1;
            hands._current = hands.left ? hands._left : hands._right;
            KeepSide.Add(hands);
        }

        [HarmonyPatch(typeof(SweepHandController), nameof(SweepHandController.Select))]
        public static class KeepPickedSide
        {
            private static bool Prefix(SweepHandController __instance) => !KeepSide.Remove(__instance);
        }

        /// <summary>Host: phase 2 begins (its health reached 0 once). Client: only on the host's word.</summary>
        [HarmonyPatch(typeof(YggdrasillElderEntAI), nameof(YggdrasillElderEntAI.OnDie))]
        public static class PhaseTwo
        {
            private static bool Prefix(YggdrasillElderEntAI __instance) => !EnemySync.IsPuppetAi(__instance);

            private static void Postfix(YggdrasillElderEntAI __instance) =>
                EnemySync.Current?.OnHostPhase(__instance.character, 2);
        }

        /// <summary>Client: what OnDie does, except the phase-2 behaviours (they come from the host one by one).</summary>
        public static void EnterPhase(AIController ai, byte phase)
        {
            var boss = (YggdrasillElderEntAI)ai;
            var character = boss.character;
            character.health.onChanged -= boss.HanldeOnHealthChanged;
            boss._onPhase2?.Run();
            character.status.RemoveAllStatus();
        }
    }
}
