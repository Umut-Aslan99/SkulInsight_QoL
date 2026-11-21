using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;
using DamageInsight.Recording;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Every player's damage for the co-op part of the combat log (Umut 2026-11-21: bars per person, rings, DPS). This game
/// sums its own damage from DamageLog (a teammate's hits never go there: Patches/DamageRecordPatch) and sends a
/// <see cref="DamageShare"/> every <see cref="SendSeconds"/> while it changes; the others' come in the same way. The
/// run total starts with the session (or the run, whichever is later); the room total with each room.
/// </summary>
internal sealed class CoopDamageBoard
{
    public const double SendSeconds = 2.0;

    public static CoopDamageBoard Current { get; private set; }

    /// <summary>One player's numbers as last heard (this game's player too, slot = its own).</summary>
    public sealed class Player
    {
        public int Slot;
        public DamageShare Share = new();
        public double Heard;
    }

    private readonly CoopSession _session;
    private readonly NetWriter _writer = new(256);
    private readonly Dictionary<DamageSource, double> _bySource = new();
    private readonly List<(float Time, double Amount)> _hits = new();
    private readonly Dictionary<int, Player> _players = new();
    private double _run, _room;
    private int _roomIndex = -1;
    private bool _changed;
    private double _nextSend;

    public CoopDamageBoard(CoopSession session)
    {
        _session = session;
        Current = this;
        session.RegisterHandler(MessageType.DamageShare, OnMessage);
        session.SetRelay(MessageType.DamageShare);
        DamageLog.Recorded += OnRecorded;
    }

    public void Dispose()
    {
        DamageLog.Recorded -= OnRecorded;
        if (Current == this)
            Current = null;
    }

    /// <summary>Everyone's numbers, this game's player first, then by slot.</summary>
    public IEnumerable<Player> Players()
    {
        var own = Own();
        yield return own;
        foreach (var p in _players.Values.Where(p => p.Slot != own.Slot).OrderBy(p => p.Slot))
            if (_session.PlayerAt(p.Slot) != null)
                yield return p;
    }

    /// <summary>A player's name as the session knows it.</summary>
    public string NameOf(int slot) => _session.PlayerAt(slot)?.Name ?? "Player " + (slot + 1);

    public bool HasTeammates => _players.Keys.Any(slot => slot != _session.LocalSlot && _session.PlayerAt(slot) != null);

    private Player Own()
    {
        int slot = _session.LocalSlot;
        if (!_players.TryGetValue(slot, out var own))
            _players[slot] = own = new Player { Slot = slot };
        own.Share = Snapshot();
        return own;
    }

    private void OnRecorded(DamageRecord record)
    {
        if (record.ToPlayer || record.Amount <= 0)
            return;
        if (record.Room != _roomIndex)
        {
            _roomIndex = record.Room;
            _room = 0;
        }
        _run += record.Amount;
        _room += record.Amount;
        _bySource.TryGetValue(record.Source, out double had);
        _bySource[record.Source] = had + record.Amount;
        _hits.Add((Time.unscaledTime, record.Amount));
        if (_hits.Count > 4000)
            _hits.RemoveRange(0, 2000);
        _changed = true;
    }

    private DamageShare Snapshot() => new()
    {
        RunTotal = _run,
        RoomTotal = _room,
        Room = Math.Max(0, _roomIndex),
        Dps = DamageShare.DpsOf(_hits, Time.unscaledTime),
        Sources = _bySource.OrderByDescending(p => p.Value).Take(DamageShare.MaxSources)
            .Select(p => ((byte)p.Key, p.Value)).ToList(),
    };

    /// <summary>Once a frame while the session runs (CoopRuntime).</summary>
    public void Tick(double now)
    {
        // also while nothing changes, as long as the DPS falls back to 0 (the others' bars must not stay up)
        bool fading = _hits.Count > 0 && Time.unscaledTime - _hits[_hits.Count - 1].Time < 10f;
        if (now < _nextSend || (!_changed && !fading))
            return;
        _nextSend = now + SendSeconds;
        _changed = false;
        _writer.Reset();
        Snapshot().Write(_writer);
        _session.Broadcast(MessageType.DamageShare, _writer, Delivery.Unreliable);
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        var share = DamageShare.Read(payload);
        if (!_players.TryGetValue(message.FromSlot, out var player))
            _players[message.FromSlot] = player = new Player { Slot = message.FromSlot };
        player.Share = share;
        player.Heard = Time.unscaledTime;
    }
}
