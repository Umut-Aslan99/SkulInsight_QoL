using System.Collections.Generic;
using System.Linq;
using Characters;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

public class LogFeedTests
{
    private readonly List<DamageRecord> _records = new();
    private long _total;
    private int _clears;

    private void Record(params DamageRecord[] hits)
    {
        _records.AddRange(hits);
        _total += hits.Length;
    }

    private static LogFeed AllRoomsFeed() => new(new LogFilter { Scope = LogScope.All });

    [Fact]
    public void Update_OnlyAddsNewHits()
    {
        var feed = AllRoomsFeed();
        Record(Hit.Make(amount: 1), Hit.Make(amount: 2));
        feed.Update(_records, _total, _clears, 1);
        int rebuilds = feed.Rebuilds;

        Record(Hit.Make(amount: 3));
        feed.Update(_records, _total, _clears, 1);

        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, feed.Shown.Select(r => r.Amount));
        Assert.Equal(rebuilds, feed.Rebuilds); // appended, not rebuilt
    }

    [Fact]
    public void Version_ChangesOnlyWhenSomethingWasAdded()
    {
        var feed = AllRoomsFeed();
        Record(Hit.Make());
        feed.Update(_records, _total, _clears, 1);
        int version = feed.Version;

        feed.Update(_records, _total, _clears, 1);
        Assert.Equal(version, feed.Version);

        Record(Hit.Make(attacker: EntityKind.Boss, target: EntityKind.Player)); // filtered out (Dealt)
        feed.Update(_records, _total, _clears, 1);
        Assert.Equal(version, feed.Version);
    }

    [Fact]
    public void FilterChange_RebuildsFromScratch()
    {
        var feed = AllRoomsFeed();
        Record(Hit.Make(amount: 5, crit: false), Hit.Make(amount: 7, crit: true));
        feed.Update(_records, _total, _clears, 1);

        feed.Filter.CritsOnly = true;
        feed.Filter.Changed();
        feed.Update(_records, _total, _clears, 1);

        Assert.Equal(new[] { 7.0 }, feed.Shown.Select(r => r.Amount));
        Assert.Equal(7.0, feed.TotalBySource[DamageSource.Basic]);
    }

    [Fact]
    public void RoomScope_StartsOverInANewRoom()
    {
        var feed = new LogFeed(new LogFilter { Scope = LogScope.Room });
        Record(Hit.Make(room: 1));
        feed.Update(_records, _total, _clears, 1);
        Assert.Single(feed.Shown);

        Record(Hit.Make(room: 2, amount: 9));
        feed.Update(_records, _total, _clears, 2);

        Assert.Equal(new[] { 9.0 }, feed.Shown.Select(r => r.Amount));
    }

    [Fact]
    public void ClearingTheLog_EmptiesTheFeed()
    {
        var feed = AllRoomsFeed();
        Record(Hit.Make());
        feed.Update(_records, _total, _clears, 1);

        _records.Clear();
        _clears++;
        feed.Update(_records, _total, _clears, 1);

        Assert.Empty(feed.Shown);
        Assert.Empty(feed.TotalBySource);
    }

    [Fact]
    public void TrimmedRecords_DoNotConfuseTheFeed()
    {
        var feed = AllRoomsFeed();
        Record(Hit.Make(amount: 1), Hit.Make(amount: 2));
        feed.Update(_records, _total, _clears, 1);

        // The log drops its oldest entry and records a new one.
        _records.RemoveAt(0);
        Record(Hit.Make(amount: 3));
        feed.Update(_records, _total, _clears, 1);

        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, feed.Shown.Select(r => r.Amount));
    }

    [Fact]
    public void Totals_MatchTheShownHits()
    {
        var feed = AllRoomsFeed();
        Record(Hit.Make(amount: 10, source: DamageSource.Skill, attribute: Damage.Attribute.Magic),
               Hit.Make(amount: 30, source: DamageSource.Skill, attribute: Damage.Attribute.Physical),
               Hit.Make(amount: 60, source: DamageSource.Item, attribute: Damage.Attribute.Magic));
        feed.Update(_records, _total, _clears, 1);

        Assert.Equal(40.0, feed.TotalBySource[DamageSource.Skill]);
        Assert.Equal(70.0, feed.TotalByAttribute[Damage.Attribute.Magic]);

        var shares = DamageStats.FromTotals(feed.TotalBySource);
        Assert.Equal(DamageSource.Item, shares[0].Key);
        Assert.Equal(60.0, shares[0].Percent, precision: 6);
    }
}
