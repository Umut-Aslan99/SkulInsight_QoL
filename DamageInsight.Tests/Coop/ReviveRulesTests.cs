using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Going down and revive (COOP_PLAN §4.4, Umut: "once per room", a boss fight is one room).</summary>
public class ReviveRulesTests
{
    private static (PartyState party, ReviveRules rules) Fight(int players, ReviveSettings settings = null)
    {
        var party = new PartyState();
        for (int i = 0; i < players; i++)
            party.Join(i);
        var rules = new ReviveRules(party, settings ?? new ReviveSettings());
        rules.OnRoomStarted(3, hasEnemies: true, now: 0);
        return (party, rules);
    }

    private static List<ReviveEvent> Events(ReviveRules rules)
    {
        var list = new List<ReviveEvent>();
        rules.TakeEvents(list);
        return list;
    }

    [Fact]
    public void A_teammate_revives_a_downed_player_with_30_percent_health()
    {
        var (party, rules) = Fight(2);
        rules.OnDowned(0, 5);
        Assert.True(party.IsDown(0));
        Assert.True(rules.CanBeRevived(0));
        Assert.False(rules.IsRunOver);

        Assert.Equal(ReviveResponse.Revived, rules.TryRevive(1, 0, 8));
        Assert.False(party.IsDown(0));
        Assert.Equal(1, rules.RevivesUsed(0));

        var events = Events(rules);
        Assert.Equal(new[] { ReviveEventKind.Downed, ReviveEventKind.Revived }, events.Select(e => e.Kind));
        Assert.True(events[0].CanBeRevived);
        Assert.Equal(0, events[1].Slot);
        Assert.Equal(1, events[1].BySlot); // who revived whom
        Assert.Equal(3, events[1].RoomIndex);
        Assert.Equal(8, events[1].Time);
        Assert.Equal(0.3f, events[1].HealthFraction);
        Assert.Empty(Events(rules)); // drained
    }

    [Fact]
    public void Once_per_room_then_down_until_the_room_is_cleared_with_10_percent()
    {
        var (party, rules) = Fight(2);
        rules.OnDowned(0, 1);
        rules.TryRevive(1, 0, 3);
        rules.OnDowned(0, 10);
        Assert.False(rules.CanBeRevived(0));
        Assert.Equal(ReviveResponse.LimitReached, rules.TryRevive(1, 0, 13));
        Assert.True(party.IsDown(0));

        rules.OnRoomCleared(20);
        Assert.False(party.IsDown(0));
        var events = Events(rules);
        Assert.False(events[2].CanBeRevived); // the second down: stays down
        Assert.Equal(ReviveEventKind.ReturnedAtClear, events[3].Kind);
        Assert.Equal(0.1f, events[3].HealthFraction);

        rules.OnRoomStarted(4, true, 30); // the count starts again
        rules.OnDowned(0, 31);
        Assert.Equal(ReviveResponse.Revived, rules.TryRevive(1, 0, 33));
    }

    [Fact]
    public void A_boss_fight_is_one_room_with_all_its_phases()
    {
        var (party, rules) = Fight(2);
        rules.OnRoomStarted(12, true, 0); // the boss room
        rules.OnDowned(1, 10);
        rules.TryRevive(0, 1, 12);
        rules.OnBossPhaseStarted(40); // phase 2, maybe on a new map
        rules.OnDowned(1, 50);
        Assert.Equal(ReviveResponse.LimitReached, rules.TryRevive(0, 1, 52));
        rules.OnRoomCleared(90); // the boss is defeated
        Assert.False(party.IsDown(1));
    }

    [Fact]
    public void Everyone_down_at_once_ends_the_run()
    {
        var (party, rules) = Fight(2);
        rules.OnDowned(0, 1);
        Assert.False(rules.IsRunOver);
        rules.OnDowned(1, 2);
        Assert.True(rules.IsRunOver);
        Assert.Equal(ReviveResponse.RunOver, rules.TryRevive(0, 1, 3));
        rules.OnRoomCleared(4); // too late: nobody comes back
        Assert.True(party.IsDown(0));
        var events = Events(rules);
        Assert.Equal(ReviveEventKind.RunOver, events.Last().Kind);
        Assert.Equal(-1, events.Last().Slot);
        Assert.Single(events, e => e.Kind == ReviveEventKind.RunOver);
    }

    [Fact]
    public void The_last_player_up_leaving_ends_the_run_for_the_downed_ones()
    {
        var (party, rules) = Fight(2);
        rules.OnDowned(0, 1);
        party.Leave(1);
        Assert.True(rules.Update(2));
        Assert.True(rules.IsRunOver);
        Assert.False(rules.Update(3));
    }

    [Fact]
    public void A_player_who_left_doesnt_count_for_the_end_of_the_run()
    {
        var (party, rules) = Fight(3);
        party.Leave(2);
        rules.OnDowned(0, 1);
        rules.OnDowned(1, 2);
        Assert.True(rules.IsRunOver);
    }

