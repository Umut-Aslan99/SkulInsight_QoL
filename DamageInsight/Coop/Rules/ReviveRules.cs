using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Rules;

public enum ReviveEventKind : byte
{
    /// <summary>A player went down (after the game's own revive effects).</summary>
    Downed = 0,
    /// <summary>A teammate revived them (<see cref="ReviveEvent.BySlot"/>).</summary>
    Revived,
    /// <summary>The room was cleared and they came back by themselves.</summary>
    ReturnedAtClear,
    /// <summary>Everyone was down at once: the run is over for all.</summary>
    RunOver,
}

/// <summary>One line for the party timeline (§4.11: "deaths, revives (who revived whom)"). Maps 1:1 to the history's
/// "down", "revive", "back" and "end" events.</summary>
public readonly struct ReviveEvent
{
    public readonly ReviveEventKind Kind;
    /// <summary>Who it's about (-1 for RunOver).</summary>
    public readonly int Slot;
    /// <summary>The reviver (Revived only; else -1).</summary>
    public readonly int BySlot;
    public readonly int RoomIndex;
    public readonly double Time;
    /// <summary>The health they come back with, a share of max health (Revived, ReturnedAtClear; else 0).</summary>
    public readonly float HealthFraction;
    /// <summary>Downed only: false = already revived this room, stays down until the room is cleared.</summary>
    public readonly bool CanBeRevived;

    public ReviveEvent(ReviveEventKind kind, int slot, int bySlot, int roomIndex, double time, float healthFraction,
        bool canBeRevived)
    {
        Kind = kind;
        Slot = slot;
        BySlot = bySlot;
        RoomIndex = roomIndex;
        Time = time;
        HealthFraction = healthFraction;
        CanBeRevived = canBeRevived;
    }
}

public enum ReviveResponse : byte
{
    Revived = 0,
    /// <summary>The target isn't down (someone else was faster, or the room was cleared).</summary>
    NotDown,
    /// <summary>The target already used this room's revives: they stay down until the room is cleared.</summary>
    LimitReached,
    /// <summary>The reviver is down, gone, or the target themselves.</summary>
    ReviverCannot,
    RunOver,
}

/// <summary>
/// Going down and revive (§4.4), decided on the host. At 0 HP (after Skul's own revive effects) a player goes down.
/// A teammate revives them by holding Interact for 3 s next to the head (the hold runs on the reviver's game,
/// <see cref="ReviveHold"/>; its end asks <see cref="TryRevive"/>); they come back with 30 % health. Umut: "once per
/// room": each player can be revived <see cref="ReviveSettings.RevivesPerRoom"/> times per room, a boss fight is one
/// room with all its phases (<see cref="OnBossPhaseStarted"/> changes nothing). Down again after that: they stay down
/// until the room is cleared; then everyone down comes back with 10 % and the counts start again. Everyone down at
/// once (of the connected players): the run is over. Call <see cref="Update"/> every frame (someone leaving can end
/// the run). Events for the party timeline: <see cref="TakeEvents"/>.
/// </summary>
public sealed class ReviveRules
{
    private readonly PartyState _party;
    private readonly ReviveSettings _settings;
    private readonly int[] _revivesUsed = new int[PartyState.MaxPlayers];
    private readonly List<ReviveEvent> _events = new();

    public ReviveRules(PartyState party, ReviveSettings settings)
    {
        _party = party ?? throw new ArgumentNullException(nameof(party));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Clamp();
    }

    public int RoomIndex { get; private set; } = -1;

    /// <summary>No enemies left in this room (or none to begin with: castle, shops, NPC rooms).</summary>
    public bool RoomCleared { get; private set; } = true;

    public bool IsRunOver { get; private set; }

    public int RevivesUsed(int slot) => PartyState.IsSlot(slot) ? _revivesUsed[slot] : 0;

    /// <summary>Down, and a teammate may still revive them in this room.</summary>
    public bool CanBeRevived(int slot) =>
        !IsRunOver && _party.IsDown(slot) && _revivesUsed[slot] < _settings.RevivesPerRoom;

    /// <summary>The party entered a new room (not a boss's next phase). Revive counts start again.</summary>
    public void OnRoomStarted(int roomIndex, bool hasEnemies, double now)
    {
        RoomIndex = roomIndex;
        RoomCleared = !hasEnemies;
        Array.Clear(_revivesUsed, 0, _revivesUsed.Length);
        // [DECISION] Nobody can enter a room down (a cleared room brings everyone back), but a player still down from
        // a cleared room's hazard comes back here.
        if (!hasEnemies)
            ReturnAll(now);
    }

    /// <summary>The boss's next phase (even on a new map): the same room for revives, nothing resets.</summary>
    public void OnBossPhaseStarted(double now)
    {
    }

    /// <summary>All enemies are dead or the boss is defeated: everyone down comes back with 10 %, counts reset.</summary>
    public void OnRoomCleared(double now)
    {
        if (IsRunOver)
            return;
        RoomCleared = true;
        ReturnAll(now);
        Array.Clear(_revivesUsed, 0, _revivesUsed.Length);
    }

    /// <summary>A player reached 0 HP and no game revive effect saved them.</summary>
    public void OnDowned(int slot, double now)
    {
        if (IsRunOver || !_party.IsConnected(slot) || _party.IsDown(slot))
            return;
        _party.SetDown(slot, true);
        _events.Add(new ReviveEvent(ReviveEventKind.Downed, slot, -1, RoomIndex, now, 0,
            _revivesUsed[slot] < _settings.RevivesPerRoom));
        if (RoomCleared)
        {
            // [DECISION] Down in a room that is already cleared (a hazard after the fight): "until the room is
            // cleared" is already true, so they come back at once with the room-clear health.
            Return(slot, now);
            return;
        }
        CheckRunOver(now);
    }

