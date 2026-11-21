using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Characters;
using Characters.AI.Emperor;
using Characters.Operations;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Boss parts whose fight is neither character moves nor AIController behaviours: King Alexander's giant and his
/// armless form (dm6; COOP_LOG 50i, 51). A BehaviorDesigner tree plays the body's animations (the task
/// PlayEmperorAnimation -> EmperorAnimationController.PlayAnimation) and runs operation lists (the task
/// RunOperations); the animation's events (EmperorAnimationEventRecevier) pick a hand and run more lists on the
/// hands and the body. A client's puppet has no tree, so none of it showed there (third `dm6r2`: DrillMissile host
/// 12 / client 3 at the giant, 10 / 0 and BuzzSaw 5 / 0 in the armless form).
/// - Host: each animation the tree starts and each operation list the tree or an animation event starts or stops
///   goes out as a call (EnemyEventKind.Call: <see cref="AnimationKey"/>, or <see cref="OperationsKey"/> /
///   <see cref="StopKey"/> + the list's path below the map).
/// - Clients: play the animation and run the list on their puppet. The animation's events do nothing there: no
///   hand is picked on a client, the host's pick is in which list it ran. The hands' attacks are character actions
///   and come with the motion replay; the hands' place comes with the snapshots (EmperorHandController.Move is off).
/// Random spots inside a list are still each game's own.
/// </summary>
internal static class BossParts
{
    internal const string AnimationKey = "Emperor.Anim";
    internal const string OperationsKey = "ops:";
    internal const string StopKey = "ops-stop:";

    internal static bool Handles(string key) =>
        key == AnimationKey || key.StartsWith(OperationsKey, StringComparison.Ordinal) || key.StartsWith(StopKey, StringComparison.Ordinal);

    // Host: inside a tree task or an animation event that starts lists / plays an animation / stops lists, and
    // inside a list's own Run (a list that another list's first operations start is that list's on a client too).
    private static int _startScope, _animationScope, _runDepth;

    // Host: the lists that went out and still run, and the list whose own coroutine is running right now (a Stop
    // from in there is its natural end).
    private static readonly HashSet<OperationInfos> SentRunning = new();
    private static OperationInfos _inOwnRun;

    // Client: the lists it initialised itself (a tree's list is initialised by the tree's OnAwake on the host).
    private static readonly HashSet<OperationInfos> Initialized = new();
    private static Transform _initializedMap;

    // ------------------------------------------------------------------ paths

