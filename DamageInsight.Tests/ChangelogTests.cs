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
        Assert.True(versions.Count >= 19);
    }

    [Fact]
    public void What_is_new_for_the_player_goes_in_the_box()
    {
        Assert.True(Changelog.Newer("0.16.1", "0.16.0"));
        Assert.False(Changelog.Newer("0.16.0", "0.16.0"));
        Assert.True(Changelog.Newer("0.10.0", "0.9.3"));                       // numbers, not text
        var (boxed, earlier) = Changelog.Split("0.15.0");                      // came from 0.15: every 0.16 version
        Assert.Equal(new[] { "0.16.2", "0.16.1", "0.16.0" }, boxed.Select(v => v.version).Skip(boxed.Count - 3));
        Assert.All(boxed, v => Assert.True(Changelog.Newer(v.version, "0.15.0")));
        Assert.Equal("0.15.0", earlier[0].version);
        Assert.Single(Changelog.Split(Changelog.Versions()[0].version).boxed); // the settings page: the newest alone
        Assert.Single(Changelog.Split("").boxed);
        Assert.Equal(Changelog.Versions().Count, boxed.Count + earlier.Count);
        string text = Changelog.Text(boxed);
        Assert.Contains("0.16.1", text);
        Assert.Contains("Restart room (F5)", text);
    }

    [Fact]
    public void The_name_box_starts_with_a_name_and_steam_is_the_last_choice()
    {
        // Umut 2026-11-02: three players sent no name and showed as "Skul"; the box now starts with the Steam name.
        Assert.Equal("DocRun", Daily.DailyRun.StartingName("", "", "DocRun"));
        Assert.Equal("Tythos", Daily.DailyRun.StartingName("Tythos", "Old", "DocRun"));    // the config's name first
        Assert.Equal("Old", Daily.DailyRun.StartingName(null, "Old", "DocRun"));           // then the last one sent
        Assert.Equal("DocRun", Daily.DailyRun.StartingName("Skul", "Skul", " DocRun "));   // "Skul" was an empty box
        Assert.Equal("Twelve Chars", Daily.DailyRun.StartingName("", "", "Twelve Chars and more"));
        Assert.Equal("", Daily.DailyRun.StartingName("", null, ""));
    }

    [Fact]
    public void The_changelog_has_its_own_row_on_the_short_settings_page()
    {
        Assert.Contains("action:" + SettingsCatalog.OpenChangelog, SettingsCatalog.Basic);
    }
}
