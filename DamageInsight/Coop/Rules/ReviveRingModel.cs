using System;

namespace DamageInsight.Coop.Rules;

/// <summary>
/// The heartbeat of the revive ring (Umut 2026-11-14: "make it pulse and give it a heartbeat sound"): how fast the
/// heart over a downed player beats and how far it is swollen at each moment of a beat. Plain math, tested; the
/// drawing (Coop/ReviveRing) and the sound (Coop/ReviveSound) follow it, so what you see and hear is in step.
/// </summary>
public static class ReviveBeat
{
    /// <summary>Beats per second: a downed player waiting, a hold that begins, a hold about to finish.</summary>
    public const float IdleRate = 0.75f, HoldStartRate = 1.3f, HoldEndRate = 2.8f;

    /// <summary>The second, smaller thump ("dub") comes this long after the first ("lub"), as in the sound.</summary>
    public const float DubSeconds = 0.15f;

    private const float Attack = 0.03f, LubDecay = 0.075f, DubDecay = 0.06f, DubHeight = 0.6f;

    /// <summary>Beats per second. A hold speeds the heart up, more and more towards its end.</summary>
    public static float Rate(bool holding, float progress)
    {
        if (!holding)
            return IdleRate;
        float p = Math.Min(1f, Math.Max(0f, progress));
        return HoldStartRate + (HoldEndRate - HoldStartRate) * p * p;
    }

    /// <summary>0..1: how far the heart is swollen this long after a beat began (lub, then a smaller dub).</summary>
    public static float Pulse(float secondsIntoBeat)
    {
        if (float.IsNaN(secondsIntoBeat) || secondsIntoBeat < 0f)
            return 0f;
        return Math.Max(Bump(secondsIntoBeat, LubDecay), DubHeight * Bump(secondsIntoBeat - DubSeconds, DubDecay));
    }

    private static float Bump(float t, float decay) =>
        t < 0f ? 0f : t < Attack ? t / Attack : (float)Math.Exp(-(t - Attack) / decay);
}

/// <summary>
/// What the ring over ONE downed player shows: the hold's progress and the heartbeat. The reviver's own game reports
/// its hold every frame; every other game gets about ten small messages a second and fills the gaps itself (the hold
/// time is known, so the ring moves on evenly between two messages). Messages that stop coming end the hold here
/// too (an unreliable "it broke" may be lost). A broken hold drains the ring instead of blanking it.
/// </summary>
public sealed class ReviveRingModel
{
    /// <summary>A teammate's hold with no message for this long counts as broken.</summary>
    public const double ReportTimeout = 0.6;

    /// <summary>A broken hold's ring runs empty in this time (a full ring).</summary>
    public const double DrainSeconds = 0.3;

    /// <summary>Between two messages the ring catches up at most this many times the hold's own speed.</summary>
    private const double CatchUp = 4.0;

    private double _last = -1, _reportAt, _beatAt = double.NegativeInfinity, _phase;
    private float _reported, _holdSeconds = 1f;
    private int _hold = -1, _endedBy = -1, _endedHold = -1;

    /// <summary>Someone is holding Interact on this player right now.</summary>
    public bool Holding { get; private set; }

    /// <summary>Who holds (their slot); -1 when nobody does.</summary>
    public int By { get; private set; } = -1;

    /// <summary>0..1, what the ring shows.</summary>
    public float Progress { get; private set; }

    /// <summary>0..1, the heart's swelling right now (<see cref="ReviveBeat.Pulse"/>).</summary>
    public float Pulse { get; private set; }

    /// <summary>Beats begun so far.</summary>
    public int Beats { get; private set; }

    /// <summary>A hold on this player is this far (the reviver's game: every frame; others: per message).
    /// <paramref name="hold"/>: the reviver's number of this hold, so a late message of a hold that has ended
    /// (messages may overtake each other) does not start it again.</summary>
    public void Report(int by, int hold, float progress, float holdSeconds, double now)
    {
        if (by == _endedBy && hold == _endedHold)
            return;
        progress = float.IsNaN(progress) ? 0f : Math.Min(1f, Math.Max(0f, progress));
        // Two revivers on one player (3-4 players): the ring follows the one who is further.
        if (Holding && by != By && now - _reportAt <= ReportTimeout && progress < Target(now))
            return;
        if (!Holding)
            _phase = 1.0; // the first beat comes with the press
        Holding = true;
        By = by;
        _hold = hold;
        _reported = progress;
        _reportAt = now;
        _holdSeconds = float.IsNaN(holdSeconds) ? 1f : Math.Min(60f, Math.Max(0.1f, holdSeconds));
    }

    /// <summary>The reviver says this hold of theirs broke (let go, moved away, ...).</summary>
    public void Stop(int by, int hold)
    {
        _endedBy = by;
        _endedHold = hold;
        if (!Holding || by != By || hold != _hold)
            return;
        Holding = false;
        By = -1;
    }

    /// <summary>The player is up again or gone: nothing of a hold is left.</summary>
    public void Reset()
    {
        Holding = false;
        By = -1;
        _hold = _endedBy = _endedHold = -1;
        Progress = 0f;
        Pulse = 0f;
        _phase = 0;
        _beatAt = double.NegativeInfinity;
        _last = -1;
    }

    /// <summary>Every frame. <paramref name="beating"/>: the heart beats (false: they stay down until the room is
    /// cleared, a still heart). True when a beat begins in this frame.</summary>
    public bool Tick(double now, bool beating)
    {
        double dt = _last < 0 ? 0 : Math.Min(0.25, Math.Max(0.0, now - _last));
        _last = now;
        if (Holding && now - _reportAt > ReportTimeout)
        {
            Holding = false;
            By = -1;
        }
        if (Holding)
        {
            float target = Target(now);
            if (target >= Progress)
                Progress = (float)Math.Min(target, Progress + dt * CatchUp / _holdSeconds);
            else if (target < Progress - 0.2f)
                Progress = (float)Math.Max(target, Progress - dt / DrainSeconds); // a new hold after a broken one
        }
        else
            Progress = (float)Math.Max(0.0, Progress - dt / DrainSeconds);

        if (!beating)
        {
            _phase = 0;
            Pulse = 0f;
            _beatAt = double.NegativeInfinity;
            return false;
        }
        _phase += ReviveBeat.Rate(Holding, Progress) * dt;
        bool beat = false;
        if (_phase >= 1.0)
        {
            _phase -= Math.Floor(_phase);
            _beatAt = now;
            Beats++;
            beat = true;
        }
        Pulse = ReviveBeat.Pulse((float)(now - _beatAt));
        return beat;
    }

    private float Target(double now) =>
        (float)Math.Min(1.0, _reported + Math.Max(0.0, now - _reportAt) / _holdSeconds);
}
