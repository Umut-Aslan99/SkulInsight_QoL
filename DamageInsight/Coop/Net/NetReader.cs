using System;
using System.Text;

namespace DamageInsight.Coop.Net;

/// <summary>
/// Reads what <see cref="NetWriter"/> wrote. Data from the network can be cut short or made up, so every read checks
/// its bounds and throws <see cref="NetReadException"/> (never IndexOutOfRange): the session catches it and drops the
/// message. One reader is reused for every message (<see cref="Reset"/>); it reads only inside its segment.
/// </summary>
public sealed class NetReader : Sync.IStateReader
{
    /// <summary>The longest string a read accepts unless the caller says otherwise.</summary>
    public const int DefaultMaxStringBytes = 4096;

    /// <summary>The longest blob a read accepts unless the caller says otherwise.</summary>
    public const int DefaultMaxBlobBytes = 1 << 20;

    // Strict: broken UTF-8 from the network is an error, not silently replaced.
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private byte[] _buffer = Array.Empty<byte>();
    private int _start, _position, _end;

    public NetReader() { }

    public NetReader(ArraySegment<byte> data) => Reset(data);

    public NetReader(byte[] data) => Reset(new ArraySegment<byte>(data));

    /// <summary>Starts reading another message (no copy: the bytes must stay valid while reading).</summary>
    public void Reset(ArraySegment<byte> data)
    {
        _buffer = data.Array ?? Array.Empty<byte>();
        _start = _position = data.Array == null ? 0 : data.Offset;
        _end = _start + (data.Array == null ? 0 : data.Count);
    }

    /// <summary>Bytes read so far.</summary>
    public int Position => _position - _start;

    /// <summary>The whole message's length.</summary>
    public int Length => _end - _start;

    /// <summary>Bytes not read yet.</summary>
    public int Remaining => _end - _position;

    public bool IsAtEnd => _position >= _end;

    private void Need(int count, string what)
    {
        if (count > _end - _position)
            throw new NetReadException(FormattableString.Invariant(
                $"message too short: {what} needs {count} bytes at offset {Position}, {Remaining} left"));
    }

