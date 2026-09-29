using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Abilities;
using Characters.Abilities.CharacterStat;
using Characters.Gear.Items;
using Characters.Gear.Synergy.Inscriptions;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Describe;

/// <summary>
/// "What if I picked this up?": while the popup of an item on the ground is being filled (GearPopupPreviewPatch),
/// every description uses the stats the player would have with that item. We let the game compute them: a copy
/// of the player's Stat gets all of the player's current bonuses, plus the item's own stat and permanent stat
/// abilities, and every inscription whose step changes gets its stat bonus swapped for the new step's.
/// </summary>
public static class PickupPreview
{
    /// <summary>The item on the ground whose popup is being built, or null.</summary>
    public static Item Current { get; private set; }

    /// <summary>The owned item it would replace (the swap menu when the inventory is full), or null.</summary>
    public static Item Replacing { get; private set; }

    private static Item _cachedFor, _cachedReplacing;
    private static int _cachedFrame = -1;
    private static (StatSnapshot stats, List<string> changes) _cached;

    public static void Begin(Item item, Item replacing = null)
    {
        Current = item;
        Replacing = replacing;
    }

    public static void End()
    {
        Current = null;
        Replacing = null;
    }

    /// <summary>The stats with <see cref="Current"/> picked up, and short notes about what changes.</summary>
    public static (StatSnapshot stats, List<string> changes) Preview()
    {
        if (_cachedFor == Current && _cachedReplacing == Replacing && _cachedFrame == Time.frameCount)
            return _cached;
        _cachedFor = Current;
        _cachedReplacing = Replacing;
        _cachedFrame = Time.frameCount;
        _cached = Build(Current, Replacing);
        return _cached;
    }

    private static (StatSnapshot, List<string>) Build(Item item, Item replacing)
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        var inscriptions = player?.playerComponents?.inventory?.synergy?.inscriptions;
        if (item == null || player == null || inscriptions == null)
            return (GearDescriptions.CurrentStats(ignorePreview: true), new List<string>());

        var changes = new List<string>();
        var remove = new HashSet<Stat.Values>();
        var add = new List<Stat.Values>();

        // The item's own stat (ItemInventory attaches item.stat) and its permanent stat abilities; the replaced
        // item's are the same Values objects on the player's Stat, so they are simply left out of the copy.
        add.AddRange(PermanentStats(item));
        if (replacing != null)
            foreach (var values in PermanentStats(replacing))
                remove.Add(values);

        // Inscriptions whose step would change.
        var added = PickupMath.CountDeltas(new[] { item.keyword1.ToString(), item.keyword2.ToString() },
            replacing != null ? new[] { replacing.keyword1.ToString(), replacing.keyword2.ToString() } : new string[0]);
        foreach (var pair in added)
        {
            if (!Enum.TryParse(pair.Key, out Inscription.Key key))
                continue;
            var inscription = inscriptions[key];
            if (inscription?.steps == null || inscription.steps.Count == 0)
                continue;
            if (pair.Value == 0)
                continue;
            int newCount = Math.Max(0, inscription.count + pair.Value);
            int newStep = PickupMath.StepFor(inscription.steps, newCount);
            if (newStep == inscription.step)
                continue;
            // The game font has no arrow glyph.
            changes.Add($"{inscription.name} {inscription.count} > {newCount}");

            var keyword = GearDescriptions.SimpleStatBonus(inscription);
            if (keyword == null)
                continue; // not a plain stat inscription: its damage lines still use the new stats
            if (inscription._instance is SimpleStatBonusKeyword live && live._statBonus?.stat != null)
                remove.Add(live._statBonus.stat);
            var (byStep, category, categoryIndex, kindIndex) = keyword.Value;
            if (newStep > 0 && newStep < byStep.Length && byStep[newStep] != 0)
                add.Add(new Stat.Values(new Stat.Value(categoryIndex, kindIndex, PickupMath.StatValue(category, byStep[newStep]))));
        }

        var preview = CopyWithout(player.stat, player, remove);
        foreach (var values in add)
            preview.AttachValues(values);
        preview.Update();

        var stats = GearDescriptions.Snapshot(preview, player);
        var now = GearDescriptions.CurrentStats(ignorePreview: true);
        AddStatChange(changes, "Phys. atk", now.Physical, stats.Physical);
        AddStatChange(changes, "Magic atk", now.Magic, stats.Magic);
        AddStatChange(changes, "Total atk", now.AttackDamage, stats.AttackDamage);
        AddStatChange(changes, "Skill atk", now.Skill, stats.Skill);
        AddStatChange(changes, "Basic atk", now.Basic, stats.Basic);
        return (stats, changes);
    }

    /// <summary>The stat Values an item puts on the player while owned: its own stat and permanent StatBonus abilities.</summary>
    private static IEnumerable<Stat.Values> PermanentStats(Item item)
    {
        if (item.stat != null)
            yield return item.stat;
        foreach (var attacher in item.GetComponentsInChildren<AlwaysAbilityAttacher>(true))
            if (attacher._abilityComponent is StatBonusComponent bonus && bonus._ability?._stat != null && bonus._ability.duration <= 0)
                yield return bonus._ability._stat;
    }

    /// <summary>A new Stat with all of <paramref name="source"/>'s bonuses except <paramref name="without"/>.</summary>
    private static Stat CopyWithout(Stat source, Character owner, HashSet<Stat.Values> without)
    {
        var copy = new Stat(owner);
        foreach (var values in source._bonuses)
            if (!without.Contains(values))
                copy.AttachValues(values);
        foreach (var withEvent in source._bonusesWithEvent)
            if (!without.Contains(withEvent.values))
                copy.AttachValues(withEvent.values);
        foreach (var timed in source._timedBonuses)
            copy.AttachValues(timed.values);
        foreach (var wrapper in source._onUpdated._items)
            copy.onUpdated.Add(wrapper.priority, wrapper.value);
        return copy;
    }

    private static void AddStatChange(List<string> changes, string label, double before, double after)
    {
        if (Math.Abs(after - before) > 1e-6)
            changes.Add(FormattableString.Invariant($"{label} {before * 100:0.#}% > {after * 100:0.#}%"));
    }
}
