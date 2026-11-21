using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using FX;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// The door vote in the game (COOP_PLAN §4.3, Umut: "a coin flip, no long waits"). A press on a door by ANY player
/// is that player's vote and no longer the room change itself (before: the host's press loaded the next room, a
/// client's press did nothing, "The host picks the door."):
/// - A client's press goes to the host as a message, the host's own press straight into the rule.
/// - The host owns one Rules.DoorVote per cleared room. It resolves when every living, connected player voted, or
///   <see cref="DoorVoteSettings.VoteSeconds"/> after the first vote: one door voted = that door, different doors =
///   a coin flip weighted by votes, seeded from the run's seed and the room.
/// - On the result the host loads that door with the game's own LevelManager.LoadNextMap; CoopWorld.NextMapPatch
///   sends it to everyone as before. Nothing else changed for room changes: the test route, story scenes and the
///   game's own jumps still go through LoadNextMap on the host.
/// - After every change the host tells everyone the vote's state; every game draws it (DoorVotePanel: the
///   doors' pictures, who voted for which, the countdown, the coin flip).
/// - The reward wait (§4.2, Rules.RoomRewardGate): the game opens a player's doors once their room reward is taken
///   (or at once in a room without one). Every game tells the host when its doors open (Ready); the host's wait
///   begins when its fight is over and ends when nobody holds it up any more, or after
///   <see cref="RewardGateSettings.MaxWaitSeconds"/> (setting Coop/RewardWaitSeconds). Presses made during the wait
///   are kept and count when it ends; only then does the vote's countdown start. A press also says "done".
/// Decided here: a room with ONE door is voted on like any other (a press = "I am ready", the others have the
/// vote's seconds).
/// </summary>
// REVIEW(fable): the reward wait (COOP_LOG 57): Ready message + host-side gate; is "doors open" the right "done", and
// is keeping early presses (instead of refusing them) right?
internal sealed class CoopDoors
{
    /// <summary>A press the host's state does not show yet is sent again this often, for this long. The host can
    /// refuse a press: its fight ends a moment after a client's (the death messages take the trip).</summary>
    private const double ResendSeconds = 1, WishSeconds = 12;

    private readonly CoopSession _session;
    private readonly CoopWorld _world;
    private readonly TeammateTags _tags;
    private readonly NetWriter _writer = new(32);
    private readonly DoorVoteSettings _settings = new();
    private readonly RewardGateSettings _gateSettings = new();
    private readonly DoorVotePanel _panel;

    // the host: the room's reward wait and vote, and when the party goes through the chosen door (below 0: not
    // resolved yet); presses made while the wait runs (by slot, -1 none); who said "my doors are open", for which room
    private DoorVote _vote;
    private RoomRewardGate _gate;
    private uint _voteRoom;
    private double _loadAt = -1;
    private bool _gateOpen;
    private readonly int[] _early = new int[PartyState.MaxPlayers];
    private uint _readyRoom;
    private int _readyMask;

    // every game: the room whose open doors this game reported
    private uint _reportedRoom;

    // every game: the vote as the host told it last (the host: as it stands), for the line
    private DoorVoteMessage _state;
    private double _stateAt = -1;
    private int _shots; // test pictures taken of the state on screen

    // this game's own press, until the host's state shows it
    private uint _wishRoom;
    private int _wishDoor = -1;
    private double _wishSince, _wishSent;

    public CoopDoors(CoopSession session, CoopWorld world, TeammateTags tags)
    {
        _session = session;
        _world = world;
        _tags = tags;
        _panel = new DoorVotePanel(tags);
        session.RegisterHandler(MessageType.DoorVote, OnMessage);
    }

    public void Dispose() => _panel.Destroy();

    private static int Seed => Data.GameData.Save.instance.randomSeed;

    // ------------------------------------------------------------------ a press

    /// <summary>This game's player pressed a door (the patch on Gate.InteractWith): their vote.</summary>
    public void Press(Gate gate)
    {
        if (gate == null || !_world.RoomLoaded || _world.Busy)
            return;
        uint room = CoopWorld.CurrentRoom();
        int door = (int)gate._nodeIndex;
        double now = Time.realtimeSinceStartupAsDouble;
        if (_wishDoor == door && _wishRoom == room && now - _wishSince < WishSeconds)
            return; // the same door again: the vote stands
        _wishRoom = room;
        _wishDoor = door;
        _wishSince = _wishSent = now;
        PersistentSingleton<SoundManager>.Instance?.PlaySound(gate._interactSound, gate.transform.position);
        CoopTrace.Write("vote-press", room, door, slot: _session.LocalSlot);
        Send(room, door, now);
    }

