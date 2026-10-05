using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameResources;
using Characters.Gear;
using Characters.Gear.Items;
using Characters.Gear.Quintessences;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Weapons;
using DamageInsight.Tools;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.AddressableAssets;
using DamageInsight.Lang;

namespace DamageInsight.Describe;

/// <summary>
/// In-game side of the descriptions: turns a live gear into the same JSON the gear scan produces,
/// analyzes it once (cached per gear), and formats the lines with the player's current stats.
/// </summary>
public static class GearDescriptions
{
    private static readonly Dictionary<string, Breakdown> Cache = new();

    static GearDescriptions()
    {
        // Analyses hold translated labels and notes: a new language analyses again.
        Loc.Changed += () =>
        {
            Cache.Clear();
            Summons.Clear();
            Transforms.Clear();
        };
    }

    /// <summary>Extra text for a skull's passive description: basic combo, jump/dash attack and passive hits.</summary>
    public static string ForWeapon(Weapon weapon) =>
        Build(weapon, "weapons", b => Join(
            Part(b, "Basic", Loc.T("Basic attack")),
            Part(b, "Jump", Loc.T("Jump attack")),
            Part(b, "Dash", Loc.T("Dash attack")),
            Part(b, "Passive", Loc.T("Passive")),
            SummonParts(b),
            TransformParts(b)));

    public static string ForSwap(Weapon weapon) => Build(weapon, "weapons", b => Part(b, "Swap", null));

    public static string ForSkill(SkillInfo skill)
    {
        var weapon = skill.GetComponentsInParent<Weapon>(true).FirstOrDefault();
        return weapon == null ? "" : Build(weapon, "weapons", b => Part(b, "Skill", null, skill.key));
    }

    public static string ForItem(Item item) =>
        PreviewHeader(item) +
        Build(item, "items", b => Join(new[] { Part(b, "Effect", null) }.Concat(UpgradeParts(b)).Append(SummonParts(b)).ToArray()));

    /// <summary>"If picked up: Arms 1→2 · Phys. atk 161% → 319.2%" on items on the ground.</summary>
    private static string PreviewHeader(Item item)
    {
        if (!Plugin.DescriptionNumbers.Value || !Plugin.PickupPreview.Value || PickupPreview.Current != item || item == null)
            return "";
        try
        {
            var changes = PickupPreview.Preview().changes;
            return changes.Count == 0 ? ""
                : $"\n<color={DescriptionFormatter.DimColor}>{Loc.F("{0}: {1}", PickupPreview.Replacing != null ? Loc.T("If swapped") : Loc.T("If picked up"), string.Join(" · ", changes))}</color>";
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Pickup preview for {item.name} failed: {e}");
            return "";
        }
    }

    /// <summary>For items that turn into another item: what they become, and that item's damage.</summary>
    private static IEnumerable<string> UpgradeParts(Breakdown b)
    {
        foreach (var upgrade in b.Upgrades)
        {
            string header = upgrade.OwnItemName.Length > 0
                ? Loc.F("Becomes {0} when you own {1}:", ItemName(upgrade.TargetName), ItemName(upgrade.OwnItemName))
                : Loc.F("Becomes {0}:", ItemName(upgrade.TargetName));
            yield return $"<color={DescriptionFormatter.DimColor}>{header}</color>";

            var target = AnalyzeItemByName(upgrade.TargetName);
            if (target != null)
                yield return Part(target, "Effect", null);
        }
    }

    private static string ItemName(string prefabName) =>
        Localization.TryGetLocalizedString($"item/{prefabName}/name", out var name) && name.Length > 0 ? name : prefabName;

    /// <summary>Loads an item prefab by name (from the game's gear list), analyzes it once and caches it.</summary>
    private static Breakdown AnalyzeItemByName(string name)
    {
        string cacheKey = "items/" + name;
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        Breakdown breakdown = null;
        var reference = GearResource.instance?.items?.FirstOrDefault(r => r.name == name);
        if (reference != null)
        {
            var request = reference.LoadAsync();
            try
            {
                request.WaitForCompletion();
                var go = request.handle.Result;
                if (go != null)
                {
                    var writer = new ObjectGraphWriter();
                    writer.WriteRoot(go, new Dictionary<string, string> { ["category"] = "items", ["name"] = name });
                    breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
                }
            }
            finally
            {
                request.Release();
            }
        }
        Cache[cacheKey] = breakdown;
        return breakdown;
    }

