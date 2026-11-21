using System.Collections.Generic;
using DamageInsight.Coop;
using Xunit;
using Part = DamageInsight.Coop.PlayerWatchLogic.Part;

namespace DamageInsight.Tests.Coop;

public class PlayerWatchLogicTests
{
    private readonly List<string> _changes = new();
    private readonly List<string> _anomalies = new();

    private PlayerWatchLogic Watch() => new((part, text) => _changes.Add(part + ": " + text), _anomalies.Add);

    [Fact]
    public void ShortHide_IsAChangeOnly()
    {
        var w = Watch();
        w.Sample(0, "", "", "");
        w.Sample(1, "collider off, renderer off", "", "");
        w.Sample(1.3, "", "", "");
        w.Sample(10, "", "", "");
        Assert.Equal(new[] { "Hidden: collider off, renderer off", "Hidden: normal again after 0.3 s" }, _changes);
        Assert.Empty(_anomalies);
    }

    [Fact]
    public void LongHide_IsOneAnomaly_AndItsEndAnother()
    {
        var w = Watch();
        w.Sample(100, "collider off, renderer off, attach off", "", "");
        w.Sample(102, "collider off, renderer off, attach off", "", "");
        w.Sample(103, "collider off, renderer off, attach off", "", "");
        w.Sample(200, "collider off, renderer off, attach off", "", "");
        Assert.Equal(new[] { "this player hidden for 3 s: collider off, renderer off, attach off" }, _anomalies);
        Assert.Equal(100, w.For(Part.Hidden, 200), 3);
        w.Sample(250, "", "", "");
        Assert.Equal("this player hidden: over after 2.5 min", _anomalies[1]);
        Assert.Equal(0, w.For(Part.Hidden, 251));
    }

    [Fact]
    public void ChangingHiddenParts_KeepsTheStart()
    {
        var w = Watch();
        w.Sample(0, "collider off", "", "");
        w.Sample(2, "collider off, renderer off", "", "");
        w.Sample(3, "collider off, renderer off", "", "");
        Assert.Single(_anomalies);
        Assert.StartsWith("this player hidden for 3 s: collider off, renderer off", _anomalies[0]);
    }

    [Fact]
    public void BlockedControls_ReportedAfterTheirLimit_WithHolders()
    {
        var w = Watch();
        w.Sample(0, "", "LetterBox (CutScenes.LetterBox)", "");
        w.Sample(14.9, "", "LetterBox (CutScenes.LetterBox)", "");
        Assert.Empty(_anomalies);
        w.Sample(15, "", "LetterBox (CutScenes.LetterBox)", "");
        Assert.Equal(new[] { "controls blocked without a dialogue for 15 s: LetterBox (CutScenes.LetterBox)" }, _anomalies);
    }

    [Fact]
    public void Parent_IsTracedButNeverAnAnomaly()
    {
        var w = Watch();
        w.Sample(0, "", "", "MovingPlatform");
        w.Sample(600, "", "", "MovingPlatform");
        w.Sample(601, "", "", "");
        Assert.Empty(_anomalies);
        Assert.Equal(new[] { "Parent: MovingPlatform", "Parent: normal again after 10 min" }, _changes);
    }

    [Fact]
    public void Changes_AreBoundedPerMinute()
    {
        var w = Watch();
        for (int i = 0; i < 200; i++)
            w.Sample(i * 0.1, i % 2 == 0 ? "renderer off" : "", "", "");
        Assert.Equal(PlayerWatchLogic.MaxChangesPerMinute, _changes.Count);
        w.Sample(61, "", "", "");
        Assert.Contains(_changes, c => c.Contains("more player state changes in the last minute"));
    }
}
