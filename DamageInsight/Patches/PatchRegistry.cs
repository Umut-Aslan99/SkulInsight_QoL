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

    public static List<Result> ApplyAll(Harmony harmony) => PatchClasses().Select(t => Apply(harmony, t)).ToList();

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
