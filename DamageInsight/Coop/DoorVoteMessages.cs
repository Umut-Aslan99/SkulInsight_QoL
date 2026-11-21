using System;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;

namespace DamageInsight.Coop;

/// <summary>What a door vote message says (COOP_PLAN §4.3; MessageType.DoorVote, reliable).</summary>
public enum DoorVoteMessageKind : byte
{
    /// <summary>A player to the host: "I pressed this door of this room" (their vote; a later press changes it).</summary>
    Vote = 1,
    /// <summary>The host to everyone: the room's vote as the host's rules have it (after every change, and the result).</summary>
    State = 2,
    /// <summary>A player to the host: "my doors in this room are open" (the game opens a player's doors once their
    /// room reward is taken, or at once when the room has none): this player no longer holds up the vote.</summary>
    Ready = 3,
}

/// <summary>
/// The door vote on the wire (plain logic, tested). The host owns the rule (Rules.DoorVote); players only report
/// their press, and every game shows what the host's state says.
/// [kind u8][room u32] + Vote [door u8] | Ready (nothing)
/// | State [vote i8 x 4, by slot][seconds left u16, tenths of a second; 0xFFFF = no countdown][flags u8: 1 resolved,
///   2 coin flip, 4 timed out][door i8][chance u8, percent][ready mask u8, bit per slot][reward wait left u16, tenths;
///   0xFFFF = no wait].
/// </summary>
public struct DoorVoteMessage
{
    public DoorVoteMessageKind Kind;
    /// <summary>The room the vote is about (WorldMessages.RoomId): a press that arrives after the room changed is
    /// not a vote on the next one.</summary>
    public uint Room;
    /// <summary>Vote: the door pressed (the game's node index, 0 or 1). State: the door the party takes once
    /// resolved, -1 before.</summary>
    public int Door;
    /// <summary>State: each slot's vote, -1 = none (down, gone, or not yet).</summary>
    public int[] Votes;
    /// <summary>State: what the others have left after the first vote; below 0 = no countdown runs.</summary>
    public float SecondsLeft;
    /// <summary>State: the vote is over, <see cref="Door"/> is the way.</summary>
    public bool Resolved;
    /// <summary>State, resolved: the votes differed and the weighted flip picked the door.</summary>
    public bool CoinFlip;
    /// <summary>State, resolved: someone did not vote in time.</summary>
    public bool TimedOut;
    /// <summary>State, resolved: the chance the chosen door had (1 = everyone agreed).</summary>
    public float Chance;
    /// <summary>State: who no longer holds up the vote (their reward is taken, they had none, or they are down or
    /// gone), a bit per slot.</summary>
    public int ReadyMask;
    /// <summary>State: the wait for everyone's room reward still runs this long; below 0 = it does not (the votes
    /// count). Votes made during the wait are kept and count when it ends.</summary>
    public float GateSeconds;

    public const int AllReady = (1 << PartyState.MaxPlayers) - 1;

    public int VoteOf(int slot) => Votes != null && slot >= 0 && slot < Votes.Length ? Votes[slot] : -1;

    public bool IsReady(int slot) => slot >= 0 && slot < PartyState.MaxPlayers && ((ReadyMask >> slot) & 1) != 0;

    public static DoorVoteMessage Done(uint room) =>
        new() { Kind = DoorVoteMessageKind.Ready, Room = room, Door = -1, SecondsLeft = -1f, GateSeconds = -1f };

    public static DoorVoteMessage Press(uint room, int door) =>
        new() { Kind = DoorVoteMessageKind.Vote, Room = room, Door = door, SecondsLeft = -1f, GateSeconds = -1f };

    /// <summary>The room's vote as it stands on the host. <paramref name="early"/>: presses made while the reward
    /// wait still runs (by slot, -1 = none; the rule takes them when it ends), shown like votes.</summary>
    public static DoorVoteMessage State(uint room, DoorVote vote, double now, int[] early = null,
        int readyMask = AllReady, float gateSeconds = -1f)
    {
        var result = vote.Result;
        var m = new DoorVoteMessage
        {
            Kind = DoorVoteMessageKind.State, Room = room, Door = -1, Votes = new int[PartyState.MaxPlayers],
            SecondsLeft = (float)vote.SecondsLeft(now), ReadyMask = readyMask & AllReady,
            GateSeconds = result != null ? -1f : gateSeconds,
        };
        for (int slot = 0; slot < m.Votes.Length; slot++)
        {
            m.Votes[slot] = result != null ? result.VoteOf(slot) : vote.VoteOf(slot);
            if (m.Votes[slot] < 0 && result == null && early != null && slot < early.Length)
                m.Votes[slot] = early[slot];
        }
        if (result != null)
        {
            m.Resolved = true;
            m.Door = result.Door;
            m.CoinFlip = result.CoinFlip;
            m.TimedOut = result.TimedOut;
            m.Chance = (float)result.ChanceOf(result.Door);
            m.SecondsLeft = -1f;
        }
        return m;
    }
}

