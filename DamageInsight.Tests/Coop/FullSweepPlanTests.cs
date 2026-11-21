using System.Linq;
using DamageInsight.Coop.Testing;
using Xunit;

namespace DamageInsight.Tests.Coop;

/// <summary>The full skull sweep's plan (docs/coop/packages/FS.md): the option, the two lists, the turns.</summary>
public class FullSweepPlanTests
{
    [Fact]
    public void OtherOptionsAreNoFullSweep()
    {
        foreach (var option in new[] { null, "", "random", "sweep:0:3", "sweepall:1:0", "KingArthur,Berserker_2" })
            Assert.Null(FullSweepPlan.Parse(option));
    }

    [Fact]
    public void ReadsTheOption()
    {
        var plain = FullSweepPlan.Parse("fullsweep");
        Assert.Equal(0, plain.Half);
        Assert.Equal(0, plain.FirstPair);
        Assert.Equal(FullSweepPlan.DefaultSlotSeconds, plain.SlotSeconds);
        Assert.Null(plain.Only);

        var retry = FullSweepPlan.Parse("fullsweep:1:4:120:Hunter+Berserker_2+ +Hunter");
        Assert.Equal(1, retry.Half);
        Assert.Equal(4, retry.FirstPair);
        Assert.Equal(120f, retry.SlotSeconds);
        Assert.Equal(new[] { "Hunter", "Berserker_2" }, retry.Only);

        // a slot out of range keeps the default
        Assert.Equal(FullSweepPlan.DefaultSlotSeconds, FullSweepPlan.Parse("fullsweep:0:0:5").SlotSeconds);
    }

    [Fact]
    public void TheTwoListsSplitOneListWithoutOverlap()
    {
        var all = new[] { "Wizard", "Alchemist", "Hunter", "Alchemist_2", "Berserker", "Hunter" };
        var host = FullSweepPlan.Parse("fullsweep:0:0").Playlist(all);
        var client = FullSweepPlan.Parse("fullsweep:1:0").Playlist(all);
        Assert.Equal(new[] { "Alchemist", "Berserker", "Wizard" }, host);
        Assert.Equal(new[] { "Alchemist_2", "Hunter" }, client);
        Assert.Empty(host.Intersect(client));

        // a retry plays only the names asked for (and only those the game has)
        var retry = FullSweepPlan.Parse("fullsweep:0:0:80:Hunter+Wizard+Nobody").Playlist(all);
        Assert.Equal(new[] { "Hunter" }, retry);
        Assert.Equal(new[] { "Wizard" }, FullSweepPlan.Parse("fullsweep:1:0:80:Hunter+Wizard+Nobody").Playlist(all));
    }

    [Fact]
    public void OneGameActsAtATime()
    {
        var host = FullSweepPlan.Parse("fullsweep:0:0");
        var client = FullSweepPlan.Parse("fullsweep:1:0");
        for (double t = 0; t < 2000; t += 0.25)
        {
            Assert.False(host.CanBegin(t) && client.CanBegin(t));
            Assert.NotEqual(host.Mine(host.Slot(t)), client.Mine(client.Slot(t)));
        }
        // the host's turns are the even slots, a turn begins just after its slot began and ends before it is over
        Assert.True(host.CanBegin(2.0));
        Assert.False(host.CanBegin(0.5));
        Assert.False(host.CanBegin(10.0));
        Assert.True(client.CanBegin(82.0));
        Assert.False(client.CanBegin(2.0));
        Assert.Equal(74.0, host.TurnEnd(0), 3);
        Assert.True(host.TurnEnd(0) < client.SlotStart(1));
    }
}
