using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using Xunit;

namespace DamageInsight.Tests.Coop;

public class CoopDiagLogTests
{
    private readonly List<string> _lines = new();
    private CoopDiagLog Log() => new(_lines.Add);

    [Fact]
    public void SessionStartJoinLeaveAndEnd_ArePlainLines()
    {
        var log = Log();
        log.SessionStarted(0, "host", "UDP", "0.16.3", "Umut", 0, new[] { "P1 Umut" });
        log.Joined(5, 1, "Bob");
        log.Left(70, 1, "Bob", LeaveReason.Timeout);
        log.SessionEnded(130, LeaveReason.LocalLeave, "quit");
        Assert.StartsWith("session started as host over UDP (mod 0.16.3", _lines[0]);
        Assert.Equal("P2 Bob joined", _lines[1]);
        Assert.Equal("P2 Bob left: nothing heard for too long (Timeout)", _lines[2]);
        Assert.StartsWith("session ended after 2 min: this game left (LocalLeave: quit); 1 joined, 1 left", _lines[3]);
    }

    [Fact]
    public void TeammateLine_AveragesTheMinute()
    {
        var log = Log();
        log.SessionStarted(0, "client", "Steam", "v", "Bob", 1, null);
        log.Sample(1, 0, "Umut", 80, 10, 1, 0.1);
        log.Sample(2, 0, "Umut", 120, 20, -1, 2.5);
        log.Sample(3, 0, "Umut", 100, 30, -1, 3.5);
        log.Sample(4, 0, "Umut", 100, 20, -1, 0.1);
        log.Traffic(0, 300, 2, 1, 10);
        log.Tick(30);
        Assert.Single(_lines);
        log.Tick(60);
        Assert.Equal("P1 Umut (last 1 min): ping 100 ms avg / 120 max, jitter 20 ms, loss 1%, 30 states/s, 2 snaps, 1 rewinds, " +
                     "1 silence over 2 s (longest 3.5 s)", _lines[1]);
    }

    [Fact]
    public void SameWarning_IsRateLimitedAndCounted()
    {
        var log = Log();
        for (int i = 0; i < 10; i++)
            log.Warning(i, "[Coop] out of step: this game is in room " + i + ", the host in " + (i + 1) + ": going there");
        Assert.Equal(CoopDiagLog.Burst, _lines.Count);
        log.Warning(100, "[Coop] out of step: again");
        Assert.EndsWith("(+7 more like it since the last one)", _lines.Last());
        Assert.StartsWith("warning: out of step: ", _lines.Last());
    }

    [Fact]
    public void AfterTheBudget_OnlySummariesEveryFiveMinutes()
    {
        var log = Log();
        log.SessionStarted(0, "host", "UDP", "v", "Umut", 0, null);
        for (int i = 0; i < 300; i++)
            log.Warning(i * 100, "kind " + (char)('a' + i % 26) + i + " x: text"); // distinct keys, never rate limited
        Assert.True(log.Quiet);
        Assert.Equal(CoopDiagLog.MaxLines + 1, _lines.Count); // + the "from now on" notice
        Assert.Equal(300 - (CoopDiagLog.MaxLines - 1), log.LeftOut);
        log.Tick(30_000); // a summary of what was left out; the next period starts
        Assert.Contains(_lines.Skip(CoopDiagLog.MaxLines + 1), l => l.Contains("co-op lines left out"));
        int before = _lines.Count;
        log.Sample(30_000, 1, "Bob", 50, 5, -1, 0);
        log.Tick(30_000 + 60);
        Assert.Equal(before, _lines.Count); // quiet: no minute lines
        log.Tick(30_000 + 300);
        Assert.Contains(_lines.Skip(before), l => l.StartsWith("P2 Bob (last 5 min)"));
    }

    [Fact]
    public void FailedJoins_AreRateLimited_AndErrorsCountedInTheEnd()
    {
        var log = Log();
        for (int i = 0; i < 20; i++)
            log.SessionEnded(i * 3, LeaveReason.Timeout, "no answer");
        Assert.Equal(CoopDiagLog.Burst, _lines.Count);
        log.SessionStarted(100, "client", "UDP", "v", "Bob", 1, null);
        log.Error(101, "[Coop] the enemies: System.NullReferenceException: x");
        log.Error(102, "[Coop] the enemies: System.NullReferenceException: x");
        log.Barrier(103, 4.2, false);
        log.Barrier(104, 1.0, false);
        log.SessionEnded(200, LeaveReason.HostLeft, "bye");
        Assert.Contains("loading barrier: waited 4.2 s for everyone", _lines);
        Assert.Contains("co-op errors logged: the enemies x2", _lines.Last());
        Assert.Contains("1 long loading waits", _lines.Last());
    }

    [Fact]
    public void SessionLines_SkipWhatEventsCover()
    {
        var log = Log();
        log.SessionLine(0, "Bob joined in slot 1 (peer 7)");
        log.SessionLine(0, "session ended: HostLeft (bye)");
        log.SessionLine(0, "turned away peer 9: VersionMismatch (mod 0.15)");
        Assert.Equal(new[] { "network: turned away peer 9: VersionMismatch (mod 0.15)" }, _lines);
        Assert.Equal("the handler for PlayerState from slot #", CoopDiagLog.KeyOf("the handler for PlayerState from slot 12 failed: x"));
        Assert.Equal("1 h 5 min", CoopDiagLog.Span(3900));
    }
}
