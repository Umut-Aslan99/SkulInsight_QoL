using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Lang;

namespace DamageInsight.Recording;

/// <summary>What kind of thing dealt a hit (DamageTrace.OriginKind, set from OwnerNames).</summary>
public static class OriginKinds
{
    public const string Skull = "Skull";
    public const string Item = "Item";
    public const string Quintessence = "Quintessence";
    public const string Inscription = "Inscription";
    public const string DarkAbility = "DarkAbility";
    public const string Status = "Status";
}

/// <summary>A row group of the damage table, in table order.</summary>
public enum GearGroup
{
    Skulls,
    Items,
    Quintessences,
    Inscriptions,
    DarkAbilities,
    Statuses,
    Summons,
    Other,
}

public sealed class GearTotal
{
    public double Amount;
    public int Hits;
    public double Biggest;
}

/// <summary>
/// The combat log's damage table (Umut 2026-10-12: "which item did how much damage?"): the player's damage per piece
/// of gear, grouped into skulls (per skull and attack kind), items, quintessences, inscriptions, dark abilities,
/// statuses and summons. Built from each hit's origin (DamageTrace.Origin / OriginKind). Plain logic.
/// </summary>
public static class GearTable
{
    /// <summary>
    /// The gear behind an origin known only by an object's code name (CodexOrigins at run time; null in tests): the
    /// table puts such hits in their gear's row instead of "Other".
    /// </summary>
    public static System.Func<string, (GearGroup group, string name)?> ResolveOrigin;

    /// <summary>The group and row a hit counts for, or null for hits that aren't the player's damage.</summary>
    public static (GearGroup group, string name)? KeyOf(in DamageRecord r)
    {
        if (!r.ByPlayer)
            return null;
        var trace = r.Trace;
        string origin = trace?.Origin ?? "";
        string kind = trace?.OriginKind ?? "";
        if (origin.Length > 0)
        {
            switch (kind)
            {
                case OriginKinds.Skull:
                    return (GearGroup.Skulls, Loc.F("{0}: {1}", origin, AttackKind(r.Source)));
                case OriginKinds.Item:
                    return (GearGroup.Items, origin);
                case OriginKinds.Quintessence:
                    return (GearGroup.Quintessences, origin);
                case OriginKinds.Inscription:
                    return (GearGroup.Inscriptions, origin);
                case OriginKinds.DarkAbility:
                    return (GearGroup.DarkAbilities, origin);
                case OriginKinds.Status:
                    return (GearGroup.Statuses, origin);
            }
            if (r.AttackerKind == EntityKind.PlayerMinion)
                return (GearGroup.Summons, origin);
            if (ResolveOrigin?.Invoke(origin) is { } gear)
                return gear.group == GearGroup.Skulls ? (GearGroup.Skulls, Loc.F("{0}: {1}", gear.name, AttackKind(r.Source))) : gear;
            return (GearGroup.Other, origin);
        }
        // No trace (combat log calculations off) or no known origin: by its source only.
        if (r.AttackerKind == EntityKind.PlayerMinion)
            return (GearGroup.Summons, r.Attacker);
        return r.Source switch
        {
            DamageSource.Basic or DamageSource.Skill or DamageSource.Dash or DamageSource.Swap
                => (GearGroup.Skulls, AttackKind(r.Source)),
            DamageSource.Item => (GearGroup.Items, Loc.T("Item (unknown which)")),
            DamageSource.Quintessence => (GearGroup.Quintessences, DamageSources.Title(r.Source)),
            DamageSource.DarkAbility => (GearGroup.DarkAbilities, DamageSources.Title(r.Source)),
            DamageSource.Poison or DamageSource.Burn or DamageSource.Bleed or DamageSource.Shock or DamageSource.Ember
                or DamageSource.Status => (GearGroup.Statuses, DamageSources.Title(r.Source)),
            _ => (GearGroup.Other, DamageSources.Title(r.Source)),
        };
    }

    private static string AttackKind(DamageSource source) => source switch
    {
        DamageSource.Basic => Loc.T("Basic attack"),
        DamageSource.Skill => Loc.T("Skills"),
        DamageSource.Dash => Loc.T("Dash attack"),
        DamageSource.Swap => Loc.T("Swap"),
        _ => DamageSources.Title(source),
    };

    public static void Add(Dictionary<(GearGroup group, string name), GearTotal> totals, in DamageRecord r)
    {
        var key = KeyOf(r);
        if (key == null)
            return;
        if (!totals.TryGetValue(key.Value, out var total))
            totals[key.Value] = total = new GearTotal();
        total.Amount += r.Amount;
        total.Hits++;
        if (r.Amount > total.Biggest)
            total.Biggest = r.Amount;
    }

    public static string GroupName(GearGroup group) => group switch
    {
        GearGroup.Skulls => Loc.T("Skulls"),
        GearGroup.Items => Loc.T("Items"),
        GearGroup.Quintessences => Loc.T("Quintessences"),
        GearGroup.Inscriptions => Loc.T("Inscriptions"),
        GearGroup.DarkAbilities => Loc.T("Dark abilities"),
        GearGroup.Statuses => Loc.T("Statuses"),
        GearGroup.Summons => Loc.T("Summons"),
        _ => Loc.T("Other"),
    };

    /// <summary>The table as rich text: a header, each group with its total, its rows biggest first.</summary>
    public static string Text(IReadOnlyDictionary<(GearGroup group, string name), GearTotal> totals)
    {
        double all = totals.Values.Sum(t => t.Amount);
        var sb = new StringBuilder();
        sb.Append($"<color=#A89F94>{Loc.T("Source")}<pos=52%>{Loc.T("Damage")}<pos=68%>%<pos=78%>{Loc.T("Hits")}<pos=88%>{Loc.T("Biggest")}</color>\n");
        if (all <= 0)
            return sb.Append($"<color=#A89F94>{Loc.T("No damage dealt yet.")}</color>").ToString();
        foreach (var group in totals.GroupBy(t => t.Key.group).OrderBy(g => g.Key))
        {
            double sum = group.Sum(t => t.Value.Amount);
            int hits = group.Sum(t => t.Value.Hits);
            sb.Append($"\n<b>{GroupName(group.Key)}</b><pos=52%><b>{N(sum)}</b><pos=68%><b>{P(sum, all)}</b><pos=78%>{hits}\n");
            foreach (var row in group.OrderByDescending(t => t.Value.Amount))
                sb.Append($"   {row.Key.name}<pos=52%>{N(row.Value.Amount)}<pos=68%>{P(row.Value.Amount, all)}<pos=78%>{row.Value.Hits}<pos=88%>{N(row.Value.Biggest)}\n");
        }
        return sb.ToString();
    }

    private static string N(double v) => v.ToString("N0", CultureInfo.InvariantCulture);

    private static string P(double v, double all) => (v / all * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
