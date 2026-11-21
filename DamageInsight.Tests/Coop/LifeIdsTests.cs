using DamageInsight.Coop;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Ids of traps and dummies: from their path, the same on every game whatever else the room holds.</summary>
public class LifeIdsTests
{
    [Fact]
    public void The_same_path_gives_the_same_id_on_every_game_whatever_comes_before()
    {
        var host = new LifeIds();
        var client = new LifeIds();
        host.Next("Props/RandomDummy");          // only the host has this one
        ushort? onHost = host.Next("Traps/VineWall");
        ushort? onClient = client.Next("Traps/VineWall");
        Assert.NotNull(onHost);
        Assert.Equal(onHost, onClient);
        Assert.True(LifeIds.IsLifeId(onHost.Value));
        Assert.False(LifeIds.IsLifeId(1));
    }

    [Fact]
    public void Two_with_the_same_path_get_different_ids_in_order()
    {
        var ids = new LifeIds();
        ushort? first = ids.Next("Traps/PowderKeg");
        ushort? second = ids.Next("Traps/PowderKeg");
        Assert.NotEqual(first, second);
        var again = new LifeIds();
        Assert.Equal(first, again.Next("Traps/PowderKeg"));
        Assert.Equal(second, again.Next("Traps/PowderKeg"));
    }
}
