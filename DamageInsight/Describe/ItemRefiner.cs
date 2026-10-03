using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DamageInsight.Lang;

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
    private static readonly string[] MotionTypes =
    {
        Loc.N("basic attacks"), Loc.N("skills"), Loc.N("items"), Loc.N("quintessences"), Loc.N("statuses"), Loc.N("dashes"),
        Loc.N("swaps"), Loc.N("dark abilities"), Loc.N("other"),
    };
    private static readonly string[] ActionTypes =
    {
        Loc.N("dash"), Loc.N("basic attack"), Loc.N("jump attack"), Loc.N("jump"), Loc.N("skill"), Loc.N("swap"), Loc.N("custom"),
    };
    private static readonly string[] StatusKinds = { "Stun", "Freeze", "Burn", "Wound", "Poison", "Unmoving" };
    // "On hitting a stunned enemy": the status as an adjective, in StatusKinds order.
    private static readonly string[] StatusAdjectives =
    {
        Loc.N("stunned"), Loc.N("frozen"), Loc.N("burning"), Loc.N("wounded"), Loc.N("poisoned"), Loc.N("rooted"),
    };
    private static readonly string[] StatusNames =
    {
        Loc.N("stun"), Loc.N("freeze"), Loc.N("burn"), Loc.N("wound"), Loc.N("poison"), Loc.N("root"),
    };

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
            return Loc.F("Every {0} s (faster with spirit cooldown speed)", Num(owner.Num("_attackInterval")));

        var own = owner.Child("_ability");
        if (!owner.Has("_triggerComponent") && HasOwnConditions(own))
        {
            // Same wording as an OnGaveDamage trigger, read from the ability itself.
            string hit = OnHit(own.Bool("_needCritical"), false, Filter(own.Child("_attackTypes"), MotionTypes));
            double cd = own.Num("_cooldownTime");
            return cd > 0 ? $"{hit} ({Loc.F("{0} s cooldown", Num(cd))})" : hit;
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
            details.Add(Loc.F("{0}% chance", Num(chance)));

        // Cooldowns can sit on the trigger or on the ability that runs the operations.
        var ability = owner.Child("_ability");
        double cooldown = trigger.ShortType == "OnUpdate" ? 0 : trigger.Num("_cooldownTime");
        if (!ability.IsNull)
            cooldown = System.Math.Max(cooldown, ability.Num("_cooldownTime"));
        if (trigger.ShortType == "OnUpdate" && trigger.Num("_cooldownTime") <= 0 && cooldown > 0)
        {
            // A trigger that checks every frame, limited by a cooldown: effectively a fixed interval.
            when = Loc.F("Every {0} s", Num(cooldown));
            cooldown = 0;
        }
        if (cooldown > 0)
            details.Add(Loc.F("{0} s cooldown", Num(cooldown)));
        int every = (int)ability.Num("_triggerCount");
        if (every > 0)
            details.Add(Loc.F("every {0} time", Loc.Ordinal(every + 1), every + 1));

        if (when.Length == 0)
            return details.Count > 0 ? string.Join(", ", details) : "";
        return details.Count > 0 ? $"{when} ({string.Join(", ", details)})" : when;
    }

    /// <summary>"On hit", "On critical hit from behind with skills"...</summary>
    private static string OnHit(bool critical, bool fromBehind, List<string> with)
    {
        string types = Loc.Join(with.Select(Loc.T));
        if (types.Length == 0)
            return critical
                ? fromBehind ? Loc.T("On critical hit from behind") : Loc.T("On critical hit")
                : fromBehind ? Loc.T("On hit from behind") : Loc.T("On hit");
        return critical
            ? fromBehind ? Loc.F("On critical hit from behind with {0}", types) : Loc.F("On critical hit with {0}", types)
            : fromBehind ? Loc.F("On hit from behind with {0}", types) : Loc.F("On hit with {0}", types);
    }

    private static string When(Node t)
    {
        switch (t.ShortType)
        {
            case "OnGaveDamage":
                return OnHit(t.Bool("_needCritical"), t.Bool("_backOnly"), Filter(t.Child("_attackTypes"), MotionTypes));
            case "OnUpdate":
            {
                double interval = t.Num("_cooldownTime");
                return interval > 0 ? Loc.F("Every {0} s", Num(interval)) : Loc.T("Continuously");
            }
            case "OnAction":
            case "OnChargeAction":
            {
                var types = Filter(t.Child("_types"), ActionTypes);
                if (types.SequenceEqual(new[] { "basic attack", "jump attack" }))
                    types = new List<string> { "basic attack" };
                bool end = t.Str("_timing") == "End";
                if (t.ShortType == "OnChargeAction")
                    return end ? Loc.T("After charging") : Loc.T("When charging");
                if (types.SequenceEqual(new[] { "swap" }))
                    return end ? Loc.T("After swapping") : Loc.T("On swap");
                if (types.Count == 0)
                    return end ? Loc.T("After an action") : Loc.T("On any action");
                string names = Loc.Join(types.Select(Loc.T));
                return end ? Loc.F("After a {0}", names) : Loc.F("On {0}", names);
            }
            case "OnApplyStatus":
            {
                int kind = Array.IndexOf(StatusKinds, t.Str("_kind"));
                return kind >= 0 ? Loc.F("When you apply {0}", Loc.T(StatusNames[kind])) : Loc.T("When you apply a status");
            }
            case "OnGaveDamageStatusTarget":
            {
                var statuses = Filter(t.Child("_characterStatusKinds"), StatusAdjectives);
                return statuses.Count > 0
                    ? Loc.F("On hitting a {0} enemy", Loc.JoinOr(statuses.Select(Loc.T)))
                    : Loc.T("On hitting an enemy with a status");
            }
            case "OnStatusTargetKilled":
                return Loc.T("On killing an enemy with a status");
            case "OnKilled":
            {
                int kills = (int)t.Num("_killCount", 1);
                return kills > 1 ? Loc.P("Every {0} kill", "Every {0} kills", kills) : Loc.T("On kill");
            }
            case "OnTookDamage":
            case "OnTakeDamage":
                return Loc.T("When you take damage");
            case "OnSwap": return Loc.T("On swap");
            case "OnDashEvade": return Loc.T("On evading with a dash");
            case "OnGrounded": return Loc.T("On landing");
            case "OnBackAttack": return Loc.T("On attacking from behind");
            case "OnEnterMap": return Loc.T("On entering a room");
            case "OnGaugeFull": return Loc.T("When the gauge is full");
            case "OnHealed": return Loc.T("When healed");
            case "OnHealthValue":
            case "OnHealthChanged":
            {
                string amount = Num(t.Num("_amount")) + (t.Str("_healthType") == "Percent" ? "%" : "");
                return t.Str("_compareType") == "LessThan" ? Loc.F("When HP is below {0}", amount) : Loc.F("When HP is above {0}", amount);
            }
            case "OnUseEssence":
            case "OnUseEssenceComponnet":
                return Loc.T("When using a quintessence");
            default:
                return "";
        }
    }

    /// <summary>The enabled entries of a bool-array filter (English names), if it's a narrow selection (1–2 entries).</summary>
    private static List<string> Filter(Node boolArray, string[] names)
    {
        if (boolArray.IsNull || !(boolArray.Raw("_array") is List<object> values))
            return new List<string>();
        var on = values.Select((v, i) => (v is bool b && b, i)).Where(x => x.Item1 && x.i < names.Length).Select(x => names[x.i]).ToList();
        return on.Count is > 0 and <= 2 && on.Count < values.Count ? on : new List<string>();
    }

    private static string Num(double v) => v.ToString(System.Math.Abs(v - System.Math.Round(v)) < 1e-6 ? "0" : "0.##", CultureInfo.InvariantCulture);
}
