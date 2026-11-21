using System;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;

namespace DamageInsight.Coop;

/// <summary>What a revive message says (COOP_PLAN §4.4; MessageType.Revive, reliable).</summary>
public enum ReviveMessageKind : byte
{
    /// <summary>A player to the host: "I went down" (the game's own revive effects did not save me).</summary>
    Down = 1,
    /// <summary>A player to the host: "my hold next to <see cref="ReviveMessage.Slot"/>'s head is complete".</summary>
    Request = 2,
    /// <summary>The host to everyone: what the rules decided (<see cref="ReviveMessage.Event"/>).</summary>
    Event = 3,
    /// <summary>A reviver to the host, the host on to everyone else: "my hold on <see cref="ReviveMessage.Slot"/> is
    /// this far" (about ten a second, unreliable; the last one says it ended). Only for the ring every game draws
    /// over the downed player; the rules never read it.</summary>
    Hold = 4,
    /// <summary>A player to the host: "the game killed me on purpose" (a game-over scene, e.g. King Alexander's bomb):
    /// the run is over for everyone (package KA).</summary>
    Final = 5,
}

/// <summary>
/// Going down and revive on the wire (plain logic, tested). The host owns the rules (Rules.ReviveRules); players only
/// report "I went down" and "my hold is complete", and everyone shows what the host's events say.
/// [kind u8] + Request [slot i8] | Event [slot i8][event u8][by i8][health f32][can be revived bool]
/// | Hold [slot i8][by i8][hold number u8][progress u8: 0..250, 255 = ended][hold time u8, tenths of a second].
/// </summary>
public struct ReviveMessage
{
    private const int ProgressSteps = 250, HoldEnded = 255;

    public ReviveMessageKind Kind;
    /// <summary>Event only.</summary>
    public ReviveEventKind Event;
    /// <summary>Request, Hold: the downed player. Event: who it is about (-1 = RunOver).</summary>
    public int Slot;
    /// <summary>Event Revived, Hold: the reviver; else -1.</summary>
    public int BySlot;
    /// <summary>Event Revived / ReturnedAtClear: the health they come back with, a share of max health.</summary>
    public float HealthFraction;
    /// <summary>Event Downed: a teammate may still revive them in this room.</summary>
    public bool CanBeRevived;
    /// <summary>Hold: 0..1; below 0 = the hold ended (let go, moved away, ...).</summary>
    public float Progress;
    /// <summary>Hold: the whole hold's length in seconds (the host's setting), so a game can move its ring on
    /// between two messages.</summary>
    public float HoldSeconds;
    /// <summary>Hold: the reviver's count of their holds (wraps at 256): tells a late message of an ended hold
    /// from the next hold's first.</summary>
    public int HoldId;

    public bool HoldEndedEarly => Kind == ReviveMessageKind.Hold && Progress < 0f;

    public static ReviveMessage Down() => new() { Kind = ReviveMessageKind.Down, Slot = -1, BySlot = -1 };

    public static ReviveMessage Final() => new() { Kind = ReviveMessageKind.Final, Slot = -1, BySlot = -1 };

    public static ReviveMessage Request(int target) => new() { Kind = ReviveMessageKind.Request, Slot = target, BySlot = -1 };

    /// <param name="progress">0..1, or below 0: the hold ended.</param>
    public static ReviveMessage Hold(int target, int by, int hold, float progress, float holdSeconds) => new()
    {
        Kind = ReviveMessageKind.Hold, Slot = target, BySlot = by, HoldId = hold & 0xFF, Progress = progress,
        HoldSeconds = holdSeconds,
    };

    internal static byte PackProgress(float progress) =>
        progress < 0f || float.IsNaN(progress) ? (byte)HoldEnded
        : (byte)Math.Min(ProgressSteps, (int)Math.Round(Math.Min(1f, progress) * ProgressSteps));

    internal static float UnpackProgress(byte packed) => packed > ProgressSteps ? -1f : packed / (float)ProgressSteps;

    internal static byte PackSeconds(float seconds) =>
        float.IsNaN(seconds) ? (byte)1 : (byte)Math.Min(255, Math.Max(1, (int)Math.Round(seconds * 10f)));

    public static ReviveMessage Of(in ReviveEvent e) => new()
    {
        Kind = ReviveMessageKind.Event, Event = e.Kind, Slot = e.Slot, BySlot = e.BySlot,
        HealthFraction = e.HealthFraction, CanBeRevived = e.CanBeRevived,
    };
}

public static class ReviveMessages
{
    public static void Write(NetWriter w, in ReviveMessage m)
    {
        w.WriteByte((byte)m.Kind);
        if (m.Kind is ReviveMessageKind.Down or ReviveMessageKind.Final)
            return;
        w.WriteSByte((sbyte)m.Slot);
        if (m.Kind == ReviveMessageKind.Hold)
        {
            w.WriteSByte((sbyte)m.BySlot);
            w.WriteByte((byte)m.HoldId);
            w.WriteByte(ReviveMessage.PackProgress(m.Progress));
            w.WriteByte(ReviveMessage.PackSeconds(m.HoldSeconds));
            return;
        }
        if (m.Kind != ReviveMessageKind.Event)
            return;
        w.WriteByte((byte)m.Event);
        w.WriteSByte((sbyte)m.BySlot);
        w.WriteFloat(m.HealthFraction);
        w.WriteBool(m.CanBeRevived);
    }

    /// <summary>False for a kind this version doesn't know (the message is ignored).</summary>
    public static bool TryRead(NetReader r, out ReviveMessage m)
    {
        m = new ReviveMessage { Kind = (ReviveMessageKind)r.ReadByte(), Slot = -1, BySlot = -1 };
        switch (m.Kind)
        {
            case ReviveMessageKind.Down:
            case ReviveMessageKind.Final:
                return true;
            case ReviveMessageKind.Request:
                m.Slot = r.ReadSByte();
                return true;
            case ReviveMessageKind.Event:
                m.Slot = r.ReadSByte();
                m.Event = (ReviveEventKind)r.ReadByte();
                m.BySlot = r.ReadSByte();
                m.HealthFraction = r.ReadFloat();
                m.CanBeRevived = r.ReadBool();
                return m.Event <= ReviveEventKind.RunOver;
            case ReviveMessageKind.Hold:
                m.Slot = r.ReadSByte();
                m.BySlot = r.ReadSByte();
                m.HoldId = r.ReadByte();
                m.Progress = ReviveMessage.UnpackProgress(r.ReadByte());
                m.HoldSeconds = r.ReadByte() / 10f;
                return true;
            default:
                return false;
        }
    }
}
