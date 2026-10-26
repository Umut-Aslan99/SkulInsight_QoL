using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Weapons;
using DamageInsight.Describe;
using DamageInsight.Lang;
using GameResources;
using Services;
using Singletons;

namespace DamageInsight.Codex;

/// <summary>
/// The text of the Arsenal pages (skulls, items, quintessences, inscriptions, dark abilities) and of the You page's
/// details: the game's own texts, the damage numbers from the same analysis as the in-game descriptions, and links
/// (TMP &lt;link&gt; tags with an entry id) to the pages they mention. Numbers use the stats handed in: neutral on the
/// Arsenal pages, the player's current ones on the You page.
/// </summary>
public static class CodexPages
{
    public const string LinkColor = "#9C3B2A";
    public const string DimInk = "#8A7058";

    // ------------------------------------------------------------------ building blocks

    /// <summary>A section of the page: a framed box with an icon (IconLibrary spec) and this title (PageBlocks).</summary>
    public static string Heading(string text, string icon = null) => PageBlocks.Section(text, icon);

    public static string Dim(string text) => $"<color={DimInk}>{text}</color>";

    /// <summary>A clickable name that opens an entry's page, or "???" while the entry is unknown.</summary>
    public static string Link(CodexEntry entry)
    {
        if (entry == null)
            return "???";
        bool known = CodexTiers.Tier(entry.Category, CodexTracker.Page(entry.Id), entry.Id) > 0;
        return known ? LinkTo(entry.Id, entry.Name) : Dim("???");
    }

    public static string LinkTo(string id, string text) => $"<link=\"{id}\"><color={LinkColor}><u>{text}</u></color></link>";

    public static string Link(string id) => Link(Entry(id));

    public static CodexEntry Entry(string id) => CodexCatalog.Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>The formatter's grey is made for the game's dark popups: on the book's parchment, the book's dim ink.</summary>
    public static string Ink(string text) => text.Replace(DescriptionFormatter.DimColor, DimInk);

