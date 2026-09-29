using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DamageInsight.Describe;

/// <summary>
/// Post-processing of item breakdowns (see GearAnalyzer.Refiners): says when each hit happens
/// ("On critical hit", "Every 7 s", "On hit (30% chance, 2 s cooldown)"). Plain logic, no Unity.
/// </summary>
/// <remarks>
/// A hit belongs to the trigger of the nearest component above it (by hierarchy path) that owns one:
/// an OperationByTrigger-style component (_triggerComponent + _ability), a trigger attacher (_trigger),
/// or a Spirit (_attackInterval).
/// </remarks>
public static class ItemRefiner
{
    // Game enums, in declaration order (the scan stores filters as bool arrays in this order).
    private static readonly string[] MotionTypes = { "basic attacks", "skills", "items", "quintessences", "statuses", "dashes", "swaps", "dark abilities", "other" };
    private static readonly string[] ActionTypes = { "dash", "basic attack", "jump attack", "jump", "skill", "swap", "custom" };
    private static readonly string[] StatusKinds = { "Stun", "Freeze", "Burn", "Wound", "Poison", "Unmoving" };

    public static void Refine(GearDoc doc, Breakdown b)
    {
        if (b.Category != "items")
            return;
        FindUpgrades(doc, b);
        var section = b.Find("Effect");
        if (section == null)
            return;

        var owners = doc.Components.Where(c => c.Path != null && (c.Has("_triggerComponent") || c.Has("_attackInterval")
                                                                 || c.Is("TriggerAbilityAttacher") || HasOwnConditions(c.Child("_ability"))))
            .OrderByDescending(c => c.Path.Length)
            .ToList();

        var triggers = new List<string>();
        foreach (var hit in section.Steps.SelectMany(s => s.Hits))
        {
            var owner = owners.FirstOrDefault(o => IsAncestor(o.Path, hit.OwnerPath));
            string text = owner.IsNull ? "" : Describe(owner);
            hit.Note = text;
            triggers.Add(text);
        }

        // One trigger for everything: show it once above the hits instead of after each one.
        if (triggers.Count > 0 && triggers.All(t => t == triggers[0]) && triggers[0].Length > 0)
        {
            section.Trigger = triggers[0];
            foreach (var hit in section.Steps.SelectMany(s => s.Hits))
                hit.Note = "";
        }
    }

    /// <summary>"Change" operations turn this item into another one; note which, and whether owning an item triggers it.</summary>
    private static void FindUpgrades(GearDoc doc, Breakdown b)
    {
        foreach (var change in doc.Components.Where(c => c.Is("Change") && c.Has("_itemToChange")))
        {
            string target = change.Child("_itemToChange").GameObject;
            if (string.IsNullOrEmpty(target) || b.Upgrades.Any(u => u.TargetName == target))
                continue;
            var own = doc.Components.FirstOrDefault(c => c.Is("OwnItemAbilityAttacher") && c.Bool("_onHas") && IsAncestor(c.Path, change.Path));
            b.Upgrades.Add(new Upgrade { TargetName = target, OwnItemName = own.IsNull ? "" : own.Child("_item").GameObject ?? "" });
        }
    }

    private static bool IsAncestor(string ancestor, string path) =>
        !string.IsNullOrEmpty(path) && (path == ancestor || path.StartsWith(ancestor + "/"));

    /// <summary>Abilities like AdditionalHit keep their hit conditions (crit only, cooldown, filters) themselves.</summary>
    private static bool HasOwnConditions(Node ability) =>
        !ability.IsNull && (ability.Has("_needCritical") || (ability.Has("_attackTypes") && ability.Has("_cooldownTime")));

