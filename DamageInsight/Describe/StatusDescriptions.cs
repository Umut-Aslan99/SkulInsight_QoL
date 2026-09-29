using System;
using System.Collections.Generic;
using System.Globalization;

namespace DamageInsight.Describe;

/// <summary>
/// Texts for the status effects (poison, burn, bleed, freeze, stun), from the game's status settings
/// (CharacterStatusSetting, serialized like a gear scan) and the player's stats. The formulas follow
/// Characters.Abilities.Statuses.*. Plain logic, no Unity.
/// </summary>
public static class StatusDescriptions
{
    /// <summary>
    /// A description of <paramref name="status"/> ("Poison", "Burn", "Bleed", "Freeze", "Stun"),
    /// or "" if the settings don't have it.
    /// </summary>
    public static string Describe(Node settings, string status, StatSnapshot stats)
    {
        switch (status)
        {
            case "Poison":
            {
                var p = settings.Child("_poison");
                if (p.IsNull) return "";
                // Poison.tickInterval: the setting minus the attacker's PoisonTickFrequency stat.
                double interval = Math.Max(0.05, p.Num("_tickFrequency") - stats.PoisonTickReduction);
                double duration = p.Num("_duration");
                int ticks = (int)Math.Floor(duration / interval + 1e-6);
                var tick = TickHit(p.Child("_hitInfo"), p.Num("_baseTickDamage"), neverCrits: true);
                var (min, _) = DescriptionFormatter.FinalRange(tick, stats);
                return $"Poison: {DescriptionFormatter.Amount(tick, stats)} every {Num(interval)} s for {Num(duration)} s"
                       + Details(tick, stats, $"~{ticks} ticks = {Num(min * ticks)}");
            }
            case "Burn":
            {
                var b = settings.Child("_burn");
                if (b.IsNull) return "";
                // Ticks on the target, plus "ember" splash damage around it; both splash damage
                // and radius scale with the attacker's EmberDamage stat.
                var target = TickHit(b.Child("_hitInfo"), b.Num("_baseTargetTickDamage"), neverCrits: true);
                var splash = TickHit(b.Child("_rangeHitInfo"), b.Num("_baseRangeTickDamage") * stats.EmberDamage, neverCrits: true);
                double radius = b.Num("_rangeRadius") * stats.EmberDamage;
                return $"Burn: {DescriptionFormatter.Amount(target, stats)} every {Num(b.Num("_tickInterval"))} s for {Num(b.Num("_duration"))} s"
                       + Details(target, stats)
                       + "\n"
                       + $"Burn splash (ember): {DescriptionFormatter.Amount(splash, stats)} per tick to enemies within {Num(radius)} m of the burning enemy"
                       + Details(splash, stats);
            }
            case "Bleed":
            {
                var bl = settings.Child("_bleed");
                if (bl.IsNull) return "";
                // Wound marks the enemy; the second application bleeds. Bleed can crit and scales with BleedDamage.
                // Wound.GiveDamage: percentMultiplier x BleedDamage, x superBleedValue on a super bleed; it only
                // crits with Excessive Bleeding 4 (canBleedCritical), using the normal crit chance and damage.
                var hit = TickHit(bl.Child("_hitInfo"), bl.Num("_baseDamage") * stats.BleedDamage, neverCrits: !stats.BleedCanCrit);
                double super = bl.Num("_superBleedValue", 1);
                var superHit = TickHit(bl.Child("_hitInfo"), bl.Num("_baseDamage") * stats.BleedDamage * super, neverCrits: !stats.BleedCanCrit);
                string text = $"Bleed (on the 2nd application): {DescriptionFormatter.Amount(hit, stats)}"
                              + Details(hit, stats, Math.Abs(stats.BleedDamage - 1) > 1e-6 ? $"incl. x{Num(stats.BleedDamage)} bleed dmg" : "");
                if (stats.BleedCanCrit)
                    text += $" · crit {Crit(hit, stats)}";
                text += $"\nSuper bleed (x{Num(super)}): {DescriptionFormatter.Amount(superHit, stats)}";
                if (stats.BleedCanCrit)
                    text += $" · crit {Crit(superHit, stats)}";
                return text;
            }
            case "Freeze":
            {
                var f = settings.Child("_freeze");
                return f.IsNull ? "" : $"Freeze: {Num(f.Num("_duration") + stats.FreezeBonus)} s";
            }
            case "Stun":
            {
                var s = settings.Child("_stun");
                return s.IsNull ? "" : $"Stun: {Num(s.Num("_duration") + stats.StunBonus)} s";
            }
            default:
                return "";
        }
    }

    /// <summary>The crit amount of a hit: the damage x the crit damage multiplier, rounded up like Damage.amount.</summary>
    private static string Crit(Hit hit, StatSnapshot stats)
    {
        // Damage.amount rounds once, after the crit multiplier.
        hit.MultMin *= stats.CritDamage;
        hit.MultMax *= stats.CritDamage;
        var (min, max) = DescriptionFormatter.FinalRange(hit, stats);
        hit.MultMin /= stats.CritDamage;
        hit.MultMax /= stats.CritDamage;
        return $"{DescriptionFormatter.Range(min, max)} (x{Num(stats.CritDamage)} crit dmg)";
    }

    /// <summary>The status an inscription is about, by the game's inscription key.</summary>
    public static string StatusOfInscription(string inscriptionKey) => inscriptionKey switch
    {
        "Poisoning" => "Poison",
        "Arson" => "Burn",
        "ExcessiveBleeding" => "Bleed",
        "AbsoluteZero" => "Freeze",
        "Dizziness" => "Stun",
        _ => "",
    };

    /// <summary>The dim "(breakdown · extra · can't crit)" suffix.</summary>
    private static string Details(Hit hit, StatSnapshot stats, string extra = "")
    {
        var parts = new List<string>();
        string breakdown = DescriptionFormatter.Breakdown(hit, stats);
        if (breakdown.Length > 0) parts.Add(breakdown);
        if (extra.Length > 0) parts.Add(extra);
        parts.AddRange(DescriptionFormatter.Notes(hit));
        return parts.Count == 0 ? "" : $" <color={DescriptionFormatter.DimColor}>({string.Join(" · ", parts)})</color>";
    }

    private static Hit TickHit(Node hitInfo, double baseDamage, bool neverCrits) => new()
    {
        BaseMin = baseDamage,
        BaseMax = baseDamage,
        MultMin = hitInfo.Num("_damageMultiplier", 1),
        MultMax = hitInfo.Num("_damageMultiplier", 1),
        Attribute = hitInfo.Str("_attribute") ?? "Physical",
        MotionType = hitInfo.Str("_motionType") ?? "Status",
        AttackType = hitInfo.Str("_type") ?? "Additional",
        NeverCrits = neverCrits,
    };

    private static string Num(double v) => v.ToString(Math.Abs(v - Math.Round(v)) < 1e-6 ? "0" : "0.##", CultureInfo.InvariantCulture);
}
