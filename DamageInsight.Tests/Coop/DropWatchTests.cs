using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using DamageInsight.Coop.Testing;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Package DC: the disconnect manager's rules (problem, drop, rejoin, gone, a client's tries and playing on
/// alone), the drop test's broken line in the lag simulator, and its start option.</summary>
public class DropWatchTests
{
    private static List<DropLine> Lines(DropWatch w, double now)
    {
        var lines = new List<DropLine>();
        w.Lines(lines, now);
        return lines;
    }

    private static List<DropNoteKind> Drain(DropWatch w)
    {
        var kinds = w.Notes.Select(n => n.Kind).ToList();
        w.Notes.Clear();
        return kinds;
    }

    [Fact]
    public void A_short_silence_is_a_problem_with_seconds_and_nothing_drops()
    {
        var w = new DropWatch();
        w.Heard(1, "Ann", 11, 0, 100);
        Assert.Empty(Lines(w, 100));
        w.Heard(1, "Ann", 11, 2.9, 102.9);
        Assert.Equal(LinkState.Ok, w.StateOf(1));
        w.Heard(1, "Ann", 11, 3.0, 103);
        Assert.Equal(LinkState.Problem, w.StateOf(1));
        w.Heard(1, "Ann", 11, 7.5, 107.5);
        var line = Assert.Single(Lines(w, 107.5));
        Assert.Equal(DropLineKind.Problem, line.Kind);
        Assert.Equal("Ann", line.Name);
        Assert.Equal(7, line.Seconds);
        w.Heard(1, "Ann", 11, 0, 108);
        Assert.Equal(LinkState.Ok, w.StateOf(1));
        Assert.Empty(Lines(w, 108));
        var notes = w.Notes.ToList();
        Assert.Equal(new[] { DropNoteKind.Problem, DropNoteKind.ProblemOver }, notes.Select(n => n.Kind));
        Assert.Equal(8, notes[1].Seconds, 3);
    }

    [Fact]
    public void A_client_shows_only_the_host_while_the_host_is_silent()
    {
        var w = new DropWatch();
        w.Heard(0, "Host", 1, 5, 105);
        w.Heard(2, "Ben", 22, 5, 105); // comes through the host: silent too
        var line = Assert.Single(Lines(w, 105));
        Assert.Equal("Host", line.Name);
    }

    [Fact]
    public void A_dropped_teammate_is_waited_for_then_comes_back()
    {
        var w = new DropWatch();
        w.Joined(1, "Ann", 11, 0);
        w.Left(1, "Ann", 11, LeaveReason.Timeout, 20);
        Assert.Equal(LinkState.Dropped, w.StateOf(1));
        var line = Assert.Single(Lines(w, 30));
        Assert.Equal(DropLineKind.WaitingFor, line.Kind);
        Assert.Equal(110, line.Seconds); // 120 s window from 20 s, at 30 s
        w.Joined(1, "Ann", 11, 60);
        Assert.Equal(LinkState.Ok, w.StateOf(1));
        Assert.Equal(new[] { DropNoteKind.Dropped, DropNoteKind.Rejoined }, Drain(w));
        Assert.Equal(DropLineKind.Back, Assert.Single(Lines(w, 61)).Kind);
        Assert.Empty(Lines(w, 61 + 10)); // the message fades
    }

    [Fact]
    public void A_teammate_who_never_comes_back_is_gone_and_the_message_says_alone_or_not()
    {
        var w = new DropWatch(new DropSettings { RejoinWindowSeconds = 60 });
        w.Left(1, "Ann", 11, LeaveReason.ConnectionLost, 0);
        w.Tick(59.9, teammates: 0);
        Assert.Equal(LinkState.Dropped, w.StateOf(1));
        w.Tick(60, teammates: 0);
        Assert.Equal(LinkState.Ok, w.StateOf(1)); // forgotten
        Assert.Equal(new[] { DropNoteKind.Dropped, DropNoteKind.Gone }, Drain(w));
        Assert.Equal(DropLineKind.LostAlone, Assert.Single(Lines(w, 61)).Kind);

        w.Left(2, "Ben", 22, LeaveReason.Timeout, 100);
        w.Tick(160, teammates: 1);
        Assert.Equal(DropLineKind.Lost, Assert.Single(Lines(w, 161)).Kind);
    }

    [Theory]
    [InlineData(LeaveReason.Goodbye)]
    [InlineData(LeaveReason.Kicked)]
    [InlineData(LeaveReason.ProtocolError)]
    public void A_meant_leave_is_no_drop(LeaveReason why)
    {
        var w = new DropWatch();
        w.Joined(1, "Ann", 11, 0);
        w.Left(1, "Ann", 11, why, 5);
        Assert.Equal(LinkState.Ok, w.StateOf(1));
        Assert.Empty(Drain(w));
        Assert.Empty(Lines(w, 6));
    }