    /// <summary>A short sentence for the trigger owned by <paramref name="owner"/>.</summary>
    public static string Describe(Node owner)
    {
        if (owner.Has("_attackInterval"))
            return $"Every {Num(owner.Num("_attackInterval"))} s (faster with spirit cooldown speed)";

        var own = owner.Child("_ability");
        if (!owner.Has("_triggerComponent") && HasOwnConditions(own))
        {
            // Same wording as an OnGaveDamage trigger, read from the ability itself.
            string hit = own.Bool("_needCritical") ? "On critical hit" : "On hit";
            string with = Filter(own.Child("_attackTypes"), MotionTypes);
            if (with.Length > 0)
                hit += " with " + with;
            double cd = own.Num("_cooldownTime");
            return cd > 0 ? $"{hit} ({Num(cd)} s cooldown)" : hit;
        }

        var trigger = owner.Is("TriggerAbilityAttacher")
            ? owner.Child("_trigger").Child("_trigger")
            : owner.Child("_triggerComponent").Child("_trigger");
        if (trigger.IsNull)
            return "";

        string when = When(trigger);
        var details = new List<string>();
        double chance = trigger.Num("_possibility", 100);
        if (chance < 100)
            details.Add($"{Num(chance)}% chance");

        // Cooldowns can sit on the trigger or on the ability that runs the operations.
        var ability = owner.Child("_ability");
        double cooldown = trigger.ShortType == "OnUpdate" ? 0 : trigger.Num("_cooldownTime");
        if (!ability.IsNull)
            cooldown = System.Math.Max(cooldown, ability.Num("_cooldownTime"));
        if (trigger.ShortType == "OnUpdate" && trigger.Num("_cooldownTime") <= 0 && cooldown > 0)
        {
            // A trigger that checks every frame, limited by a cooldown: effectively a fixed interval.
            when = $"Every {Num(cooldown)} s";
            cooldown = 0;
        }
        if (cooldown > 0)
            details.Add($"{Num(cooldown)} s cooldown");
        int every = (int)ability.Num("_triggerCount");
        if (every > 0)
            details.Add($"every {Ordinal(every + 1)} time");

        if (when.Length == 0)
            return details.Count > 0 ? string.Join(", ", details) : "";
        return details.Count > 0 ? $"{when} ({string.Join(", ", details)})" : when;
    }

    private static string When(Node t)
    {
        switch (t.ShortType)
        {
            case "OnGaveDamage":
            {
                string hit = t.Bool("_needCritical") ? "On critical hit" : "On hit";
                if (t.Bool("_backOnly"))
                    hit += " from behind";
                string with = Filter(t.Child("_attackTypes"), MotionTypes);
                return with.Length > 0 ? $"{hit} with {with}" : hit;
            }
            case "OnUpdate":
            {
                double interval = t.Num("_cooldownTime");
                return interval > 0 ? $"Every {Num(interval)} s" : "Continuously";
            }
            case "OnAction":
            case "OnChargeAction":
            {
                string types = Filter(t.Child("_types"), ActionTypes);
                if (types == "basic attack and jump attack")
                    types = "basic attack";
                bool end = t.Str("_timing") == "End";
                if (t.ShortType == "OnChargeAction")
                    return end ? "After charging" : "When charging";
                if (types == "swap")
                    return end ? "After swapping" : "On swap";
                if (types.Length == 0)
                    return end ? "After an action" : "On any action";
                return end ? $"After a {types}" : $"On {types}";
            }
            case "OnApplyStatus":
                return $"When you apply {t.Str("_kind") ?? "a status"}";
            case "OnGaveDamageStatusTarget":
            {
                string statuses = Filter(t.Child("_characterStatusKinds"), StatusKinds, " or ");
                return statuses.Length > 0 ? $"On hitting a {statuses}ed enemy" : "On hitting an enemy with a status";
            }
            case "OnStatusTargetKilled":
                return "On killing an enemy with a status";
            case "OnKilled":
            {
                int kills = (int)t.Num("_killCount", 1);
                return kills > 1 ? $"Every {kills} kills" : "On kill";
            }
            case "OnTookDamage":
            case "OnTakeDamage":
                return "When you take damage";
            case "OnSwap": return "On swap";
            case "OnDashEvade": return "On evading with a dash";
            case "OnGrounded": return "On landing";
            case "OnBackAttack": return "On attacking from behind";
            case "OnEnterMap": return "On entering a room";
            case "OnGaugeFull": return "When the gauge is full";
            case "OnHealed": return "When healed";
            case "OnHealthValue":
            case "OnHealthChanged":
                return $"When HP is {(t.Str("_compareType") == "LessThan" ? "below" : "above")} {Num(t.Num("_amount"))}{(t.Str("_healthType") == "Percent" ? "%" : "")}";
            case "OnUseEssence":
            case "OnUseEssenceComponnet":
                return "When using a quintessence";
            default:
                return "";
        }
    }

    /// <summary>Names of the enabled entries of a bool-array filter, if it's a narrow selection (1–2 entries).</summary>
    private static string Filter(Node boolArray, string[] names, string separator = " and ")
    {
        if (boolArray.IsNull || !(boolArray.Raw("_array") is List<object> values))
            return "";
        var on = values.Select((v, i) => (v is bool b && b, i)).Where(x => x.Item1 && x.i < names.Length).Select(x => names[x.i]).ToList();
        return on.Count is > 0 and <= 2 && on.Count < values.Count ? string.Join(separator, on) : "";
    }

    private static string Ordinal(int n) => n switch { 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    private static string Num(double v) => v.ToString(System.Math.Abs(v - System.Math.Round(v)) < 1e-6 ? "0" : "0.##", CultureInfo.InvariantCulture);
}
