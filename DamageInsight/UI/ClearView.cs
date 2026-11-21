using System;
using System.Collections.Generic;
using Characters.Projectiles;
using DamageInsight.Codex;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// Clear view (0.14, players' request through Umut 2026-10-25: with a strong build "many many effects happen and the
/// visibility really suffers ... runs die to random damage you couldn't see properly"): the effects the game starts
/// for you and your summons (skills, items, hit sparks, fields: <see cref="FilmClutter"/> tells them apart) and your
/// projectiles get fainter, over the run (full in chapter 1, the lowest opacity from chapter 5 on) and when many of them
/// are on screen at once. Enemies' attacks and their warnings are never touched. Settings section "Clear View".
/// </summary>
/// <remarks>
/// Runs after the game's own updates (LateUpdate), where the effect's animation has set its colour for the frame. A
/// renderer's alpha is remembered with what we wrote: when it still holds our value the animation left it alone; when
/// it changed, the game (an animation, a fade) set a new one, which becomes the base. Effects get their colour anew
/// from the game when spawned; projectiles are reused as they are, so they get their alpha back when they despawn.
/// </remarks>
public sealed class ClearView : MonoBehaviour
{
    /// <summary>Your effects and projectiles on screen at once before "when busy" starts fading, and where it is at its lowest.</summary>
    public const int BusyFrom = 8, BusyFull = 40;

    private sealed class Faded
    {
        public float Base, Written;
        public int Frame;
    }

    private static readonly Dictionary<SpriteRenderer, Faded> Renderers = new();
    private static readonly Dictionary<Projectile, SpriteRenderer[]> Projectiles = new();
    /// <summary>Co-op: the projectiles a teammate's copy fired here (faded to the teammate opacity too).</summary>
    private static readonly HashSet<Projectile> TeammateShots = new();
    private static readonly List<SpriteRenderer> Stale = new();
    private static readonly List<Projectile> Gone = new();

    private float _opacity = 1f;
    private float _nextPrune;
    private Action _apply;

    private static bool On => Plugin.ClearViewEnabled?.Value == true;

    /// <summary>A projectile was fired (Patches/ClearViewPatches): yours or your summons' fade too.</summary>
    public static void Fired(Projectile projectile, Characters.Character owner)
    {
        if (projectile == null)
            return;
        // co-op (Umut 2026-11-21): all of a teammate's things are faded like the teammate, also with Clear view off
        bool mate = Coop.CoopCopies.Count > 0 && Coop.CoopCopies.BelongsToCopy(owner);
        if (mate)
            TeammateShots.Add(projectile);
        else
            TeammateShots.Remove(projectile);
        if (!mate && (!On || Plugin.ClearViewProjectiles?.Value != true || !FilmClutter.IsPlayerSide(owner)))
            return;
        Projectiles[projectile] = projectile.GetComponentsInChildren<SpriteRenderer>(true);
    }

    /// <summary>How solid a teammate's things are drawn (1 without co-op).</summary>
    private static float TeammateOpacity => Coop.CoopCopies.Count > 0 ? Coop.TeammateLook.Opacity : 1f;

    /// <summary>A projectile ends: its renderers get their alpha back (the pool reuses it as it is).</summary>
    public static void Despawned(Projectile projectile)
    {
        if (projectile == null || !Projectiles.TryGetValue(projectile, out var renderers))
            return;
        Projectiles.Remove(projectile);
        TeammateShots.Remove(projectile);
        foreach (var renderer in renderers)
            Restore(renderer);
    }

    private void LateUpdate()
    {
        _apply ??= Apply;
        Patches.Guard.Run("Clear view", _apply);
    }

