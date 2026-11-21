using System;
using System.Collections.Generic;
using Characters;
using Characters.Abilities.Customs;
using Characters.Gear.Weapons;
using HarmonyLib;
using Level;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// The Gravedigger's spirits, graves and ghouls on the teammate's game (Umut 2026-11-20: "the partner must see the
/// Gravedigger's graves and summons"). Its passive makes the spirits (GraveDiggerCorpse: every few seconds, on a kill,
/// when a map is entered) and only for THIS game's player (it checks the local player's skull), so a copy never made
/// any; a spirit becomes a grave when its Gravedigger hits it, and a copy's hits do nothing here; with no graves the
/// replayed "raise from graves" move raised nothing. Now:
/// - the owner's game sends where a spirit appears (<see cref="TeammateEventKind.Corpse"/>) and where one breaks into a
///   grave (<see cref="TeammateEventKind.Grave"/>), relative to the map;
/// - the copy's passive makes spirits and graves only from those (its own would be a second set), and a spirit of a
///   copy can't be broken here (its owner's game decides);
/// - ghouls: the grave container takes its owner from <c>levelManager.player</c> when it wakes (GraveDiggerGraveContainer
///   .Awake), so a copy's "raise from graves" summoned the ghouls as THIS game's player's minions (real hits here). The
///   container now raises them for the owner of its skull.
/// The moves that make graves directly (SummonGrave) are replayed by the copy as before.
/// </summary>
// REVIEW(fable): Gravedigger on the teammate's game: spirits/graves from events, the container's owner fixed for every
// game (also the local player's: the skull's owner, the same as before for one player).
internal static class TeammateGraves
{
    private static readonly Dictionary<Character, GraveDiggerPassive> Passives = new();
    private static readonly List<GraveDiggerCorpse> CopySpirits = new();
    private static bool _replaying;

    private static Vector3 MapOrigin => Map.Instance != null ? Map.Instance.transform.position : Vector3.zero;

    private static void SendSpot(TeammateEventKind kind, Vector3 position)
    {
        var at = position - MapOrigin;
        TeammateProjectiles.Send(TeammateEvent.ForSpot(kind, at.x, at.y));
        CoopTrace.Write(kind == TeammateEventKind.Corpse ? "spirit-sent" : "grave-sent", CoopWorld.CurrentRoom());
    }

    private static bool IsLocal(Character owner) =>
        owner != null && owner == TeammateProjectiles.Local && TeammateProjectiles.Send != null;

    [HarmonyPatch(typeof(GraveDiggerPassive), nameof(GraveDiggerPassive.CreateInstance))]
    public static class Remember
    {
        private static void Postfix(GraveDiggerPassive __instance, Character owner)
        {
            if (owner == null)
                return;
            if (Passives.Count > 8)
            {
                var gone = new List<Character>();
                foreach (var pair in Passives)
                    if (pair.Key == null || pair.Value == null)
                        gone.Add(pair.Key);
                foreach (var key in gone)
                    Passives.Remove(key);
            }
            Passives[owner] = __instance;
        }
    }

    [HarmonyPatch(typeof(GraveDiggerPassive), nameof(GraveDiggerPassive.SpawnCorpse))]
    public static class Spirit
    {
        private static bool Prefix(GraveDiggerPassive __instance, Vector3 position)
        {
            try
            {
                var owner = __instance._owner;
                if (CoopCopies.IsCopy(owner))
                    return _replaying; // the copy's spirits come from its owner's game
                if (IsLocal(owner))
                    SendSpot(TeammateEventKind.Corpse, position);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] Gravedigger spirit not handled: " + e.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GraveDiggerPassive), nameof(GraveDiggerPassive.HandleCorpseDie))]
    public static class SpiritBroken
    {
        private static bool Prefix(GraveDiggerPassive __instance, Vector3 position)
        {
            try
            {
                var owner = __instance._owner;
                if (CoopCopies.IsCopy(owner))
                    return false; // the grave comes from its owner's game
                if (IsLocal(owner))
                    SendSpot(TeammateEventKind.Grave, position);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] Gravedigger grave not handled: " + e.Message);
            }
            return true;
        }
    }

