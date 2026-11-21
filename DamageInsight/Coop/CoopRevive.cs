using System;
using System.Collections.Generic;
using Characters;
using Characters.Controllers;
using Characters.Player;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using DamageInsight.Lang;
using GameResources;
using HarmonyLib;
using Level;
using Scenes;
using Services;
using Singletons;
using UnityEngine;
using UserInput;

namespace DamageInsight.Coop;

/// <summary>
/// Going down and revive in the game (COOP_PLAN §4.4; the rules themselves are Rules.ReviveRules, tested). Before
/// this a dead player's game showed "Shattered!" and reset while the others played on (Umut's pictures, 2026-11-13).
/// - A player at 0 health whose own revive effects did not save them GOES DOWN instead of dying: the game's death
///   (HandlePlayerDeath.OnPlayerDied: save gone, result screen, reset) does not run. The body is hidden, the head
///   lies where they fell, input is off, enemies leave them alone, the camera follows a teammate.
/// - The host decides: a downed player's game says "I went down", a reviver's game says "my hold is complete", the
///   host's ReviveRules answer with events (Downed, Revived, ReturnedAtClear, RunOver) that every game shows.
/// - A teammate holds Interact next to the body for the hold time (ReviveHold, on the reviver's game). A ring over
///   the downed player shows it on every game (ReviveRing: it fills, a heart beats faster and faster with a
///   heartbeat sound, a flash when the revive lands); the reviver's game tells the others how far the hold is.
/// - The host polls the room: enemies there = the fight runs; none for 3 s = cleared, everyone down comes back.
///   A boss's later arenas (out-of-track maps) are the same room. Known leniency: a pause of more than 3 s
///   between two phases counts as cleared.
/// - Everyone down: the game's own death runs on every game. A session that ends while a player is down brings
///   them back with the room-clear health (Umut 2026-11-13: a lost connection lets everyone play on alone).
/// Settings are the defaults of Rules.ReviveSettings until the co-op settings page exists.
/// </summary>
internal sealed class CoopRevive : IDisposable
{
    internal static CoopRevive Current { get; private set; }

    /// <summary>Test copies (`--skulinsight-autorun-down=SECONDS`): this copy's player is killed that long after
    /// enemies showed up in a room, once per room (0 = never).</summary>
    internal static float TestDownAfter;

    private readonly CoopSession _session;
    private readonly Func<int, Character> _playerOf;
    private readonly Func<Character, int> _slotOf;
    private readonly TeammateTags _tags;
    private readonly ReviveRing _ring;
    // Umut 2026-11-14: "hold time: 3s, revives per room 1" (ReviveSettings' defaults).
    private readonly ReviveSettings _settings; // the host's numbers (CoopHostRules), the same object on every change
    private readonly PartyState _party = new();
    private readonly ReviveRules _rules;
    private readonly ReviveHold _hold;
    private readonly bool[] _down = new bool[PartyState.MaxPlayers];
    private readonly bool[] _canRevive = new bool[PartyState.MaxPlayers];
    private readonly List<Renderer>[] _hidden = new List<Renderer>[PartyState.MaxPlayers];
    private readonly PlayerDieHeadParts[] _heads = new PlayerDieHeadParts[PartyState.MaxPlayers];
    private readonly List<ReviveEvent> _events = new();
    private readonly NetWriter _writer = new();
    private uint _roomId, _testRoom;
    private int _roomIndex;
    private double _nextRoomCheck, _quietSince = -1, _graceUntil, _fightSince = -1;
    private bool _passDeath, _runOver, _testDowned, _testTwice;

    /// <summary>This co-op run is over (everyone fell, or a game-over scene): the test bot's route ends.</summary>
    internal static bool RunIsOver => Current?._runOver == true;
    // A game-over scene killed this game's player (Suicide under a cut scene, package KA): the run ends for everyone
    // (_finalDeath until LocalDowned took it; _diedFinal: this game's own death already ran, the host's RunOver
    // must not run it a second time).
    private bool _finalDeath, _diedFinal;
    private double _upAgainSince = -1;
    private Character _player, _watching;
    private string _notice = "";
    // this game's own hold, for the ring every game draws: its number, when the next "this far" goes out, and the
    // player whose finished hold waits for the host's answer (no second hold on them meanwhile)
    private int _holdId, _awaiting = -1;
    private double _nextHoldSend, _awaitUntil;