    private void Send(uint room, int door, double now)
    {
        if (_session.IsHost)
        {
            Cast(_session.LocalSlot, room, door, now);
            return;
        }
        _writer.Reset();
        DoorVoteMessages.Write(_writer, DoorVoteMessage.Press(room, door));
        _session.Send(0, MessageType.DoorVote, _writer, Delivery.Reliable);
    }

    /// <summary>The host's state shows this game's press (or the vote is over).</summary>
    private bool Taken() =>
        _stateAt >= 0 && _state.Room == _wishRoom && (_state.Resolved || _state.VoteOf(_session.LocalSlot) == _wishDoor);

    private void Resend(double now)
    {
        if (_wishDoor < 0)
            return;
        if (_wishRoom != CoopWorld.CurrentRoom() || now - _wishSince > WishSeconds)
        {
            _wishDoor = -1; // another room, or the host never took it: a new press starts over
            return;
        }
        if (Taken() || now - _wishSent < ResendSeconds)
            return;
        _wishSent = now;
        Send(_wishRoom, _wishDoor, now);
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (!DoorVoteMessages.TryRead(payload, out var m))
            return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (m.Kind == DoorVoteMessageKind.Vote)
        {
            if (_session.IsHost && message.FromSlot != _session.LocalSlot)
                Cast(message.FromSlot, m.Room, m.Door, now);
            return;
        }
        if (m.Kind == DoorVoteMessageKind.Ready)
        {
            if (_session.IsHost && message.FromSlot != _session.LocalSlot)
                Ready(message.FromSlot, m.Room, now);
            return;
        }
        if (_session.IsHost || message.FromSlot != 0)
            return; // only the host says how a vote stands
        Shown(m, now);
        if (m.Resolved)
            CoopTrace.Write("vote-result-got", m.Room, m.Door, what: Outcome(m.CoinFlip, m.TimedOut), amount: m.Chance);
    }

    // ------------------------------------------------------------------ the host's rule

    private void Cast(int slot, uint room, int door, double now)
    {
        var party = CoopRevive.Current?.Party;
        string refused = !_world.IsHost || party == null ? "no session"
            : room != CoopWorld.CurrentRoom() || !_world.RoomLoaded || _world.Busy ? "another room"
            : !Open(door) ? "door closed"
            : null;
        if (refused == null && (_vote == null || _voteRoom != room))
            Begin(room, party, now);
        if (refused != null || _vote == null)
        {
            CoopTrace.Write("vote-refused", room, door, what: refused ?? "no vote", slot: slot);
            return;
        }
        // a press says "my reward is done" too (a player can only press an open door)
        Ready(slot, room, now, tell: false);
        OpenWhenDone(now);
        if (!_gateOpen)
        {
            // the reward wait still runs: the press is kept and counts when it ends
            if (_early[slot] != door && !_vote.IsResolved)
            {
                _early[slot] = door;
                CoopTrace.Write("vote-early", room, door, slot: slot);
            }
            Tell(now);
            return;
        }
        var response = _vote.Vote(slot, door, now);
        CoopTrace.Write("vote", room, door, what: response.ToString(), slot: slot);
        if (response == VoteResponse.Accepted && !_vote.IsResolved)
            Tell(now); // a vote that resolved it: Tick tells the result
    }

