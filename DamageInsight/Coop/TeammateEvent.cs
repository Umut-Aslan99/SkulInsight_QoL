using System;
using System.Linq;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop;

public enum TeammateEventKind : byte
{
    /// <summary>The teammate's skulls (with their skills), current skull, quintessence and items changed.</summary>
    Loadout = 1,
    /// <summary>The teammate started a move: an attack, skill, dash, jump, swap (one motion of one action).</summary>
    Motion = 2,
    /// <summary>The teammate's character fired a projectile (TeammateProjectiles): the copy shows exactly these.</summary>
    Projectile = 3,
    /// <summary>A move that runs without taking over the character (`Character.DoActionNonBlock`: the Alchemist's
    /// flask throw): no animation of its own and no onStartMotion; same layout as Motion.</summary>
    NonblockMotion = 4,
    /// <summary>The teammate used its quintessence (TeammateEssences): the copy runs the same quintessence's active.
    /// [name str].</summary>
    Essence = 5,
    /// <summary>A Gravedigger corpse (wandering spirit) appeared on the owner's game (TeammateGraves). [x f32][y f32]
    /// relative to the map.</summary>
    Corpse = 6,
    /// <summary>A Gravedigger corpse was broken into a grave on the owner's game (TeammateGraves). [x f32][y f32].</summary>
    Grave = 7,
    /// <summary>The teammate's follower (TeammateFollowers: the Fairy Tale's Oberon), "" when none. [name str].</summary>
    Follower = 8,
}

/// <summary>Projectile flags of a shot event.</summary>
[Flags]
public enum ShotFlags : byte
{
    None = 0,
    FlipX = 1,
    FlipY = 2,
}

/// <summary>
/// What a teammate's character shows besides its position (COOP_PLAN §5.6): its gear and the moves it starts. Sent
/// reliably as MessageType.TimedEvent (relayed by the host) with the sender's clock, and played on the same timeline
/// as the PlayerState positions (package C's TimedEvents), so a swing starts where the drawn character is.
/// Layout: [time f64][kind u8] then Loadout or Motion (plain logic, tested).
/// </summary>
public sealed class TeammateEvent
{
    public TeammateEventKind Kind;
    public Loadout Loadout;
    /// <summary>Motion: the action's path below the character (each name without "(Clone)"), the motion's index in
    /// the action, and its speed (Character.onStartMotion's run speed).</summary>
    public string ActionPath = "";
    public int MotionIndex;
    public float Speed = 1f;
    /// <summary>Projectile: the prefab's name (no "(Clone)"), where it appeared relative to the character, and the
    /// arguments of its Fire (angle in degrees, flips, speed multiplier, delay) and its scale before Fire.</summary>
    public string ProjectileName = "";
    public float OffsetX, OffsetY, Direction, SpeedMultiplier = 1f, ScaleX = 1f, ScaleY = 1f, Delay;
    public ShotFlags Flags;
    /// <summary>Essence: the quintessence's resource name (Loadout.GearName).</summary>
    public string GearName = "";

    public static TeammateEvent ForEssence(string name) => new() { Kind = TeammateEventKind.Essence, GearName = name ?? "" };

    public static TeammateEvent ForFollower(string name) => new() { Kind = TeammateEventKind.Follower, GearName = name ?? "" };

    /// <summary>Corpse or Grave at a spot relative to the map's origin (OffsetX / OffsetY).</summary>
    public static TeammateEvent ForSpot(TeammateEventKind kind, float x, float y) => new() { Kind = kind, OffsetX = x, OffsetY = y };

    public static TeammateEvent ForLoadout(Loadout loadout) => new() { Kind = TeammateEventKind.Loadout, Loadout = loadout };

    public static TeammateEvent ForMotion(string actionPath, int motionIndex, float speed, bool nonblock = false) =>
        new()
        {
            Kind = nonblock ? TeammateEventKind.NonblockMotion : TeammateEventKind.Motion, ActionPath = actionPath ?? "",
            MotionIndex = motionIndex, Speed = speed,
        };

    public static TeammateEvent ForProjectile(string name, float offsetX, float offsetY, float direction, ShotFlags flags,
        float speedMultiplier, float scaleX, float scaleY, float delay) =>
        new()
        {
            Kind = TeammateEventKind.Projectile, ProjectileName = name ?? "", OffsetX = offsetX, OffsetY = offsetY,
            Direction = direction, Flags = flags, SpeedMultiplier = speedMultiplier, ScaleX = scaleX, ScaleY = scaleY,
            Delay = delay,
        };

    public void Write(NetWriter w, double senderTime)
    {
        w.WriteDouble(senderTime);
        w.WriteByte((byte)Kind);
        switch (Kind)
        {
            case TeammateEventKind.Loadout:
                (Loadout ?? new Loadout()).Write(w);
                break;
            case TeammateEventKind.Projectile:
                w.WriteString(ProjectileName, 128);
                w.WriteFloat(OffsetX);
                w.WriteFloat(OffsetY);
                w.WriteFloat(Direction);
                w.WriteByte((byte)Flags);
                w.WriteFloat(SpeedMultiplier);
                w.WriteFloat(ScaleX);
                w.WriteFloat(ScaleY);
                w.WriteFloat(Delay);
                break;
            case TeammateEventKind.Essence:
            case TeammateEventKind.Follower:
                w.WriteString(GearName, 128);
                break;
            case TeammateEventKind.Corpse:
            case TeammateEventKind.Grave:
                w.WriteFloat(OffsetX);
                w.WriteFloat(OffsetY);
                break;
            default:
                w.WriteString(ActionPath, 512);
                w.WriteByte((byte)Math.Max(0, Math.Min(255, MotionIndex)));
                w.WriteFloat(Speed);
                break;
        }
    }