    /// <summary>The path of <paramref name="transform"/> below <paramref name="root"/>; a name that an earlier
    /// sibling has too gets "~n" (the n-th of that name), so the same object is found on every game.</summary>
    internal static string PathBelow(Transform transform, Transform root)
    {
        var parts = new List<string>();
        for (var t = transform; t != null && t != root; t = t.parent)
        {
            int nth = 0;
            if (t.parent != null)
                for (int i = 0, mine = t.GetSiblingIndex(); i < mine; i++)
                    if (t.parent.GetChild(i).name == t.name)
                        nth++;
            parts.Add(nth > 0 ? t.name + "~" + nth : t.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    internal static Transform Find(Transform root, string path)
    {
        var at = root;
        foreach (string part in path.Split('/'))
        {
            string name = part;
            int nth = 0, mark = part.LastIndexOf('~');
            if (mark > 0 && int.TryParse(part.Substring(mark + 1), out int n))
            {
                name = part.Substring(0, mark);
                nth = n;
            }
            Transform next = null;
            for (int i = 0; i < at.childCount; i++)
            {
                var child = at.GetChild(i);
                if (child.name == name && nth-- == 0)
                {
                    next = child;
                    break;
                }
            }
            if (next == null)
                return null;
            at = next;
        }
        return at;
    }

    // ------------------------------------------------------------------ client

    /// <summary>Client: runs the host's call on the puppet; null, or why it could not.</summary>
    internal static string Play(Character enemy, string key, byte arg, Transform map)
    {
        if (key == AnimationKey)
        {
            var controller = UnityEngine.Object.FindObjectsOfType<EmperorAnimationController>().FirstOrDefault(c => c._owner == enemy);
            if (controller == null)
                return "no animation controller";
            controller.PlayAnimation((EmperorAnimation.Tag)arg);
            return null;
        }
        bool stop = key.StartsWith(StopKey, StringComparison.Ordinal);
        var found = map != null ? Find(map, key.Substring((stop ? StopKey : OperationsKey).Length)) : null;
        var list = found != null ? found.GetComponent<OperationInfos>() : null;
        if (list == null)
            return "no such operations";
        if (stop)
        {
            if (list._running)
                list.Stop();
            return null;
        }
        if (map != _initializedMap)
        {
            _initializedMap = map;
            Initialized.Clear();
        }
        if (Initialized.Add(list))
            list.Initialize();
        list.gameObject.SetActive(true);
        list.Run(enemy);
        return null;
    }

    // ------------------------------------------------------------------ patches

    private static IEnumerable<MethodBase> ReceiverEvents() =>
        typeof(EmperorAnimationEventRecevier).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0);

    /// <summary>Host: what starts operation lists (the tree's RunOperations, the giant's animation events, a stunned
    /// hand's release). Clients: a puppet's animation events and release do nothing.</summary>
    [HarmonyPatch]
    public static class StartScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BehaviorDesigner.Runtime.Tasks.RunOperations), "OnStart");
            yield return AccessTools.Method(typeof(EmperorHandController), nameof(EmperorHandController.ReleaseHand));
            foreach (var method in ReceiverEvents())
                yield return method;
        }

        private static bool Prefix(object __instance, out bool __state)
        {
            __state = false;
            if ((__instance is EmperorAnimationEventRecevier receiver && EnemySync.IsPuppet(receiver._body))
                || (__instance is EmperorHandController hand && EnemySync.IsPuppet(hand._owner)))
                return false;
            __state = true;
            _startScope++;
            return true;
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
                _startScope--;
        }
    }

    /// <summary>Clients: a puppet hand's place is the host's (the snapshots), not its own glide.</summary>
    [HarmonyPatch(typeof(EmperorHandController), nameof(EmperorHandController.Move))]
    public static class NoPuppetGlide
    {
        private static bool Prefix(EmperorHandController __instance) => !EnemySync.IsPuppet(__instance._owner);
    }

    [HarmonyPatch(typeof(OperationInfos), nameof(OperationInfos.Run), typeof(Character), typeof(float))]
    public static class SendRun
    {
        private static void Prefix(OperationInfos __instance, Character owner)
        {
            if (_runDepth++ != 0 || _startScope <= 0 || EnemySync.Current?.OnHostOperations(__instance, owner, OperationsKey) != true)
                return;
            if (SentRunning.Count > 200)
                SentRunning.RemoveWhere(list => list == null);
            SentRunning.Add(__instance);
        }

        private static void Finalizer() => _runDepth--;
    }

    /// <summary>Host: the tree's tasks that play the body's animations (a story scene's event plays them on every
    /// game by itself).</summary>
    [HarmonyPatch]
    public static class AnimationScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BehaviorDesigner.Runtime.Tasks.Spine.PlayEmperorAnimation), "OnStart");
            yield return AccessTools.Method(typeof(BehaviorDesigner.Runtime.Tasks.Spine.PlayEmperorAnimationForStunOrFreeze), "OnStart");
        }

        private static void Prefix() => _animationScope++;

        private static void Finalizer() => _animationScope--;
    }

    [HarmonyPatch(typeof(EmperorAnimationController), nameof(EmperorAnimationController.PlayAnimation))]
    public static class SendAnimation
    {
        private static void Prefix(EmperorAnimationController __instance, EmperorAnimation.Tag tag)
        {
            if (_animationScope > 0)
                EnemySync.Current?.OnHostPartCall(__instance._owner, AnimationKey, (byte)tag);
        }
    }

    /// <summary>The list whose own coroutine runs now (its Stop at the end of it is the natural end).</summary>
    [HarmonyPatch]
    public static class OwnRun
    {
        private static FieldInfo _list;

        private static MethodBase TargetMethod()
        {
            var moveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(OperationInfos), "CRun"));
            _list = AccessTools.Field(moveNext.DeclaringType, "<>4__this");
            return moveNext;
        }

        private static void Prefix(object __instance, out OperationInfos __state)
        {
            __state = _inOwnRun;
            _inOwnRun = _list?.GetValue(__instance) as OperationInfos;
        }

        private static void Finalizer(OperationInfos __state) => _inOwnRun = __state;
    }

    /// <summary>Host: a list that went out is ended early (the tree's StopOperations / CancelOperation, a stunned
    /// hand, anything but its own end): clients end theirs. Its own end is not sent: a client's copy started a
    /// moment later and would lose its last operations. Fifth `dm6r2`: the armless form's last attack was cut off on
    /// the host when the fight ended, ran on at the client and killed its player, who then never reached the last
    /// arena (Umut's pictures, 2026-11-13).</summary>
    [HarmonyPatch(typeof(OperationInfos), nameof(OperationInfos.Stop))]
    public static class SendStop
    {
        private static void Prefix(OperationInfos __instance)
        {
            if (SentRunning.Remove(__instance) && __instance._running && _inOwnRun != __instance)
                EnemySync.Current?.OnHostOperations(__instance, __instance._owner, StopKey);
        }
    }

    /// <summary>Host: a running list that went out is switched off (its part is hidden at the fight's end).</summary>
    [HarmonyPatch(typeof(OperationInfos), "OnDisable")]
    public static class SendCut
    {
        private static void Prefix(OperationInfos __instance)
        {
            if (SentRunning.Remove(__instance) && __instance._running)
                EnemySync.Current?.OnHostOperations(__instance, __instance._owner, StopKey);
        }
    }
}

