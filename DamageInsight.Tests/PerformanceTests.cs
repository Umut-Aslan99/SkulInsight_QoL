using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DamageInsight.Crash;
using DamageInsight.Patches;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The performance lines and the freeze watch's verdict (0.16, Umut's lag on stream; the daily name freeze).</summary>
[Collection("Perf")]
public class PerformanceTests : IDisposable
{
    public PerformanceTests()
    {
        Perf.Reset();
        Perf.SetMainThread(); // other tests' hooks on other threads don't count here
    }

    public void Dispose() => Perf.Reset();

    [Fact]
    public void Scopes_add_up_per_part_and_only_the_outer_one_counts_for_the_frame()
    {
        using (Perf.Measure("Outer"))
        {
            Assert.Equal("Outer", Perf.Current);
            using (Perf.Measure("Inner"))
            {
                Assert.Equal("Inner", Perf.Current);
                Thread.Sleep(5);
            }
            Assert.Equal("Outer", Perf.Current);
        }
        Assert.Equal("", Perf.Current);
        Assert.Equal("Outer", Perf.LastFinished);

        var heavy = new List<(string name, long ticks)>();
        long frame = Perf.TakeFrame(heavy);
        Assert.True(Perf.Ms(frame) >= 4, "the outer scope's time is the frame's");
        Assert.Equal(new[] { "Outer" }, heavy.Select(h => h.name)); // over 1 ms at once: named on a stutter's line
        Assert.Equal(0, Perf.TakeFrame(null));

        var minute = Perf.TakeMinute();
        Assert.Equal(new[] { "Outer", "Inner" }, minute.Select(p => p.name));
        Assert.All(minute, p => Assert.Equal(1, p.part.Calls));
        Assert.Empty(Perf.TakeMinute());
        Assert.Equal(2, Perf.SessionParts().Count); // the minute went into the session's total
    }

    [Fact]
    public void Guard_measures_its_part_and_an_error_still_closes_the_scope()
    {
        Guard.Run("Test part", () => { });
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using (Perf.Measure("Throws"))
                throw new InvalidOperationException("boom");
        }));
        Assert.Equal("", Perf.Current);
        var minute = Perf.TakeMinute();
        Assert.Contains(minute, p => p.name == "Test part" && p.part.Calls == 1);
        Assert.Contains(minute, p => p.name == "Throws" && p.part.Calls == 1);
    }

    [Fact]
    public void Switched_off_nothing_is_counted()
    {
        Perf.Enabled = false;
        try
        {
            using (Perf.Measure("Off"))
                Assert.Equal("", Perf.Current);
            Assert.Empty(Perf.TakeMinute());
        }
        finally
        {
            Perf.Enabled = true;
        }
    }

    [Fact]
    public void A_minute_line_names_fps_stutters_and_the_mods_share()
    {
        var stats = new PerfStats();
        for (int i = 0; i < 3000; i++)
            stats.Frame(16.7, 0.5, gc: i % 1000 == 0, loading: false);
        stats.Frame(250, 2.0, gc: true, loading: false);  // a stutter
        stats.Frame(900, 0.1, gc: false, loading: true);  // a map change: counted apart
        var parts = new List<(string, Perf.Part)> { ("Damage numbers", new Perf.Part { Ticks = (long)(Perf.TicksPerMs * 900), Calls = 9000 }) };
        string line = stats.TakeMinute(parts, "21:04:00", 400L * 1024 * 1024);
        Assert.StartsWith("performance (last 51 s): 59 fps", line);
        Assert.Contains("1 stutter over 100 ms (+1 during map changes)", line);
        Assert.Contains("SkulInsight 0.50 ms a frame", line);
        Assert.Contains("Damage numbers 0.30", line);
        Assert.Contains("garbage collections 4", line);
        Assert.Contains("memory 400 MB", line);
        Assert.DoesNotContain(",5", line); // the same decimal point on every PC
        Assert.Equal("", stats.TakeMinute(parts, "21:05:00", 0)); // nothing new

        string summary = stats.Summary(parts);
        Assert.Contains("=== Performance ===", summary);
        Assert.Contains("1 stutters over 100 ms (+1 during map changes)", summary);
        Assert.Contains("Damage numbers", summary);
    }

    [Fact]
    public void The_slowest_one_percent()
    {
        var frames = Enumerable.Repeat(10f, 99).Concat(new[] { 100f }).ToList();
        Assert.Equal(10, PerfStats.LowFps(frames), 3);
        Assert.Equal(0, PerfStats.LowFps(new List<float>()));
    }

    [Fact]
    public void A_freeze_in_our_code_is_ours_else_it_says_where_the_game_stood()
    {
        var ours = PerfStats.FreezeVerdict("Codex (draw)", "updates", "the Codex is open");
        Assert.Equal(Blame.Mod, ours.Blame);
        Assert.Contains("Codex (draw)", ours.Text);
        Assert.Equal(Blame.Mod, SessionReport.BlameOf(ours.Text));

        var ime = PerfStats.FreezeVerdict("", "between frames",
            "the Codex is open; a text box has the keyboard ('Name', 3 characters); an input method (IME) is composing (2 characters)");
        Assert.Equal(Blame.Game, ime.Blame);
        Assert.StartsWith("NOT SKULINSIGHT'S CODE", ime.Text);
        Assert.Contains("input method", ime.Text);
        Assert.Equal(Blame.Game, SessionReport.BlameOf(ime.Text));

        Assert.Contains("drawing the frame", PerfStats.FreezeVerdict("", "drawing", "").Text);
    }

    [Fact]
    public void A_freeze_that_never_ends_is_the_next_starts_verdict()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skulinsight-perf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var now = new DateTime(2026, 10, 27, 16, 8, 36);
        var report = new SessionReport(folder, new ErrorClassifier("docrun.skulinsight_qol"), () => now);
        report.Open("Mod: test");
        now = now.AddSeconds(20);
        var verdict = PerfStats.FreezeVerdict("", "between frames", "a text box has the keyboard ('Name', 0 characters)");
        report.Incident("the game froze", "No new frame for 6 s", now.AddSeconds(-6), verdict);
        report.Tick();
        report.Abandon(); // the player closes the frozen game

        var previous = SessionReport.FinishPrevious(report.FilePath, now.AddHours(3));
        Assert.True(previous.EndedUnexpectedly);
        Assert.Equal(verdict.Text, previous.Verdict);
        Assert.Contains("!!! INCIDENT: the game froze", File.ReadAllText(report.FilePath));
    }
}
