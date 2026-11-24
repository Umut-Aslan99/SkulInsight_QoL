using System.Collections.Generic;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Sync;

namespace DamageInsight.Coop;

public enum EnemyEventKind : byte
{
    /// <summary>An enemy started a move (attack, skill, jump...): clients play the same motion on their copy.</summary>
    Motion = 1,
    /// <summary>An enemy died on the host: it dies on every game (its drops are each player's own).</summary>
    Died = 2,
    /// <summary>A wave appeared on the host.</summary>
    WaveSpawn = 3,
    /// <summary>The host's room is done (its waves stopped, its reward came out): a client's room may finish too.</summary>
    RoomCleared = 4,
    /// <summary>A boss behaviour started on the host (one whose work isn't a character move: BossSync.Replayed):
    /// clients run the same behaviour on their puppet. Extra: a random pick made for it on the host.</summary>
    Behaviour = 5,
    /// <summary>A boss changed phase on the host (Extra = the new phase).</summary>
    Phase = 6,
    /// <summary>A boss method ran on the host (BossSync.Calls: ActionPath = its key, Extra / Speed = its argument,
    /// Seq = the host's call number, which seeds the random spots inside it on both games).</summary>
    Call = 7,
    /// <summary>An enemy summoned a new enemy on the host (NetId = the new one's id from the summon range, Wave = the
    /// summoner's id or 0, ActionPath = the prefab's name, X/Y = its spot from the room's origin, Extra 1 = in a wave).</summary>
    Summon = 8,
    /// <summary>An enemy's target changed on the host between its moves or in the middle of one (the co-op rule,
    /// a hit): TargetSlot = the new one. A delayed part of a move (a spell cast "at the player" seconds later) reads it
    /// then; before, only moves carried the target, so such a part went to another player on each game.</summary>
    Target = 9,
    /// <summary>Dark Mirror: an enemy became a dark elite on the host and got its abilities (its intro): ActionPath =
    /// the abilities' names joined by '|', Extra = the Dark Mirror level (its constructor). Clients make their puppet
    /// the same elite (DarkElites.cs).</summary>
    DarkElite = 10,
}

/// <summary>One host event about the room's enemies, on the host's timeline.</summary>
public sealed class EnemyEvent
{
    public EnemyEventKind Kind;
    public ushort NetId;
    public int Wave;
    public string ActionPath = "";
    public int MotionIndex;
    public float Speed = 1f;
    /// <summary>Motion: the slot of the player the enemy goes for (255 = none), so a client's puppet aims there.</summary>
    public byte TargetSlot = 255;
    /// <summary>Behaviour: the host's random pick for it (0 = none); Phase: the phase; Call: a byte argument.</summary>
    public byte Extra;
    /// <summary>Call: the host's number for this boss's call (seeds its random spots on every game).</summary>
    public ushort Seq;
    /// <summary>Summon: where the new enemy appeared; Motion: where the enemy stood when the move started (from the
    /// room's origin).</summary>
    public float X, Y;
    /// <summary>Motion: the points the enemy's AI placed that moved since its last move (BossMarkers), or null.</summary>
    public List<EnemyMarker> Markers;
}

/// <summary>A point an enemy's AI placed for its move: the AI's n-th Transform field (BossMarkers), from the room's origin.</summary>
public struct EnemyMarker
{
    public byte Index;
    public float X, Y;
}

/// <summary>A player's hit on an enemy as their game saw it (COOP_PLAN §5.4): the damage before the enemy's own
/// reductions (its game rolled the critical hit), so the host applies it through the game's damage code once.</summary>
public struct EnemyHit
{
    public ushort NetId;
    public double Amount;
    public bool Critical;
    public byte AttackType, MotionType, Attribute;
    /// <summary>How long the hit makes the enemy flinch (the attack's stopping power; 0 = none): the host's enemy
    /// flinches too, so the hit interrupts its move for everyone.</summary>
    public float StoppingPower;
}

