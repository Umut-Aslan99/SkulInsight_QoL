using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Lang;

namespace DamageInsight.Describe;

/// <summary>
/// How the description texts are put together from analyses (which parts, in which order, with which headings).
/// Plain logic, no Unity: the game (GearDescriptions) and the QA corpus (tests) build the same texts with it.
/// </summary>
public static class DescriptionLayout
{
    /// <summary>
    /// Compact (the game's own boxes, Umut 2026-10-11: "the Codex is the place for all the numbers"): one line per attack
    /// with its hits' totals side by side ("Basic attack: 10–12 · 22–24 · 36–42 Magic"), equal parts merged ("4 x 11–13"),
    /// one line per summon, a transformed body's basic / jump / dash attack only. Set by GearDescriptions per text;
    /// the Codex always writes the full form.
    /// </summary>
    public static bool Compact;

    /// <summary>
    /// A skill's name from its key: the game's own name in the game (GearDescriptions sets this to
    /// "skill/&lt;key&gt;/name"), else the key made readable ("Sword of fate 2").
    /// </summary>
    public static Func<string, string> SkillName = key => Loc.Name(WeaponRefiner.Humanize(key));

    /// <summary>At most this many summons get a line in compact texts; the rest is counted.</summary>
    public const int CompactSummons = 4;

    /// <summary>"All numbers: Codex (K)" under a compact skull text.</summary>
    public static string CodexFooter(string key) =>
        $"<color={DescriptionFormatter.DimColor}>{Loc.F("All numbers: Codex ({0})", key)}</color>";

    /// <summary>A skull's passive text: basic combo, jump/dash attack, passive hits, summons and transformed bodies.</summary>
    public static string Weapon(Breakdown b, IEnumerable<(string name, Breakdown breakdown)> summons,
        IEnumerable<(bool swap, Breakdown breakdown)> bodies, StatSnapshot stats) =>
        Join(
            Part(b, "Basic", Loc.T("Basic attack"), null, stats),
            Part(b, "Jump", Loc.T("Jump attack"), null, stats),
            Part(b, "Dash", Loc.T("Dash attack"), null, stats),
            Part(b, "Passive", Loc.T("Passive"), null, stats),
            Summons(summons, stats),
            Transforms(bodies, stats));

    public static string Swap(Breakdown b, StatSnapshot stats) => Part(b, "Swap", null, null, stats);

    public static string Skill(Breakdown b, string key, StatSnapshot stats) => Part(b, "Skill", null, key, stats);

    /// <summary>An item's effect, what it turns into (with that item's damage) and its summons.</summary>
    public static string Item(Breakdown b, Func<string, Breakdown> analyzeItem, Func<string, string> itemName,
        IEnumerable<(string name, Breakdown breakdown)> summons, StatSnapshot stats) =>
        Join(new[] { Part(b, "Effect", null, null, stats) }
            .Concat(Upgrades(b, analyzeItem, itemName, stats))
            .Append(Summons(summons, stats)).ToArray());

    public static string Essence(Breakdown b, IEnumerable<(string name, Breakdown breakdown)> summons, StatSnapshot stats) =>
        Join(Part(b, "Active", null, null, stats), Summons(summons, stats));

    /// <summary>For items that turn into another item: what they become, and that item's damage.</summary>
    public static IEnumerable<string> Upgrades(Breakdown b, Func<string, Breakdown> analyzeItem, Func<string, string> itemName,
        StatSnapshot stats)
    {
        foreach (var upgrade in b.Upgrades)
        {
            string header = upgrade.OwnItemName.Length > 0
                ? Loc.F("Becomes {0} when you own {1}:", itemName(upgrade.TargetName), itemName(upgrade.OwnItemName))
                : Loc.F("Becomes {0}:", itemName(upgrade.TargetName));
            yield return $"<color={DescriptionFormatter.DimColor}>{header}</color>";

            var target = analyzeItem(upgrade.TargetName);
            if (target != null)
                yield return Part(target, "Effect", null, null, stats);
        }
    }