    public void Tick(double now)
    {
        uint here = CoopWorld.CurrentRoom();
        if (_world.Active && _world.RoomLoaded && !_world.Busy && here != 0)
        {
            var party = CoopRevive.Current?.Party;
            if (_world.IsHost && party != null && FightOver() && Gates().Count > 0 && (_vote == null || _voteRoom != here))
                Begin(here, party, now);
            if (_reportedRoom != here && OwnDoorsOpen())
            {
                // this game's doors opened: its player is done with the room's reward
                _reportedRoom = here;
                CoopTrace.Write("reward-done", here, slot: _session.LocalSlot);
                if (_session.IsHost)
                    Ready(_session.LocalSlot, here, now);
                else
                {
                    _writer.Reset();
                    DoorVoteMessages.Write(_writer, DoorVoteMessage.Done(here));
                    _session.Send(0, MessageType.DoorVote, _writer, Delivery.Reliable);
                }
            }
        }
        if (_vote != null && _world.IsHost && _voteRoom == here && _world.RoomLoaded)
            OpenWhenDone(now);
        if (_vote != null)
        {
            if (!_world.IsHost || _voteRoom != CoopWorld.CurrentRoom() || !_world.RoomLoaded)
            {
                // the room went another way (the test route, a scene's jump, the session ended): the vote is void
                if (!_vote.IsResolved)
                    CoopTrace.Write("vote-void", _voteRoom);
                _vote = null;
                _gate = null;
                _loadAt = -1;
            }
            else
            {
                _vote.Update(now); // the time ran out, or a voter went down or left
                if (_vote.IsResolved && _loadAt < 0)
                {
                    var result = _vote.Result;
                    _loadAt = now + (result.CoinFlip ? _settings.CoinFlipSeconds : 0f);
                    CoopTrace.Write("vote-result", _voteRoom, result.Door, what: Outcome(result.CoinFlip, result.TimedOut),
                        amount: result.ChanceOf(result.Door));
                    Tell(now);
                }
                if (_loadAt >= 0 && now >= _loadAt && !_world.Busy)
                {
                    int door = _vote.Result.Door;
                    _vote = null;
                    _gate = null;
                    _loadAt = -1;
                    Plugin.Log.LogInfo($"[Coop] door vote: the party takes door {door + 1}");
                    Singleton<Service>.Instance?.levelManager?.LoadNextMap((NodeIndex)door); // NextMapPatch tells everyone
                }
            }
        }
        Resend(now);
        Show(now);
    }

    private static string Outcome(bool coinFlip, bool timedOut) =>
        coinFlip ? (timedOut ? "flip, time up" : "flip") : timedOut ? "time up" : "agreed";

    /// <summary>The host's room: its fight is over and it has doors. The reward wait and the vote begin.</summary>
    private void Begin(uint room, PartyState party, double now)
    {
        int index = (int)(room & 0x7FFFFFFF);
        // the host's own settings (Coop/DoorVoteSeconds, Coop/RewardWaitSeconds); the rules pull them into range
        _settings.VoteSeconds = Plugin.CoopDoorVoteSeconds?.Value ?? DoorVoteSettings.DefaultVoteSeconds;
        _gateSettings.MaxWaitSeconds = Plugin.CoopRewardWaitSeconds?.Value ?? RewardGateSettings.DefaultMaxWaitSeconds;
        _gate = new RoomRewardGate(party, _gateSettings);
        _vote = new DoorVote(index, 2, party, _settings, CoopRandom.ForRoom(Seed, index, CoopRandom.Purpose.DoorFlip), _gate);
        _voteRoom = room;
        _loadAt = -1;
        _gateOpen = false;
        for (int slot = 0; slot < _early.Length; slot++)
            _early[slot] = -1;
        _gate.Begin(now);
        if (_readyRoom == room)
            for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
                if (((_readyMask >> slot) & 1) != 0)
                    _gate.MarkDone(slot, now);
        CoopTrace.Write("reward-wait", room, _gate.WaitingCount);
    }

    /// <summary>The host: this player's doors are open in that room (their reward is done). Kept by room: a
    /// client's word can come before the host's own fight is over.</summary>
    private void Ready(int slot, uint room, double now, bool tell = true)
    {
        if (!PartyState.IsSlot(slot))
            return;
        if (_readyRoom != room)
        {
            _readyRoom = room;
            _readyMask = 0;
        }
        if (((_readyMask >> slot) & 1) != 0)
            return;
        _readyMask |= 1 << slot;
        CoopTrace.Write("reward-ready", room, slot: slot);
        if (_gate != null && _voteRoom == room)
        {
            _gate.MarkDone(slot, now);
            if (tell && !_gateOpen && AnyEarly())
                Tell(now); // the panel shows who is still busy
        }
    }

    /// <summary>The host: once nobody holds the vote up (or the wait ran out), the kept presses count.</summary>
    private void OpenWhenDone(double now)
    {
        if (_gateOpen || _gate == null || _vote == null)
            return;
        _gate.Update(now);
        if (!_gate.IsReleased)
            return;
        _gateOpen = true;
        CoopTrace.Write("reward-wait-over", _voteRoom, what: _gate.TimedOut ? "time up" : "everyone done");
        bool any = false;
        for (int slot = 0; slot < _early.Length; slot++)
        {
            if (_early[slot] < 0 || _vote.IsResolved)
                continue;
            any = true;
            var response = _vote.Vote(slot, _early[slot], now);
            CoopTrace.Write("vote", _voteRoom, _early[slot], what: response.ToString(), slot: slot);
            _early[slot] = -1;
        }
        if (any && !_vote.IsResolved)
            Tell(now); // a vote that resolved it: Tick tells the result
    }

