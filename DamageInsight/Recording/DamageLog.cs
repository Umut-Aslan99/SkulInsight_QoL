using System;
using System.Collections.Generic;
using Characters;
using GameResources;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Recording;

/// <summary>Who dealt or received a hit, as far as the log cares.</summary>
public enum EntityKind
{
    Player,
    PlayerMinion,
    TrashMob,
    Elite,      // Character.Type.Named: dark enemies and other named elites
    Adventurer,
    Boss,
    Summoned,
    Trap,
    Other,
}

public readonly struct DamageRecord
{
    public readonly float Time;          // seconds since the room started
    public readonly int Room;
    public readonly string Attacker;
    public readonly EntityKind AttackerKind;
    public readonly string Target;
    public readonly EntityKind TargetKind;
    public readonly DamageSource Source;
    public readonly Damage.Attribute Attribute;
    public readonly double Amount;
    public readonly bool Critical;
    public readonly DamageTrace Trace;   // how the number came about (null if not traced)

    public DamageRecord(float time, int room, string attacker, EntityKind attackerKind, string target,
        EntityKind targetKind, DamageSource source, Damage.Attribute attribute, double amount, bool critical,
        DamageTrace trace = null)
    {
        Trace = trace;
        Time = time;
        Room = room;
        Attacker = attacker;
        AttackerKind = attackerKind;
        Target = target;
        TargetKind = targetKind;
        Source = source;
        Attribute = attribute;
        Amount = amount;
        Critical = critical;
    }

    public bool ByPlayer => AttackerKind is EntityKind.Player or EntityKind.PlayerMinion;
    public bool ToPlayer => TargetKind == EntityKind.Player;
}

public readonly struct RoomInfo
{
    public readonly int Index;
    public readonly string Label;
    public readonly float StartTime;

    public RoomInfo(int index, string label, float startTime)
    {
        Index = index;
        Label = label;
        StartTime = startTime;
    }
}

/// <summary>
/// Every hit recorded while playing, grouped into rooms. Kept in memory; LogFile also writes it to disk.
/// </summary>
public static class DamageLog
{
    private const int MaxRecords = 50000;

    public static readonly List<DamageRecord> Records = new();
    public static readonly List<RoomInfo> Rooms = new();

    /// <summary>Increases whenever something changes, so the UI knows when to redraw.</summary>
    public static int Version { get; private set; }

    /// <summary>How many hits were ever recorded, including ones trimmed from the front of Records.</summary>
    public static long TotalAdded { get; private set; }

    /// <summary>Increases whenever the log is cleared.</summary>
    public static int ClearCount { get; private set; }

    /// <summary>Raised for every recorded hit.</summary>
    public static event Action<DamageRecord> Recorded;

    public static int CurrentRoom => Rooms.Count > 0 ? Rooms[Rooms.Count - 1].Index : 0;

    public static void Clear()
    {
        Records.Clear();
        Rooms.Clear();
        ClearCount++;
        Version++;
    }

    public static void StartRoom()
    {
        Rooms.Add(new RoomInfo(Rooms.Count + 1, DescribeCurrentRoom(), UnityEngine.Time.time));
        Version++;
    }

    public static void Add(Character target, in Damage damage, double dealt)
    {
        if (Rooms.Count == 0)
            StartRoom();

        var room = Rooms[Rooms.Count - 1];
        Character attacker = damage.attacker.character;

        string attackerName;
        EntityKind attackerKind;
        if (attacker != null)
        {
            attackerKind = KindOf(attacker);
            attackerName = NameOf(attacker);
        }
        else if (damage.attacker.trap != null)
        {
            attackerKind = EntityKind.Trap;
            attackerName = "Trap";
        }
        else
        {
            attackerKind = EntityKind.Other;
            attackerName = "Unknown";
        }

        var record = new DamageRecord(
            UnityEngine.Time.time - room.StartTime, room.Index,
            attackerName, attackerKind, NameOf(target), KindOf(target),
            DamageSources.Classify(damage), damage.attribute, dealt, damage.critical,
            DamageInsight.Patches.DamageTracePatch.Finish(target, damage, dealt));
        Records.Add(record);
        TotalAdded++;
        Recorded?.Invoke(record);

        if (Records.Count > MaxRecords)
            Records.RemoveRange(0, Records.Count - MaxRecords);
        Version++;
    }

    public static EntityKind KindOf(Character character) => character.type switch
    {
        Character.Type.Player => EntityKind.Player,
        Character.Type.PlayerMinion => EntityKind.PlayerMinion,
        Character.Type.TrashMob => EntityKind.TrashMob,
        Character.Type.Named => EntityKind.Elite,
        Character.Type.Adventurer => EntityKind.Adventurer,
        Character.Type.Boss => EntityKind.Boss,
        Character.Type.Summoned => EntityKind.Summoned,
        Character.Type.Trap => EntityKind.Trap,
        _ => EntityKind.Other,
    };

    private static readonly Dictionary<string, string> NameCache = new();

    public static string NameOf(Character character)
    {
        if (character == null)
            return "Unknown";
        if (character.type == Character.Type.Player)
            return "You";

        string key = character.key.ToString();
        if (!NameCache.TryGetValue(key, out var name))
        {
            if (!Localization.TryGetLocalizedString($"enemy/name/{key}", out name) || string.IsNullOrEmpty(name))
                name = character.name.Replace("(Clone)", "").Trim();
            NameCache[key] = name;
        }
        return name;
    }

    private static string DescribeCurrentRoom()
    {
        try
        {
            var chapter = Singleton<Service>.Instance.levelManager.currentChapter;
            string map = chapter?.map != null ? chapter.map.name.Replace("(Clone)", "").Trim() : "?";
            return chapter != null ? $"{chapter.type} · {map}" : map;
        }
        catch (Exception)
        {
            return "?";
        }
    }
}