    /// <summary>"Summons X:" blocks with each summon's attacks.</summary>
    public static string Summons(IEnumerable<(string name, Breakdown breakdown)> summons, StatSnapshot stats)
    {
        var parts = new List<string>();
        if (Compact)
        {
            // One line per summon: its first attack.
            var list = (summons ?? Enumerable.Empty<(string name, Breakdown breakdown)>())
                .GroupBy(x => x.name).Select(g => g.First()).ToList(); // a swap's and a skill's copy of one summon
            foreach (var (name, summon) in list.Take(CompactSummons))
            {
                var first = summon.Sections.FirstOrDefault(sec => sec.HasHits);
                if (first != null)
                    parts.Add($"<color={DescriptionFormatter.DimColor}>{Loc.F("Summons {0}:", name)}</color> {CompactSteps(first, stats)}");
            }
            if (list.Count > CompactSummons)
                parts.Add($"<color={DescriptionFormatter.DimColor}>{Loc.P("+{0} more summon", "+{0} more summons", list.Count - CompactSummons)}</color>");
            return Join(parts.ToArray());
        }
        foreach (var (name, summon) in summons ?? Enumerable.Empty<(string, Breakdown)>())
        {
            parts.Add($"<color={DescriptionFormatter.DimColor}>{Loc.F("Summons {0}:", name)}</color>");
            foreach (var section in summon.Sections.Where(sec => sec.HasHits))
                parts.Add(DescriptionFormatter.Section(section, stats, Loc.Name(WeaponRefiner.Humanize(section.Key))));
        }
        return Join(parts.ToArray());
    }

    /// <summary>"Transformed by a skill / the swap:" blocks with the transformed body's attacks.</summary>
    public static string Transforms(IEnumerable<(bool swap, Breakdown breakdown)> bodies, StatSnapshot stats)
    {
        var parts = new List<string>();
        foreach (var (swap, body) in bodies ?? Enumerable.Empty<(bool, Breakdown)>())
        {
            parts.Add($"<color={DescriptionFormatter.DimColor}>{(swap ? Loc.T("Transformed by the swap:") : Loc.T("Transformed by a skill:"))}</color>");
            parts.Add(Part(body, "Basic", Loc.T("Basic attack"), null, stats));
            parts.Add(Part(body, "Jump", Loc.T("Jump attack"), null, stats));
            parts.Add(Part(body, "Dash", Loc.T("Dash attack"), null, stats));
            if (Compact)
                continue; // the body's skills show in the skill boxes while transformed, and in the Codex
            foreach (var skill in body.Sections.Where(sec => sec.Kind == "Skill" && sec.HasHits))
                parts.Add(Part(body, "Skill", SkillName(skill.Key), skill.Key, stats));
        }
        return Join(parts.ToArray());
    }

    /// <summary>The lines of one inscription step that come from its analysis (the parts this step unlocks).</summary>
    public static IEnumerable<string> InscriptionParts(Breakdown b, int step, StatSnapshot stats)
    {
        if (b == null)
            yield break;
        foreach (var section in b.Sections.Where(s => s.Kind == "Inscription" && s.Step == step && (s.HasHits || s.Notes.Count > 0)))
            yield return Compact && section.HasHits
                ? CompactLine(section, stats, section.Title.Length > 0 ? section.Title : null)
                : DescriptionFormatter.Section(section, stats, section.Title.Length > 0 ? section.Title : null);
    }

    /// <summary>All sections of one kind (and skill key), one after another, with their heading.</summary>
    public static string Part(Breakdown b, string kind, string title, string key, StatSnapshot stats)
    {
        var sections = b.Sections.Where(s => s.Kind == kind && (key == null || s.Key == key) && s.HasHits).ToList();
        if (sections.Count == 0)
            return "";
        // Compact: more than two actions of one kind (the Mummies' guns) share one line with their distinct totals.
        if (Compact && sections.Count > 2)
        {
            string all = string.Join(" | ", sections.Select(s => CompactSteps(s, stats)).Where(x => x.Length > 0).Distinct());
            return title == null ? all : $"<color={DescriptionFormatter.DimColor}>{Loc.F("{0}:", title)}</color> {all}";
        }
        // Several actions of one kind (e.g. ground and air combos) are listed one after another.
        return string.Join("\n", sections.Select((s, i) =>
        {
            string heading = title == null ? null
                : sections.Count > 1 ? (s.Title.Length > 0 ? Loc.F("{0}: {1}", title, s.Title) : $"{title} ({i + 1})")
                : title;
            return Compact ? CompactLine(s, stats, heading) : DescriptionFormatter.Section(s, stats, heading);
        }).Distinct()); // compact: equal lines (a gun firing forward and upward) once
    }

