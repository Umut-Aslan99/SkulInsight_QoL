using System.Linq;
using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The changelog in the game (0.16.1, Tythos + Umut 2026-11-01).</summary>
public class ChangelogTests
{
    [Fact]
    public void Every_version_is_there_newest_first_and_the_newest_is_this_one()
    {
        var versions = Changelog.Versions();
        // A new version needs its entry: the newest is the mod's own version (DamageInsight.csproj; the test project has a
        // MyPluginInfo of its own, so the mod's is read from its assembly).
        var info = typeof(Changelog).Assembly.GetType("DamageInsight.MyPluginInfo");
        Assert.Equal((string)info.GetField("PLUGIN_VERSION").GetValue(null), versions[0].version);
        Assert.Equal("0.9.0", versions.Last().version);                       // all of them, back to the first beta
        for (int i = 1; i < versions.Count; i++)
            Assert.True(Changelog.Newer(versions[i - 1].version, versions[i].version), versions[i].version);
        Assert.All(versions, v => Assert.NotEmpty(v.lines));
        Assert.Equal(18, versions.Count);
    }

    [Fact]
    public void What_is_new_for_the_player_goes_in_the_box()
    {
        Assert.True(Changelog.Newer("0.16.1", "0.16.0"));
        Assert.False(Changelog.Newer("0.16.0", "0.16.0"));
        Assert.True(Changelog.Newer("0.10.0", "0.9.3"));                       // numbers, not text
        var (boxed, earlier) = Changelog.Split("0.15.0");                      // came from 0.15: both 0.16 versions
        Assert.Equal(new[] { "0.16.1", "0.16.0" }, boxed.Select(v => v.version));
        Assert.Equal("0.15.0", earlier[0].version);
        Assert.Single(Changelog.Split("0.16.1").boxed);                       // the settings page: the newest alone
        Assert.Single(Changelog.Split("").boxed);
        Assert.Equal(Changelog.Versions().Count, boxed.Count + earlier.Count);
        string text = Changelog.Text(boxed);
        Assert.Contains("0.16.1", text);
        Assert.Contains("Restart room (F5)", text);
    }

    [Fact]
    public void The_changelog_has_its_own_row_on_the_short_settings_page()
    {
        Assert.Contains("action:" + SettingsCatalog.OpenChangelog, SettingsCatalog.Basic);
    }
}
