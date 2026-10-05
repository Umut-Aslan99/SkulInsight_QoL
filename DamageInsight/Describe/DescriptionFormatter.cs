using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Lang;

namespace DamageInsight.Describe;

/// <summary>The player's damage stats as final multipliers (1.6 = 160%), like Stat.GetFinal returns them.</summary>
public sealed class StatSnapshot
{
    public double Physical = 1, Magic = 1, Projectile = 1, Basic = 1, Skill = 1, AttackDamage = 1;

    /// <summary>The equipped skull's base damage, for hits that borrow it (0 = unknown).</summary>
    public double SkullMin, SkullMax;

    // Status effect stats (Stat.Kind values; neutral defaults).
    public double PoisonTickReduction;      // seconds taken off the poison tick interval
    public double EmberDamage = 1;          // burn splash damage and radius multiplier
    public double BleedDamage = 1;          // bleed damage multiplier
    public double CritDamage = 1.5;         // Stat.Kind.CriticalDamage final value (the damage multiplier of a crit)
    public bool BleedCanCrit;               // Excessive Bleeding 4: bleed hits can crit
    public double FreezeBonus, StunBonus;   // extra seconds

    public static readonly StatSnapshot Neutral = new();

    public StatSnapshot Copy() => (StatSnapshot)MemberwiseClone();

    /// <summary>
    /// The stat factor the game applies to a hit (Characters.Stat.GetDamage): the category bonuses are
    /// added together, then multiplied by the general attack damage. Fixed damage ignores all of it.
    /// </summary>
    public double FactorFor(Hit hit)
    {
        if (hit.Attribute == "Fixed" || hit.UsesEnemyStats)
            return 1;
        double attribute = hit.AdaptiveForce ? Math.Max(Physical, Magic) : hit.Attribute == "Magic" ? Magic : Physical;
        double factor = 1 + (attribute - 1);
        if (hit.AttackType == "Projectile")
            factor += Projectile - 1;
        if (hit.MotionType == "Basic")
            factor += Basic - 1;
        else if (hit.MotionType == "Skill")
            factor += Skill - 1;
        return factor * AttackDamage;
    }
}

/// <summary>Turns a Breakdown into description lines, League-of-Legends style. Plain logic, no Unity.</summary>
public static class DescriptionFormatter
{
    public const string DimColor = "#9A8F84";
    public const string PhysicalColor = "#F25D1C"; // the game's own keyword colours
    public const string MagicColor = "#1787D8";
    public const string FixedColor = "#B8B0A8";

    /// <summary>Final damage range for one hit with the given stats (before crits and enemy damage reduction).</summary>
    public static (double min, double max) FinalRange(Hit hit, StatSnapshot stats)
    {
        double factor = stats.FactorFor(hit);
        var (baseMin, baseMax) = Base(hit, stats);
        return (Math.Ceiling(baseMin * hit.MultMin * factor - 1e-9), Math.Ceiling(baseMax * hit.MultMax * factor - 1e-9));
    }

    private static (double min, double max) Base(Hit hit, StatSnapshot stats) =>
        hit.UsesSkullDamage ? (stats.SkullMin, stats.SkullMax) : (hit.BaseMin, hit.BaseMax);

    /// <summary>
    /// One hit as text, e.g. "13–20 Physical (8–12 x 160%) · can't crit".
    /// The part in brackets shows base damage x hit multiplier x stat factor (the game font has no "×").
    /// </summary>
    public static string HitLine(Hit hit, StatSnapshot stats)
    {
        var sb = new StringBuilder();
        string amount = Amount(hit, stats);
        if (hit.Count > 1 && !UnknownBase(hit, stats))
        {
            // "16 hits of 6–8 Magic = 96–128" reads better than "16x 6–8".
            var (min, max) = FinalRange(hit, stats);
            sb.Append(Loc.P("{0} hit of {1} = {2}", "{0} hits of {1} = {2}", hit.Count, amount, Range(min * hit.Count, max * hit.Count)));
        }
        else if (hit.Count > 1)
            sb.Append(Loc.P("{0} hit of {1}", "{0} hits of {1}", hit.Count, amount));
        else
            sb.Append(amount);

        sb.Append($"<color={DimColor}>");
        string breakdown = Breakdown(hit, stats);
        if (breakdown.Length > 0)
            sb.Append($" ({breakdown})");
        var notes = Notes(hit);
        if (notes.Count > 0)
            sb.Append(" · ").Append(string.Join(" · ", notes));
        sb.Append("</color>");
        return sb.ToString();
    }

