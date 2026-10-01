using System;
using System.Collections.Generic;
using Characters;
using Characters.Operations;
using DamageInsight.Recording;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>
/// Records how every hit's number comes about, for the combat log's breakdown:
/// Stat.GetDamage creates the damage (base x attack stats) → the attacker's onGiveDamage handlers (items,
/// inscriptions, dark abilities, buffs) → the crit roll → the target's onTakeDamage handlers (debuffs, damage
/// taken). We run both handler lists ourselves and note every handler that changed the numbers.
/// </summary>
public static class DamageTracePatch
{
    private sealed class Active
    {
        public DamageTrace Trace;
        public Character Target;
        public bool GiveDone, TakeDone;
    }

    private static readonly List<Active> Stack = new();
    private static OwnerNames.Owner? _pendingOrigin;
    private static int _pendingFrame = -1;

    private static bool Enabled => Plugin.CombatLogCalculations.Value;

    public static DamageState Snap(in Damage d) => new()
    {
        Base = d.@base,
        Multiplier = d.multiplier,
        PercentMultiplier = d.percentMultiplier,
        CritMultiplier = d.criticalDamageMultiplier * d.criticalDamagePercentMultiplier,
        ExtraFixed = d.extraFixedDamage,
        Fixed = d.attribute == Damage.Attribute.Fixed,
        Null = d.@null,
    };

    /// <summary>Records a step; never lets our own code break the game's damage handling.</summary>
    private static void Note(DamageTrace trace, System.Delegate handler, string stage, DamageState after)
    {
        try
        {
            var owner = OwnerNames.OfHandler(handler);
            trace.Add(owner.Name, stage, after, owner.Icon);
        }
        catch (Exception e)
        {
            Fail("naming a damage effect", e);
        }
    }

    private static readonly HashSet<string> Failed = new();

    /// <summary>Logs a tracing error once per place; the game carries on as if the mod weren't there.</summary>
    private static void Fail(string where, Exception e)
    {
        if (Failed.Add(where))
            Plugin.Log.LogWarning($"Combat log calculation: error while {where} (only logged once): {e}");
    }

    private static Active Top => Stack.Count > 0 ? Stack[Stack.Count - 1] : null;

    /// <summary>Called by DamageLog.Add: the finished trace of the hit that just landed on <paramref name="target"/>.</summary>
    public static DamageTrace Finish(Character target, in Damage damage, double dealt)
    {
        var active = Top;
        if (active?.Trace == null || active.Target != target)
            return null;
        var trace = active.Trace;
        active.Trace = null;
        trace.Critical = damage.critical;
        trace.Dealt = dealt;
        return trace;
    }

    [HarmonyPatch(typeof(OperationInfos), nameof(OperationInfos.Run), typeof(Character), typeof(float))]
    public static class RegisterOperations
    {
        private static void Prefix(OperationInfos __instance)
        {
            if (!Enabled)
                return;
            try
            {
                OwnerNames.Register(__instance);
            }
            catch (Exception e)
            {
                Fail("registering an attack", e);
            }
        }
    }

    [HarmonyPatch(typeof(Stat), nameof(Stat.GetDamage), typeof(double), typeof(Vector2), typeof(HitInfo))]
    public static class RememberOrigin
    {
        private static void Postfix(HitInfo hitInfo)
        {
            if (!Enabled)
                return;
            try
            {
                _pendingOrigin = OwnerNames.OfHitInfo(hitInfo);
                _pendingFrame = Time.frameCount;
            }
            catch (Exception e)
            {
                Fail("finding what dealt a hit", e);
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.TryAttackCharacter))]
    public static class BeginTrace
    {
        private static void Prefix(Character __instance, ITarget target, ref Damage damage, out bool __state)
        {
            __state = false;
            if (!Enabled)
                return;
            try
            {
                __state = Begin(__instance, target, damage);
            }
            catch (Exception e)
            {
                Fail("starting a trace", e);
            }
        }

