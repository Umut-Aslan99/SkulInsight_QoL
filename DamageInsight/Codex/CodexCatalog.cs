using System;
using System.Collections.Generic;
using System.Linq;
using Characters.Gear.Synergy.Inscriptions;
using GameResources;
using UnityEngine;
using DamageInsight.Lang;

namespace DamageInsight.Codex;

/// <summary>
/// Every Codex entry, built once from the game's own lists: enemies and bosses from Characters.Key, skulls/items/
/// essences from GearResource, inscriptions from Inscription.keys. Names come from the game's translations.
/// </summary>
public static class CodexCatalog
{
    private static List<CodexEntry> _entries;
    private static readonly Dictionary<string, GearReference> Gear = new();
    private static readonly Dictionary<string, Characters.Gear.Upgrades.UpgradeResource.Reference> Dark = new();

    public static IReadOnlyList<CodexEntry> Entries => _entries ??= Build();

    public static string EnemyId(Characters.Key key) => "enemy:" + key;
    public static string GearId(Characters.Gear.Gear.Type type, string name) => type switch
    {
        Characters.Gear.Gear.Type.Weapon => "skull:" + name,
        Characters.Gear.Gear.Type.Quintessence => "essence:" + name,
        _ => "item:" + name,
    };
    public static string InscriptionId(Inscription.Key key) => "inscription:" + key;
    public static string DarkId(string name) => "dark:" + name;

    /// <summary>Id for an enemy the game has no key for (key Unspecified, e.g. Dark Mirror bosses), by its object name.</summary>
    public static string NamedId(string objectName) => "named:" + CleanName(objectName);

    public static string CleanName(string objectName) =>
        System.Text.RegularExpressions.Regex.Replace(objectName ?? "", @"\(Clone\)|\s+", "").Trim();

    /// <summary>"ElderEnt(Hardmode)" → "Elder Ent (Dark Mirror)".</summary>
    public static string DisplayName(string cleanName)
    {
        string name = System.Text.RegularExpressions.Regex.Replace(cleanName, @"(?<=[a-z])(?=[A-Z])", " ");
        return name.Replace("(Hardmode)", " (Dark Mirror)").Replace("_", " ").Trim();
    }

    /// <summary>Adds an entry for an enemy without a game key the first time it is met (and when loading).</summary>
    public static void EnsureNamed(string id, string name, CodexCategory category)
    {
        var entries = (List<CodexEntry>)Entries;
        if (entries.Any(e => e.Id == id))
            return;
        entries.Add(NamedEntry(id, name, category, entries.Count));
    }

    /// <summary>
    /// An entry for an enemy without a game key. Adventurers (the party of chapters 1-3, their Dark Mirror supports)
    /// and the stronger veteran adventurers of chapter 4 are boss fights: they go to the Bosses tab in their own groups.
    /// </summary>
    private static CodexEntry NamedEntry(string id, string name, CodexCategory category, int index)
    {
        string key = id.Substring("named:".Length);
        bool veteran = key.StartsWith("Veteran");
        bool adventurer = veteran || key.StartsWith("Adventurer") || key.StartsWith("Supporting");
        return new CodexEntry
        {
            Id = id, Key = key, Name = name,
            Category = adventurer ? CodexCategory.Bosses : category,
            Group = veteran ? Loc.N("Veteran adventurers") : adventurer ? Loc.N("Adventurers")
                : category == CodexCategory.Bosses ? Loc.N("Boss (Dark Mirror)") : Loc.N("Other"),
            Order = (veteran ? 80000 : adventurer ? 79000 : 90000) + index,
        };
    }

    /// <summary>
    /// Entries shown together on one page (identical movesets): member id → page id. The Leiana sisters are one
    /// page ("Leiana sisters"); Short Hair and Long Hair stay switchable on it.
    /// </summary>
    public static readonly Dictionary<string, string> TwinOf = new() { ["enemy:LeianaLongHair"] = "enemy:LeianaShortHair" };

    /// <summary>The name of a page that shows several entries.</summary>
    private static readonly Dictionary<string, string> PageNames = new() { ["enemy:LeianaShortHair"] = Loc.N("Leiana sisters") };

    static CodexCatalog() => Loc.Changed += () => _entries = null; // names come from the game's texts: list them again

    /// <summary>The gear groups (rarities) as shown; listed here so they get translated.</summary>
    private static readonly string[] RarityNames = { Loc.N("Common"), Loc.N("Rare"), Loc.N("Unique"), Loc.N("Legendary") };

    /// <summary>A page's members: the page's own entry first, then the entries shown on it.</summary>
    public static List<CodexEntry> MembersOf(CodexEntry page) =>
        new[] { page }.Concat(Entries.Where(e => TwinOf.TryGetValue(e.Id, out var p) && p == page.Id)).ToList();

    /// <summary>The same adventurer's other version: "VeteranMagician" for "AdventurerMagician" and back.</summary>
    public static CodexEntry OtherVersion(CodexEntry entry)
    {
        string key = entry.Key;
        string other = key.StartsWith("Veteran") ? "Adventurer" + key.Substring("Veteran".Length)
            : key.StartsWith("Adventurer") ? "Veteran" + key.Substring("Adventurer".Length) : null;
        return other == null ? null : Entries.FirstOrDefault(e => e.Key == other);
    }

    public static Characters.Gear.Upgrades.UpgradeResource.Reference DarkOf(CodexEntry entry) =>
        Dark.TryGetValue(entry.Id, out var reference) ? reference : null;

    /// <summary>The game's reference for a skull/item/essence entry (icon, rarity, name key), or null.</summary>
    public static GearReference GearOf(CodexEntry entry) =>
        Gear.TryGetValue(entry.Id, out var reference) ? reference : null;

