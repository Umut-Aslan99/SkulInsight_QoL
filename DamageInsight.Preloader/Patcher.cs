using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace DamageInsight.Preloader;

/// <summary>
/// BepInEx 5's entry point for the preloader (it looks for TargetDLLs and Patch). Runs once at game start, before
/// Assembly-CSharp is loaded: with co-op off it only logs one line; with co-op on it rewrites the player reads
/// (<see cref="PlayerReads"/>) in memory. A failure leaves the game's code untouched.
/// </summary>
public static class Patcher
{
    private static readonly ManualLogSource Log = Logger.CreateLogSource("SkulInsight Preloader");

    public static IEnumerable<string> TargetDLLs => new[] { "Assembly-CSharp.dll" };

    public static void Patch(AssemblyDefinition assembly)
    {
        if (!CoopGate.FromStartOptions(Environment.GetCommandLineArgs(), out string why)
            && !CoopGate.IsOn(Path.Combine(Paths.ConfigPath, CoopGate.ConfigFileName), out why))
        {
            Log.LogInfo($"Co-op is off ({why}): the game's code is not changed.");
            return;
        }
        try
        {
            var sites = PlayerReads.Patch(assembly.MainModule);
            if (sites == null)
            {
                Log.LogInfo("Co-op: the player reads were already rewritten.");
                return;
            }
            string counts = string.Join(", ", sites.GroupBy(s => s.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
            Log.LogInfo($"Co-op is on ({why}): {sites.Count} player reads ({counts}); list {PlayerReads.Hash(sites)}.");
            int unclassified = sites.Count(s => !s.Classified);
            if (unclassified > 0)
                Log.LogWarning($"Co-op: {unclassified} player reads have no rule (a game update?) and stay local: " +
                               string.Join(", ", sites.Where(s => !s.Classified).Take(10).Select(s => s.Key)));
        }
        catch (Exception e)
        {
            // Cecil works on the definition BepInEx hands us; a half-done rewrite can't be undone here, so say so.
            Log.LogError($"Co-op: rewriting the player reads failed: {e}");
        }
    }
}
