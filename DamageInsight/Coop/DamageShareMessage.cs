using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop;

/// <summary>
/// One player's damage, for every player's combat log (Umut 2026-11-21: "a multiplayer version with multiple bars ...
/// compare Damage dealt per person ... multiple rings ... a DPS screening"). Each game knows its own damage exactly
/// (DamageLog, with its sources), so each sends a short summary every few seconds (MessageType.DamageShare, relayed by
/// the host) and every game shows the same bars and rings for everyone.
/// Layout: [run total f64][room total f64][room u16][dps f32][n u8] then n x [source u8][amount f64] (plain logic,
/// tested). Sources go as DamageSource numbers, so every game names and colours them in its own language.
/// </summary>
public sealed class DamageShare
{
    public const int MaxSources = 8;

    public double RunTotal, RoomTotal;
    public int Room;
    /// <summary>Damage per second over the last few seconds of fighting.</summary>
    public float Dps;
    /// <summary>The run's biggest sources (DamageSource values), largest first.</summary>
    public List<(byte Source, double Amount)> Sources = new();

    public void Write(NetWriter w)
    {
        w.WriteDouble(RunTotal);
        w.WriteDouble(RoomTotal);
        w.WriteUInt16((ushort)Math.Max(0, Math.Min(ushort.MaxValue, Room)));
        w.WriteFloat(Dps);
        int n = Math.Min(Sources.Count, MaxSources);
        w.WriteByte((byte)n);
        for (int i = 0; i < n; i++)
        {
            w.WriteByte(Sources[i].Source);
            w.WriteDouble(Sources[i].Amount);
        }
    }

    public static DamageShare Read(NetReader r)
    {
        var share = new DamageShare
        {
            RunTotal = r.ReadDouble(),
            RoomTotal = r.ReadDouble(),
            Room = r.ReadUInt16(),
            Dps = r.ReadFloat(),
        };
        int n = Math.Min((int)r.ReadByte(), MaxSources);
        for (int i = 0; i < n; i++)
        {
            byte source = r.ReadByte();
            share.Sources.Add((source, r.ReadDouble()));
        }
        return share;
    }

    /// <summary>Damage per second from (time, amount) hits: the sum over the window ending at <paramref name="now"/>,
    /// divided by the window (or by the time since the first hit in it, at least 1 s, while a fight starts).</summary>
    public static float DpsOf(IReadOnlyList<(float Time, double Amount)> hits, float now, float window = 8f)
    {
        double sum = 0;
        float first = float.MaxValue;
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            var (time, amount) = hits[i];
            if (time < now - window)
                break;
            if (time > now)
                continue;
            sum += amount;
            first = Math.Min(first, time);
        }
        if (sum <= 0)
            return 0f;
        float span = Math.Max(1f, Math.Min(window, now - first));
        return (float)(sum / span);
    }
}
