using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Enemy health by player count (COOP_PLAN §4.7), the rules' settings and the seeded random.</summary>
public class CoopBalanceTests
{
    [Theory]
    [InlineData(2, EnemyCategory.Normal, 1.6f)]
    [InlineData(2, EnemyCategory.Elite, 1.7f)]
    [InlineData(2, EnemyCategory.Boss, 1.8f)]
    [InlineData(3, EnemyCategory.Normal, 2.2f)]
    [InlineData(3, EnemyCategory.Elite, 2.4f)]
    [InlineData(3, EnemyCategory.Boss, 2.6f)]
    [InlineData(4, EnemyCategory.Normal, 2.8f)]
    [InlineData(4, EnemyCategory.Elite, 3.1f)]
    [InlineData(4, EnemyCategory.Boss, 3.4f)]
    public void The_plans_health_table_is_the_default(int players, EnemyCategory category, float multiplier) =>
        Assert.Equal(multiplier, new CoopBalance(new BalanceSettings()).HealthMultiplier(players, category));

    [Theory]
    [InlineData(1, 1f)]
    [InlineData(0, 1f)]
    [InlineData(-3, 1f)]
    [InlineData(7, 3.4f)]
    public void One_player_is_never_changed_and_other_counts_are_clamped(int players, float multiplier) =>
        Assert.Equal(multiplier, new CoopBalance(new BalanceSettings()).HealthMultiplier(players, EnemyCategory.Boss));

    [Fact]
    public void Values_can_be_overridden_and_are_kept_sane()
    {
        var settings = new BalanceSettings();
        var balance = new CoopBalance(settings);
        Assert.True(settings.Set(2, EnemyCategory.Normal, 1.45f));
        Assert.Equal(1.45f, balance.HealthMultiplier(2, EnemyCategory.Normal));
        Assert.Equal(145, balance.ScaledHealth(100, 2, EnemyCategory.Normal), 3);
        Assert.False(settings.Set(1, EnemyCategory.Normal, 3f)); // solo stays x 1.0
        settings.Set(3, EnemyCategory.Boss, 100f);
        Assert.Equal(BalanceSettings.MaxMultiplier, settings.Get(3, EnemyCategory.Boss));
        settings.Set(3, EnemyCategory.Boss, float.NaN);
        Assert.Equal(2.6f, settings.Get(3, EnemyCategory.Boss)); // NaN = the default
        settings.ResetToDefaults();
        Assert.Equal(1.6f, settings.Get(2, EnemyCategory.Normal));
    }

    [Fact]
    public void The_table_reads_and_writes_as_one_short_text_for_the_config_and_the_server()
    {
        var settings = new BalanceSettings();
        Assert.Equal("1.6/1.7/1.8; 2.2/2.4/2.6; 2.8/3.1/3.4", settings.ToText());
        Assert.True(settings.TryParse("1.5/1.75/2; 2/2.5/3 ;2.5/3/0.1"));
        Assert.Equal(1.75f, settings.Get(2, EnemyCategory.Elite));
        Assert.Equal(3f, settings.Get(3, EnemyCategory.Boss));
        Assert.Equal(BalanceSettings.MinMultiplier, settings.Get(4, EnemyCategory.Boss)); // 0.1 clamped
        string before = settings.ToText();
        Assert.False(settings.TryParse("1.6/1.7; 2.2/2.4/2.6; 2.8/3.1/3.4"));
        Assert.False(settings.TryParse("x/1.7/1.8; 2.2/2.4/2.6; 2.8/3.1/3.4"));
        Assert.False(settings.TryParse(""));
        Assert.Equal(before, settings.ToText()); // all or nothing
    }

