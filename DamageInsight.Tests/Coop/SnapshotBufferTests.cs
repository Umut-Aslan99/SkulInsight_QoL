using DamageInsight.Coop.Sync;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The time buffer for one remote entity (co-op §5.3): interpolation, extrapolation, order, teleports.</summary>
public class SnapshotBufferTests
{
    private static PlayerState At(float x, float y = 0, float vx = 0, float vy = 0) =>
        new() { Position = new Vec2(x, y), Velocity = new Vec2(vx, vy), Health = 100, MaxHealth = 100 };

    [Fact]
    public void Draws_the_straight_line_between_the_two_states_around_the_render_time()
    {
        var b = new SnapshotBuffer<PlayerState>();
        b.Add(1.0, At(0));
        b.Add(1.1, At(1));
        Assert.Equal(SampleKind.Interpolated, b.Sample(1.025, out var s));
        Assert.Equal(0.25f, s.Position.X, 4);
        Assert.Equal(SampleKind.Interpolated, b.Sample(1.075, out s));
        Assert.Equal(0.75f, s.Position.X, 4);
    }

    [Fact]
    public void Shows_the_first_state_while_the_render_time_is_still_before_it()
    {
        var b = new SnapshotBuffer<PlayerState>();
        Assert.Equal(SampleKind.Empty, b.Sample(0.5, out _));
        b.Add(1.0, At(3));
        Assert.Equal(SampleKind.Waiting, b.Sample(0.5, out var s));
        Assert.Equal(3f, s.Position.X);
    }

    [Fact]
    public void Moves_on_with_the_last_velocity_for_a_quarter_second_then_waits()
    {
        var b = new SnapshotBuffer<PlayerState>();
        b.Add(1.0, At(0, 0, 4));
        Assert.Equal(SampleKind.Extrapolated, b.Sample(1.1, out var s));
        Assert.Equal(0.4f, s.Position.X, 4);
        Assert.Equal(SampleKind.Extrapolated, b.Sample(1.25, out s));
        Assert.Equal(1.0f, s.Position.X, 4);
        Assert.Equal(SampleKind.Held, b.Sample(1.6, out s));
        Assert.Equal(1.0f, s.Position.X, 4); // never further than 0.25 s of movement
        Assert.Equal(SampleKind.Held, b.Sample(3.0, out s));
        Assert.Equal(1.0f, s.Position.X, 4);
    }

    [Fact]
    public void Sorts_late_states_in_and_ignores_duplicates_and_states_too_old_to_use()
    {
        var b = new SnapshotBuffer<PlayerState>();
        Assert.True(b.Add(1.2, At(2)));
        Assert.True(b.Add(1.0, At(0)));
        Assert.True(b.Add(1.1, At(1)));
        Assert.False(b.Add(1.1, At(9))); // duplicate
        Assert.Equal(3, b.Count);
        b.Sample(1.15, out var s);
        Assert.Equal(1.5f, s.Position.X, 4);
        Assert.Equal(2, b.Count); // 1.0 is no longer needed
        Assert.False(b.Add(1.05, At(9))); // older than the state being drawn from
        Assert.False(b.Add(1.1, At(9)));
        Assert.True(b.Add(1.15, At(1.5f))); // between the two: still useful
    }

    [Fact]
    public void A_full_buffer_drops_its_oldest_state()
    {
        var b = new SnapshotBuffer<PlayerState>(4);
        for (int i = 0; i < 6; i++)
            Assert.True(b.Add(i * 0.1, At(i)));
        Assert.Equal(4, b.Count);
        Assert.False(b.Add(0.05, At(0))); // older than everything kept
        Assert.Equal(0.5, b.NewestTime, 6);
    }

    [Fact]
    public void A_teleport_snaps_instead_of_sliding_across_the_room()
    {
        var b = new SnapshotBuffer<PlayerState>();
        b.Add(1.0, At(0));
        b.Add(1.1, At(20));
        b.Sample(1.05, out var s);
        Assert.Equal(0f, s.Position.X, 4); // no point in between
        Assert.False(b.Snapped);
        b.Add(1.2, At(20));
        b.Sample(1.12, out s);
        Assert.Equal(20f, s.Position.X, 4);
        Assert.True(b.Snapped);
    }

    [Fact]
    public void Real_data_after_a_guess_blends_in_instead_of_jumping()
    {
        var b = new SnapshotBuffer<PlayerState>();
        b.Add(1.0, At(0, 0, 4));
        b.Sample(1.2, out var guess); // extrapolated to x = 0.8
        Assert.Equal(0.8f, guess.Position.X, 4);
        // The truth: it had stopped at x = 0.2.
        b.Add(1.05, At(0.2f));
        b.Add(1.3, At(0.2f));
        b.Sample(1.2 + 1 / 60.0, out var s);
        Assert.False(b.Snapped);
        Assert.InRange(s.Position.X, 0.86f, 0.87f); // exactly where the guess would be now: no jump
        for (int f = 2; f <= 15; f++)
            b.Sample(1.2 + f / 60.0, out s);
        Assert.InRange(s.Position.X, 0.2f, 0.26f); // 0.23 s later: almost all blended out
        Assert.Equal(SampleKind.Held, b.Sample(2.0, out s));
        Assert.Equal(0.2f, s.Position.X, 3);
    }

    [Fact]
    public void A_motion_that_started_between_two_states_begins_when_it_started_on_the_sender()
    {
        var b = new SnapshotBuffer<PlayerState>();
        var walk = At(0); walk.MotionId = 1; walk.MotionTime = 2.0f; walk.FacingRight = true;
        var attack = At(1); attack.MotionId = 7; attack.MotionTime = 0.04f; attack.FacingRight = false;
        b.Add(1.0, walk);
        b.Add(1.1, attack); // the attack started at 1.06
        b.Sample(1.05, out var s);
        Assert.Equal(1, s.MotionId);
        Assert.Equal(2.05f, s.MotionTime, 4);
        Assert.True(s.FacingRight);
        b.Sample(1.08, out s);
        Assert.Equal(7, s.MotionId);
        Assert.Equal(0.02f, s.MotionTime, 4);
        Assert.False(s.FacingRight); // switches together with the motion
    }
}
