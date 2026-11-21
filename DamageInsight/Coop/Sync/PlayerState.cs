using System;

namespace DamageInsight.Coop.Sync;

/// <summary>Short states of a player, one bit each (bits 0-5; bits 6 and 7 of the packed byte are grounded and
/// facing).</summary>
[Flags]
public enum PlayerFlags : byte
{
    None = 0,
    Down = 1,
    Dashing = 2,
    Invisible = 4,
}

/// <summary>
/// A teammate as their game sends it about 20 times a second (docs/COOP_PLAN.md §5.6): where they are, how they
/// move, which way they face, which motion plays and how far, their health. Always sent whole (20 bytes with a small
/// motion id; there are at most three teammates). Position is relative to the room's origin (see
/// <see cref="StatePacking"/>). Drawn through a <see cref="SnapshotBuffer{T}"/>.
/// </summary>
public struct PlayerState : ISnapshot<PlayerState>
{
    public Vec2 Position;
    public Vec2 Velocity;
    public bool FacingRight;
    public bool Grounded;
    public PlayerFlags Flags;
    /// <summary>The motion (animation) playing, as the integrator numbers them; its time in seconds.</summary>
    public int MotionId;
    public float MotionTime;
    public float Health, MaxHealth;

    Vec2 ISnapshot<PlayerState>.Position => Position;

    public PlayerState Lerp(in PlayerState next, float t, float span)
    {
        var s = StatePacking.BlendMotion(MotionId, MotionTime, next.MotionId, next.MotionTime, t, span, out int id, out float time)
            ? next : this;
        s.Position = Vec2.Lerp(Position, next.Position, t);
        s.Velocity = Vec2.Lerp(Velocity, next.Velocity, t);
        s.MotionId = id;
        s.MotionTime = time;
        return s;
    }

    public PlayerState Extrapolate(float seconds)
    {
        var s = this;
        s.Position = Position + Velocity * seconds;
        s.MotionTime = MotionTime + seconds;
        return s;
    }

    public PlayerState Moved(Vec2 offset)
    {
        var s = this;
        s.Position = Position + offset;
        return s;
    }

    /// <summary>Writes the whole state (20 bytes for motion ids -64..63).</summary>
    public void Write(IStateWriter w)
    {
        StatePacking.WriteVec(w, Position, StatePacking.PositionScale);
        StatePacking.WriteVec(w, Velocity, StatePacking.VelocityScale);
        w.WriteByte((byte)(((byte)Flags & 0x3F) | (Grounded ? 0x40 : 0) | (FacingRight ? 0x80 : 0)));
        StatePacking.WriteVarInt(w, MotionId);
        w.WriteUInt16(StatePacking.QuantizeMotionTime(MotionTime));
        w.WriteFloat(Health);
        w.WriteFloat(MaxHealth);
    }

    public static PlayerState Read(IStateReader r)
    {
        var s = new PlayerState
        {
            Position = StatePacking.ReadVec(r, StatePacking.PositionScale),
            Velocity = StatePacking.ReadVec(r, StatePacking.VelocityScale),
        };
        byte bits = r.ReadByte();
        s.Flags = (PlayerFlags)(bits & 0x3F);
        s.Grounded = (bits & 0x40) != 0;
        s.FacingRight = (bits & 0x80) != 0;
        s.MotionId = StatePacking.ReadVarInt(r);
        s.MotionTime = r.ReadUInt16() / StatePacking.MotionTimeScale;
        s.Health = r.ReadFloat();
        s.MaxHealth = r.ReadFloat();
        return s;
    }
}
