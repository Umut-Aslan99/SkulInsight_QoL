using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// Events of one remote game, stamped with the sender's time, played on the same render timeline as its states
/// (docs/COOP_PLAN.md §5.3): an enemy's attack starts on this screen exactly when the drawn enemy reaches the moment
/// it started on the host. <see cref="TryTakeDue"/> hands out the events whose time the render time has reached, in
/// time order (equal times in arrival order). An event that came too late (the render time was already past it by
/// more than <see cref="LateTolerance"/>) is marked late, with how late, so the caller can fast-forward its animation.
/// </summary>
public sealed class TimedEvents<T>
{
    /// <summary>Seconds past its time an event may be handed out and still count as on time (about 3 frames).</summary>
    public float LateTolerance = 0.05f;

    private double[] _times;
    private T[] _items;
    private int _head, _count;

    public TimedEvents(int capacity = 16)
    {
        _times = new double[Math.Max(4, capacity)];
        _items = new T[_times.Length];
    }

    public int Count => _count;

    /// <summary>Queues an event that happened at <paramref name="time"/> (sender timeline).</summary>
    public void Add(double time, T item)
    {
        if (_head + _count == _times.Length)
            MakeRoom();
        int end = _head + _count;
        int at = end;
        while (at > _head && _times[at - 1] > time) // equal times keep arrival order
            at--;
        if (at < end)
        {
            Array.Copy(_times, at, _times, at + 1, end - at);
            Array.Copy(_items, at, _items, at + 1, end - at);
        }
        _times[at] = time;
        _items[at] = item;
        _count++;
    }

    /// <summary>The next event whose time <paramref name="renderTime"/> has reached, if any (call in a loop).</summary>
    public bool TryTakeDue(double renderTime, out DueEvent<T> due)
    {
        if (_count == 0 || _times[_head] > renderTime)
        {
            due = default;
            return false;
        }
        float lateness = (float)(renderTime - _times[_head]);
        due = new DueEvent<T>(_times[_head], _items[_head], lateness, lateness > LateTolerance);
        _items[_head] = default;
        _head++;
        _count--;
        if (_count == 0)
            _head = 0;
        return true;
    }

    /// <summary>Adds every due event to <paramref name="into"/>; returns how many.</summary>
    public int Due(double renderTime, List<DueEvent<T>> into)
    {
        int n = 0;
        while (TryTakeDue(renderTime, out var due))
        {
            into.Add(due);
            n++;
        }
        return n;
    }

    /// <summary>The time of the next event (positive infinity when none).</summary>
    public double NextTime => _count == 0 ? double.PositiveInfinity : _times[_head];

    public void Clear()
    {
        Array.Clear(_items, 0, _items.Length);
        _head = _count = 0;
    }

    private void MakeRoom()
    {
        if (_head > 0)
        {
            Array.Copy(_times, _head, _times, 0, _count);
            Array.Copy(_items, _head, _items, 0, _count);
            Array.Clear(_items, _count, _head);
            _head = 0;
            return;
        }
        Array.Resize(ref _times, _times.Length * 2);
        Array.Resize(ref _items, _items.Length * 2);
    }
}

/// <summary>An event handed out by <see cref="TimedEvents{T}"/>: when it happened, and how far the render time
/// already was past that (fast-forward the animation by <see cref="Lateness"/> when <see cref="IsLate"/>).</summary>
public readonly struct DueEvent<T>
{
    public readonly double Time;
    public readonly T Item;
    public readonly float Lateness;
    public readonly bool IsLate;

    public DueEvent(double time, T item, float lateness, bool isLate)
    {
        Time = time;
        Item = item;
        Lateness = lateness;
        IsLate = isLate;
    }
}