public static class DoorVoteMessages
{
    private const ushort NoCountdown = 0xFFFF;
    private const byte ResolvedFlag = 1, CoinFlipFlag = 2, TimedOutFlag = 4;

    public static void Write(NetWriter w, in DoorVoteMessage m)
    {
        w.WriteByte((byte)m.Kind);
        w.WriteUInt32(m.Room);
        if (m.Kind == DoorVoteMessageKind.Vote)
        {
            w.WriteByte((byte)Math.Max(0, Math.Min(255, m.Door)));
            return;
        }
        if (m.Kind == DoorVoteMessageKind.Ready)
            return;
        for (int slot = 0; slot < PartyState.MaxPlayers; slot++)
            w.WriteSByte((sbyte)Math.Max(-1, Math.Min(DoorVote.MaxDoors - 1, m.VoteOf(slot))));
        w.WriteUInt16(Tenths(m.SecondsLeft));
        w.WriteByte((byte)((m.Resolved ? ResolvedFlag : 0) | (m.CoinFlip ? CoinFlipFlag : 0) | (m.TimedOut ? TimedOutFlag : 0)));
        w.WriteSByte((sbyte)Math.Max(-1, Math.Min(DoorVote.MaxDoors - 1, m.Door)));
        float chance = float.IsNaN(m.Chance) ? 0f : Math.Max(0f, Math.Min(1f, m.Chance));
        w.WriteByte((byte)Math.Round(chance * 100f));
        w.WriteByte((byte)(m.ReadyMask & DoorVoteMessage.AllReady));
        w.WriteUInt16(Tenths(m.GateSeconds));
    }

    private static ushort Tenths(float seconds) => seconds < 0f || float.IsNaN(seconds)
        ? NoCountdown
        : (ushort)Math.Min(NoCountdown - 1, (int)Math.Round(seconds * 10f));

    /// <summary>False: not a door vote message (too short, an unknown kind, a door or slot that cannot be).</summary>
    public static bool TryRead(NetReader r, out DoorVoteMessage m)
    {
        m = default;
        if (r.Remaining < 5)
            return false;
        m.Kind = (DoorVoteMessageKind)r.ReadByte();
        m.Room = r.ReadUInt32();
        m.SecondsLeft = m.GateSeconds = -1f;
        m.Door = -1;
        if (m.Kind == DoorVoteMessageKind.Ready)
            return true;
        if (m.Kind == DoorVoteMessageKind.Vote)
        {
            if (r.Remaining < 1)
                return false;
            m.Door = r.ReadByte();
            return m.Door < DoorVote.MaxDoors;
        }
        if (m.Kind != DoorVoteMessageKind.State || r.Remaining < PartyState.MaxPlayers + 8)
            return false;
        m.Votes = new int[PartyState.MaxPlayers];
        for (int slot = 0; slot < m.Votes.Length; slot++)
        {
            m.Votes[slot] = r.ReadSByte();
            if (m.Votes[slot] < -1 || m.Votes[slot] >= DoorVote.MaxDoors)
                return false;
        }
        ushort tenths = r.ReadUInt16();
        m.SecondsLeft = tenths == NoCountdown ? -1f : tenths / 10f;
        byte flags = r.ReadByte();
        m.Resolved = (flags & ResolvedFlag) != 0;
        m.CoinFlip = (flags & CoinFlipFlag) != 0;
        m.TimedOut = (flags & TimedOutFlag) != 0;
        m.Door = r.ReadSByte();
        m.Chance = r.ReadByte() / 100f;
        m.ReadyMask = r.ReadByte() & DoorVoteMessage.AllReady;
        ushort gate = r.ReadUInt16();
        m.GateSeconds = gate == NoCountdown ? -1f : gate / 10f;
        if (m.Door < -1 || m.Door >= DoorVote.MaxDoors)
            return false;
        return m.Resolved == (m.Door >= 0); // a result names its door, an open vote names none
    }
}