/// <summary>
/// The messages of host-run enemies (COOP_PLAN §5.1, §5.3-5.5; plain logic, tested). Every message names the room
/// (WorldMessages.RoomId) so nothing of an old room reaches a new one.
/// - EnemySnapshot (unreliable, ~20/s): [room u32][time f64][count u16] + count x EnemyState (package C, whole).
/// - EnemyEvent (reliable): [room u32][time f64][kind u8] + Motion [id u16][path str][index u8][speed f32][target u8][x f32][y f32]
///   (+ since 0.17.1, optional: [count u8] + count x [marker u8][x f32][y f32], the AI's points that moved, BossMarkers;
///   one event per message and no reader checks the end, so 0.17.0 games ignore the tail and send none) |
///   Died [id u16][killer u8: the slot of the player whose hit killed it, 255 nobody's] | WaveSpawn [wave u8] | RoomCleared (nothing more) | Behaviour [id u16][path str][target u8][extra u8] |
///   Phase [id u16][phase u8] | Call [id u16][key str][target u8][arg u8][value f32][seq u16] |
///   Summon [id u16][summoner u16][prefab str][x f32][y f32][flags u8].
/// - EnemyHit (reliable, a client to the host): [room u32][id u16][amount f64][crit bool][attack u8][motion u8][attribute u8][stopping power f32].
/// - EnemyStatus (reliable, a client to the host): [room u32][id u16][kind u8] (CharacterStatus.Kind: 0 stun, 1 freeze:
///   real on the host; 2 burn, 3 wound, 4 poison: only shown there, their damage is the client's hits).
/// - EnemyPush (reliable, a client to the host): [room u32][id u16][flags u8: 1 smash, 2 ignore other force, 4 expire
///   on ground] + 2 x [x f32][y f32][value multiplier f32][duration f32][key count u8][keys: time, value, in, out f32].
/// </summary>
/// <summary>A knockback or smash a client's player put on an enemy (Characters.Movements.Push as it was set: two
/// forces, each with its curve). The host puts the same push on its enemy.</summary>
public sealed class EnemyPush
{
    public const int MaxKeys = 16;
    public ushort Id;
    public bool Smash, IgnoreOtherForce, ExpireOnGround;
    public readonly PushPart[] Parts = { new(), new() };
}

/// <summary>One force of a push: its vector and its curve (keys as time, value, in tangent, out tangent).</summary>
public sealed class PushPart
{
    public float X, Y, ValueMultiplier, Duration;
    public float[] Keys = System.Array.Empty<float>();
}

public static class EnemyMessages
{
    public static void WritePush(NetWriter w, uint room, EnemyPush push)
    {
        w.WriteUInt32(room);
        w.WriteUInt16(push.Id);
        w.WriteByte((byte)((push.Smash ? 1 : 0) | (push.IgnoreOtherForce ? 2 : 0) | (push.ExpireOnGround ? 4 : 0)));
        foreach (var part in push.Parts)
        {
            w.WriteFloat(part.X);
            w.WriteFloat(part.Y);
            w.WriteFloat(part.ValueMultiplier);
            w.WriteFloat(part.Duration);
            int keys = System.Math.Min(part.Keys.Length / 4, EnemyPush.MaxKeys);
            w.WriteByte((byte)keys);
            for (int i = 0; i < keys * 4; i++)
                w.WriteFloat(part.Keys[i]);
        }
    }

    public static EnemyPush ReadPush(NetReader r, out uint room)
    {
        room = r.ReadUInt32();
        var push = new EnemyPush { Id = r.ReadUInt16() };
        byte flags = r.ReadByte();
        push.Smash = (flags & 1) != 0;
        push.IgnoreOtherForce = (flags & 2) != 0;
        push.ExpireOnGround = (flags & 4) != 0;
        foreach (var part in push.Parts)
        {
            part.X = r.ReadFloat();
            part.Y = r.ReadFloat();
            part.ValueMultiplier = r.ReadFloat();
            part.Duration = r.ReadFloat();
            var keys = new float[System.Math.Min((int)r.ReadByte(), EnemyPush.MaxKeys) * 4];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = r.ReadFloat();
            part.Keys = keys;
        }
        return push;
    }

