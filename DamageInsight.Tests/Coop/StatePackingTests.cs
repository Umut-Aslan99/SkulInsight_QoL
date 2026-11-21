using System;
using DamageInsight.Coop.Sync;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Compact player and enemy states (co-op §5.7: "positions packed small, only what changed").</summary>
public class StatePackingTests
{
    private static EnemyState Goblin(float x, float y = 2, float vx = 3, float vy = 0) => new()
    {
        NetId = 42, Position = new Vec2(x, y), Velocity = new Vec2(vx, vy), FacingRight = true,
        Flags = EnemyFlags.None, MotionId = 3, MotionTime = 0.5f, Health = 120.5f,
    };

    private static StateBytesReader ReaderOf(StateBytesWriter w) => new(w.ToArray());

    [Fact]
    public void A_player_state_round_trips_in_20_bytes()
    {
        var p = new PlayerState
        {
            Position = new Vec2(-123.456f, 17.03f), Velocity = new Vec2(8.2f, -14.9f), FacingRight = true,
            Grounded = false, Flags = PlayerFlags.Dashing | PlayerFlags.Invisible, MotionId = 12, MotionTime = 0.731f,
            Health = 87.25f, MaxHealth = 140,
        };
        var w = new StateBytesWriter();
        p.Write(w);
        Assert.Equal(20, w.Length);
        var r = ReaderOf(w);
        var q = PlayerState.Read(r);
        Assert.Equal(0, r.Remaining);
        Assert.InRange(Vec2.Distance(p.Position, q.Position), 0, 1 / 64f * 1.5f);
        Assert.InRange(Vec2.Distance(p.Velocity, q.Velocity), 0, 1 / 32f * 1.5f);
        Assert.True(q.FacingRight);
        Assert.False(q.Grounded);
        Assert.Equal(p.Flags, q.Flags);
        Assert.Equal(12, q.MotionId);
        Assert.Equal(0.731f, q.MotionTime, 3);
        Assert.Equal(87.25f, q.Health);
        Assert.Equal(140f, q.MaxHealth);
    }