    [Fact]
    public void A_client_that_lost_the_host_tries_again_every_few_seconds_and_gets_back_in()
    {
        var w = new DropWatch(new DropSettings { RejoinWindowSeconds = 120, RetrySeconds = 3 });
        w.HostLost("Host", LeaveReason.Timeout, wasActive: true, now: 20);
        Assert.True(w.Reconnecting);
        Assert.False(w.RetryDue(22.9));
        Assert.True(w.RetryDue(23));
        Assert.False(w.RetryDue(24)); // a try is under way
        w.HostLost("", LeaveReason.Timeout, wasActive: false, now: 33); // the try got no answer
        Assert.True(w.Reconnecting);
        Assert.False(w.RetryDue(35.9));
        Assert.True(w.RetryDue(36));
        Assert.Equal(2, w.Tries);
        var line = Assert.Single(Lines(w, 40));
        Assert.Equal(DropLineKind.Reconnecting, line.Kind);
        Assert.Equal("Host", line.Name);
        Assert.Equal(100, line.Seconds);
        w.Reconnected(45);
        Assert.False(w.Reconnecting);
        Assert.False(w.IsSolo);
        Assert.Equal(new[] { DropNoteKind.Dropped, DropNoteKind.RetryNow, DropNoteKind.RetryNow, DropNoteKind.Rejoined }, Drain(w));
        Assert.Equal(DropLineKind.Back, Assert.Single(Lines(w, 46)).Kind);
    }

    [Fact]
    public void A_rejected_try_is_tried_again_while_the_window_lasts()
    {
        var w = new DropWatch();
        w.HostLost("Host", LeaveReason.ConnectionLost, true, 0);
        Assert.True(w.RetryDue(3));
        w.HostLost("Host", LeaveReason.Rejected, false, 4); // the host still had the old line (DuplicatePlayer)
        Assert.True(w.Reconnecting);
        Assert.True(w.RetryDue(7));
    }

    [Fact]
    public void When_the_tries_fail_the_client_plays_on_alone()
    {
        var w = new DropWatch(new DropSettings { RejoinWindowSeconds = 30 });
        w.HostLost("Host", LeaveReason.Timeout, true, 20);
        w.Tick(49.9, 0);
        Assert.True(w.Reconnecting);
        w.Tick(50, 0);
        Assert.False(w.Reconnecting);
        Assert.True(w.IsSolo);
        Assert.Equal(DropNoteKind.Solo, Drain(w).Last());
        var line = Assert.Single(Lines(w, 51));
        Assert.Equal(DropLineKind.LostAlone, line.Kind);
        Assert.Equal("Host", line.Name);
        Assert.False(w.RetryDue(60));
        w.HostLost("Host", LeaveReason.Timeout, true, 70); // nothing more once alone
        Assert.Empty(Drain(w));
    }

    [Fact]
    public void The_host_ending_the_session_means_alone_at_once_without_tries()
    {
        var w = new DropWatch();
        w.HostLost("Host", LeaveReason.HostLeft, true, 10);
        Assert.True(w.IsSolo);
        Assert.False(w.Reconnecting);
        Assert.Equal(new[] { DropNoteKind.Solo }, Drain(w));
        Assert.Equal(DropLineKind.HostLeftAlone, Assert.Single(Lines(w, 11)).Kind);
    }

    [Fact]
    public void A_try_that_never_got_in_is_not_a_drop()
    {
        var w = new DropWatch();
        w.HostLost("Host", LeaveReason.Timeout, wasActive: false, now: 10); // a copy started before its host
        Assert.False(w.Reconnecting);
        Assert.False(w.IsSolo);
        Assert.Empty(Drain(w));
    }

    [Fact]
    public void No_rejoin_window_means_alone_at_once()
    {
        var w = new DropWatch(new DropSettings { RejoinWindowSeconds = 0 });
        w.HostLost("Host", LeaveReason.Timeout, true, 10);
        Assert.True(w.IsSolo);
        Assert.Equal(DropLineKind.LostAlone, Assert.Single(Lines(w, 11)).Kind);
    }

    [Fact]
    public void Broken_settings_are_pulled_into_range()
    {
        var s = new DropSettings { ProblemSeconds = 0, DropSeconds = -5, RejoinWindowSeconds = 1e9, RetrySeconds = double.NaN }.Clamped();
        Assert.Equal(1, s.ProblemSeconds);
        Assert.Equal(5, s.DropSeconds);
        Assert.Equal(600, s.RejoinWindowSeconds);
        Assert.Equal(DropSettings.DefaultRetrySeconds, s.RetrySeconds);
        Assert.True(new DropSettings { ProblemSeconds = 30, DropSeconds = 10 }.Clamped().DropSeconds > 30);
    }

    // ---- the broken line of the drop tests --------------------------------------------------------------------

