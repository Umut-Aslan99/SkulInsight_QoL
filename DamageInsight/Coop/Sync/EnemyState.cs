using System;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// Short states of an enemy. On the wire: one byte (bits 0-6; bit 7 is facing), and only for an enemy that has one of
/// the rarer states of the second byte (<see cref="Burning"/> and up) a second byte: bit 1 of the first byte
/// (<see cref="WireMore"/>) says it follows.
/// </summary>
[Flags]
public enum EnemyFlags : ushort
{
    None = 0,
    /// <summary>Attacks pass through it in the host's game (`Character.cinematic`: an intro scene, a boss move that
    /// can't be interrupted): the same on a client's puppet, so nobody can hit what the host's player can't.</summary>
    Cinematic = 1,
    /// <summary>Wire only, never a state: the second byte follows.</summary>
    WireMore = 2,
    /// <summary>Takes no damage in the host's game (`Character.invulnerable`).</summary>
    Invulnerable = 4,
    /// <summary>Stunned in the host's game (see <see cref="Frozen"/>).</summary>
    Stunned = 8,
    /// <summary>On the ground in the host's game (its animator's flag; walk animations play only on the ground).</summary>
    Grounded = 16,
    /// <summary>Walking in the host's game (its animator's flag).</summary>
    Walking = 32,
    /// <summary>Frozen in the host's game (with <see cref="Stunned"/>: the statuses that lock an enemy; a client's
    /// puppet is locked exactly while the host's enemy is).</summary>
    Frozen = 64,
    /// <summary>Burning in the host's game (second byte, with <see cref="Poisoned"/> and <see cref="Wounded"/>: the
    /// statuses that only show; every game shows them while the host's enemy has them, whoever gave them).</summary>
    Burning = 256,
    Poisoned = 512,
    Wounded = 1024,
}

/// <summary>Which fields an enemy delta carries (the mask byte).</summary>
[Flags]
public enum EnemyFields : byte
{
    None = 0,
    PositionFull = 1,
    /// <summary>Position as a step from the baseline, ±127/32 units per axis (2 bytes instead of 4).</summary>
    PositionStep = 2,
    VelocityFull = 4,
    /// <summary>Velocity as a step from the baseline, ±127/16 units/s per axis.</summary>
    VelocityStep = 8,
    Bits = 16,
    /// <summary>Motion id and its time (a new or restarted motion).</summary>
    Motion = 32,
    /// <summary>Only the motion time (it drifted from the expected baseline time + elapsed).</summary>
    MotionTime = 64,
    Health = 128,
}

/// <summary>
/// An enemy as the host sends it (docs/COOP_PLAN.md §5.3, §5.7: "positions packed small, only what changed").
/// Wire format of one entry: net id (uint16), field mask (byte, <see cref="EnemyFields"/>), then only the fields the
/// mask names, in that order. A delta is written against a baseline the receiver is known to have (an acknowledged
/// earlier snapshot); an enemy the baseline doesn't have is written against <c>default</c> (that is
/// <see cref="Write"/>). Motion time is predicted as baseline time + elapsed and sent only when it drifts more than
/// <see cref="MotionTimeTolerance"/>. A walking enemy costs 5 bytes, a falling one 7. Position is room-relative
/// (see <see cref="StatePacking"/>); health is the current hit points (exact, float).
/// </summary>
public struct EnemyState : ISnapshot<EnemyState>
{
    /// <summary>Seconds the predicted motion time may be off before the real one is sent.</summary>
    public const float MotionTimeTolerance = 0.02f;

    public ushort NetId;
    public Vec2 Position;
    public Vec2 Velocity;
    public bool FacingRight;
    public EnemyFlags Flags;
    public int MotionId;
    public float MotionTime;
    public float Health;

    Vec2 ISnapshot<EnemyState>.Position => Position;

    public EnemyState Lerp(in EnemyState next, float t, float span)
    {
        var s = StatePacking.BlendMotion(MotionId, MotionTime, next.MotionId, next.MotionTime, t, span, out int id, out float time)
            ? next : this;
        s.Position = Vec2.Lerp(Position, next.Position, t);
        s.Velocity = Vec2.Lerp(Velocity, next.Velocity, t);
        s.MotionId = id;
        s.MotionTime = time;
        return s;
    }

