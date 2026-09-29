using System;
using System.Collections.Generic;
using System.Globalization;

namespace DamageInsight.Recording;

/// <summary>
/// The numbers of a Characters.Damage at one moment. Damage.amount is
/// ceil(base x multiplier x percentMultiplier x (crit ? critPercent x critDamage : 1) + extraFixed);
/// Fixed damage is just ceil(base).
/// </summary>
public struct DamageState
{
    public double Base, Multiplier, PercentMultiplier, CritMultiplier, ExtraFixed;
    public bool Fixed, Null;

    public double Amount(bool critical)
    {
        if (Null)
            return 0;
        if (Fixed)
            return Math.Ceiling(Base);
        double amount = Base * Multiplier * PercentMultiplier;
        if (critical)
            amount *= CritMultiplier;
        return amount + ExtraFixed;
    }

    public bool SameAs(DamageState o) =>
        Base == o.Base && Multiplier == o.Multiplier && PercentMultiplier == o.PercentMultiplier &&
        CritMultiplier == o.CritMultiplier && ExtraFixed == o.ExtraFixed && Null == o.Null && Fixed == o.Fixed;
}

/// <summary>One thing that changed the damage: an item, inscription, dark ability, buff or debuff.</summary>
public sealed class TraceStep
{
    public string Label = "";
    public string Stage = "";     // "give" (attacker's effects) or "take" (target's effects)
    public DamageState After;
    public object Icon;           // a UnityEngine.Sprite at runtime; never written to files
}

/// <summary>One row of the explanation: "Strike  x2.59  = 1,234".</summary>
public readonly struct TraceRow
{
    public readonly string Label;
    public readonly string Effect;
    public readonly double Total;
    public readonly object Icon;

    public TraceRow(string label, string effect, double total, object icon = null)
    {
        Label = label;
        Effect = effect;
        Total = total;
        Icon = icon;
    }
}

/// <summary>
/// Everything that happened to one hit, from the attacker's stats to the target's defenses, recorded by
/// DamageTracePatch. Explain() turns it into rows whose factors multiply up to the dealt damage. Plain logic.
/// </summary>
public sealed class DamageTrace
{
    public string Origin = "";          // what dealt the hit: "Wind Spirit Sylphid", "Warlord", "Oberon attack"...
    public object OriginIcon;
    public DamageState Start;           // as created by Stat.GetDamage, before any effect
    public readonly List<(string label, double value)> StatParts = new(); // "Magic atk" 3.55, "Attack damage" 1.1
    public readonly List<TraceStep> Steps = new();
    public bool Critical;
    public double Dealt;

    public void Add(string label, string stage, DamageState after, object icon = null)
    {
        var before = Steps.Count > 0 ? Steps[Steps.Count - 1].After : Start;
        if (!before.SameAs(after))
            Steps.Add(new TraceStep { Label = label, Stage = stage, After = after, Icon = icon });
    }

    public List<TraceRow> Explain()
    {
        var rows = new List<TraceRow>();
        if (Start.Fixed)
        {
            rows.Add(new TraceRow("Fixed damage", "", Start.Amount(false), OriginIcon));
        }
        else
        {
            rows.Add(new TraceRow("Base damage", "", Start.Base, OriginIcon));
            var raw = Start;
            raw.Multiplier = 1;
            raw.PercentMultiplier = 1;
            raw.ExtraFixed = 0;
            double total = raw.Amount(false);

            // Attack stats (Stat.GetDamage): the category bonuses are added, then x attack damage.
            if (Math.Abs(Start.Multiplier - 1) > 1e-9)
            {
                total *= Start.Multiplier;
                rows.Add(new TraceRow(StatLabel(), X(Start.Multiplier), total));
            }
            if (Math.Abs(Start.PercentMultiplier - 1) > 1e-9)
            {
                total *= Start.PercentMultiplier;
                rows.Add(new TraceRow("Damage bonus", X(Start.PercentMultiplier), total));
            }
            if (Critical)
            {
                total *= Start.CritMultiplier;
                rows.Add(new TraceRow("Critical hit", X(Start.CritMultiplier), total));
            }
            if (Start.ExtraFixed != 0)
            {
                total += Start.ExtraFixed;
                rows.Add(new TraceRow("Extra fixed damage", Plus(Start.ExtraFixed), total));
            }
        }

        var previous = Start;
        foreach (var step in Steps)
        {
            double before = previous.Amount(Critical), after = step.After.Amount(Critical);
            string effect = step.After.Null && !previous.Null ? "blocked"
                : before > 0 && Math.Abs(step.After.ExtraFixed - previous.ExtraFixed) < 1e-9 ? X(after / before)
                : Plus(after - before);
            rows.Add(new TraceRow(step.Label + (step.Stage == "take" ? " (on target)" : ""), effect, after, step.Icon));
            previous = step.After;
        }

        // The game rounds up once, at the very end (Damage.amount).
        double exact = previous.Amount(Critical);
        double computed = Math.Ceiling(exact - 1e-9);
        if (Dealt > 0 && Math.Abs(computed - Dealt) >= 1)
            rows.Add(new TraceRow(Dealt < computed ? "Dealt (shield or overkill)" : "Dealt", "", Dealt));
        else if (Math.Abs(exact - computed) > 1e-6)
            rows.Add(new TraceRow("Rounded up", "", computed));
        return rows;
    }

    private string StatLabel()
    {
        if (StatParts.Count == 0)
            return "Attack stats";
        var parts = new List<string>();
        foreach (var (label, value) in StatParts)
            parts.Add($"{label} {Pct(value)}");
        return string.Join(", ", parts);
    }

    public static string X(double factor) => "x" + factor.ToString(factor >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture);

    private static string Plus(double v) => (v >= 0 ? "+" : "") + v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
