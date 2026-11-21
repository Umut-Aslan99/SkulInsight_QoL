using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The teammate event message: gear and moves of a teammate (phase 1).</summary>
public class TeammateEventTests
{
    private static TeammateEvent RoundTrip(TeammateEvent e, double time, out double readTime)
    {
        var w = new NetWriter();
        e.Write(w, time);
        return TeammateEvent.Read(new NetReader(w.ToSegment()), out readTime);
    }

    [Fact]
    public void A_motion_event_round_trips()
    {
        var read = RoundTrip(TeammateEvent.ForMotion("Weapons/WereWolf_3/Equipped/Skill/Leap", 2, 1.25f), 77.5, out double time);
        Assert.Equal(77.5, time);
        Assert.Equal(TeammateEventKind.Motion, read.Kind);
        Assert.Equal("Weapons/WereWolf_3/Equipped/Skill/Leap", read.ActionPath);
        Assert.Equal(2, read.MotionIndex);
        Assert.Equal(1.25f, read.Speed);
    }

    [Fact]
    public void A_nonblock_motion_keeps_its_kind()
    {
        var read = RoundTrip(TeammateEvent.ForMotion("Base/Alchemist/Equipped/Attack", 0, 1f, nonblock: true), 5, out _);
        Assert.Equal(TeammateEventKind.NonblockMotion, read.Kind);
        Assert.Equal("Base/Alchemist/Equipped/Attack", read.ActionPath);
        Assert.Equal(TeammateEventKind.Motion, RoundTrip(TeammateEvent.ForMotion("a", 0, 1f), 5, out _).Kind);
    }

    [Fact]
    public void A_loadout_round_trips_and_compares_by_value()
    {
        var loadout = new Loadout
        {
            Weapons = new[] { "WereWolf_3", "Skeleton_Shield_2" },
            Skills = new[] { new[] { "Leap", "Howl" }, new[] { "Bash" } },
            Current = 1,
            Essence = "Ogre",
            Items = new[] { "BoneOfBrave", "CursedSword" },
        };
        var read = RoundTrip(TeammateEvent.ForLoadout(loadout), 1, out _);
        Assert.Equal(TeammateEventKind.Loadout, read.Kind);
        Assert.Equal(loadout, read.Loadout);
        Assert.Equal(new[] { "Leap", "Howl" }, read.Loadout.Skills[0]);
        Assert.Equal(1, read.Loadout.Current);

        var other = RoundTrip(TeammateEvent.ForLoadout(loadout), 1, out _).Loadout;
        other.Current = 0;
        Assert.NotEqual(loadout, other);
        Assert.False(loadout.Equals(null));
    }

    [Fact]
    public void An_unknown_kind_is_skipped_not_an_error()
    {
        var w = new NetWriter();
        w.WriteDouble(3);
        w.WriteByte(200);
        Assert.Null(TeammateEvent.Read(new NetReader(w.ToSegment()), out _));
    }

    [Fact]
    public void Gear_names_drop_unitys_clone_suffix()
    {
        Assert.Equal("WereWolf_3", Loadout.GearName("WereWolf_3(Clone)"));
        Assert.Equal("Skul", Loadout.GearName("Skul (Clone)"));
        Assert.Equal("Ogre", Loadout.GearName("Ogre"));
        Assert.Equal("", Loadout.GearName(null));
    }

    [Fact]
    public void A_projectile_event_round_trips()
    {
        var sent = TeammateEvent.ForProjectile("DavyJones_ReadyToShoot_Bomb_Projecitle", 1.5f, -0.25f, 180f,
            ShotFlags.FlipX, 1.2f, -1f, 2f, 0.1f);
        var read = RoundTrip(sent, 12.25, out double time);
        Assert.Equal(12.25, time);
        Assert.Equal(TeammateEventKind.Projectile, read.Kind);
        Assert.Equal("DavyJones_ReadyToShoot_Bomb_Projecitle", read.ProjectileName);
        Assert.Equal((1.5f, -0.25f, 180f), (read.OffsetX, read.OffsetY, read.Direction));
        Assert.Equal(ShotFlags.FlipX, read.Flags);
        Assert.Equal((1.2f, -1f, 2f, 0.1f), (read.SpeedMultiplier, read.ScaleX, read.ScaleY, read.Delay));
    }

    [Fact]
    public void Pooled_projectile_names_drop_every_clone_suffix()
    {
        Assert.Equal("Clown_Switch", Loadout.PrefabName("Clown_Switch(Clone)(Clone)"));
        Assert.Equal("Clown_Switch", Loadout.PrefabName("Clown_Switch"));
        Assert.Equal("", Loadout.PrefabName(null));
    }
}
