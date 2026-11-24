using System;
using System.Collections.Generic;
using Characters;
using Characters.Projectiles;
using HarmonyLib;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Whose shot is it (Umut 2026-11-21): "enemy projectiles that can hurt us ... a bright, red, slightly flashing ...
/// outline ... as thin as the one the second player has", "our own projectiles ... a bright green outline"; and the red
/// one also when playing alone (a YouTuber, Haxa, asked for it). Settings Outlines/EnemyShots (everywhere) and
/// Outlines/OwnShots (co-op only: alone every shot is yours).
/// <para>
/// The game's projectiles draw with "Lightweight Render Pipeline/2D/Simple" (and glows with ".../2D/Effect"), which
/// have no outline (the shader probe, COOP_LOG 63c); characters draw with a material that has one (`_IsOutlineEnabled`,
/// TeammateLook). So while a shot flies, each of its plain sprite renderers borrows the player's character material
/// and gets a 1-pixel outline through its property block; its own material comes back when the shot ends (the pool
/// reuses it). Glow renderers (the Effect shader: additive light) keep their look: an outline around a glow is noise.
/// </para>
/// </summary>
// REVIEW(fable): borrowing the character material changes how a plain shot is drawn (lighting); Umut judges the look.
internal sealed class ShotOutlines : MonoBehaviour
{
    private const string PlainShader = "Lightweight Render Pipeline/2D/Simple";
    private static readonly int OutlineEnabled = Shader.PropertyToID("_IsOutlineEnabled");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineSize = Shader.PropertyToID("_OutlineSize");
    private static readonly int AlphaThreshold = Shader.PropertyToID("_AlphaThreshold");

    /// <summary>The alpha below which a pixel isn't part of the shot for its outline (0.01 outlined the whole faint
    /// glow of the chapter 2 mages' sun: far bigger than the shot).</summary>
    private const float SolidAlpha = 0.5f;

    private enum Side : byte { Enemy, Own }

    private sealed class Shot
    {
        public Side Side;
        public SpriteRenderer[] Renderers;
        public Material[] Originals;
    }

    private static readonly Dictionary<Projectile, Shot> Shots = new();
    private static readonly List<Projectile> Gone = new();
    private static readonly MaterialPropertyBlock Block = new();
    private static readonly HashSet<string> NotOursTraced = new();
    private static Material _outlined;

    private static bool EnemyOn => Plugin.CoopEnemyShotOutline?.Value != false;
    private static bool OwnOn => Plugin.CoopOwnShotOutline?.Value != false;

    /// <summary>Anything to do: enemy shots everywhere, own ones only with a teammate.</summary>
    private static bool Active => EnemyOn || OwnOn && CoopCopies.Count > 0;

    /// <summary>A material that draws outlines: the local player's character material.</summary>
    private static Material Outlined()
    {
        if (_outlined != null)
            return _outlined;
        var player = Singleton<Service>.Instance?.levelManager?.player;
        var material = player != null ? player.spriteEffectStack?.mainRenderer?.sharedMaterial : null;
        if (material != null && material.HasProperty(OutlineEnabled))
            _outlined = material;
        return _outlined;
    }

