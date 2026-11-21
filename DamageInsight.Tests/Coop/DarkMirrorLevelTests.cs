using DamageInsight.Coop;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Co-op Dark Mirror runs are at the host's level; a client's own progress counts up to its own next level
/// (Umut 2026-11-07, COOP_LOG 35).</summary>
public class DarkMirrorLevelTests
{
    [Theory]
    [InlineData(4, 3, 8, 4)]    // best 3, clears the host's 8: the ending's "+1" gives 4, kept
    [InlineData(8, 3, 8, 4)]    // anything that sets it higher: still only its own next level
    [InlineData(3, 3, 8, 3)]    // not cleared: nothing changes
    [InlineData(5, 5, 2, 5)]    // the host's level is lower: the game changes nothing, the cap neither
    [InlineData(6, 5, 2, 5)]    //   and nothing may push it up
    [InlineData(0, -1, 5, 0)]   // never cleared any level: the first clear gives level 0
    [InlineData(10, 9, 10, 10)] // the last level, played at the host's same level
    public void The_cleared_level_rises_at_most_to_this_games_next_level(int cleared, int before, int played, int expected) =>
        Assert.Equal(expected, DarkMirrorLevel.CapCleared(cleared, before, played));

    [Fact]
    public void Run_chapters_are_dark_mirror_chapters_one_to_six_not_the_castle()
    {
        Assert.Equal((int)Level.Chapter.Type.HardmodeChapter1, DarkMirrorLevel.FirstRun);
        Assert.Equal((int)Level.Chapter.Type.HardmodeChapter6, DarkMirrorLevel.LastRun);
        Assert.False(DarkMirrorLevel.IsRunChapter((int)Level.Chapter.Type.HardmodeCastle));
        Assert.False(DarkMirrorLevel.IsRunChapter((int)Level.Chapter.Type.Chapter5));
        Assert.True(DarkMirrorLevel.IsRunChapter((int)Level.Chapter.Type.HardmodeChapter3));
    }
}
