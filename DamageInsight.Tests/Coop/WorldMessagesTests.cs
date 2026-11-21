using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The shared world's messages and the loading barrier (phase 1 step 2).</summary>
public class WorldMessagesTests
{
    private static NetReader Reader(NetWriter w) => new(w.ToSegment());

    [Fact]
    public void A_stage_path_round_trips_with_every_field()
    {
        var a = new PathNodeData { Guid = "0f3a", AssetPath = "Maps/Ch1/Normal_03", MapType = 1, SpecialType = 0, Reward = 2, Gate = 3 };
        var b = new PathNodeData { Guid = "77bc", AssetPath = "Maps/Ch1/Special_Altar", MapType = 4, SpecialType = 2, DarkEnemy = true, Reward = 0, Gate = 1 };
        var path = new List<(PathNodeData, PathNodeData)> { (a, b), (b, a), (new PathNodeData(), new PathNodeData()) };
        var w = new NetWriter();
        WorldMessages.WriteStagePath(w, -12345, "Chapter1_Stage1", path);

        var (seed, stage, read) = WorldMessages.ReadStagePath(Reader(w));
        Assert.Equal(-12345, seed);
        Assert.Equal("Chapter1_Stage1", stage);
        Assert.Equal(3, read.Count);
        Assert.Equal(a, read[0].Item1);
        Assert.Equal(b, read[0].Item2);
        Assert.True(read[0].Item2.DarkEnemy);
        Assert.True(read[2].Item1.Empty);
    }

    [Fact]
    public void Run_start_and_next_map_round_trip()
    {
        var w = new NetWriter();
        WorldMessages.WriteRunStart(w, 987654321, 3);
        Assert.Equal((987654321, 3), WorldMessages.ReadRunStart(Reader(w), out int noLevel));
        Assert.Equal(0, noLevel);

        w = new NetWriter();
        WorldMessages.WriteRunStart(w, -5, 9, darkMirrorLevel: 7); // Dark Mirror chapter 1 at the host's level 7
        Assert.Equal((-5, 9), WorldMessages.ReadRunStart(Reader(w), out int level));
        Assert.Equal(7, level);

        w = new NetWriter();
        WorldMessages.WriteNextMap(w, 42, "HardmodeChapter2_Stage1", 7, 1);
        Assert.Equal((42, "HardmodeChapter2_Stage1", 7, 1), WorldMessages.ReadNextMap(Reader(w)));
    }

    [Fact]
    public void Room_ids_match_across_games_and_differ_between_rooms()
    {
        uint a = WorldMessages.RoomId(5, "Chapter1_Stage1", 2, "Map_Forest_04(Clone)");
        Assert.Equal(a, WorldMessages.RoomId(5, "Chapter1_Stage1", 2, "Map_Forest_04"));
        Assert.NotEqual(a, WorldMessages.RoomId(5, "Chapter1_Stage1", 3, "Map_Forest_04"));
        Assert.NotEqual(a, WorldMessages.RoomId(6, "Chapter1_Stage1", 2, "Map_Forest_04"));
        Assert.NotEqual(0u, a);
    }

    [Fact]
    public void The_barrier_waits_for_everyone_then_releases_once()
    {
        var barrier = new RoomBarrier();
        int[] slots = { 0, 1 };
        barrier.Enter(0, 77, now: 100);
        Assert.True(barrier.Waiting);
        Assert.Equal(new[] { 1 }, barrier.Missing(slots).ToArray());
        Assert.False(barrier.Release(slots, 101, out _));

        barrier.Reached(1, 76); // still in the old room
        Assert.False(barrier.Release(slots, 102, out _));
        barrier.Reached(1, 77);
        Assert.True(barrier.Release(slots, 103, out bool timedOut));
        Assert.False(timedOut);
        Assert.Equal(3, barrier.LastWaitSeconds, 3); // Enter at 100, released at 103 (the session report's long waits)
        Assert.False(barrier.Waiting);
        Assert.False(barrier.Release(slots, 104, out _));
        Assert.Equal(3, barrier.LastWaitSeconds, 3); // a Release that ends no wait keeps the last one
    }

    [Fact]
    public void A_teammate_who_got_there_first_counts_and_a_slow_one_times_out()
    {
        var barrier = new RoomBarrier { TimeoutSeconds = 10 };
        int[] slots = { 0, 1, 2 };
        barrier.Reached(1, 9); // the message came before this game finished loading
        barrier.Enter(0, 9, now: 0);
        Assert.Equal(new[] { 2 }, barrier.Missing(slots).ToArray());
        Assert.False(barrier.Release(slots, 9.9, out _));
        Assert.True(barrier.Release(slots, 10, out bool timedOut));
        Assert.True(timedOut);
        Assert.Equal(10, barrier.LastWaitSeconds, 3);
    }
}
