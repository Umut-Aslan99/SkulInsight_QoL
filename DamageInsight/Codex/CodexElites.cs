using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Characters.Abilities.Darks;
using DamageInsight.Describe;
using DamageInsight.Lang;
using DamageInsight.Tools;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// The dark enemies' abilities (Dark Mirror elites, Characters.Abilities.Darks): listed from the game's own pools
/// (DarkEnemySelector → constructors → DarkAbilityContainer → WeightedDarkAbility), met when a dark enemy rolls
/// them, learned by killing dark enemies that had them. The game has names for them but no descriptions, so the
/// page text is ours (codex_content.json, key "elite:&lt;name&gt;"), plus the numbers read from the ability.
/// </summary>
public static class CodexElites
{
    public static string Id(string abilityName) => "elite:" + abilityName;

    private sealed class Info
    {
        public DarkAbility Ability;
        public readonly SortedSet<int> Levels = new();   // Dark Mirror levels whose pool can roll it
        public readonly List<string> Except = new();      // enemy keys that never get it
    }

    private static readonly Dictionary<string, Info> Abilities = new();

    /// <summary>Adds an entry for every ability in the game's pools (called while the catalog is built).</summary>
    public static void AddTo(List<CodexEntry> list)
    {
        try
        {
            var selector = Level.DarkEnemySelector.instance;
            var constructors = selector?._constructors;
            if (constructors == null)
                return;
            int order = 0;
            // One pool per Dark Mirror level (DarkEnemySelector.ElectIn: _constructors[currentLevel]); an ability can
            // come up at a level when that level's pool has it with a weight and the level is in its range.
            for (int level = 0; level < constructors.Length; level++)
            {
                var weighted = constructors[level]?._attacher?._darkAbilityContainer?._weightedDarkAbility?.components;
                if (weighted == null)
                    continue;
                foreach (var w in weighted)
                {
                    var ability = w?._abilityComponent;
                    if (ability == null || string.IsNullOrEmpty(ability.name))
                        continue;
                    string id = Id(ability.name);
                    bool rolls = w._value > 0 && level >= w._appearanceLevel.x && level <= w._appearanceLevel.y;
                    if (Abilities.TryGetValue(id, out var known))
                    {
                        if (rolls)
                            known.Levels.Add(level);
                        continue;
                    }
                    var info = new Info { Ability = ability };
                    if (rolls)
                        info.Levels.Add(level);
                    if (ability._exceptTarget != null)
                        info.Except.AddRange(ability._exceptTarget.Select(k => k.ToString()));
                    Abilities[id] = info;
                    // The game's English text has "IntenseCold": split words glued together.
                    string name = System.Text.RegularExpressions.Regex.Replace(
                        CodexCatalog.Localize($"darkEnemy/ability/{ability.name}/name"), @"(?<=\p{Ll})(?=\p{Lu})", " ");
                    list.Add(new CodexEntry
                    {
                        Id = id, Category = CodexCategory.Elites, Group = Loc.N("Dark enemy ability"),
                        Order = order++, Key = ability.name,
                        Name = name.Length > 0 ? name : Recording.OwnerNames.Humanize(ability.name),
                    });
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: dark enemy abilities could not be listed: {e.Message}");
        }
    }

    /// <summary>The dark enemies' abilities share the Omen inscription's icon (the game has none of their own).</summary>
    public static Sprite Icon(CodexEntry entry)
    {
        try
        {
            return Characters.Gear.Synergy.Inscriptions.Inscription.GetActiveIcon(Characters.Gear.Synergy.Inscriptions.Inscription.Key.Omen);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly Dictionary<string, Breakdown> Analyses = new();
    private static readonly Dictionary<string, List<(string name, Breakdown breakdown)>> Summons = new();

    /// <summary>The ability's parts analyzed like gear (hits, summoned turrets...), once.</summary>
    private static Breakdown Analyze(string id, Info info)
    {
        if (Analyses.TryGetValue(id, out var cached))
            return cached;
        Breakdown breakdown = null;
        var summons = new List<(string, Breakdown)>();
        try
        {
            var writer = new ObjectGraphWriter();
            writer.WriteRoot(info.Ability.gameObject, new Dictionary<string, string> { ["category"] = "darks", ["name"] = info.Ability.name });
            breakdown = GearAnalyzer.Analyze(GearDoc.Parse(writer.Result));
            foreach (var character in writer.ReferencedCharacters.Where(c => c != null).Distinct())
            {
                var w2 = new ObjectGraphWriter();
                w2.WriteRoot(character.gameObject, new Dictionary<string, string> { ["category"] = "characters", ["name"] = character.gameObject.name });
                var b = GearAnalyzer.Analyze(GearDoc.Parse(w2.Result));
                if (b.Sections.Any(s => s.HasHits))
                    summons.Add((CodexCatalog.EnemyName(character.key) is { Length: > 0 } n && character.key != Characters.Key.Unspecified
                        ? n : Recording.OwnerNames.Humanize(character.gameObject.name.Replace("(Clone)", "")), b));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: dark enemy ability {info.Ability?.name} could not be analyzed: {e.Message}");
        }
        Analyses[id] = breakdown;
        Summons[id] = summons;
        return breakdown;
    }

    /// <summary>"0-3, 7-10" (Dark Mirror levels).</summary>
    public static string Ranges(IEnumerable<int> values)
    {
        var parts = new List<string>();
        int? start = null, last = null;
        foreach (int v in values.OrderBy(v => v))
        {
            if (start != null && v == last + 1)
            {
                last = v;
                continue;
            }
            if (start != null)
                parts.Add(start == last ? $"{start}" : $"{start}-{last}");
            start = last = v;
        }
        if (start != null)
            parts.Add(start == last ? $"{start}" : $"{start}-{last}");
        return string.Join(", ", parts);
    }

    public static string Text(CodexEntry entry, EntryProgress p, int tier, StatSnapshot stats)
    {
        var sb = new StringBuilder();
        string about = CodexContent.About(entry.Id);
        if (tier == 0)
            return "";
        sb.Append(about.Length > 0 ? about : CodexPages.Dim(Loc.T("No notes on this ability yet.")));

        Abilities.TryGetValue(entry.Id, out var info);
        if (info != null)
        {
            if (info.Levels.Count > 0)
                sb.Append("\n\n").Append(CodexPages.Dim(Loc.F("Dark Mirror levels: {0}", Ranges(info.Levels))));
            if (info.Except.Count > 0)
            {
                var names = info.Except.Select(k => CodexPages.Entry("enemy:" + k)).Where(e => e != null).Select(CodexPages.Link).ToList();
                if (names.Count > 0)
                    sb.Append('\n').Append(CodexPages.Dim(Loc.F("Never on: {0}", string.Join(", ", names))));
            }

            if (tier >= 2 || CodexTiers.RevealAllMet)
            {
                var b = Analyze(entry.Id, info);
                var neutral = stats ?? CodexPages.Neutral();
                var parts = new List<string>();
                DescriptionFormatter.EnemyOwner = true; // "x% of the enemy's damage", not "of skull damage"
                try
                {
                if (b != null)
                    parts.AddRange(b.Sections.Where(s => s.HasHits).Select(s =>
                        CodexPages.Ink(DescriptionFormatter.Section(s, neutral, s.Title.Length > 0 ? s.Title : null))));
                foreach (var (name, summon) in Summons.TryGetValue(entry.Id, out var list) ? list : new List<(string, Breakdown)>())
                {
                    parts.Add(CodexPages.Dim(Loc.F("Summons {0}:", name)));
                    parts.AddRange(summon.Sections.Where(s => s.HasHits).Select(s =>
                        CodexPages.Ink(DescriptionFormatter.Section(s, neutral, Loc.Name(WeaponRefiner.Humanize(s.Key))))));
                }
                }
                finally
                {
                    DescriptionFormatter.EnemyOwner = false;
                }
                if (parts.Count > 0)
                    sb.Append(CodexPages.Heading(Loc.T("Damage"), "inscription:Arms")).Append(CodexPages.Dim(Loc.T("Before the enemy's own attack bonuses and the Dark Mirror level."))).Append('\n')
                      .Append(string.Join("\n", parts));
            }
            else
            {
                sb.Append("\n\n").Append(CodexPages.Dim(Loc.T("Kill a dark enemy with it to see its numbers.")));
            }
        }

        // The enemies it was seen on.
        var carriers = (p?.SummonedBy ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CodexPages.Entry).Where(e => e != null).ToList();
        if (carriers.Count > 0)
            sb.Append(CodexPages.Heading(Loc.T("Seen on"))).Append(string.Join(", ", carriers.Select(CodexPages.Link)));

        if (p != null)
        {
            string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
            sb.Append("\n\n").Append(CodexPages.Dim($"{Loc.T("Encountered")}: {N(p.Seen)}  ·  {Loc.T("Killed")}: {N(p.Kills)}"));
        }
        return sb.ToString();
    }
}
