using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The door vote on the wire (COOP_PLAN §4.3): a player's press, and the vote's state as the host tells it.</summary>
public class DoorVoteMessagesTests
{
    private sealed class Rolls : ICoopRandom
    {
        private readonly double _value;
        public Rolls(double value) => _value = value;
        public double NextDouble() => _value;
    }

    private static PartyState Party(int players)
    {
        var party = new PartyState();
        for (int slot = 0; slot < players; slot++)
            party.Join(slot);
        return party;
    }

    private static DoorVote Vote(PartyState party, double roll = 0.9) =>
        new(7, 2, party, new DoorVoteSettings(), new Rolls(roll), null);

    private static DoorVoteMessage RoundTrip(DoorVoteMessage message)
    {
        var w = new NetWriter();
        DoorVoteMessages.Write(w, message);
        var r = new NetReader(w.ToSegment());
        Assert.True(DoorVoteMessages.TryRead(r, out var read));
        Assert.True(r.IsAtEnd);
        return read;
    }

    [Fact]
    public void A_press_names_the_room_and_the_door()
    {
        var w = new NetWriter();
        DoorVoteMessages.Write(w, DoorVoteMessage.Press(0xDAE2F682, 1));
        Assert.Equal(6, w.ToSegment().Count);
        var read = RoundTrip(DoorVoteMessage.Press(0xDAE2F682, 1));
        Assert.Equal(DoorVoteMessageKind.Vote, read.Kind);
        Assert.Equal(0xDAE2F682u, read.Room);
        Assert.Equal(1, read.Door);
    }

    [Fact]
    public void An_open_vote_says_who_voted_and_how_long_the_others_have()
    {
        var vote = Vote(Party(3));
        Assert.Equal(VoteResponse.Accepted, vote.Vote(1, 0, 100));
        var read = RoundTrip(DoorVoteMessage.State(42, vote, 103.5));
        Assert.Equal(DoorVoteMessageKind.State, read.Kind);
        Assert.Equal(42u, read.Room);
        Assert.False(read.Resolved);
        Assert.Equal(-1, read.Door);
        Assert.Equal(new[] { -1, 0, -1, -1 }, read.Votes);
        Assert.Equal(6.5f, read.SecondsLeft, 2);
    }

    [Fact]
    public void A_coin_flip_names_the_door_and_its_chance()
    {
        var vote = Vote(Party(3), roll: 0.9); // 0.9 x 3 votes = 2.7: past door 0's two votes, door 1 wins
        vote.Vote(0, 0, 10);
        vote.Vote(1, 0, 11);
        vote.Vote(2, 1, 12);
        Assert.True(vote.IsResolved);
        var read = RoundTrip(DoorVoteMessage.State(42, vote, 12));
        Assert.True(read.Resolved);
        Assert.True(read.CoinFlip);
        Assert.False(read.TimedOut);
        Assert.Equal(1, read.Door);
        Assert.Equal(new[] { 0, 0, 1, -1 }, read.Votes);
        Assert.Equal(0.33f, read.Chance, 2);
        Assert.True(read.SecondsLeft < 0);
    }

    [Fact]
    public void Agreement_and_a_vote_that_ran_out_of_time()
    {
        var agreed = Vote(Party(2));
        agreed.Vote(0, 1, 10);
        agreed.Vote(1, 1, 11);
        var read = RoundTrip(DoorVoteMessage.State(1, agreed, 11));
        Assert.True(read.Resolved);
        Assert.False(read.CoinFlip);
        Assert.False(read.TimedOut);
        Assert.Equal(1, read.Door);
        Assert.Equal(1f, read.Chance, 2);

        var late = Vote(Party(2));
        late.Vote(0, 0, 10);
        Assert.False(late.Update(19.9));
        Assert.True(late.Update(20.1));
        read = RoundTrip(DoorVoteMessage.State(1, late, 20.1));
        Assert.True(read.Resolved);
        Assert.True(read.TimedOut);
        Assert.False(read.CoinFlip);
        Assert.Equal(0, read.Door);
        Assert.Equal(new[] { 0, -1, -1, -1 }, read.Votes);
    }

