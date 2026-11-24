using System.Collections.Generic;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Sync;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Host-run enemies: snapshots, events and hits (phase 1 step 3).</summary>
public class EnemyMessagesTests
{
    private static NetReader Reader(NetWriter w) => new(w.ToSegment());

    [Fact]
    public void A_snapshot_round_trips_every_enemy()
    {
        var states = new List<EnemyState>
        {
            new() { NetId = 1, Position = new Vec2(10.5f, 2f), Velocity = new Vec2(-3f, 0f), FacingRight = true, Health = 120f },
            new() { NetId = 7, Position = new Vec2(-40f, 12.25f), Flags = EnemyFlags.Stunned, Health = 3.5f },
        };
        var w = new NetWriter();
        EnemyMessages.WriteSnapshot(w, 0xABCD, 12.5, states);
        var read = new List<EnemyState>();
        EnemyMessages.ReadSnapshot(Reader(w), out uint room, out double time, read);
        Assert.Equal(0xABCDu, room);
        Assert.Equal(12.5, time);
        Assert.Equal(2, read.Count);
        Assert.Equal(7, read[1].NetId);
        Assert.Equal(10.5f, read[0].Position.X, 2);
        Assert.Equal(-3f, read[0].Velocity.X, 1);
        Assert.True(read[0].FacingRight);
        Assert.Equal(EnemyFlags.Stunned, read[1].Flags);
        Assert.Equal(3.5f, read[1].Health);
    }

    [Theory]
    [InlineData(EnemyEventKind.Motion)]
    [InlineData(EnemyEventKind.Died)]
    [InlineData(EnemyEventKind.WaveSpawn)]
    [InlineData(EnemyEventKind.RoomCleared)]
    [InlineData(EnemyEventKind.Behaviour)]
    [InlineData(EnemyEventKind.Phase)]
    [InlineData(EnemyEventKind.Call)]
    [InlineData(EnemyEventKind.Summon)]
    [InlineData(EnemyEventKind.Target)]
    [InlineData(EnemyEventKind.DarkElite)]
    public void Every_event_kind_round_trips(EnemyEventKind kind)
    {
        var e = new EnemyEvent { Kind = kind, NetId = 33, Wave = 4, ActionPath = "AI/Attack/Slash", MotionIndex = 1, Speed = 0.8f, TargetSlot = 1, Extra = 2, Seq = 513, X = -3.5f, Y = 12.25f };
        var w = new NetWriter();
        EnemyMessages.WriteEvent(w, 9, 3.25, e);
        var read = EnemyMessages.ReadEvent(Reader(w), out uint room, out double time);
        Assert.Equal(9u, room);
        Assert.Equal(3.25, time);
        Assert.Equal(kind, read.Kind);
        if (kind == EnemyEventKind.WaveSpawn || kind == EnemyEventKind.Summon)
            Assert.Equal(4, read.Wave);
        if (kind == EnemyEventKind.Summon)
        {
            Assert.Equal("AI/Attack/Slash", read.ActionPath);
            Assert.Equal(-3.5f, read.X);
            Assert.Equal(12.25f, read.Y);
            Assert.Equal(2, read.Extra);
        }
        if (kind is not (EnemyEventKind.RoomCleared or EnemyEventKind.WaveSpawn))
            Assert.Equal(33, read.NetId);
        if (kind == EnemyEventKind.Behaviour)
        {
            Assert.Equal("AI/Attack/Slash", read.ActionPath);
            Assert.Equal(1, read.TargetSlot);
        }
        if (kind is EnemyEventKind.Target or EnemyEventKind.Died) // Died: the slot of the player whose hit killed it
            Assert.Equal(1, read.TargetSlot);
        if (kind is EnemyEventKind.Behaviour or EnemyEventKind.Phase or EnemyEventKind.Call or EnemyEventKind.Summon)
            Assert.Equal(2, read.Extra);
        if (kind == EnemyEventKind.DarkElite) // the abilities' names, the Dark Mirror level
        {
            Assert.Equal("AI/Attack/Slash", read.ActionPath);
            Assert.Equal(2, read.Extra);
        }
        if (kind == EnemyEventKind.Call)
        {
            Assert.Equal("AI/Attack/Slash", read.ActionPath);
            Assert.Equal(1, read.TargetSlot);
            Assert.Equal(0.8f, read.Speed);
            Assert.Equal(513, read.Seq);
        }
        if (kind == EnemyEventKind.Motion)
        {
            Assert.Equal(-3.5f, read.X);
            Assert.Equal(12.25f, read.Y);
            Assert.Equal("AI/Attack/Slash", read.ActionPath);
            Assert.Equal(1, read.MotionIndex);
            Assert.Equal(0.8f, read.Speed);
            Assert.Equal(1, read.TargetSlot);
        }
    }