    private static void Fired(Projectile projectile, Character owner)
    {
        if (!Active || projectile == null || owner == null)
            return;
        if (Shots.TryGetValue(projectile, out var before))
        {
            Shots.Remove(projectile); // reused by its pool before the frame ended: decided anew
            Restore(before);
        }
        Side side;
        if (CoopCopies.BelongsToCopy(owner))
            return; // a teammate's: faded (ClearView), no outline
        var behind = CoopCopies.PlayerBehind(owner);
        if (behind != null)
        {
            if (!OwnOn || CoopCopies.Count == 0)
                return;
            // Green only for THIS player's shots and its minions'. Umut 2026-11-23: "the projectiles of a coop friend
            // can get fully green": a minion whose leader isn't known here is its own answer in PlayerBehind (on the
            // players' side, not the copy's), so a teammate's summon shot with this player's bright green outline,
            // which on a small shot covers most of it.
            if (behind != Singleton<Service>.Instance?.levelManager?.player)
            {
                if (NotOursTraced.Count < 64 && NotOursTraced.Add(owner.name))
                    CoopTrace.Write("outline-skipped", CoopWorld.CurrentRoom(), name: CoopTrace.NameOf(owner),
                        what: "a players' side shot that isn't this player's (" + owner.type + ", behind it: " + behind.name + ")");
                return;
            }
            side = Side.Own;
        }
        else
        {
            if (!EnemyOn)
                return;
            side = Side.Enemy;
        }
        var material = Outlined();
        if (material == null)
            return;
        var renderers = projectile.GetComponentsInChildren<SpriteRenderer>(true);
        var picked = new List<SpriteRenderer>();
        var originals = new List<Material>();
        foreach (var renderer in renderers)
        {
            var own = renderer != null ? renderer.sharedMaterial : null;
            if (own == null || own.shader == null || own.shader.name != PlainShader)
                continue; // a glow, or a material that draws its own way
            picked.Add(renderer);
            originals.Add(own);
            renderer.sharedMaterial = material;
        }
        if (picked.Count == 0)
            return;
        Shots[projectile] = new Shot { Side = side, Renderers = picked.ToArray(), Originals = originals.ToArray() };
    }

    private static void Ended(Projectile projectile)
    {
        if (projectile == null || !Shots.TryGetValue(projectile, out var shot))
            return;
        Shots.Remove(projectile);
        Restore(shot);
    }

    private static void Restore(Shot shot)
    {
        for (int i = 0; i < shot.Renderers.Length; i++)
        {
            var renderer = shot.Renderers[i];
            if (renderer == null)
                continue;
            renderer.SetPropertyBlock(null);
            renderer.sharedMaterial = shot.Originals[i];
        }
    }

    private void LateUpdate()
    {
        if (Shots.Count == 0)
            return;
        Gone.Clear();
        float t = Time.unscaledTime;
        // red, slightly pulsing (HDR brightness like the game's own outlines, which use 2)
        var red = new Color(1f, 0.12f, 0.1f, 1f) * (2.2f + 0.7f * Mathf.Sin(t * 7f));
        var green = new Color(0.25f, 1f, 0.3f, 1f) * 1.8f;
        red.a = green.a = 1f;
        foreach (var pair in Shots)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
            {
                Gone.Add(pair.Key);
                continue;
            }
            var color = pair.Value.Side == Side.Enemy ? red : green;
            foreach (var renderer in pair.Value.Renderers)
            {
                if (renderer == null)
                    continue;
                renderer.GetPropertyBlock(Block);
                Block.SetFloat(OutlineEnabled, 1f);
                Block.SetColor(OutlineColor, color);
                Block.SetFloat(OutlineSize, 1f);
                Block.SetFloat(AlphaThreshold, SolidAlpha); // only around the solid part: the mages' sun has a wide faint glow (Umut 2026-11-21)
                renderer.SetPropertyBlock(Block);
            }
        }
        foreach (var projectile in Gone)
        {
            if (Shots.TryGetValue(projectile, out var shot))
                Restore(shot);
            Shots.Remove(projectile);
        }
    }

    private void OnDestroy()
    {
        foreach (var shot in Shots.Values)
            Restore(shot);
        Shots.Clear();
        _outlined = null;
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Fire))]
    public static class OnFire
    {
        private static void Postfix(Projectile __instance, Character owner)
        {
            if (!Active)
                return;
            try
            {
                Fired(__instance, owner);
            }
            catch (Exception)
            {
                // a look only: never break the game's shots
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Despawn))]
    public static class OnDespawn
    {
        private static void Prefix(Projectile __instance)
        {
            if (Shots.Count == 0)
                return;
            try
            {
                Ended(__instance);
            }
            catch (Exception)
            {
                // a look only
            }
        }
    }
}
