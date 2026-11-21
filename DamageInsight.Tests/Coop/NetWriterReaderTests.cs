using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The co-op wire format (package A): every type round-trips, and broken data is a NetReadException.</summary>
public class NetWriterReaderTests
{
    private static NetReader Read(NetWriter w) => new(w.ToArray());

    [Fact]
    public void Every_fixed_size_type_round_trips_with_its_edge_values()
    {
        var w = new NetWriter();
        w.WriteByte(0); w.WriteByte(255);
        w.WriteSByte(sbyte.MinValue); w.WriteSByte(sbyte.MaxValue);
        w.WriteBool(true); w.WriteBool(false);
        w.WriteInt16(short.MinValue); w.WriteInt16(short.MaxValue);
        w.WriteUInt16(0); w.WriteUInt16(ushort.MaxValue);
        w.WriteInt32(int.MinValue); w.WriteInt32(int.MaxValue); w.WriteInt32(-1);
        w.WriteUInt32(0); w.WriteUInt32(uint.MaxValue);
        w.WriteInt64(long.MinValue); w.WriteInt64(long.MaxValue);
        w.WriteUInt64(0); w.WriteUInt64(ulong.MaxValue);

        var r = Read(w);
        Assert.Equal(0, r.ReadByte()); Assert.Equal(255, r.ReadByte());
        Assert.Equal(sbyte.MinValue, r.ReadSByte()); Assert.Equal(sbyte.MaxValue, r.ReadSByte());
        Assert.True(r.ReadBool()); Assert.False(r.ReadBool());
        Assert.Equal(short.MinValue, r.ReadInt16()); Assert.Equal(short.MaxValue, r.ReadInt16());
        Assert.Equal(0, r.ReadUInt16()); Assert.Equal(ushort.MaxValue, r.ReadUInt16());
        Assert.Equal(int.MinValue, r.ReadInt32()); Assert.Equal(int.MaxValue, r.ReadInt32()); Assert.Equal(-1, r.ReadInt32());
        Assert.Equal(0u, r.ReadUInt32()); Assert.Equal(uint.MaxValue, r.ReadUInt32());
        Assert.Equal(long.MinValue, r.ReadInt64()); Assert.Equal(long.MaxValue, r.ReadInt64());
        Assert.Equal(0ul, r.ReadUInt64()); Assert.Equal(ulong.MaxValue, r.ReadUInt64());
        Assert.True(r.IsAtEnd);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Numbers_are_little_endian()
    {
        var w = new NetWriter();
        w.WriteUInt16(0x0102);
        w.WriteUInt32(0x01020304);
        w.WriteFloat(1f);
        w.WriteUInt64(0x0102030405060708);
        Assert.Equal(new byte[] { 2, 1, 4, 3, 2, 1, 0, 0, 0x80, 0x3F, 8, 7, 6, 5, 4, 3, 2, 1 }, w.ToArray());
    }

    [Theory]
    [InlineData(0ul, 1)]
    [InlineData(1ul, 1)]
    [InlineData(127ul, 1)]
    [InlineData(128ul, 2)]
    [InlineData(16383ul, 2)]
    [InlineData(16384ul, 3)]
    [InlineData(uint.MaxValue, 5)]
    [InlineData(ulong.MaxValue, 10)]
    public void Unsigned_varints_round_trip_and_stay_short(ulong value, int bytes)
    {
        var w = new NetWriter();
        w.WriteVarUInt(value);
        Assert.Equal(bytes, w.Length);
        var r = Read(w);
        Assert.Equal(value, r.ReadVarUInt());
        Assert.True(r.IsAtEnd);
    }

    [Theory]
    [InlineData(0L, 1)]
    [InlineData(-1L, 1)]
    [InlineData(1L, 1)]
    [InlineData(63L, 1)]
    [InlineData(-64L, 1)]
    [InlineData(64L, 2)]
    [InlineData(-65L, 2)]
    [InlineData(int.MinValue, 5)]
    [InlineData(long.MaxValue, 10)]
    [InlineData(long.MinValue, 10)]
    public void Signed_varints_are_zigzag_so_small_negatives_stay_short(long value, int bytes)
    {
        var w = new NetWriter();
        w.WriteVarInt(value);
        Assert.Equal(bytes, w.Length);
        Assert.Equal(value, Read(w).ReadVarInt());
    }

    [Fact]
    public void Thirty_two_bit_varints_refuse_bigger_numbers()
    {
        var w = new NetWriter();
        w.WriteVarUInt(uint.MaxValue);
        w.WriteVarUInt((ulong)uint.MaxValue + 1);
        w.WriteVarInt(int.MinValue);
        w.WriteVarInt((long)int.MaxValue + 1);
        var r = Read(w);
        Assert.Equal(uint.MaxValue, r.ReadVarUInt32());
        Assert.Throws<NetReadException>(() => r.ReadVarUInt32());
        Assert.Equal(int.MinValue, r.ReadVarInt32());
        Assert.Throws<NetReadException>(() => r.ReadVarInt32());
    }

    [Fact]
    public void Overlong_varints_are_read_errors()
    {
        // Eleven continuation bytes, and a tenth byte with more than the 64th bit.
        var tooLong = new byte[11];
        for (int i = 0; i < 10; i++)
            tooLong[i] = 0x80;
        Assert.Throws<NetReadException>(() => new NetReader(tooLong).ReadVarUInt());
        var overflow = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };
        Assert.Throws<NetReadException>(() => new NetReader(overflow).ReadVarUInt());
    }

