using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using DamageInsight.Codex;
using DamageInsight.UI;
using UnityEngine;
using Xunit;

namespace DamageInsight.Tests;

public class SettingsCatalogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"settings-catalog-{Guid.NewGuid():N}.cfg");
    private readonly ConfigFile _config;

    public SettingsCatalogTests()
    {
        _config = new ConfigFile(_path, saveOnInit: false);
        // A few entries like Plugin.Awake binds them, deliberately out of page order.
        _config.Bind("Codex", "ToggleKey", new KeyboardShortcut(KeyCode.K), "Key that opens and closes the Codex book.");
        _config.Bind("Damage Numbers", "ShowSourceTag", true, "Mark damage numbers.");
        _config.Bind("Damage Icons", "Burn", "inscription:Arson", "Icon.");
        _config.Bind("Damage Numbers", "HoldSeconds", 0.8f, "How long.");
        _config.Bind("Combat Log", "WindowRect", "", "Saved automatically.");
        _config.Bind("Combat Log", "BackgroundOpacity", 0.85f, "Opacity.");
        _config.Bind("Mini Log", "Lines", 4, "Number of lines (1-8).");
        CodexBalance.Bind(_config);
    }

    public void Dispose()
    {
        CodexBalance.Bind(new ConfigFile(_path + ".2", saveOnInit: false)); // don't leave the statics on our file
        foreach (var p in new[] { _path, _path + ".2" })
            if (File.Exists(p))
                File.Delete(p);
    }

    private SettingItem Row(string label) =>
        SettingsCatalog.Build(_config, new[] { "Arson", "Arms", "ExcessiveBleeding" }).Single(i => i.Label == label);

    [Fact]
    public void Features_GroupTheSettings_WithTheirMainSwitchOnTheLeft()
    {
        _config.Bind("Rewards", "Enabled", true, "Rewards.");
        _config.Bind("Rewards", "Coins", true, "Coins.");
        _config.Bind("Restart Room", "Enabled", true, "Restart.");
        var features = SettingsCatalog.Features(SettingsCatalog.Build(_config, new[] { "Arson" }));
        // Gameplay first (what changes the game), then what only shows, General last (Reset everything in it).
        Assert.Equal(new[] { "Gameplay", "Gameplay" }, features.Take(2).Select(f => f.Group));
        Assert.Equal("General", features.Last().Group);
        Assert.Equal(SettingsCatalog.AllSettings, features.Last().Key);

        var rewards = features.Single(f => f.Key == "Rewards");
        Assert.Equal("Enabled", rewards.Toggle.Entry.Definition.Key);   // the switch on the left
        Assert.Equal(new[] { "Coins" }, rewards.Items.Select(i => i.Entry.Definition.Key)); // the rest on the right
        Assert.Equal(rewards.Toggle.Help, rewards.Description);

        var numbers = features.Single(f => f.Key == "Damage Numbers");
        Assert.Equal("ShowSourceTag", numbers.Toggle.Entry.Definition.Key);
        Assert.Contains(numbers.Items, i => i.ConfigSection == "Damage Icons"); // the icons belong to the damage numbers
        Assert.DoesNotContain(features, f => f.Key == "Damage Icons");

        var log = features.Single(f => f.Key == "Combat Log");
        Assert.Null(log.Toggle);                                          // no main switch: a plain row
        Assert.NotEmpty(log.Description);
        Assert.Contains(log.Items, i => i.Action == SettingsCatalog.ResetWindow);
        Assert.Equal("Insight", log.Group);
        Assert.Single(features, f => f.Key == "Codex balance");           // the unlock groups are one feature
    }

    [Fact]
    public void Without_all_settings_only_what_most_players_change_shows()
    {
        _config.Bind("Rewards", "Enabled", true, "Rewards.");
        _config.Bind("Rewards", "Coins", true, "Coins.");
        _config.Bind("Rewards", "QuestGold", 50, "Gold.");
        _config.Bind(SettingsCatalog.AllSettings, "Show", false, "All.");
        var items = SettingsCatalog.Build(_config, new[] { "Arson" });
        Assert.True(items.Single(i => i.Label == "Mini Log Lines" || i.Entry?.Definition.Key == "Lines").Advanced);
        Assert.False(items.Single(i => i.Entry?.Definition.Key == "Coins").Advanced);

        var shortPage = SettingsCatalog.Features(items, all: false);
        var rewards = shortPage.Single(f => f.Key == "Rewards");
        Assert.Equal(new[] { "Coins" }, rewards.Items.Select(i => i.Entry.Definition.Key)); // not the quest gold
        var numbers = shortPage.Single(f => f.Key == "Damage Numbers");
        Assert.DoesNotContain(numbers.Items, i => i.ConfigSection == "Damage Icons" || i.Entry?.Definition.Key == "HoldSeconds");
        Assert.DoesNotContain(shortPage, f => f.Key == "Codex balance");   // nothing left to show: the feature goes
        Assert.DoesNotContain(shortPage, f => f.Key == "Mini Log");        // only its lines here, no switch bound
        var all = shortPage.Single(f => f.Key == SettingsCatalog.AllSettings);
        Assert.Equal("Show", all.Toggle.Entry.Definition.Key);              // the switch for the full page, always there
        Assert.Contains(all.Items, i => i.Action == SettingsCatalog.ResetAll);

        var fullPage = SettingsCatalog.Features(items, all: true);
        Assert.Contains(fullPage, f => f.Key == "Codex balance");
        Assert.Contains(fullPage.Single(f => f.Key == "Rewards").Items, i => i.Entry.Definition.Key == "QuestGold");
    }

    [Fact]
    public void Sections_FollowThePageOrder_AndEndWithResetEverything()
    {
        var items = SettingsCatalog.Build(_config, new string[0]);
        var sections = items.Select(i => i.Section).Distinct().ToList();

        Assert.Equal("Damage numbers", sections[0]);
        Assert.Equal("Damage icons", sections[1]);
        Assert.True(sections.IndexOf("Combat log") < sections.IndexOf("Codex"));
        Assert.True(sections.IndexOf("Codex") < sections.IndexOf("Codex unlocks: enemies"));
        Assert.Equal(new[] { "Codex unlocks: enemies", "Codex unlocks: bosses", "Codex unlocks: dark enemies", "Codex unlocks: gear" },
            sections.Where(s => s.StartsWith("Codex unlocks")));
        Assert.Equal(SettingsCatalog.ResetAll, items.Last().Action);
    }

    [Fact]
    public void WindowRect_IsReplacedByAResetRow()
    {
        var log = SettingsCatalog.Build(_config, new string[0]).Where(i => i.Section == "Combat log").ToList();

        Assert.DoesNotContain(log, i => i.Entry?.Definition.Key == "WindowRect");
        Assert.Contains(log, i => i.Kind == SettingKind.Action && i.Action == SettingsCatalog.ResetWindow);
    }

    [Fact]
    public void OnOff_GoesRound_NumbersStopAtTheEnds()
    {
        var tag = Row("Show source");
        Assert.Equal(SettingKind.Choice, tag.Kind);
        Assert.Equal(new[] { "Off", "On" }, tag.Values().Select(tag.Format));
        Assert.True(tag.Wraps);

        var hold = Row("Stay visible");
        Assert.False(hold.Wraps);
        Assert.Equal("0 s", hold.Format(hold.Values().First()));
        Assert.Equal("5 s", hold.Format(hold.Values().Last()));
        Assert.Equal("0.8 s", hold.Format(hold.Entry.BoxedValue));
        Assert.Equal("Default: 0.8 s", "Default: " + hold.DefaultText);
    }

    [Fact]
    public void Numbers_KeepTheirType()
    {
        Assert.All(Row("Stay visible").Values(), v => Assert.IsType<float>(v));
        var lines = Row("Lines");
        Assert.All(lines.Values(), v => Assert.IsType<int>(v));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => i.ToString()), lines.Values().Select(lines.Format));
    }

    [Fact]
    public void Percent_IsShownAsPercent()
    {
        var opacity = Row("Background");
        Assert.Equal("85%", opacity.Format(opacity.Entry.BoxedValue));
        Assert.Equal(21, opacity.Values().Count); // 0%, 5%, ... 100%
    }

    [Fact]
    public void HandEditedValue_IsKeptInOrder()
    {
        var hold = Row("Stay visible");
        hold.Entry.BoxedValue = 0.85f;

        var values = hold.Values();
        int at = SettingItem.IndexOf(values, hold.Entry.BoxedValue);
        Assert.Equal("0.85 s", hold.Format(values[at]));
        Assert.Equal("0.8 s", hold.Format(values[at - 1]));
        Assert.Equal("0.9 s", hold.Format(values[at + 1]));
    }

    [Fact]
    public void Keys_AreCapturedRows()
    {
        var key = Row("Open / close key");
        Assert.Equal(SettingKind.Key, key.Kind);
        Assert.Equal("K", key.Format(key.Entry.BoxedValue));
        Assert.Contains("press the new key", key.Help);
    }

    [Fact]
    public void Icons_OfferTheTextTagAndEveryInscription()
    {
        var burn = Row("Burn");
        Assert.True(burn.ShowsIcon);
        Assert.Equal(new[] { "Text tag", "Arms", "Arson", "Excessive bleeding" }, burn.Values().Select(burn.Format));
        Assert.Equal("Arson", burn.Format(burn.Entry.BoxedValue));

        burn.Entry.BoxedValue = "skill:Fireball"; // typed into the config by hand
        Assert.Equal("Fireball (skill)", burn.Format(burn.Values().First()));
    }

    [Fact]
    public void CodexBalance_UsesItsOwnRanges()
    {
        var max = SettingsCatalog.Build(_config, new string[0]).Single(i => i.Entry?.Definition.Key == "EnemyMax3");
        var values = max.Values().Select(v => Convert.ToDouble(v)).ToList();

        Assert.Equal(CodexBalance.EnemyMax3.Min, values.First());
        Assert.Equal(CodexBalance.EnemyMax3.Max, values.Last());
        Assert.Contains(CodexBalance.EnemyMax3.Default, values);
        Assert.True(values.Count <= 60, $"{values.Count} steps: too many to hold an arrow through");

        // Changing it on the page changes the Codex numbers.
        max.Entry.BoxedValue = 100f;
        Assert.Equal(100f, CodexBalance.EnemyMax3.Value);
    }

    [Theory]
    [InlineData(0, 5, 0.1, 51)]
    [InlineData(0.2, 10, 0.2, 50)]
    [InlineData(1, 8, 1, 8)]
    public void NumberSteps_ShortRanges_StepEvenly(double min, double max, double step, int count)
    {
        var steps = SettingsCatalog.NumberSteps(min, max, step);
        Assert.Equal(count, steps.Count);
        Assert.Equal(min, steps.First(), 6);
        Assert.Equal(max, steps.Last(), 6);
        for (int i = 1; i < steps.Count; i++)
            Assert.Equal(step, steps[i] - steps[i - 1], 6);
    }

    [Theory]
    [InlineData(10, 5000, 10)]
    [InlineData(1, 200, 1)]
    [InlineData(1, 100, 1)]
    public void NumberSteps_LongRanges_GetCoarserButStayRound(double min, double max, double step)
    {
        var steps = SettingsCatalog.NumberSteps(min, max, step);
        Assert.True(steps.Count <= 40, $"{steps.Count} steps");
        Assert.Equal(min, steps.First());
        Assert.Equal(max, steps.Last());
        for (int i = 1; i < steps.Count; i++)
            Assert.True(steps[i] > steps[i - 1]);
        Assert.All(steps, s => Assert.Equal(0, s % step, 6));
        Assert.Contains(10, steps.Where(s => s >= 10)); // the small numbers stay one by one up to 10
    }

    [Theory]
    [InlineData("ShowSourceTag", "Show source tag")]
    [InlineData("ExcessiveBleeding", "Excessive bleeding")]
    [InlineData("HPBar", "HP bar")]
    [InlineData("ShowUI", "Show UI")]
    [InlineData("DarkAbility", "Dark ability")]
    public void Words_SplitsKeys(string key, string words) => Assert.Equal(words, SettingsCatalog.Words(key));

    /// <summary>Every number Plugin.cs binds needs a label and a range on the page (else it gets a guessed one).</summary>
    [Fact]
    public void EveryNumberInThePluginHasARange()
    {
        string source = File.ReadAllText(FindRepoFile(Path.Combine("DamageInsight", "Plugin.cs")));
        var missing = Regex.Matches(source, @"Config\.Bind\(""(?<section>[^""]+)"",\s*""(?<key>[^""]+)"",\s*(?<value>[^,]+),")
            .Cast<Match>()
            .Where(m => Regex.IsMatch(m.Groups["value"].Value.Trim(), @"^-?\d+(\.\d+)?f?$"))
            .Select(m => m.Groups["section"].Value + "/" + m.Groups["key"].Value)
            .Where(k => !SettingsCatalog.HasSpec(k.Split('/')[0], k.Split('/')[1]))
            .ToList();
        Assert.True(missing.Count == 0, "No range for: " + string.Join(", ", missing));
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