    public static string ForQuintessence(Quintessence essence) =>
        Build(essence, "essences", b => Join(Part(b, "Active", null), SummonParts(b)));

    /// <summary>
    /// Extra lines for one step of an inscription: status numbers (Poisoning, Arson...) on step 1,
    /// plus the damage of every part that this step unlocks (e.g. Brawl's shockwave).
    /// </summary>
    public static string ForInscription(Inscription inscription, int step)
    {
        if (!Plugin.DescriptionNumbers.Value || inscription == null)
            return "";
        string key = inscription.key.ToString();
        try
        {
            var stats = CurrentStats();
            var lines = new List<string>();
            if (step == 1)
            {
                string status = StatusDescriptions.StatusOfInscription(key);
                if (status.Length > 0)
                    lines.Add(StatusDescriptions.Describe(StatusSettings(), status, stats));
            }
            lines.AddRange(InscriptionParts(inscription, step, stats));
            if (inscription.key == Inscription.Key.FairyTale && inscription.steps != null && step == inscription.steps.Count - 1)
                lines.AddRange(OberonParts(inscription, super: false, stats));
            lines.RemoveAll(l => string.IsNullOrEmpty(l));
            return lines.Count > 0 ? "\n" + string.Join("\n", lines) : "";
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Inscription numbers for {key} failed: {e.Message}");
            return "";
        }
    }

    /// <summary>Damage lines for an inscription's "true form" (super) text.</summary>
    public static string ForInscriptionSuper(Inscription inscription)
    {
        if (!Plugin.DescriptionNumbers.Value || inscription == null)
            return "";
        try
        {
            var stats = CurrentStats();
            var lines = InscriptionParts(inscription, Section.SuperStep, stats).ToList();
            if (inscription.key == Inscription.Key.FairyTale)
                lines.AddRange(OberonParts(inscription, super: true, stats));
            lines.AddRange(TunedStatusLines(inscription, stats));
            return lines.Count > 0 ? "\n" + string.Join("\n", lines) : "";
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Inscription super numbers for {inscription.key} failed: {e.Message}");
            return "";
        }
    }

    /// <summary>
    /// Tuned texts that only say "further" or "more damage": Poisoning's tuned tick interval (it replaces the
    /// step bonus, SimpleStatBonusKeyword.UpdateStat) and Excessive Bleeding's severe bleed damage.
    /// </summary>
    private static IEnumerable<string> TunedStatusLines(Inscription inscription, StatSnapshot stats)
    {
        string dim = DescriptionFormatter.DimColor;
        if (inscription.key == Inscription.Key.Poisoning)
        {
            var values = InscriptionValue(inscription, (Poisoning p) => (byStep: p._statBonusByStep, super: p._superAbilityStatBonusValue));
            if (values == null)
                yield break;
            // The player's reduction minus what Poisoning gives right now = what other gear gives.
            var (bonusByStep, superBonus) = values.Value;
            int step = inscription.step;
            double current = inscription.isSuper ? superBonus
                : bonusByStep != null && step >= 0 && step < bonusByStep.Length ? bonusByStep[step] : 0;
            var tuned = stats.Copy();
            tuned.PoisonTickReduction = stats.PoisonTickReduction - current + superBonus;
            yield return $"<color={dim}>{Loc.T("Tuned:")}</color> " + StatusDescriptions.Describe(StatusSettings(), "Poison", tuned);
        }
        else if (inscription.key == Inscription.Key.ExcessiveBleeding)
        {
            double? chance = InscriptionValue(inscription, (ExcessiveBleeding e) => (double)e._superBleedChance);
            string severe = StatusDescriptions.SuperBleed(StatusSettings(), stats, severe: true, chance);
            if (severe.Length > 0)
                yield return severe;
        }
    }

    private static readonly Dictionary<(Inscription.Key, Type), object> InscriptionValues = new();

    /// <summary>A plain stat inscription's per-step numbers and stat (from its prefab), or null for other kinds.</summary>
    public static (double[] byStep, string category, int categoryIndex, int kindIndex)? SimpleStatBonus(Inscription inscription) =>
        InscriptionValue(inscription, (SimpleStatBonusKeyword k) => (k.statBonusByStep, k.statCategory.ToString(), k.statCategory.index, k.statKind.index));

