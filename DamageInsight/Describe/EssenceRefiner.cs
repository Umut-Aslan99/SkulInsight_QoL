using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DamageInsight.Describe;

/// <summary>
/// Post-processing of essences breakdowns (see GearAnalyzer.Refiners). Plain logic, no Unity.
///
/// The generic analyzer misses several essences because their damage lives behind shapes it doesn't
/// walk: a summoned minion/turret's own Action (any node whose type ends in "Action" is skipped by the
/// core Walker, since normally actions are handled by the weapon-specific code), a temporary weapon
/// transformation, or a timed stat buff/debuff instead of a hit. For those we can't recover trustworthy
/// hit numbers (a summoned minion's "attackDamage" in the scan is a scanner placeholder - it is
/// identically 7-13 for five unrelated essences, so it is not real per-minion data), so we add a
/// plain-language Note instead of inventing a Hit.
/// </summary>
public static class EssenceRefiner
{
    public static void Refine(GearDoc doc, Breakdown b)
    {
        if (b.Category != "essences")
            return;
        var section = b.Find("Active");
        if (section == null)
            return;

        NoteCharges(doc, section);
        NoteSummons(doc, section);
        NotePolymorph(doc, section);
        NoteStatEffects(doc, section);
        NoteCompanion(doc, section);
    }

    /// <summary>Cooldowns with more than one charge (Quintessence._cooldown._maxStack): not shown anywhere else.</summary>
    private static void NoteCharges(GearDoc doc, Section section)
    {
        var quintessence = doc.Components.FirstOrDefault(c => c.Is("Quintessence"));
        if (quintessence.IsNull)
            return;
        int maxStack = (int)quintessence.Child("_cooldown").Num("_maxStack", 1);
        if (maxStack > 1)
            section.Notes.Add($"Charges: {maxStack}");
    }

    /// <summary>SummonMinion / SummonDwarfTurret: describe count and lifetime; the minion's own damage isn't reliable data (see class doc).</summary>
    private static void NoteSummons(GearDoc doc, Section section)
    {
        foreach (var op in doc.Components.Where(c => c.Is("SummonMinion")))
            AddMinionNote(op.Child("_minion"), section);

        foreach (var op in doc.Components.Where(c => c.Is("SummonDwarfTurret")))
        {
            var turret = op.Child("_dwarfTurret");
            double interval = turret.Num("_attackInterval");
            string extra = interval > 0 ? FormattableString.Invariant($"; attacks every {interval:0.#} s") : "";
            AddMinionNote(turret.Child("_minion"), section, extra);
        }
    }

    private static void AddMinionNote(Node minion, Section section, string extra = "")
    {
        if (minion.IsNull)
            return;
        string name = minion.Child("_character").Str("$character") ?? "a minion";
        var setting = minion.Child("_defaultSetting");
        int maxCount = (int)setting.Num("maxCount", 1);
        double lifeTime = setting.Num("lifeTime");
        string count = maxCount >= int.MaxValue ? "no limit" : $"up to {maxCount}";
        string life = lifeTime >= 1e6 ? "lasts until you unequip the essence" : FormattableString.Invariant($"lasts {lifeTime:0.#} s");
        string note = $"Summons {name} ({count}, {life}{extra}). Its attack damage isn't in this data.";
        if (!section.Notes.Contains(note))
            section.Notes.Add(note);
    }

    /// <summary>StartWeaponPolymorph: temporarily turns the weapon into a different one with its own moveset.</summary>
    private static void NotePolymorph(GearDoc doc, Section section)
    {
        foreach (var op in doc.Components.Where(c => c.Is("StartWeaponPolymorph")))
        {
            string name = op.Child("_polymorphWeapon").Str("$go") ?? "a different weapon";
            string note = $"Transforms your weapon into {name} with its own moveset; reverts when you swap weapons.";
            if (!section.Notes.Contains(note))
                section.Notes.Add(note);
        }
    }

    /// <summary>
    /// StatBonus / FaceBug abilities: a timed buff or debuff (self or applied to a hit target) instead of a hit.
    /// The "value" is the raw data contribution for that Stat.Kind - shown as-is (×value), not converted to a
    /// percentage, since the sign/base convention for a single contribution isn't confirmed against decompiled code.
    /// </summary>
    private static void NoteStatEffects(GearDoc doc, Section section)
    {
        foreach (var ability in doc.Components.Where(c => c.Is("StatBonus", "FaceBug")))
        {
            if (!ability.Has("_stat") || !ability.Has("_duration"))
                continue;
            var values = ability.Child("_stat").List("values")
                .Select(v => FormattableString.Invariant($"{SpaceCase(v.Str("$kind"))} x{v.Num("value"):0.##}"))
                .ToList();
            if (values.Count == 0)
                continue;
            double duration = ability.Num("_duration");
            string note = FormattableString.Invariant($"Applies for {duration:0.#} s: {string.Join(", ", values)} (raw data values).");
            if (!section.Notes.Contains(note))
                section.Notes.Add(note);
        }
    }

    /// <summary>
    /// RunAction essences that summon a full companion character (Troll, Wisp): the companion has a real
    /// AttackDamage component, but its actual attack is AI-driven and not serialized as a HitInfo here.
    /// </summary>
    private static void NoteCompanion(GearDoc doc, Section section)
    {
        if (section.HasHits)
            return;
        foreach (var run in doc.Components.Where(c => c.Is("RunAction")))
        {
            var character = run.Child("_character");
            if (character.IsNull)
                continue;
            var attackDamage = doc.Components.FirstOrDefault(c => c.Is("AttackDamage") && c.Path == character.Path);
            if (attackDamage.IsNull)
                continue;
            string note = $"Summons a companion (base attack {attackDamage.Num("_minAttackDamage"):0}–{attackDamage.Num("_maxAttackDamage"):0}) " +
                           "that fights using its own AI; its exact hits aren't in this data.";
            if (!section.Notes.Contains(note))
                section.Notes.Add(note);
        }
    }

    private static string SpaceCase(string s) =>
        string.IsNullOrEmpty(s) ? s : Regex.Replace(s, "(?<!^)([A-Z])", " $1");
}