    public byte ReadByte()
    {
        Need(1, "a byte");
        return _buffer[_position++];
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public bool ReadBool()
    {
        byte b = ReadByte();
        if (b > 1)
            throw new NetReadException(FormattableString.Invariant($"invalid bool {b} at offset {Position - 1}"));
        return b == 1;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public ushort ReadUInt16()
    {
        Need(2, "a 16-bit number");
        ushort v = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
        _position += 2;
        return v;
    }

    public int ReadInt32() => (int)ReadUInt32();

    public uint ReadUInt32()
    {
        Need(4, "a 32-bit number");
        uint v = _buffer[_position] | ((uint)_buffer[_position + 1] << 8) | ((uint)_buffer[_position + 2] << 16) |
                 ((uint)_buffer[_position + 3] << 24);
        _position += 4;
        return v;
    }

    public long ReadInt64() => (long)ReadUInt64();

    public ulong ReadUInt64()
    {
        Need(8, "a 64-bit number");
        ulong v = 0;
        for (int i = 0; i < 8; i++)
            v |= (ulong)_buffer[_position + i] << (8 * i);
        _position += 8;
        return v;
    }

    /// <summary>A varint (<see cref="NetWriter.WriteVarUInt"/>); more than 64 bits or 10 bytes is an error.</summary>
    public ulong ReadVarUInt()
    {
        ulong value = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            if (_position >= _end)
                Need(1, "a varint");
            byte b = _buffer[_position++];
            if (shift == 63 && b > 1)
                throw new NetReadException(FormattableString.Invariant($"varint over 64 bits at offset {Position - 1}"));
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
        throw new NetReadException(FormattableString.Invariant($"varint longer than 10 bytes at offset {Position - 1}"));
    }

    public long ReadVarInt()
    {
        ulong u = ReadVarUInt();
        return (long)(u >> 1) ^ -(long)(u & 1);
    }

    /// <summary>A varint that must fit 32 bits (counts, ids).</summary>
    public uint ReadVarUInt32()
    {
        int at = Position;
        ulong v = ReadVarUInt();
        if (v > uint.MaxValue)
            throw new NetReadException(FormattableString.Invariant($"varint {v} at offset {at} is over 32 bits"));
        return (uint)v;
    }

    public int ReadVarInt32()
    {
        int at = Position;
        long v = ReadVarInt();
        if (v < int.MinValue || v > int.MaxValue)
            throw new NetReadException(FormattableString.Invariant($"varint {v} at offset {at} is over 32 bits"));
        return (int)v;
    }

    public float ReadFloat() => new NetWriter.FloatBits { Bits = ReadUInt32() }.Float;

    public double ReadDouble() => BitConverter.Int64BitsToDouble((long)ReadUInt64());

    public (float x, float y) ReadVector2()
    {
        float x = ReadFloat();
        return (x, ReadFloat());
    }

    /// <summary>A UTF-8 string with its varint byte count; longer than <paramref name="maxBytes"/> or broken UTF-8 is an error.</summary>
    public string ReadString(int maxBytes = DefaultMaxStringBytes)
    {
        int at = Position;
        uint count = ReadVarUInt32();
        if (count == 0)
            return "";
        if (count > maxBytes)
            throw new NetReadException(FormattableString.Invariant($"string of {count} bytes at offset {at} is over the limit of {maxBytes}"));
        Need((int)count, "a string");
        string s;
        try
        {
            s = Utf8.GetString(_buffer, _position, (int)count);
        }
        catch (DecoderFallbackException)
        {
            throw new NetReadException(FormattableString.Invariant($"invalid UTF-8 in the string at offset {at}"));
        }
        _position += (int)count;
        return s;
    }

    /// <summary>The next <paramref name="count"/> bytes, without copying (valid as long as the message is).</summary>
    public ArraySegment<byte> ReadBytes(int count)
    {
        if (count < 0)
            throw new NetReadException(FormattableString.Invariant($"negative byte count {count}"));
        Need(count, "bytes");
        var segment = new ArraySegment<byte>(_buffer, _position, count);
        _position += count;
        return segment;
    }

    /// <summary>Bytes with a varint count in front (<see cref="NetWriter.WriteBlob"/>), without copying.</summary>
    public ArraySegment<byte> ReadBlob(int maxBytes = DefaultMaxBlobBytes)
    {
        int at = Position;
        uint count = ReadVarUInt32();
        if (count > maxBytes)
            throw new NetReadException(FormattableString.Invariant($"blob of {count} bytes at offset {at} is over the limit of {maxBytes}"));
        return ReadBytes((int)count);
    }

    /// <summary>Everything not read yet, without copying.</summary>
    public ArraySegment<byte> ReadRest() => ReadBytes(Remaining);

    /// <summary>A value written by <see cref="NetWriter.WriteQuantized"/> with the same range and bits.</summary>
    public float ReadQuantized(float min, float max, int bits = 16)
    {
        uint top = NetWriter.MaxStep(bits);
        int bytes = (bits + 7) >> 3;
        Need(bytes, "a quantized value");
        uint q = 0;
        for (int i = 0; i < bytes; i++)
            q |= (uint)_buffer[_position + i] << (8 * i);
        if (q > top)
            throw new NetReadException(FormattableString.Invariant($"quantized value {q} at offset {Position} is over {bits} bits"));
        _position += bytes;
        return NetWriter.Dequantize(q, min, max, bits);
    }
}

/// <summary>A message that is cut short or holds impossible values (thrown by <see cref="NetReader"/>).</summary>
public sealed class NetReadException : Exception
{
    public NetReadException(string message) : base(message) { }
}
