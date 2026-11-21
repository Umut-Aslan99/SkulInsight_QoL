using System.Collections.Generic;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests.Coop;

public class HostWorldTests
{
    private static NetReader Reader(NetWriter w) => new(w.ToSegment());

    [Fact]
    public void The_hosts_world_flags_round_trip()
    {
        var names = new List<string> { "cutscene/rookieHero", "darktech-unlocked/OmenAmplifier", "rescued/fox", "" };
        var w = new NetWriter();
        HostWorldMessages.Write(w, names);
        Assert.True(HostWorldMessages.TryRead(Reader(w), out var read));
        Assert.Equal(names, read);
    }

    [Fact]
    public void A_cut_message_is_refused()
    {
        var w = new NetWriter();
        HostWorldMessages.Write(w, new[] { "cutscene/rookieHero", "cutscene/pope_Intro" });
        var all = w.ToSegment();
        var cut = new NetReader(new System.ArraySegment<byte>(all.Array, all.Offset, all.Count - 3));
        Assert.False(HostWorldMessages.TryRead(cut, out _));
    }
}
