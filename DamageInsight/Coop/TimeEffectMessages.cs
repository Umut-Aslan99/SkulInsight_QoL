using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop;

/// <summary>
/// A slow motion that changes the fight (COOP_PLAN 4.5, NEXT_SPECS A; MessageType.TimeEffect, reliable): a player's
/// "the world slows down" (the Fighter's rage, a skill's ChronoInfo, the ModifyTimeScale ability) slows the room's
/// enemies on every game for <see cref="Seconds"/>, instead of one game's whole world.
/// [room u32][key u32][scale f32][seconds f32] (16 bytes).
/// </summary>
public struct TimeEffect
{
    /// <summary>The room it is about (WorldMessages.RoomId): one that arrives after the room changed is dropped.</summary>
    public uint Room;
    /// <summary>Which effect: the source's slot in the top byte, the source object's hash below
    /// (<see cref="KeyFor"/>). A new message with the same key refreshes or ends it.</summary>
    public uint Key;
    /// <summary>The enemies' time scale while it lasts (0.5 = half speed).</summary>
    public float Scale;
    /// <summary>How long it lasts from now; 0 = it ends now; below 0 = until a message with 0 ends it
    /// (<see cref="TimeEffectTable.UntilEndedCap"/> at most).</summary>
    public float Seconds;

    public bool Ends => Seconds == 0f;
    public bool UntilEnded => Seconds < 0f;
    public int SourceSlot => (int)(Key >> 24);

    public static uint KeyFor(int slot, int hash) => (((uint)slot & 0xFF) << 24) | ((uint)hash & 0xFFFFFF);
}

public static class TimeEffectMessages
{
    public const int Size = 16;
    /// <summary>Scales above this are not a slow motion (and not a speed-up the game has).</summary>
    public const float MaxScale = 16f;
    public const float MaxSeconds = 600f;

    public static void Write(NetWriter w, in TimeEffect m)
    {
        w.WriteUInt32(m.Room);
        w.WriteUInt32(m.Key);
        float scale = float.IsNaN(m.Scale) ? 1f : Math.Max(0f, Math.Min(MaxScale, m.Scale));
        float seconds = float.IsNaN(m.Seconds) ? 0f : m.Seconds < 0f ? -1f : Math.Min(MaxSeconds, m.Seconds);
        w.WriteFloat(scale);
        w.WriteFloat(seconds);
    }

    /// <summary>False: not a time effect (too short, a scale or a duration that cannot be).</summary>
    public static bool TryRead(NetReader r, out TimeEffect m)
    {
        m = default;
        if (r.Remaining < Size)
            return false;
        m.Room = r.ReadUInt32();
        m.Key = r.ReadUInt32();
        m.Scale = r.ReadFloat();
        m.Seconds = r.ReadFloat();
        if (float.IsNaN(m.Scale) || m.Scale < 0f || m.Scale > MaxScale)
            return false;
        if (float.IsNaN(m.Seconds) || m.Seconds > MaxSeconds)
            return false;
        if (m.Seconds < 0f)
            m.Seconds = -1f;
        return true;
    }
}

/// <summary>The running time effects of one game, by key (plain logic, tested): a refresh moves the end, an end
/// message or the time running out removes it.</summary>
public sealed class TimeEffectTable
{
    /// <summary>An effect "until ended" ends by itself after this long (the end message never came: its game left).</summary>
    public const double UntilEndedCap = 60.0;

    private readonly Dictionary<uint, (float scale, double endsAt)> _effects = new();
    private readonly List<uint> _expired = new();

    public int Count => _effects.Count;

    public IEnumerable<uint> Keys => _effects.Keys;

    /// <summary>Starts, refreshes or (seconds 0) ends an effect. True when it runs afterwards.</summary>
    public bool Set(in TimeEffect effect, double now)
    {
        if (effect.Ends)
        {
            _effects.Remove(effect.Key);
            return false;
        }
        double seconds = effect.UntilEnded ? UntilEndedCap : Math.Min(effect.Seconds, UntilEndedCap);
        _effects[effect.Key] = (effect.Scale, now + seconds);
        return true;
    }

    public bool TryGetScale(uint key, out float scale)
    {
        bool found = _effects.TryGetValue(key, out var e);
        scale = found ? e.scale : 1f;
        return found;
    }

    public bool Remove(uint key) => _effects.Remove(key);

    public void Clear() => _effects.Clear();

    /// <summary>The effects whose time ran out (removed). The list is reused by the next call.</summary>
    public IReadOnlyList<uint> Expire(double now)
    {
        _expired.Clear();
        foreach (var pair in _effects)
            if (pair.Value.endsAt <= now)
                _expired.Add(pair.Key);
        foreach (uint key in _expired)
            _effects.Remove(key);
        return _expired;
    }
}