internal sealed partial class EnemySync
{
    /// <summary>Host: a boss part's tree plays an animation (BossParts.AnimationKey): clients play it too.</summary>
    internal void OnHostPartCall(Character enemy, string key, byte arg)
    {
        if (!IsHost || _map == null || enemy == null || !_ids.TryGetValue(enemy, out var id))
            return;
        CoopTrace.Write("call", _room, id, CoopTrace.NameOf(enemy), $"{key} {arg}");
        SendEvent(new EnemyEvent { Kind = EnemyEventKind.Call, NetId = id, ActionPath = key, Extra = arg });
    }

    /// <summary>Host: a boss part's operation list starts or is stopped by its tree or an animation event
    /// (BossParts): clients run or stop the same list. Only King Alexander's parts so far (other tree-run bosses
    /// are in step by their moves; their RunOperations lists are a candidate, T.md Open 2).</summary>
    internal bool OnHostOperations(Characters.Operations.OperationInfos list, Character owner, string key)
    {
        if (!IsHost || _map == null || list == null || owner == null || !IsBossPart(owner) || !_ids.TryGetValue(owner, out var id))
            return false;
        // His first form fights with character moves (in step by the motion replay); its tree's lists only place
        // him (SetPositionToTargetPlatform threw on a puppet: no platform under it), which the snapshots do.
        if (owner.name.StartsWith("Emperor_Renewal", StringComparison.Ordinal))
            return false;
        if (!list.transform.IsChildOf(_map.transform))
        {
            if (key == BossParts.OperationsKey)
                CoopTrace.Write("call-unbound", _room, id, CoopTrace.NameOf(owner), key + list.name + " (not below the map)");
            return false;
        }
        string path = key + BossParts.PathBelow(list.transform, _map.transform);
        var target = CoopTargeting.TargetOf(owner);
        int slot = target != null ? _slotOf(target) : -1;
        CoopTrace.Write("call", _room, id, CoopTrace.NameOf(owner), path, slot: slot);
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.Call, NetId = id, ActionPath = path,
            TargetSlot = slot is >= 0 and < 255 ? (byte)slot : (byte)255,
        });
        return true;
    }

    /// <summary>Client: the host's animation or operation list on the puppet.</summary>
    private void PlayPartCall(EnemyEvent e)
    {
        var enemy = EnemyOf(e.NetId);
        if (enemy != null && !enemy.health.dead && !enemy.gameObject.activeInHierarchy)
            ActivateLikeHost(e.NetId, enemy, true);
        // not skipped for a dead part: the armless form's body goes on attacking for 100 s after its health is 0
        // (fourth `dm6r2`: 17 lists skipped on the client after `died-applied`)
        if (enemy == null || !enemy.gameObject.activeInHierarchy)
        {
            CoopTrace.Write("call-skipped", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + (enemy == null ? " no such enemy" : ""));
            return;
        }
        if (e.TargetSlot != 255)
            CoopTargeting.SetTarget(enemy, _playerOf(e.TargetSlot));
        string problem;
        try
        {
            problem = BossParts.Play(enemy, e.ActionPath, e.Extra, _map != null ? _map.transform : null);
        }
        catch (Exception ex)
        {
            problem = ex.GetType().Name + ": " + ex.Message;
        }
        CoopTrace.Write(problem == null ? "call-played" : "call-not-found", _room, e.NetId, CoopTrace.NameOf(enemy),
            problem == null ? (e.ActionPath == BossParts.AnimationKey ? e.ActionPath + " " + e.Extra : e.ActionPath) : e.ActionPath + " (" + problem + ")",
            slot: e.TargetSlot == 255 ? -1 : e.TargetSlot);
    }
}
