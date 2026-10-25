using System;
using System.Linq;
using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Clear view's fade (over the run, when busy) and the achievement chime's samples.</summary>
public class ClearViewTests
{
    [Theory]
    [InlineData(Level.Chapter.Type.Castle, 0f)]
    [InlineData(Level.Chapter.Type.Chapter1, 0f)]
    [InlineData(Level.Chapter.Type.Chapter3, 0.5f)]
    [InlineData(Level.Chapter.Type.Chapter5, 1f)]
    [InlineData(Level.Chapter.Type.HardmodeChapter2, 0.25f)]
    [InlineData(Level.Chapter.Type.HardmodeChapter6, 1f)]
    public void OverTheRun_FadesChapterByChapter(Level.Chapter.Type chapter, float fade)
    {
        Assert.Equal(fade, ClearView.RunProgress(chapter));
    }

    [Fact]
    public void WhenBusy_StartsAtEight_AndIsFullAtForty()
    {
        Assert.Equal(0f, ClearView.BusyFade(0));
        Assert.Equal(0f, ClearView.BusyFade(ClearView.BusyFrom));
        Assert.Equal(0.5f, ClearView.BusyFade((ClearView.BusyFrom + ClearView.BusyFull) / 2), 3);
        Assert.Equal(1f, ClearView.BusyFade(ClearView.BusyFull));
        Assert.Equal(1f, ClearView.BusyFade(200));
    }

    [Fact]
    public void Chime_IsShort_Soft_AndEndsInSilence()
    {
        var samples = AchievementSound.Samples();
        Assert.InRange(samples.Length / (double)AchievementSound.Rate, 0.3, 0.6);
        Assert.All(samples, s => Assert.False(float.IsNaN(s)));
        Assert.Equal(0.42f, samples.Max(Math.Abs), 3);           // well below full scale
        Assert.Equal(0f, samples[samples.Length - 1], 3);       // no click at the end
        Assert.True(Math.Abs(samples[0]) < 0.01f);               // and none at the start
    }
}
