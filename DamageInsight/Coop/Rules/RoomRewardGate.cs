using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Rules;

/// <summary>
/// The wait for everyone's room reward (§4.2): "The next room waits until everyone has taken (or left) their room
/// reward, at most 60 s, with a banner ('Waiting for DocRun's reward: 42 s')." Runs on the host, one per room.
/// <see cref="Begin"/> when the room's rewards appear; every connected player who is up then has a reward to take.
/// <see cref="MarkDone"/> when one took or left theirs. Released when nobody blocks any more or the time ran out;
/// then the doors open for votes (<see cref="DoorVote"/>). A player who disconnects or is down doesn't block; one who
/// comes back while it still waits blocks again (their reward is still there). Call <see cref="Update"/> every frame.
/// </summary>
public sealed class RoomRewardGate
{
    private readonly PartyState _party;
    private readonly RewardGateSettings _settings;
    private readonly bool[] _pending = new bool[PartyState.MaxPlayers];
    private double _deadline;

    public RoomRewardGate(PartyState party, RewardGateSettings settings)
    {
        _party = party ?? throw new ArgumentNullException(nameof(party));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Clamp();
    }

    public bool Started { get; private set; }
    public bool IsReleased { get; private set; }

    /// <summary>Released because the time ran out (someone still had their reward lying there).</summary>
    public bool TimedOut { get; private set; }

    public double ReleasedAt { get; private set; } = -1;

    /// <summary>The room's rewards appeared (the fight is over, the chest or grave is there). Players without a
    /// reward in this room get <see cref="MarkDone"/> right away.</summary>
    public void Begin(double now)
    {
        if (Started)
            return;
        Started = true;
        _deadline = now + _settings.MaxWaitSeconds;
        for (int i = 0; i < PartyState.MaxPlayers; i++)
            _pending[i] = _party.IsActive(i);
        Update(now);
    }

    /// <summary>A room without rewards: the doors may open at once.</summary>
    public void ReleaseNow(double now)
    {
        Started = true;
        Release(now, false);
    }

    /// <summary>The player took their reward, or left it (the game side decides what "left" is, e.g. they pressed
    /// Interact on a closed door, [DECISION] for the integration).</summary>
    public void MarkDone(int slot, double now)
    {
        if (PartyState.IsSlot(slot))
            _pending[slot] = false;
        if (Started)
            Update(now);
    }

    /// <summary>Checks whether the party may go on. True once, on the call that released it.</summary>
    public bool Update(double now)
    {
        if (!Started || IsReleased)
            return false;
        if (now >= _deadline)
        {
            Release(now, WaitingCount > 0);
            return true;
        }
        if (WaitingCount == 0)
        {
            Release(now, false);
            return true;
        }
        return false;
    }

    /// <summary>Whether this player still blocks the party.</summary>
    public bool IsWaitingFor(int slot) =>
        Started && !IsReleased && PartyState.IsSlot(slot) && _pending[slot] && _party.IsActive(slot);

    public int WaitingCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < PartyState.MaxPlayers; i++)
                if (_pending[i] && _party.IsActive(i))
                    n++;
            return n;
        }
    }

    /// <summary>The players the banner names, lowest slot first (fills the list, no allocation).</summary>
    public void Waiting(List<int> into)
    {
        into.Clear();
        if (!Started || IsReleased)
            return;
        for (int i = 0; i < PartyState.MaxPlayers; i++)
            if (_pending[i] && _party.IsActive(i))
                into.Add(i);
    }

    /// <summary>Seconds until the party goes on anyway, for the banner; 0 once released or before it began.</summary>
    public double SecondsLeft(double now) => !Started || IsReleased ? 0 : Math.Max(0, _deadline - now);

    private void Release(double now, bool timedOut)
    {
        IsReleased = true;
        TimedOut = timedOut;
        ReleasedAt = now;
    }
}