        private static bool Begin(Character __instance, ITarget target, in Damage damage)
        {
            var trace = new DamageTrace { Start = Snap(damage) };
            if (_pendingFrame == Time.frameCount && _pendingOrigin is { } origin)
            {
                trace.Origin = origin.Name;
                trace.OriginIcon = origin.Icon;
            }
            else if (DamageSources.CurrentStatus is { } status)
            {
                trace.Origin = DamageSources.Title(status); // a status tick (see StatusContextPatch)
            }
            else if (damage.attacker.projectile != null)
            {
                // Named after what fired it (e.g. the spirit's item); else after the projectile itself.
                if (OwnerNames.OfProjectile(damage.attacker.projectile) is { } fired)
                {
                    trace.Origin = fired.Name;
                    trace.OriginIcon = fired.Icon;
                }
                else
                {
                    trace.Origin = OwnerNames.Humanize(damage.attacker.projectile.gameObject.name);
                }
            }
            _pendingOrigin = null;
            AddStatParts(trace, __instance.stat, damage);
            Stack.Add(new Active { Trace = trace, Target = target.character });
            return true;
        }

        private static void Finalizer(bool __state)
        {
            if (__state && Stack.Count > 0)
                Stack.RemoveAt(Stack.Count - 1);
        }

        /// <summary>Which attack stats made up Damage.multiplier (see Stat.GetDamage).</summary>
        private static void AddStatParts(DamageTrace trace, Stat stat, in Damage damage)
        {
            if (stat == null || damage.attribute == Damage.Attribute.Fixed)
                return;
            void Part(string label, double value)
            {
                if (System.Math.Abs(value - 1) > 1e-6)
                    trace.StatParts.Add((label, value));
            }
            if (damage.attribute == Damage.Attribute.Physical)
                Part("Phys. atk", stat.GetFinal(Stat.Kind.PhysicalAttackDamage));
            else if (damage.attribute == Damage.Attribute.Magic)
                Part("Magic atk", stat.GetFinal(Stat.Kind.MagicAttackDamage));
            if (damage.attackType == Damage.AttackType.Projectile)
                Part("Projectile atk", stat.GetFinal(Stat.Kind.ProjectileAttackDamage));
            if (damage.motionType == Damage.MotionType.Basic)
                Part("Basic atk", stat.GetFinal(Stat.Kind.BasicAttackDamage));
            else if (damage.motionType == Damage.MotionType.Skill)
                Part("Skill atk", stat.GetFinal(Stat.Kind.SkillAttackDamage));
            Part("Total atk", stat.GetFinal(Stat.Kind.AttackDamage));
        }
    }

    [HarmonyPatch(typeof(GiveDamageEvent), nameof(GiveDamageEvent.Invoke))]
    public static class TraceGive
    {
        private static bool Prefix(GiveDamageEvent __instance, ITarget target, ref Damage damage, ref bool __result)
        {
            var active = Top;
            if (active?.Trace == null || active.GiveDone)
                return true; // run the game's own loop
            active.GiveDone = true;
            var trace = active.Trace;
            for (int i = 0; i < __instance.Count; i++)
            {
                var handler = __instance[i];
                var before = Snap(damage);
                bool stop = handler(target, ref damage);
                var after = Snap(damage);
                if (!before.SameAs(after))
                    Note(trace, handler, "give", after);
                if (stop)
                {
                    __result = true;
                    return false;
                }
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(TakeDamageEvent), nameof(TakeDamageEvent.Invoke))]
    public static class TraceTake
    {
        private static bool Prefix(TakeDamageEvent __instance, ref Damage damage, ref bool __result)
        {
            var active = Top;
            if (active?.Trace == null || active.TakeDone || !active.GiveDone)
                return true;
            active.TakeDone = true;
            var trace = active.Trace;
            for (int i = 0; i < __instance.Count; i++)
            {
                var handler = __instance[i];
                var before = Snap(damage);
                bool stop = handler(ref damage);
                var after = Snap(damage);
                if (!before.SameAs(after))
                    Note(trace, handler, "take", after);
                if (stop)
                {
                    __result = true;
                    return false;
                }
            }
            __result = false;
            return false;
        }
    }
}