    [Fact]
    public void Ready_names_only_the_room()
    {
        var w = new NetWriter();
        DoorVoteMessages.Write(w, DoorVoteMessage.Done(0xDAE2F682));
        Assert.Equal(5, w.ToSegment().Count);
        var read = RoundTrip(DoorVoteMessage.Done(0xDAE2F682));
        Assert.Equal(DoorVoteMessageKind.Ready, read.Kind);
        Assert.Equal(0xDAE2F682u, read.Room);
        Assert.Equal(-1, read.Door);
    }

    [Fact]
    public void During_the_reward_wait_the_state_shows_kept_presses_who_is_busy_and_the_wait()
    {
        var party = Party(2);
        var gate = new RoomRewardGate(party, new RewardGateSettings());
        var vote = new DoorVote(7, 2, party, new DoorVoteSettings(), new Rolls(0.9), gate);
        gate.Begin(100);
        gate.MarkDone(0, 101);
        // slot 0 pressed door 1 while slot 1 still takes their reward: the rule does not take it yet
        Assert.Equal(VoteResponse.NotOpenYet, vote.Vote(0, 1, 101));
        var early = new[] { 1, -1, -1, -1 };
        int ready = 0b1101; // slot 1 still busy (slots 2 and 3 are empty and count as ready)
        var read = RoundTrip(DoorVoteMessage.State(9, vote, 101, early, ready, (float)gate.SecondsLeft(101)));
        Assert.False(read.Resolved);
        Assert.Equal(new[] { 1, -1, -1, -1 }, read.Votes);
        Assert.True(read.IsReady(0));
        Assert.False(read.IsReady(1));
        Assert.Equal(59f, read.GateSeconds, 1);
        Assert.True(read.SecondsLeft < 0, "the vote's countdown starts only after the wait");

        // the wait is over: the kept press counts, the countdown runs, and the state shows no wait any more
        gate.MarkDone(1, 120);
        Assert.True(gate.IsReleased);
        Assert.Equal(VoteResponse.Accepted, vote.Vote(0, 1, 120));
        read = RoundTrip(DoorVoteMessage.State(9, vote, 121));
        Assert.Equal(DoorVoteMessage.AllReady, read.ReadyMask);
        Assert.True(read.GateSeconds < 0);
        Assert.Equal(9f, read.SecondsLeft, 1);
        Assert.Equal(1, read.VoteOf(0));
    }

    [Fact]
    public void A_result_shows_no_wait_and_no_kept_presses()
    {
        var vote = Vote(Party(2));
        vote.Vote(0, 0, 10);
        vote.Vote(1, 0, 11);
        var read = RoundTrip(DoorVoteMessage.State(1, vote, 11, new[] { 1, 1, -1, -1 }, 0, 30f));
        Assert.True(read.Resolved);
        Assert.Equal(new[] { 0, 0, -1, -1 }, read.Votes);
        Assert.True(read.GateSeconds < 0);
    }

    [Fact]
    public void Broken_messages_are_not_read()
    {
        Assert.False(DoorVoteMessages.TryRead(new NetReader(new NetWriter().ToSegment()), out _));

        var w = new NetWriter();
        w.WriteByte(9); // an unknown kind
        w.WriteUInt32(1);
        w.WriteByte(0);
        Assert.False(DoorVoteMessages.TryRead(new NetReader(w.ToSegment()), out _));

        w = new NetWriter();
        w.WriteByte((byte)DoorVoteMessageKind.Vote);
        w.WriteUInt32(1);
        w.WriteByte(200); // no such door
        Assert.False(DoorVoteMessages.TryRead(new NetReader(w.ToSegment()), out _));

        w = new NetWriter();
        w.WriteByte((byte)DoorVoteMessageKind.State);
        w.WriteUInt32(1);
        w.WriteByte(0); // cut off after one vote
        Assert.False(DoorVoteMessages.TryRead(new NetReader(w.ToSegment()), out _));

        // "resolved" without a door
        w = new NetWriter();
        DoorVoteMessages.Write(w, new DoorVoteMessage
        {
            Kind = DoorVoteMessageKind.State, Room = 1, Votes = new[] { 0, -1, -1, -1 }, Resolved = true, Door = -1, SecondsLeft = -1f,
        });
        Assert.False(DoorVoteMessages.TryRead(new NetReader(w.ToSegment()), out _));
    }
}
