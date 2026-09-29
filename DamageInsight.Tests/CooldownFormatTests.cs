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
}
