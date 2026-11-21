using System;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// The time buffer for one remote entity (an enemy, a teammate): the states that arrived, each with the time it was
/// true, and the state to draw at a render time (docs/COOP_PLAN.md §5.3). The render time lies a short moment in the
/// past (<see cref="AdaptiveDelay"/>), so there is usually a known state on both sides of it and the entity moves
/// smoothly between them, however unevenly the packets arrive. Past the newest state it moves on with its last
/// velocity for at most <see cref="MaxExtrapolation"/>, then waits. When real data replaces such a guess, the
/// difference is blended away (<see cref="Correction"/>); a teleport (a jump bigger than
/// <see cref="TeleportDistance"/>) snaps. States may arrive out of order; duplicates and states too old to matter
/// are ignored. Fixed capacity, no allocations after construction.
/// </summary>
public sealed class SnapshotBuffer<T> where T : struct, ISnapshot<T>
{
    /// <summary>Seconds an entity moves on past its newest state before it waits (COOP_PLAN §5.3: 0.25 s).</summary>
    public float MaxExtrapolation = 0.25f;

    /// <summary>Two neighbouring states further apart than this (world units) are a teleport: no sliding between
    /// them. 5 units: far more than any walk or dash covers in one or two updates.</summary>
    public float TeleportDistance = 5f;

    /// <summary>Blends the jump when real data replaces an extrapolated guess. Its SnapDistance decides when that
    /// jump is snapped instead.</summary>
    public readonly SmoothCorrection Correction = new();

    private readonly double[] _times;
    private readonly T[] _states;
    private int _count;

    // Everything at or before this time is useless: the state there is already the one being drawn from.
    private double _horizon = double.NegativeInfinity;
    private double _lastRender = double.NaN;

    // The state the last sample extrapolated from (kept even when the buffer drops it), to measure the jump.
    private bool _guessing;
    private double _guessTime;
    private T _guessBase;

    public SnapshotBuffer(int capacity = 32)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _times = new double[capacity];
        _states = new T[capacity];
    }

    public int Count => _count;
    public int Capacity => _times.Length;

    /// <summary>The time of the newest state (negative infinity when empty).</summary>
    public double NewestTime => _count == 0 ? double.NegativeInfinity : _times[_count - 1];

    /// <summary>The last <see cref="Sample"/> snapped (a teleport, or a correction too big to blend): skip trails
    /// and similar effects that would draw a line across the room.</summary>
    public bool Snapped { get; private set; }

    /// <summary>Stores a state that was true at <paramref name="time"/> (on the same timeline as the render time).
    /// Returns false when it was ignored: a duplicate, or older than the state already being drawn from.</summary>
    public bool Add(double time, in T state)
    {
        if (time <= _horizon || double.IsNaN(time))
            return false;
        // Most states arrive in order: search from the newest.
        int at = _count;
        while (at > 0 && _times[at - 1] > time)
            at--;
        if ((at > 0 && time - _times[at - 1] < 1e-6) || (at < _count && _times[at] - time < 1e-6))
            return false;
        if (_count == _times.Length)
        {
            if (at == 0)
                return false; // full and older than everything kept
            RemoveFront(1);
            at--;
        }
        if (at < _count)
        {
            Array.Copy(_times, at, _times, at + 1, _count - at);
            Array.Copy(_states, at, _states, at + 1, _count - at);
        }
        _times[at] = time;
        _states[at] = state;
        _count++;
        return true;
    }

    /// <summary>The state to draw at <paramref name="renderTime"/> (call once per frame with a rising render
    /// time). Drops the states that are no longer needed.</summary>
    public SampleKind Sample(double renderTime, out T state)
    {
        Snapped = false;
        float dt = double.IsNaN(_lastRender) ? 0f : (float)Math.Max(0, renderTime - _lastRender);
        _lastRender = renderTime;
        Correction.Step(dt);

        if (_count == 0)
        {
            state = default;
            _guessing = false;
            return SampleKind.Empty;
        }
        if (renderTime <= _times[0])
        {
            state = Corrected(_states[0]);
            return SampleKind.Waiting;
        }

        // The newest state at or before the render time becomes the first; the ones before it go.
        int from = 0;
        while (from + 1 < _count && _times[from + 1] <= renderTime)
            from++;
        if (from > 0)
        {
            for (int i = 0; i < from; i++)
                if (IsTeleport(i))
                    Snapped = true;
            RemoveFront(from);
        }
        _horizon = _times[0];

        T raw;
        SampleKind kind;
        double ahead = renderTime - _times[0];
        if (_count >= 2)
        {
            kind = SampleKind.Interpolated;
            float span = (float)(_times[1] - _times[0]);
            raw = IsTeleport(0)
                ? _states[0].Extrapolate((float)Math.Min(ahead, MaxExtrapolation)) // jumps when the next one is reached
                : _states[0].Lerp(in _states[1], (float)(ahead / span), span);
        }
        else
        {
            kind = ahead <= MaxExtrapolation ? SampleKind.Extrapolated : SampleKind.Held;
            raw = _states[0].Extrapolate((float)Math.Min(ahead, MaxExtrapolation));
        }

        // New data replaced a guess: what was drawn and what is true differ. Blend that (or snap if too far).
        if (!Snapped && _guessing && (kind == SampleKind.Interpolated || _times[0] != _guessTime))
        {
            var guess = _guessBase.Extrapolate((float)Math.Min(Math.Max(0, renderTime - _guessTime), MaxExtrapolation));
            Snapped = Correction.Add(guess.Position - raw.Position);
        }
        if (Snapped)
            Correction.Reset();

        _guessing = kind is SampleKind.Extrapolated or SampleKind.Held;
        if (_guessing)
        {
            _guessTime = _times[0];
            _guessBase = _states[0];
        }
        state = Corrected(raw);
        return kind;
    }

    /// <summary>Forgets everything (a room change, the entity left).</summary>
    public void Clear()
    {
        _count = 0;
        _horizon = double.NegativeInfinity;
        _lastRender = double.NaN;
        _guessing = false;
        Correction.Reset();
    }

    private T Corrected(in T raw) => Correction.IsActive ? raw.Moved(Correction.Offset) : raw;

    private bool IsTeleport(int i)
    {
        var d = _states[i + 1].Position - _states[i].Position;
        return d.LengthSquared > TeleportDistance * TeleportDistance;
    }

    private void RemoveFront(int n)
    {
        _count -= n;
        Array.Copy(_times, n, _times, 0, _count);
        Array.Copy(_states, n, _states, 0, _count);
        Array.Clear(_states, _count, n);
    }
}