    [Fact]
    public void Only_a_living_teammate_can_revive_and_only_someone_who_is_down()
    {
        var (_, rules) = Fight(3);
        Assert.Equal(ReviveResponse.NotDown, rules.TryRevive(1, 0, 0));
        rules.OnDowned(0, 1);
        rules.OnDowned(1, 1);
        Assert.Equal(ReviveResponse.ReviverCannot, rules.TryRevive(1, 0, 2)); // down themselves
        Assert.Equal(ReviveResponse.ReviverCannot, rules.TryRevive(0, 0, 2));
        Assert.Equal(ReviveResponse.Revived, rules.TryRevive(2, 0, 3));
        Assert.Equal(ReviveResponse.NotDown, rules.TryRevive(2, 0, 3)); // a second reviver was too late
    }

    [Fact]
    public void Down_in_a_room_already_cleared_comes_back_at_once()
    {
        var (party, rules) = Fight(2);
        rules.OnRoomCleared(10);
        rules.OnDowned(0, 12); // a hazard after the fight
        Assert.False(party.IsDown(0));
        Assert.False(rules.IsRunOver);
        Assert.Equal(new[] { ReviveEventKind.Downed, ReviveEventKind.ReturnedAtClear }, Events(rules).Select(e => e.Kind));
    }

    [Fact]
    public void Revives_per_room_and_the_health_are_settings()
    {
        var settings = new ReviveSettings { RevivesPerRoom = 2, ReviveHealth = 0.5f, RoomClearHealth = 0.25f };
        var (_, rules) = Fight(2, settings);
        for (int i = 0; i < 2; i++)
        {
            rules.OnDowned(0, i * 10);
            Assert.Equal(ReviveResponse.Revived, rules.TryRevive(1, 0, i * 10 + 2));
        }
        rules.OnDowned(0, 30);
        Assert.Equal(ReviveResponse.LimitReached, rules.TryRevive(1, 0, 32));
        rules.OnRoomCleared(40);
        var events = Events(rules);
        Assert.Equal(0.5f, events.First(e => e.Kind == ReviveEventKind.Revived).HealthFraction);
        Assert.Equal(0.25f, events.Last().HealthFraction);

        var (_, none) = Fight(2, new ReviveSettings { RevivesPerRoom = 0 });
        none.OnDowned(0, 1);
        Assert.False(none.CanBeRevived(0));
        Assert.Equal(ReviveResponse.LimitReached, none.TryRevive(1, 0, 2));
    }

    [Fact]
    public void A_player_who_leaves_while_down_and_comes_back_is_still_down()
    {
        var (party, rules) = Fight(3);
        rules.OnDowned(0, 1);
        party.Leave(0);
        rules.OnRoomCleared(5); // only connected players come back
        party.Join(0);
        Assert.True(party.IsDown(0));
        rules.OnRoomStarted(4, hasEnemies: false, now: 10); // the next calm room brings them back
        Assert.False(party.IsDown(0));
    }

    [Fact]
    public void The_hold_finishes_after_three_seconds_next_to_the_head()
    {
        var hold = new ReviveHold(new ReviveSettings());
        hold.Begin(target: 1, now: 0);
        Assert.Equal(HoldState.Holding, hold.Update(1.5, holding: true, distance: 1, reviverDown: false, targetStillDown: true));
        Assert.Equal(0.5f, hold.Progress(1.5));
        hold.OnDamaged(); // being hit doesn't break it (plan: "they can be hit meanwhile")
        Assert.Equal(HoldState.Holding, hold.Update(2.5, true, 1.9f, false, true));
        Assert.Equal(HoldState.Done, hold.Update(3, true, 1, false, true));
        Assert.Equal(1f, hold.Progress(3));
        Assert.Equal(1, hold.Target);
        hold.Clear();
        Assert.Equal(HoldState.Idle, hold.State);
    }

    [Theory]
    [InlineData(false, 1f, false, true, HoldCancel.Released)]
    [InlineData(true, 3f, false, true, HoldCancel.MovedAway)]
    [InlineData(true, 1f, true, true, HoldCancel.ReviverDown)]
    [InlineData(true, 1f, false, false, HoldCancel.TargetGone)]
    public void The_hold_breaks_and_starts_again_from_zero(bool holding, float distance, bool reviverDown,
        bool targetStillDown, HoldCancel why)
    {
        var hold = new ReviveHold(new ReviveSettings());
        hold.Begin(0, 0);
        Assert.Equal(HoldState.Cancelled, hold.Update(1.9, holding, distance, reviverDown, targetStillDown));
        Assert.Equal(why, hold.LastCancel);
        Assert.Equal(0f, hold.Progress(1.9));
        hold.Begin(0, 2);
        Assert.Equal(0f, hold.Progress(2));
    }

    [Fact]
    public void Hits_break_the_hold_only_with_the_setting()
    {
        var hold = new ReviveHold(new ReviveSettings { HoldBreaksOnDamage = true, HoldSeconds = 3 });
        hold.Begin(0, 0);
        Assert.Equal(HoldState.Holding, hold.Update(2.5, true, 1, false, true)); // 3 s now
        hold.OnDamaged();
        Assert.Equal(HoldState.Cancelled, hold.State);
        Assert.Equal(HoldCancel.Damaged, hold.LastCancel);
    }
}
