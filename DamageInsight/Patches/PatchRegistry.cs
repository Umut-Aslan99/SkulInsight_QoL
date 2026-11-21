using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DamageInsight.Patches;

/// <summary>
/// Finds and applies this mod's Harmony patch classes one by one, so a patch broken by a game
/// update only disables its own feature and is reported by name (see SelfTest and the tests).
/// </summary>
public static class PatchRegistry
{
    public readonly struct Result
    {
        public readonly Type PatchClass;
        public readonly int PatchedMethods;
        public readonly Exception Error;

        public Result(Type patchClass, int patchedMethods, Exception error)
        {
            PatchClass = patchClass;
            PatchedMethods = patchedMethods;
            Error = error;
        }

        public bool Ok => Error == null && PatchedMethods > 0;
    }

    /// <summary>Every class in this mod marked with [HarmonyPatch], including nested classes.</summary>
    public static IEnumerable<Type> PatchClasses() =>
        typeof(PatchRegistry).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(HarmonyAttribute), inherit: false).Length > 0)
            .OrderBy(t => t.FullName);

    public static List<Result> ApplyAll(Harmony harmony) =>
        PatchClasses().Where(t => !AppliedSeparately(t)).Select(t => Apply(harmony, t)).ToList();

    /// <summary>
    /// Patch classes that their own code applies at a set moment instead of ApplyAll (still checked by the patch target
    /// tests): the co-op test copies' save redirect, which only a test copy gets, at the very start; the block of
    /// puppet AI main loops (every AI class), applied when co-op starts.
    /// </summary>
    public static bool AppliedSeparately(Type patchClass) =>
        patchClass == typeof(Coop.Testing.SaveIsolationPatches) || patchClass == typeof(Coop.BossSync.NoPuppetMainLoop)
        // co-op test copies only: the probe's census of effects and projectiles, the test camera
        || patchClass == typeof(Coop.Testing.CoopProbe.EffectCensus) || patchClass == typeof(Coop.Testing.CoopProbe.ProjectileCensus)
        || patchClass == typeof(Coop.Testing.CoopShots.CameraOnHost) || patchClass == typeof(Coop.Testing.CoopShots.SameBackground);

    public static Result Apply(Harmony harmony, Type patchClass)
    {
        try
        {
            var patched = harmony.CreateClassProcessor(patchClass).Patch();
            return new Result(patchClass, patched?.Count ?? 0, null);
        }
        catch (Exception e)
        {
            return new Result(patchClass, 0, e);
        }
    }
}