    private static string Seconds(double s) => s.ToString(s >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shift is held on a Codex page: the numbers use your run's stats (what this gear would do in your build) instead of
    /// the neutral ones (Umut 2026-10-12). Set by the window every frame.
    /// </summary>
    public static bool RunStats;

    /// <summary>
    /// Neutral stats for a gear page (or, while Shift is held, your run's stats); a skull's own base damage fills the
    /// skull-damage hits on its page.
    /// </summary>
    public static StatSnapshot Neutral(Breakdown b = null)
    {
        var stats = RunStats ? GearDescriptions.CurrentStats(ignorePreview: true).Copy() : StatSnapshot.Neutral.Copy();
        if (b != null && b.Category == "weapons" && b.BaseMax > 0)
        {
            stats.SkullMin = b.BaseMin;
            stats.SkullMax = b.BaseMax;
        }
        return stats;
    }

    /// <summary>Which numbers a page shows: the You page's, your run's (Shift held) or the neutral ones.</summary>
    private static string StatsNote(bool given, bool current) => Dim(given
        ? Loc.T("With your current stats.")
        : current
            ? Loc.T("With your run's stats (Shift held). Release Shift for the neutral numbers.")
            : Loc.T("At neutral stats (100% attack, no items): what it does by itself.") + " " + Loc.T("Hold Shift: with your run's stats."));

    /// <summary>The numbers are formatted with the full calculation, whatever the in-game "short" setting says.</summary>
    private static string Full(Func<string> format)
    {
        bool shortBefore = DescriptionFormatter.Short, compactBefore = DescriptionLayout.Compact, fullBefore = DescriptionFormatter.FullFormula;
        DescriptionFormatter.Short = false;
        DescriptionLayout.Compact = false; // the Codex is the place for all the numbers
        DescriptionFormatter.FullFormula = true; // every factor, also 100 %
        try
        {
            return Ink(format());
        }
        finally
        {
            DescriptionFormatter.Short = shortBefore;
            DescriptionLayout.Compact = compactBefore;
            DescriptionFormatter.FullFormula = fullBefore;
        }
    }

    // ------------------------------------------------------------------ pages

    /// <summary>The page text of a skull, item, quintessence, inscription or dark ability (tier 2+).</summary>
    public static string Text(CodexEntry entry, EntryProgress p, int tier, StatSnapshot stats = null)
    {
        try
        {
            return entry.Category switch
            {
                CodexCategory.Skulls => Skull(entry, p, tier, stats),
                CodexCategory.Items => Item(entry, p, tier, stats),
                CodexCategory.Essences => Essence(entry, p, tier, stats),
                CodexCategory.Inscriptions => InscriptionPage(entry, p, tier, stats),
                CodexCategory.DarkAbilities => DarkAbility(entry, p, tier),
                _ => "",
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex page {entry.Id} failed: {e}");
            return CodexCatalog.GearTexts(entry).description;
        }
    }

    private static string Counters(EntryProgress p, CodexCategory category)
    {
        if (p == null)
            return "";
        string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
        return PageBlocks.Close() + (category == CodexCategory.Inscriptions
            ? "\n\n" + Dim($"{Loc.T("Collected in runs")}: {N(p.Seen)}  ·  {Loc.T("Completed (max step)")}: {N(p.PickedUp)}")
            : "\n\n" + Dim($"{Loc.T("Seen")}: {N(p.Seen)}  ·  {Loc.T("Taken")}: {N(p.PickedUp)}"));
    }

    /// <summary>0.15: the coins of a skull or item (Rewards), with Summon.</summary>
    private static string CoinText(CodexEntry entry)
    {
        try
        {
            bool omen = CodexCatalog.GearOf(entry) is GameResources.GearReference reference && (reference.gearTag & Characters.Gear.Gear.Tag.Omen) != 0;
            return DamageInsight.Rewards.RewardPages.GearCoinText(entry.Id, entry.Group == "Legendary", omen, DamageInsight.Rewards.RewardService.WhyNoSummon);
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string Flavor(string flavor, int tier) =>
        PageBlocks.Close() + (flavor.Length == 0 ? "" : tier >= 3 ? $"\n\n<i>{flavor}</i>" : "\n\n" + Dim(Loc.T("Master it to read its story.")));

    // ---- items

    private static string Item(CodexEntry entry, EntryProgress p, int tier, StatSnapshot stats)
    {
        var sb = new StringBuilder();
        var (description, flavor) = CodexCatalog.GearTexts(entry);
        sb.Append(description);

        var reference = CodexCatalog.GearOf(entry) as ItemReference;
        if (reference != null)
        {
            var keys = new[] { reference.prefabKeyword1, reference.prefabKeyword2 }.Where(k => k != Inscription.Key.None).Distinct().ToList();
            if (keys.Count > 0)
                sb.Append(Heading(Loc.T("Inscriptions"), "inscription:Artifact")).Append(string.Join(", ", keys.Select(InscriptionLink)));
        }

        var b = GearDescriptions.AnalyzeReference(reference);
        if (b != null)
        {
            bool given = stats != null, current = given || RunStats;
            stats ??= Neutral();
            string effect = Full(() => GearDescriptions.Part(b, "Effect", null, stats: stats));
            string summons = Full(() => GearDescriptions.SummonParts(b, stats));
            if (effect.Length > 0 || summons.Length > 0)
            {
                sb.Append(Heading(Loc.T("Damage"), "inscription:Arms")).Append(StatsNote(given, current));
                if (effect.Length > 0)
                    sb.Append('\n').Append(effect);
                if (summons.Length > 0)
                    sb.Append('\n').Append(summons);
            }
            foreach (var upgrade in b.Upgrades)
            {
                string target = Link(CodexCatalog.GearId(Characters.Gear.Gear.Type.Item, upgrade.TargetName));
                string header = upgrade.OwnItemName.Length > 0
                    ? Loc.F("Becomes {0} when you own {1}:", target, Link(CodexCatalog.GearId(Characters.Gear.Gear.Type.Item, upgrade.OwnItemName)))
                    : Loc.F("Becomes {0}:", target);
                sb.Append(Heading(Loc.T("Transformation"), "inscription:Mutation")).Append(header);
                var targetBreakdown = GearDescriptions.AnalyzeItemByName(upgrade.TargetName);
                string numbers = targetBreakdown == null ? "" : Full(() => GearDescriptions.Part(targetBreakdown, "Effect", null, stats: stats));
                if (numbers.Length > 0)
                    sb.Append('\n').Append(numbers);
            }
            // Items that this one comes from.
            foreach (var from in ItemsBecoming(entry.Key))
                sb.Append(Heading(Loc.T("Comes from"), "inscription:Mutation")).Append(Link(CodexCatalog.GearId(Characters.Gear.Gear.Type.Item, from)));
        }
        sb.Append(CoinText(entry));
        sb.Append(Flavor(flavor, tier));
        sb.Append(Counters(p, entry.Category));
        return sb.ToString();
    }

    /// <summary>Items that turn into <paramref name="itemName"/> (only those analyzed so far: the book learns them as pages open).</summary>
    private static IEnumerable<string> ItemsBecoming(string itemName) =>
        GearDescriptions.KnownUpgrades.Where(u => u.target == itemName).Select(u => u.from).Distinct();

    private static string InscriptionLink(Inscription.Key key) =>
        Link(CodexCatalog.InscriptionId(key)) is var link && link.Contains("???") ? Inscription.GetName(key) : link;

    // ---- quintessences

    private static string Essence(CodexEntry entry, EntryProgress p, int tier, StatSnapshot stats)
    {
        var sb = new StringBuilder();
        var (description, flavor) = CodexCatalog.GearTexts(entry);
        sb.Append(description);
        // A quintessence's main text is its active one (Troll had none in its plain description).
        var (activeName, activeText) = CodexCatalog.ActiveTexts(entry);
        if (activeName.Length > 0 || activeText.Length > 0)
            sb.Append(description.Length > 0 ? "\n\n" : "").Append(activeName.Length > 0 ? $"<b>{activeName}</b>\n" : "").Append(activeText);
        var b = GearDescriptions.AnalyzeReference(CodexCatalog.GearOf(entry));
        if (b != null)
        {
            bool given = stats != null, current = given || RunStats;
            stats ??= Neutral();
            var active = b.Sections.FirstOrDefault(s => s.Kind == "Active");
            if (active != null && active.Cooldown > 0)
                sb.Append("\n\n").Append(Loc.F("Cooldown: {0} s", Seconds(CooldownFor(active.Cooldown, "Essence", current))));
            string numbers = Full(() => GearDescriptions.Part(b, "Active", null, stats: stats));
            string summons = Full(() => GearDescriptions.SummonParts(b, stats));
            if (numbers.Length > 0 || summons.Length > 0)
            {
                sb.Append(Heading(Loc.T("Damage"), "inscription:Arms")).Append(StatsNote(given, current));
                if (numbers.Length > 0)
                    sb.Append('\n').Append(numbers);
                if (summons.Length > 0)
                    sb.Append('\n').Append(summons);
            }
        }
        sb.Append(Flavor(flavor, tier));
        sb.Append(Counters(p, entry.Category));
        return sb.ToString();
    }

    /// <summary>A cooldown in seconds; on the You page divided by the player's cooldown speed for that kind.</summary>
    private static double CooldownFor(double seconds, string kind, bool current)
    {
        if (!current)
            return seconds;
        try
        {
            var stat = Singleton<Service>.Instance?.levelManager?.player?.stat;
            if (stat == null)
                return seconds;
            var speedKind = kind switch
            {
                "Swap" => Characters.Stat.Kind.SwapCooldownSpeed,
                "Essence" => Characters.Stat.Kind.EssenceCooldownSpeed,
                _ => Characters.Stat.Kind.SkillCooldownSpeed,
            };
            double speed = stat.GetFinal(speedKind);
            return speed > 0 ? seconds / speed : seconds;
        }
        catch (Exception)
        {
            return seconds;
        }
    }

    // ---- skulls

    /// <summary>What the Codex reads from a skull's prefab besides its numbers (cached; texts are looked up when shown).</summary>
    public sealed class SkullInfo
    {
        public string Category = "";
        public string Next = "";
        public int SkillSlots = 1;
        public readonly List<string> Skills = new();   // SkillInfo keys, in prefab order
    }

    private static readonly Dictionary<string, SkullInfo> Skulls = new();

    public static SkullInfo SkullOf(CodexEntry entry)
    {
        if (Skulls.TryGetValue(entry.Key, out var cached))
            return cached;
        var info = new SkullInfo();
        if (CodexCatalog.GearOf(entry) is WeaponReference reference)
        {
            info.Category = reference.category.ToString();
            GearDescriptions.WithGearPrefab(reference, go =>
            {
                var weapon = go.GetComponent<Weapon>();
                if (weapon == null)
                    return;
                info.Category = weapon.category.ToString();
                info.Next = weapon.nextLevelReference?.name ?? "";
                info.SkillSlots = Math.Max(1, weapon._skillSlots);
                foreach (var skill in go.GetComponentsInChildren<SkillInfo>(true))
                    if (!string.IsNullOrEmpty(skill.key) && !info.Skills.Contains(skill.key))
                        info.Skills.Add(skill.key);
            });
        }
        Skulls[entry.Key] = info;
        return info;
    }

    private static readonly Regex TierSuffix = new(@"^(.*?)(?:_(\d+))?$");

    /// <summary>
    /// The awakening path a skull belongs to, first tier first: the skulls named like it ("GraveDigger",
    /// "GraveDigger_2", ...), checked against the skull's own link to its next tier.
    /// </summary>
    public static List<CodexEntry> AwakeningPath(CodexEntry entry)
    {
        string root = TierSuffix.Match(entry.Key).Groups[1].Value;
        int TierOf(string key)
        {
            var m = TierSuffix.Match(key);
            return m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
        }
        var path = CodexCatalog.Entries
            .Where(e => e.Category == CodexCategory.Skulls && TierSuffix.Match(e.Key).Groups[1].Value == root)
            .OrderBy(e => TierOf(e.Key)).ToList();
        string next = SkullOf(entry).Next;
        if (next.Length > 0 && path.All(e => e.Key != next) &&
            CodexCatalog.Entries.FirstOrDefault(e => e.Category == CodexCategory.Skulls && e.Key == next) is { } extra)
            path.Insert(path.IndexOf(entry) + 1, extra);
        return path.Count > 1 ? path : new List<CodexEntry>();
    }

    private static string CategoryName(string category)
    {
        string text = CodexCatalog.Localize($"label/weapon/Category/{category}");
        return text.Length > 0 ? text : Loc.Name(category);
    }

    /// <summary>The skills on your skulls of this kind right now (the You page shows them).</summary>
    private static HashSet<string> HeldSkills(CodexEntry entry)
    {
        var keys = new HashSet<string>();
        try
        {
            var weapons = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.weapon;
            foreach (var skull in new[] { weapons?.current, weapons?.next })
                if (skull != null && skull.currentSkills != null && CodexCatalog.GearId(Characters.Gear.Gear.Type.Weapon, skull.name.Replace("(Clone)", "").Trim()) == entry.Id)
                    foreach (var skill in skull.currentSkills)
                        if (skill != null)
                            keys.Add(skill.key);
        }
        catch (Exception)
        {
            // no run
        }
        return keys;
    }

    private static string Skull(CodexEntry entry, EntryProgress p, int tier, StatSnapshot stats)
    {
        var sb = new StringBuilder();
        var info = SkullOf(entry);
        var (description, flavor) = CodexCatalog.GearTexts(entry);
        if (info.Category.Length > 0)
            sb.Append($"<b>{Loc.T("Type")}:</b> {CategoryName(info.Category)}\n\n");
        sb.Append(description);

        var path = AwakeningPath(entry);
        if (path.Count > 0)
            sb.Append(Heading(Loc.T("Awakening"), "inscription:Heritage")).Append(string.Join("  >  ", path.Select(e => e.Id == entry.Id ? $"<b>{e.Name}</b>" : Link(e))));

        var b = GearDescriptions.AnalyzeReference(CodexCatalog.GearOf(entry));
        bool given = stats != null, current = given || RunStats;
        stats ??= Neutral(b);
        if (b != null)
        {
            var attacks = new[]
            {
                Full(() => GearDescriptions.Part(b, "Basic", Loc.T("Basic attack"), stats: stats)),
                Full(() => GearDescriptions.Part(b, "Jump", Loc.T("Jump attack"), stats: stats)),
                Full(() => GearDescriptions.Part(b, "Dash", Loc.T("Dash attack"), stats: stats)),
                Full(() => GearDescriptions.Part(b, "Passive", Loc.T("Passive"), stats: stats)),
            }.Where(t => t.Length > 0).ToList();
            if (attacks.Count > 0)
                sb.Append(Heading(Loc.T("Attacks"), "inscription:Arms")).Append(StatsNote(given, current)).Append('\n').Append(string.Join("\n", attacks));
        }

        if (info.Skills.Count > 0)
        {
            sb.Append(Heading(Loc.T("Skills"), "inscription:Wisdom"));
            if (info.Skills.Count > info.SkillSlots)
                sb.Append(Dim(Loc.F("You get {0} of these at random.", info.SkillSlots))).Append('\n');
            var held = given ? HeldSkills(entry) : new HashSet<string>();
            foreach (string key in info.Skills)
            {
                // A box per skill: revealed once a won run had it (beat a boss of chapter 5 or later with it on one of
                // your skulls); on the You page the skills you hold are shown.
                bool revealed = CodexTiers.RevealAllMet || held.Contains(key) || p?.WonSkill(key) == true;
                if (!revealed)
                {
                    sb.Append(PageBlocks.Tile("", "<b>???</b>\n" + Dim(Loc.T("Win a run with this skill to reveal it: beat a boss of chapter 5 or later while it is on one of your skulls."))));
                    continue;
                }
                string name = CodexCatalog.Localize($"skill/{key}/name");
                string skillText = CodexCatalog.Localize($"skill/{key}/desc");
                var section = b?.Sections.FirstOrDefault(s => s.Kind == "Skill" && s.Key == key);
                var tile = new StringBuilder($"<b>{(name.Length > 0 ? name : Loc.Name(WeaponRefiner.Humanize(key)))}</b>");
                if (section != null && section.Cooldown > 0)
                    tile.Append("  ").Append(Dim(Loc.F("Cooldown: {0} s", Seconds(CooldownFor(section.Cooldown, "Skill", current)))));
                if (skillText.Length > 0)
                    tile.Append('\n').Append(skillText);
                if (b != null && b.Sections.Any(s => s.Kind == "Skill" && s.Key == key && s.HasHits))
                    tile.Append('\n').Append(Full(() => GearDescriptions.Part(b, "Skill", null, key, stats)));
                sb.Append(PageBlocks.Tile("skillkey:" + key, tile.ToString()));
            }
        }

        if (b != null)
        {
            string swap = Full(() => GearDescriptions.Part(b, "Swap", null, stats: stats));
            if (swap.Length > 0)
                sb.Append(Heading(Loc.T("Swap"), "inscription:Mutation")).Append(swap);
            string bodies = Full(() => GearDescriptions.TransformParts(b, stats));
            if (bodies.Length > 0)
                sb.Append('\n').Append(bodies);
            string summons = Full(() => GearDescriptions.SummonParts(b, stats));
            if (summons.Length > 0)
                sb.Append('\n').Append(summons);
        }
        sb.Append(CoinText(entry));
        sb.Append(Flavor(flavor, tier));
        sb.Append(Counters(p, entry.Category));
        return sb.ToString();
    }

    // ---- inscriptions

    /// <summary>The player's inscription object for a key (it exists for every key while a player exists), or null.</summary>
    public static Inscription InscriptionOf(string key)
    {
        var inscriptions = Singleton<Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy?.inscriptions;
        return inscriptions != null && Enum.TryParse(key, out Inscription.Key k) ? inscriptions[k] : null;
    }

    private static string InscriptionPage(CodexEntry entry, EntryProgress p, int tier, StatSnapshot stats)
    {
        var inscription = InscriptionOf(entry.Key);
        if (inscription == null)
            return Dim(Loc.T("Open the Codex in the castle or during a run to read the steps."));
        bool given = stats != null, current = given || RunStats;
        stats ??= Neutral();
        var sb = new StringBuilder();
        int reached = CodexTiers.RevealAllMet ? int.MaxValue : Math.Max(p?.MaxStep ?? 0, tier >= 2 ? int.MaxValue : 0);
        sb.Append(Heading(Loc.T("Steps"), "inscription:" + entry.Key)).Append(StatsNote(given, current)).Append('\n');
        for (int step = 1; inscription.steps != null && step < inscription.steps.Count; step++)
        {
            string count = $"<b>{inscription.steps[step]}</b>";
            if (step > reached)
            {
                sb.Append($"\n{count}  ").Append(Dim(Loc.T("Reach this step once to read it."))).Append('\n');
                continue;
            }
            int s = step;
            sb.Append($"\n{count}  {Patches.DescriptionPatches.PlainText(() => inscription.GetDescription(s))}");
            string numbers = Full(() => GearDescriptions.ForInscription(inscription, s, stats));
            if (numbers.Length > 0)
                sb.Append(numbers);
            sb.Append('\n');
        }
        if (inscription.canBeSuperType)
        {
            sb.Append(Heading(Loc.T("Tuned"), "inscription_full:" + entry.Key));
            if (tier >= 3 || CodexTiers.RevealAllMet)
                sb.Append(Patches.DescriptionPatches.PlainText(() => inscription.GetSuperDescription())).Append(Full(() => GearDescriptions.ForInscriptionSuper(inscription, stats)));
            else
                sb.Append(Dim(Loc.T("Master it to read its tuned form.")));
        }

        // Every item that carries it.
        if (Enum.TryParse(entry.Key, out Inscription.Key key))
        {
            var items = (GearResource.instance?.items ?? Enumerable.Empty<ItemReference>())
                .Where(r => r != null && (r.prefabKeyword1 == key || r.prefabKeyword2 == key))
                .Select(r => Entry(CodexCatalog.GearId(Characters.Gear.Gear.Type.Item, r.name)))
                .Where(e => e != null).Distinct().OrderBy(e => e.Order).ToList();
            if (items.Count > 0)
            {
                int known = items.Count(e => CodexTiers.Tier(e.Category, CodexTracker.Page(e.Id), e.Id) > 0);
                sb.Append(Heading(Loc.F("Items with it ({0} / {1} found)", known, items.Count), "inscription:Artifact"));
                sb.Append(string.Join(", ", items.Select(Link)));
            }
        }
        sb.Append(Counters(p, entry.Category));
        return sb.ToString();
    }

    // ---- the player's dark abilities (Dark Mirror shop)

    private static string DarkAbility(CodexEntry entry, EntryProgress p, int tier)
    {
        var reference = CodexCatalog.DarkOf(entry);
        if (reference == null)
            return "";
        var sb = new StringBuilder();
        int level = 0;
        try
        {
            level = reference.GetCurrentLevel();
        }
        catch (Exception)
        {
            // no player
        }
        string description;
        try
        {
            description = reference.GetDescription(level, LinkColor.TrimStart('#'), DimInk.TrimStart('#'));
        }
        catch (Exception)
        {
            description = CodexCatalog.Localize(reference.displayNameKey.Replace("/name", "/desc"));
        }
        sb.Append(description);
        if (reference.maxLevel > 1)
            sb.Append("\n\n").Append(Dim(Loc.F("Levels: {0}", reference.maxLevel) + (level > 0 ? "  ·  " + Loc.F("yours: {0}", level) : "")));
        if (reference.type == Characters.Gear.Upgrades.UpgradeObject.Type.Cursed)
            sb.Append("\n").Append($"<color={LinkColor}>{Characters.Gear.Upgrades.UpgradeResource.Reference.curseText}</color>");
        // Damage per level (Umut 2026-10-12: in 0.12, not the 0.14 Dark Mirror page): the full formula, neutral or your run.
        var b = GearDescriptions.AnalyzeUpgrade(reference);
        if (b != null && b.Sections.Any(s => s.HasHits))
        {
            bool current = RunStats;
            var stats = Neutral();
            sb.Append(Heading(Loc.T("Damage"), "inscription:Arms")).Append(StatsNote(false, current));
            var passive = b.Sections.FirstOrDefault(s => s.Kind == "Passive" && s.HasHits);
            if (passive != null)
                sb.Append('\n').Append(Full(() => DescriptionFormatter.Section(passive, stats, Loc.T("Always"))));
            foreach (var section in b.Sections.Where(s => s.Kind == "Level" && s.HasHits))
            {
                string label = Loc.F("Level {0}", section.Key);
                if (level.ToString(CultureInfo.InvariantCulture) == section.Key)
                    label += " " + Loc.T("(yours)");
                sb.Append('\n').Append(Full(() => DescriptionFormatter.Section(section, stats, label)));
            }
        }
        if (reference.prices != null && reference.prices.Length > 0)
            sb.Append(Heading(Loc.T("Price per level"), "inscription:Omen")).Append(string.Join(" / ", reference.prices.Take(Math.Max(1, reference.maxLevel))));
        string flavor = CodexCatalog.Localize(reference.displayNameKey.Replace("/name", "/flavor"));
        sb.Append(Flavor(flavor, tier));
        sb.Append(Counters(p, entry.Category));
        return sb.ToString();
    }
}