    /// <summary>One compact line: "Heading: totals", with the section's trigger ("On critical hit") when it has one.</summary>
    private static string CompactLine(Section section, StatSnapshot stats, string heading)
    {
        string body = CompactSteps(section, stats);
        string trigger = section.Trigger.Length > 0 ? section.Trigger : null;
        string label = heading != null && trigger != null ? $"{heading} · {trigger}" : heading ?? trigger;
        return label == null ? body : $"<color={DescriptionFormatter.DimColor}>{Loc.F("{0}:", label)}</color> {body}";
    }

    /// <summary>
    /// A section's steps as one line of totals: "10–12 · 22–24 · 36–42 Magic"; equal parts "4 x 11–13 Magic"; named steps
    /// keep their name ("Uncharged 112–130 · Charged 167–195 Magic"); one step "5 hits = 50–70 Physical".
    /// </summary>
    public static string CompactSteps(Section section, StatSnapshot stats)
    {
        var steps = section.Steps.Where(st => st.Hits.Count > 0).ToList();
        if (steps.Count == 0)
            return "";
        if (steps.Count == 1)
            return DescriptionFormatter.ShortHits(steps[0].Hits, stats);
        if (steps.Any(st => st.Hits.Any(h => h.UsesSkullDamage && stats.SkullMax <= 0)))
            return string.Join(" · ", steps.Select(st => DescriptionFormatter.ShortHits(st.Hits, stats)));

        var totals = steps.Select(st =>
        {
            double min = 0, max = 0;
            foreach (var h in st.Hits)
            {
                var (hMin, hMax) = DescriptionFormatter.FinalRange(h, stats);
                min += hMin * Math.Max(1, h.Count);
                max += hMax * Math.Max(1, h.Count);
            }
            return (min, max, attribute: string.Join("/", st.Hits.Select(DescriptionFormatter.AttributeName).Distinct()));
        }).ToList();
        bool oneAttribute = totals.Select(t => t.attribute).Distinct().Count() == 1;
        string colour = steps[0].Hits[0].Attribute == "Magic" ? DescriptionFormatter.MagicColor
            : steps[0].Hits[0].Attribute == "Fixed" ? DescriptionFormatter.FixedColor : DescriptionFormatter.PhysicalColor;
        bool named = steps.Select((st, i) => Named(st, i)).Any(n => n);

        if (!named && oneAttribute && totals.All(t => t.min == totals[0].min && t.max == totals[0].max))
            return $"<color={colour}>{steps.Count} x {DescriptionFormatter.Range(totals[0].min, totals[0].max)} {totals[0].attribute}</color>";

        var parts = new List<string>();
        for (int i = 0; i < steps.Count; i++)
        {
            string range = DescriptionFormatter.Range(totals[i].min, totals[i].max) + (oneAttribute ? "" : " " + totals[i].attribute);
            if (Named(steps[i], i))
            {
                parts.Add($"<color={DescriptionFormatter.DimColor}>{steps[i].Label}</color> {range}");
                continue;
            }
            int run = 1;
            while (i + run < steps.Count && !Named(steps[i + run], i + run) && totals[i + run] == totals[i])
                run++;
            parts.Add(run > 1 ? $"{run} x {range}" : range);
            i += run - 1;
        }
        return $"<color={colour}>{string.Join(" · ", parts)}{(oneAttribute ? " " + totals[0].attribute : "")}</color>";
    }

    /// <summary>A step label worth keeping in a compact line ("Charged", "Enhanced cannonball"), not "Hit 2" or "Part 3".</summary>
    private static bool Named(Step step, int index) =>
        !string.IsNullOrEmpty(step.Label) && !step.IsPart && step.Label != Loc.F("Hit {0}", index + 1);

    public static string Join(params string[] parts) => string.Join("\n", parts.Where(p => p.Length > 0));
}
