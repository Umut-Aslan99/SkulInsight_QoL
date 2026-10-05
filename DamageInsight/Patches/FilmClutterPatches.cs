using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DamageInsight.Codex;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Notes which character the game acts for while effects start, so clean films can leave out the player's
/// (Codex/FilmClutter). Each Prefix pushes exactly one entry (null when unknown) and its Finalizer pops it, also when
/// the game's code throws, so the record stays balanced.
/// </summary>
public static class FilmClutterPatches
{
    // Operations (skills, items, summoned fields, projectile hits) run one by one inside OperationInfos.CRun.
    [HarmonyPatch]
    public static class OperationsRunning
    {
        private static FieldInfo _owner;

        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Characters.Operations.OperationInfos), "CRun"));

        private static void Prefix(object __instance)
        {
            Characters.Character owner = null;
            try
            {
                _owner ??= AccessTools.Field(__instance.GetType(), "<>4__this");
                owner = (_owner?.GetValue(__instance) as Characters.Operations.OperationInfos)?.owner;
            }
            catch (Exception)
            {
                // unknown owner: effects started now count as nobody's
            }
            FilmClutter.Enter(owner);
        }

        private static void Finalizer() => FilmClutter.Leave();
    }

    // Hit sparks: spawned with the attacker (or its projectile) when an attack lands.
    [HarmonyPatch]
    public static class HitEffects
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            new[]
                {
                    typeof(FX.CastAttackVisualEffect.SpawnOnHitPoint), typeof(FX.SmashAttackVisualEffect.SpawnOnHitPoint),
                    typeof(FX.BoundsAttackVisualEffect.RandomWithinIntersect), typeof(FX.ProjectileAttackVisualEffect.SpawnOnHitPoint),
                }
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Where(m => m.Name.StartsWith("Spawn") && m.GetParameters().FirstOrDefault()?.ParameterType is { } first &&
                            (first == typeof(Characters.Character) || first == typeof(Characters.Projectiles.IProjectile)));

        private static void Prefix(object __0)
        {
            Characters.Character owner = null;
            try
            {
                owner = __0 as Characters.Character ?? (__0 as Characters.Projectiles.IProjectile)?.owner;
            }
            catch (Exception)
            {
                // unknown owner
            }
            FilmClutter.Enter(owner);
        }

        private static void Finalizer() => FilmClutter.Leave();
    }

    // Every pooled effect the game starts.
    [HarmonyPatch(typeof(FX.EffectPool), nameof(FX.EffectPool.Play))]
    public static class EffectStarted
    {
        private static void Postfix(EffectPoolInstance __result)
        {
            try
            {
                FilmClutter.Played(__result);
            }
            catch (Exception)
            {
                // never break the game's effects (no closure here: this runs for every effect in the game)
            }
        }
    }
}
