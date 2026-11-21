using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Who enemies and bosses attack (COOP_PLAN §4.1): target lock for normal enemies and elites, the threat
/// table for bosses.</summary>
public class TargetingTests
{
    private static TargetCandidate P(int slot, float distance, bool targetable = true, bool noticed = true,
        bool inReach = true, bool hidden = false) => new(slot, targetable, distance, noticed, inReach, hidden);

    private static EnemyTargeting Enemy(EnemyCategory category = EnemyCategory.Normal) =>
        new(category, new TargetingSettings());

    [Fact]
    public void A_normal_enemy_takes_the_closest_player_it_notices()
    {
        var enemy = Enemy();
        Assert.Equal(-1, enemy.Update(0, new[] { P(0, 3, noticed: false), P(1, 9, targetable: false) }));
        Assert.Equal(1, enemy.Update(0, new[] { P(0, 5), P(1, 3), P(2, 1, hidden: true), P(3, 2, targetable: false) }));
    }

    [Fact]
    public void A_new_attacker_takes_over_only_after_the_two_second_lock()
    {
        var enemy = Enemy();
        var players = new[] { P(0, 5), P(1, 3) };
        enemy.Update(0, players); // notices player 1
        enemy.OnDamagedBy(0, 0.5);
        Assert.Equal(1, enemy.Update(1, players));
        Assert.Equal(1, enemy.Update(1.99, players));
        Assert.Equal(0, enemy.Update(2, players));
        Assert.Equal(2, enemy.Switches);
        Assert.Equal(4, enemy.LockedUntil);
    }

    [Fact]
    public void After_the_lock_a_player_less_than_half_as_far_takes_over()
    {
        var enemy = Enemy();
        enemy.Update(0, new[] { P(0, 4), P(1, 9) }); // the host's player is closer: it goes for them
        Assert.Equal(0, enemy.Target);
        Assert.Equal(0, enemy.Update(1, new[] { P(0, 8), P(1, 3) }));   // still locked
        Assert.Equal(1, enemy.Update(2.5, new[] { P(0, 8), P(1, 3) })); // 3 < 8 x 0.5
        Assert.Equal(1, enemy.Update(5, new[] { P(0, 4), P(1, 6) }));   // 4 isn't less than half of 6: stays
    }

    [Fact]
    public void Without_the_closer_rule_only_hits_switch()
    {
        var enemy = new EnemyTargeting(EnemyCategory.Normal, new TargetingSettings { CloserRatio = 0f });
        enemy.Update(0, new[] { P(0, 4), P(1, 9) });
        Assert.Equal(0, enemy.Update(10, new[] { P(0, 20), P(1, 1) }));
        Assert.Equal(0, enemy.Update(10, new[] { P(0, 20), P(1, 1, noticed: false) }));
    }

    [Fact]
    public void Elites_and_mini_bosses_keep_their_target_three_seconds()
    {
        var elite = Enemy(EnemyCategory.Elite);
        var players = new[] { P(0, 1), P(1, 5) };
        elite.Update(0, players);
        elite.OnDamagedBy(1, 0.1);
        Assert.Equal(0, elite.Update(2.5, players));
        Assert.Equal(1, elite.Update(3, players));
    }

    [Theory]
    [InlineData(false, true, false)] // down (or gone)
    [InlineData(true, false, false)] // out of reach
    [InlineData(true, true, true)] // invisible
    public void A_target_that_is_down_out_of_reach_or_invisible_is_dropped_at_once(bool targetable, bool inReach,
        bool hidden)
    {
        var enemy = Enemy();
        enemy.Update(0, new[] { P(0, 1), P(1, 5) });
        enemy.OnDamagedBy(1, 0.2);
        // Inside the lock, but the target can't be kept: the attacker takes over at once.
        Assert.Equal(1, enemy.Update(0.5, new[] { P(0, 1, targetable, inReach: inReach, hidden: hidden), P(1, 5) }));
    }

    [Fact]
    public void Without_an_attacker_a_lost_target_gives_way_to_the_closest_player()
    {
        var enemy = Enemy();
        enemy.Update(0, new[] { P(0, 1), P(1, 5), P(2, 4) });
        Assert.Equal(2, enemy.Update(0.5, new[] { P(0, 1, targetable: false), P(1, 5), P(2, 4) }));
        Assert.Equal(-1, enemy.Update(1, new[] { P(1, 5, noticed: false) })); // player 2 gone, 1 out of sight
    }

    [Fact]
    public void Being_hit_by_an_unseen_player_makes_them_the_target()
    {
        var enemy = Enemy();
        enemy.OnDamagedBy(1, 0);
        Assert.Equal(1, enemy.Update(0, new[] { P(0, 2), P(1, 20, noticed: false) }));
    }

    [Fact]
    public void Two_players_hitting_all_the_time_switch_it_at_most_once_per_lock()
    {
        var enemy = Enemy();
        var players = new[] { P(0, 2), P(1, 2) };
        for (int frame = 0; frame <= 600; frame++) // 10 s at 60 fps
        {
            double now = frame / 60.0;
            if (frame % 6 == 0)
                enemy.OnDamagedBy(0, now);
            if (frame % 6 == 3)
                enemy.OnDamagedBy(1, now);
            enemy.Update(now, players);
        }
        Assert.InRange(enemy.Switches, 4, 6); // a switch every 2 s, not every hit
    }

