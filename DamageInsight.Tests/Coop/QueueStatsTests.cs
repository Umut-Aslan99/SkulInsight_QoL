using System;
using DamageInsight.Coop.Match;
using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Umut 2026-11-23: who searches before queuing, the busiest time on the player's clock, why nobody matches
/// (other mods, another version), and the update notice's version from Thunderstore.</summary>
public class QueueStatsTests
{
    private const string Stats = "{\"looking\": 3, \"inMatch\": 2, \"byMode\": {\"normal\": 1, \"darkmirror\": 2, \"any\": 0}, " +
                                 "\"byRegion\": {\"eu\": 3}, \"matchesLastHour\": 1, \"hoursDays\": 14, " +
                                 "\"hoursUtc\": [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,5,5,1,0,0]}";

    [Fact]
    public void The_queues_counts_are_read()
    {
        var s = QueueStats.Parse(Stats);
        Assert.NotNull(s);
        Assert.Equal((3, 2, 1, 2, 0, 1, 14), (s.Looking, s.InMatch, s.Normal, s.DarkMirror, s.Any, s.MatchesLastHour, s.HoursDays));
        Assert.Equal(19, s.BusiestUtcHour); // the earliest of two equal hours
    }

    [Fact]
    public void An_older_or_switched_off_server_gives_what_it_can()
    {
        Assert.Null(QueueStats.Parse("{\"detail\": \"coop_off\"}"));
        Assert.Null(QueueStats.Parse("not json"));
        var old = QueueStats.Parse("{\"looking\": 0, \"byMode\": {\"normal\": 0, \"darkmirror\": 0, \"any\": 0}}");
        Assert.NotNull(old);
        Assert.Null(old.HoursUtc);
        Assert.Equal(-1, old.BusiestUtcHour);
        var empty = QueueStats.Parse("{\"looking\": 0, \"hoursUtc\": [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]}");
        Assert.Equal(-1, empty.BusiestUtcHour); // nobody came: no busiest time
    }

    [Fact]
    public void The_busiest_hour_is_shown_on_the_players_clock()
    {
        var day = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("19:00–20:00", QueueStats.LocalHourSpan(19, day, TimeZoneInfo.Utc));
        var berlin = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
        Assert.Equal("20:00–21:00", QueueStats.LocalHourSpan(19, day, berlin));
        Assert.Equal("00:00–01:00", QueueStats.LocalHourSpan(23, day, berlin)); // over midnight
        var india = TimeZoneInfo.CreateCustomTimeZone("Test+5:30", new TimeSpan(5, 30, 0), "Test+5:30", "Test+5:30");
        Assert.Equal("00:30–01:30", QueueStats.LocalHourSpan(19, day, india)); // a half-hour zone keeps its minutes
    }

    [Fact]
    public void A_waiting_ticket_hears_who_is_kept_apart_by_mods_or_version()
    {
        var s = MatchStatus.Parse("{\"state\": \"queued\", \"version\": 4, \"looking\": 3, \"otherMods\": 1, \"otherVersion\": 2}");
        Assert.Equal((3, 1, 2), (s.Looking, s.OtherMods, s.OtherVersion));
        var old = MatchStatus.Parse("{\"state\": \"queued\", \"version\": 4, \"looking\": 3}");
        Assert.Equal((0, 0), (old.OtherMods, old.OtherVersion)); // an older server
    }

    [Fact]
    public void The_update_notice_reads_thunderstores_newest_version()
    {
        Assert.Equal("0.17.1", UpdateNotice.LatestIn("{\"name\": \"SkulInsight_QoL\", \"latest\": {\"version_number\": \"0.17.1\", \"download_url\": \"https://thunderstore.io/x\"}}"));
        Assert.Null(UpdateNotice.LatestIn("{\"latest\": {\"version_number\": \"soon\"}}"));
        Assert.Null(UpdateNotice.LatestIn("<html>"));
        Assert.True(DamageInsight.Mods.ThunderstoreCatalog.IsNewer("0.17.1", "0.17.0"));
        Assert.False(DamageInsight.Mods.ThunderstoreCatalog.IsNewer("0.17.0", "0.17.0"));
        Assert.True(DamageInsight.Mods.ThunderstoreCatalog.IsNewer("0.17.10", "0.17.9"));
    }
}
