using System.Collections.Generic;
using Characters;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The combat log's damage table: hits grouped by the piece of gear that dealt them.</summary>
public class GearTableTests
{
    private static DamageRecord Traced(double amount, DamageSource source, string origin, string kind,
        EntityKind attacker = EntityKind.Player, EntityKind target = EntityKind.TrashMob)
    {
        var plain = Hit.Make(amount, attacker: attacker, target: target, source: source);
        var trace = new DamageTrace { Origin = origin, OriginKind = kind };
        return new DamageRecord(plain.Time, plain.Room, plain.Attacker, plain.AttackerKind, plain.Target, plain.TargetKind,
            plain.Source, plain.Attribute, plain.Amount, plain.Critical, trace);
    }

    [Fact]
    public void Hits_are_grouped_by_the_gear_that_dealt_them()
    {
        var totals = new Dictionary<(GearGroup, string), GearTotal>();
        GearTable.Add(totals, Traced(100, DamageSource.Basic, "Warlord", OriginKinds.Skull));
        GearTable.Add(totals, Traced(50, DamageSource.Basic, "Warlord", OriginKinds.Skull));
        GearTable.Add(totals, Traced(300, DamageSource.Skill, "Warlord", OriginKinds.Skull));
        GearTable.Add(totals, Traced(80, DamageSource.Item, "Gunpowder Sword", OriginKinds.Item));
        GearTable.Add(totals, Traced(20, DamageSource.Item, "Brawl", OriginKinds.Inscription));
        GearTable.Add(totals, Traced(5, DamageSource.Poison, "Poison", OriginKinds.Status));
        GearTable.Add(totals, Traced(999, DamageSource.Basic, "Ent", "", EntityKind.TrashMob, EntityKind.Player)); // damage taken

        Assert.Equal(5, totals.Count); // Warlord basic + skills, sword, Brawl, poison; the damage taken is left out
        var basic = totals[(GearGroup.Skulls, "Warlord: Basic attack")];
        Assert.Equal(150, basic.Amount);
        Assert.Equal(2, basic.Hits);
        Assert.Equal(100, basic.Biggest);
        Assert.Equal(300, totals[(GearGroup.Skulls, "Warlord: Skills")].Amount);
        Assert.Equal(80, totals[(GearGroup.Items, "Gunpowder Sword")].Amount);
        Assert.Equal(20, totals[(GearGroup.Inscriptions, "Brawl")].Amount);
        Assert.Equal(5, totals[(GearGroup.Statuses, "Poison")].Amount);
    }

    [Fact]
    public void Untraced_hits_fall_back_to_their_source_and_summons_to_their_name()
    {
        var totals = new Dictionary<(GearGroup, string), GearTotal>();
        GearTable.Add(totals, Hit.Make(10, source: DamageSource.Item));
        GearTable.Add(totals, Hit.Make(7, attacker: EntityKind.PlayerMinion, attackerName: "Hydra"));
        Assert.Equal(10, totals[(GearGroup.Items, "Item (unknown which)")].Amount);
        Assert.Equal(7, totals[(GearGroup.Summons, "Hydra")].Amount);
    }

    [Fact]
    public void The_table_lists_groups_with_their_share()
    {
        var totals = new Dictionary<(GearGroup, string), GearTotal>();
        GearTable.Add(totals, Traced(75, DamageSource.Item, "Gunpowder Sword", OriginKinds.Item));
        GearTable.Add(totals, Traced(25, DamageSource.Basic, "Little Bone", OriginKinds.Skull));
        string text = GearTable.Text(totals);
        Assert.Contains("Gunpowder Sword", text);
        Assert.Contains("75.0%", text);
        Assert.True(text.IndexOf("Skulls", System.StringComparison.Ordinal) < text.IndexOf("Items", System.StringComparison.Ordinal));
    }
}