    /// <summary>A spirit of a copy is the copy's (fades with it, never this game's player's).</summary>
    [HarmonyPatch(typeof(GraveDiggerCorpse), nameof(GraveDiggerCorpse.SetPassive))]
    public static class SpiritOwner
    {
        private static void Postfix(GraveDiggerCorpse __instance, Character owner)
        {
            var spirit = __instance.minion != null ? __instance.minion.character : null;
            if (spirit == null)
                return;
            if (CoopCopies.IsCopy(owner))
            {
                CoopCopies.Adopt(spirit, owner);
                CopySpirits.RemoveAll(s => s == null);
                if (!CopySpirits.Contains(__instance))
                    CopySpirits.Add(__instance);
            }
            else
            {
                CoopCopies.Unadopt(spirit); // a pooled spirit used again
                CopySpirits.Remove(__instance);
            }
        }
    }

    /// <summary>A copy's spirit isn't broken by hits here: its owner's game breaks it and sends the grave.</summary>
    [HarmonyPatch(typeof(GraveDiggerCorpse), nameof(GraveDiggerCorpse.Hit),
        new[] { typeof(Character), typeof(Damage), typeof(Vector2) }, new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class SpiritHit
    {
        private static bool Prefix(GraveDiggerCorpse __instance) =>
            _replaying || CopySpirits.Count == 0 || !CopySpirits.Contains(__instance);
    }

    /// <summary>The ghouls rise for the owner of the skull the container belongs to (the game takes this game's player
    /// when the container wakes).</summary>
    [HarmonyPatch(typeof(GraveDiggerGraveContainer), nameof(GraveDiggerGraveContainer.SummonMinionFromGraves),
        new[] { typeof(Characters.Minion), typeof(int) })]
    public static class GhoulsForTheSkullsOwner
    {
        private static void Prefix(GraveDiggerGraveContainer __instance)
        {
            try
            {
                var weapon = __instance.GetComponentInParent<Weapon>();
                if (weapon != null && weapon.owner != null && weapon.owner != __instance._owner)
                    __instance._owner = weapon.owner;
            }
            catch (Exception)
            {
                // as the game had it
            }
        }
    }

    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    /// <summary>The teammate's game made a spirit or a grave: the copy's passive makes the same here.</summary>
    internal static void Show(Character copy, TeammateEvent e, Vector3 mapOrigin)
    {
        if (copy == null || e == null)
            return;
        if (!Passives.TryGetValue(copy, out var passive) || passive == null)
        {
            if (Logged.Add("no passive"))
                Plugin.Log.LogInfo("[Coop] a teammate's Gravedigger spirit / grave came, but its copy has no Gravedigger passive (yet)");
            CoopTrace.Write("grave-missing", CoopWorld.CurrentRoom(), what: e.Kind.ToString());
            return;
        }
        var at = mapOrigin + new Vector3(e.OffsetX, e.OffsetY, 0f);
        _replaying = true;
        try
        {
            if (e.Kind == TeammateEventKind.Corpse)
            {
                passive.SpawnCorpse(at);
                CoopTrace.Write("spirit-shown", CoopWorld.CurrentRoom());
            }
            else
            {
                BreakNearest(at);
                passive.SpawnGrave(at);
                CoopTrace.Write("grave-shown", CoopWorld.CurrentRoom());
            }
        }
        catch (Exception ex)
        {
            if (Logged.Add("failed " + e.Kind))
                Plugin.Log.LogWarning($"[Coop] teammate Gravedigger {e.Kind} failed: {ex.Message}");
            CoopTrace.Write("grave-missing", CoopWorld.CurrentRoom(), what: e.Kind.ToString(), name: ex.Message);
        }
        finally
        {
            _replaying = false;
        }
    }

    /// <summary>The copy's spirit nearest to where the owner's broke (it dies as the game's does: its own effect).</summary>
    private static void BreakNearest(Vector3 at)
    {
        GraveDiggerCorpse nearest = null;
        float best = 2.5f * 2.5f;
        foreach (var spirit in CopySpirits)
        {
            if (spirit == null || !spirit.isActiveAndEnabled)
                continue;
            float d = ((Vector2)(spirit.transform.position - at)).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = spirit;
            }
        }
        var character = nearest != null && nearest.minion != null ? nearest.minion.character : null;
        if (character != null && character.health != null && !character.health.dead)
            character.health.Kill();
    }
}
