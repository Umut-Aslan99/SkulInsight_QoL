using System.Collections.Generic;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The co-op combat log's per-player damage summary (Coop/DamageShareMessage).</summary>
public class DamageShareTests
{
    [Fact]
    public void A_summary_reads_back_as_it_was_written_with_at_most_eight_sources()
    {
        var share = new DamageShare { RunTotal = 123456.5, RoomTotal = 789, Room = 14, Dps = 321.25f };
        for (int i = 0; i < 10; i++)
            share.Sources.Add(((byte)i, 1000 - i));
        var w = new NetWriter();
        share.Write(w);
        var back = DamageShare.Read(new NetReader(w.ToSegment()));
        Assert.Equal(123456.5, back.RunTotal);
        Assert.Equal(789, back.RoomTotal);
        Assert.Equal(14, back.Room);
        Assert.Equal(321.25f, back.Dps);
        Assert.Equal(DamageShare.MaxSources, back.Sources.Count);
        Assert.Equal(((byte)0, 1000.0), back.Sources[0]);
    }

    [Fact]
    public void Dps_counts_the_last_window_and_a_starting_fight_by_its_own_length()
    {
        var hits = new List<(float, double)> { (0f, 500), (10f, 100), (12f, 100), (14f, 200) };
        // window 8 s ending at 14: the hits at 10, 12, 14 (400) over the 4 s since the first of them
        Assert.Equal(100f, DamageShare.DpsOf(hits, 14f), 3);
        // ending at 18: still those three, now over the whole window
        Assert.Equal(50f, DamageShare.DpsOf(hits, 18f), 3);
        // nothing in the window
        Assert.Equal(0f, DamageShare.DpsOf(hits, 40f));
        // one hit right now: at least one second
        Assert.Equal(200f, DamageShare.DpsOf(new List<(float, double)> { (5f, 200) }, 5f), 3);
    }
}
