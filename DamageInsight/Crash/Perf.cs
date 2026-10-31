using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace DamageInsight.Crash;

/// <summary>
/// How long SkulInsight's own code takes, part by part (Umut 2026-10-28: "the games performance somehow is suffering
/// quite a lot especially with some builds ... Can we somehow log or monitor when the game is running slow and figure
/// out, why that is happening?"). Every hook (through Guard) and every per-frame update of the mod is a measured
/// "scope": a timestamp at its start and its end, added up per part. It also knows what the mod's code is doing at
/// this moment (<see cref="Current"/>), which the freeze watch writes down if the game stops. Plain C#: the tests use it.
/// </summary>
public static class Perf
{
    /// <summary>A part's time: in a minute (the report's line) and in the whole session (its summary).</summary>
    public sealed class Part
    {
        public long Ticks, Calls, MaxTicks;

        public void Add(long ticks)
        {
            Ticks += ticks;
            Calls++;
            if (ticks > MaxTicks)
                MaxTicks = ticks;
        }
    }

    public static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;
    private const int MaxParts = 300;
    private const int MaxDepth = 32;
    private const string OtherPart = "(other parts)";

    /// <summary>Off: scopes cost nothing (the setting General/PerformanceLog).</summary>
    public static bool Enabled = true;

    private static int _mainThread = -1;
    private static int _depth;
    private static readonly string[] Stack = new string[MaxDepth];
    private static long _frameTicks;
    private static readonly Dictionary<string, Part> Minute = new(), Session = new();
    private static readonly List<(string name, long ticks)> HeavyThisFrame = new();
    private static readonly object SessionLock = new();

    /// <summary>What SkulInsight's code is doing right now ("" = none of its code is running). Read by the freeze watch.</summary>
    public static volatile string Current = "";

    /// <summary>The last part of SkulInsight that finished, and when (Stopwatch ticks).</summary>
    public static volatile string LastFinished = "";

    /// <summary>The thread the game runs on (only its scopes count; a background task's don't).</summary>
    public static void SetMainThread() => _mainThread = Thread.CurrentThread.ManagedThreadId;

    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _start;

        internal Scope(string name, long start)
        {
            _name = name;
            _start = start;
        }

        public void Dispose()
        {
            if (_name != null)
                End(_name, _start);
        }
    }

    /// <summary><c>using (Perf.Measure("Codex")) { ... }</c>: the time in between counts for that part.</summary>
    public static Scope Measure(string name)
    {
        if (!Enabled || name == null || (_mainThread >= 0 && Thread.CurrentThread.ManagedThreadId != _mainThread))
            return default;
        if (_depth < MaxDepth)
            Stack[_depth] = name;
        _depth++;
        Current = name;
        return new Scope(name, Stopwatch.GetTimestamp());
    }

    private static void End(string name, long start)
    {
        long ticks = Stopwatch.GetTimestamp() - start;
        _depth = Math.Max(0, _depth - 1);
        Current = _depth > 0 ? Stack[Math.Min(_depth, MaxDepth) - 1] ?? "" : "";
        LastFinished = name;
        if (_depth == 0)
        {
            _frameTicks += ticks;
            // A part that took more than 1 ms at once: named on a stutter's line.
            if (ticks > TicksPerMs && HeavyThisFrame.Count < 8)
                HeavyThisFrame.Add((name, ticks));
        }
        Add(Minute, name, ticks);
    }

    private static void Add(Dictionary<string, Part> parts, string name, long ticks)
    {
        if (!parts.TryGetValue(name, out var part))
        {
            if (parts.Count >= MaxParts)
                name = OtherPart;
            if (!parts.TryGetValue(name, out part))
                parts[name] = part = new Part();
        }
        part.Add(ticks);
    }

    /// <summary>SkulInsight's time since the last call (once a frame) and the parts that took over 1 ms at once.</summary>
    public static long TakeFrame(List<(string name, long ticks)> heavy)
    {
        long ticks = _frameTicks;
        _frameTicks = 0;
        heavy?.Clear();
        heavy?.AddRange(HeavyThisFrame);
        HeavyThisFrame.Clear();
        return ticks;
    }

    /// <summary>The minute's parts (most time first); they go into the session's total and start again.</summary>
    public static List<(string name, Part part)> TakeMinute()
    {
        var list = Minute.Select(p => (p.Key, p.Value)).OrderByDescending(p => p.Value.Ticks).ToList();
        lock (SessionLock)
            foreach (var (name, part) in list)
            {
                if (!Session.TryGetValue(name, out var total))
                    Session[name] = total = new Part();
                total.Ticks += part.Ticks;
                total.Calls += part.Calls;
                total.MaxTicks = Math.Max(total.MaxTicks, part.MaxTicks);
            }
        Minute.Clear();
        return list;
    }

    /// <summary>The whole session's parts so far (most time first).</summary>
    public static List<(string name, Part part)> SessionParts()
    {
        lock (SessionLock)
            return Session.Select(p => (p.Key, new Part { Ticks = p.Value.Ticks, Calls = p.Value.Calls, MaxTicks = p.Value.MaxTicks }))
                .OrderByDescending(p => p.Item2.Ticks).ToList();
    }

    /// <summary>Tests: everything back to the start.</summary>
    public static void Reset()
    {
        _depth = 0;
        _frameTicks = 0;
        Current = LastFinished = "";
        Minute.Clear();
        HeavyThisFrame.Clear();
        lock (SessionLock)
            Session.Clear();
    }

    public static double Ms(long ticks) => ticks / TicksPerMs;
}