    /// <summary>"This far" messages of a hold per second, and how long a finished hold waits for the host's answer.</summary>
    private const double HoldSendInterval = 0.1, AnswerWait = 1.5;

    public CoopRevive(CoopSession session, Func<int, Character> playerOf, Func<Character, int> slotOf, TeammateTags tags,
        ReviveSettings settings)
    {
        _settings = settings ?? new ReviveSettings();
        _session = session;
        _playerOf = playerOf;
        _slotOf = slotOf;
        _tags = tags;
        _ring = new ReviveRing(tags);
        _rules = new ReviveRules(_party, _settings);
        _hold = new ReviveHold(_settings);
        session.RegisterHandler(MessageType.Revive, OnMessage);
        CoopTargeting.IsDown = IsDown;
        Current = this;
    }

    private static double Now => Time.realtimeSinceStartupAsDouble;

    /// <summary>Who is in the run and who is down (on the host: as its rules have it). The door vote counts the
    /// players who are up.</summary>
    internal PartyState Party => _party;

    private static Character LocalPlayer => Singleton<Service>.Instance?.levelManager?.player;

    /// <summary>Going down replaces dying: a running session with someone else in it, and not everyone down.</summary>
    private bool Active => _session.State == SessionState.Active && _party.ConnectedCount >= 2 && !_runOver;

    public bool IsDown(Character character)
    {
        int slot = _slotOf(character);
        return PartyState.IsSlot(slot) && _down[slot];
    }

    public bool IsDown(int slot) => PartyState.IsSlot(slot) && _down[slot];

    private string Name(int slot) => _session.PlayerAt(slot)?.Name ?? "?";

    public void Dispose()
    {
        if (Current == this)
            Current = null;
        CoopTargeting.IsDown = _ => false;
        Notice("");
        _ring.Destroy();
        int local = _session.LocalSlot;
        bool wasDown = IsDown(local);
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
            SetDown(slot, false, false);
        if (wasDown)
            ReviveLocal(_settings.RoomClearHealth, "the session ended");
        var player = LocalPlayer;
        if (player != null)
            player.invulnerable.Detach(this);
    }

    // ------------------------------------------------------------------ every frame

    public void Tick(double now)
    {
        // who is in the run (the rules count connected players)
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
        {
            bool here = _session.PlayerAt(slot) != null;
            if (here && !_party.IsConnected(slot))
                _party.Join(slot);
            else if (!here && _party.IsConnected(slot))
            {
                _party.Leave(slot);
                SetDown(slot, false, false);
            }
        }
        var me = LocalPlayer;
        int local = _session.LocalSlot;
        _ring.PromptSlot = -1; // LocalHold names the player whose ring shows the key
        if (me != _player)
        {
            // a new player object (the game was reset after everyone fell): nothing of the old one is left
            _player = me;
            _runOver = false;
            _finalDeath = _diedFinal = false;
            if (IsDown(local))
                SetDown(local, false, false);
            _watching = null;
        }
        if (_graceUntil > 0 && now > _graceUntil && me != null)
        {
            _graceUntil = 0;
            me.invulnerable.Detach(this);
        }
        if (_session.IsHost)
            HostRoom(now);
        if (_runOver)
        {
            // everyone fell: the game's result screen is up. No prompt and no rings over it (Umut's picture of
            // 2026-11-15: both rings and "You are down. A teammate can revive you." stood on "Shattered!")
            Notice("");
            for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
                _ring.Remove(slot);
            return;
        }
        if (me == null || !PartyState.IsSlot(local) || _session.State != SessionState.Active)
        {
            Notice("");
            DrawRings(now);
            return;
        }
        TestDown(now, me);
        if (IsDown(local))
            Watch(local);
        LocalHold(now, me, local);
        DrawRings(now);
    }

