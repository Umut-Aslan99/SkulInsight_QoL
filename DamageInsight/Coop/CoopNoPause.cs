using Characters;
using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// COOP_PLAN §4.5: "The game never pauses online. The pause menu, inventory, item swap and settings open while the
/// game runs (your character stands still and can be hit)." Skul stops all of time when one of its menus opens
/// (<c>Chronometer.global.AttachTimeScale(menu, 0)</c>). In co-op that stopped the whole room on that game: on the
/// host every enemy (and with it everyone's fight) stood still, on a client its own player and what it shows froze
/// and fell behind. Here, in a session with teammates, the menus' time stop is not attached: the menu opens, the
/// game's own input block keeps the player standing, the world goes on. Time stops that are not menus stay: the
/// map change (Chapter), the death and ending screens (the run is over), the dev tools.
/// Not here yet (the plan's next part): the show-only slow motions (critical hit, death) and the fight-changing
/// ones (the Fighter's slowed world, skills' ChronoInfo) per game.
/// </summary>
// REVIEW(fable): a prefix on ChronometerBase.AttachTimeScale (every status and slow motion goes through it; the
// check is two compares for non-menus). Is the list of menus complete, and is a running world under an open
// inventory / item swap safe (the swap panel of a full inventory, gear popups)? COOP_LOG 57.
[HarmonyPatch(typeof(ChronometerBase), nameof(ChronometerBase.AttachTimeScale), new[] { typeof(object), typeof(float) })]
internal static class CoopNoPause
{
    private static bool Prefix(ChronometerBase __instance, object key, float timeScale)
    {
        if (timeScale != 0f || !ReferenceEquals(__instance, Chronometer.global) || !IsMenu(key))
            return true;
        if (CoopWorld.Current?.Active != true)
            return true;
        CoopTrace.Write("no-pause", CoopWorld.CurrentRoom(), what: key.GetType().Name);
        return false;
    }

    /// <summary>The game's menus that stop time (the decompiled game, 2026-11-15: the 41 callers of AttachTimeScale).</summary>
    internal static bool IsMenu(object key) =>
        key is global::UI.Pause.Panel or global::UI.Inventory.Panel or global::UI.Confirm or global::UI.GearPopup.ItemSelect
            or global::Platforms.PlatformPauseHandler;
}

/// <summary>
/// COOP_PLAN 4.5: "effects that are only for show (critical hit or kill slow motion) become a short screen effect
/// instead". The game's own revives slow the whole world to 0.2 for 0.5 s (an item's revive, the witch's
/// "reassemble"): on the host that slowed every enemy for everyone. In co-op they don't slow time; their heal,
/// sound and particles stay. (The critical hit's slow motion: <see cref="CoopNoCritSlowMotion"/>.)
/// </summary>
// REVIEW(fable): part of REVIEW_QUEUE entry 2 (no pause); COOP_LOG 57.
[HarmonyPatch(typeof(ChronometerBase), nameof(ChronometerBase.AttachTimeScale), new[] { typeof(object), typeof(float), typeof(float) })]
internal static class CoopNoShowSlowMotion
{
    private static bool Prefix(ChronometerBase __instance, object key)
    {
        if (!ReferenceEquals(__instance, Chronometer.global) || !(key is Characters.Abilities.Revive.Instance or Characters.WitchBonus.ReviveOnce))
            return true;
        if (CoopWorld.Current?.Active != true)
            return true;
        CoopTrace.Write("no-slow-motion", CoopWorld.CurrentRoom(), what: key.GetType().Name);
        return false;
    }
}

/// <summary>
/// A critical hit slows the world to 0.2 and speeds its player to 5x for 0.1 s (Characters.Player.PlayerCriticalEffect),
/// on every critical hit. In co-op the world is shared: a host's critical hit slowed every enemy for everyone, a
/// client's made its view fall behind. In co-op only the screen's vignette stays (plan 4.5: "a short screen effect").
/// </summary>
[HarmonyPatch(typeof(Characters.Player.PlayerCriticalEffect), "OnGaveDamage")]
internal static class CoopNoCritSlowMotion
{
    private static bool Prefix(Characters.Player.PlayerCriticalEffect __instance, ITarget target, in Damage tookDamage)
    {
        if (CoopWorld.Current?.Active != true)
            return true;
        if (target?.character != null && tookDamage.amount > 0.0 && tookDamage.critical)
            __instance._vignette.Run(__instance._character);
        return false;
    }
}