    [Fact]
    public void The_lock_is_a_setting()
    {
        var enemy = new EnemyTargeting(EnemyCategory.Normal, new TargetingSettings { NormalLockSeconds = 0.5f });
        var players = new[] { P(0, 1), P(1, 5) };
        enemy.Update(0, players);
        enemy.OnDamagedBy(1, 0.1);
        Assert.Equal(1, enemy.Update(0.5, players));
    }

    // ----- Bosses -----

    [Fact]
    public void A_boss_picks_its_target_only_when_a_pattern_starts()
    {
        var boss = new BossThreat(new BossThreatSettings());
        var players = new[] { P(0, 2), P(1, 8) };
        Assert.Equal(0, boss.OnPatternStart(0, players)); // nobody hit yet: the closer one
        for (int i = 0; i < 20; i++)
            boss.OnDamage(1, 500, 0.1 * i);
        Assert.Equal(0, boss.Target); // mid-pattern: never switches
        Assert.Equal(1, boss.OnPatternStart(2.5, players));
        Assert.Equal(1, boss.Target);
    }

    [Fact]
    public void Down_players_are_never_targeted_by_a_boss_but_invisible_ones_are()
    {
        var boss = new BossThreat(new BossThreatSettings());
        boss.OnDamage(0, 10000, 0);
        Assert.Equal(1, boss.OnPatternStart(1, new[] { P(0, 0, targetable: false), P(1, 20, hidden: true) }));
        Assert.Equal(-1, boss.OnPatternStart(2, new[] { P(0, 0, targetable: false), P(1, 20, targetable: false) }));
    }

    [Fact]
    public void Recent_damage_fades_out_over_eight_seconds()
    {
        var boss = new BossThreat(new BossThreatSettings());
        boss.OnDamage(0, 100, 0.25); // the middle of its 0.5 s bucket
        Assert.Equal(100, boss.RecentDamage(0, 0.25), 3);
        Assert.Equal(50, boss.RecentDamage(0, 4.25), 3);
        Assert.Equal(0, boss.RecentDamage(0, 8.3), 3);
        Assert.Equal(0, boss.RecentDamage(1, 1), 3);
    }

    [Fact]
    public void A_player_just_revived_isnt_chased_at_once_for_the_time_spent_down()
    {
        var boss = new BossThreat(new BossThreatSettings());
        Assert.Equal(0, boss.OnPatternStart(0, new[] { P(0, 6), P(1, 6, targetable: false) }));
        for (int t = 1; t <= 30; t++)
        {
            boss.OnDamage(0, 50, t);
            boss.OnPatternStart(t, new[] { P(0, 6), P(1, 6, targetable: false) });
        }
        // Player 1 was down for 30 s: no turn bonus for it, and player 0 still holds the boss.
        Assert.Equal(0, boss.OnPatternStart(31, new[] { P(0, 6), P(1, 6) }));
    }

    [Fact]
    public void A_player_who_deals_no_damage_and_stays_away_still_gets_turns()
    {
        var boss = new BossThreat(new BossThreatSettings());
        var players = new[] { P(0, 0), P(1, 30) };
        for (double t = 0; t < 60; t += 3)
        {
            for (double h = 0; h < 3; h += 0.25)
                boss.OnDamage(0, 40, t + h);
            boss.OnPatternStart(t + 3, players);
        }
        Assert.True(boss.TimesPicked(1) >= 2, $"picked {boss.TimesPicked(1)} of {boss.Patterns}");
        Assert.True(boss.TimesPicked(0) > boss.TimesPicked(1));
    }

    /// <summary>A simulated boss fight: two players moving around, hitting the boss, patterns of 2-4 s.</summary>
    private static BossThreat Simulate(float dpsA, float dpsB, int patterns, ulong seed)
    {
        var random = new CoopRandom(seed);
        var boss = new BossThreat(new BossThreatSettings());
        double now = 0;
        var players = new TargetCandidate[2];
        for (int p = 0; p < patterns; p++)
        {
            players[0] = P(0, (float)(random.NextDouble() * 12));
            players[1] = P(1, (float)(random.NextDouble() * 12));
            boss.OnPatternStart(now, players);
            double length = 2 + random.NextDouble() * 2;
            for (double h = 0; h < length; h += 0.25)
            {
                boss.OnDamage(0, dpsA * 0.25f, now + h);
                boss.OnDamage(1, dpsB * 0.25f, now + h);
            }
            now += length;
        }
        return boss;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void With_equal_damage_neither_player_is_chased_more_than_70_percent_of_the_patterns(ulong seed)
    {
        var boss = Simulate(100, 100, 200, seed);
        Assert.Equal(200, boss.Patterns);
        Assert.InRange(boss.TimesPicked(0), 60, 140);
        Assert.InRange(boss.TimesPicked(1), 60, 140);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void The_player_dealing_more_damage_is_chased_somewhat_more(ulong seed)
    {
        var boss = Simulate(200, 100, 200, seed);
        int a = boss.TimesPicked(0), b = boss.TimesPicked(1);
        Assert.True(a > b, $"double damage picked {a}, the other {b}");
        Assert.InRange(a, 101, 170); // more, but not the whole fight
        Assert.True(b >= 30, $"the other was picked only {b} times");
    }
}
