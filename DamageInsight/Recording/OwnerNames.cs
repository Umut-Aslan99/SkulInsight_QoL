using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Characters.Abilities;
using Characters.Gear;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Upgrades;
using Characters.Operations;
using UnityEngine;
using DamageInsight.Lang;

namespace DamageInsight.Recording;

/// <summary>
/// Names (and icons) for the things behind a hit: which item/skull/inscription/dark ability a damage handler or
/// an attack belongs to. The game's handlers are delegates on ability objects that don't know their item, so we
/// map every Ability that an equipped gear holds back to that gear (rebuilt when we meet an unknown one).
/// </summary>
public static class OwnerNames
{
    public readonly struct Owner
    {
        public readonly string Name;
        public readonly Sprite Icon;
        public Owner(string name, Sprite icon) { Name = name; Icon = icon; }
    }

    private static readonly Dictionary<object, Owner> ByObject = new();
    private static readonly Dictionary<Type, FieldInfo[]> AbilityFields = new();
    private static readonly Dictionary<Type, FieldInfo[]> HitInfoFields = new();
    private static readonly Dictionary<Type, FieldInfo[]> ObjectFields = new();
    private static readonly Dictionary<HitInfo, Owner> ByHitInfo = new();
    private static readonly HashSet<OperationInfos> Registered = new();
    private static float _nextRebuild;
    private static readonly HashSet<Type> Unresolved = new();

    /// <summary>The owner of a damage handler (GiveDamageDelegate / TakeDamageDelegate).</summary>
    public static Owner OfHandler(Delegate handler)
    {
        object target = handler.Target;
        if (target != null && ByObject.TryGetValue(target, out var known))
            return known;

        Owner owner = Resolve(target, handler.Method.DeclaringType, out bool found);
        // Only cache real owners: a fallback name may be replaced once the maps know this handler's gear.
        if (target != null && found)
            Remember(target, owner);
        else if (Unresolved.Add(handler.Method.DeclaringType))
            Plugin.Log.LogInfo($"[TraceDiag] unnamed damage handler {handler.Method.DeclaringType?.FullName}.{handler.Method.Name} " +
                               $"(target {target?.GetType().FullName ?? "static"}) shown as '{owner.Name}'");
        return owner;
    }

    private static readonly Dictionary<object, Owner> ByProjectile = new();
    private static Owner? _firing;

    /// <summary>A FireProjectile operation starts: remember whose it is (projectiles fired now belong to it).</summary>
    public static void BeginFiring(Component operation)
    {
        _firing = OfComponent(operation);
        if (_firing == null && TryRebuild())
            _firing = OfComponent(operation);
    }

    public static void EndFiring() => _firing = null;

    /// <summary>A projectile was fired: link it to the operation that is firing right now.</summary>
    public static void LinkProjectile(object projectile)
    {
        if (_firing is not { } owner || projectile == null)
            return;
        if (ByProjectile.Count > 5000)
            ByProjectile.Clear();
        ByProjectile[projectile] = owner; // pooled projectiles are reused: the latest firing wins
    }

    /// <summary>What fired a projectile, if known.</summary>
    public static Owner? OfProjectile(object projectile) =>
        projectile != null && ByProjectile.TryGetValue(projectile, out var owner) ? owner : (Owner?)null;

    /// <summary>What dealt a hit, from its HitInfo (registered when the attack's operations start).</summary>
    public static Owner? OfHitInfo(HitInfo hitInfo)
    {
        if (hitInfo == null)
            return null;
        if (ByHitInfo.TryGetValue(hitInfo, out var owner))
            return owner;
        if (TryRebuild() && ByHitInfo.TryGetValue(hitInfo, out owner))
            return owner;
        return null;
    }

    /// <summary>
    /// Called when a sequence of operations starts (OperationInfos.Run): remember every HitInfo in it as
    /// belonging to the gear it sits in, or to its root object (e.g. a spawned "Oberon_Attack").
    /// </summary>
    public static void Register(OperationInfos operations)
    {
        if (operations == null || !Registered.Add(operations))
            return;
        var owner = OfComponent(operations) ?? new Owner(Humanize(operations.transform.root.name), null);
        foreach (var behaviour in operations.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;
            foreach (var field in FieldsOf(behaviour.GetType(), HitInfoFields, f => typeof(HitInfo).IsAssignableFrom(f.FieldType)))
                if (field.GetValue(behaviour) is HitInfo hitInfo)
                    ByHitInfo[hitInfo] = owner;
        }
    }

    private static Owner Resolve(object target, Type declaringType, out bool found)
    {
        found = true;
        switch (target)
        {
            case Characters.Stat:
                return new Owner(Loc.N("Damage taken"), null);
            case Component component when OfComponent(component) is { } gear:
                return gear;
            case AbilityInstance instance:
                if (FindAbilityOwner(instance.ability) is { } abilityOwner)
                    return abilityOwner;
                found = false;
                return new Owner(Humanize(declaringType), instance.icon);
            case Ability ability when FindAbilityOwner(ability) is { } owner:
                return owner;
        }
        found = false;
        return new Owner(Humanize(declaringType), null);
    }

    private static Owner? FindAbilityOwner(Ability ability)
    {
        if (ability == null)
            return null;
        if (ByObject.TryGetValue(ability, out var owner))
            return owner;
        if (TryRebuild() && ByObject.TryGetValue(ability, out owner))
            return owner;
        return null;
    }

    private static bool _dirty = true;

