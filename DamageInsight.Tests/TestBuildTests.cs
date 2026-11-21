using System;
using DamageInsight.Coop;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The private test build's date from the server (Coop/TestBuild.cs).</summary>
public class TestBuildTests
{
    [Fact]
    public void The_servers_date_is_read_from_the_dailies_answer()
    {
        Assert.True(TestBuild.TryParseServerDate("{\"date\":\"2026-11-17\",\"formula\":4,\"boards\":[]}", out var date));
        Assert.Equal(new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc), date.ToUniversalTime());
        Assert.False(TestBuild.TryParseServerDate("{\"detail\":\"rate\"}", out _));
        Assert.False(TestBuild.TryParseServerDate(null, out _));
    }

    [Fact]
    public void A_normal_build_is_no_test_build_and_may_always_start()
    {
        Assert.False(TestBuild.IsTestBuild);
        Assert.False(TestBuild.Expired);
        Assert.True(TestBuild.MayStart);
    }
}
