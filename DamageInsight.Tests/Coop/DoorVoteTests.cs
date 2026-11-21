using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>A random that always gives the same number (to pick a coin flip's side on purpose).</summary>
internal sealed class FixedRandom : ICoopRandom
{
    private readonly double _value;
    public FixedRandom(double value) => _value = value;
    public double NextDouble() => _value;
}

/// <summary>The door vote (COOP_PLAN §4.3, Umut: "a coin flip, no long waits").</summary>
public class DoorVoteTests
{
    private static PartyState Party(int players)
    {
        var party = new PartyState();
        for (int i = 0; i < players; i++)
            party.Join(i);
        return party;
    }

    private static DoorVote Vote(PartyState party, ICoopRandom random = null, RoomRewardGate gate = null, int doors = 2,
        DoorVoteSettings settings = null) =>
        new(7, doors, party, settings ?? new DoorVoteSettings(), random ?? new FixedRandom(0), gate);

    [Fact]
    public void The_same_door_from_everyone_goes_at_once_without_a_flip()
    {
        var vote = Vote(Party(2));
        Assert.Equal(VoteResponse.Accepted, vote.Vote(0, 1, 0));
        Assert.False(vote.IsResolved);
        Assert.Equal(10, vote.SecondsLeft(0));
        Assert.Equal(VoteResponse.Accepted, vote.Vote(1, 1, 2));

        var r = vote.Result;
        Assert.NotNull(r);
        Assert.Equal(7, r.RoomIndex);
        Assert.Equal(1, r.Door);
        Assert.False(r.CoinFlip);
        Assert.False(r.TimedOut);
        Assert.Equal(2, r.ResolvedAt);
        Assert.Equal(new[] { 1, 1, -1, -1 }, r.Votes);
        Assert.Equal(1.0, r.ChanceOf(1));
        Assert.Equal(VoteResponse.AlreadyResolved, vote.Vote(0, 0, 3));
    }

    [Theory]
    [InlineData(0.2, 0)]
    [InlineData(0.7, 1)]
    public void Different_doors_flip_a_coin_at_once(double roll, int door)
    {
        var vote = Vote(Party(2), new FixedRandom(roll));
        vote.Vote(0, 0, 0);
        vote.Vote(1, 1, 0.5);
        Assert.True(vote.IsResolved);
        Assert.True(vote.Result.CoinFlip);
        Assert.False(vote.Result.TimedOut);
        Assert.Equal(door, vote.Result.Door);
        Assert.Equal(0.5, vote.Result.ChanceOf(0));
        Assert.Equal(0, vote.Result.VoteOf(0));
        Assert.Equal(1, vote.Result.VoteOf(1));
    }

    [Fact]
    public void The_coin_flip_is_the_same_for_the_same_run_seed_and_room_and_fair_over_many_rooms()
    {
        int door1 = 0;
        for (int room = 0; room < 400; room++)
        {
            int a = Flip(CoopRandom.ForRoom(1234, room, CoopRandom.Purpose.DoorFlip));
            int b = Flip(CoopRandom.ForRoom(1234, room, CoopRandom.Purpose.DoorFlip));
            Assert.Equal(a, b);
            door1 += a;
        }
        Assert.InRange(door1, 160, 240); // about half

        static int Flip(ICoopRandom random)
        {
            var vote = Vote(Party(2), random);
            vote.Vote(0, 0, 0);
            vote.Vote(1, 1, 0);
            return vote.Result.Door;
        }
    }

    [Theory]
    [InlineData(0.6, 0)] // 0.6 x 3 votes = 1.8 < 2 votes for door 0
    [InlineData(0.7, 1)]
    public void With_three_players_the_flip_is_weighted_by_votes(double roll, int door)
    {
        var vote = Vote(Party(3), new FixedRandom(roll));
        vote.Vote(0, 0, 0);
        vote.Vote(1, 1, 0);
        vote.Vote(2, 0, 0);
        Assert.True(vote.Result.CoinFlip);
        Assert.Equal(door, vote.Result.Door);
        Assert.Equal(2.0 / 3, vote.Result.ChanceOf(0), 6);
        Assert.Equal(2, vote.Result.VotesFor(0));
    }

    [Fact]
    public void Three_doors_flip_only_between_the_voted_ones()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var vote = Vote(Party(2), new CoopRandom((ulong)seed), doors: 3);
            vote.Vote(0, 0, 0);
            vote.Vote(1, 2, 0);
            Assert.NotEqual(1, vote.Result.Door);
        }
    }

    [Fact]
    public void A_vote_can_change_until_the_vote_is_resolved()
    {
        var vote = Vote(Party(3));
        vote.Vote(0, 0, 0);
        Assert.Equal(VoteResponse.Unchanged, vote.Vote(0, 0, 1));
        Assert.Equal(VoteResponse.Accepted, vote.Vote(0, 1, 2));
        Assert.Equal(1, vote.VoteOf(0));
        Assert.Equal(8, vote.SecondsLeft(2)); // a change doesn't restart the countdown
        vote.Withdraw(0, 3);
        Assert.Equal(-1, vote.VoteOf(0));
        vote.Vote(1, 1, 4);
        vote.Vote(2, 1, 4);
        vote.Vote(0, 1, 4);
        Assert.Equal(1, vote.Result.Door);
        Assert.False(vote.Result.CoinFlip);
    }

    [Fact]
    public void After_the_first_vote_the_others_have_ten_seconds_then_the_votes_that_exist_decide()
    {
        var vote = Vote(Party(2));
        vote.Vote(0, 1, 100);
        Assert.False(vote.Update(109.9));
        Assert.Equal(5, vote.SecondsLeft(105), 6);
        Assert.True(vote.Update(110));
        Assert.Equal(1, vote.Result.Door);
        Assert.True(vote.Result.TimedOut);
        Assert.False(vote.Result.CoinFlip);
        Assert.Equal(-1, vote.Result.VoteOf(1));
        Assert.False(vote.Update(111)); // true only once
    }

    [Fact]
    public void A_timeout_with_different_votes_still_flips()
    {
        var vote = Vote(Party(3), new FixedRandom(0.9));
        vote.Vote(0, 0, 0);
        vote.Vote(1, 1, 1);
        vote.Update(10);
        Assert.True(vote.Result.TimedOut);
        Assert.True(vote.Result.CoinFlip);
        Assert.Equal(1, vote.Result.Door);
    }

    [Fact]
    public void The_vote_time_is_a_setting()
    {
        var vote = Vote(Party(2), settings: new DoorVoteSettings { VoteSeconds = 3 });
        vote.Vote(0, 0, 0);
        Assert.True(vote.Update(3));
    }

    [Fact]
    public void Down_players_dont_vote_and_dont_hold_the_others_up()
    {
        var party = Party(2);
        party.SetDown(1, true);
        var vote = Vote(party);
        Assert.Equal(VoteResponse.NotAllowed, vote.Vote(1, 0, 0));
        Assert.Equal(VoteResponse.Accepted, vote.Vote(0, 1, 0));
        Assert.Equal(1, vote.Result.Door);
        Assert.False(vote.Result.TimedOut);
    }

    [Fact]
    public void A_player_leaving_mid_vote_lets_the_others_go_at_once()
    {
        var party = Party(3);
        var vote = Vote(party);
        vote.Vote(0, 1, 0);
        vote.Vote(1, 1, 1);
        party.Leave(2);
        Assert.True(vote.Update(2));
        Assert.Equal(1, vote.Result.Door);
        Assert.False(vote.Result.TimedOut);
    }

    [Fact]
    public void The_vote_of_a_player_who_left_doesnt_count_and_with_no_votes_left_the_countdown_waits()
    {
        var party = Party(2);
        var vote = Vote(party);
        vote.Vote(0, 0, 0);
        party.Leave(0);
        Assert.False(vote.Update(5));
        Assert.False(vote.Update(20)); // nothing to decide: no door without a vote
        Assert.Equal(-1, vote.SecondsLeft(20));
        vote.Vote(1, 1, 21);
        Assert.Equal(1, vote.Result.Door);
        Assert.Equal(-1, vote.Result.VoteOf(0));
    }

    [Fact]
    public void The_doors_open_for_votes_only_after_the_room_rewards()
    {
        var party = Party(2);
        var gate = new RoomRewardGate(party, new RewardGateSettings());
        gate.Begin(0);
        var vote = Vote(party, gate: gate);
        Assert.False(vote.IsOpen);
        Assert.Equal(VoteResponse.NotOpenYet, vote.Vote(0, 0, 1));
        gate.MarkDone(0, 2);
        gate.MarkDone(1, 3);
        Assert.True(vote.IsOpen);
        Assert.Equal(VoteResponse.Accepted, vote.Vote(0, 0, 4));
        Assert.Equal(VoteResponse.Accepted, vote.Vote(1, 0, 5));
        Assert.Equal(0, vote.Result.Door);
    }

    [Fact]
    public void Doors_that_dont_exist_are_refused()
    {
        var vote = Vote(Party(1));
        Assert.Equal(VoteResponse.InvalidDoor, vote.Vote(0, 2, 0));
        Assert.Equal(VoteResponse.InvalidDoor, vote.Vote(0, -1, 0));
        Assert.Equal(VoteResponse.NotAllowed, vote.Vote(3, 0, 0)); // nobody in slot 3
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Vote(Party(1), doors: 0));
    }

    [Fact]
    public void A_player_alone_goes_through_their_door_at_once()
    {
        var vote = Vote(Party(1));
        vote.Vote(0, 0, 0);
        Assert.Equal(0, vote.Result.Door);
        Assert.False(vote.Result.TimedOut);
    }
}
