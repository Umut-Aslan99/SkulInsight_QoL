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
        // A transformed body's skills by the game's own names (they show in the skill boxes while transformed).
        DescriptionLayout.SkillName = key =>
            Localization.TryGetLocalizedString($"skill/{key}/name", out var name) && name.Length > 0
                ? name
                : Loc.Name(WeaponRefiner.Humanize(key));
    }

    /// <summary>Extra text for a skull's passive description: basic combo, jump/dash attack and passive hits.</summary>
    public static string ForWeapon(Weapon weapon) =>
        Build(weapon, "weapons", b =>
        {
            string text = DescriptionLayout.Weapon(b, SummonsOf(b), BodiesOf(b), CurrentStats());
            return DescriptionLayout.Compact && text.Length > 0
                ? text + "\n" + DescriptionLayout.CodexFooter(Plugin.CodexKey.Value.MainKey.ToString())
                : text;
        });

    public static string ForSwap(Weapon weapon) => Build(weapon, "weapons", b => DescriptionLayout.Swap(b, CurrentStats()));

    public static string ForSkill(SkillInfo skill)
    {
        var weapon = skill.GetComponentsInParent<Weapon>(true).FirstOrDefault();
        return weapon == null ? "" : Build(weapon, "weapons", b => DescriptionLayout.Skill(b, skill.key, CurrentStats()));
    }

    public static string ForItem(Item item) =>
        PreviewHeader(item) +
        Build(item, "items", b => DescriptionLayout.Item(b, AnalyzeItemByName, ItemName, SummonsOf(b), CurrentStats()));

    /// <summary>"If picked up: Arms 1→2 · Phys. atk 161% → 319.2%" on items on the ground.</summary>
    private static string PreviewHeader(Item item)
    {
        if (!NumbersInBoxes || !Plugin.PickupPreview.Value || PickupPreview.Current != item || item == null)
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

    public static string ItemName(string prefabName) =>
        Localization.TryGetLocalizedString($"item/{prefabName}/name", out var name) && name.Length > 0 ? name : prefabName;

    /// <summary>A dark ability of the Dark Mirror shop, analyzed once (cached): its passive and per-level hits.</summary>
    public static Breakdown AnalyzeUpgrade(Characters.Gear.Upgrades.UpgradeResource.Reference reference)
    {
        if (reference == null)
            return null;
        string cacheKey = "upgrades/" + reference.name;
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;
        Breakdown breakdown = null;
        try
        {
            WithPrefab(reference.assetReference?.RuntimeKey, go =>
            {
                var writer = new ObjectGraphWriter();
                writer.WriteRoot(go, new Dictionary<string, string> { ["category"] = "upgrades", ["name"] = reference.name });
                breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not analyze dark ability {reference.name}: {e.Message}");
        }
        Cache[cacheKey] = breakdown;
        return breakdown;
    }

    /// <summary>Loads an item prefab by name (from the game's gear list), analyzes it once and caches it.</summary>
    public static Breakdown AnalyzeItemByName(string name)
    {
        var reference = GearResource.instance?.items?.FirstOrDefault(r => r.name == name);
        return reference == null ? null : AnalyzeReference(reference);
    }

    /// <summary>
    /// The analysis of any skull, item or quintessence, held or not (the Codex pages): its prefab is loaded through
    /// the game's gear list, serialized like a gear scan and analyzed once (cached, with its summons and bodies).
    /// </summary>
    public static Breakdown AnalyzeReference(GearReference reference)
    {
        if (reference == null)
            return null;
        string category = CategoryOf(reference.type);
        string cacheKey = category + "/" + reference.name;
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        Breakdown breakdown = null;
        var request = reference.LoadAsync();
        try
        {
            request.WaitForCompletion();
            var go = request.handle.Result;
            if (go != null)
                breakdown = Analyze(go.gameObject, category, reference.name);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not analyze {reference.name}: {e.Message}");
        }
        finally
        {
            request.Release();
        }
        Cache[cacheKey] = breakdown;
        return breakdown;
    }

    private static string CategoryOf(Gear.Type type) => type switch
    {
        Gear.Type.Weapon => "weapons",
        Gear.Type.Quintessence => "essences",
        _ => "items",
    };

    /// <summary>
    /// Loads a gear prefab through its reference and hands it to <paramref name="use"/> while it is loaded (the
    /// Codex reads skills, awakening and other fields the analysis doesn't keep).
    /// </summary>
    public static void WithGearPrefab(GearReference reference, Action<GameObject> use)
    {
        if (reference == null)
            return;
        var request = reference.LoadAsync();
        try
        {
            request.WaitForCompletion();
            var go = request.handle.Result;
            if (go != null)
                use(go.gameObject);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not load {reference.name}: {e.Message}");
        }
        finally
        {
            request.Release();
        }
    }

    public static string ForQuintessence(Quintessence essence) =>
        Build(essence, "essences", b => DescriptionLayout.Essence(b, SummonsOf(b), CurrentStats()));

    /// <summary>
    /// Extra lines for one step of an inscription: status numbers (Poisoning, Arson...) on step 1,
    /// plus the damage of every part that this step unlocks (e.g. Brawl's shockwave).
    /// </summary>
    public static string ForInscription(Inscription inscription, int step, StatSnapshot stats = null)
    {
        // Given stats: a Codex page, which always shows its numbers.
        if ((stats == null && !NumbersInBoxes) || inscription == null)
            return "";
        if (stats == null)
            DescriptionLayout.Compact = Plugin.DescriptionsCompact?.Value ?? true; // a game box
        string key = inscription.key.ToString();
        try
        {
            stats ??= CurrentStats();
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
    public static string ForInscriptionSuper(Inscription inscription, StatSnapshot stats = null)
    {
        if ((stats == null && !NumbersInBoxes) || inscription == null)
            return "";
        if (stats == null)
            DescriptionLayout.Compact = Plugin.DescriptionsCompact?.Value ?? true; // a game box
        try
        {
            stats ??= CurrentStats();
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

    private static IEnumerable<string> InscriptionParts(Inscription inscription, int step, StatSnapshot stats) =>
        DescriptionLayout.InscriptionParts(AnalyzeInscription(inscription), step, stats);

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

    /// <summary>The game's own boxes show the numbers: the setting is on and (hold to show) the key is held.</summary>
    public static bool NumbersInBoxes => Plugin.DescriptionNumbers.Value && ExtraInfo.Shown;

    /// <summary>Whether a skull, item or quintessence gets any numbers (for the "hold Shift" hint).</summary>
    public static bool HasNumbers(Gear gear)
    {
        string category = gear switch
        {
            Weapon => "weapons",
            Quintessence => "essences",
            Item => "items",
            _ => null,
        };
        if (category == null)
            return false;
        try
        {
            var b = Analyze(gear, category);
            return b != null && (b.Sections.Any(s => s.HasHits) || b.Upgrades.Count > 0 || SummonsOf(b).Count > 0 || BodiesOf(b).Count > 0);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether a skill gets numbers (for the "hold Shift" hint in the detail views).</summary>
    public static bool HasSkillNumbers(SkillInfo skill)
    {
        try
        {
            var weapon = skill.GetComponentsInParent<Weapon>(true).FirstOrDefault();
            var b = weapon == null ? null : Analyze(weapon, "weapons");
            return b != null && b.Sections.Any(s => s.Kind == "Skill" && s.Key == skill.key && s.HasHits);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool HasSwapNumbers(Weapon weapon)
    {
        try
        {
            var b = Analyze(weapon, "weapons");
            return b != null && b.Sections.Any(s => s.Kind == "Swap" && s.HasHits);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The first inscription step with numbers (the hint goes there once), or -1.</summary>
    public static int FirstNumberStep(Inscription inscription)
    {
        try
        {
            if (inscription == null)
                return -1;
            if (StatusDescriptions.StatusOfInscription(inscription.key.ToString()).Length > 0)
                return 1;
            var b = AnalyzeInscription(inscription);
            var steps = b?.Sections.Where(s => s.Kind == "Inscription" && s.Step > 0 && s.HasHits).Select(s => s.Step).ToList();
            return steps != null && steps.Count > 0 ? steps.Min() : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static string Build(Gear gear, string category, Func<Breakdown, string> format)
    {
        if (!NumbersInBoxes || gear == null)
            return "";
        try
        {
            var breakdown = Analyze(gear, category);
            if (breakdown == null)
                return "";
            DescriptionFormatter.Short = false; // "Short descriptions" is merged into Compact (2026-10-12)
            DescriptionLayout.Compact = Plugin.DescriptionsCompact?.Value ?? true;
            string text = format(breakdown);
            return text.Length > 0 ? "\n\n" + text : "";
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Description numbers for {gear.name} failed: {e.Message}");
            return "";
        }
    }

    public static string Part(Breakdown b, string kind, string title, string key = null, StatSnapshot stats = null) =>
        DescriptionLayout.Part(b, kind, title, key, stats ?? CurrentStats());

    private static Breakdown Analyze(Gear gear, string category) => Analyze(gear.gameObject, category, gear.name);

    private static Breakdown Analyze(GameObject gear, string category, string name)
    {
        string key = category + "/" + name.Replace("(Clone)", "").Trim();
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var writer = new ObjectGraphWriter();
        writer.WriteRoot(gear, new Dictionary<string, string> { ["category"] = category, ["name"] = name });
        var breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
        Cache[key] = breakdown;
        foreach (var upgrade in breakdown.Upgrades)
            KnownUpgrades.Add((name.Replace("(Clone)", "").Trim(), upgrade.TargetName));

        // Characters this gear summons (turrets, companions, buddies) are analyzed like gear too.
        var summons = new List<(string name, Breakdown breakdown)>();
        foreach (var character in writer.ReferencedCharacters.Where(c => c != null))
        {
            var summon = AnalyzeCharacter(character);
            string summonName = SummonName(character);
            // A summon prefab just called "Character" (the Archdemon's): the gear's own name says more.
            if (string.Equals(summonName, "Character", StringComparison.OrdinalIgnoreCase))
                summonName = OwnName(category, name);
            if (summon != null && summon.Sections.Any(sec => sec.HasHits) && summons.All(x => x.name != summonName))
                summons.Add((summonName, summon));
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

    /// <summary>Item transformations met in analyses so far: (item, the item it becomes).</summary>
    public static readonly HashSet<(string from, string target)> KnownUpgrades = new();

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
    public static string TransformParts(Breakdown b, StatSnapshot stats = null) =>
        DescriptionLayout.Transforms(BodiesOf(b), stats ?? CurrentStats());

    private static List<(bool swap, Breakdown breakdown)> BodiesOf(Breakdown b) =>
        Transforms.TryGetValue(b, out var bodies) ? bodies : new List<(bool, Breakdown)>();

    private static List<(string name, Breakdown breakdown)> SummonsOf(Breakdown b) =>
        Summons.TryGetValue(b, out var summons) ? summons : new List<(string, Breakdown)>();

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

    /// <summary>A gear's own display name from its category and prefab name.</summary>
    private static string OwnName(string category, string name)
    {
        string prefab = name.Replace("(Clone)", "").Trim();
        string prefix = category switch { "weapons" => "weapon", "essences" => "quintessence", _ => "item" };
        return Localization.TryGetLocalizedString($"{prefix}/{prefab}/name", out var own) && own.Length > 0 ? own : prefab;
    }

    private static string SummonName(Character character)
    {
        if (Localization.TryGetLocalizedString($"enemy/name/{character.key}", out var name) && name.Length > 0)
            return name;
        string prefab = character.gameObject.name.Replace("(Clone)", "").Trim();
        // A quintessence's own summon (Hydra, Imp, Raven Lord): the quintessence's name.
        if (Localization.TryGetLocalizedString($"quintessence/{prefab}/name", out var essence) && essence.Length > 0)
            return essence;
        string named = SummonNames.For(prefab);
        return named.Length > 0 ? named : prefab.Replace("_", " ");
    }

    /// <summary>"Summons X:" blocks with each summon's attacks.</summary>
    public static string SummonParts(Breakdown b, StatSnapshot stats = null) =>
        DescriptionLayout.Summons(SummonsOf(b), stats ?? CurrentStats());

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
