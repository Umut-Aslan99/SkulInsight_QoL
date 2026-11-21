using System.Collections.Generic;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Going down and revive on the wire (COOP_PLAN §4.4): what a player reports and what the host decides.</summary>
public class ReviveMessagesTests
{
    private static ReviveMessage RoundTrip(ReviveMessage message)
    {
        var w = new NetWriter();
        ReviveMessages.Write(w, message);
        var r = new NetReader(w.ToSegment());
        Assert.True(ReviveMessages.TryRead(r, out var read));
        Assert.True(r.IsAtEnd);
        return read;
    }

    [Fact]
    public void I_went_down_is_one_byte()
    {
        var w = new NetWriter();
        ReviveMessages.Write(w, ReviveMessage.Down());
        Assert.Equal(1, w.ToSegment().Count);
        Assert.Equal(ReviveMessageKind.Down, RoundTrip(ReviveMessage.Down()).Kind);
    }

    [Fact]
    public void A_request_names_the_downed_player()
    {
        var read = RoundTrip(ReviveMessage.Request(2));
        Assert.Equal(ReviveMessageKind.Request, read.Kind);
        Assert.Equal(2, read.Slot);
    }

    [Fact]
    public void The_hosts_events_arrive_as_the_rules_made_them()
    {
        var party = new PartyState();
        party.Join(0);
        party.Join(1);
        var rules = new ReviveRules(party, new ReviveSettings());
        rules.OnRoomStarted(1, hasEnemies: true, now: 0);
        rules.OnDowned(1, 1);
        Assert.Equal(ReviveResponse.Revived, rules.TryRevive(0, 1, 3));
        rules.OnDowned(1, 4); // a second time in the same room: stays down
        rules.OnRoomCleared(9);
        var events = new List<ReviveEvent>();
        rules.TakeEvents(events);
        var read = events.ConvertAll(e => RoundTrip(ReviveMessage.Of(e)));

        Assert.Equal(4, read.Count);
        Assert.Equal((ReviveEventKind.Downed, 1, true), (read[0].Event, read[0].Slot, read[0].CanBeRevived));
        Assert.Equal((ReviveEventKind.Revived, 1, 0), (read[1].Event, read[1].Slot, read[1].BySlot));
        Assert.Equal(ReviveSettings.DefaultReviveHealth, read[1].HealthFraction);
        Assert.Equal((ReviveEventKind.Downed, 1, false), (read[2].Event, read[2].Slot, read[2].CanBeRevived));
        Assert.Equal((ReviveEventKind.ReturnedAtClear, 1), (read[3].Event, read[3].Slot));
        Assert.Equal(ReviveSettings.DefaultRoomClearHealth, read[3].HealthFraction);
    }

    [Fact]
    public void A_game_over_scene_ends_the_run_for_all_once()
    {
        // package KA: King Alexander's bomb ran out on one game; that is the run's end for everyone
        var w = new NetWriter();
        ReviveMessages.Write(w, ReviveMessage.Final());
        Assert.Equal(1, w.ToSegment().Count);
        Assert.Equal(ReviveMessageKind.Final, RoundTrip(ReviveMessage.Final()).Kind);

        var party = new PartyState();
        party.Join(0);
        party.Join(1);
        var rules = new ReviveRules(party, new ReviveSettings());
        rules.OnRoomStarted(1, hasEnemies: true, now: 0);
        Assert.True(rules.EndRun(5));
        Assert.False(rules.EndRun(6)); // the other game's bomb a moment later
        rules.OnDowned(0, 7);          // and nothing after it
        Assert.Equal(ReviveResponse.RunOver, rules.TryRevive(1, 0, 8));
        var events = new List<ReviveEvent>();
        rules.TakeEvents(events);
        Assert.Single(events);
        Assert.Equal(ReviveEventKind.RunOver, RoundTrip(ReviveMessage.Of(events[0])).Event);
        Assert.True(rules.IsRunOver);
    }

    [Fact]
    public void Everyone_down_ends_the_run_for_all()
    {
        var party = new PartyState();
        party.Join(0);
        party.Join(1);
        var rules = new ReviveRules(party, new ReviveSettings());
        rules.OnRoomStarted(1, hasEnemies: true, now: 0);
        rules.OnDowned(0, 1);
        rules.OnDowned(1, 2);
        var events = new List<ReviveEvent>();
        rules.TakeEvents(events);
        var last = RoundTrip(ReviveMessage.Of(events[events.Count - 1]));
        Assert.Equal(ReviveEventKind.RunOver, last.Event);
        Assert.Equal(-1, last.Slot);
    }

    [Fact]
    public void A_holds_progress_fits_in_six_bytes()
    {
        var w = new NetWriter();
        ReviveMessages.Write(w, ReviveMessage.Hold(target: 1, by: 0, hold: 7, progress: 0.5f, holdSeconds: 3f));
        Assert.Equal(6, w.ToSegment().Count);
        var read = RoundTrip(ReviveMessage.Hold(1, 0, 7, 0.5f, 3f));
        Assert.Equal((ReviveMessageKind.Hold, 1, 0, 7), (read.Kind, read.Slot, read.BySlot, read.HoldId));
        Assert.Equal(0.5f, read.Progress, 2);
        Assert.Equal(3f, read.HoldSeconds, 3);
        Assert.False(read.HoldEndedEarly);
        Assert.Equal(1f, RoundTrip(ReviveMessage.Hold(1, 0, 7, 1f, 3f)).Progress);
        Assert.Equal(0f, RoundTrip(ReviveMessage.Hold(1, 0, 7, 0f, 3f)).Progress);
    }

    [Fact]
    public void A_hold_that_ended_says_so_and_the_number_wraps()
    {
        var read = RoundTrip(ReviveMessage.Hold(2, 3, 256 + 4, -1f, 3f));
        Assert.True(read.HoldEndedEarly);
        Assert.Equal(4, read.HoldId);
        Assert.Equal((2, 3), (read.Slot, read.BySlot));
        // a hold time outside a byte of tenths is clamped, never 0
        Assert.Equal(25.5f, RoundTrip(ReviveMessage.Hold(2, 3, 1, 0.2f, 99f)).HoldSeconds, 3);
        Assert.Equal(0.1f, RoundTrip(ReviveMessage.Hold(2, 3, 1, 0.2f, 0f)).HoldSeconds, 3);
    }

    [Fact]
    public void An_unknown_kind_is_ignored()
    {
        var w = new NetWriter();
        w.WriteByte(200);
        Assert.False(ReviveMessages.TryRead(new NetReader(w.ToSegment()), out _));
    }
}