    private static List<CodexEntry> Build()
    {
        var list = new List<CodexEntry>();

        foreach (Characters.Key key in Enum.GetValues(typeof(Characters.Key)))
        {
            var group = CodexGroups.ForEnemy((int)key);
            if (group == null)
                continue;
            list.Add(new CodexEntry
            {
                Id = EnemyId(key), Category = group.Value.category, Group = group.Value.group,
                Order = (int)key, Key = key.ToString(), Name = EnemyName(key),
            });
        }

        var gear = GearResource.instance;
        if (gear != null)
        {
            AddGear(list, gear.weapons, CodexCategory.Skulls);
            AddGear(list, gear.items, CodexCategory.Items);
            AddGear(list, gear.essences, CodexCategory.Essences);
        }

        int order = 0;
        foreach (var key in Inscription.keys)
        {
            if (key == Inscription.Key.None)
                continue;
            string name = Localize($"synergy/key/{key}/name");
            if (name.Length == 0)
                continue;
            list.Add(new CodexEntry
            {
                Id = InscriptionId(key), Category = CodexCategory.Inscriptions, Group = Loc.N("Inscription"),
                Order = order++, Key = key.ToString(), Name = name,
            });
        }

        try
        {
            var upgrades = Characters.Gear.Upgrades.UpgradeResource.instance?.upgradeReferences;
            if (upgrades != null)
            {
                foreach (var reference in upgrades)
                {
                    if (reference == null || string.IsNullOrEmpty(reference.name))
                        continue;
                    string name = Localize(reference.displayNameKey);
                    if (name.Length == 0 || Dark.ContainsKey(DarkId(reference.name)))
                        continue;
                    Dark[DarkId(reference.name)] = reference;
                    list.Add(new CodexEntry
                    {
                        Id = DarkId(reference.name), Category = CodexCategory.DarkAbilities,
                        Group = reference.type == Characters.Gear.Upgrades.UpgradeObject.Type.Cursed ? Loc.N("Cursed") : Loc.N("Dark ability"),
                        Order = reference.orderInShop * 10 + list.Count % 10, Key = reference.name, Name = name,
                    });
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: dark abilities could not be listed: {e.Message}");
        }

        // Enemies without a game key that were met before (their names are stored with the progress).
        foreach (var pair in CodexTracker.Progress.Entries)
            if (pair.Key.StartsWith("named:") && pair.Value.Name.Length > 0)
                list.Add(NamedEntry(pair.Key, pair.Value.Name,
                    pair.Value.Category == "Bosses" ? CodexCategory.Bosses : CodexCategory.Enemies, list.Count));

        foreach (var entry in list)
            if (PageNames.TryGetValue(entry.Id, out var pageName))
                entry.Name = Loc.T(pageName);

        Plugin.Log.LogInfo($"Codex catalog: {list.Count} entries " +
                           string.Join(", ", list.GroupBy(e => e.Category).Select(g => $"{g.Key} {g.Count()}")));
        return list;
    }

    private static void AddGear<T>(List<CodexEntry> list, IEnumerable<T> references, CodexCategory category) where T : GearReference
    {
        if (references == null)
            return;
        foreach (var reference in references)
        {
            if (reference == null || string.IsNullOrEmpty(reference.name))
                continue;
            string name = Localize(reference.displayNameKey);
            if (name.Length == 0)
                continue; // test and unused gear without a translation
            string id = GearId(reference.type, reference.name);
            if (Gear.ContainsKey(id))
                continue;
            Gear[id] = reference;
            list.Add(new CodexEntry
            {
                Id = id, Category = category, Group = reference.rarity.ToString(),
                Order = (int)reference.rarity * 10000 + list.Count, Key = reference.name, Name = name,
            });
        }
    }

    public static string EnemyName(Characters.Key key)
    {
        string name = Localize($"enemy/name/{key}");
        return name.Length > 0 ? name : Recording.OwnerNames.Humanize(key.ToString());
    }

    public static string Localize(string key) =>
        !string.IsNullOrEmpty(key) && Localization.TryGetLocalizedString(key, out var text) && !string.IsNullOrEmpty(text) ? text : "";

    /// <summary>The description texts of a gear entry (from its name key: .../name → .../desc, .../flavor).</summary>
    public static (string description, string flavor) GearTexts(CodexEntry entry)
    {
        var reference = GearOf(entry);
        if (reference == null || string.IsNullOrEmpty(reference.displayNameKey) || !reference.displayNameKey.EndsWith("/name"))
            return ("", "");
        string keyBase = reference.displayNameKey.Substring(0, reference.displayNameKey.Length - "/name".Length);
        return (Localize(keyBase + "/desc"), Localize(keyBase + "/flavor"));
    }

    public static Sprite DarkIcon(CodexEntry entry)
    {
        var reference = DarkOf(entry);
        return reference == null ? null : reference.icon != null ? reference.icon : reference.thumbnail;
    }

    public static string DarkDescription(CodexEntry entry)
    {
        var reference = DarkOf(entry);
        if (reference == null)
            return "";
        try
        {
            string description = reference.GetDescription(Math.Max(1, reference.maxLevel));
            return string.IsNullOrEmpty(description) ? Localize(reference.displayNameKey.Replace("/name", "/desc")) : description;
        }
        catch (Exception)
        {
            return Localize(reference.displayNameKey.Replace("/name", "/desc"));
        }
    }

    public static Sprite GearIcon(CodexEntry entry)
    {
        var reference = GearOf(entry);
        return reference == null ? null : reference.thumbnail != null ? reference.thumbnail : reference.icon;
    }
}
