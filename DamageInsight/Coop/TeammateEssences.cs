using System;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using Characters.Gear.Quintessences;
using Characters.Gear.Quintessences.Effects;
using HarmonyLib;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// A teammate's quintessence on this game (Umut 2026-11-20 23:40: "the partner must SEE quintessence effects:
/// Archdemon, Medusa and the others"). A quintessence acts through its own parts (its character's action, its
/// operations, its effects), not through the player's moves, so the motion replay never showed it. Now:
/// - the owner's game sends "quintessence used" (<see cref="TeammateEventKind.Essence"/>) when its player's quintessence
///   really ran (constraints and cooldown passed: the game invoked its active);
/// - each copy carries its own instance of the teammate's quintessence (loaded with the loadout, TeammateProjectiles.
///   LoadGear), disowned (never this game's gear), equipped for show only (no passive, no stats);
/// - on the event the copy's instance runs its active (the same parts as on the owner's game). Its hits, statuses
///   and kills do nothing here (CoopCopies: its character belongs to the copy), its projectiles are the owner's
///   (TeammateProjectiles: the copy's own shots are put back, the sent ones are fired).
/// <para>
/// Also the owner of a quintessence's character: the game's RunAction detaches that character from the quintessence
/// in Awake (`transform.parent = null`), so CoopCopies.PlayerBehind's search up the hierarchy never found its gear (the
/// 2026-11-20 Archdemon fix could not work). <see cref="Register"/> maps every character a quintessence's parts point
/// to back to it; PlayerBehind asks <see cref="Of"/> first.
/// </para>
/// </summary>
// REVIEW(fable): a new message kind and a second, private instance of the teammate's quintessence on every copy (no
// passive run, owner set directly). Check against the quintessence sweep (`coop-test.py --essences`) per name.
internal static class TeammateEssences
{
    // ------------------------------------------------------------------ whose character

    private static readonly Dictionary<Character, Quintessence> CharacterOf = new();
    private static readonly HashSet<Quintessence> Registered = new();
    private static readonly Dictionary<Type, FieldInfo[]> CharacterFields = new();
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The quintessence a character is part of (its detached character), or null.</summary>
    internal static Quintessence Of(Character character)
    {
        if (character == null || CharacterOf.Count == 0 || !CharacterOf.TryGetValue(character, out var essence))
            return null;
        return essence != null ? essence : null;
    }

    /// <summary>Notes the characters a quintessence's parts point to (once per instance).</summary>
    internal static void Register(Quintessence essence)
    {
        if (essence == null || !Registered.Add(essence))
            return;
        if (Registered.Count > 64)
            Registered.RemoveWhere(e => e == null);
        if (CharacterOf.Count > 256)
        {
            var dead = new List<Character>();
            foreach (var pair in CharacterOf)
                if (pair.Key == null || pair.Value == null)
                    dead.Add(pair.Key);
            foreach (var key in dead)
                CharacterOf.Remove(key);
        }
        foreach (var part in essence.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (part == null)
                continue;
            foreach (var field in CharacterFieldsOf(part.GetType()))
            {
                if (field.GetValue(part) is Character character && character != null)
                {
                    CharacterOf[character] = essence;
                    CoopCopies.Forget(character); // a "nobody's" verdict from before
                }
            }
        }
    }

    private static FieldInfo[] CharacterFieldsOf(Type type)
    {
        if (CharacterFields.TryGetValue(type, out var known))
            return known;
        var found = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            foreach (var field in t.GetFields(Fields))
                if (typeof(Character).IsAssignableFrom(field.FieldType))
                    found.Add(field);
        return CharacterFields[type] = found.ToArray();
    }

    // ------------------------------------------------------------------ the owner's game: "used"

    private static Quintessence _using;
    private static bool _activated;

    [HarmonyPatch(typeof(Quintessence), nameof(Quintessence.Use))]
    public static class OnUse
    {
        private static void Prefix(Quintessence __instance)
        {
            _using = __instance;
            _activated = false;
            try
            {
                Register(__instance);
            }
            catch (Exception)
            {
                // the owner lookup is a help; never stop the game's quintessence
            }
        }

        private static void Postfix(Quintessence __instance)
        {
            if (!_activated || _using != __instance)
                return;
            try
            {
                var owner = __instance.owner;
                if (owner == null || owner != TeammateProjectiles.Local || TeammateProjectiles.Send == null)
                    return;
                string name = Loadout.GearName(__instance.name);
                TeammateProjectiles.Send(TeammateEvent.ForEssence(name));
                CoopTrace.Write("essence-sent", CoopWorld.CurrentRoom(), what: name);
                Testing.BotEssences.Sent++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] a quintessence use was not sent: " + e.Message);
            }
        }

