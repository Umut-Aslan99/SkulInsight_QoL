using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Sync;
using DamageInsight.Coop.Testing;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The plain parts of the phase-0 runtime: when co-op starts, the PlayerState message, the preloader check.</summary>
public class CoopRuntimeLogicTests
{
    private static CoopLaunch Launch(params string[] args) => CoopLaunch.Parse(args);

    [Fact]
    public void Without_start_options_coop_stays_off()
    {
        var plan = CoopStartPlan.From(Launch());
        Assert.False(plan.Starts);
        Assert.Contains("no co-op start option", plan.Reason);
    }

    [Fact]
    public void The_launcher_host_and_join_start_over_udp_with_their_port()
    {
        var host = CoopStartPlan.From(Launch("--skulinsight-instance", "2", "--skulinsight-coop", "host",
            "--skulinsight-transport", "udp", "--skulinsight-port", "28000"));
        Assert.Equal(CoopStartKind.Host, host.Kind);
        Assert.Equal(CoopTransportKind.Udp, host.Transport);
        Assert.Equal(28000, host.Port);
        Assert.Null(host.Lag);

        var join = CoopStartPlan.From(Launch("--skulinsight-instance", "3", "--skulinsight-coop", "join"));
        Assert.Equal(CoopStartKind.Join, join.Kind);
        Assert.Equal(CoopTransportKind.Udp, join.Transport); // -coop without -transport: UDP
        Assert.Equal(CoopLaunch.DefaultPort, join.Port);
    }

    [Fact]
    public void Simulated_lag_is_passed_on_in_the_same_units()
    {
        var plan = CoopStartPlan.From(Launch("--skulinsight-instance", "2", "--skulinsight-coop", "host",
            "--skulinsight-lag", "100", "--skulinsight-jitter", "20", "--skulinsight-loss", "2%"));
        Assert.NotNull(plan.Lag);
        Assert.Equal(100, plan.Lag.DelayMs);
        Assert.Equal(20, plan.Lag.JitterMs);
        Assert.Equal(2, plan.Lag.LossPercent, 3);
        Assert.Contains("simulated lag 100 ms", plan.ToString());
    }

    [Fact]
    public void Only_test_copies_wait_for_their_own_save()
    {
        Assert.True(CoopStartPlan.From(Launch("--skulinsight-instance", "2", "--skulinsight-coop", "join")).NeedsSaveIsolation);
        // The launcher's copy 1 is the normal game with the real save: nothing to wait for.
        Assert.False(CoopStartPlan.From(Launch("--skulinsight-instance", "1", "--skulinsight-coop", "host")).NeedsSaveIsolation);
    }

    [Fact]
    public void Steam_and_loopback_are_not_started_from_options()
    {
        Assert.False(CoopStartPlan.From(Launch("--skulinsight-instance", "2", "--skulinsight-coop", "join",
            "--skulinsight-transport", "steam")).Starts);
        Assert.False(CoopStartPlan.From(Launch("--skulinsight-instance", "2", "--skulinsight-coop", "join",
            "--skulinsight-transport", "loopback")).Starts);
    }

    [Fact]
    public void The_player_state_message_round_trips()
    {
        var state = new PlayerState
        {
            Position = new Vec2(12.5f, -3.25f),
            Velocity = new Vec2(6f, -9.5f),
            FacingRight = true,
            Grounded = false,
            Health = 87f,
            MaxHealth = 100f,
        };
        var w = new NetWriter();
        PlayerStateMessage.Write(w, 1234.5678, 0xDEADBEEF, state);
        Assert.Equal(8 + 4 + 20, w.Length);

        var read = PlayerStateMessage.Read(new NetReader(w.ToSegment()), out double time, out uint map);
        Assert.Equal(1234.5678, time);
        Assert.Equal(0xDEADBEEFu, map);
        Assert.Equal(12.5f, read.Position.X, 3);
        Assert.Equal(-3.25f, read.Position.Y, 3);
        Assert.Equal(6f, read.Velocity.X, 2);
        Assert.True(read.FacingRight);
        Assert.False(read.Grounded);
        Assert.Equal(87f, read.Health);
        Assert.Equal(100f, read.MaxHealth);
    }

    [Fact]
    public void Map_keys_ignore_unitys_clone_suffix_and_zero_means_no_map()
    {
        Assert.Equal(PlayerStateMessage.MapKey("Castle_Lobby"), PlayerStateMessage.MapKey("Castle_Lobby(Clone)"));
        Assert.Equal(PlayerStateMessage.MapKey("Castle_Lobby"), PlayerStateMessage.MapKey("Castle_Lobby (Clone)"));
        Assert.NotEqual(PlayerStateMessage.MapKey("Castle_Lobby"), PlayerStateMessage.MapKey("Chapter1_Map3"));
        Assert.Equal(0u, PlayerStateMessage.MapKey(""));
        Assert.Equal(0u, PlayerStateMessage.MapKey(null));
        Assert.NotEqual(0u, PlayerStateMessage.MapKey("x"));
    }

    [Fact]
    public void The_preloader_is_on_for_launcher_copies_only_through_their_start_option()
    {
        Assert.True(DamageInsight.Preloader.CoopGate.FromStartOptions(new[] { "Skul.exe", "--skulinsight-coop", "join" }, out _));
        Assert.True(DamageInsight.Preloader.CoopGate.FromStartOptions(new[] { "--SkulInsight-Coop=host" }, out _));
        Assert.False(DamageInsight.Preloader.CoopGate.FromStartOptions(new[] { "Skul.exe", "--skulinsight-instance", "2" }, out _));
        Assert.False(DamageInsight.Preloader.CoopGate.FromStartOptions(new[] { "--skulinsight-coopx" }, out _));
        Assert.False(DamageInsight.Preloader.CoopGate.FromStartOptions(null, out _));
    }

    [Fact]
    public void The_preloader_api_is_used_only_when_it_matches_the_review()
    {
        Assert.Null(PlayerReadsBridge.Problem(1, 0, PlayerReadsBridge.ReviewedSitesHash));
        Assert.Null(PlayerReadsBridge.Problem(1, 0, PlayerReadsBridge.ReviewedSitesHash.ToUpperInvariant()));
        Assert.Contains("API version", PlayerReadsBridge.Problem(2, 0, PlayerReadsBridge.ReviewedSitesHash));
        Assert.Contains("not reviewed", PlayerReadsBridge.Problem(1, 3, PlayerReadsBridge.ReviewedSitesHash));
        Assert.Contains("differ", PlayerReadsBridge.Problem(1, 0, "0000000000000000"));
    }
}