    public const int MaxEnemies = 512;

    public static void WriteSnapshot(NetWriter w, uint room, double time, IReadOnlyList<EnemyState> states)
    {
        w.WriteUInt32(room);
        w.WriteDouble(time);
        int count = System.Math.Min(states.Count, MaxEnemies);
        w.WriteUInt16((ushort)count);
        for (int i = 0; i < count; i++)
            states[i].Write(w);
    }

    public static void ReadSnapshot(NetReader r, out uint room, out double time, List<EnemyState> into)
    {
        room = r.ReadUInt32();
        time = r.ReadDouble();
        int count = System.Math.Min((int)r.ReadUInt16(), MaxEnemies);
        into.Clear();
        for (int i = 0; i < count; i++)
            into.Add(EnemyState.Read(r));
    }

    public static void WriteEvent(NetWriter w, uint room, double time, EnemyEvent e)
    {
        w.WriteUInt32(room);
        w.WriteDouble(time);
        w.WriteByte((byte)e.Kind);
        switch (e.Kind)
        {
            case EnemyEventKind.Motion:
                w.WriteUInt16(e.NetId);
                w.WriteString(e.ActionPath ?? "", 512);
                w.WriteByte((byte)System.Math.Max(0, System.Math.Min(255, e.MotionIndex)));
                w.WriteFloat(e.Speed);
                w.WriteByte(e.TargetSlot);
                w.WriteFloat(e.X);
                w.WriteFloat(e.Y);
                if (e.Markers is { Count: > 0 })
                {
                    int count = System.Math.Min(e.Markers.Count, 32);
                    w.WriteByte((byte)count);
                    for (int i = 0; i < count; i++)
                    {
                        w.WriteByte(e.Markers[i].Index);
                        w.WriteFloat(e.Markers[i].X);
                        w.WriteFloat(e.Markers[i].Y);
                    }
                }
                break;
            case EnemyEventKind.Died:
                w.WriteUInt16(e.NetId);
                w.WriteByte(e.TargetSlot);
                break;
            case EnemyEventKind.WaveSpawn:
                w.WriteByte((byte)e.Wave);
                break;
            case EnemyEventKind.RoomCleared:
                break;
            case EnemyEventKind.Behaviour:
                w.WriteUInt16(e.NetId);
                w.WriteString(e.ActionPath ?? "", 512);
                w.WriteByte(e.TargetSlot);
                w.WriteByte(e.Extra);
                break;
            case EnemyEventKind.Phase:
                w.WriteUInt16(e.NetId);
                w.WriteByte(e.Extra);
                break;
            case EnemyEventKind.Call:
                w.WriteUInt16(e.NetId);
                w.WriteString(e.ActionPath ?? "", 512);
                w.WriteByte(e.TargetSlot);
                w.WriteByte(e.Extra);
                w.WriteFloat(e.Speed);
                w.WriteUInt16(e.Seq);
                break;
            case EnemyEventKind.Target:
                w.WriteUInt16(e.NetId);
                w.WriteByte(e.TargetSlot);
                break;
            case EnemyEventKind.Summon:
                w.WriteUInt16(e.NetId);
                w.WriteUInt16((ushort)e.Wave);
                w.WriteString(e.ActionPath ?? "", 512);
                w.WriteFloat(e.X);
                w.WriteFloat(e.Y);
                w.WriteByte(e.Extra);
                break;
            case EnemyEventKind.DarkElite:
                w.WriteUInt16(e.NetId);
                w.WriteString(e.ActionPath ?? "", 512);
                w.WriteByte(e.Extra);
                break;
        }
    }

