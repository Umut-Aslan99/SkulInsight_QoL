using DamageInsight.Coop;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Rooms the host has cleared, as a client remembers them.</summary>
public class RecentRoomsTests
{
    [Fact]
    public void A_room_is_remembered_until_newer_ones_push_it_out()
    {
        var rooms = new RecentRooms(2);
        rooms.Add(10);
        rooms.Add(11);
        Assert.True(rooms.Contains(10));
        rooms.Add(12);
        Assert.False(rooms.Contains(10));
        Assert.True(rooms.Contains(11));
        Assert.True(rooms.Contains(12));
    }

    [Fact]
    public void Room_zero_is_never_a_room_and_a_repeat_changes_nothing()
    {
        var rooms = new RecentRooms(2);
        rooms.Add(0);
        Assert.False(rooms.Contains(0));
        rooms.Add(5);
        rooms.Add(5);
        rooms.Add(6);
        Assert.True(rooms.Contains(5));
        rooms.Clear();
        Assert.False(rooms.Contains(6));
    }
}
