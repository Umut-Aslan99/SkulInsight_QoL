using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The shared world's loot on the wire (COOP_PLAN §4.2): what an enemy dropped, a healing orb, a broken
/// prop with its gold.</summary>
public class LootMessagesTests
{
    private static LootMessage RoundTrip(LootMessage message)
    {
        var w = new NetWriter();
        LootMessages.Write(w, message);
        var r = new NetReader(w.ToSegment());
        Assert.True(LootMessages.TryRead(r, out var read));
        Assert.True(r.IsAtEnd);
        return read;
    }

    [Fact]
    public void An_enemys_gold_arrives_with_its_place_and_push()
    {
        var read = RoundTrip(LootMessage.OfDrop(0xb589d7b1, 12.5f, -3.25f, new LootDrop(0, 37, 5), 4f, 9.5f));
        Assert.Equal(LootMessageKind.Drop, read.Kind);
        Assert.Equal(0xb589d7b1, read.Room);
        Assert.Equal((12.5f, -3.25f, 4f, 9.5f), (read.X, read.Y, read.ForceX, read.ForceY));
        Assert.Equal((0, 37, 5), (read.Drop.Currency, read.Drop.Amount, read.Drop.Count));
    }

    [Fact]
    public void A_healing_orb_names_its_size()
    {
        var read = RoundTrip(LootMessage.OfPotion(7, 1f, 2f, 2));
        Assert.Equal((LootMessageKind.Potion, (byte)2), (read.Kind, read.PotionSize));
        Assert.Equal(2, RoundTrip(LootMessage.OfPotion(7, 1f, 2f, 9)).PotionSize); // never more than large
    }

    [Fact]
    public void A_broken_prop_brings_what_it_dropped_or_nothing()
    {
        var read = RoundTrip(LootMessage.OfBroken(7, 3f, 4f, new[] { new LootDrop(0, 12, 3), new LootDrop(1, 2, 2) }));
        Assert.Equal(LootMessageKind.Broken, read.Kind);
        Assert.Equal(2, read.Drops.Length);
        Assert.Equal((0, 12, 3), (read.Drops[0].Currency, read.Drops[0].Amount, read.Drops[0].Count));
        Assert.Equal((1, 2, 2), (read.Drops[1].Currency, read.Drops[1].Amount, read.Drops[1].Count));
        Assert.Empty(RoundTrip(LootMessage.OfBroken(7, 3f, 4f, null)).Drops);
    }

    [Fact]
    public void Numbers_stay_inside_their_limits()
    {
        var drop = new LootDrop(0, int.MaxValue, 5000);
        Assert.Equal((LootDrop.MaxAmount, LootDrop.MaxCount), (drop.Amount, drop.Count));
        var none = new LootDrop(0, -5, 0);
        Assert.Equal((0, 1), (none.Amount, none.Count));
        var read = RoundTrip(LootMessage.OfDrop(1, 0, 0, drop, 0, 0));
        Assert.Equal((LootDrop.MaxAmount, LootDrop.MaxCount), (read.Drop.Amount, read.Drop.Count));
    }

    [Fact]
    public void Nonsense_is_ignored()
    {
        var w = new NetWriter();
        w.WriteByte(200);
        Assert.False(LootMessages.TryRead(new NetReader(w.ToSegment()), out _));

        w = new NetWriter();
        LootMessages.Write(w, LootMessage.OfDrop(1, float.NaN, 0, new LootDrop(0, 5, 1), 0, 0));
        Assert.False(LootMessages.TryRead(new NetReader(w.ToSegment()), out _)); // a place that is no place

        w = new NetWriter();
        LootMessages.Write(w, LootMessage.OfDrop(1, 1, 1, new LootDrop(0, 5, 1), float.PositiveInfinity, 0));
        Assert.True(LootMessages.TryRead(new NetReader(w.ToSegment()), out var read)); // a broken push: no push
        Assert.Equal((0f, 0f), (read.ForceX, read.ForceY));
    }
}
