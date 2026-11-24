using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>An enemy without a target of its own goes for the nearest standing player (COOP_LOG 68).</summary>
public class NoTargetPickTests
{
    [Fact]
    public void The_nearest_standing_player_is_picked_not_the_first()
    {
        // host (index 0) outside the arena, the client's copy next to the Veteran Thief
        Assert.Equal(1, NoTargetPick.Pick(new[] { 25f, 4f }, -1));
    }

    [Fact]
    public void Down_dead_or_gone_players_are_never_picked()
    {
        Assert.Equal(1, NoTargetPick.Pick(new[] { -1f, 30f }, -1));
        Assert.Equal(0, NoTargetPick.Pick(new[] { 30f, float.NaN }, 1));
        Assert.Equal(-1, NoTargetPick.Pick(new[] { -1f, float.NaN }, -1));
        Assert.Equal(-1, NoTargetPick.Pick(new float[0], 0));
    }

    [Fact]
    public void Ties_go_to_the_first_candidate()
    {
        Assert.Equal(0, NoTargetPick.Pick(new[] { 5f, 5f }, -1));
    }

    [Fact]
    public void The_last_pick_stays_while_it_is_not_much_farther()
    {
        Assert.Equal(1, NoTargetPick.Pick(new[] { 10f, 14f }, 1)); // 14 <= 10 * 1.5
        Assert.Equal(1, NoTargetPick.Pick(new[] { 0.5f, 3f }, 1)); // close by: within the slack
        Assert.Equal(0, NoTargetPick.Pick(new[] { 10f, 16f }, 1)); // much farther: the nearest takes over
        Assert.Equal(0, NoTargetPick.Pick(new[] { 10f, -1f }, 1)); // the last one went down
        Assert.Equal(0, NoTargetPick.Pick(new[] { 10f }, 3));      // a last index that no longer exists
    }
}