    public EnemyState Extrapolate(float seconds)
    {
        var s = this;
        s.Position = Position + Velocity * seconds;
        s.MotionTime = MotionTime + seconds;
        return s;
    }

    public EnemyState Moved(Vec2 offset)
    {
        var s = this;
        s.Position = Position + offset;
        return s;
    }

    /// <summary>Low byte: the first wire byte (states 0-6 without <see cref="EnemyFlags.WireMore"/>, which is set
    /// when the high byte isn't empty; bit 7 facing). High byte: the second wire byte (states 8-15).</summary>
    private ushort PackedBits
    {
        get
        {
            int states = (int)Flags & ~(int)EnemyFlags.WireMore;
            int low = (states & 0x7F) | (FacingRight ? 0x80 : 0), high = states & 0xFF00;
            return (ushort)(low | high | (high != 0 ? (int)EnemyFlags.WireMore : 0));
        }
    }

    /// <summary>The whole state (a new enemy, or no baseline): a delta against <c>default</c>.</summary>
    public EnemyState Write(IStateWriter w) => WriteDelta(w, default, 0f);

    /// <summary>The whole state as <see cref="Write"/> wrote it (reads the net id too).</summary>
    public static EnemyState Read(IStateReader r) => ReadDelta(r, ReadNetId(r), default, 0f);

    /// <summary>Writes the net id, the mask and what changed since <paramref name="baseline"/>
    /// (<paramref name="elapsed"/> = seconds from the baseline's snapshot to this one, from the message times).
    /// Returns the state exactly as the receiver will rebuild it: keep that as the next baseline, not this.</summary>
    public EnemyState WriteDelta(IStateWriter w, in EnemyState baseline, float elapsed)
    {
        const float P = StatePacking.PositionScale, V = StatePacking.VelocityScale;
        var fields = EnemyFields.None;
        int px = StatePacking.Quantize(Position.X, P), py = StatePacking.Quantize(Position.Y, P);
        int bx = StatePacking.Quantize(baseline.Position.X, P), by = StatePacking.Quantize(baseline.Position.Y, P);
        int dpx = px - bx, dpy = py - by;
        if (dpx != 0 || dpy != 0)
            fields |= IsStep(dpx) && IsStep(dpy) ? EnemyFields.PositionStep : EnemyFields.PositionFull;
        int vx = StatePacking.Quantize(Velocity.X, V), vy = StatePacking.Quantize(Velocity.Y, V);
        int dvx = vx - StatePacking.Quantize(baseline.Velocity.X, V), dvy = vy - StatePacking.Quantize(baseline.Velocity.Y, V);
        if (dvx != 0 || dvy != 0)
            fields |= IsStep(dvx) && IsStep(dvy) ? EnemyFields.VelocityStep : EnemyFields.VelocityFull;
        if (PackedBits != baseline.PackedBits)
            fields |= EnemyFields.Bits;
        ushort mt = StatePacking.QuantizeMotionTime(MotionTime);
        if (MotionId != baseline.MotionId)
            fields |= EnemyFields.Motion;
        else if (Math.Abs(mt / StatePacking.MotionTimeScale - (baseline.MotionTime + elapsed)) > MotionTimeTolerance)
            fields |= EnemyFields.MotionTime;
        if (Health != baseline.Health && !(float.IsNaN(Health) && float.IsNaN(baseline.Health)))
            fields |= EnemyFields.Health;

        w.WriteUInt16(NetId);
        w.WriteByte((byte)fields);
        if ((fields & EnemyFields.PositionFull) != 0)
        {
            w.WriteInt16((short)px);
            w.WriteInt16((short)py);
        }
        else if ((fields & EnemyFields.PositionStep) != 0)
        {
            w.WriteSByte((sbyte)dpx);
            w.WriteSByte((sbyte)dpy);
        }
        if ((fields & EnemyFields.VelocityFull) != 0)
        {
            w.WriteInt16((short)vx);
            w.WriteInt16((short)vy);
        }
        else if ((fields & EnemyFields.VelocityStep) != 0)
        {
            w.WriteSByte((sbyte)dvx);
            w.WriteSByte((sbyte)dvy);
        }
        if ((fields & EnemyFields.Bits) != 0)
        {
            ushort bits = PackedBits;
            w.WriteByte((byte)bits);
            if ((bits & (int)EnemyFlags.WireMore) != 0)
                w.WriteByte((byte)(bits >> 8));
        }
        if ((fields & EnemyFields.Motion) != 0)
            StatePacking.WriteVarInt(w, MotionId);
        if ((fields & (EnemyFields.Motion | EnemyFields.MotionTime)) != 0)
            w.WriteUInt16(mt);
        if ((fields & EnemyFields.Health) != 0)
            w.WriteFloat(Health);

        return Rebuild(fields, baseline, elapsed, new Vec2(px / P, py / P), new Vec2(vx / V, vy / V), PackedBits,
                       MotionId, mt, Health);
    }

