using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>A client's knockback / smash sent to the host (EnemyPush).</summary>
public class EnemyPushTests
{
    [Fact]
    public void A_push_round_trips()
    {
        var push = new EnemyPush { Id = 513, Smash = true, ExpireOnGround = true };
        push.Parts[0].X = -3.5f;
        push.Parts[0].Y = 1.25f;
        push.Parts[0].ValueMultiplier = 2f;
        push.Parts[0].Duration = 0.4f;
        push.Parts[0].Keys = new[] { 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f };
        var w = new NetWriter();
        EnemyMessages.WritePush(w, 0xABCDEF01, push);
        var read = EnemyMessages.ReadPush(new NetReader(w.ToSegment()), out uint room);
        Assert.Equal(0xABCDEF01u, room);
        Assert.Equal((ushort)513, read.Id);
        Assert.True(read.Smash);
        Assert.False(read.IgnoreOtherForce);
        Assert.True(read.ExpireOnGround);
        Assert.Equal((-3.5f, 1.25f, 2f, 0.4f), (read.Parts[0].X, read.Parts[0].Y, read.Parts[0].ValueMultiplier, read.Parts[0].Duration));
        Assert.Equal(push.Parts[0].Keys, read.Parts[0].Keys);
        Assert.Empty(read.Parts[1].Keys);
    }

    [Fact]
    public void Curve_keys_are_capped()
    {
        var push = new EnemyPush();
        push.Parts[0].Keys = new float[(EnemyPush.MaxKeys + 5) * 4];
        var w = new NetWriter();
        EnemyMessages.WritePush(w, 1, push);
        Assert.Equal(EnemyPush.MaxKeys * 4, EnemyMessages.ReadPush(new NetReader(w.ToSegment()), out _).Parts[0].Keys.Length);
    }
}