    [Fact]
    public void Floats_and_doubles_round_trip_bit_exactly()
    {
        var floats = new[] { 0f, -0f, 1.5f, -123.456f, float.Epsilon, float.MaxValue, float.MinValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        var doubles = new[] { 0d, -0d, Math.PI, double.Epsilon, double.MaxValue, double.NaN, double.NegativeInfinity, 123456789.123456789 };
        var w = new NetWriter();
        foreach (var f in floats)
            w.WriteFloat(f);
        foreach (var d in doubles)
            w.WriteDouble(d);
        var r = Read(w);
        foreach (var f in floats)
            Assert.Equal(new NetWriter.FloatBits { Float = f }.Bits, new NetWriter.FloatBits { Float = r.ReadFloat() }.Bits);
        foreach (var d in doubles)
            Assert.Equal(BitConverter.DoubleToInt64Bits(d), BitConverter.DoubleToInt64Bits(r.ReadDouble()));
    }

    [Fact]
    public void Vector_pairs_are_two_floats()
    {
        var w = new NetWriter();
        w.WriteVector2(12.5f, -3.25f);
        Assert.Equal(8, w.Length);
        var (x, y) = Read(w).ReadVector2();
        Assert.Equal(12.5f, x);
        Assert.Equal(-3.25f, y);
    }

    [Fact]
    public void Strings_round_trip_in_utf8_with_a_varint_length()
    {
        var texts = new[] { "", "DocRun", "스컬", "日本語のテキスト", "Grüße", "emoji \U0001F480", new string('x', 300) };
        var w = new NetWriter();
        foreach (var t in texts)
            w.WriteString(t);
        w.WriteString(null);
        var r = Read(w);
        foreach (var t in texts)
            Assert.Equal(t, r.ReadString());
        Assert.Equal("", r.ReadString()); // null is written as ""
        Assert.True(r.IsAtEnd);

        var abc = new NetWriter();
        abc.WriteString("abc");
        Assert.Equal(new byte[] { 3, (byte)'a', (byte)'b', (byte)'c' }, abc.ToArray());
    }

    [Fact]
    public void A_string_over_the_limit_is_refused_on_both_sides()
    {
        var w = new NetWriter();
        Assert.Throws<ArgumentException>(() => w.WriteString(new string('a', 11), maxBytes: 10));
        Assert.Throws<ArgumentException>(() => w.WriteString("ééééé" + "é", maxBytes: 10)); // 12 bytes in UTF-8
        Assert.Equal(0, w.Length);

        w.WriteString(new string('a', 11));
        Assert.Throws<NetReadException>(() => Read(w).ReadString(maxBytes: 10));
    }

    [Fact]
    public void Broken_utf8_is_a_read_error()
    {
        Assert.Throws<NetReadException>(() => new NetReader(new byte[] { 2, 0xC3, 0x28 }).ReadString());
        Assert.Throws<NetReadException>(() => new NetReader(new byte[] { 1, 0xFF }).ReadString());
    }

    [Fact]
    public void A_bool_other_than_0_or_1_is_a_read_error()
    {
        Assert.Throws<NetReadException>(() => new NetReader(new byte[] { 2 }).ReadBool());
    }

    [Fact]
    public void Blobs_and_raw_bytes_round_trip_without_copies()
    {
        var source = new byte[] { 9, 9, 1, 2, 3, 4, 9 };
        var w = new NetWriter();
        w.WriteBlob(new ArraySegment<byte>(source, 2, 4));
        w.WriteBytes(source, 0, 2);
        w.WriteBlob(default);
        var bytes = w.ToArray();
        var r = new NetReader(bytes);
        var blob = r.ReadBlob();
        Assert.Same(bytes, blob.Array);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, blob.ToArray());
        Assert.Equal(new byte[] { 9, 9 }, r.ReadBytes(2).ToArray());
        Assert.Equal(0, r.ReadBlob().Count);
        Assert.True(r.IsAtEnd);

        var big = new NetWriter();
        big.WriteBlob(new ArraySegment<byte>(new byte[20]));
        Assert.Throws<NetReadException>(() => Read(big).ReadBlob(maxBytes: 10));
    }

