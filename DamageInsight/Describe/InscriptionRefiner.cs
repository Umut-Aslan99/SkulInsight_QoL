using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.Describe;

/// <summary>
/// Post-processing of inscription breakdowns (see GearAnalyzer.AnalyzeInscription): assigns every
/// damaging part to the step that unlocks it, or to the "true form" (super) text, and gives it a title.
/// Plain logic, no Unity.
/// </summary>
public static class InscriptionRefiner
{
    /// <summary>(inscription, field) → (step, title). Steps count from 1, like Inscription.GetDescription(step).</summary>
    private static readonly Dictionary<(string, string), (int step, string title)> Known = new()
    {
        [("Brawl", "_operations")] = (1, "Shockwave"),
        [("Brawl", "_enhanceOperations")] = (2, "Enhanced shockwave"),
        [("Revenge", "_revengeAbility2")] = (1, "Revenge attack"),
        [("Revenge", "_revengeAbility4")] = (2, "Revenge attack"),
        [("Arms", "_additionalHit/_additionalAttackOnGround")] = (2, "Armament swing (ground)"),
        [("Arms", "_additionalHit/_additionalAttackOnAir")] = (2, "Armament swing (air)"),
        [("Arms", "_additionalHit/_additionalEnhancedAttackOnGround")] = (3, "Enhanced swing (ground)"),
        [("Arms", "_additionalHit/_additionalEnhancedAttackOnAir")] = (3, "Enhanced swing (air)"),
        [("Arms", "_additionalHit/_superAttackOnGround")] = (Section.SuperStep, "True form swing (ground)"),
        [("Arms", "_additionalHit/_superAttackOnAir")] = (Section.SuperStep, "True form swing (air)"),
        [("Artifact", "_buff")] = (2, "Meteors"),
        [("Rapidity", "_rapidityAttack")] = (2, "Combo"),
        [("Arson", "_deathrattle")] = (3, "Firestorm"),
    };

    public static void Refine(GearDoc doc, Breakdown b)
    {
        if (b.Category != "inscriptions")
            return;

        foreach (var section in b.Sections.Where(s => s.Kind == "Inscription"))
        {
            if (Known.TryGetValue((b.Name, section.Key), out var known))
            {
                section.Step = known.step;
                section.Title = known.title;
            }
            else if (section.Key.ToLowerInvariant().Contains("super"))
            {
                section.Step = Section.SuperStep;
            }
            else
            {
                section.Step = 1;
            }
        }

        // Arms: every swing is several armaments at once; one compact line each, and when the enhanced one comes.
        if (b.Name == "Arms")
        {
            var hit = doc.Components.FirstOrDefault(c => c.Path == "").Child("_additionalHit");
            int cycle = (int)hit.Num("_cycle");
            foreach (var section in b.Sections.Where(s => s.Key.StartsWith("_additionalHit/")))
            {
                section.Compact = true;
                if (section.Key.Contains("Enhanced") && cycle > 0)
                    section.Trigger = $"Every {Ordinal(cycle)} swing";
            }
        }

        if (b.Name == "Strike")
            AddStrikeNotes(doc, b);

        // Brawl: the enhanced shockwave replaces every Nth one.
        if (b.Name == "Brawl")
        {
            int cycle = (int)doc.Components.FirstOrDefault(c => c.Path == "").Child("_ability").Num("_cycle");
            var enhanced = b.Sections.FirstOrDefault(s => s.Key == "_enhanceOperations");
            if (enhanced != null && cycle > 0)
                enhanced.Trigger = $"Every {Ordinal(cycle)} shockwave";
        }
    }

    /// <summary>
    /// Strike 4 (Characters.Gear.Synergy.Inscriptions.Strike.OnGiveDamage): each hit has a chance to get extra
    /// crit damage, added to the hit's crit multiplier (x1.5 base crit + bonuses). It only matters if the hit
    /// crits; the crit roll itself is unchanged. The true form adds a second, rarer and stronger roll.
    /// </summary>
    private static void AddStrikeNotes(GearDoc doc, Breakdown b)
    {
        var strike = doc.Components.FirstOrDefault(c => c.Is("Strike"));
        if (strike.IsNull)
            return;
        double chance = strike.Num("_maxStatBonusChance");
        double bonus = strike.Num("_criticalDamageMultiplier");
        double superChance = strike.Num("_superAbilityChance");
        double superBonus = strike.Num("_superAbilityCriticalDamageMultiplier");
        const double crit = 1.7; // base x1.5 + Strike 2's +20%
        if (chance > 0 && bonus > 0)
            b.Sections.Add(new Section
            {
                Kind = "Inscription", Key = "_criticalDamageMultiplier", Step = 2, Title = "Deadlier strikes",
                Notes = { FormattableString.Invariant($"{chance * 100:0}% of your hits: +{bonus * 100:0}% crit damage if they crit (x{crit:0.#} becomes x{crit + bonus:0.#})") },
            });
        if (superChance > 0 && superBonus > 0)
            b.Sections.Add(new Section
            {
                Kind = "Inscription", Key = "_superAbilityCriticalDamageMultiplier", Step = Section.SuperStep, Title = "Deadlier strikes",
                Notes = { FormattableString.Invariant($"another {superChance * 100:0}% of your hits: +{superBonus * 100:0}% crit damage if they crit (x{crit:0.#} becomes x{crit + superBonus:0.#})") },
            });
    }

    private static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
}
