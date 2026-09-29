using System.Collections.Generic;

namespace DamageInsight.Describe;

/// <summary>One kind of hit found in a gear's data, before the player's stats are applied.</summary>
public sealed class Hit
{
    /// <summary>Base damage range (from an AttackDamage or an amount field), before multipliers.</summary>
    public double BaseMin, BaseMax;

    /// <summary>Where the base comes from: "gear", "gear:path", "runner", "field:name", or "skull".</summary>
    public string BaseSource = "";

    /// <summary>The hit has no damage of its own and uses the equipped skull's base damage.</summary>
    public bool UsesSkullDamage => BaseSource == "skull";

    /// <summary>Combined multiplier range: HitInfo._damageMultiplier × projectile multipliers etc.</summary>
    public double MultMin = 1, MultMax = 1;

    public string Attribute = "Physical";   // Physical | Magic | Fixed
    public string MotionType = "Basic";     // Basic | Skill | Item | Quintessence | Status | Dash | Swap | DarkAbility | None
    public string AttackType = "Melee";     // Melee | Ranged | Projectile | Additional | None

    /// <summary>How many times it hits per use (1 = once). 0 = repeats indefinitely while active.</summary>
    public int Count = 1;

    /// <summary>Chance (0..1) that this part happens at all, if it is behind a chance decorator.</summary>
    public double Chance = 1;

    public bool AdaptiveForce;      // uses the higher of physical/magic attack
    public bool UsesEnemyStats;     // damage computed with the target's stats (HitBomb, DotDamage)
    public string Owner = "";       // type that holds the HitInfo, e.g. "SweepAttack"
    public string OwnerPath = "";   // $path of the component that holds it (if known)
    public string Note = "";        // extra text shown after the hit, e.g. "scales with air time"
    public string Key = "";         // HitInfo._key (some passives modify hits by key)

    /// <summary>Set when the game code turns crits off for this hit (e.g. poison and burn ticks).</summary>
    public bool NeverCrits;

    /// <summary>The game's crit rule: item and quintessence hits never crit; fixed damage ignores crits too.</summary>
    public bool CanCrit => !NeverCrits && MotionType != "Item" && MotionType != "Quintessence" && Attribute != "Fixed";

    public bool SameAs(Hit o) =>
        BaseMin == o.BaseMin && BaseMax == o.BaseMax && MultMin == o.MultMin && MultMax == o.MultMax &&
        Attribute == o.Attribute && MotionType == o.MotionType && AttackType == o.AttackType &&
        Chance == o.Chance && AdaptiveForce == o.AdaptiveForce && UsesEnemyStats == o.UsesEnemyStats;
}

/// <summary>A labelled group of hits, e.g. "Hit 2" of a combo, or a whole skill.</summary>
public sealed class Step
{
    public string Label = "";
    public readonly List<Hit> Hits = new();
}

/// <summary>A part of a gear: its basic combo, jump attack, one skill, the swap, the passive or an item effect.</summary>
public sealed class Section
{
    public string Kind = "";   // Basic | Jump | Skill | Swap | Dash | Passive | Effect | Active
    public string Key = "";    // skill key (SkillInfo._key) for Skill sections
    public double Cooldown;    // seconds, 0 = none
    public string Trigger = ""; // when it happens, e.g. "On critical hit", "Every 12 s", "30% chance on hit"
    public int Step;            // inscriptions: the step that unlocks it (1 = first step), SuperStep = "true form"
    public const int SuperStep = -1;
    public string Title = "";
    public bool Compact;        // one short line per step (sum of the hits) instead of every hit with its breakdown   // optional heading, e.g. "Shockwave"
    public string Path = "";    // hierarchy path of the action (weapons), for labels
    public readonly List<Step> Steps = new();
    public readonly List<string> Notes = new();

    public bool HasHits
    {
        get
        {
            foreach (var s in Steps)
                if (s.Hits.Count > 0)
                    return true;
            return false;
        }
    }
}

/// <summary>An item that turns into another item (the game's "Change" operation).</summary>
public sealed class Upgrade
{
    public string TargetName = "";   // prefab name of the item it becomes, e.g. "ShadeDarkSpirit"
    public string OwnItemName = "";  // set when the change happens while owning another item, e.g. "LughLightSpirit"
}

/// <summary>Everything we know about one gear's damage.</summary>
public sealed class Breakdown
{
    public readonly List<Upgrade> Upgrades = new();
    public string Name = "";
    public string Category = ""; // weapons | items | essences
    public double BaseMin, BaseMax;   // the gear's own AttackDamage (skull base damage), if any
    public readonly List<Section> Sections = new();

    public Section Find(string kind, string key = null)
    {
        foreach (var s in Sections)
            if (s.Kind == kind && (key == null || s.Key == key))
                return s;
        return null;
    }
}