    /// <summary>The coloured damage amount, e.g. "13–20 Physical" (or "150% of skull damage, Magic").</summary>
    public static string Amount(Hit hit, StatSnapshot stats)
    {
        var (min, max) = FinalRange(hit, stats);
        string colour = hit.Attribute == "Magic" ? MagicColor : hit.Attribute == "Fixed" ? FixedColor : PhysicalColor;
        string attribute = AttributeName(hit);
        return UnknownBase(hit, stats)
            ? $"<color={colour}>{Loc.F("{0} of skull damage, {1}", Percent(hit.MultMin, hit.MultMax), attribute)}</color>"
            : $"<color={colour}>{Range(min, max)} {attribute}</color>";
    }

    /// <summary>"Physical", "Magic", "Fixed" or (adaptive force) "Physical/Magic", in the current language.</summary>
    public static string AttributeName(Hit hit) => hit.AdaptiveForce ? Loc.T("Physical/Magic") : AttributeName(hit.Attribute);

    public static string AttributeName(string attribute) => attribute switch
    {
        "Magic" => Loc.T("Magic"),
        "Fixed" => Loc.T("Fixed"),
        "Physical" => Loc.T("Physical"),
        _ => attribute,
    };

    /// <summary>How the amount is made: "8–12 x 125% x 160% phys. atk", or "" if it's just the base.</summary>
    public static string Breakdown(Hit hit, StatSnapshot stats)
    {
        var parts = new List<string>();
        bool scaled = false;
        if (!UnknownBase(hit, stats))
        {
            var (baseMin, baseMax) = Base(hit, stats);
            parts.Add(hit.UsesSkullDamage ? Loc.F("{0} skull dmg", Range(baseMin, baseMax)) : Range(baseMin, baseMax));
            if (Math.Abs(hit.MultMin - 1) > 1e-6 || Math.Abs(hit.MultMax - 1) > 1e-6)
            {
                parts.Add(Percent(hit.MultMin, hit.MultMax));
                scaled = true;
            }
        }
        double factor = stats.FactorFor(hit);
        if (hit.Attribute != "Fixed" && !hit.UsesEnemyStats && Math.Abs(factor - 1) > 1e-6)
        {
            parts.Add(Percent(factor, factor) + " " + AttackStatName(hit));
            scaled = true;
        }
        return scaled || hit.UsesSkullDamage ? string.Join(" x ", parts) : "";
    }

    /// <summary>Which attack stat scales a hit: "phys. atk", "magic atk" or (adaptive force) "higher atk".</summary>
    public static string AttackStatName(Hit hit) =>
        hit.AdaptiveForce ? Loc.T("higher atk") : hit.Attribute == "Magic" ? Loc.T("magic atk") : Loc.T("phys. atk");

    /// <summary>Short remarks about a hit: chance, "can't crit", its trigger note...</summary>
    public static List<string> Notes(Hit hit, bool repeats = true)
    {
        var notes = new List<string>();
        if (hit.Count == 0 && repeats)
            notes.Add(Loc.T("repeats"));
        if (hit.Chance < 0.999)
            notes.Add(Loc.F("{0}% chance", (hit.Chance * 100).ToString("0.#", CultureInfo.InvariantCulture)));
        if (!hit.CanCrit)
            notes.Add(Loc.T("can't crit"));
        if (hit.UsesEnemyStats)
            notes.Add(Loc.T("ignores your atk bonuses"));
        if (!string.IsNullOrEmpty(hit.Note))
            notes.Add(hit.Note);
        return notes;
    }

    private static bool UnknownBase(Hit hit, StatSnapshot stats) => hit.UsesSkullDamage && Base(hit, stats).max <= 0;

    /// <summary>Short descriptions (setting Descriptions/Short, set by GearDescriptions): only the totals per step.</summary>
    public static bool Short;

