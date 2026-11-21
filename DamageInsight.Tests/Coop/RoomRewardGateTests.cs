using System.Collections.Generic;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The wait for everyone's room reward (COOP_PLAN §4.2: "at most 60 s, with a banner").</summary>
public class RoomRewardGateTests
{
    private static PartyState Party(int players)
    {
        var party = new PartyState();
        for (int i = 0; i < players; i++)
            party.Join(i);
        return party;
    }

    [Fact]
    public void Waits_until_every_living_player_took_or_left_their_reward()
    {
        var gate = new RoomRewardGate(Party(2), new RewardGateSettings());
        Assert.False(gate.Update(0)); // not begun: nothing to wait for yet, nothing released either
        gate.Begin(10);
        var waiting = new List<int>();
        gate.Waiting(waiting);
        Assert.Equal(new[] { 0, 1 }, waiting);

        gate.MarkDone(0, 12);
        Assert.False(gate.IsReleased);
        gate.Waiting(waiting);
        Assert.Equal(new[] { 1 }, waiting);
        Assert.True(gate.IsWaitingFor(1));

        gate.MarkDone(1, 15);
        Assert.True(gate.IsReleased);
        Assert.False(gate.TimedOut);
        Assert.Equal(15, gate.ReleasedAt);
        Assert.False(gate.Update(16)); // true only on the releasing call (here inside MarkDone)
    }

    [Fact]
    public void Gives_up_after_sixty_seconds_with_the_time_left_for_the_banner()
    {
        var gate = new RoomRewardGate(Party(2), new RewardGateSettings());
        gate.Begin(0);
        gate.MarkDone(0, 1);
        Assert.Equal(42, gate.SecondsLeft(18), 6); // "Waiting for DocRun's reward: 42 s"
        Assert.False(gate.Update(59.9));
        Assert.True(gate.Update(60));
        Assert.True(gate.TimedOut);
        Assert.Equal(0, gate.SecondsLeft(61));
        var waiting = new List<int> { 99 };
        gate.Waiting(waiting);
        Assert.Empty(waiting); // released: the banner names nobody any more
        Assert.False(gate.IsWaitingFor(1));
    }

    [Fact]
    public void Players_who_disconnect_dont_block()
    {
        var party = Party(3);
        var gate = new RoomRewardGate(party, new RewardGateSettings());
        gate.Begin(0);
        gate.MarkDone(0, 1);
        gate.MarkDone(2, 1);
        party.Leave(1);
        Assert.True(gate.Update(2));
        Assert.False(gate.TimedOut);
    }

    [Fact]
    public void A_player_who_comes_back_while_it_waits_blocks_again()
    {
        var party = Party(2);
        var gate = new RoomRewardGate(party, new RewardGateSettings());
        gate.Begin(0);
        party.Leave(1);
        party.Join(1);
        gate.MarkDone(0, 1);
        Assert.False(gate.IsReleased);
        Assert.True(gate.IsWaitingFor(1));
    }

    [Fact]
    public void Down_players_and_late_joiners_dont_block()
    {
        var party = Party(2);
        party.SetDown(1, true);
        var gate = new RoomRewardGate(party, new RewardGateSettings());
        gate.Begin(0);
        party.Join(2); // joins after the rewards appeared
        Assert.False(gate.IsWaitingFor(2));
        gate.MarkDone(0, 1);
        Assert.True(gate.IsReleased);
    }

    [Fact]
    public void A_room_without_rewards_releases_at_once()
    {
        var gate = new RoomRewardGate(Party(2), new RewardGateSettings());
        gate.ReleaseNow(5);
        Assert.True(gate.IsReleased);
        Assert.False(gate.TimedOut);
    }

    [Fact]
    public void The_longest_wait_is_a_setting()
    {
        var gate = new RoomRewardGate(Party(2), new RewardGateSettings { MaxWaitSeconds = 5 });
        gate.Begin(0);
        Assert.True(gate.Update(5));
        Assert.True(gate.TimedOut);

        var never = new RoomRewardGate(Party(2), new RewardGateSettings { MaxWaitSeconds = 0 });
        never.Begin(0);
        Assert.True(never.IsReleased);
    }
}
