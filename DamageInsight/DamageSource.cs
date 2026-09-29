using Characters;
using UnityEngine;

namespace DamageInsight;

/// <summary>
/// Where a hit came from. More detailed than the game's Damage.MotionType:
/// the game's single "Status" is split into the actual status effect.
/// </summary>
public enum DamageSource
{
    Basic,
    Skill,
    Item,
    Quintessence,
    Poison,
    Burn,
    Bleed,
    Shock,
    Ember,
    Status, // a status we couldn't identify
    Dash,
    Swap,
    DarkAbility,
    Other,
}

public static class DamageSources
{
    /// <summary>
    /// Set while a status effect is dealing its tick damage (see StatusContextPatch),
    /// so hits happening inside that call can be attributed to the right status.
    /// </summary>
    internal static DamageSource? CurrentStatus;

    public static DamageSource Classify(in Damage damage)
    {
        switch (damage.motionType)
        {
            case Damage.MotionType.Basic: return DamageSource.Basic;
            case Damage.MotionType.Skill: return DamageSource.Skill;
            case Damage.MotionType.Item: return DamageSource.Item;
            case Damage.MotionType.Quintessence: return DamageSource.Quintessence;
            case Damage.MotionType.Dash: return DamageSource.Dash;
            case Damage.MotionType.Swap: return DamageSource.Swap;
            case Damage.MotionType.DarkAbility: return DamageSource.DarkAbility;
            case Damage.MotionType.Status: return CurrentStatus ?? DamageSource.Status;
            default: return DamageSource.Other;
        }
    }

    public static string Label(DamageSource source) => source switch
    {
        DamageSource.Basic => "ATK",
        DamageSource.Skill => "SKILL",
        DamageSource.Item => "ITEM",
        DamageSource.Quintessence => "ESSENCE",
        DamageSource.Poison => "POISON",
        DamageSource.Burn => "BURN",
        DamageSource.Bleed => "BLEED",
        DamageSource.Shock => "SHOCK",
        DamageSource.Ember => "EMBER",
        DamageSource.Status => "STATUS",
        DamageSource.Dash => "DASH",
        DamageSource.Swap => "SWAP",
        DamageSource.DarkAbility => "DARK",
        _ => "",
    };

    /// <summary>Readable name for the combat log and filter chips.</summary>
    public static string Title(DamageSource source) => source switch
    {
        DamageSource.Quintessence => "Essence",
        DamageSource.DarkAbility => "Dark",
        _ => source.ToString(),
    };

    /// <summary>Colour per damage type (close to the game's own damage number colours).</summary>
    public static string AttributeColorHex(Damage.Attribute attribute) => attribute switch
    {
        Damage.Attribute.Physical => "#FF9A3C",
        Damage.Attribute.Magic => "#3FD0FF",
        _ => "#DDDDDD",
    };

    /// <summary>One fixed colour per source, used for popups, the combat log and the pie chart.</summary>
    public static string ColorHex(DamageSource source) => source switch
    {
        DamageSource.Basic => "#FFFFFF",
        DamageSource.Skill => "#FFC83D",
        DamageSource.Item => "#4FE0C8",
        DamageSource.Quintessence => "#C49BFF",
        DamageSource.Poison => "#96E03A",
        DamageSource.Burn => "#FF6A2B",
        DamageSource.Bleed => "#E8334F",
        DamageSource.Shock => "#FFF36B",
        DamageSource.Ember => "#FFA05E",
        DamageSource.Status => "#D0D0D0",
        DamageSource.Dash => "#7FB8FF",
        DamageSource.Swap => "#FF77DD",
        DamageSource.DarkAbility => "#9A6BFF",
        _ => "#A0A0A0",
    };

    public static Color Color(DamageSource source) => ParseHex(ColorHex(source));

    public static Color AttributeColor(Damage.Attribute attribute) => ParseHex(AttributeColorHex(attribute));

    private static Color ParseHex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var color);
        return color;
    }
}