        private static void Finalizer()
        {
            _using = null;
            _activated = false;
        }
    }

    /// <summary>The game runs a quintessence's effects: inside its Use, that is the active (constraints and cooldown
    /// passed). The passive runs from SetOwner, never inside Use.</summary>
    [HarmonyPatch(typeof(QuintessenceEffect.Subcomponents), nameof(QuintessenceEffect.Subcomponents.Invoke))]
    public static class OnEffects
    {
        private static void Prefix(Quintessence quintessence)
        {
            if (quintessence != null && ReferenceEquals(quintessence, _using))
                _activated = true;
        }
    }

    // ------------------------------------------------------------------ the copy's game

    private sealed class Held
    {
        public string Name = "";
        public Quintessence Instance;
    }

    private static readonly Dictionary<Character, Held> CopiesHeld = new();
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static FieldInfo _onUseField;
    private static bool _onUseLooked;

    /// <summary>Every frame for a shown copy (CoopRuntime): the copy carries the teammate's quintessence, made here
    /// once its prefab has loaded (TeammateProjectiles.LoadGear started it when the loadout came).</summary>
    internal static void Keep(Character copy, string name)
    {
        if (copy == null)
            return;
        name ??= "";
        CopiesHeld.TryGetValue(copy, out var held);
        if (held != null && held.Name == name && (name.Length == 0 || held.Instance != null))
            return;
        if (held != null && held.Name != name)
        {
            Discard(held);
            CopiesHeld.Remove(copy);
            held = null;
        }
        if (name.Length == 0)
            return;
        var prefab = TeammateProjectiles.LoadedGear(name);
        var essence = prefab != null ? prefab.GetComponent<Quintessence>() : null;
        if (essence == null)
            return; // still loading (or not in this game: the use will say so)
        if (CopiesHeld.Count > 8)
        {
            var gone = new List<Character>();
            foreach (var pair in CopiesHeld)
                if (pair.Key == null)
                    gone.Add(pair.Key);
            foreach (var key in gone)
                CopiesHeld.Remove(key);
        }
        CopiesHeld[copy] = new Held { Name = name, Instance = Make(copy, essence, name) };
    }

    private static Quintessence Make(Character copy, Quintessence prefab, string name)
    {
        try
        {
            var levels = Singleton<Service>.Instance?.levelManager;
            var essence = levels != null ? levels.DropQuintessence(prefab, copy.transform.position) : null;
            if (essence == null)
                return null;
            CoopCopies.Disown(essence);
            essence.lootable = false;
            // as QuintessenceInventory.EquipAt does, without the stats and the passive (SetOwner): only the active is shown
            essence.transform.parent = copy.@base;
            essence.transform.localPosition = Vector3.zero;
            essence.owner = copy;
            essence.state = Characters.Gear.Gear.State.Equipped;
            Register(essence);
            CoopTrace.Write("essence-ready", CoopWorld.CurrentRoom(), what: name);
            return essence;
        }
        catch (Exception e)
        {
            if (Logged.Add("make " + name))
                Plugin.Log.LogWarning($"[Coop] teammate quintessence {name} not made: {e.Message}");
            CoopTrace.Write("essence-missing", CoopWorld.CurrentRoom(), what: name, name: "not made: " + e.Message);
            return null;
        }
    }

    private static void Discard(Held held)
    {
        if (held?.Instance == null)
            return;
        try
        {
            // Out of the copy first: the instance hangs under the copy's base (as QuintessenceInventory.EquipAt does), so
            // the copy's animation list took in its animations (the Harpy quintessence's harpy). Destroyed there, they
            // stayed listed and the game's animation Update threw every frame (n3 --essences, COOP_LOG 64h). The game
            // itself detaches a dropped quintessence; it never destroys one under its player.
            var essence = held.Instance;
            var copy = essence.owner;
            var list = copy != null && copy.animationController != null ? copy.animationController.animations : null;
            list?.RemoveAll(a => a == null || a.transform.IsChildOf(essence.transform));
            essence.transform.parent = null;
            UnityEngine.Object.Destroy(essence.gameObject);
        }
        catch (Exception)
        {
            // gone with its copy
        }
    }

    /// <summary>The teammate used its quintessence: the copy's instance runs the same active. False when this game
    /// has no such quintessence for the copy (yet).</summary>
    internal static bool Use(Character copy, string name)
    {
        if (copy == null || string.IsNullOrEmpty(name))
            return false;
        CopiesHeld.TryGetValue(copy, out var held);
        var essence = held != null && held.Name == name ? held.Instance : null;
        if (essence == null)
        {
            string why = held == null || held.Name != name ? "the copy carries " + (held?.Name ?? "none") : "not loaded";
            if (Logged.Add("missing " + name))
                Plugin.Log.LogInfo($"[Coop] teammate quintessence {name} not shown: {why}");
            CoopTrace.Write("essence-missing", CoopWorld.CurrentRoom(), what: name, name: why);
            return false;
        }
        try
        {
            essence._active?.Invoke(essence);
            if (!_onUseLooked)
            {
                _onUseLooked = true;
                // the event's backing field (UseQuintessence parts listen to it)
                _onUseField = typeof(Quintessence).GetField("onUse", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? typeof(Quintessence).GetField("m_onUse", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_onUseField == null)
                    Plugin.Log.LogWarning("[Coop] Quintessence.onUse not found: quintessences with a summoned character show only their effects");
            }
            (_onUseField?.GetValue(essence) as Action)?.Invoke();
            CoopTrace.Write("essence-shown", CoopWorld.CurrentRoom(), what: name);
            return true;
        }
        catch (Exception e)
        {
            if (Logged.Add("failed " + name))
                Plugin.Log.LogWarning($"[Coop] teammate quintessence {name} failed: {e}");
            CoopTrace.Write("essence-missing", CoopWorld.CurrentRoom(), what: name, name: "failed: " + e.Message);
            return false;
        }
    }
}
