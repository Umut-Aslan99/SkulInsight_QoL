using System;
using System.Collections.Generic;
using Characters;

namespace DamageInsight.Recording;

public enum LogDirection { Dealt, Taken, All }

public enum LogScope { Room, All }

/// <summary>Which recorded hits the combat log shows. Plain logic, no Unity, so it can be unit tested.</summary>
public sealed class LogFilter
{
    public static readonly int[] MinDamageSteps = { 0, 10, 50, 100, 500 };

    public LogDirection Direction = LogDirection.Dealt;
    public LogScope Scope = LogScope.Room;
    public bool CritsOnly;
    public double MinDamage;

    /// <summary>Kinds of the "other party": the target when you deal damage, the attacker when you take it.</summary>
    public readonly HashSet<EntityKind> EnemyKinds = AllOf<EntityKind>();
    public readonly HashSet<DamageSource> Sources = AllOf<DamageSource>();
    public readonly HashSet<Damage.Attribute> Attributes = AllOf<Damage.Attribute>();

    /// <summary>Increases on every change, so views know to re-filter. Call <see cref="Changed"/> after editing.</summary>
    public int Version { get; private set; }

    public void Changed() => Version++;

    public bool Passes(in DamageRecord r, int currentRoom)
    {
        if (Scope == LogScope.Room && r.Room != currentRoom)
            return false;
        if (Direction == LogDirection.Dealt && !r.ByPlayer)
            return false;
        if (Direction == LogDirection.Taken && !r.ToPlayer)
            return false;

        EntityKind other = r.ToPlayer ? r.AttackerKind : r.TargetKind;
        if (!EnemyKinds.Contains(other))
            return false;
        if (!Sources.Contains(r.Source) || !Attributes.Contains(r.Attribute))
            return false;
        if (CritsOnly && !r.Critical)
            return false;
        return r.Amount >= MinDamage;
    }

    public List<DamageRecord> Apply(IEnumerable<DamageRecord> records, int currentRoom)
    {
        var shown = new List<DamageRecord>();
        foreach (var r in records)
            if (Passes(r, currentRoom))
                shown.Add(r);
        return shown;
    }

    private static HashSet<T> AllOf<T>() => new((T[])Enum.GetValues(typeof(T)));
}
