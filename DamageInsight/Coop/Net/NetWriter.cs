using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DamageInsight.Coop.Net;

/// <summary>
/// Writes a co-op message as compact little-endian bytes (docs/COOP_PLAN.md §5.7: positions packed small). One writer
/// is kept and reused for every message (<see cref="Reset"/>), so sending allocates nothing once the buffer has grown
/// to the biggest message. <see cref="NetReader"/> reads the same layout back. Plain C#, no Unity types: a position
/// is written as two floats (<see cref="WriteVector2"/>) or two quantized values.
/// </summary>
public sealed class NetWriter : Sync.IStateWriter
{
    // Strings are written with the same encoder that counts them; a lone surrogate becomes U+FFFD (the reader is
    // strict and would refuse broken UTF-8, but never sees any from here).
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);

    private byte[] _buffer;
    private int _length;

    public NetWriter(int initialCapacity = 64) => _buffer = new byte[Math.Max(16, initialCapacity)];

    /// <summary>Bytes written since the last <see cref="Reset"/>.</summary>
    public int Length => _length;

    /// <summary>The size of the buffer now (it doubles when a message doesn't fit).</summary>
    public int Capacity => _buffer.Length;

    /// <summary>Starts a new message in the same buffer.</summary>
    public void Reset() => _length = 0;

    /// <summary>The message so far, without copying (valid until the next write or <see cref="Reset"/>).</summary>
    public ArraySegment<byte> ToSegment() => new(_buffer, 0, _length);

    /// <summary>A copy of the message (for tests and messages that are kept).</summary>
    public byte[] ToArray()
    {
        var copy = new byte[_length];
        Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
        return copy;
    }

    private void Ensure(int extra)
    {
        int need = _length + extra;
        if (need > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(need, _buffer.Length * 2));
    }

    public void WriteByte(byte value)
    {
        Ensure(1);
        _buffer[_length++] = value;
    }

    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    /// <summary>One byte: 1 or 0 (the reader refuses anything else).</summary>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteInt16(short value) => WriteUInt16((ushort)value);

    public void WriteUInt16(ushort value)
    {
        Ensure(2);
        _buffer[_length] = (byte)value;
        _buffer[_length + 1] = (byte)(value >> 8);
        _length += 2;
    }

    public void WriteInt32(int value) => WriteUInt32((uint)value);

    public void WriteUInt32(uint value)
    {
        Ensure(4);
        _buffer[_length] = (byte)value;
        _buffer[_length + 1] = (byte)(value >> 8);
        _buffer[_length + 2] = (byte)(value >> 16);
        _buffer[_length + 3] = (byte)(value >> 24);
        _length += 4;
    }

    public void WriteInt64(long value) => WriteUInt64((ulong)value);

    public void WriteUInt64(ulong value)
    {
        Ensure(8);
        for (int i = 0; i < 8; i++)
            _buffer[_length + i] = (byte)(value >> (8 * i));
        _length += 8;
    }

    /// <summary>An unsigned number in 1 to 10 bytes, 7 bits per byte (small numbers stay small: under 128 = 1 byte).</summary>
    public void WriteVarUInt(ulong value)
    {
        Ensure(10);
        while (value >= 0x80)
        {
            _buffer[_length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        _buffer[_length++] = (byte)value;
    }

    /// <summary>A signed number as a varint, zigzag-coded so that small negative numbers stay small too (-1 = 1 byte).</summary>
    public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

    public void WriteFloat(float value) => WriteUInt32(new FloatBits { Float = value }.Bits);

    public void WriteDouble(double value) => WriteUInt64((ulong)BitConverter.DoubleToInt64Bits(value));

    /// <summary>A position or any other pair as two floats (8 bytes).</summary>
    public void WriteVector2(float x, float y)
    {
        WriteFloat(x);
        WriteFloat(y);
    }

    /// <summary>
    /// A string as UTF-8 with a varint byte count in front (null is written as ""). A string longer than
    /// <paramref name="maxBytes"/> is a bug in the caller and throws; the reader refuses it with the same limit.
    /// </summary>
    public void WriteString(string value, int maxBytes = NetReader.DefaultMaxStringBytes)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteByte(0);
            return;
        }
        int count = Utf8.GetByteCount(value);
        if (count > maxBytes)
            throw new ArgumentException(FormattableString.Invariant($"string of {count} bytes is over the limit of {maxBytes}"), nameof(value));
        WriteVarUInt((uint)count);
        Ensure(count);
        _length += Utf8.GetBytes(value, 0, value.Length, _buffer, _length);
    }

    /// <summary>Raw bytes, no length in front (the reader must know the count).</summary>
    public void WriteBytes(ArraySegment<byte> bytes)
    {
        if (bytes.Count == 0)
            return;
        Ensure(bytes.Count);
        Buffer.BlockCopy(bytes.Array!, bytes.Offset, _buffer, _length, bytes.Count);
        _length += bytes.Count;
    }

    public void WriteBytes(byte[] bytes, int offset, int count) => WriteBytes(new ArraySegment<byte>(bytes, offset, count));

    /// <summary>Bytes with a varint count in front (<see cref="NetReader.ReadBlob"/>).</summary>
    public void WriteBlob(ArraySegment<byte> bytes)
    {
        WriteVarUInt((uint)bytes.Count);
        WriteBytes(bytes);
    }

    /// <summary>
    /// A float in [<paramref name="min"/>, <paramref name="max"/>] stored in <paramref name="bits"/> bits (1-32),
    /// rounded up to whole bytes: 16 bits = 2 bytes. Values outside the range are clamped, NaN becomes min. The
    /// error after reading back is at most half a step (<see cref="QuantizeStep"/>).
    /// </summary>
    public void WriteQuantized(float value, float min, float max, int bits = 16)
    {
        uint q = Quantize(value, min, max, bits);
        int bytes = (bits + 7) >> 3;
        Ensure(bytes);
        for (int i = 0; i < bytes; i++)
            _buffer[_length + i] = (byte)(q >> (8 * i));
        _length += bytes;
    }

    /// <summary>The largest number <paramref name="bits"/> bits can hold (the top step of a quantized value).</summary>
    public static uint MaxStep(int bits)
    {
        if (bits < 1 || bits > 32)
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "1 to 32 bits");
        return bits == 32 ? uint.MaxValue : (1u << bits) - 1;
    }

    /// <summary>The distance between two neighbouring quantized values.</summary>
    public static double QuantizeStep(float min, float max, int bits) => ((double)max - min) / MaxStep(bits);

    public static uint Quantize(float value, float min, float max, int bits)
    {
        uint top = MaxStep(bits);
        if (!(max > min))
            throw new ArgumentOutOfRangeException(nameof(max), max, "max must be above min");
        if (!(value > min)) // also NaN
            return 0;
        if (value >= max)
            return top;
        double t = ((double)value - min) / ((double)max - min);
        double q = Math.Floor(t * top + 0.5);
        return q >= top ? top : (uint)q;
    }

    public static float Dequantize(uint q, float min, float max, int bits)
    {
        uint top = MaxStep(bits);
        if (q >= top)
            return max;
        return (float)(min + ((double)max - min) * q / top);
    }

    /// <summary>Float and its bits without unsafe code or allocations (BitConverter has no float version in .NET 4.7.2).</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct FloatBits
    {
        [FieldOffset(0)] public float Float;
        [FieldOffset(0)] public uint Bits;
    }
}
