using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>A player's slow motion on the wire (COOP_PLAN 4.5, NEXT_SPECS A): every game slows its room's enemies
/// for the effect's seconds; a refresh with the same key moves the end, an end message stops it.</summary>
public class TimeEffectMessagesTests
{
    private static TimeEffect RoundTrip(TimeEffect effect)
    {
        var w = new NetWriter();
        TimeEffectMessages.Write(w, effect);
        Assert.Equal(TimeEffectMessages.Size, w.ToSegment().Count);
        var r = new NetReader(w.ToSegment());
        Assert.True(TimeEffectMessages.TryRead(r, out var read));
        Assert.True(r.IsAtEnd);
        return read;
    }

    private static TimeEffect Effect(uint key, float scale, float seconds) =>
        new() { Room = 0xDAE2F682, Key = key, Scale = scale, Seconds = seconds };

    [Fact]
    public void An_effect_arrives_as_it_was_sent()
    {
        uint key = TimeEffect.KeyFor(1, 0x12345678);
        var read = RoundTrip(Effect(key, 0.35f, 8.5f));
        Assert.Equal(0xDAE2F682u, read.Room);
        Assert.Equal(key, read.Key);
        Assert.Equal(1, read.SourceSlot);
        Assert.Equal(0.35f, read.Scale);
        Assert.Equal(8.5f, read.Seconds);
        Assert.False(read.Ends);
        Assert.False(read.UntilEnded);
    }

    [Fact]
    public void The_key_holds_the_source_slot_and_the_low_bits_of_the_source()
    {
        Assert.Equal(0x03345678u, TimeEffect.KeyFor(3, 0x12345678));
        Assert.Equal(0, new TimeEffect { Key = TimeEffect.KeyFor(0, -1) }.SourceSlot);
        Assert.NotEqual(TimeEffect.KeyFor(0, 77), TimeEffect.KeyFor(1, 77)); // two games' sources never share a key
    }

    [Fact]
    public void An_end_and_an_effect_until_ended_survive_the_wire()
    {
        Assert.True(RoundTrip(Effect(5, 1f, 0f)).Ends);
        var held = RoundTrip(Effect(5, 0.5f, -7f));
        Assert.True(held.UntilEnded);
        Assert.Equal(-1f, held.Seconds);
    }

    [Fact]
    public void Writing_clamps_what_cannot_be()
    {
        var read = RoundTrip(Effect(5, 99f, 5000f));
        Assert.Equal(TimeEffectMessages.MaxScale, read.Scale);
        Assert.Equal(TimeEffectMessages.MaxSeconds, read.Seconds);
        Assert.Equal(1f, RoundTrip(Effect(5, float.NaN, 1f)).Scale);
        Assert.True(RoundTrip(Effect(5, 0.5f, float.NaN)).Ends);
        Assert.Equal(0f, RoundTrip(Effect(5, -2f, 1f)).Scale);
    }

    [Fact]
    public void A_short_or_impossible_message_is_refused()
    {
        var w = new NetWriter();
        w.WriteUInt32(1);
        w.WriteUInt32(2);
        w.WriteFloat(0.5f);
        Assert.False(TimeEffectMessages.TryRead(new NetReader(w.ToSegment()), out _));

        w.Reset();
        w.WriteUInt32(1);
        w.WriteUInt32(2);
        w.WriteFloat(-0.5f); // a scale below 0
        w.WriteFloat(1f);
        Assert.False(TimeEffectMessages.TryRead(new NetReader(w.ToSegment()), out _));

        w.Reset();
        w.WriteUInt32(1);
        w.WriteUInt32(2);
        w.WriteFloat(0.5f);
        w.WriteFloat(float.NaN);
        Assert.False(TimeEffectMessages.TryRead(new NetReader(w.ToSegment()), out _));
    }

    [Fact]
    public void An_effect_runs_for_its_seconds_and_a_refresh_moves_the_end()
    {
        var table = new TimeEffectTable();
        Assert.True(table.Set(Effect(7, 0.5f, 2f), 100));
        Assert.True(table.TryGetScale(7, out float scale));
        Assert.Equal(0.5f, scale);
        Assert.Empty(table.Expire(101.5));
        Assert.True(table.Set(Effect(7, 0.4f, 2f), 101.5)); // refreshed: now until 103.5, the new scale
        Assert.Empty(table.Expire(102.5));
        Assert.True(table.TryGetScale(7, out scale));
        Assert.Equal(0.4f, scale);
        Assert.Equal(new uint[] { 7 }, table.Expire(103.5));
        Assert.Equal(0, table.Count);
        Assert.False(table.TryGetScale(7, out scale));
        Assert.Equal(1f, scale);
    }

    [Fact]
    public void An_end_message_stops_only_its_own_effect()
    {
        var table = new TimeEffectTable();
        table.Set(Effect(TimeEffect.KeyFor(0, 1), 0.5f, -1f), 0);
        table.Set(Effect(TimeEffect.KeyFor(1, 1), 0.5f, 3f), 0);
        Assert.False(table.Set(Effect(TimeEffect.KeyFor(0, 1), 1f, 0f), 1));
        Assert.Equal(1, table.Count);
        Assert.True(table.TryGetScale(TimeEffect.KeyFor(1, 1), out _));
        Assert.False(table.Set(Effect(99, 1f, 0f), 1)); // an end for nothing running is harmless
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void An_effect_until_ended_stops_by_itself_after_the_cap()
    {
        var table = new TimeEffectTable();
        table.Set(Effect(9, 0.5f, -1f), 10);
        Assert.Empty(table.Expire(10 + TimeEffectTable.UntilEndedCap - 1));
        Assert.Equal(new uint[] { 9 }, table.Expire(10 + TimeEffectTable.UntilEndedCap));
        table.Set(Effect(9, 0.5f, 500f), 0); // no effect outlasts the cap
        Assert.Equal(new uint[] { 9 }, table.Expire(TimeEffectTable.UntilEndedCap));
    }
}