    private bool AnyEarly()
    {
        foreach (int door in _early)
            if (door >= 0)
                return true;
        return false;
    }

    private void Tell(double now)
    {
        int ready = 0;
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
            if (_gate == null || !_gate.IsWaitingFor(slot))
                ready |= 1 << slot;
        var state = DoorVoteMessage.State(_voteRoom, _vote, now, _gateOpen ? null : _early, ready,
            _gateOpen || _gate == null ? -1f : (float)_gate.SecondsLeft(now));
        _writer.Reset();
        DoorVoteMessages.Write(_writer, state);
        _session.Broadcast(MessageType.DoorVote, _writer, Delivery.Reliable);
        Shown(state, now);
    }

    // ------------------------------------------------------------------ the doors of this game's room

    private static readonly List<Gate> Found = new();

    /// <summary>The room's doors a player can go through (not the broken ones a path leaves behind).</summary>
    private static List<Gate> Gates()
    {
        Found.Clear();
        var map = Map.Instance;
        if (map == null)
            return Found;
        foreach (var gate in map.GetComponentsInChildren<Gate>())
            if (gate.isActiveAndEnabled && gate._collider != null && gate._collider.enabled)
                Found.Add(gate);
        return Found;
    }

    /// <summary>This game's doors are open: the game opened them once its player took the room's reward.</summary>
    private static bool OwnDoorsOpen()
    {
        foreach (var gate in Gates())
            if (gate.activated)
                return true;
        return false;
    }

    /// <summary>The room's fight is over (or it had none).</summary>
    private static bool FightOver()
    {
        var waves = Map.Instance != null ? Map.Instance.waveContainer : null;
        return waves == null || waves.enemyWaves.Length == 0 || waves.state != EnemyWaveContainer.State.Remain;
    }

    /// <summary>
    /// The host's room has this door and its fight is over. Not asked: that the HOST's doors are open. The game
    /// opens each game's doors when that game's player took the room's reward (Map.CCheckRewardActivating), so a
    /// client who has taken theirs can press while the host has not: that press is a vote (2026-11-15, after the
    /// first game run; not run in the game with this rule yet).
    /// </summary>
    private static bool Open(int door)
    {
        if (!FightOver())
            return false;
        foreach (var gate in Gates())
            if ((int)gate._nodeIndex == door)
                return true;
        return false;
    }

    // ------------------------------------------------------------------ what every game shows

    private void Shown(DoorVoteMessage state, double now)
    {
        _state = state;
        _stateAt = now;
        _shots = 0;
        if (state.Room == CoopWorld.CurrentRoom())
            _panel.SetDoors(Gates()); // now: the room and its doors are gone while the next one loads
    }

    private void Show(double now)
    {
        bool show = false;
        if (_stateAt >= 0)
        {
            if (_state.Resolved)
            {
                if (now - _stateAt > DoorVotePanel.ResultSeconds(_state))
                    _stateAt = -1;
                else
                    show = true; // stays over the loading screen
            }
            else if (_state.Room != CoopWorld.CurrentRoom())
                _stateAt = -1; // this game left the room the vote was about
            else
                show = AnyVote();
        }
        if (!show)
        {
            _panel.Hide();
            return;
        }
        _panel.Draw(_state, _stateAt, now, _session.Roster);
        TestPictures(now - _stateAt);
    }

    private bool AnyVote()
    {
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
            if (_state.VoteOf(slot) >= 0)
                return true;
        return false;
    }

    /// <summary>Test runs only (CoopShots does nothing in a normal game): a picture of a vote that stands, of the
    /// coin in the air and of the result.</summary>
    private void TestPictures(double since)
    {
        if (!_state.Resolved)
        {
            if (_shots == 0 && since >= 0.6)
            {
                _shots = 1;
                Testing.CoopShots.Extra("vote");
            }
            return;
        }
        if (_state.CoinFlip && _shots < 1 && since >= 0.45)
        {
            _shots = 1;
            Testing.CoopShots.Extra("flip");
        }
        else if (_shots < 2 && since >= (_state.CoinFlip ? DoorVotePanel.FlipSeconds + 0.35 : 0.4))
        {
            _shots = 2;
            Testing.CoopShots.Extra("result");
        }
    }
}