    private sealed class Recorder : ITransportListener
    {
        public readonly List<(byte number, Delivery delivery)> Messages = new();
        public void OnConnected(PeerId peer) { }
        public void OnDisconnected(PeerId peer, string reason) { }
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery) =>
            Messages.Add((message.Array![message.Offset], delivery));
    }

    private sealed class Line
    {
        public double Now;
        public readonly LagSimulator A;
        public readonly LoopbackTransport B;
        public readonly Recorder AtA = new(), AtB = new();

        public Line(Outage outage)
        {
            var hub = new LoopbackTransport.Hub();
            var a = hub.Join();
            B = hub.Join();
            hub.Connect(a, B);
            A = new LagSimulator(a, new LagSettings(), 1, () => Now) { Outage = outage };
            Tick();
        }

        public void Tick()
        {
            A.Poll(AtA);
            B.Poll(AtB);
        }

        public void ToB(byte n, Delivery d) => A.Send(B.Self, new ArraySegment<byte>(new[] { n }), d);
        public void ToA(byte n, Delivery d) => B.Send(A.Self, new ArraySegment<byte>(new[] { n }), d);
    }

    [Fact]
    public void An_outage_loses_unreliable_messages_and_holds_reliable_ones_until_it_ends()
    {
        var outage = new Outage(10, 5);
        var line = new Line(outage);
        outage.Arm(0);
        line.Now = 9;
        line.ToB(1, Delivery.Unreliable);
        line.Tick();
        Assert.Single(line.AtB.Messages);
        line.Now = 11; // broken from 10 to 15
        line.ToB(2, Delivery.Unreliable);
        line.ToB(3, Delivery.Reliable);
        line.ToA(4, Delivery.Unreliable);
        line.ToA(5, Delivery.Reliable);
        line.Tick();
        Assert.Single(line.AtB.Messages);
        Assert.Empty(line.AtA.Messages);
        line.Now = 14.9;
        line.Tick();
        Assert.Single(line.AtB.Messages);
        line.Now = 15;
        line.ToB(6, Delivery.Reliable); // after the held one
        line.Tick();
        line.Tick();
        Assert.Equal(new byte[] { 1, 3, 6 }, line.AtB.Messages.Select(m => m.number));
        Assert.Equal(new byte[] { 5 }, line.AtA.Messages.Select(m => m.number));
        Assert.Equal(2, line.A.LostToOutage);
    }

    [Fact]
    public void An_outage_for_good_loses_everything_from_its_start()
    {
        var outage = new Outage(1, double.PositiveInfinity);
        var line = new Line(outage);
        outage.Arm(0);
        line.Now = 2;
        line.ToB(1, Delivery.Reliable);
        line.ToA(2, Delivery.Reliable);
        line.Now = 1000;
        line.ToB(3, Delivery.Unreliable);
        line.Tick();
        line.Tick();
        Assert.Empty(line.AtB.Messages);
        Assert.Empty(line.AtA.Messages);
        Assert.Equal(3, line.A.LostToOutage);
    }

    [Fact]
    public void An_outage_waits_for_its_arming_and_arms_once()
    {
        var outage = new Outage(5, 2);
        Assert.False(outage.IsOn(100));
        outage.Arm(100);
        outage.Arm(200);
        Assert.Equal(105, outage.From);
        Assert.False(outage.IsOn(104.9));
        Assert.True(outage.IsOn(105));
        Assert.False(outage.IsOn(107));
    }

    [Theory]
    [InlineData("120:8", 120, 8)]
    [InlineData("0:45.5", 0, 45.5)]
    [InlineData("120:forever", 120, double.PositiveInfinity)]
    [InlineData("30:FOREVER", 30, double.PositiveInfinity)]
    public void Outages_are_read_from_text(string text, double after, double length)
    {
        Assert.True(Outage.TryParse(text, out var outage));
        Assert.Equal(after, outage.AfterSeconds);
        Assert.Equal(length, outage.ForSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("120")]
    [InlineData("a:8")]
    [InlineData("120:0")]
    [InlineData("-1:5")]
    [InlineData("1:2:3")]
    public void Broken_outage_texts_are_refused(string text) => Assert.False(Outage.TryParse(text, out _));

    [Fact]
    public void The_drop_start_option_reaches_the_start_plan()
    {
        var launch = CoopLaunch.Parse(new[] { "--skulinsight-coop", "join", "--skulinsight-drop=120:forever" });
        Assert.NotNull(launch.Drop);
        Assert.True(launch.Drop.Forever);
        var plan = DamageInsight.Coop.CoopStartPlan.From(launch);
        Assert.Same(launch.Drop, plan.Drop);
        Assert.Null(plan.Lag);
        Assert.Null(CoopLaunch.Parse(new[] { "--skulinsight-coop", "join" }).Drop);
    }
}
