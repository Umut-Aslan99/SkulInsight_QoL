using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop.Rules;

/// <summary>What a player asks of the host's enemy health (the Co-op window's arrows).</summary>
public enum HealthRequest : sbyte
{
    Less = -1,
    None = 0,
    More = 1,
}

/// <summary>
/// One player's note for the Co-op window (MessageType.PlayerNote, every game → everyone through the host's relay):
/// how far this player got in the Dark Mirror, and what they say about the host's enemy health: a request for more
/// or less, or OK. Umut 2026-11-23: "the player should be able to see the hosts HP setting in the lobby menu. There
/// should also be an "up" arrow and a "down" arrow, where the players can "request" higher difficulty HP wise and a
/// "checkbox" to say they are ready or OK with this setting. Host should always be marked and the person with the
/// least amount of progress (Dark mirror unlocked)".
/// [cleared level s8][request s8][ok bool][the host's value it is about: hundredths u16]
/// </summary>
public struct PlayerNote
{
    /// <summary>Not known (the game's save could not be read).</summary>
    public const int UnknownProgress = -2;

    /// <summary>The highest Dark Mirror level this player cleared, 0-10; -1: none yet.</summary>
    public int ClearedLevel;
    public HealthRequest Request;
    public bool Ok;
    /// <summary>The host's enemy health (the two-player value, in hundredths: 200 = 2x) the request and OK are about.
    /// A note about another value says nothing (it was sent before the host changed it).</summary>
    public int VotedOn;

    public void Write(NetWriter w)
    {
        w.WriteSByte((sbyte)Math.Max(UnknownProgress, Math.Min(10, ClearedLevel)));
        w.WriteSByte((sbyte)Request);
        w.WriteBool(Ok);
        w.WriteUInt16((ushort)Math.Max(0, Math.Min(ushort.MaxValue, VotedOn)));
    }

    /// <summary>Reads a note; false for a broken one. Values out of range are pulled in.</summary>
    public static bool TryRead(NetReader r, out PlayerNote note)
    {
        note = default;
        try
        {
            note.ClearedLevel = Math.Max(UnknownProgress, Math.Min(10, (int)r.ReadSByte()));
            note.Request = (HealthRequest)Math.Max(-1, Math.Min(1, (int)r.ReadSByte()));
            note.Ok = r.ReadByte() != 0;
            note.VotedOn = r.ReadUInt16();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Every player's note as this game knows it, and this game's own (pure logic for <see cref="CoopPlayerNotes"/>).
/// A request and an OK belong to one value of the host's enemy health: when the host changes it, this game's request
/// and OK are cleared (the host answered, or the value isn't the one that was OK) and the others' old notes no longer
/// count. A request and an OK exclude each other.
/// </summary>
public sealed class PartyNotes
{
    private readonly Dictionary<int, PlayerNote> _others = new();
    private PlayerNote _own = new() { ClearedLevel = PlayerNote.UnknownProgress };

    public PlayerNote Own => _own;

    /// <summary>The host's enemy health now (hundredths of the two-player value).</summary>
    public int HostValue { get; private set; }

    /// <summary>The host's value now. A changed one clears this game's request and OK. True: this game's note changed.</summary>
    public bool SetHostValue(int hundredths)
    {
        HostValue = hundredths;
        if (_own.VotedOn == hundredths)
            return false;
        _own.VotedOn = hundredths;
        _own.Request = HealthRequest.None;
        _own.Ok = false;
        return true;
    }

    /// <summary>This game's Dark Mirror progress (the save may change it during a session). True when it changed.</summary>
    public bool SetProgress(int clearedLevel)
    {
        if (_own.ClearedLevel == clearedLevel)
            return false;
        _own.ClearedLevel = clearedLevel;
        return true;
    }

    /// <summary>An arrow: asks for more or less; the same arrow again takes the request back. Clears an OK.</summary>
    public void Ask(HealthRequest request)
    {
        _own.Request = _own.Request == request ? HealthRequest.None : request;
        if (_own.Request != HealthRequest.None)
            _own.Ok = false;
        _own.VotedOn = HostValue;
    }

    /// <summary>The OK box: on or off. On clears a request.</summary>
    public void ToggleOk()
    {
        _own.Ok = !_own.Ok;
        if (_own.Ok)
            _own.Request = HealthRequest.None;
        _own.VotedOn = HostValue;
    }

    public void Heard(int slot, PlayerNote note) => _others[slot] = note;

    public void Forget(int slot) => _others.Remove(slot);

    /// <summary>A player's note (this game's own for <paramref name="localSlot"/>), or null when none came yet.</summary>
    public PlayerNote? NoteOf(int slot, int localSlot)
    {
        if (slot == localSlot)
            return _own;
        return _others.TryGetValue(slot, out var note) ? note : null;
    }

    /// <summary>A player's request about the host's value as it is now (an old one counts as none).</summary>
    public HealthRequest RequestOf(int slot, int localSlot) =>
        NoteOf(slot, localSlot) is { } note && note.VotedOn == HostValue ? note.Request : HealthRequest.None;

    /// <summary>The player said the host's value as it is now suits them.</summary>
    public bool IsOk(int slot, int localSlot) =>
        NoteOf(slot, localSlot) is { } note && note.VotedOn == HostValue && note.Ok;

    /// <summary>The players with the least Dark Mirror progress among <paramref name="slots"/> (the ones whose
    /// progress is known). Empty when fewer than two are known or everyone got as far.</summary>
    public List<int> LeastProgress(IEnumerable<int> slots, int localSlot)
    {
        var known = new List<(int Slot, int Cleared)>();
        foreach (int slot in slots)
            if (NoteOf(slot, localSlot) is { } note && note.ClearedLevel != PlayerNote.UnknownProgress)
                known.Add((slot, note.ClearedLevel));
        if (known.Count < 2)
            return new List<int>();
        int least = known.Min(k => k.Cleared), most = known.Max(k => k.Cleared);
        return least == most ? new List<int>() : known.Where(k => k.Cleared == least).Select(k => k.Slot).ToList();
    }
}
