using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Characters;
using Characters.Projectiles;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Which materials the game's projectiles and effects draw with, and whether those can show the game's sprite outline
/// (`_IsOutlineEnabled`, `_OutlineColor`, `_OutlineSize`: TeammateLook uses them on characters). For the outlines by
/// side (Umut 2026-11-21: enemy projectiles that can hurt you red, your own green). Test copies only (the probe runs):
/// one LogOutput line per prefab name, "[ShaderProbe] projectile|effect <side> <name>: <renderer> '<shader>' outline=..".
/// </summary>
internal static class ShaderProbe
{
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> Shaders = new(StringComparer.Ordinal);

    private static string Side(Character owner)
    {
        if (owner == null)
            return "world";
        if (CoopCopies.BelongsToCopy(owner))
            return "teammate";
        return CoopCopies.IsPlayerSide(owner) ? "own" : "enemy";
    }

    private static void Note(string kind, string side, string name, GameObject root)
    {
        if (!CoopProbe.On || root == null || Seen.Count > 600 || !Seen.Add(kind + side + name))
            return;
        var line = new StringBuilder($"[ShaderProbe] {kind} {side} {name}:");
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true).Take(6))
        {
            var material = renderer.sharedMaterial;
            string shader = material != null && material.shader != null ? material.shader.name : "none";
            bool outline = material != null && material.HasProperty("_IsOutlineEnabled");
            line.Append($" {renderer.GetType().Name} '{shader}' outline={(outline ? "yes" : "no")};");
            Shaders.TryGetValue(shader + (outline ? " +outline" : ""), out int n);
            Shaders[shader + (outline ? " +outline" : "")] = n + 1;
        }
        Plugin.Log.LogInfo(line.ToString());
        if (Seen.Count % 50 == 0)
            Plugin.Log.LogInfo("[ShaderProbe] shaders so far: " + string.Join(", ", Shaders.OrderByDescending(p => p.Value).Select(p => $"{p.Key} x{p.Value}")));
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Fire))]
    public static class Fired
    {
        private static void Postfix(Projectile __instance, Character owner)
        {
            if (!CoopProbe.On)
                return;
            try
            {
                Note("projectile", Side(owner), Loadout.PrefabName(__instance.name), __instance.gameObject);
            }
            catch (Exception)
            {
                // a probe only
            }
        }
    }

    [HarmonyPatch(typeof(FX.EffectPool), nameof(FX.EffectPool.Play))]
    public static class Played
    {
        private static void Postfix(EffectPoolInstance __result)
        {
            if (!CoopProbe.On || __result == null)
                return;
            try
            {
                var owner = Codex.FilmClutter.Actor;
                string name = __result.renderer != null && __result.renderer.sprite != null ? __result.renderer.sprite.name : __result.name;
                Note("effect", Side(owner), name, __result.gameObject);
            }
            catch (Exception)
            {
                // a probe only
            }
        }
    }
}