    private void Apply()
    {
        if (Renderers.Count == 0 && Projectiles.Count == 0 && !On && Coop.CoopCopies.Count == 0)
            return;
        int frame = Time.frameCount;
        int busy = 0;
        foreach (var effect in FilmClutter.PlayerEffects)
            if (effect != null && effect.state == EffectPoolInstance.State.Playing)
                busy++;
        Gone.Clear();
        foreach (var pair in Projectiles)
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
                Gone.Add(pair.Key);
        foreach (var projectile in Gone)
        {
            Projectiles.Remove(projectile);
            TeammateShots.Remove(projectile);
        }
        busy += Projectiles.Count;

        float target = On ? Target(busy) : 1f;
        // Eased: from full to 40% takes about half a second, so a burst of effects doesn't flicker.
        _opacity = Mathf.MoveTowards(_opacity, target, Time.unscaledDeltaTime * 1.2f);
        float mate = _opacity * TeammateOpacity;
        if (_opacity < 0.999f || mate < 0.999f)
        {
            foreach (var effect in FilmClutter.PlayerEffects)
            {
                if (effect == null || effect.state != EffectPoolInstance.State.Playing || effect.renderer == null)
                    continue;
                float opacity = FilmClutter.IsTeammates(effect) ? mate : _opacity;
                if (opacity < 0.999f)
                    Dim(effect.renderer, frame, opacity);
            }
            foreach (var pair in Projectiles)
            {
                float opacity = TeammateShots.Contains(pair.Key) ? mate : _opacity;
                if (opacity >= 0.999f)
                    continue;
                foreach (var renderer in pair.Value)
                    if (renderer != null)
                        Dim(renderer, frame, opacity);
            }
        }

        // What wasn't faded this frame (an effect that ended or fades out on its own, full opacity again) is let go.
        Stale.Clear();
        foreach (var pair in Renderers)
            if (pair.Value.Frame != frame)
                Stale.Add(pair.Key);
        foreach (var renderer in Stale)
            Restore(renderer);

        if (Time.unscaledTime >= _nextPrune)
        {
            _nextPrune = Time.unscaledTime + 10f;
            FilmClutter.PruneDestroyed();
        }
    }

    /// <summary>How opaque your effects should be now.</summary>
    private static float Target(int busy)
    {
        bool overRun = Plugin.ClearViewOverRun?.Value != false, whenBusy = Plugin.ClearViewWhenBusy?.Value != false;
        float fade = !overRun && !whenBusy ? 1f : Math.Max(overRun ? RunProgress() : 0f, whenBusy ? BusyFade(busy) : 0f);
        return Mathf.Lerp(1f, Mathf.Clamp(Plugin.ClearViewLowest?.Value ?? 0.4f, 0.05f, 1f), fade);
    }

    /// <summary>0 in chapter 1 (and the castle), 1 from chapter 5 on; the Dark Mirror's chapters count the same.</summary>
    internal static float RunProgress(Level.Chapter.Type chapter) => chapter switch
    {
        Level.Chapter.Type.Chapter2 or Level.Chapter.Type.HardmodeChapter2 => 0.25f,
        Level.Chapter.Type.Chapter3 or Level.Chapter.Type.HardmodeChapter3 => 0.5f,
        Level.Chapter.Type.Chapter4 or Level.Chapter.Type.HardmodeChapter4 => 0.75f,
        Level.Chapter.Type.Chapter5 or Level.Chapter.Type.HardmodeChapter5 or Level.Chapter.Type.HardmodeChapter6 => 1f,
        _ => 0f,
    };

    private static float RunProgress()
    {
        var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
        return chapter == null ? 0f : RunProgress(chapter.type);
    }

    /// <summary>0 up to <see cref="BusyFrom"/> of your effects and projectiles at once, 1 from <see cref="BusyFull"/>.</summary>
    internal static float BusyFade(int busy) => Mathf.Clamp01((busy - BusyFrom) / (float)(BusyFull - BusyFrom));

    private static void Dim(SpriteRenderer renderer, int frame, float opacity)
    {
        var color = renderer.color;
        if (!Renderers.TryGetValue(renderer, out var faded))
            Renderers[renderer] = faded = new Faded { Base = color.a };
        else if (Mathf.Abs(color.a - faded.Written) > 0.002f)
            faded.Base = color.a; // the game set a new alpha (its animation or fade)
        color.a = faded.Base * opacity;
        renderer.color = color;
        faded.Written = color.a;
        faded.Frame = frame;
    }

    private static void Restore(SpriteRenderer renderer)
    {
        if ((object)renderer == null)
            return;
        if (renderer == null || !Renderers.TryGetValue(renderer, out var faded))
        {
            Renderers.Remove(renderer); // destroyed with its scene
            return;
        }
        Renderers.Remove(renderer);
        var color = renderer.color;
        if (Mathf.Abs(color.a - faded.Written) <= 0.002f)
        {
            color.a = faded.Base;
            renderer.color = color;
        }
    }
}
