using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DamageInsight.Lang;

namespace DamageInsight.Describe;

/// <summary>
/// Post-processing of skull breakdowns (see GearAnalyzer.Refiners): when a skull has several actions of
/// one kind (e.g. a ground and an air combo, or "fire upward"), give each a readable title from its action
/// name plus its air/ground and direction constraints. Plain logic, no Unity.
/// </summary>
public static class WeaponRefiner
{
    public static void Refine(GearDoc doc, Breakdown b)
    {
        if (b.Category != "weapons")
            return;

        NoteStackingDamage(doc, b);

        foreach (var group in b.Sections.Where(s => s.Kind != "Skill" && s.Path.Length > 0).GroupBy(s => s.Kind))
        {
            if (group.Count() < 2)
                continue;
            foreach (var section in group)
                section.Title = Label(doc, section.Path);
        }
    }

    /// <summary>
    /// ModifyDamageStackable: hits with a given key deal base x (1 + percent x stacks). The Warlord gains a stack
    /// for every hit he takes while a skill runs (OperationOnGuardMotion, max _operationOnGuardMaxCount).
    /// Adds a note to every section whose hits carry that key.
    /// </summary>
    private static void NoteStackingDamage(GearDoc doc, Breakdown b)
    {
        foreach (var stackable in doc.Components.Where(c => c.Is("ModifyDamageStackableComponent")))
        {
            var ability = stackable.Child("_ability");
            string key = ability.Str("_attackKey") ?? "";
            double percent = ability.Num("_damagePercentByStack");
            if (key.Length == 0 || percent <= 0)
                continue;

            var guard = doc.Components.FirstOrDefault(c => c.Is("OperationOnGuardMotionComponent") && stackable.Path.StartsWith(c.Path + "/"));
            int maxStacks = guard.IsNull ? (int)ability.Num("_maxStack") : (int)guard.Child("_ability").Num("_operationOnGuardMaxCount");
            string how = guard.IsNull ? Loc.T("per stack") : Loc.T("per hit you take while it runs");
            string cap = maxStacks > 0
                ? " " + Loc.F("(max {0} = +{1}%, x{2} damage)", maxStacks, Fmt(percent * maxStacks * 100, "0"), Fmt(1 + percent * maxStacks, "0.#"))
                : "";
            string note = Loc.F("+{0}% {1}", Fmt(percent * 100, "0"), how) + cap;

            foreach (var section in b.Sections.Where(s => s.Steps.SelectMany(st => st.Hits).Any(h => h.Key == key)))
                if (!section.Notes.Contains(note))
                    section.Notes.Add(note);
        }
    }

    /// <summary>"Equipped/BasicAttack_Pistol/Fire_Upward" + constraints → "Fire upward (ground)".</summary>
    public static string Label(GearDoc doc, string actionPath)
    {
        string name = Humanize(actionPath.Split('/').Last());
        var constraints = doc.Components.Where(c => c.Path != null && c.Path.StartsWith(actionPath + "/") && c.Path.Contains("Constraints")).ToList();

        string state = constraints.FirstOrDefault(c => c.Is("AirAndGroundConstraint")).Str("_state");
        string direction = constraints.FirstOrDefault(c => c.Is("DirectionConstraint")).Str("_direcion"); // sic, the game's field name
        var extras = new[] { state, direction }
            .Where(x => !string.IsNullOrEmpty(x) && name.IndexOf(x, System.StringComparison.OrdinalIgnoreCase) < 0)
            .Select(x => x.ToLowerInvariant() == "air" ? Loc.T("in the air") : x.ToLowerInvariant() == "ground" ? Loc.T("on the ground") : Loc.Name(x.ToLowerInvariant()));
        string extra = string.Join(", ", extras);
        return extra.Length > 0 ? $"{Loc.Name(name)} ({extra})" : Loc.Name(name);
    }

    private static string Fmt(double v, string format) => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"PowerbombJumpAttack" / "Fire_Upward" / "ComboAttack(3)" → "Powerbomb jump attack" / "Fire upward" / "Combo attack".</summary>
    public static string Humanize(string name)
    {
        name = Regex.Replace(name, @"\s*\(\d+\)", "").Replace('_', ' ');
        name = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " "); // split camel case
        var words = name.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            bool acronym = w.Length > 1 && w.All(char.IsUpper);
            if (i > 0)
                sb.Append(' ').Append(acronym ? w : w.ToLowerInvariant());
            else
                sb.Append(char.ToUpperInvariant(w[0])).Append(acronym ? w.Substring(1) : w.Substring(1).ToLowerInvariant());
        }
        return sb.ToString();
    }
}
