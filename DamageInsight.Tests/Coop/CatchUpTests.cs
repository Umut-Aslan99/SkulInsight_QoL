using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>A client out of step with the host goes to the host's room by itself.</summary>
public class CatchUpTests
{
    private static RunSpot Room(int stage, int path, int node = 0, int seed = 7, int chapter = 5) =>
        new(false, seed, chapter, stage, path, node);

    [Fact]
    public void In_the_same_room_or_with_nothing_known_it_is_in_step()
    {
        var c = new CatchUp();
        Assert.Equal(CatchUpStep.InStep, c.Decide(null, Room(0, 1), false, false, 0));
        Assert.Equal(CatchUpStep.InStep, c.Decide(Room(0, 1), Room(0, 1), false, false, 0));
        Assert.Equal(CatchUpStep.InStep, c.Decide(RunSpot.InCastle(1), RunSpot.InCastle(1), false, false, 0));
    }

    [Fact]
    public void Elsewhere_it_waits_the_grace_time_then_jumps()
    {
        var c = new CatchUp { Grace = 3 };
        var host = Room(1, 0);
        Assert.Equal(CatchUpStep.Wait, c.Decide(host, Room(0, 1), false, false, 10));
        Assert.Equal(CatchUpStep.Wait, c.Decide(host, Room(0, 1), false, false, 12.9));
        Assert.Equal(CatchUpStep.Jump, c.Decide(host, Room(0, 1), false, false, 13.1));
    }

    [Fact]
    public void Loading_or_a_door_on_the_way_restarts_the_wait()
    {
        var c = new CatchUp { Grace = 3 };
        var host = Room(0, 2);
        Assert.Equal(CatchUpStep.Wait, c.Decide(host, Room(0, 1), false, false, 0));
        Assert.Equal(CatchUpStep.Wait, c.Decide(host, Room(0, 1), false, true, 2.5));   // the host's door is queued
        Assert.Equal(CatchUpStep.Wait, c.Decide(host, Room(0, 1), true, false, 4));     // loading
        Assert.Equal(CatchUpStep.Wait, c.Decide(host, Room(0, 1), false, false, 5));
        Assert.Equal(CatchUpStep.Jump, c.Decide(host, Room(0, 1), false, false, 8.1));
    }

    [Fact]
    public void A_different_node_seed_or_the_castle_is_another_place()
    {
        Assert.False(Room(0, 1, 0).SameRoom(Room(0, 1, 1)));
        Assert.False(Room(0, 1, seed: 1).SameRoom(Room(0, 1, seed: 2)));
        Assert.False(RunSpot.InCastle(1).SameRoom(Room(0, 0)));
        Assert.True(Room(2, 3).SameChapter(Room(0, 0)));
        Assert.False(Room(0, 0, chapter: 5).SameChapter(Room(0, 0, chapter: 6)));
    }

    [Fact]
    public void After_three_jumps_to_the_same_room_without_arriving_it_gives_up_until_the_host_moves()
    {
        var c = new CatchUp { Grace = 1, MaxJumps = 3 };
        var host = Room(1, 0);
        double t = 0;
        for (int i = 0; i < 3; i++)
        {
            c.Decide(host, Room(0, 1), false, false, t);
            Assert.Equal(CatchUpStep.Jump, c.Decide(host, Room(0, 1), false, false, t + 1.5));
            c.Jumped(host);
            t += 2;
        }
        c.Decide(host, Room(0, 1), false, false, t);
        Assert.Equal(CatchUpStep.GiveUp, c.Decide(host, Room(0, 1), false, false, t + 1.5));
        var moved = Room(1, 1);
        c.Decide(moved, Room(0, 1), false, false, t + 2);
        Assert.Equal(CatchUpStep.Jump, c.Decide(moved, Room(0, 1), false, false, t + 3.5));
    }

    [Fact]
    public void The_host_room_message_round_trips()
    {
        var w = new NetWriter();
        WorldMessages.WriteHostRoom(w, new RunSpot(false, -75082974, 6, 1, 7, 1));
        var read = WorldMessages.ReadHostRoom(new NetReader(w.ToSegment()));
        Assert.True(read.SameRoom(new RunSpot(false, -75082974, 6, 1, 7, 1)));
        var castle = new NetWriter();
        WorldMessages.WriteHostRoom(castle, RunSpot.InCastle(1), inScene: true);
        Assert.True(WorldMessages.ReadHostRoom(new NetReader(castle.ToSegment()), out bool inScene).Castle);
        Assert.True(inScene);
        var dark = new NetWriter();
        WorldMessages.WriteHostRoom(dark, new RunSpot(false, 3, 10, 0, 2, 0), darkMirrorLevel: 10);
        WorldMessages.ReadHostRoom(new NetReader(dark.ToSegment()), out _, out int level);
        Assert.Equal(10, level);
    }
}
