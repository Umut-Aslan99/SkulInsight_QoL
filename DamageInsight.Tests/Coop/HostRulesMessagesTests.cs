using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The host's numbers on the wire: every game plays by the host's balance table and revive numbers.</summary>
public class HostRulesMessagesTests
{
    private static HostRules RoundTrip(HostRules rules)
    {
        var w = new NetWriter();
        HostRulesMessages.Write(w, rules);
        var r = new NetReader(w.ToSegment());
        Assert.True(HostRulesMessages.TryRead(r, out var read));
        Assert.True(r.IsAtEnd);
        return read;
    }

    [Fact]
    public void The_host_s_numbers_arrive_as_they_were()
    {
        var balance = new BalanceSettings();
        Assert.True(balance.Set(2, EnemyCategory.Boss, 2.5f));
        var revive = new ReviveSettings { HoldSeconds = 2.5f, Reach = 3f, ReviveHealth = 0.45f, RoomClearHealth = 0.2f, RevivesPerRoom = 2 };
        var read = RoundTrip(HostRules.From(balance, revive));

        var balance2 = new BalanceSettings();
        var revive2 = new ReviveSettings();
        Assert.True(read.ApplyTo(balance2, revive2));
        Assert.Equal(2.5f, balance2.Get(2, EnemyCategory.Boss), 3);
        Assert.Equal(1.6f, balance2.Get(2, EnemyCategory.Normal), 3);
        Assert.Equal(2.5f, revive2.HoldSeconds, 2);
        Assert.Equal(3f, revive2.Reach, 2);
        Assert.Equal(0.45f, revive2.ReviveHealth, 2);
        Assert.Equal(0.2f, revive2.RoomClearHealth, 2);
        Assert.Equal(2, revive2.RevivesPerRoom);
    }

    [Fact]
    public void Numbers_out_of_range_are_pulled_back_and_a_bad_table_keeps_the_old_one()
    {
        var read = RoundTrip(new HostRules { Balance = "nonsense", HoldSeconds = 99f, Reach = 0f, ReviveHealth = 3f, RoomClearHealth = 0f, RevivesPerRoom = 300 });
        var balance = new BalanceSettings();
        var revive = new ReviveSettings();
        Assert.False(read.ApplyTo(balance, revive));
        Assert.Equal(1.6f, balance.Get(2, EnemyCategory.Normal), 3);
        Assert.InRange(revive.HoldSeconds, 0.1f, 10f);
        Assert.InRange(revive.Reach, 0.1f, 20f);
        Assert.InRange(revive.ReviveHealth, 0.01f, 1f);
        Assert.InRange(revive.RoomClearHealth, 0.01f, 1f);
        Assert.InRange(revive.RevivesPerRoom, 0, 10);
    }

    [Fact]
    public void A_cut_off_message_is_not_read()
    {
        var w = new NetWriter();
        w.WriteString("1.6/1.7/1.8; 2.2/2.4/2.6; 2.8/3.1/3.4");
        w.WriteByte(30);
        Assert.False(HostRulesMessages.TryRead(new NetReader(w.ToSegment()), out _));
        Assert.False(HostRulesMessages.TryRead(new NetReader(new NetWriter().ToSegment()), out _));
    }
}
