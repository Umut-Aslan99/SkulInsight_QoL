using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using DamageInsight.SaveSlots;
using DamageInsight.UI;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Save slots (whole games): the slot folders, the summary text, the settings rows. Switching is tested in the game.</summary>
public class SaveSlotTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"save-slots-{Guid.NewGuid():N}");
    private readonly string _previousFolder = SlotStore.Folder;

    public SaveSlotTests()
    {
        SlotStore.Folder = _folder;
        SlotStore.Load();
    }

    public void Dispose()
    {
        SlotStore.Folder = _previousFolder;
        SlotStore.Load();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Store_TheGameYouHadIsSlot1_UntilYouSwitch()
    {
        Assert.Equal(1, SlotStore.Active);
        Assert.True(SlotStore.HasGame(1)); // the game being played, even without a slot file
        Assert.False(SlotStore.HasGame(2));

        var left = new DateTime(2026, 10, 11, 13, 0, 0, DateTimeKind.Local);
        SlotStore.WriteSave(1, "{\"IntData\":[]}");
        SlotStore.SetActive(2, left);
        SlotStore.Load(); // read back from slots.json
        Assert.Equal(2, SlotStore.Active);
        Assert.Equal(left, SlotStore.LastPlayedAt(1));
        Assert.Null(SlotStore.LastPlayedAt(2)); // being played
        Assert.Equal("{\"IntData\":[]}", SlotStore.ReadSave(1));
        Assert.Null(SlotStore.ReadSave(3));
    }

    [Fact]
    public void Store_ClearsWaitingSlots_NeverThePlayedOne()
    {
        SlotStore.WriteSave(3, "{}");
        Directory.CreateDirectory(Path.Combine(SlotStore.CodexPath(3), "Replays"));
        Assert.True(SlotStore.HasGame(3));
        SlotStore.Clear(3);
        Assert.False(SlotStore.HasGame(3));
        Assert.False(Directory.Exists(SlotStore.SlotFolder(3)));
        Assert.Throws<InvalidOperationException>(() => SlotStore.Clear(SlotStore.Active));
    }

    [Fact]
    public void Store_CopiesAFolderWithEverythingInIt()
    {
        string from = Path.Combine(_folder, "Codex");
        Directory.CreateDirectory(Path.Combine(from, "Replays", "Boss"));
        File.WriteAllText(Path.Combine(from, "progress.json"), "{}");
        File.WriteAllText(Path.Combine(from, "Replays", "Boss", "clip.jpg"), "x");
        string to = SlotStore.CodexPath(2);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(to, "old.txt"), "gone after the copy");

        SlotStore.CopyFolder(from, to);
        Assert.True(File.Exists(Path.Combine(to, "progress.json")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(to, "Replays", "Boss", "clip.jpg")));
        Assert.False(File.Exists(Path.Combine(to, "old.txt")));
    }

    private static SlotSummary.Lookup From(Dictionary<string, string> values) =>
        key => values.TryGetValue(key, out var v) ? v : null;

    [Fact]
    public void Summary_ANewGame()
    {
        var lines = SlotSummary.Lines(3, playing: false, null, From(new()), 0, (c, s, p) => "?");
        Assert.Equal(new[] { "Slot 3", "A new game: the tutorial isn't done yet." }, lines);
    }

    [Fact]
    public void Summary_AGameWithARunInProgress()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Generic/tutorialPlayed"] = "true",
            ["Generic/normalEnding"] = "true",
            ["Save/hasSave"] = "true",
            ["Save/chapterIndex"] = "4", ["Save/stageIndex"] = "1", ["Save/pathIndex"] = "2",
            ["Progress/foxRescued"] = "true", ["Progress/druidRescued"] = "true",
            ["Currency/darkQuartz/balance"] = "1234",
            ["HardmodeProgress/clearedCount"] = "2", ["HardmodeProgress/clearedLevel"] = "5",
            ["Progress/deaths"] = "41",
        };
        var witch = new List<(string, string)>
        {
            ("Progress/WitchMastery/Skull/0", "3"), ("Progress/WitchMastery/Body/1", "2"), ("Progress/deaths", "41"),
        };
        var left = new DateTime(2026, 10, 11, 13, 5, 0);
        var lines = SlotSummary.Lines(2, playing: false, left, From(values), SlotSummary.WitchLevels(witch),
            (c, s, p) => $"ch{c}.{s} room {p + 1}");
        Assert.Equal(new[]
        {
            "Slot 2",
            "Run in progress: ch4.1 room 3",
            "Story: the ending was reached.",
            "NPCs rescued: 2 of 4",
            "Witch upgrades: 5 levels",
            "Dark quartz: 1234",
            "Dark Mirror: cleared up to level 5",
            "Deaths: 41",
            "Last played: 2026-10-11 13:05",
        }, lines);
        Assert.Equal("Slot 1 (playing now)", SlotSummary.Lines(1, playing: true, null, From(values), 0, (c, s, p) => "")[0]);
    }

    [Fact]
    public void SettingsRows_SlotThenPlayCopyClear_WithAsManySlotsAsSet()
    {
        Directory.CreateDirectory(_folder);
        var config = new ConfigFile(Path.Combine(_folder, "settings.cfg"), saveOnInit: false);
        var slot = config.Bind("Save Slots", "Slot", 1, "The slot.");
        config.Bind("Save Slots", "Slots", 3, "How many.");

        var count = SettingsCatalog.SlotCount;
        var details = SettingsCatalog.SlotDetails;
        try
        {
            SettingsCatalog.SlotCount = () => 4;
            SettingsCatalog.SlotDetails = s => "slot " + s.ToString(CultureInfo.InvariantCulture);
            var items = SettingsCatalog.Build(config, new string[0]);
            var rows = items.Where(i => i.Section == "Save slots").ToList();

            Assert.Equal(new[] { "Slot", "Play this slot", "Copy my game here", "Clear this slot", "Number of slots" }, rows.Select(r => r.Label));
            Assert.Equal(new object[] { 1, 2, 3, 4 }, rows[0].Values());
            Assert.Equal(new[] { SettingsCatalog.SlotPlay, SettingsCatalog.SlotCopy, SettingsCatalog.SlotClear },
                rows.Skip(1).Take(3).Select(r => r.Action));
            Assert.Equal(new[] { "Play", "Copy", "Clear" }, rows.Skip(1).Take(3).Select(r => r.ButtonLabel));

            slot.Value = 3;
            Assert.StartsWith("slot 3", rows[0].CurrentHelp);
            Assert.EndsWith("slot 3", rows[1].CurrentHelp);
        }
        finally
        {
            SettingsCatalog.SlotCount = count;
            SettingsCatalog.SlotDetails = details;
        }
    }
}
