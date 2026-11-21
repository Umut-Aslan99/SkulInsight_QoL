using System.Collections.Generic;
using Characters;
using FX;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Who is who (COOP_LOG 61 (5), UBDuck + Umut: with the same skull both players look alike): a teammate's copy is a
/// little see-through and has an outline in the teammate's colour (the colour of their name tag and damage numbers).
/// Settings Coop/TeammateOpacity and Coop/TeammateOutline.
/// <para>
/// Applied right after the game draws the copy's own effects for the frame (a postfix on SpriteEffectStack.LateUpdate:
/// the stack writes the default look and its effects into the main renderer's property block every frame, so an
/// outline set earlier would be gone). A hit's flash or the game's own outline wins: ours only when no outline is on.
/// The opacity scales the renderer's own alpha like Clear view does: a fade the game sets becomes the new base.
/// </para>
/// </summary>
internal static class TeammateLook
{
    private static readonly int OutlineEnabled = Shader.PropertyToID("_IsOutlineEnabled");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineSize = Shader.PropertyToID("_OutlineSize");
    private static readonly int AlphaThreshold = Shader.PropertyToID("_AlphaThreshold");

    /// <summary>How bright the outline is drawn (the game's own outlines use 2).</summary>
    public const float OutlineBrightness = 2f;

    private sealed class Look
    {
        public Character Copy;
        /// <summary>The copy itself (outline + opacity), or a thing of a copy: its summon, its quintessence's
        /// character, its Gravedigger spirit (opacity only; Umut 2026-11-21: all of a friend's things at 75 %).</summary>
        public bool IsCopy, Belongs;
        public float NextCheck;
        public int Slot = -1;
        public SpriteRenderer Renderer;
    }

    /// <summary>The alpha bookkeeping of one renderer. Kept per RENDERER, applied once a frame: a character has
    /// several sprite stacks on the same main renderer, and with the bookkeeping per stack each one faded what the
    /// one before had faded already, frame after frame, until the copy was nearly invisible (Umut 2026-11-21: "the
    /// second player is too transparent").</summary>
    private sealed class Fade
    {
        public float Base = -1f, Written = -1f;
        public int Frame = -1;
    }

    private static readonly Dictionary<SpriteRenderer, Fade> Fades = new();

    // every stack seen: whose it is is checked again every half second (pooled summons change hands)
    private static readonly Dictionary<SpriteEffectStack, Look> Stacks = new();
    private static readonly MaterialPropertyBlock Block = new();

    internal static float Opacity => Mathf.Clamp(Plugin.CoopTeammateOpacity?.Value ?? 1f, 0.2f, 1f);
    internal static bool Outline => Plugin.CoopTeammateOutline?.Value != false;

    private static void Apply(SpriteEffectStack stack)
    {
        if (!Stacks.TryGetValue(stack, out var look))
        {
            if (Stacks.Count > 2000)
                Stacks.Clear();
            look = new Look { Copy = stack.GetComponentInParent<Character>() };
            Stacks[stack] = look;
        }
        if (look.Copy == null)
            return;
        float now = Time.unscaledTime;
        if (now >= look.NextCheck)
        {
            look.NextCheck = now + 0.5f;
            look.IsCopy = CoopCopies.IsCopy(look.Copy);
            bool belongs = look.IsCopy || CoopCopies.BelongsToCopy(look.Copy);
            if (look.Belongs && !belongs)
                LetGo(look); // a pooled summon now someone else's
            look.Belongs = belongs;
        }
        if (!look.Belongs)
            return;
        var renderer = stack.mainRenderer;
        if (renderer == null)
            return;
        look.Renderer = renderer; // a skull swap gives the copy another main renderer
        if (look.Slot < 0)
            look.Slot = CoopRuntime.SlotOfCharacter(look.Copy);
        if (look.IsCopy && Outline && look.Slot >= 0)
        {
            renderer.GetPropertyBlock(Block);
            if (Block.GetFloat(OutlineEnabled) < 0.5f) // the game's own outline (a flash, a status) wins
            {
                var color = TeammateTags.ColorOf(look.Slot) * OutlineBrightness;
                color.a = 1f;
                Block.SetFloat(OutlineEnabled, 1f);
                Block.SetColor(OutlineColor, color);
                Block.SetFloat(OutlineSize, 1f);
                Block.SetFloat(AlphaThreshold, 0.01f);
                renderer.SetPropertyBlock(Block);
            }
        }
        if (!Fades.TryGetValue(renderer, out var fade))
        {
            if (Fades.Count > 500)
                PruneFades();
            Fades[renderer] = fade = new Fade();
        }
        int frame = Time.frameCount;
        if (fade.Frame == frame)
            return; // another stack on the same renderer did it this frame
        fade.Frame = frame;
        float opacity = Opacity;
        var c = renderer.color;
        if (fade.Base < 0f || Mathf.Abs(c.a - fade.Written) > 0.002f)
            fade.Base = c.a; // the game set its own alpha (a fade, a skill): that is the base now
        float a = fade.Base * opacity;
        if (Mathf.Abs(c.a - a) > 0.0005f)
        {
            c.a = a;
            renderer.color = c;
        }
        fade.Written = a;
    }

    private static void PruneFades()
    {
        var gone = new List<SpriteRenderer>();
        foreach (var pair in Fades)
            if (pair.Key == null)
                gone.Add(pair.Key);
        foreach (var key in gone)
            Fades.Remove(key);
        if (Fades.Count > 500)
            Fades.Clear();
    }

    /// <summary>The alpha this look wrote goes back to the game's own (when nothing else changed it since).</summary>
    private static void LetGo(Look look)
    {
        var renderer = look.Renderer;
        if (renderer != null && Fades.TryGetValue(renderer, out var fade))
        {
            if (fade.Base >= 0f && Mathf.Abs(renderer.color.a - fade.Written) <= 0.002f)
            {
                var c = renderer.color;
                c.a = fade.Base;
                renderer.color = c;
            }
            Fades.Remove(renderer);
        }
        look.Slot = -1;
    }

    [HarmonyPatch(typeof(SpriteEffectStack), "LateUpdate")]
    public static class AfterEffects
    {
        private static void Postfix(SpriteEffectStack __instance)
        {
            if (CoopCopies.Count == 0)
                return;
            try
            {
                Apply(__instance);
            }
            catch (System.Exception)
            {
                // the look is cosmetic: never break the game's own drawing
            }
        }
    }
}