    /// <summary>The ring over every downed player, this game's own too (they watch a teammate meanwhile).</summary>
    private void DrawRings(double now)
    {
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
        {
            _ring.Tick(slot, _playerOf(slot), _down[slot], _canRevive[slot], now);
            // test copies: a picture of the ring about half full (once per hold) and of the flash
            if (!Testing.CoopProbe.On)
                continue;
            float shown = _ring.Progress(slot);
            if (shown >= 0.55f && !_ringShot[slot])
            {
                _ringShot[slot] = true;
                Testing.CoopShots.Extra("ring");
            }
            else if (shown < 0.1f)
                _ringShot[slot] = false;
        }
        if (_flashShot < FlashShots.Length && now >= _flashAt + FlashShots[_flashShot])
        {
            if (Testing.CoopProbe.On)
                Testing.CoopShots.Extra("flash" + _flashShot);
            _flashShot++;
        }
    }

    /// <summary>Test copies: pictures this long after a revive landed (the flash, the sparks, the fading).</summary>
    private static readonly double[] FlashShots = { 0.06, 0.2, 0.36, 0.55 };
    private readonly bool[] _ringShot = new bool[PartyState.MaxPlayers];
    private double _flashAt;
    private int _flashShot = FlashShots.Length;

    /// <summary>Host: is the room fought in, or cleared? (Polled: waves, bosses and scripted fights all end with
    /// "no living enemy".)</summary>
    private void HostRoom(double now)
    {
        if (now < _nextRoomCheck)
            return;
        _nextRoomCheck = now + 0.5;
        var world = CoopWorld.Current;
        var map = Map.Instance;
        if (world == null || map == null || !world.RoomLoaded || world.Busy)
            return;
        uint room = CoopWorld.CurrentRoom();
        bool enemies = Enemies(map);
        if (room != _roomId)
        {
            _roomId = room;
            _quietSince = -1;
            if (map.name.Contains("Out track"))
                _rules.OnBossPhaseStarted(now); // a boss's next arena: the same room for revives
            else
                _rules.OnRoomStarted(++_roomIndex, enemies, now);
        }
        else if (enemies)
        {
            _quietSince = -1;
            if (_rules.RoomCleared)
                _rules.OnRoomStarted(_roomIndex, true, now); // the fight began after the room did (a boss's intro)
        }
        else if (!_rules.RoomCleared)
        {
            if (_quietSince < 0)
                _quietSince = now;
            if (now - _quietSince >= 3.0)
                _rules.OnRoomCleared(now);
        }
        _rules.Update(now);
        Flush();
    }

    private static bool Enemies(Map map)
    {
        foreach (var c in map.GetComponentsInChildren<Character>())
            if (c.health != null && !c.health.dead
                && c.type is Character.Type.Boss or Character.Type.Adventurer or Character.Type.Named or Character.Type.TrashMob)
                return true;
        return false;
    }

    /// <summary>The downed player watches a teammate who is up.</summary>
    private void Watch(int local)
    {
        Character mate = null;
        for (int slot = 0; slot < PartyState.MaxPlayers && mate == null; slot++)
            if (slot != local && !_down[slot])
                mate = _playerOf(slot);
        if (mate == null || mate == _watching)
            return;
        _watching = mate;
        Track(mate);
    }

