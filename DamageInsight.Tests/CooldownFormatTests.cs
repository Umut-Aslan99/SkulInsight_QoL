using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

public class CooldownFormatTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(0.2, "1s")]    // shows 1s until it is ready
    [InlineData(82.3, "83s")]  // counts down in whole seconds
    [InlineData(83, "83s")]
    [InlineData(99.5, "1m+")]  // 100 s or more: minutes
    [InlineData(125, "2m+")]
    [InlineData(120, "2m")]
    [InlineData(600, "10m")]
    [InlineData(601, "10m+")]
    public void Format(double seconds, string expected) => Assert.Equal(expected, CooldownFormat.Format(seconds));

    [Theory]
    [InlineData(8, 1, 8)]           // swap, no bonus
    [InlineData(8, 2, 4)]           // +100% swap cooldown speed: half the time
    [InlineData(6, 1.5, 4)]         // skill with +50% skill cooldown speed
    [InlineData(0, 1.5, 0)]         // ready
    [InlineData(5, 0, 0)]           // speed 0 never ends: show nothing
    public void RealSeconds(double remain, double speed, double expected) =>
        Assert.Equal(expected, CooldownFormat.RealSeconds(remain, speed), precision: 6);
}
