using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using GameResources;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// Finds the icon sprite for each DamageSource, based on the "Damage Icons" config section.
/// A config value looks like "inscription:Mutation", "skill:SomeSkill", "item:SomeItem",
/// "quintessence:Name", "gear:Name", or "none" to keep the text tag.
/// </summary>
public static class IconLibrary
{
    // Provisional picks from the game's inscription icons. The user chooses the final set.
    private static readonly Dictionary<DamageSource, string> Defaults = new()
    {
        [DamageSource.Basic] = "inscription:Arms",
        [DamageSource.Skill] = "inscription:Wisdom",
        [DamageSource.Item] = "inscription:Artifact",
        [DamageSource.Quintessence] = "inscription:Mystery",
        [DamageSource.Poison] = "inscription:Poisoning",
        [DamageSource.Burn] = "inscription:Arson",
        [DamageSource.Bleed] = "inscription:ExcessiveBleeding",
        [DamageSource.Shock] = "inscription:Manatech",
        [DamageSource.Ember] = "inscription:Arson",
        [DamageSource.Status] = "none",
        [DamageSource.Dash] = "inscription:Rapidity",
        [DamageSource.Swap] = "inscription:Mutation",
        [DamageSource.DarkAbility] = "inscription:Omen",
        [DamageSource.Other] = "none",
    };

    private static readonly Dictionary<DamageSource, ConfigEntry<string>> Entries = new();
    private static readonly Dictionary<DamageSource, Sprite> Cache = new();
    private static readonly HashSet<string> WarnedMissing = new();

    public static void Bind(ConfigFile config)
    {
        foreach (DamageSource source in Enum.GetValues(typeof(DamageSource)))
        {
            var entry = config.Bind("Damage Icons", source.ToString(), Defaults[source],
                "Icon shown next to this damage type. Examples: inscription:Mutation, skill:<name>, item:<name>, none");
            entry.SettingChanged += (_, _) => Cache.Remove(source);
            Entries[source] = entry;
        }
    }

    /// <summary>The configured icon, e.g. "inscription:Mutation" or "none".</summary>
    public static string Spec(DamageSource source) =>
        Entries.TryGetValue(source, out var entry) ? entry.Value : "none";

    /// <summary>Returns the icon for a source, or null to fall back to the text tag.</summary>
    public static Sprite Get(DamageSource source)
    {
        if (Cache.TryGetValue(source, out var cached) && cached != null)
            return cached;

        string spec = Spec(source);
        Sprite sprite = Resolve(spec);
        if (sprite != null)
            Cache[source] = sprite;
        else if (spec != "none" && WarnedMissing.Add(spec))
            Plugin.Log.LogWarning($"Icon '{spec}' for {source} not found (yet); using the text tag.");
        return sprite;
    }

    private static Sprite Resolve(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        int colon = spec.IndexOf(':');
        string kind = colon >= 0 ? spec.Substring(0, colon).Trim().ToLowerInvariant() : "inscription";
        string name = (colon >= 0 ? spec.Substring(colon + 1) : spec).Trim();

        IEnumerable<Sprite> pool = kind switch
        {
            "inscription" => CommonResource.instance?._keywordIcons,
            "inscription_full" => CommonResource.instance?._keywordFullactiveIcons,
            "skill" => GearResource.instance?._skillIcons,
            "item" => GearResource.instance?._itemBuffIcons,
            "quintessence" => GearResource.instance?._quintessenceHudIcons,
            "gear" => GearResource.instance?._gearThumbnails,
            "skull" => GearResource.instance?._weaponHudMainIcons,
            _ => null,
        };
        if (pool == null)
            return null;

        var sprites = pool.Where(s => s != null).ToList();
        // Exact name first, then "contains", both ignoring case (sprite names may have prefixes/suffixes).
        return sprites.FirstOrDefault(s => s.name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? sprites.FirstOrDefault(s => s.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
