using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

public class WindowMathTests
{
    private const float ScreenW = 1920, ScreenH = 1080, MinW = 560, MinH = 440;

    [Fact]
    public void Move_StaysOnScreen()
    {
        var e = new Edges(100, 100, 900, 700);

        var farRight = WindowMath.Move(e, 5000, 0, ScreenW, ScreenH);
        Assert.Equal(ScreenW, farRight.Right);
        Assert.Equal(900, farRight.Width);

        var farUp = WindowMath.Move(e, 0, -5000, ScreenW, ScreenH);
        Assert.Equal(0, farUp.Top);
        Assert.Equal(700, farUp.Height);
    }

    [Fact]
    public void Move_SnapsToTheBorderWhenClose()
    {
        var e = new Edges(100, 300, 900, 500);

        var nearLeft = WindowMath.Move(e, -85, 0, ScreenW, ScreenH); // 15 px from the left border
        Assert.Equal(0, nearLeft.Left);

        var nearBottom = WindowMath.Move(e, 0, 270, ScreenW, ScreenH); // 10 px above the bottom
        Assert.Equal(ScreenH, nearBottom.Bottom);

        var notNear = WindowMath.Move(e, -50, 0, ScreenW, ScreenH); // 50 px away: no snap
        Assert.Equal(50, notNear.Left);
    }

    [Fact]
    public void Resize_BottomRightCornerKeepsTopLeft()
    {
        var e = new Edges(100, 100, 900, 700);
        var r = WindowMath.Resize(e, leftEdge: false, topEdge: false, dx: 50, dy: 30, MinW, MinH, ScreenW, ScreenH);

        Assert.Equal(100, r.Left);
        Assert.Equal(100, r.Top);
        Assert.Equal(950, r.Width);
        Assert.Equal(730, r.Height);
    }

    [Fact]
    public void Resize_TopLeftCornerKeepsBottomRight()
    {
        var e = new Edges(300, 200, 900, 700);
        var r = WindowMath.Resize(e, leftEdge: true, topEdge: true, dx: -100, dy: -50, MinW, MinH, ScreenW, ScreenH);

        Assert.Equal(e.Right, r.Right);
        Assert.Equal(e.Bottom, r.Bottom);
        Assert.Equal(200, r.Left);
        Assert.Equal(150, r.Top);
    }

    [Fact]
    public void Resize_RespectsMinimumSizeAndScreen()
    {
        var e = new Edges(300, 200, 900, 700);

        var tiny = WindowMath.Resize(e, false, false, -5000, -5000, MinW, MinH, ScreenW, ScreenH);
        Assert.Equal(MinW, tiny.Width);
        Assert.Equal(MinH, tiny.Height);

        var huge = WindowMath.Resize(e, true, true, -5000, -5000, MinW, MinH, ScreenW, ScreenH);
        Assert.Equal(0, huge.Left);
        Assert.Equal(0, huge.Top);
    }

    [Fact]
    public void Fit_PullsASavedWindowBackOntoASmallerScreen()
    {
        var saved = new Edges(1500, 600, 900, 700); // saved on a big screen
        var fitted = WindowMath.Fit(saved, MinW, MinH, 1280, 720);

        Assert.True(fitted.Left >= 0 && fitted.Right <= 1280);
        Assert.True(fitted.Top >= 0 && fitted.Bottom <= 720);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var e = new Edges(12, 34, 900, 700);
        Assert.True(WindowFrame.TryParse(WindowFrame.Serialize(e), out var back));
        Assert.Equal(e.Left, back.Left);
        Assert.Equal(e.Height, back.Height);
        Assert.False(WindowFrame.TryParse("", out _));
        Assert.False(WindowFrame.TryParse("1,2,x,4", out _));
    }
}
