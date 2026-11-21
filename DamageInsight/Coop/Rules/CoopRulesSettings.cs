using System;

namespace DamageInsight.Coop.Rules;

/// <summary>
/// Every number of the co-op gameplay rules in one place, with the defaults from docs/COOP_PLAN.md §4 (Umut's
/// decisions in §12). Umut: revive health, the hold time, revives per room and the balance numbers are "host
/// settings, like the balance numbers", so all of them live here: the host's values decide, the server may later send
/// recommended balance values. Values from a config file or the network can be anything, so <see cref="Clamp"/> pulls
/// every number back into a sane range (NaN becomes the default); every rule calls it when it is made.
/// </summary>
public sealed class CoopRulesSettings
{
    /// <summary>Door vote and coin flip (§4.3).</summary>
    public DoorVoteSettings Doors { get; } = new();

    /// <summary>Going down and revive (§4.4).</summary>
    public ReviveSettings Revive { get; } = new();

    /// <summary>The next room waits for everyone's room reward (§4.2).</summary>
    public RewardGateSettings RewardGate { get; } = new();

    /// <summary>Normal enemies and elites: target lock (§4.1).</summary>
    public TargetingSettings Targeting { get; } = new();

    /// <summary>Bosses: the threat table (§4.1).</summary>
    public BossThreatSettings BossThreat { get; } = new();

    /// <summary>Enemy health by player count (§4.7).</summary>
    public BalanceSettings Balance { get; } = new();

    /// <summary>Pulls every number into its allowed range. Returns this object (for chaining).</summary>
    public CoopRulesSettings Clamp()
    {
        Doors.Clamp();
        Revive.Clamp();
        RewardGate.Clamp();
        Targeting.Clamp();
        BossThreat.Clamp();
        Balance.Clamp();
        return this;
    }