    /// <summary>The event, or null for a kind this version doesn't know (a newer teammate: skipped).</summary>
    public static TeammateEvent Read(NetReader r, out double senderTime)
    {
        senderTime = r.ReadDouble();
        var kind = (TeammateEventKind)r.ReadByte();
        switch (kind)
        {
            case TeammateEventKind.Loadout:
                return ForLoadout(Loadout.Read(r));
            case TeammateEventKind.Motion:
            case TeammateEventKind.NonblockMotion:
                string path = r.ReadString(512);
                int index = r.ReadByte();
                return ForMotion(path, index, r.ReadFloat(), kind == TeammateEventKind.NonblockMotion);
            case TeammateEventKind.Projectile:
                string name = r.ReadString(128);
                float x = r.ReadFloat(), y = r.ReadFloat(), direction = r.ReadFloat();
                var flags = (ShotFlags)r.ReadByte();
                float speed = r.ReadFloat(), scaleX = r.ReadFloat(), scaleY = r.ReadFloat();
                return ForProjectile(name, x, y, direction, flags, speed, scaleX, scaleY, r.ReadFloat());
            case TeammateEventKind.Essence:
                return ForEssence(r.ReadString(128));
            case TeammateEventKind.Follower:
                return ForFollower(r.ReadString(128));
            case TeammateEventKind.Corpse:
            case TeammateEventKind.Grave:
                float spotX = r.ReadFloat();
                return ForSpot(kind, spotX, r.ReadFloat());
            default:
                return null;
        }
    }
}

/// <summary>A player's gear as the teammates need it to draw them: gear by its resource name (GearResource),
/// skulls with their chosen skill keys.</summary>
public sealed class Loadout : IEquatable<Loadout>
{
    public const int Slots = 2;
    public string[] Weapons = new string[Slots];
    public string[][] Skills = new string[Slots][];
    public int Current;
    public string Essence = "";
    public string[] Items = Array.Empty<string>();

    public void Write(NetWriter w)
    {
        for (int i = 0; i < Slots; i++)
        {
            w.WriteString(Weapons[i] ?? "", 128);
            var skills = Skills[i] ?? Array.Empty<string>();
            w.WriteByte((byte)Math.Min(skills.Length, 8));
            foreach (string skill in skills.Take(8))
                w.WriteString(skill ?? "", 128);
        }
        w.WriteByte((byte)Math.Max(0, Math.Min(Slots - 1, Current)));
        w.WriteString(Essence ?? "", 128);
        w.WriteByte((byte)Math.Min(Items.Length, 32));
        foreach (string item in Items.Take(32))
            w.WriteString(item ?? "", 128);
    }

    public static Loadout Read(NetReader r)
    {
        var l = new Loadout();
        for (int i = 0; i < Slots; i++)
        {
            l.Weapons[i] = r.ReadString(128);
            var skills = new string[Math.Min((int)r.ReadByte(), 8)];
            for (int s = 0; s < skills.Length; s++)
                skills[s] = r.ReadString(128);
            l.Skills[i] = skills;
        }
        l.Current = Math.Min(r.ReadByte(), (byte)(Slots - 1));
        l.Essence = r.ReadString(128);
        var items = new string[Math.Min((int)r.ReadByte(), 32)];
        for (int i = 0; i < items.Length; i++)
            items[i] = r.ReadString(128);
        l.Items = items;
        return l;
    }

    /// <summary>A gear object's resource name: Unity's "(Clone)" removed.</summary>
    public static string GearName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return "";
        const string clone = "(Clone)";
        return objectName.EndsWith(clone, StringComparison.Ordinal)
            ? objectName.Substring(0, objectName.Length - clone.Length).TrimEnd()
            : objectName;
    }

    /// <summary>A pooled object's prefab name: every "(Clone)" removed (a pool copies its prefab once, then each
    /// spawn copies that copy: "X(Clone)(Clone)").</summary>
    public static string PrefabName(string objectName)
    {
        string name = objectName ?? "";
        for (string shorter = GearName(name); shorter != name; shorter = GearName(name))
            name = shorter;
        return name;
    }

    public bool Equals(Loadout other)
    {
        if (other == null || Current != other.Current || (Essence ?? "") != (other.Essence ?? ""))
            return false;
        for (int i = 0; i < Slots; i++)
        {
            if ((Weapons[i] ?? "") != (other.Weapons[i] ?? "")
                || !(Skills[i] ?? Array.Empty<string>()).SequenceEqual(other.Skills[i] ?? Array.Empty<string>()))
                return false;
        }
        return Items.SequenceEqual(other.Items);
    }

    public override bool Equals(object obj) => Equals(obj as Loadout);
    public override int GetHashCode() => (Weapons[0] ?? "").GetHashCode() ^ Current;

    public override string ToString() =>
        $"{Weapons[0]}[{string.Join(",", Skills[0] ?? Array.Empty<string>())}] / {Weapons[1]}[{string.Join(",", Skills[1] ?? Array.Empty<string>())}]" +
        $" (current {Current}), essence {Essence}, {Items.Length} items";
}