    /// <summary>All hits of a section, one step per line ("Hit 1: …").</summary>
    public static string Section(Section section, StatSnapshot stats, string title = null)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(title))
            lines.Add($"<color={DimColor}>{title}</color>");
        if (!string.IsNullOrEmpty(section.Trigger))
            lines.Add($"<color={DimColor}>{section.Trigger}</color>");
        foreach (var step in section.Steps)
        {
            if (step.Hits.Count == 0)
                continue;
            string hits = Short ? ShortHits(step.Hits, stats)
                : section.Compact ? CompactHits(step.Hits, stats) : string.Join(" + ", step.Hits.Select(h => HitLine(h, stats)));
            lines.Add(string.IsNullOrEmpty(step.Label) ? hits : Loc.F("{0}: {1}", step.Label, hits));
        }
        foreach (var note in section.Notes)
            lines.Add($"<color={DimColor}>{note}</color>");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Several hits as one short line: "4 hits = 202–317 Physical (530% + 265% + 1000% + 400% of 7–11 skull dmg,
    /// x160% phys. atk) · can't crit". Used where a text would otherwise be far too long (Arms).
    /// </summary>
    public static string CompactHits(List<Hit> hits, StatSnapshot stats)
    {
        if (hits.Count == 1 && hits[0].Count <= 1)
            return HitLine(hits[0], stats);
        int count = hits.Sum(h => Math.Max(1, h.Count));
        double min = 0, max = 0;
        foreach (var h in hits)
        {
            var (hMin, hMax) = FinalRange(h, stats);
            min += hMin * Math.Max(1, h.Count);
            max += hMax * Math.Max(1, h.Count);
        }
        var first = hits[0];
        string colour = first.Attribute == "Magic" ? MagicColor : first.Attribute == "Fixed" ? FixedColor : PhysicalColor;
        string attributes = string.Join("/", hits.Select(AttributeName).Distinct());

        string percents = string.Join(" + ", hits.Select(h => (h.Count > 1 ? $"{h.Count}x " : "") + Percent(h.MultMin, h.MultMax)));
        var (baseMin, baseMax) = Base(first, stats);
        string of = UnknownBase(first, stats) ? Loc.T("skull dmg")
            : first.UsesSkullDamage ? Loc.F("of {0} skull dmg", Range(baseMin, baseMax))
            : Loc.F("of {0}", Range(baseMin, baseMax));
        double factor = stats.FactorFor(first);
        string atk = Math.Abs(factor - 1) > 1e-6 && first.Attribute != "Fixed" ? $", x{Percent(factor, factor)} {AttackStatName(first)}" : "";
        var notes = Notes(first, repeats: false);
        string amount = UnknownBase(first, stats) ? "" : $" = <color={colour}>{Range(min, max)} {attributes}</color>";
        return $"{Loc.P("{0} hit", "{0} hits", count)}{amount}<color={DimColor}> ({percents} {of}{atk})" +
               (notes.Count > 0 ? " · " + string.Join(" · ", notes) : "") + "</color>";
    }

    /// <summary>Several hits as their total only: "4 hits = 202–317 Physical" (one hit: "51–80 Physical").</summary>
    public static string ShortHits(List<Hit> hits, StatSnapshot stats)
    {
        int count = hits.Sum(h => Math.Max(1, h.Count));
        double min = 0, max = 0;
        foreach (var h in hits)
        {
            var (hMin, hMax) = FinalRange(h, stats);
            min += hMin * Math.Max(1, h.Count);
            max += hMax * Math.Max(1, h.Count);
        }
        var first = hits[0];
        if (UnknownBase(first, stats))
            return Loc.P("{0} hit", "{0} hits", count);
        string colour = first.Attribute == "Magic" ? MagicColor : first.Attribute == "Fixed" ? FixedColor : PhysicalColor;
        string amount = $"<color={colour}>{Range(min, max)} {string.Join("/", hits.Select(AttributeName).Distinct())}</color>";
        return count > 1 ? $"{Loc.P("{0} hit", "{0} hits", count)} = {amount}" : amount;
    }

    public static string Range(double min, double max) =>
        Math.Abs(min - max) < 1e-9 ? Num(min) : $"{Num(min)}–{Num(max)}";

    private static string Percent(double min, double max) =>
        Math.Abs(min - max) < 1e-9 ? $"{Num(min * 100)}%" : $"{Num(min * 100)}–{Num(max * 100)}%";

    private static string Num(double v) => v.ToString(Math.Abs(v - Math.Round(v)) < 1e-6 ? "0" : "0.#", CultureInfo.InvariantCulture);
}