    [Fact]
    public void Quantized_positions_are_never_more_than_a_64th_unit_off_and_clamp_at_the_room_edge()
    {
        var rng = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            var v = new Vec2((float)(rng.NextDouble() * 2000 - 1000), (float)(rng.NextDouble() * 2000 - 1000));
            var s = StatePacking.Snap(v, StatePacking.PositionScale);
            Assert.InRange(Math.Abs(s.X - v.X), 0, 1 / 64f + 1e-4f);
            Assert.InRange(Math.Abs(s.Y - v.Y), 0, 1 / 64f + 1e-4f);
        }
        Assert.Equal(short.MaxValue, StatePacking.Quantize(5000, StatePacking.PositionScale));
        Assert.Equal(short.MinValue, StatePacking.Quantize(-5000, StatePacking.PositionScale));
        Assert.Equal(0, StatePacking.Quantize(float.NaN, StatePacking.PositionScale));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(63, 1)]
    [InlineData(-64, 1)]
    [InlineData(64, 2)]
    [InlineData(1000, 2)]
    [InlineData(int.MaxValue, 5)]
    [InlineData(int.MinValue, 5)]
    public void Motion_ids_are_small_varints(int id, int bytes)
    {
        var w = new StateBytesWriter();
        StatePacking.WriteVarInt(w, id);
        Assert.Equal(bytes, w.Length);
        Assert.Equal(id, StatePacking.ReadVarInt(ReaderOf(w)));
    }

    [Fact]
    public void A_new_enemy_round_trips_whole()
    {
        var e = Goblin(10.3f);
        e.Flags = EnemyFlags.Stunned;
        var w = new StateBytesWriter();
        var sent = e.Write(w);
        Assert.InRange(w.Length, 15, 18);
        var got = EnemyState.Read(ReaderOf(w));
        Assert.Equal(42, got.NetId);
        Assert.Equal(sent.Position, got.Position);
        Assert.Equal(sent.Velocity, got.Velocity);
        Assert.True(got.FacingRight);
        Assert.Equal(EnemyFlags.Stunned, got.Flags);
        Assert.Equal(3, got.MotionId);
        Assert.Equal(0.5f, got.MotionTime, 3);
        Assert.Equal(120.5f, got.Health);
    }

    [Fact]
    public void The_rarer_states_cost_a_second_byte_only_when_one_is_on()
    {
        var plain = Goblin(10);
        plain.Flags = EnemyFlags.Grounded | EnemyFlags.Walking | EnemyFlags.Frozen;
        var w = new StateBytesWriter();
        plain.Write(w);
        int plainBytes = w.Length;
        Assert.Equal(plain.Flags, EnemyState.Read(ReaderOf(w)).Flags);

        foreach (var rare in new[] { EnemyFlags.Burning, EnemyFlags.Poisoned, EnemyFlags.Wounded,
                     EnemyFlags.Burning | EnemyFlags.Poisoned | EnemyFlags.Wounded })
        {
            var e = Goblin(10);
            e.Flags = plain.Flags | rare;
            w = new StateBytesWriter();
            var sent = e.Write(w);
            Assert.Equal(plainBytes + 1, w.Length);
            var got = EnemyState.Read(ReaderOf(w));
            Assert.Equal(e.Flags, got.Flags);
            Assert.Equal(e.Flags, sent.Flags);
            Assert.True(got.FacingRight);
        }
    }

    [Fact]
    public void A_state_of_the_second_byte_alone_and_its_end_reach_the_receiver()
    {
        var w = new StateBytesWriter();
        var baseline = Goblin(10).Write(w);

        var burning = Goblin(10);
        burning.FacingRight = false;
        burning.Flags = EnemyFlags.Burning;
        w = new StateBytesWriter();
        var sent = burning.WriteDelta(w, baseline, 0f);
        var r = ReaderOf(w);
        var got = EnemyState.ReadDelta(r, EnemyState.ReadNetId(r), baseline, 0f);
        Assert.Equal(EnemyFlags.Burning, got.Flags);
        Assert.False(got.FacingRight);

        var over = Goblin(10);
        over.FacingRight = false;
        w = new StateBytesWriter();
        over.WriteDelta(w, sent, 0f);
        r = ReaderOf(w);
        got = EnemyState.ReadDelta(r, EnemyState.ReadNetId(r), got, 0f);
        Assert.Equal(EnemyFlags.None, got.Flags);
    }

    [Fact]
    public void A_walking_enemy_costs_5_bytes_and_a_falling_one_7()
    {
        var w = new StateBytesWriter();
        var baseline = Goblin(10).Write(w);
        w.Clear();
        var walking = Goblin(10.15f);
        walking.MotionTime = 0.55f; // its walk cycle ran on by the elapsed time: not sent
        walking.WriteDelta(w, baseline, 0.05f);
        Assert.Equal(5, w.Length); // id 2 + mask 1 + position step 2

        w.Clear();
        var falling = Goblin(10.15f, 1.6f, 3, -2.5f);
        falling.MotionTime = 0.55f;
        falling.WriteDelta(w, baseline, 0.05f);
        Assert.Equal(7, w.Length); // + velocity step 2

        w.Clear();
        var attacking = Goblin(10.15f);
        attacking.MotionId = 9;
        attacking.MotionTime = 0.01f;
        attacking.WriteDelta(w, baseline, 0.05f);
        Assert.Equal(8, w.Length); // + motion id 1 + motion time 2

        w.Clear();
        Goblin(10).WriteDelta(w, baseline, 0f);
        Assert.Equal(3, w.Length); // standing still: id and an empty mask
    }

    [Fact]
    public void Deltas_decode_to_exactly_what_the_sender_kept_as_the_next_baseline()
    {
        var w = new StateBytesWriter();
        EnemyState senderBase = default, receiverBase = default;
        var rng = new Random(6);
        var truth = Goblin(0);
        for (int tick = 0; tick < 400; tick++)
        {
            truth.Position += truth.Velocity * 0.05f;
            truth.MotionTime += 0.05f;
            if (rng.NextDouble() < 0.1)
                truth.Velocity = new Vec2((float)(rng.NextDouble() * 20 - 10), (float)(rng.NextDouble() * 20 - 10));
            if (rng.NextDouble() < 0.05)
            {
                truth.MotionId = rng.Next(0, 200);
                truth.MotionTime = 0;
            }
            if (rng.NextDouble() < 0.05)
                truth.Position = new Vec2((float)(rng.NextDouble() * 400 - 200), 3); // teleport: full position
            if (rng.NextDouble() < 0.05)
                truth.Health -= 7.5f;
            truth.FacingRight = truth.Velocity.X >= 0;

            w.Clear();
            float elapsed = tick == 0 ? 0f : 0.05f;
            senderBase = truth.WriteDelta(w, senderBase, elapsed);
            var r = ReaderOf(w);
            receiverBase = EnemyState.ReadDelta(r, EnemyState.ReadNetId(r), receiverBase, elapsed);
            Assert.Equal(0, r.Remaining);
            Assert.Equal(senderBase.Position, receiverBase.Position);
            Assert.Equal(senderBase.Velocity, receiverBase.Velocity);
            Assert.Equal(senderBase.MotionId, receiverBase.MotionId);
            Assert.Equal(senderBase.MotionTime, receiverBase.MotionTime);
            Assert.Equal(senderBase.Health, receiverBase.Health);
            Assert.Equal(senderBase.FacingRight, receiverBase.FacingRight);
            // ...and that stays close to the truth (no drift over 400 deltas).
            Assert.InRange(Vec2.Distance(truth.Position, receiverBase.Position), 0, 1 / 64f * 1.5f);
            Assert.InRange(Vec2.Distance(truth.Velocity, receiverBase.Velocity), 0, 1 / 32f * 1.5f);
            Assert.InRange(Math.Abs(truth.MotionTime - receiverBase.MotionTime), 0, EnemyState.MotionTimeTolerance + 0.001f);
            Assert.Equal(truth.MotionId, receiverBase.MotionId);
            Assert.Equal(truth.Health, receiverBase.Health);
        }
    }

    [Fact]
    public void Reading_past_the_end_throws()
    {
        var r = new StateBytesReader(new byte[] { 1 });
        Assert.Equal(1, r.ReadByte());
        Assert.Throws<InvalidOperationException>(() => r.ReadByte());
    }
}