    [Fact]
    public void A_motion_carries_the_AI_markers_and_reads_without_them_too()
    {
        var e = new EnemyEvent
        {
            Kind = EnemyEventKind.Motion, NetId = 7, ActionPath = "Brain/Prepare", MotionIndex = 0, Speed = 1f,
            TargetSlot = 1, X = 4f, Y = 2f,
            Markers = new List<EnemyMarker> { new() { Index = 3, X = 18.5f, Y = 1.25f }, new() { Index = 9, X = -2f, Y = 0f } },
        };
        var w = new NetWriter();
        EnemyMessages.WriteEvent(w, 5, 1.0, e);
        var read = EnemyMessages.ReadEvent(Reader(w), out _, out _);
        Assert.Equal(2, read.Markers.Count);
        Assert.Equal(3, read.Markers[0].Index);
        Assert.Equal(18.5f, read.Markers[0].X);
        Assert.Equal(1.25f, read.Markers[0].Y);
        Assert.Equal(9, read.Markers[1].Index);
        Assert.Equal(-2f, read.Markers[1].X);

        // without markers (and what a 0.17.0 host sends): no tail, nothing read
        e.Markers = null;
        w = new NetWriter();
        EnemyMessages.WriteEvent(w, 5, 1.0, e);
        int plain = w.ToSegment().Count;
        read = EnemyMessages.ReadEvent(Reader(w), out _, out _);
        Assert.Null(read.Markers);
        Assert.Equal(4f, read.X);
        Assert.Equal(2f, read.Y);
        e.Markers = new List<EnemyMarker>();
        w = new NetWriter();
        EnemyMessages.WriteEvent(w, 5, 1.0, e);
        Assert.Equal(plain, w.ToSegment().Count); // an empty list writes no tail either
    }

    [Fact]
    public void An_unknown_event_kind_is_skipped()
    {
        var w = new NetWriter();
        w.WriteUInt32(1);
        w.WriteDouble(0);
        w.WriteByte(99);
        Assert.Null(EnemyMessages.ReadEvent(Reader(w), out _, out _));
    }

    [Fact]
    public void A_hit_round_trips_with_its_critical_and_types()
    {
        var hit = new EnemyHit { NetId = 12, Amount = 345.75, Critical = true, AttackType = 2, MotionType = 3, Attribute = 1, StoppingPower = 0.35f };
        var w = new NetWriter();
        EnemyMessages.WriteHit(w, 77, hit);
        var read = EnemyMessages.ReadHit(Reader(w), out uint room);
        Assert.Equal(77u, room);
        Assert.Equal(hit, read);
    }

    [Fact]
    public void A_status_round_trips()
    {
        var w = new NetWriter();
        EnemyMessages.WriteStatus(w, 0xABCD1234, 515, 1);
        EnemyMessages.ReadStatus(Reader(w), out uint room, out ushort id, out byte kind);
        Assert.Equal(0xABCD1234u, room);
        Assert.Equal((ushort)515, id);
        Assert.Equal((byte)1, kind);
    }
}