    private static void Track(Character character)
    {
        try
        {
            Scene<GameBase>.instance.cameraController.StartTrack(character.transform);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] revive: the camera could not follow " + character.name + ": " + e.Message);
        }
    }

    /// <summary>This game's player next to a downed teammate: the prompt, the hold, the request to the host.</summary>
    private void LocalHold(double now, Character me, int local)
    {
        bool meDown = IsDown(local);
        int target = -1;
        float best = float.MaxValue;
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
        {
            if (slot == local || !_down[slot] || !_canRevive[slot])
                continue;
            var body = _playerOf(slot);
            if (body == null)
                continue;
            float distance = Vector2.Distance(me.transform.position, body.transform.position);
            if (distance < best)
            {
                best = distance;
                target = slot;
            }
        }
        // a test bot goes to the body by itself (it has no path finding for a spot that is not an enemy)
        if (LocalBot.Active && !meDown && target >= 0 && best > _settings.Reach * 0.8f)
        {
            me.transform.position = _playerOf(target).transform.position;
            best = 0f;
        }
        bool holding = LocalBot.Active || KeyMapper.Map.Interaction.IsPressed;
        if (_hold.State == HoldState.Holding)
        {
            int held = _hold.Target;
            var body = PartyState.IsSlot(held) ? _playerOf(held) : null;
            float distance = body != null ? Vector2.Distance(me.transform.position, body.transform.position) : float.MaxValue;
            var state = _hold.Update(now, holding, distance, meDown, IsDown(held) && _canRevive[held]);
            if (state == HoldState.Holding)
            {
                // the ring over their head shows how far it is (here at once, on the other games by messages)
                ShowHold(held, local, _hold.Progress(now), now, false);
                _ring.PromptSlot = held;
                Notice(Loc.F("Hold the interact key to revive {0}", Name(held)));
                return;
            }
            if (state == HoldState.Done)
            {
                CoopTrace.Write("revive-request", CoopWorld.CurrentRoom(), what: "for slot " + held, slot: local);
                // the ring stays full until the host's answer is here
                _awaiting = held;
                _awaitUntil = now + AnswerWait;
                ShowHold(held, local, 1f, now, true);
                if (_session.IsHost)
                {
                    _rules.TryRevive(local, held, now);
                    Flush();
                }
                else
                    Send(ReviveMessage.Request(held));
            }
            else
            {
                CoopTrace.Write("revive-hold-broken", CoopWorld.CurrentRoom(), what: _hold.LastCancel.ToString(), slot: local);
                EndHold(held, local);
            }
            _hold.Clear();
        }
        if (_awaiting >= 0)
        {
            if (IsDown(_awaiting) && now < _awaitUntil)
                ShowHold(_awaiting, local, 1f, now, false);
            else
            {
                if (IsDown(_awaiting))
                    EndHold(_awaiting, local); // the host said no (someone else was faster, no revive left)
                _awaiting = -1;
            }
        }
        // the host's own finished hold revived them in this very frame: no new hold on someone who is up
        // (it began and broke at once with TargetGone: the 11 broken holds of the first ring test)
        if (target >= 0 && (!_down[target] || !_canRevive[target]))
            target = -1;
        if (meDown)
            Notice(_canRevive[local] ? Loc.T("You are down. A teammate can revive you.") : Loc.T("You are down until the room is cleared."));
        else if (target >= 0 && best <= _settings.Reach)
        {
            if (holding && target != _awaiting)
            {
                _hold.Begin(target, now);
                _holdId = (_holdId + 1) & 0xFF;
                ShowHold(target, local, 0f, now, true);
                CoopTrace.Write("revive-hold", CoopWorld.CurrentRoom(), what: "for slot " + target, slot: local);
            }
            _ring.PromptSlot = target;
            Notice(Loc.F("Hold the interact key to revive {0}", Name(target)));
        }
        else
            Notice("");
    }

    /// <summary>This game's hold is this far: the ring here, and "this far" to the other games (ten a second;
    /// <paramref name="force"/>: now).</summary>
    private void ShowHold(int target, int local, float progress, double now, bool force)
    {
        _ring.Hold(target, local, _holdId, progress, _settings.HoldSeconds, now);
        if (!force && now < _nextHoldSend)
            return;
        _nextHoldSend = now + HoldSendInterval;
        SendHold(ReviveMessage.Hold(target, local, _holdId, progress, _settings.HoldSeconds), -1);
    }

    /// <summary>This game's hold broke: the ring runs empty here and on the other games.</summary>
    private void EndHold(int target, int local)
    {
        _ring.Stop(target, local, _holdId);
        SendHold(ReviveMessage.Hold(target, local, _holdId, -1f, _settings.HoldSeconds), -1);
    }

    /// <summary>A hold's "this far" on its way: a client's to the host, the host's (its own, or a client's passed
    /// on) to every client but <paramref name="except"/>. Progress may get lost; "it ended" may not.</summary>
    private void SendHold(in ReviveMessage message, int except)
    {
        _writer.Reset();
        ReviveMessages.Write(_writer, message);
        var delivery = message.HoldEndedEarly ? Delivery.Reliable : Delivery.Unreliable;
        if (!_session.IsHost)
        {
            _session.Send(0, MessageType.Revive, _writer, delivery);
            return;
        }
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
            if (slot != _session.LocalSlot && slot != except && _session.PlayerAt(slot) != null)
                _session.Send(slot, MessageType.Revive, _writer, delivery);
    }

    /// <summary>A teammate's hold as its messages tell it: the ring over the downed player follows.</summary>
    private void SeeHold(in ReviveMessage m)
    {
        if (!PartyState.IsSlot(m.Slot) || !PartyState.IsSlot(m.BySlot) || m.BySlot == _session.LocalSlot)
            return;
        if (m.HoldEndedEarly)
        {
            _ring.Stop(m.Slot, m.BySlot, m.HoldId);
            return;
        }
        if (!_ring.Holding(m.Slot) && IsDown(m.Slot))
            CoopTrace.Write("revive-hold-seen", CoopWorld.CurrentRoom(), what: "by slot " + m.BySlot, amount: m.Progress, slot: m.Slot);
        _ring.Hold(m.Slot, m.BySlot, m.HoldId, m.Progress, m.HoldSeconds, Now);
    }

    private void Notice(string text)
    {
        if (text == _notice)
            return;
        _notice = text;
        _tags.Notice(text);
    }

    private void TestDown(double now, Character me)
    {
        if (TestDownAfter <= 0f || _session.IsHost || Map.Instance == null || me.health == null || me.health.dead)
            return;
        uint room = CoopWorld.CurrentRoom();
        if (room != _testRoom)
        {
            _testRoom = room;
            _testDowned = _testTwice = false;
            _fightSince = _upAgainSince = -1;
        }
        if (_testDowned && !_testTwice && ((room >> 8) & 3) == 1 && Enemies(Map.Instance))
        {
            // Down twice in a room (about every fourth room): 5 s after the revive, down again. With one revive per
            // room it can't be revived now: it stays down and comes back when the room is cleared (COOP_LOG 52's open case).
            if (IsDown(_session.LocalSlot))
                _upAgainSince = -1;
            else if (_upAgainSince < 0)
                _upAgainSince = now;
            else if (now - _upAgainSince >= 5)
            {
                _testTwice = true;
                Plugin.Log.LogInfo("[CoopTest] revive: this copy's player is killed a second time in this room");
                CoopTrace.Write("test-down-twice", room, slot: _session.LocalSlot);
                me.health.Kill();
            }
            return;
        }
        if (_testDowned || !Enemies(Map.Instance))
            return;
        if (_fightSince < 0)
            _fightSince = now;
        if (now - _fightSince < TestDownAfter)
            return;
        _testDowned = true;
        Plugin.Log.LogInfo("[CoopTest] revive: this copy's player is killed");
        me.health.Kill();
    }

    // ------------------------------------------------------------------ down and back

    /// <summary>This game's player died (the game's own revive effects ran before). True = they went down and the
    /// game's death must not run.</summary>
    private bool LocalDowned(Character player)
    {
        int local = _session.LocalSlot;
        if (player == null || player != LocalPlayer || !PartyState.IsSlot(local) || !Active)
            return false;
        if (_finalDeath)
        {
            // REVIEW(fable): KA scripted game over ends the run for everyone (REVIEW_QUEUE 14).
            // The game's own "you lose" (King Alexander's bomb: a scene that ends with Suicide on the player): no
            // down and no revive. This game's death runs now; the host ends the run, so every other game dies too.
            _finalDeath = false;
            _diedFinal = true;
            _runOver = true;
            CoopTrace.Write("final-death", CoopWorld.CurrentRoom(), what: "a game-over scene", slot: local);
            if (_session.IsHost)
            {
                _rules.EndRun(Now);
                Flush();
            }
            else
                Send(ReviveMessage.Final());
            return false;
        }
        CoopTrace.Write("down", CoopWorld.CurrentRoom(), slot: local);
        SetDown(local, true, false); // at once; the host's event says whether a teammate may revive
        if (_session.IsHost)
        {
            _rules.OnDowned(local, Now);
            Flush();
        }
        else
            Send(ReviveMessage.Down());
        return true;
    }

    private void SetDown(int slot, bool down, bool canRevive)
    {
        if (!PartyState.IsSlot(slot))
            return;
        _canRevive[slot] = down && canRevive;
        if (_down[slot] == down)
            return;
        _down[slot] = down;
        bool local = slot == _session.LocalSlot;
        var body = _playerOf(slot);
        if (down)
        {
            if (body != null)
            {
                SpawnHead(slot, body);
                Hide(slot, body);
            }
            if (local && body != null)
            {
                body.CancelAction();
                PlayerInput.blocked.Attach(this);
                body.invulnerable.Attach(this);
                _graceUntil = 0;
            }
            return;
        }
        Unhide(slot);
        RemoveHead(slot);
        if (local)
        {
            PlayerInput.blocked.Detach(this);
            _watching = null;
            if (body != null)
                Track(body);
        }
    }

    private void ReviveLocal(float fraction, string why)
    {
        var player = LocalPlayer;
        if (player == null || player.health == null)
            return;
        player.health.Revive(Math.Max(1.0, player.health.maximumHealth * fraction));
        _graceUntil = Now + 2.0; // not hit again in the moment they stand up
        player.invulnerable.Attach(this);
        CoopTrace.Write("back-up", CoopWorld.CurrentRoom(), what: why, amount: player.health.currentHealth, slot: _session.LocalSlot);
    }

    /// <summary>Everyone is down: the game's own death, as if no co-op ran.</summary>
    private void GameDeath()
    {
        var player = LocalPlayer;
        var handler = player != null ? player.GetComponent<HandlePlayerDeath>() : null;
        int local = _session.LocalSlot;
        RemoveHead(local);
        PlayerInput.blocked.Detach(this);
        if (player != null)
            player.invulnerable.Detach(this);
        if (handler == null)
            return;
        _passDeath = true;
        try
        {
            handler.OnPlayerDied();
        }
        finally
        {
            _passDeath = false;
        }
    }

    private void Hide(int slot, Character body)
    {
        if (_hidden[slot] != null)
            return;
        var list = new List<Renderer>();
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
            if (renderer.enabled)
            {
                renderer.enabled = false;
                list.Add(renderer);
            }
        _hidden[slot] = list;
    }

    private void Unhide(int slot)
    {
        var list = _hidden[slot];
        _hidden[slot] = null;
        if (list == null)
            return;
        foreach (var renderer in list)
            if (renderer != null)
                renderer.enabled = true;
    }

    /// <summary>The head on the floor, as in the game's own death (its sprite is the skull's icon).</summary>
    private void SpawnHead(int slot, Character body)
    {
        try
        {
            var head = CommonResource.instance.playerDieHeadParts.parts.poolObject.Spawn().GetComponent<PlayerDieHeadParts>();
            head.transform.parent = Map.Instance.transform;
            head.transform.position = body.transform.position;
            var icon = body.playerComponents?.inventory?.weapon?.polymorphOrCurrent?.icon;
            if (icon != null)
                head.sprite = icon;
            head.parts.Initialize(body.movement.push);
            _heads[slot] = head;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] revive: no head for slot " + slot + ": " + e.Message);
        }
    }

    private void RemoveHead(int slot)
    {
        var head = _heads[slot];
        _heads[slot] = null;
        try
        {
            if (head != null && head.gameObject.activeInHierarchy)
                head.parts.poolObject.Despawn();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] revive: the head of slot " + slot + " stays: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ messages

    private void Send(in ReviveMessage message)
    {
        _writer.Reset();
        ReviveMessages.Write(_writer, message);
        _session.Send(0, MessageType.Revive, _writer, Delivery.Reliable);
    }

    /// <summary>Host: what the rules decided since the last call goes to everyone and is shown here.</summary>
    private void Flush()
    {
        _events.Clear();
        _rules.TakeEvents(_events);
        foreach (var e in _events)
        {
            var message = ReviveMessage.Of(e);
            _writer.Reset();
            ReviveMessages.Write(_writer, message);
            _session.Broadcast(MessageType.Revive, _writer, Delivery.Reliable);
            Apply(message);
        }
        _events.Clear();
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (!ReviveMessages.TryRead(payload, out var m))
            return;
        if (m.Kind == ReviveMessageKind.Hold)
        {
            // only for the ring: a client's hold is shown here and passed on to the other clients
            if (_session.IsHost)
            {
                if (message.FromSlot == _session.LocalSlot)
                    return;
                m.BySlot = message.FromSlot;
                SendHold(m, message.FromSlot);
            }
            else if (message.FromSlot != 0)
                return;
            SeeHold(m);
            return;
        }
        if (_session.IsHost)
        {
            if (m.Kind == ReviveMessageKind.Down)
                _rules.OnDowned(message.FromSlot, Now);
            else if (m.Kind == ReviveMessageKind.Final)
            {
                CoopTrace.Write("final-death-of", CoopWorld.CurrentRoom(), what: "a game-over scene", slot: message.FromSlot);
                _rules.EndRun(Now);
            }
            else if (m.Kind == ReviveMessageKind.Request)
            {
                var answer = _rules.TryRevive(message.FromSlot, m.Slot, Now);
                CoopTrace.Write("revive-answer", CoopWorld.CurrentRoom(), what: answer + " for slot " + m.Slot, slot: message.FromSlot);
            }
            Flush();
        }
        else if (m.Kind == ReviveMessageKind.Event && message.FromSlot == 0)
            Apply(m);
    }

    /// <summary>Every game: the host's decision is shown.</summary>
    private void Apply(in ReviveMessage m)
    {
        int local = _session.LocalSlot;
        switch (m.Event)
        {
            case ReviveEventKind.Downed:
                SetDown(m.Slot, true, m.CanBeRevived);
                CoopTrace.Write("downed", CoopWorld.CurrentRoom(), what: m.CanBeRevived ? "can be revived" : "until the room is cleared", slot: m.Slot);
                break;
            case ReviveEventKind.Revived:
            case ReviveEventKind.ReturnedAtClear:
                bool revived = m.Event == ReviveEventKind.Revived;
                SetDown(m.Slot, false, false);
                CoopTrace.Write(revived ? "revived" : "returned", CoopWorld.CurrentRoom(), what: revived ? "by slot " + m.BySlot : "room cleared", amount: m.HealthFraction, slot: m.Slot);
                if (_playerOf(m.Slot) != null)
                {
                    _ring.Burst(m.Slot, revived, Now); // the flash and the sound, on every game
                    _flashAt = Now;
                    _flashShot = 0;
                }
                if (m.Slot == local)
                    ReviveLocal(m.HealthFraction, revived ? "revived by " + Name(m.BySlot) : "the room is cleared");
                break;
            case ReviveEventKind.RunOver:
                _runOver = true;
                CoopTrace.Write("run-over", CoopWorld.CurrentRoom(), what: _diedFinal ? "this game's game-over scene" : "everyone is down / a game-over scene");
                if (!_diedFinal) // this game's own death ran already (its game-over scene)
                    GameDeath();
                break;
        }
    }

    // ------------------------------------------------------------------ patches

    /// <summary>The game's death of the player: in co-op they go down instead.</summary>
    [HarmonyPatch(typeof(HandlePlayerDeath), "OnPlayerDied")]
    public static class DownInsteadOfDeath
    {
        private static bool Prefix(HandlePlayerDeath __instance)
        {
            var revive = Current;
            return revive == null || revive._passDeath || !revive.LocalDowned(__instance._player);
        }
    }

    /// <summary>A game-over scene kills this game's player on purpose (King Alexander's bomb: `Bomb Exploded -
    /// GameOver` ends with Suicide on the player; package KA): that death is the run's end, not a down.</summary>
    [HarmonyPatch(typeof(Characters.Operations.Health.Suicide), nameof(Characters.Operations.Health.Suicide.Run), new[] { typeof(Character) })]
    public static class GameOverScene
    {
        private static void Prefix(Characters.Operations.Health.Suicide __instance, Character owner)
        {
            var revive = Current;
            if (revive == null || !revive.Active || owner == null || owner != LocalPlayer)
                return;
            bool scene = __instance.GetComponentInParent<Runnables.ACutScene>() != null;
            CoopTrace.Write("suicide-seen", CoopWorld.CurrentRoom(), what: scene ? "in a scene" : "not in a scene", slot: revive._session.LocalSlot);
            if (scene)
                revive._finalDeath = true;
        }

        private static void Postfix()
        {
            var revive = Current;
            if (revive != null)
                revive._finalDeath = false; // taken by LocalDowned, or the kill did not lead to a death
        }
    }

    /// <summary>The game's die effect switches the dead character off; a downed player's body stays (hidden), so
    /// its gear and abilities go on when they stand up again.</summary>
    [HarmonyPatch(typeof(CharacterDieEffect), "Spawn")]
    public static class KeepTheBody
    {
        private static bool Prefix(CharacterDieEffect __instance)
        {
            var revive = Current;
            return revive == null || revive._passDeath || !revive.Active || __instance._character != LocalPlayer;
        }
    }
}
