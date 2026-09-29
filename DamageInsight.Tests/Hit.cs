using Characters;
using DamageInsight.Recording;

namespace DamageInsight.Tests;

/// <summary>Builds DamageRecords for tests with sensible defaults: you hit a regular enemy in room 1.</summary>
internal static class Hit
{
    public static DamageRecord Make(
        double amount = 45,
        int room = 1,
        EntityKind attacker = EntityKind.Player,
        EntityKind target = EntityKind.TrashMob,
        DamageSource source = DamageSource.Basic,
        Damage.Attribute attribute = Damage.Attribute.Physical,
        bool crit = false,
        string attackerName = null,
        string targetName = null)
    {
        return new DamageRecord(
            time: 1.5f, room: room,
            attacker: attackerName ?? (attacker == EntityKind.Player ? "You" : "Carleon Recruit"), attackerKind: attacker,
            target: targetName ?? (target == EntityKind.Player ? "You" : "Ent"), targetKind: target,
            source: source, attribute: attribute, amount: amount, critical: crit);
    }
}