    /// <summary>Reads the net id that starts every entry (to look up the baseline before <see cref="ReadDelta"/>).</summary>
    public static ushort ReadNetId(IStateReader r) => r.ReadUInt16();

    /// <summary>Reads the rest of an entry written by <see cref="WriteDelta"/> against the same baseline and elapsed.</summary>
    public static EnemyState ReadDelta(IStateReader r, ushort netId, in EnemyState baseline, float elapsed)
    {
        const float P = StatePacking.PositionScale, V = StatePacking.VelocityScale;
        var fields = (EnemyFields)r.ReadByte();
        var pos = baseline.Position;
        if ((fields & EnemyFields.PositionFull) != 0)
            pos = StatePacking.ReadVec(r, P);
        else if ((fields & EnemyFields.PositionStep) != 0)
            pos = StepFrom(StatePacking.Snap(baseline.Position, P), r.ReadSByte(), r.ReadSByte(), P);
        var vel = baseline.Velocity;
        if ((fields & EnemyFields.VelocityFull) != 0)
            vel = StatePacking.ReadVec(r, V);
        else if ((fields & EnemyFields.VelocityStep) != 0)
            vel = StepFrom(StatePacking.Snap(baseline.Velocity, V), r.ReadSByte(), r.ReadSByte(), V);
        ushort bits = baseline.PackedBits;
        if ((fields & EnemyFields.Bits) != 0)
        {
            bits = r.ReadByte();
            if ((bits & (int)EnemyFlags.WireMore) != 0)
                bits |= (ushort)(r.ReadByte() << 8);
        }
        int motionId = (fields & EnemyFields.Motion) != 0 ? StatePacking.ReadVarInt(r) : baseline.MotionId;
        ushort mt = (fields & (EnemyFields.Motion | EnemyFields.MotionTime)) != 0 ? r.ReadUInt16() : (ushort)0;
        float health = (fields & EnemyFields.Health) != 0 ? r.ReadFloat() : baseline.Health;
        var s = Rebuild(fields, baseline, elapsed, pos, vel, bits, motionId, mt, health);
        s.NetId = netId;
        return s;
    }

    private static EnemyState Rebuild(EnemyFields fields, in EnemyState baseline, float elapsed, Vec2 pos, Vec2 vel,
                                      ushort bits, int motionId, ushort mt, float health)
    {
        bool moved = (fields & (EnemyFields.PositionFull | EnemyFields.PositionStep)) != 0;
        bool sped = (fields & (EnemyFields.VelocityFull | EnemyFields.VelocityStep)) != 0;
        return new EnemyState
        {
            NetId = baseline.NetId,
            Position = moved ? pos : baseline.Position,
            Velocity = sped ? vel : baseline.Velocity,
            FacingRight = (bits & 0x80) != 0,
            Flags = (EnemyFlags)(bits & 0xFF7F & ~(int)EnemyFlags.WireMore),
            MotionId = motionId,
            MotionTime = (fields & (EnemyFields.Motion | EnemyFields.MotionTime)) != 0
                ? mt / StatePacking.MotionTimeScale
                : baseline.MotionTime + elapsed,
            Health = health,
        };
    }

    private static bool IsStep(int d) => d >= sbyte.MinValue && d <= sbyte.MaxValue;

    private static Vec2 StepFrom(Vec2 snappedBase, sbyte dx, sbyte dy, float scale) =>
        new((StatePacking.Quantize(snappedBase.X, scale) + dx) / scale, (StatePacking.Quantize(snappedBase.Y, scale) + dy) / scale);
}
