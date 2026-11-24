using System;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using HarmonyLib;
using Level;
using PhysicsUtils;
using Runnables;
using Runnables.Triggers;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.Events;

namespace DamageInsight.Coop;

/// <summary>
/// A room's own logic starts when "a player" walks into a zone (<see cref="EnterZone"/> with the Player character type:
/// an invoker's trigger, then its runnable: seals behind the player, music, gates, plain cut scenes). A teammate's copy
/// is a Player character on this game, so it set that logic off on THIS game for THIS game's player. US tester's run
/// 2026-11-22 (COOP_LOG 65), Dark Mirror chapter 4's last room before the boss ("Terminal Variant"): the client
/// walked into the "To boss" zone; on the host's game its scene closed the gate and switched on "Left Block", a 52 x 29
/// wall over the whole left part of the room, where the host still stood. He floated inside it, the game's
/// StuckResolver put him back six times (into the same wall), until F6 took him to the client.
/// <para>
/// Now such a zone counts only this game's own player, unless its runnable makes enemies (a wave, a summon, a boss
/// chest or name: enemies come from the host's game, which must start them when any player walks in; the scan of every
/// map, tools/unity/enter_zones.py, found 179 such zones, a dozen of them with enemies). A zone no invoker starts
/// directly (inside a Sequence or an Inverter) stays as the game has it. Story scenes are shared anyway
/// (<see cref="SharedScenes"/>: a client's own trigger asks the host, whose game starts them for everyone).
/// </para>
/// </summary>
// REVIEW(fable): a Harmony patch on a common room trigger (trigger 3) and who sets room logic off (trigger 2); built
// from the game files and the report, not yet proven by a game run.
[HarmonyPatch(typeof(EnterZone), "Check")]
internal static class OwnZoneTriggers
{
    private static NonAllocOverlapper _lapper;
    private static Map _map;
    private static readonly Dictionary<EnterZone, bool> OwnOnly = new();
    private static readonly HashSet<EnterZone> Traced = new();

    private static void Postfix(EnterZone __instance, ref bool __result)
    {
        if (!__result || CoopCopies.Count == 0 || CoopWorld.Current?.Active != true)
            return;
        try
        {
            if (!__instance._checkCharacterType || __instance._characterType != Character.Type.Player)
                return;
            if (!IsOwnOnly(__instance) || OwnPlayerInside(__instance))
                return;
            __result = false;
            if (Traced.Add(__instance))
                CoopTrace.Write("zone-own-only", CoopWorld.CurrentRoom(), name: __instance.name,
                    what: "a teammate's copy is in it, this game's player isn't");
        }
        catch (Exception e)
        {
            OwnOnly[__instance] = false; // never again for this zone: the game's own answer
            Plugin.Log.LogWarning("[Coop] zone check failed (" + e.Message + "); the game's own answer is used");
        }
    }

    private static bool OwnPlayerInside(EnterZone zone)
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        if (player == null || zone._collider == null)
            return false;
        _lapper ??= new NonAllocOverlapper(15);
        _lapper.contactFilter.SetLayerMask(zone._layer);
        foreach (var collider in _lapper.OverlapCollider(zone._collider).results)
        {
            var target = collider != null ? collider.GetComponent<Characters.Target>() : null;
            if (target != null && target.character == player)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ which zones

    private static bool IsOwnOnly(EnterZone zone)
    {
        var map = Map.Instance;
        if (map != _map || _runsOf == null)
        {
            _map = map;
            OwnOnly.Clear();
            Traced.Clear();
            _runsOf = IndexInvokers(map != null ? map.transform : zone.transform.root);
        }
        if (OwnOnly.TryGetValue(zone, out bool known))
            return known;
        bool ownOnly = false;
        if (_runsOf.TryGetValue(zone, out var runs) && runs.Count > 0)
        {
            ownOnly = true;
            foreach (var run in runs)
                if (run == null || MakesEnemies(run))
                    ownOnly = false;
        }
        OwnOnly[zone] = ownOnly;
        return ownOnly;
    }

    private static Dictionary<Trigger, List<Runnable>> _runsOf;
    private static readonly Dictionary<Type, (FieldInfo trigger, FieldInfo execute)> InvokerFields = new();

    /// <summary>Every invoker below <paramref name="root"/> (a field `_trigger` and a field `_execute`, like
    /// UpdateInvoker): its trigger -> its runnables. One walk per map.</summary>
    private static Dictionary<Trigger, List<Runnable>> IndexInvokers(Transform root)
    {
        var index = new Dictionary<Trigger, List<Runnable>>();
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;
            var type = behaviour.GetType();
            if (!InvokerFields.TryGetValue(type, out var fields))
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var trigger = type.GetField("_trigger", flags);
                var execute = type.GetField("_execute", flags);
                fields = trigger != null && execute != null && typeof(Trigger).IsAssignableFrom(trigger.FieldType)
                         && typeof(Runnable).IsAssignableFrom(execute.FieldType)
                    ? (trigger, execute)
                    : (null, null);
                InvokerFields[type] = fields;
            }
            if (fields.trigger == null || fields.trigger.GetValue(behaviour) is not Trigger key || key == null)
                continue;
            if (!index.TryGetValue(key, out var list))
                index[key] = list = new List<Runnable>();
            list.Add(fields.execute.GetValue(behaviour) as Runnable);
        }
        return index;
    }

    private static readonly string[] EnemyWords = { "Wave", "Summon", "Enemy", "Boss", "Adventurer" };

    /// <summary>A runnable whose parts (below it) or whose events' targets are enemy things: waves, summons...</summary>
    private static bool MakesEnemies(Runnable run)
    {
        foreach (var part in run.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (part == null)
                continue;
            if (IsEnemyThing(part.GetType()))
                return true;
            foreach (var unityEvent in EventsOf(part))
                for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
                {
                    var target = unityEvent.GetPersistentTarget(i);
                    var go = target is Component c ? c.gameObject : target as GameObject;
                    if (go == null)
                        continue;
                    foreach (var component in go.GetComponents<MonoBehaviour>())
                        if (component != null && IsEnemyThing(component.GetType()))
                            return true;
                }
        }
        return false;
    }

    private static bool IsEnemyThing(Type type)
    {
        string name = type.Name;
        foreach (var word in EnemyWords)
            if (name.IndexOf(word, StringComparison.Ordinal) >= 0)
                return true;
        return false;
    }

    private static readonly Dictionary<Type, FieldInfo[]> EventFields = new();

    private static IEnumerable<UnityEventBase> EventsOf(MonoBehaviour part)
    {
        var type = part.GetType();
        if (!EventFields.TryGetValue(type, out var fields))
        {
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (typeof(UnityEventBase).IsAssignableFrom(f.FieldType))
                        list.Add(f);
            EventFields[type] = fields = list.ToArray();
        }
        foreach (var f in fields)
            if (f.GetValue(part) is UnityEventBase e)
                yield return e;
    }
}