    [Fact]
    public void The_enemy_health_slider_sets_the_level_and_keeps_the_tables_shape()
    {
        var settings = new BalanceSettings();
        settings.ScaleTo(BalanceSettings.DefaultTwoPlayers); // 2x: Umut's default (1.6 felt too weak)
        Assert.Equal(2f, settings.Get(2, EnemyCategory.Normal), 3);
        Assert.Equal(2.125f, settings.Get(2, EnemyCategory.Elite), 3);
        Assert.Equal(2.25f, settings.Get(2, EnemyCategory.Boss), 3);
        Assert.Equal(4.25f, settings.Get(4, EnemyCategory.Boss), 3);
        Assert.Equal(1f, settings.Get(1, EnemyCategory.Boss)); // solo never changes

        settings.ScaleTo(3f); // from any level: the first value is what the slider says
        Assert.Equal(3f, settings.Get(2, EnemyCategory.Normal), 3);
        Assert.Equal(3.375f, settings.Get(2, EnemyCategory.Boss), 3);

        settings.ScaleTo(9f); // out of the slider's range: clamped to 3x
        Assert.Equal(3f, settings.Get(2, EnemyCategory.Normal), 3);
        settings.ScaleTo(float.NaN); // nonsense: the default
        Assert.Equal(2f, settings.Get(2, EnemyCategory.Normal), 3);

        // a recommended table keeps its own shape under the slider
        Assert.True(settings.TryParse("1.5/3/4.5; 2/2/2; 3/3/3"));
        settings.ScaleTo(1.5f);
        Assert.Equal(3f, settings.Get(2, EnemyCategory.Elite), 3);
        Assert.Equal(4.5f, settings.Get(2, EnemyCategory.Boss), 3);
    }

    [Fact]
    public void The_rule_defaults_are_the_plans_numbers()
    {
        var s = new CoopRulesSettings();
        Assert.Equal(10f, s.Doors.VoteSeconds);
        Assert.Equal(1f, s.Doors.CoinFlipSeconds);
        Assert.Equal(3f, s.Revive.HoldSeconds); // Umut 2026-11-14 (the plan's first number was 2 s)
        Assert.Equal(0.3f, s.Revive.ReviveHealth);
        Assert.Equal(1, s.Revive.RevivesPerRoom);
        Assert.Equal(0.1f, s.Revive.RoomClearHealth);
        Assert.False(s.Revive.HoldBreaksOnDamage);
        Assert.Equal(60f, s.RewardGate.MaxWaitSeconds);
        Assert.Equal(2f, s.Targeting.LockSeconds(EnemyCategory.Normal));
        Assert.Equal(3f, s.Targeting.LockSeconds(EnemyCategory.Elite));
        Assert.Equal(8f, s.BossThreat.WindowSeconds);
        // The turn bonus can outgrow everything else, so nobody is chased the whole fight.
        Assert.True(s.BossThreat.TurnBonusMax > s.BossThreat.DamageWeight + s.BossThreat.ClosenessBonus + s.BossThreat.CurrentTargetBonus);
    }

    [Fact]
    public void Clamp_pulls_bad_values_back_into_range()
    {
        var s = new CoopRulesSettings();
        s.Doors.VoteSeconds = -5;
        s.Revive.HoldSeconds = float.NaN;
        s.Revive.ReviveHealth = 3;
        s.Revive.RevivesPerRoom = -1;
        s.RewardGate.MaxWaitSeconds = float.PositiveInfinity;
        s.Targeting.EliteLockSeconds = 1000;
        s.BossThreat.CloseDistance = 0;
        s.Clamp();
        Assert.Equal(1f, s.Doors.VoteSeconds);
        Assert.Equal(ReviveSettings.DefaultHoldSeconds, s.Revive.HoldSeconds);
        Assert.Equal(1f, s.Revive.ReviveHealth);
        Assert.Equal(0, s.Revive.RevivesPerRoom);
        Assert.Equal(RewardGateSettings.DefaultMaxWaitSeconds, s.RewardGate.MaxWaitSeconds);
        Assert.Equal(30f, s.Targeting.EliteLockSeconds);
        Assert.Equal(0.1f, s.BossThreat.CloseDistance);
    }

    [Fact]
    public void The_seeded_random_repeats_per_run_seed_and_room_and_differs_between_rooms()
    {
        var a = CoopRandom.ForRoom(42, 3, CoopRandom.Purpose.DoorFlip);
        var b = CoopRandom.ForRoom(42, 3, CoopRandom.Purpose.DoorFlip);
        var c = CoopRandom.ForRoom(42, 4, CoopRandom.Purpose.DoorFlip);
        var d = CoopRandom.ForRoom(43, 3, CoopRandom.Purpose.DoorFlip);
        bool differsC = false, differsD = false;
        for (int i = 0; i < 20; i++)
        {
            double x = a.NextDouble();
            Assert.Equal(x, b.NextDouble());
            Assert.InRange(x, 0, 0.9999999999);
            differsC |= c.NextDouble() != x;
            differsD |= d.NextDouble() != x;
        }
        Assert.True(differsC);
        Assert.True(differsD);
        Assert.Equal(0xE220A8397B1DCDAFUL, new CoopRandom(0).NextULong()); // SplitMix64's reference value: the same on every PC
    }
}