    /// <summary>The event, or null for a kind this version doesn't know.</summary>
    public static EnemyEvent ReadEvent(NetReader r, out uint room, out double time)
    {
        room = r.ReadUInt32();
        time = r.ReadDouble();
        var e = new EnemyEvent { Kind = (EnemyEventKind)r.ReadByte() };
        switch (e.Kind)
        {
            case EnemyEventKind.Motion:
                e.NetId = r.ReadUInt16();
                e.ActionPath = r.ReadString(512);
                e.MotionIndex = r.ReadByte();
                e.Speed = r.ReadFloat();
                e.TargetSlot = r.ReadByte();
                e.X = r.ReadFloat();
                e.Y = r.ReadFloat();
                if (!r.IsAtEnd)
                {
                    int count = r.ReadByte();
                    e.Markers = new List<EnemyMarker>(count);
                    for (int i = 0; i < count; i++)
                        e.Markers.Add(new EnemyMarker { Index = r.ReadByte(), X = r.ReadFloat(), Y = r.ReadFloat() });
                }
                return e;
            case EnemyEventKind.Died:
                e.NetId = r.ReadUInt16();
                e.TargetSlot = r.ReadByte();
                return e;
            case EnemyEventKind.WaveSpawn:
                e.Wave = r.ReadByte();
                return e;
            case EnemyEventKind.RoomCleared:
                return e;
            case EnemyEventKind.Behaviour:
                e.NetId = r.ReadUInt16();
                e.ActionPath = r.ReadString(512);
                e.TargetSlot = r.ReadByte();
                e.Extra = r.ReadByte();
                return e;
            case EnemyEventKind.Phase:
                e.NetId = r.ReadUInt16();
                e.Extra = r.ReadByte();
                return e;
            case EnemyEventKind.Call:
                e.NetId = r.ReadUInt16();
                e.ActionPath = r.ReadString(512);
                e.TargetSlot = r.ReadByte();
                e.Extra = r.ReadByte();
                e.Speed = r.ReadFloat();
                e.Seq = r.ReadUInt16();
                return e;
            case EnemyEventKind.Target:
                e.NetId = r.ReadUInt16();
                e.TargetSlot = r.ReadByte();
                return e;
            case EnemyEventKind.Summon:
                e.NetId = r.ReadUInt16();
                e.Wave = r.ReadUInt16();
                e.ActionPath = r.ReadString(512);
                e.X = r.ReadFloat();
                e.Y = r.ReadFloat();
                e.Extra = r.ReadByte();
                return e;
            case EnemyEventKind.DarkElite:
                e.NetId = r.ReadUInt16();
                e.ActionPath = r.ReadString(512);
                e.Extra = r.ReadByte();
                return e;
            default:
                return null;
        }
    }

    public static void WriteHit(NetWriter w, uint room, in EnemyHit hit)
    {
        w.WriteUInt32(room);
        w.WriteUInt16(hit.NetId);
        w.WriteDouble(hit.Amount);
        w.WriteBool(hit.Critical);
        w.WriteByte(hit.AttackType);
        w.WriteByte(hit.MotionType);
        w.WriteByte(hit.Attribute);
        w.WriteFloat(hit.StoppingPower);
    }

    public static void WriteStatus(NetWriter w, uint room, ushort id, byte kind)
    {
        w.WriteUInt32(room);
        w.WriteUInt16(id);
        w.WriteByte(kind);
    }

    public static void ReadStatus(NetReader r, out uint room, out ushort id, out byte kind)
    {
        room = r.ReadUInt32();
        id = r.ReadUInt16();
        kind = r.ReadByte();
    }

    public static EnemyHit ReadHit(NetReader r, out uint room)
    {
        room = r.ReadUInt32();
        return new EnemyHit
        {
            NetId = r.ReadUInt16(),
            Amount = r.ReadDouble(),
            Critical = r.ReadBool(),
            AttackType = r.ReadByte(),
            MotionType = r.ReadByte(),
            Attribute = r.ReadByte(),
            StoppingPower = r.ReadFloat(),
        };
    }
}