    /// <summary>The value inside [min, max]; NaN or infinity gives the fallback (the default).</summary>
    internal static float Range(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

    internal static int Range(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}

/// <summary>Door vote (§4.3). Umut: "a coin flip, no long waits".</summary>
public sealed class DoorVoteSettings
{
    public const float DefaultVoteSeconds = 10f, DefaultCoinFlipSeconds = 1f;

    /// <summary>After the first vote, the others have this long; then the votes that exist decide. Plan: 10 s.</summary>
    public float VoteSeconds { get; set; } = DefaultVoteSeconds;

    /// <summary>How long the coin flip animation over the doors runs before the party goes (plan: ~1 s). The rule
    /// decides at once; this is for the game side that shows it.</summary>
    public float CoinFlipSeconds { get; set; } = DefaultCoinFlipSeconds;

    public void Clamp()
    {
        VoteSeconds = CoopRulesSettings.Range(VoteSeconds, 1f, 120f, DefaultVoteSeconds);
        CoinFlipSeconds = CoopRulesSettings.Range(CoinFlipSeconds, 0f, 5f, DefaultCoinFlipSeconds);
    }
}

/// <summary>Going down and revive (§4.4). Umut: "once per room", "it needs to be balanced".</summary>
public sealed class ReviveSettings
{
    public const float DefaultHoldSeconds = 3f, DefaultReviveHealth = 0.3f, DefaultRoomClearHealth = 0.1f,
        DefaultReach = 2f;
    public const int DefaultRevivesPerRoom = 1;

    /// <summary>A teammate holds Interact this long next to the downed player's head. 3 s (Umut 2026-11-14 after
    /// his first hand test; the plan had 2 s).</summary>
    public float HoldSeconds { get; set; } = DefaultHoldSeconds;

    /// <summary>Health after a teammate's revive, as a share of max health. Plan: 30 %.</summary>
    public float ReviveHealth { get; set; } = DefaultReviveHealth;

    /// <summary>How often each player can be revived per room (a boss fight is one room, all phases). Plan: 1.
    /// 0 = no teammate revives, only the return when the room is cleared.</summary>
    public int RevivesPerRoom { get; set; } = DefaultRevivesPerRoom;

    /// <summary>Health of the players still down when the room is cleared, as a share of max health. Plan: 10 %.</summary>
    public float RoomClearHealth { get; set; } = DefaultRoomClearHealth;

    /// <summary>[DECISION] The reviver must stay this close to the head (world units); further away, the hold breaks.
    /// To be tuned in the game.</summary>
    public float Reach { get; set; } = DefaultReach;

    /// <summary>[DECISION] Off: being hit while holding doesn't break the hold (plan: "it costs the reviver nothing
    /// but time, and they can be hit meanwhile"). On: any hit restarts it.</summary>
    public bool HoldBreaksOnDamage { get; set; }

    public void Clamp()
    {
        HoldSeconds = CoopRulesSettings.Range(HoldSeconds, 0.1f, 10f, DefaultHoldSeconds);
        ReviveHealth = CoopRulesSettings.Range(ReviveHealth, 0.01f, 1f, DefaultReviveHealth);
        RevivesPerRoom = CoopRulesSettings.Range(RevivesPerRoom, 0, 10);
        RoomClearHealth = CoopRulesSettings.Range(RoomClearHealth, 0.01f, 1f, DefaultRoomClearHealth);
        Reach = CoopRulesSettings.Range(Reach, 0.1f, 20f, DefaultReach);
    }
}

/// <summary>The room reward wait (§4.2): "The next room waits until everyone has taken (or left) their room reward,
/// at most 60 s".</summary>
public sealed class RewardGateSettings
{
    public const float DefaultMaxWaitSeconds = 60f;

    /// <summary>The longest the party waits for anyone's reward. Plan: 60 s. 0 = never wait.</summary>
    public float MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;

    public void Clamp() => MaxWaitSeconds = CoopRulesSettings.Range(MaxWaitSeconds, 0f, 600f, DefaultMaxWaitSeconds);
}

/// <summary>Normal enemies and elites (§4.1): "a target lock of 2 s after each switch" (3 s for elites and
/// mini-bosses), against ping-pong between two attackers.</summary>
public sealed class TargetingSettings
{
    public const float DefaultNormalLockSeconds = 2f, DefaultEliteLockSeconds = 3f;

    public const float DefaultCloserRatio = 0.5f;

    public float NormalLockSeconds { get; set; } = DefaultNormalLockSeconds;
    public float EliteLockSeconds { get; set; } = DefaultEliteLockSeconds;

    /// <summary>Umut 2026-11-07 ("hit or much closer"): after the lock, a noticed player nearer than this share of the
    /// current target's distance takes over (0.5 = less than half as far). 0 = only hits switch the target.</summary>
    public float CloserRatio { get; set; } = DefaultCloserRatio;

    /// <summary>The lock for an enemy of this kind (bosses use the threat table; here they count as elites).</summary>
    public float LockSeconds(EnemyCategory category) =>
        category == EnemyCategory.Normal ? NormalLockSeconds : EliteLockSeconds;

    public void Clamp()
    {
        NormalLockSeconds = CoopRulesSettings.Range(NormalLockSeconds, 0f, 30f, DefaultNormalLockSeconds);
        EliteLockSeconds = CoopRulesSettings.Range(EliteLockSeconds, 0f, 30f, DefaultEliteLockSeconds);
        CloserRatio = CoopRulesSettings.Range(CloserRatio, 0f, 1f, DefaultCloserRatio);
    }
}

/// <summary>
/// The boss threat table (§4.1). Each player's threat when a pattern starts =
/// <see cref="DamageWeight"/> x their share of the party's recent damage to the boss (the last
/// <see cref="WindowSeconds"/>, older hits fading out) + up to <see cref="ClosenessBonus"/> for being close +
/// a turn bonus of <see cref="TurnBonusPerSecond"/> for every second the boss chased someone else (at most
/// <see cref="TurnBonusMax"/>) + <see cref="CurrentTargetBonus"/> for the player it chases now. The defaults spread
/// the pressure (2 players with equal damage: about half the patterns each; double damage: about two thirds).
/// </summary>
public sealed class BossThreatSettings
{
    public const float DefaultWindowSeconds = 8f, DefaultDamageWeight = 1f, DefaultClosenessBonus = 0.3f,
        DefaultCloseDistance = 6f, DefaultTurnBonusPerSecond = 0.1f, DefaultTurnBonusMax = 2f,
        DefaultCurrentTargetBonus = 0.2f;

    /// <summary>Damage counts for this long, fading out linearly (plan: the last 8 s).</summary>
    public float WindowSeconds { get; set; } = DefaultWindowSeconds;

    /// <summary>Weight of the damage share (0..1 of the party's recent damage).</summary>
    public float DamageWeight { get; set; } = DefaultDamageWeight;

    /// <summary>Bonus at distance 0, falling to 0 at <see cref="CloseDistance"/>.</summary>
    public float ClosenessBonus { get; set; } = DefaultClosenessBonus;

    /// <summary>World units; further away gives no closeness bonus.</summary>
    public float CloseDistance { get; set; } = DefaultCloseDistance;

    /// <summary>Grows for every second the boss chased someone else.</summary>
    public float TurnBonusPerSecond { get; set; } = DefaultTurnBonusPerSecond;

    /// <summary>Cap of the turn bonus. Above damage weight + closeness + current target bonus, so even a player
    /// who deals no damage and stays far away gets their turn (plan: "nobody is chased the whole fight").</summary>
    public float TurnBonusMax { get; set; } = DefaultTurnBonusMax;

    /// <summary>[DECISION] The chased player keeps a small bonus, so the boss doesn't turn around on every pattern
    /// when both players are equal.</summary>
    public float CurrentTargetBonus { get; set; } = DefaultCurrentTargetBonus;

    public void Clamp()
    {
        WindowSeconds = CoopRulesSettings.Range(WindowSeconds, 1f, 60f, DefaultWindowSeconds);
        DamageWeight = CoopRulesSettings.Range(DamageWeight, 0f, 10f, DefaultDamageWeight);
        ClosenessBonus = CoopRulesSettings.Range(ClosenessBonus, 0f, 10f, DefaultClosenessBonus);
        CloseDistance = CoopRulesSettings.Range(CloseDistance, 0.1f, 100f, DefaultCloseDistance);
        TurnBonusPerSecond = CoopRulesSettings.Range(TurnBonusPerSecond, 0f, 10f, DefaultTurnBonusPerSecond);
        TurnBonusMax = CoopRulesSettings.Range(TurnBonusMax, 0f, 100f, DefaultTurnBonusMax);
        CurrentTargetBonus = CoopRulesSettings.Range(CurrentTargetBonus, 0f, 10f, DefaultCurrentTargetBonus);
    }
}
