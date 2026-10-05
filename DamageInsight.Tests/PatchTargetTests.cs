using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DamageInsight.Patches;
using HarmonyLib;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>
/// Checks every Harmony patch of the mod against the real game code (Assembly-CSharp.dll from the
/// Skul install), without starting the game: the hooked method must still exist with the expected
/// parameters, and every game value a patch reads (parameters by name, private fields) must still exist.
/// If Skul renames or changes something we hook, the test for that patch class fails and says why.
/// </summary>
/// <remarks>
/// We don't actually apply the patches here: patching recompiles the game method, and many of them call
/// into Unity's native engine code, which only works inside the game. The in-game SelfTest covers that part.
/// </remarks>
public class PatchTargetTests
{
    public static IEnumerable<object[]> PatchClassNames() =>
        PatchRegistry.PatchClasses().Select(t => new object[] { t.FullName });

    [Fact]
    public void ThereArePatchClasses()
    {
        Assert.NotEmpty(PatchRegistry.PatchClasses());
    }

    [Theory]
    [MemberData(nameof(PatchClassNames))]
    public void PatchMatchesTheGame(string patchClassName)
    {
        Type patchClass = PatchRegistry.PatchClasses().Single(t => t.FullName == patchClassName);
        var classInfo = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patchClass));

        var patchMethods = patchClass.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(IsPatchMethod)
            .ToList();
        Assert.True(patchMethods.Count > 0, "No patch methods (Prefix/Postfix/Finalizer/...) found.");

        // Targets computed by the patch class itself (TargetMethod / TargetMethods): each must exist.
        var computed = ComputedTargets(patchClass);
        if (computed != null)
        {
            Assert.True(computed.Count > 0, "TargetMethod(s) found no game method.");
            foreach (var target in computed)
            {
                Assert.True(target != null, "TargetMethod(s) returned a missing game method.");
                foreach (var patchMethod in patchMethods)
                    CheckInjectedParameters(patchMethod, target);
            }
            return;
        }

        foreach (var patchMethod in patchMethods)
        {
            var methodInfo = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromMethod(patchMethod));
            var info = classInfo.Merge(methodInfo);

            MethodBase target = ResolveTarget(info);
            Assert.True(target != null,
                $"{patchMethod.Name}: game method {info.declaringType?.FullName}.{info.methodName}" +
                $"({string.Join(", ", info.argumentTypes?.Select(a => a.Name) ?? new string[0])}) not found.");

            CheckInjectedParameters(patchMethod, target);
        }
    }

    /// <summary>The game methods a patch class picks itself (Harmony's TargetMethod / TargetMethods), or null.</summary>
    private static List<MethodBase> ComputedTargets(Type patchClass)
    {
        const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        if (patchClass.GetMethod("TargetMethods", any) is { } many)
            return ((IEnumerable<MethodBase>)many.Invoke(null, null)).ToList();
        if (patchClass.GetMethod("TargetMethod", any) is { } one)
            return new List<MethodBase> { (MethodBase)one.Invoke(null, null) };
        return null;
    }

    private static readonly string[] PatchMethodNames = { "Prefix", "Postfix", "Finalizer", "Transpiler" };

    private static bool IsPatchMethod(MethodInfo m) =>
        PatchMethodNames.Contains(m.Name) ||
        m.GetCustomAttributes(true).Any(a => a is HarmonyPrefix or HarmonyPostfix or HarmonyFinalizer or HarmonyTranspiler);

    /// <summary>Finds the game method described by the merged [HarmonyPatch] attributes.</summary>
    private static MethodBase ResolveTarget(HarmonyMethod info)
    {
        Type type = info.declaringType;
        if (type == null)
            return null;

        // Harmony already turns [HarmonyPatch(..., ArgumentType.Ref/Out)] into by-ref types here.
        Type[] args = info.argumentTypes;

        if ((info.methodType ?? MethodType.Normal) == MethodType.Normal && args == null)
        {
            // Without argument types Harmony takes the first overload; in the game that fails with
            // "Ambiguous match found". Require explicit argument types when a method is overloaded.
            int overloads = AccessTools.GetDeclaredMethods(type).Count(m => m.Name == info.methodName);
            Assert.True(overloads <= 1, $"{type.Name}.{info.methodName} has {overloads} overloads: give the argument types in [HarmonyPatch].");
        }

        return info.methodType switch
        {
            MethodType.Getter => AccessTools.PropertyGetter(type, info.methodName),
            MethodType.Setter => AccessTools.PropertySetter(type, info.methodName),
            MethodType.Constructor => AccessTools.Constructor(type, args),
            _ => AccessTools.Method(type, info.methodName, args),
        };
    }

    /// <summary>
    /// Harmony matches patch parameters to the original by name. If the game renames a parameter
    /// or a private field we read (___field), the patch would silently get defaults or fail.
    /// </summary>
    private static void CheckInjectedParameters(MethodInfo patchMethod, MethodBase target)
    {
        var originalParams = target.GetParameters().Select(p => p.Name).ToHashSet();
        foreach (var p in patchMethod.GetParameters())
        {
            string name = p.Name;
            if (name.StartsWith("___")) // Harmony: three underscores + the field name
            {
                string field = name.Substring(3);
                Assert.True(AccessTools.Field(target.DeclaringType, field) != null,
                    $"{patchMethod.Name}: field '{field}' no longer exists on {target.DeclaringType.Name}.");
            }
            else if (name == "__instance")
            {
                Assert.True(p.ParameterType.IsAssignableFrom(target.DeclaringType),
                    $"{patchMethod.Name}: __instance is {p.ParameterType.Name}, but the method is on {target.DeclaringType.Name}.");
            }
            else if (!name.StartsWith("__"))
            {
                Assert.True(originalParams.Contains(name),
                    $"{patchMethod.Name}: parameter '{name}' not found on {target.DeclaringType.Name}.{target.Name}" +
                    $"({string.Join(", ", originalParams)}).");
            }
        }
    }
}
