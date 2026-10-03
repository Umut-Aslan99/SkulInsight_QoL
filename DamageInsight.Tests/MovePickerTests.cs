using System;
using System.Linq;
using DamageInsight.Codex;
using Xunit;

namespace DamageInsight.Tests;

public class MovePickerTests
{
    [Fact]
    public void Nothing_wanted_keeps_the_games_pick() =>
        Assert.Equal(-1, MovePicker.Pick(new[] { 0, 0, -1 }));

    [Fact]
    public void Unseen_moves_come_before_moves_not_done_this_fight() =>
        Assert.Equal(2, MovePicker.Pick(new[] { 1, 0, 2, 1 }));

    [Fact]
    public void Children_the_game_would_never_pick_now_are_skipped() =>
        Assert.Equal(1, MovePicker.Pick(new[] { -1, 1 }));

    [Fact]
    public void Equal_wants_are_picked_at_random()
    {
        var random = new Random(7);
        var picks = Enumerable.Range(0, 200).Select(_ => MovePicker.Pick(new[] { 2, 0, 2 }, random)).ToList();
        Assert.Contains(0, picks);
        Assert.Contains(2, picks);
        Assert.DoesNotContain(1, picks);
    }
}