    [Fact]
    public void Quantized_floats_stay_within_half_a_step()
    {
        foreach (int bits in new[] { 1, 4, 8, 10, 12, 16, 20, 24, 32 })
        {
            const float min = -50f, max = 150f;
            double step = NetWriter.QuantizeStep(min, max, bits);
            var values = new List<float> { min, max, 0f, 0.001f, 49.99f, 100f, -49.5f };
            for (int i = 0; i < 50; i++)
                values.Add(min + (max - min) * ((i * 37) % 101) / 100f);
            var w = new NetWriter();
            foreach (var v in values)
                w.WriteQuantized(v, min, max, bits);
            Assert.Equal(values.Count * ((bits + 7) / 8), w.Length);
            var r = Read(w);
            foreach (var v in values)
            {
                float back = r.ReadQuantized(min, max, bits);
                Assert.True(Math.Abs(back - v) <= step / 2 + 1e-4, $"{bits} bits: {v} came back as {back} (step {step})");
            }
        }
    }

    [Fact]
    public void Quantized_ends_are_exact_and_outside_values_are_clamped()
    {
        var w = new NetWriter();
        w.WriteQuantized(-1f, -1f, 1f);
        w.WriteQuantized(1f, -1f, 1f);
        w.WriteQuantized(-99f, -1f, 1f);
        w.WriteQuantized(99f, -1f, 1f);
        w.WriteQuantized(float.NaN, -1f, 1f);
        var r = Read(w);
        Assert.Equal(-1f, r.ReadQuantized(-1f, 1f));
        Assert.Equal(1f, r.ReadQuantized(-1f, 1f));
        Assert.Equal(-1f, r.ReadQuantized(-1f, 1f));
        Assert.Equal(1f, r.ReadQuantized(-1f, 1f));
        Assert.Equal(-1f, r.ReadQuantized(-1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.WriteQuantized(0, 0, 1, bits: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.WriteQuantized(0, 0, 1, bits: 33));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.WriteQuantized(0, 1, 1));
    }

    [Fact]
    public void A_quantized_value_over_its_bits_is_a_read_error()
    {
        // 10 bits are stored in 2 bytes; 0xFFFF can't come from the writer.
        Assert.Throws<NetReadException>(() => new NetReader(new byte[] { 0xFF, 0xFF }).ReadQuantized(0, 1, 10));
    }

    /// <summary>Every read, with what the writer puts there.</summary>
    private static IEnumerable<(string name, Action<NetWriter> write, Action<NetReader> read)> AllReads()
    {
        yield return ("byte", w => w.WriteByte(7), r => r.ReadByte());
        yield return ("sbyte", w => w.WriteSByte(-7), r => r.ReadSByte());
        yield return ("bool", w => w.WriteBool(true), r => r.ReadBool());
        yield return ("int16", w => w.WriteInt16(-300), r => r.ReadInt16());
        yield return ("uint16", w => w.WriteUInt16(300), r => r.ReadUInt16());
        yield return ("int32", w => w.WriteInt32(-70000), r => r.ReadInt32());
        yield return ("uint32", w => w.WriteUInt32(70000), r => r.ReadUInt32());
        yield return ("int64", w => w.WriteInt64(-1), r => r.ReadInt64());
        yield return ("uint64", w => w.WriteUInt64(ulong.MaxValue), r => r.ReadUInt64());
        yield return ("varuint", w => w.WriteVarUInt(ulong.MaxValue), r => r.ReadVarUInt());
        yield return ("varint", w => w.WriteVarInt(long.MinValue), r => r.ReadVarInt());
        yield return ("varuint32", w => w.WriteVarUInt(uint.MaxValue), r => r.ReadVarUInt32());
        yield return ("varint32", w => w.WriteVarInt(int.MinValue), r => r.ReadVarInt32());
        yield return ("float", w => w.WriteFloat(1.5f), r => r.ReadFloat());
        yield return ("double", w => w.WriteDouble(1.5), r => r.ReadDouble());
        yield return ("vector2", w => w.WriteVector2(1, 2), r => r.ReadVector2());
        yield return ("string", w => w.WriteString("héllo wörld"), r => r.ReadString());
        yield return ("blob", w => w.WriteBlob(new ArraySegment<byte>(new byte[] { 1, 2, 3, 4, 5 })), r => r.ReadBlob());
        yield return ("bytes", w => w.WriteBytes(new byte[] { 1, 2, 3 }, 0, 3), r => r.ReadBytes(3));
        yield return ("quantized 16", w => w.WriteQuantized(0.5f, 0, 1), r => r.ReadQuantized(0, 1));
        yield return ("quantized 20", w => w.WriteQuantized(0.5f, 0, 1, 20), r => r.ReadQuantized(0, 1, 20));
    }

    [Fact]
    public void Cut_messages_throw_NetReadException_never_IndexOutOfRange()
    {
        foreach (var (name, write, read) in AllReads())
        {
            var w = new NetWriter();
            write(w);
            var full = w.ToArray();
            read(new NetReader(full)); // the whole value reads
            for (int cut = 0; cut < full.Length; cut++)
            {
                // A segment inside a bigger array: the reader must not look past the segment's end.
                var padded = new byte[full.Length + 8];
                Buffer.BlockCopy(full, 0, padded, 3, full.Length);
                for (int i = full.Length + 3; i < padded.Length; i++)
                    padded[i] = 0x80;
                var r = new NetReader(new ArraySegment<byte>(padded, 3, cut));
                var e = Record.Exception(() => read(r));
                Assert.True(e is NetReadException, $"{name} cut to {cut} of {full.Length} bytes threw {e?.GetType().Name ?? "nothing"}");
            }
        }
    }

    [Fact]
    public void Remaining_and_position_follow_the_reads_inside_a_segment()
    {
        var bytes = new byte[] { 99, 1, 2, 0, 0, 0, 99 };
        var r = new NetReader(new ArraySegment<byte>(bytes, 1, 5));
        Assert.Equal(5, r.Length);
        Assert.Equal(5, r.Remaining);
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(1, r.Position);
        Assert.Equal(2u, r.ReadUInt32());
        Assert.Equal(0, r.Remaining);
        Assert.True(r.IsAtEnd);
        Assert.Throws<NetReadException>(() => r.ReadByte());

        r.Reset(default);
        Assert.Equal(0, r.Remaining);
        Assert.Throws<NetReadException>(() => r.ReadByte());
        Assert.Throws<NetReadException>(() => new NetReader(new byte[4]).ReadBytes(-1));
    }

    [Fact]
    public void The_rest_of_a_message_reads_as_one_segment()
    {
        var r = new NetReader(new byte[] { 1, 2, 3, 4 });
        r.ReadByte();
        Assert.Equal(new byte[] { 2, 3, 4 }, r.ReadRest().ToArray());
        Assert.True(r.IsAtEnd);
        Assert.Equal(0, r.ReadRest().Count);
    }

    [Fact]
    public void A_reset_writer_reuses_its_buffer_and_grows_when_needed()
    {
        var w = new NetWriter(16);
        Assert.Equal(16, w.Capacity);
        w.WriteUInt64(1);
        w.Reset();
        Assert.Equal(0, w.Length);
        w.WriteByte(5);
        var segment = w.ToSegment();
        Assert.Equal(0, segment.Offset);
        Assert.Equal(1, segment.Count);
        Assert.Equal(5, segment.Array[0]);

        for (int i = 0; i < 100; i++)
            w.WriteUInt32((uint)i);
        Assert.Equal(401, w.Length);
        Assert.True(w.Capacity >= 401);
        var r = Read(w);
        Assert.Equal(5, r.ReadByte());
        for (int i = 0; i < 100; i++)
            Assert.Equal((uint)i, r.ReadUInt32());
    }

    [Fact]
    public void Game_message_ids_start_at_32_and_the_sessions_own_are_below()
    {
        foreach (MessageType t in Enum.GetValues(typeof(MessageType)))
        {
            bool session = t is MessageType.None or MessageType.Hello or MessageType.Welcome or MessageType.Reject or
                MessageType.Roster or MessageType.Ping or MessageType.Pong or MessageType.Goodbye;
            Assert.Equal(!session, Protocol.IsGameMessage(t));
        }
        Assert.Equal(Protocol.FirstGameMessage, (byte)MessageType.PlayerState);
    }

    [Fact]
    public void The_mod_list_hash_ignores_order_case_spaces_and_repeats()
    {
        string a = Protocol.HashModList(new[] { "docrun.skulinsight_qol@0.17.0", "other.mod@1.2" });
        Assert.Equal(16, a.Length);
        Assert.Equal(a, Protocol.HashModList(new[] { " Other.Mod@1.2 ", "docrun.skulinsight_qol@0.17.0", "", null, "other.mod@1.2" }));
        Assert.NotEqual(a, Protocol.HashModList(new[] { "docrun.skulinsight_qol@0.17.1", "other.mod@1.2" }));
        Assert.NotEqual(a, Protocol.HashModList(new[] { "docrun.skulinsight_qol@0.17.0" }));
        // FNV-1a 64 of nothing is its offset basis; of "a" a known value (the server's Python computes the same).
        Assert.Equal("cbf29ce484222325", Protocol.HashModList(Array.Empty<string>()));
        Assert.Equal("af63dc4c8601ec8c", Protocol.HashModList(new[] { "A" }));
    }

    [Fact]
    public void Names_are_cleaned_for_the_roster()
    {
        Assert.Equal("DocRun", Protocol.CleanName("  Doc\nRun\t ", 1));
        Assert.Equal("Player 3", Protocol.CleanName("   ", 2));
        Assert.Equal("Player 1", Protocol.CleanName(null, 0));
        Assert.Equal(Protocol.MaxNameChars, Protocol.CleanName(new string('x', 100), 0).Length);
        // A surrogate pair across the cut is dropped whole.
        string skulls = new string('x', Protocol.MaxNameChars - 1) + "\U0001F480";
        string cut = Protocol.CleanName(skulls, 0);
        Assert.Equal(Protocol.MaxNameChars - 1, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[cut.Length - 1]));
    }
}