    /// <summary>The reviver's hold finished (<see cref="ReviveHold"/>): revives the target if the rules allow.</summary>
    public ReviveResponse TryRevive(int reviver, int target, double now)
    {
        if (IsRunOver)
            return ReviveResponse.RunOver;
        if (reviver == target || !_party.IsActive(reviver))
            return ReviveResponse.ReviverCannot;
        if (!_party.IsDown(target))
            return ReviveResponse.NotDown;
        if (_revivesUsed[target] >= _settings.RevivesPerRoom)
            return ReviveResponse.LimitReached;
        _revivesUsed[target]++;
        _party.SetDown(target, false);
        _events.Add(new ReviveEvent(ReviveEventKind.Revived, target, reviver, RoomIndex, now, _settings.ReviveHealth, false));
        return ReviveResponse.Revived;
    }

    /// <summary>
    /// The game ended the run on purpose for one player (a game-over scene: King Alexander's bomb ran out, package KA):
    /// alone that is the end of the run, so it is for everyone. True once, on the call that ended it.
    /// </summary>
    public bool EndRun(double now)
    {
        if (IsRunOver)
            return false;
        IsRunOver = true;
        _events.Add(new ReviveEvent(ReviveEventKind.RunOver, -1, -1, RoomIndex, now, 0, false));
        return true;
    }

    /// <summary>Checks for the end of the run after players left. True once, on the call that ended it.</summary>
    public bool Update(double now) => CheckRunOver(now);

    /// <summary>Moves the events since the last call into the list (for the party timeline and the network).</summary>
    public void TakeEvents(List<ReviveEvent> into)
    {
        into.AddRange(_events);
        _events.Clear();
    }

    private void ReturnAll(double now)
    {
        for (int i = 0; i < PartyState.MaxPlayers; i++)
            if (_party.IsDown(i) && _party.IsConnected(i))
                Return(i, now);
    }

    private void Return(int slot, double now)
    {
        _party.SetDown(slot, false);
        _events.Add(new ReviveEvent(ReviveEventKind.ReturnedAtClear, slot, -1, RoomIndex, now, _settings.RoomClearHealth, false));
    }

    private bool CheckRunOver(double now)
    {
        if (IsRunOver || _party.ConnectedCount == 0 || _party.ActiveCount > 0)
            return false;
        IsRunOver = true;
        _events.Add(new ReviveEvent(ReviveEventKind.RunOver, -1, -1, RoomIndex, now, 0, false));
        return true;
    }
}

public enum HoldState : byte
{
    Idle = 0,
    Holding,
    /// <summary>The hold time is reached: send the revive to the host (<see cref="ReviveRules.TryRevive"/>).</summary>
    Done,
    Cancelled,
}

public enum HoldCancel : byte
{
    None = 0,
    Released,
    /// <summary>The reviver moved further than <see cref="ReviveSettings.Reach"/> from the head.</summary>
    MovedAway,
    ReviverDown,
    /// <summary>The target came back (another reviver, the room cleared) or left.</summary>
    TargetGone,
    /// <summary>Only with <see cref="ReviveSettings.HoldBreaksOnDamage"/>.</summary>
    Damaged,
}

/// <summary>
/// The reviver's own hold of Interact, on the reviver's game (no lag on the progress ring; the host only checks the
/// end with <see cref="ReviveRules.TryRevive"/>). [DECISION] What breaks the hold: letting go, moving further than
/// <see cref="ReviveSettings.Reach"/> from the head, going down, the target coming back or leaving; being hit doesn't
/// (plan: "they can be hit meanwhile"; setting <see cref="ReviveSettings.HoldBreaksOnDamage"/>). A broken hold starts
/// again from 0.
/// </summary>
public sealed class ReviveHold
{
    private readonly ReviveSettings _settings;
    private double _started;

    public ReviveHold(ReviveSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Clamp();
    }

    public HoldState State { get; private set; }
    public HoldCancel LastCancel { get; private set; }
    public int Target { get; private set; } = -1;

    /// <summary>0..1 for the ring over the head.</summary>
    public float Progress(double now) =>
        State == HoldState.Done ? 1f
        : State != HoldState.Holding ? 0f
        : (float)Math.Min(1.0, Math.Max(0.0, (now - _started) / _settings.HoldSeconds));

    /// <summary>The reviver started holding Interact next to this player's head.</summary>
    public void Begin(int target, double now)
    {
        Target = target;
        _started = now;
        State = HoldState.Holding;
        LastCancel = HoldCancel.None;
    }

    /// <summary>Every frame while a hold runs. Done (once) when the time is reached; Cancelled when it broke.</summary>
    public HoldState Update(double now, bool holding, float distance, bool reviverDown, bool targetStillDown)
    {
        if (State != HoldState.Holding)
            return State;
        if (!holding)
            return Cancel(HoldCancel.Released);
        if (reviverDown)
            return Cancel(HoldCancel.ReviverDown);
        if (!targetStillDown)
            return Cancel(HoldCancel.TargetGone);
        if (distance > _settings.Reach)
            return Cancel(HoldCancel.MovedAway);
        if (now - _started >= _settings.HoldSeconds)
            State = HoldState.Done;
        return State;
    }

    /// <summary>The reviver was hit.</summary>
    public void OnDamaged()
    {
        if (State == HoldState.Holding && _settings.HoldBreaksOnDamage)
            Cancel(HoldCancel.Damaged);
    }

    /// <summary>Back to idle after Done or Cancelled.</summary>
    public void Clear()
    {
        State = HoldState.Idle;
        Target = -1;
    }

    private HoldState Cancel(HoldCancel why)
    {
        State = HoldState.Cancelled;
        LastCancel = why;
        return State;
    }
}
