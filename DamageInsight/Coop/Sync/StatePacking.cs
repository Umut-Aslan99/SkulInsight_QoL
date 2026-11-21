using System;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// Where the states write their compact bytes. Kept this small so package A's NetWriter can implement it as is
/// (same method names) or through a few-line adapter; <see cref="StateBytesWriter"/> is a plain byte[] version.
/// Little-endian.
/// </summary>
public interface IStateWriter
{
    void WriteByte(byte value);
    void WriteSByte(sbyte value);
    void WriteUInt16(ushort value);
    void WriteInt16(short value);
    void WriteFloat(float value);
}

/// <summary>Reads what an <see cref="IStateWriter"/> wrote (package A's NetReader can implement it).</summary>
public interface IStateReader
{
    byte ReadByte();
    sbyte ReadSByte();
    ushort ReadUInt16();
    short ReadInt16();
    float ReadFloat();
}

/// <summary>
/// The quantization shared by the states. Positions are relative to the room's origin (the integrator subtracts it)
/// in 1/32 unit steps as int16: ±1024 units of room, at most 1/64 unit off. Velocities in 1/16 unit/s steps as int16
/// (±2048 units/s; only used to extrapolate 0.25 s at most, so 1/16 is plenty). Motion time in milliseconds as
/// uint16 (up to 65.5 s; longer is clamped). Motion ids as a zigzag varint (1 byte for -64..63).
/// </summary>
public static class StatePacking
{
    public const float PositionScale = 32f, VelocityScale = 16f, MotionTimeScale = 1000f;

    public static short Quantize(float value, float scale)
    {
        float q = value * scale;
        if (float.IsNaN(q))
            return 0;
        q = (float)Math.Round(q);
        return q > short.MaxValue ? short.MaxValue : q < short.MinValue ? short.MinValue : (short)q;
    }

    public static ushort QuantizeMotionTime(float seconds)
    {
        float q = (float)Math.Round(seconds * MotionTimeScale);
        return float.IsNaN(q) || q < 0 ? (ushort)0 : q > ushort.MaxValue ? ushort.MaxValue : (ushort)q;
    }

    public static void WriteVec(IStateWriter w, Vec2 v, float scale)
    {
        w.WriteInt16(Quantize(v.X, scale));
        w.WriteInt16(Quantize(v.Y, scale));
    }

    public static Vec2 ReadVec(IStateReader r, float scale) => new(r.ReadInt16() / scale, r.ReadInt16() / scale);

    /// <summary>The value as it comes out of the wire (to compare and to keep as a baseline).</summary>
    public static Vec2 Snap(Vec2 v, float scale) => new(Quantize(v.X, scale) / scale, Quantize(v.Y, scale) / scale);

    public static void WriteVarInt(IStateWriter w, int value)
    {
        uint v = (uint)((value << 1) ^ (value >> 31)); // zigzag: small negatives stay small
        while (v >= 0x80)
        {
            w.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        w.WriteByte((byte)v);
    }

    public static int ReadVarInt(IStateReader r)
    {
        uint v = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = r.ReadByte();
            v |= (uint)(b & 0x7F) << shift;
            if (b < 0x80)
                break;
        }
        return (int)(v >> 1) ^ -(int)(v & 1);
    }

    /// <summary>
    /// The motion to draw between two states: the same motion running on blends its time; a motion that started in
    /// between (another id, or the same one restarted) begins exactly when it started on the sender (its time in
    /// <paramref name="nextTime"/> says how long before the later state). Returns whether the later state's motion is
    /// already showing (discrete fields switch with it).
    /// </summary>
    public static bool BlendMotion(int fromId, float fromTime, int nextId, float nextTime, float t, float span,
                                   out int id, out float time)
    {
        if (fromId == nextId && nextTime >= fromTime)
        {
            id = fromId;
            time = fromTime + (nextTime - fromTime) * t;
            return t >= 0.5f;
        }
        float untilNext = (1 - t) * span;
        if (nextTime >= untilNext)
        {
            id = nextId;
            time = nextTime - untilNext;
            return true;
        }
        id = fromId;
        time = fromTime + t * span;
        return false;
    }
}

/// <summary>An <see cref="IStateWriter"/> into a byte array that grows (tests, and until NetWriter is wired in).</summary>
public sealed class StateBytesWriter : IStateWriter
{
    private byte[] _bytes;

    public StateBytesWriter(int capacity = 256) => _bytes = new byte[capacity];

    public int Length { get; private set; }
    public ArraySegment<byte> Written => new(_bytes, 0, Length);
    public void Clear() => Length = 0;

    public byte[] ToArray()
    {
        var copy = new byte[Length];
        Buffer.BlockCopy(_bytes, 0, copy, 0, Length);
        return copy;
    }

    public void WriteByte(byte value)
    {
        if (Length == _bytes.Length)
            Array.Resize(ref _bytes, _bytes.Length * 2);
        _bytes[Length++] = value;
    }

    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    public void WriteUInt16(ushort value)
    {
        WriteByte((byte)value);
        WriteByte((byte)(value >> 8));
    }

    public void WriteInt16(short value) => WriteUInt16((ushort)value);

    public unsafe void WriteFloat(float value)
    {
        uint v = *(uint*)&value;
        WriteUInt16((ushort)v);
        WriteUInt16((ushort)(v >> 16));
    }
}

/// <summary>Reads a byte array written by <see cref="StateBytesWriter"/>.</summary>
public sealed class StateBytesReader : IStateReader
{
    private readonly ArraySegment<byte> _bytes;
    private int _at;

    public StateBytesReader(ArraySegment<byte> bytes) => _bytes = bytes;
    public StateBytesReader(byte[] bytes) : this(new ArraySegment<byte>(bytes)) { }

    public int Remaining => _bytes.Count - _at;

    public byte ReadByte()
    {
        if (_at >= _bytes.Count)
            throw new InvalidOperationException("read past the end of the state bytes");
        return _bytes.Array![_bytes.Offset + _at++];
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();
    public ushort ReadUInt16() => (ushort)(ReadByte() | ReadByte() << 8);
    public short ReadInt16() => (short)ReadUInt16();

    public unsafe float ReadFloat()
    {
        uint v = ReadUInt16() | (uint)ReadUInt16() << 16;
        return *(float*)&v;
    }
}
