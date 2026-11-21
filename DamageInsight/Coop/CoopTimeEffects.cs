using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Characters;
using Characters.Abilities.Customs;
using Characters.Operations;
using DamageInsight.Coop.Net;
using HarmonyLib;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Slow motions that change the fight (COOP_PLAN 4.5: "effects that change the fight (the Fighter's slowed world)
/// apply to the enemies for everyone, through the host"; NEXT_SPECS A). In co-op a player's "the world slows down"
/// (the Fighter's rage, the ModifyTimeScale ability, a skill operation's ChronoInfo.ApplyGlobe) no longer goes on
/// <c>Chronometer.global</c> (that slowed ONE game's whole world: on the host every real enemy for everyone, on a
/// client only what it shows). Its game sends a TimeEffect instead (MessageType.TimeEffect, a client's through the
/// host's relay) and EVERY game, the sender too, puts the scale on the <c>chronometer.master</c> of every enemy it
/// bound in the room (and of those bound while it lasts) for its seconds. The source's own compensation (the
/// Fighter's master x 1 / scale) is not attached either: the world isn't slowed for it any more. A room change or
/// the session's end clears everything. Players and teammate copies are never slowed.
/// </summary>
// REVIEW(fable): a new message (MessageType.TimeEffect, REVIEW_QUEUE entry 7) and every game now slows its own
// enemies (on a client its puppets) instead of the source's game slowing its world. Right for the host's AI timing?
internal sealed class CoopTimeEffects
{
    public static CoopTimeEffects Current { get; private set; }

    /// <summary>One running effect on this game: the object its scale is attached under on each enemy.</summary>
    private sealed class Slow
    {
        public float Scale;
        public readonly HashSet<Character> On = new();
    }

    private readonly CoopSession _session;
    private readonly NetWriter _writer = new(32);
    private readonly TimeEffectTable _table = new();
    private readonly Dictionary<uint, Slow> _slows = new();
    private readonly List<uint> _keys = new();
    private Map _map;

    public CoopTimeEffects(CoopSession session)
    {
        _session = session;
        Current = this;
        session.RegisterHandler(MessageType.TimeEffect, OnMessage);
        if (session.IsHost)
            session.SetRelay(MessageType.TimeEffect); // a client's effect also goes to the other clients
    }

    public void Dispose()
    {
        ClearAll("session end");
        Sources.Clear();
        if (Current == this)
            Current = null;
    }

    private static double Now => Time.realtimeSinceStartupAsDouble;

    /// <summary>The effect's key on the wire: this game's slot and the source object.</summary>
    internal uint KeyOf(object source) => TimeEffect.KeyFor(_session.LocalSlot, RuntimeHelpers.GetHashCode(source));

    /// <summary>A source on this game started (seconds > 0, or below 0 = until ended), refreshed or (seconds 0)
    /// ended: slow this game's enemies and tell everyone.</summary>
    internal void Send(uint key, float scale, float seconds, string source)
    {
        var effect = new TimeEffect { Room = CoopWorld.CurrentRoom(), Key = key, Scale = scale, Seconds = seconds };
        _writer.Reset();
        TimeEffectMessages.Write(_writer, effect);
        int peers = _session.Broadcast(MessageType.TimeEffect, _writer, Delivery.Reliable);
        CoopTrace.Write("time-effect-sent", effect.Room, name: source,
            what: $"{key:x8} x{scale:0.###} {(effect.Ends ? "end" : effect.UntilEnded ? "until ended" : seconds.ToString("0.00") + " s")} to {peers}",
            amount: scale, after: seconds, slot: _session.LocalSlot);
        Apply(effect, Now);
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (!TimeEffectMessages.TryRead(payload, out var effect))
            return;
        uint room = CoopWorld.CurrentRoom();
        if (effect.Room != room)
        {
            CoopTrace.Write("time-effect-skipped", room, what: $"{effect.Key:x8} for room {effect.Room:x8}", slot: message.FromSlot);
            return;
        }
        Apply(effect, Now);
    }

    private void Apply(in TimeEffect effect, double now)
    {
        if (!ReferenceEquals(Map.Instance, _map))
            ClearAll("room change");
        _map = Map.Instance;
        if (!_table.Set(effect, now))
            End(effect.Key);
        else
        {
            if (!_slows.TryGetValue(effect.Key, out var slow))
                _slows[effect.Key] = slow = new Slow { Scale = effect.Scale };
            else if (slow.Scale != effect.Scale)
            {
                slow.Scale = effect.Scale;
                foreach (var enemy in slow.On)
                    if (enemy != null && enemy.chronometer?.master != null)
                        enemy.chronometer.master.AttachTimeScale(slow, slow.Scale); // the same key: the new scale
            }
            AttachNew();
        }
        int count = _slows.TryGetValue(effect.Key, out var s) ? s.On.Count : 0;
        CoopTrace.Write("time-effect-applied", CoopWorld.CurrentRoom(),
            what: $"{effect.Key:x8} x{effect.Scale:0.###} {(effect.Ends ? "end" : effect.UntilEnded ? "until ended" : effect.Seconds.ToString("0.00") + " s")} on {count} enemies",
            amount: effect.Scale, after: effect.Seconds, slot: effect.SourceSlot);
    }

    /// <summary>Once a frame: the room changed (all end), effects whose time ran out end, enemies bound since get
    /// the running ones.</summary>
    public void Tick(double now)
    {
        if (!ReferenceEquals(Map.Instance, _map))
        {
            ClearAll("room change");
            _map = Map.Instance;
        }
        if (_slows.Count == 0)
            return;
        foreach (uint key in _table.Expire(now))
        {
            End(key);
            CoopTrace.Write("time-effect-expired", CoopWorld.CurrentRoom(), what: $"{key:x8}", slot: (int)(key >> 24));
        }
        AttachNew();
    }

    private void AttachNew()
    {
        var enemies = EnemySync.Current?.BoundCharacters;
        if (enemies == null || _slows.Count == 0)
            return;
        foreach (var enemy in enemies)
        {
            if (!Slows(enemy))
                continue;
            foreach (var slow in _slows.Values)
                if (slow.On.Add(enemy))
                    enemy.chronometer.master.AttachTimeScale(slow, slow.Scale);
        }
    }

    /// <summary>Enemies only: never a player, a player's minion or a teammate's copy.</summary>
    private static bool Slows(Character c) =>
        c != null && !CoopCopies.IsPlayerSide(c) && !CoopCopies.IsCopy(c) && c.chronometer?.master != null;

    private void End(uint key)
    {
        _table.Remove(key);
        if (!_slows.TryGetValue(key, out var slow))
            return;
        _slows.Remove(key);
        foreach (var enemy in slow.On)
            if (enemy != null && enemy.chronometer?.master != null)
                enemy.chronometer.master.DetachTimeScale(slow);
    }

    private void ClearAll(string why)
    {
        if (_slows.Count == 0 && _table.Count == 0)
            return;
        _keys.Clear();
        _keys.AddRange(_slows.Keys);
        foreach (uint key in _keys)
            End(key);
        _table.Clear();
        CoopTrace.Write("time-effect-cleared", CoopWorld.CurrentRoom(), what: why + ", " + _keys.Count + " effects");
    }

    // ------------------------------------------------------------------ where the game slows the world

    /// <summary>The held sources on this game (FighterPassive, ModifyTimeScale.Instance) whose global scale was not
    /// attached, with that scale (their owner part is measured against it).</summary>
    internal static readonly Dictionary<object, (uint key, float scale, bool sent)> Sources = new();

    internal enum Side { World, Local, Copy }

    private static Character LocalPlayer => Singleton<Service>.Instance?.levelManager?.player;

    /// <summary>Local: this game's player or its minion (this game sends). Copy: a teammate's copy, its minion or
    /// another Player character (the teammate's own game sends; here nothing). World: everyone else (unchanged).</summary>
    internal static Side SideOf(Character owner)
    {
        if (owner == null)
            return Side.World;
        if (CoopCopies.BelongsToCopy(owner))
            return Side.Copy;
        if (owner.type == Character.Type.Player)
            return ReferenceEquals(owner, LocalPlayer) ? Side.Local : Side.Copy;
        var player = CoopCopies.PlayerBehind(owner); // a minion's leader, a quintessence's owner
        if (player == null)
            return Side.World;
        if (player.type != Character.Type.Player)
            return Side.Copy; // a minion with no known leader (as before)
        return ReferenceEquals(player, LocalPlayer) ? Side.Local : Side.Copy;
    }

    private static bool InCoop => Current != null && CoopWorld.Current?.Active == true;

    /// <summary>A held slow motion: <c>Chronometer.global.AttachTimeScale(source, scale)</c> until it is detached
    /// (the Fighter's rage, the ModifyTimeScale ability). And the source's own part on its owner's chronometer.</summary>
    // REVIEW(fable): a prefix on ChronometerBase.AttachTimeScale (every status and slow motion goes through it; non-
    // sources leave after one type check). The owner keeps the net speed it had in solo (its scale x the world's):
    // the Fighter's 1 / scale becomes nothing; is that right for ModifyTimeScale on Animation / Projectile too?
    [HarmonyPatch(typeof(ChronometerBase), nameof(ChronometerBase.AttachTimeScale), new[] { typeof(object), typeof(float) })]
    internal static class HeldSlowMotion
    {
        private static bool Prefix(ChronometerBase __instance, object key, ref float timeScale)
        {
            if (!(key is FighterPassive or Characters.Abilities.ModifyTimeScale.Instance))
                return true;
            try
            {
                if (ReferenceEquals(__instance, Chronometer.global))
                    return Global(key, timeScale);
                if (!Sources.TryGetValue(key, out var source))
                    return true;
                float net = timeScale * source.scale;
                if (Math.Abs(net - 1f) < 0.01f)
                    return false; // the compensation for a world it no longer slows
                timeScale = net;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] slow motion: " + e.Message);
                return true;
            }
        }

        private static bool Global(object key, float scale)
        {
            Sources.Remove(key);
            if (!InCoop || Math.Abs(scale - 1f) < 0.001f)
                return true;
            var owner = key is FighterPassive fighter ? fighter.owner : ((Characters.Abilities.ModifyTimeScale.Instance)key).owner;
            var side = SideOf(owner);
            if (side == Side.World)
                return true;
            if (side == Side.Copy)
            {
                Sources[key] = (0, scale, false);
                return false;
            }
            uint wire = Current.KeyOf(key);
            Sources[key] = (wire, scale, true);
            // Until its game detaches it (an end message): a refresh of the ability doesn't come through here.
            Current.Send(wire, scale, -1f, key.GetType().Name + " of " + CoopTrace.NameOf(owner));
            return false;
        }
    }

    /// <summary>The held slow motion ends: its game tells everyone (the game's own detach then removes nothing).</summary>
    [HarmonyPatch(typeof(ChronometerBase), nameof(ChronometerBase.DetachTimeScale))]
    internal static class HeldSlowMotionEnds
    {
        private static void Prefix(ChronometerBase __instance, object key)
        {
            if (Sources.Count == 0 || !ReferenceEquals(__instance, Chronometer.global))
                return;
            try
            {
                if (!Sources.TryGetValue(key, out var source))
                    return;
                Sources.Remove(key);
                if (source.sent)
                    Current?.Send(source.key, 1f, 0f, key.GetType().Name);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] slow motion end: " + e.Message);
            }
        }
    }

    /// <summary>A timed slow motion: a skill operation's <c>ChronoInfo.ApplyGlobe</c> (<c>Chronometer.global</c>,
    /// a scale for a duration). The owner comes from the operation that runs it (<see cref="OperationOwner"/>).</summary>
    // REVIEW(fable): ApplyGlobe from a caller without an owner (Guard, GuardForTeam, SweepAttack, a grab) still slows
    // the world as before; Operations.ModifyTimeScale run on a hit target outside an attack's run counts as the
    // target's (an enemy: the world slows as before). Enough?
    [HarmonyPatch(typeof(ChronometerBase), nameof(ChronometerBase.AttachTimeScale), new[] { typeof(object), typeof(float), typeof(float) })]
    internal static class TimedSlowMotion
    {
        private static bool Prefix(ChronometerBase __instance, object key, float timeScale, float duration)
        {
            if (!(key is ChronoInfo) || !ReferenceEquals(__instance, Chronometer.global) || OperationOwner.Current == null)
                return true;
            try
            {
                if (!InCoop)
                    return true;
                var owner = OperationOwner.Current;
                var side = SideOf(owner);
                if (side == Side.World)
                    return true;
                if (side == Side.Local)
                    Current.Send(Current.KeyOf(key), timeScale, Math.Max(0.01f, duration), "ChronoInfo of " + CoopTrace.NameOf(owner));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] slow motion (operation): " + e.Message);
                return true;
            }
        }
    }

    /// <summary>Who runs the operation that may call ChronoInfo.ApplyGlobe (the global time scale doesn't say): the
    /// attack operations' owner while they run.</summary>
    // REVIEW(fable): prefix + finalizer on every attack operation's run (PlayerAttack, InstantAttack, ...); they only
    // set and restore one static field.
    [HarmonyPatch]
    internal static class OperationOwner
    {
        internal static Character Current;

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var one = new[] { typeof(Character) };
            var two = new[] { typeof(Character), typeof(Character) };
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.BoundsAttackInfo), "ApplyChrono", one);
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.BoundsAttackInfo), "ApplyChrono", two);
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.CastAttackInfo), "ApplyChrono", one);
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.CastAttackInfo), "ApplyChrono", two);
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.GlobalAttack), "Attack",
                new[] { typeof(Character), typeof(List<Character>) });
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.InstantAttack), "Run", one);
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.PlayerAttack), "Attack", one);
            yield return AccessTools.Method(typeof(Characters.Operations.Attack.RingAttack), "Detect", one);
        }

        private static void Prefix(Character __0, out Character __state)
        {
            __state = Current;
            Current = __0;
        }

        private static void Finalizer(Character __state) => Current = __state;
    }

    /// <summary>The ModifyTimeScale operation's argument is its owner, or the target an attack hit (then it runs
    /// inside the attack's own run): a player's attack stays the owner of what its hit sets off.</summary>
    [HarmonyPatch(typeof(Characters.Operations.ModifyTimeScale), nameof(Characters.Operations.ModifyTimeScale.Run), new[] { typeof(Character) })]
    internal static class TimeScaleOperationOwner
    {
        private static void Prefix(Character target, out Character __state)
        {
            __state = OperationOwner.Current;
            var outer = OperationOwner.Current;
            if (outer == null || !CoopCopies.IsPlayerSide(outer))
                OperationOwner.Current = target;
        }

        private static void Finalizer(Character __state) => OperationOwner.Current = __state;
    }
}