    /// <summary>Gear, a dark ability or an inscription changed: the next unknown handler may rebuild the maps.</summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>
    /// Rebuilds the maps when the player's gear changed, otherwise at most every 30 seconds. Scanning all gear
    /// (FindObjectsOfType) takes a few milliseconds; handlers that belong to no gear (enemy buffs, the game's own
    /// rules) would otherwise trigger it constantly and make the game stutter.
    /// </summary>
    private static bool TryRebuild()
    {
        if (!_dirty && Time.unscaledTime < _nextRebuild)
            return false;
        _dirty = false;
        _nextRebuild = Time.unscaledTime + 30f;
        RebuildAbilityMap();
        return true;
    }

    /// <summary>
    /// Maps every component, Ability and HitInfo of every gear, dark ability and inscription to its owner. Also
    /// follows references to objects outside the gear's hierarchy: e.g. a spirit is detached from its item so it
    /// can follow the player (SpiritContainer._spirit), but its attacks still belong to the item.
    /// </summary>
    private static void RebuildAbilityMap()
    {
        var roots = new List<(Component root, Owner owner)>();
        foreach (var gear in UnityEngine.Object.FindObjectsOfType<Gear>(true))
            roots.Add((gear, OwnerOf(gear)));
        foreach (var upgrade in UnityEngine.Object.FindObjectsOfType<UpgradeObject>(true))
            roots.Add((upgrade, new Owner(upgrade.displayName, upgrade.icon)));
        foreach (var inscription in UnityEngine.Object.FindObjectsOfType<InscriptionInstance>(true))
            if (inscription.keyword != null)
                roots.Add((inscription, new Owner(inscription.keyword.name, inscription.keyword.activeIcon)));

        foreach (var (root, owner) in roots)
        {
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null).ToList();
            foreach (var detached in DetachedObjects(root.transform, behaviours).ToList()) // ToList: the loop adds to behaviours
                behaviours.AddRange(detached.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null));

            foreach (var behaviour in behaviours)
            {
                Remember(behaviour, owner);
                foreach (var field in FieldsOf(behaviour.GetType(), AbilityFields, f => typeof(Ability).IsAssignableFrom(f.FieldType)))
                    if (field.GetValue(behaviour) is Ability ability)
                        Remember(ability, owner);
                foreach (var field in FieldsOf(behaviour.GetType(), HitInfoFields, f => typeof(HitInfo).IsAssignableFrom(f.FieldType)))
                    if (field.GetValue(behaviour) is HitInfo hitInfo)
                        ByHitInfo[hitInfo] = owner;
            }
        }
    }

    /// <summary>Scene objects that components under <paramref name="root"/> reference but that live outside it.</summary>
    private static IEnumerable<Transform> DetachedObjects(Transform root, List<MonoBehaviour> behaviours)
    {
        var found = new HashSet<Transform>();
        foreach (var behaviour in behaviours)
        {
            foreach (var field in FieldsOf(behaviour.GetType(), ObjectFields,
                         f => typeof(Component).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(GameObject)))
            {
                var value = field.GetValue(behaviour) as UnityEngine.Object;
                var t = value is Component c && c != null ? c.transform : value is GameObject go && go != null ? go.transform : null;
                // Only live scene objects (prefab assets have no valid scene) that are not already under the root.
                // Never follow into characters (the player, enemies) or other gear: that would claim their hits.
                if (t != null && t.gameObject.scene.IsValid() && !t.IsChildOf(root)
                    && t.GetComponentInParent<Characters.Character>() == null && t.GetComponentInParent<Gear>() == null
                    && t.GetComponentInChildren<Characters.Character>(true) == null && found.Add(t))
                    yield return t;
            }
        }
    }

    private static void Remember(object key, Owner owner)
    {
        if (ByObject.Count > 20000)
            ByObject.Clear();
        ByObject[key] = owner;
    }

    /// <summary>The gear, dark ability or inscription a component sits in, if any.</summary>
    private static Owner? OfComponent(Component component)
    {
        if (ByObject.TryGetValue(component, out var known))
            return known;
        for (var t = component.transform; t != null; t = t.parent)
        {
            if (t.GetComponent<Gear>() is { } gear)
                return OwnerOf(gear);
            if (t.GetComponent<UpgradeObject>() is { } upgrade)
                return new Owner(upgrade.displayName, upgrade.icon);
            if (t.GetComponent<InscriptionInstance>() is { keyword: not null } inscription)
                return new Owner(inscription.keyword.name, inscription.keyword.activeIcon);
        }
        return null;
    }

    private static Owner OwnerOf(Gear gear)
    {
        Sprite icon = null;
        try { icon = gear.thumbnail; } catch (Exception) { /* no dropped sprite */ }
        string name;
        try { name = gear.displayName; } catch (Exception) { name = Humanize(gear.name); }
        return new Owner(string.IsNullOrEmpty(name) ? Humanize(gear.name) : name, icon);
    }

    private static FieldInfo[] FieldsOf(Type type, Dictionary<Type, FieldInfo[]> cache, Func<FieldInfo, bool> match)
    {
        if (cache.TryGetValue(type, out var fields))
            return fields;
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(match));
        return cache[type] = list.ToArray();
    }

    /// <summary>"SoulReap+Instance" / "Oberon_Attack(Clone)" → "Soul reap" / "Oberon attack".</summary>
    public static string Humanize(Type type)
    {
        if (type == null)
            return "Unknown";
        // Handlers usually live in nested "Instance" classes or compiler-generated closures: name the outer type.
        while (type.DeclaringType != null && (type.Name == "Instance" || type.Name.StartsWith("<")))
            type = type.DeclaringType;
        return Humanize(Regex.Replace(type.Name, "(Component|Instance)$", ""));
    }

    public static string Humanize(string name)
    {
        name = Regex.Replace(name ?? "", @"\(Clone\)|`\d+", "").Replace('_', ' ').Trim();
        name = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");
        return name.Length == 0 ? "Unknown" : char.ToUpperInvariant(name[0]) + name.Substring(1).ToLowerInvariant();
    }
}