    /// <summary>
    /// Values read from the inscription's own component in its prefab (numbers the game doesn't expose elsewhere).
    /// They are copied out while the prefab is loaded, because it is released right after.
    /// </summary>
    private static TValue? InscriptionValue<T, TValue>(Inscription inscription, Func<T, TValue> read)
        where T : Component where TValue : struct
    {
        if (!InscriptionValues.TryGetValue((inscription.key, typeof(TValue)), out var value))
        {
            TValue? found = null;
            WithPrefab(inscription.settings?.reference?.RuntimeKey, go =>
            {
                var component = go.GetComponentInChildren<T>(true);
                if (component != null)
                    found = read(component);
            });
            InscriptionValues[(inscription.key, typeof(TValue))] = value = found;
        }
        return value as TValue?;
    }

    private static IEnumerable<string> InscriptionParts(Inscription inscription, int step, StatSnapshot stats)
    {
        var b = AnalyzeInscription(inscription);
        if (b == null)
            yield break;
        foreach (var section in b.Sections.Where(s => s.Kind == "Inscription" && s.Step == step && (s.HasHits || s.Notes.Count > 0)))
            yield return DescriptionFormatter.Section(section, stats, section.Title.Length > 0 ? section.Title : null);
    }

    /// <summary>
    /// Fairy Tale's max step summons Oberon (Dark Oberon when the inscription is an Omen; stronger versions in the
    /// true form). Oberon is a separate prefab behind an AssetReference, so load and analyze it like a gear scan.
    /// </summary>
    private static IEnumerable<string> OberonParts(Inscription inscription, bool super, StatSnapshot stats)
    {
        bool dark = inscription.omen;
        string cacheKey = $"oberon/{(dark ? "dark" : "light")}/{(super ? "super" : "normal")}";
        if (!Cache.TryGetValue(cacheKey, out var b))
        {
            b = null;
            WithPrefab(inscription.settings?.reference?.RuntimeKey, inscriptionPrefab =>
            {
                var fairyTale = inscriptionPrefab.GetComponentInChildren<FairyTale>(true);
                if (fairyTale == null)
                    return;
                var reference = super
                    ? (dark ? fairyTale._superDarkOberonReference : fairyTale._superLightOberonReference)
                    : (dark ? fairyTale._darkOberonReference : fairyTale._oberonReference);
                WithPrefab(reference?.RuntimeKey, oberon =>
                {
                    var writer = new ObjectGraphWriter();
                    writer.WriteRoot(oberon, new Dictionary<string, string> { ["category"] = "linked", ["name"] = oberon.name });
                    b = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
                    // Characters.Gear.Synergy.Inscriptions.FairyTaleSummon.Oberon.ModifyDamage: in the true form,
                    // every damage you deal gets +x to its multiplier per Spirit item.
                    var component = oberon.GetComponentInChildren<Characters.Gear.Synergy.Inscriptions.FairyTaleSummon.Oberon>(true);
                    if (super && component != null && component._damageMultiplierBySuper > 0 && b.Sections.Count > 0)
                        b.Sections[b.Sections.Count - 1].Notes.Add(Loc.F(
                            "All your damage: +{0}% atk per Spirit item you hold (added to your atk %)",
                            (component._damageMultiplierBySuper * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
                });
            });
            Cache[cacheKey] = b;
        }
        if (b == null)
            yield break;
        yield return $"<color={DescriptionFormatter.DimColor}>{Loc.F("{0}:", dark ? Loc.T("Dark Oberon") : Loc.T("Oberon"))}</color>";
        foreach (var section in b.Sections.Where(s => s.HasHits))
            yield return DescriptionFormatter.Section(section, stats, section.Title);
    }

    /// <summary>Loads an Addressables prefab by its runtime key, hands it to <paramref name="use"/>, then releases it.</summary>
    private static void WithPrefab(object runtimeKey, Action<GameObject> use)
    {
        if (runtimeKey == null)
            return;
        var handle = Addressables.LoadAssetAsync<GameObject>(runtimeKey);
        try
        {
            var go = handle.WaitForCompletion();
            if (go != null)
                use(go);
        }
        finally
        {
            try
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"Releasing {runtimeKey} failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// The game only instantiates an inscription's data object while it is active, so load its prefab
    /// (settings.reference) once, serialize it like the gear scan does, and cache the analysis.
    /// </summary>
    private static Breakdown AnalyzeInscription(Inscription inscription)
    {
        string cacheKey = "inscriptions/" + inscription.key;
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        Breakdown breakdown = null;
        var reference = inscription.settings?.reference;
        if (reference != null && reference.RuntimeKeyIsValid())
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
            try
            {
                var go = handle.WaitForCompletion();
                if (go != null)
                {
                    var writer = new ObjectGraphWriter();
                    writer.WriteRoot(go, new Dictionary<string, string> { ["category"] = "inscriptions", ["name"] = inscription.key.ToString() });
                    breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
                }
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }
        Cache[cacheKey] = breakdown;
        return breakdown;
    }

    private static Node? _statusSettings;

    /// <summary>The game's status settings, serialized once like a gear scan.</summary>
    private static Node StatusSettings()
    {
        if (_statusSettings == null)
        {
            var writer = new ObjectGraphWriter();
            writer.WriteValue(CharacterStatusSetting.instance);
            _statusSettings = GearDoc.ParseNode(writer.Result);
        }
        return _statusSettings.Value;
    }

    private static string Build(Gear gear, string category, Func<Breakdown, string> format)
    {
        if (!Plugin.DescriptionNumbers.Value || gear == null)
            return "";
        try
        {
            var breakdown = Analyze(gear, category);
            if (breakdown == null)
                return "";
            DescriptionFormatter.Short = Plugin.DescriptionsShort?.Value ?? false;
            string text = format(breakdown);
            return text.Length > 0 ? "\n\n" + text : "";
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Description numbers for {gear.name} failed: {e.Message}");
            return "";
        }
    }

    private static string Part(Breakdown b, string kind, string title, string key = null)
    {
        var stats = CurrentStats();
        var sections = b.Sections.Where(s => s.Kind == kind && (key == null || s.Key == key) && s.HasHits).ToList();
        if (sections.Count == 0)
            return "";
        // Several actions of one kind (e.g. ground and air combos) are listed one after another.
        return string.Join("\n", sections.Select((s, i) =>
            DescriptionFormatter.Section(s, stats, title == null ? null
                : sections.Count > 1 ? (s.Title.Length > 0 ? Loc.F("{0}: {1}", title, s.Title) : $"{title} ({i + 1})")
                : title)));
    }

    private static string Join(params string[] parts) => string.Join("\n", parts.Where(p => p.Length > 0));

    private static Breakdown Analyze(Gear gear, string category)
    {
        string key = category + "/" + gear.name.Replace("(Clone)", "").Trim();
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var writer = new ObjectGraphWriter();
        writer.WriteRoot(gear.gameObject, new Dictionary<string, string> { ["category"] = category, ["name"] = gear.name });
        var breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
        Cache[key] = breakdown;

        // Characters this gear summons (turrets, companions, buddies) are analyzed like gear too.
        var summons = new List<(string name, Breakdown breakdown)>();
        foreach (var character in writer.ReferencedCharacters.Where(c => c != null))
        {
            var summon = AnalyzeCharacter(character);
            if (summon != null && summon.Sections.Any(sec => sec.HasHits) && summons.All(x => x.name != SummonName(character)))
                summons.Add((SummonName(character), summon));
        }
        Summons[breakdown] = summons;

        // A skill or swap that turns the skull into another body (Nightmare's Hell Bike, Devil Berserker's devil
        // form: StartWeaponPolymorph): that body's attacks, analyzed like a skull (docs/DAMAGE_SCAN.md).
        var transforms = new List<(bool swap, Breakdown breakdown)>();
        if (category == "weapons")
            foreach (var weapon in writer.ReferencedWeapons.Where(w => w != null && w.gameObject.name.Contains("Polymorph")))
            {
                var body = AnalyzeTransformed(weapon);
                if (body != null && body.Sections.Any(sec => sec.HasHits && sec.Kind != "Passive"))
                    transforms.Add((weapon.gameObject.name.Contains("Swap"), body));
            }
        Transforms[breakdown] = transforms;
        return breakdown;
    }

    private static readonly Dictionary<Breakdown, List<(bool swap, Breakdown breakdown)>> Transforms = new();

    private static Breakdown AnalyzeTransformed(Weapon weapon)
    {
        string key = "weapons/" + weapon.gameObject.name.Replace("(Clone)", "").Trim();
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var writer = new ObjectGraphWriter();
        writer.WriteRoot(weapon.gameObject, new Dictionary<string, string> { ["category"] = "weapons", ["name"] = weapon.gameObject.name });
        var breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
        Cache[key] = breakdown;
        return breakdown;
    }

    /// <summary>"Transformed by a skill / the swap:" blocks with the transformed body's attacks.</summary>
    private static string TransformParts(Breakdown b)
    {
        if (!Transforms.TryGetValue(b, out var bodies) || bodies.Count == 0)
            return "";
        var parts = new List<string>();
        foreach (var (swap, body) in bodies)
        {
            parts.Add($"<color={DescriptionFormatter.DimColor}>{(swap ? Loc.T("Transformed by the swap:") : Loc.T("Transformed by a skill:"))}</color>");
            parts.Add(Part(body, "Basic", Loc.T("Basic attack")));
            parts.Add(Part(body, "Jump", Loc.T("Jump attack")));
            parts.Add(Part(body, "Dash", Loc.T("Dash attack")));
            foreach (var skill in body.Sections.Where(sec => sec.Kind == "Skill" && sec.HasHits))
                parts.Add(Part(body, "Skill", Loc.Name(WeaponRefiner.Humanize(skill.Key)), skill.Key));
        }
        return Join(parts.ToArray());
    }

    private static readonly Dictionary<Breakdown, List<(string name, Breakdown breakdown)>> Summons = new();

    private static Breakdown AnalyzeCharacter(Character character)
    {
        string key = "characters/" + character.gameObject.name.Replace("(Clone)", "").Trim();
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var writer = new ObjectGraphWriter();
        writer.WriteRoot(character.gameObject, new Dictionary<string, string> { ["category"] = "characters", ["name"] = character.gameObject.name });
        var breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
        Cache[key] = breakdown;
        return breakdown;
    }

    private static string SummonName(Character character)
    {
        if (Localization.TryGetLocalizedString($"enemy/name/{character.key}", out var name) && name.Length > 0)
            return name;
        return character.gameObject.name.Replace("(Clone)", "").Replace("_", " ").Trim();
    }

    /// <summary>"Summons X:" blocks with each summon's attacks.</summary>
    private static string SummonParts(Breakdown b)
    {
        if (!Summons.TryGetValue(b, out var summons) || summons.Count == 0)
            return "";
        var stats = CurrentStats();
        var parts = new List<string>();
        foreach (var (name, summon) in summons)
        {
            parts.Add($"<color={DescriptionFormatter.DimColor}>{Loc.F("Summons {0}:", name)}</color>");
            foreach (var section in summon.Sections.Where(sec => sec.HasHits))
                parts.Add(DescriptionFormatter.Section(section, stats, Loc.Name(WeaponRefiner.Humanize(section.Key))));
        }
        return Join(parts.ToArray());
    }

    /// <summary>The player's current damage stats, and the equipped skull's base damage.</summary>
    /// <summary>
    /// The player's stats for descriptions; while the popup of an item on the ground is being built, the stats
    /// the player would have after picking it up (PickupPreview).
    /// </summary>
    public static StatSnapshot CurrentStats(bool ignorePreview = false)
    {
        if (!ignorePreview && PickupPreview.Current != null && Plugin.PickupPreview.Value)
            return PickupPreview.Preview().stats;
        var player = Singleton<Service>.Instance?.levelManager?.player;
        if (player == null)
            return StatSnapshot.Neutral;
        return Snapshot(player.stat, player);
    }

    public static StatSnapshot Snapshot(Stat stat, Character player)
    {
        var snapshot = new StatSnapshot
        {
            Physical = stat.GetFinal(Stat.Kind.PhysicalAttackDamage),
            Magic = stat.GetFinal(Stat.Kind.MagicAttackDamage),
            Projectile = stat.GetFinal(Stat.Kind.ProjectileAttackDamage),
            Basic = stat.GetFinal(Stat.Kind.BasicAttackDamage),
            Skill = stat.GetFinal(Stat.Kind.SkillAttackDamage),
            AttackDamage = stat.GetFinal(Stat.Kind.AttackDamage),
            PoisonTickReduction = stat.GetFinal(Stat.Kind.PoisonTickFrequency),
            EmberDamage = stat.GetFinal(Stat.Kind.EmberDamage),
            BleedDamage = stat.GetFinal(Stat.Kind.BleedDamage),
            CritDamage = stat.GetFinal(Stat.Kind.CriticalDamage),
            BleedCanCrit = player.status != null && player.status.canBleedCritical,
            FreezeBonus = stat.GetFinal(Stat.Kind.FreezeDuration),
            StunBonus = stat.GetFinal(Stat.Kind.StunDuration),
        };
        var skull = player.playerComponents?.inventory?.weapon?.current;
        var damage = skull != null ? skull.GetComponent<AttackDamage>() : null;
        if (damage != null)
        {
            snapshot.SkullMin = damage.minAttackDamage;
            snapshot.SkullMax = damage.maxAttackDamage;
        }
        return snapshot;
    }
}
