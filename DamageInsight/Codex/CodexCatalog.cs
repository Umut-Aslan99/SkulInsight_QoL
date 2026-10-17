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
    /// <summary>Our own page names (none at the moment: the bosses' names come from the game, BossNames).</summary>
    private static readonly Dictionary<string, string> PageNames = new();

    /// <summary>
    /// The bosses' own names and titles: the texts of their health bars and title cards ("label/healthbar/&lt;boss&gt;/
    /// name" and ".../title", read from the game's string table on 2026-10-17 with tools/unity/boss_names.py and
    /// loctable.py). The game has no "enemy/name/..." text for bosses, so without these their pages carried code
    /// names ("Alexander1", "First hero1"). On the first phase's page; the run pages name a boss by it.
    /// The hidden boss's name is "Skul" (the player's own name) and its title "Dark Skeleton": the title names it.
    /// </summary>
    private static readonly Dictionary<string, (string name, string title)> BossNames = new()
    {
        ["enemy:Yggdrasil"] = ("label/healthbar/chapter1/name", "label/healthbar/chapter1/title"),
        ["enemy:LeianaShortHair"] = ("label/healthbar/chapter2/name", "label/healthbar/chapter2/title"),
        ["enemy:Chimera"] = ("label/healthbar/chapter3/name", "label/healthbar/chapter3/title"),
        ["enemy:Pope"] = ("label/healthbar/chapter4/name", "label/healthbar/chapter4/title"),
        ["enemy:FirstHero1"] = ("label/healthbar/chapter5/name", "label/healthbar/chapter5/title"),
        ["enemy:DarkSkul1"] = ("label/healthbar/darkSkeleton/title", "label/healthbar/darkSkeleton/name"),
        ["enemy:Alexander1"] = ("label/healthbar/chapter6/name", "label/healthbar/chapter6/title"),
    };

    /// <summary>
    /// The other phases of a boss fight (each has its own page): named after the boss's first page, "First Hero · dark
    /// form", "Alexander · phase 4" (Umut 2026-10-17: "First Hero" stood twice in the list, the rest were code names).
    /// </summary>
    private static readonly Dictionary<string, (string boss, Func<string, string> name)> BossPhases = new()
    {
        ["enemy:FirstHero2"] = ("enemy:FirstHero1", n => Loc.F("{0} · phase {1}", n, 2)),
        ["enemy:FirstHero3"] = ("enemy:FirstHero1", n => Loc.F("{0} · phase {1}", n, 3)),
        ["enemy:FirstDarkHero"] = ("enemy:FirstHero1", n => Loc.F("{0} · dark form", n)),
        ["enemy:AwakenLeiana"] = ("enemy:LeianaShortHair", n => Loc.F("{0} · awakened", n)),
        ["enemy:DarkSkul2"] = ("enemy:DarkSkul1", n => Loc.F("{0} · phase {1}", n, 2)),
        ["enemy:Alexander2_Heart"] = ("enemy:Alexander1", n => Loc.F("{0} · phase {1}", n, 2)),
        ["enemy:Alexander2_Left"] = ("enemy:Alexander1", n => Loc.F("{0} · left hand", n)),
        ["enemy:Alexander2_Right"] = ("enemy:Alexander1", n => Loc.F("{0} · right hand", n)),
        ["enemy:Alexander3"] = ("enemy:Alexander1", n => Loc.F("{0} · phase {1}", n, 3)),
        ["enemy:Alexander4"] = ("enemy:Alexander1", n => Loc.F("{0} · phase {1}", n, 4)),
    };

    /// <summary>A boss's title in the current language ("Emperor of Carleon"), or "".</summary>
    public static string BossTitle(string id) => BossNames.TryGetValue(id ?? "", out var keys) ? Localize(keys.title) : "";

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

        CodexElites.AddTo(list);

        // Enemies without a game key that were met before (their names are stored with the progress).
        foreach (var pair in CodexTracker.Progress.Entries)
            if (pair.Key.StartsWith("named:") && pair.Value.Name.Length > 0)
                list.Add(NamedEntry(pair.Key, pair.Value.Name,
                    pair.Value.Category == "Bosses" ? CodexCategory.Bosses : CodexCategory.Enemies, list.Count));

        foreach (var entry in list)
        {
            if (BossNames.TryGetValue(entry.Id, out var keys) && Localize(keys.name) is { Length: > 0 } bossName)
                entry.Name = bossName;
            if (PageNames.TryGetValue(entry.Id, out var pageName))
                entry.Name = Loc.T(pageName);
        }
        foreach (var entry in list)
            if (BossPhases.TryGetValue(entry.Id, out var phase) && list.FirstOrDefault(e => e.Id == phase.boss) is { } boss &&
                boss.Name != OwnerNamesHumanized(boss.Key))
                entry.Name = phase.name(boss.Name);

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

    private static string OwnerNamesHumanized(string key) => Recording.OwnerNames.Humanize(key);

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

    /// <summary>A quintessence's active name and text ("&lt;key&gt;/active/name", ".../active/desc"), or empty.</summary>
    public static (string name, string description) ActiveTexts(CodexEntry entry)
    {
        var reference = GearOf(entry);
        if (reference == null || string.IsNullOrEmpty(reference.displayNameKey) || !reference.displayNameKey.EndsWith("/name"))
            return ("", "");
        string keyBase = reference.displayNameKey.Substring(0, reference.displayNameKey.Length - "/name".Length);
        return (Localize(keyBase + "/active/name"), Localize(keyBase + "/active/desc"));
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

    /// <summary>
    /// A fragment by a run's id ("fragment:RibsOfTerror", the dropped object's name): the Dark Mirror's fragments have
    /// no Codex page; their names and icons are in the game's FragmentResource (its icon dictionary stays empty).
    /// </summary>
    public static Characters.Gear.Fragments.FragmentResource.Reference FragmentOf(string id)
    {
        try
        {
            string name = id.StartsWith("fragment:", StringComparison.Ordinal) ? id.Substring("fragment:".Length) : id;
            var references = Characters.Gear.Fragments.FragmentResource.instance?.fragmentReferences;
            if (references == null || name.Length == 0)
                return null;
            return references.FirstOrDefault(f => string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase))
                   ?? references.FirstOrDefault(f => !string.IsNullOrEmpty(f.name) &&
                                                     (f.name.EndsWith(name, StringComparison.OrdinalIgnoreCase) ||
                                                      name.EndsWith(f.name, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Sprite FragmentIcon(string id) =>
        FragmentOf(id) is { } fragment ? fragment.icon != null ? fragment.icon : fragment.thumbnail : null;

    public static Sprite GearIcon(CodexEntry entry)
    {
        var reference = GearOf(entry);
        if (reference == null)
            return null;
        // Skulls: the inventory's head icon. Their thumbnail is the small sprite lying on the floor (Umut 2026-10-16: the
        // skull icons looked "small and weird" in lists, run pages and their own Arsenal page).
        if (entry.Category == CodexCategory.Skulls)
            return reference.icon != null ? reference.icon : reference.thumbnail;
        return reference.thumbnail != null ? reference.thumbnail : reference.icon;
    }
}
