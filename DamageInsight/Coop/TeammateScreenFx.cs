using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Characters;
using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// A teammate's screen-wide effects stay on its own screen (Umut 2026-11-21: "make sure that the visibility doesnt get
/// too cluttered"). A copy replays its owner's moves and quintessence with the game's own operations, and some of
/// those act on the whole screen of THIS game: camera shake, zoom, screen flash, vignette, controller rumble, effects
/// drawn over the screen, music changes. Nothing stopped them for copies, so every big teammate skill shook and
/// flashed your screen too. Setting Coop/TeammateScreenEffects (off) brings them back.
/// </summary>
internal static class TeammateScreenFx
{
    private static bool Allowed => Plugin.CoopTeammateScreenEffects?.Value == true;

    /// <summary>True when the operation may run: not a teammate's, or the setting allows it.</summary>
    private static bool Runs(Character owner) => owner == null || Allowed || !CoopCopies.BelongsToCopy(owner);

    private static readonly HashSet<string> Counted = new(StringComparer.Ordinal);

    private static bool Skip(Character owner, object operation)
    {
        if (CoopCopies.Count == 0 || Runs(owner))
            return false;
        string name = operation.GetType().Name;
        if (Counted.Add(name))
            CoopTrace.Write("teammate-screen-fx", CoopWorld.CurrentRoom(), what: name);
        return true;
    }

    /// <summary>Operations with an owner: Run(Character owner).</summary>
    [HarmonyPatch]
    public static class WithOwner
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            new[]
                {
                    typeof(Characters.Operations.Fx.CameraShake), typeof(Characters.Operations.Fx.CameraShakeCurve),
                    typeof(Characters.Operations.Fx.CameraZoom), typeof(Characters.Operations.Fx.ScreenFlash),
                    typeof(Characters.Operations.Fx.Vignette), typeof(Characters.Operations.Fx.Vibration),
                    typeof(Characters.Operations.Fx.PlayMusic), typeof(Characters.Operations.Fx.PlayMusicAtPoint),
                    typeof(Characters.Operations.Fx.StopMusic),
                }
                .Select(t => AccessTools.DeclaredMethod(t, "Run", new[] { typeof(Character) }))
                .Where(m => m != null);

        private static bool Prefix(object __instance, Character owner)
        {
            try
            {
                return !Skip(owner, __instance);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <summary>Operations without an owner: the character the game acts for (Codex/FilmClutter's record).</summary>
    [HarmonyPatch]
    public static class WithoutOwner
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            new[]
                {
                    typeof(Characters.Operations.Fx.SpawnEffectOnScreen), typeof(Characters.Operations.Fx.PauseBackgroundMusic),
                    typeof(Characters.Operations.Fx.SetInternalMusicVolume), typeof(Characters.Operations.Fx.PlayChapterMusic),
                }
                .Select(t => AccessTools.DeclaredMethod(t, "Run", Type.EmptyTypes))
                .Where(m => m != null);

        private static bool Prefix(object __instance)
        {
            try
            {
                return !Skip(Codex.FilmClutter.Actor, __instance);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
