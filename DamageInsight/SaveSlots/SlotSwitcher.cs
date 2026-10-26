using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DamageInsight.Codex;
using DamageInsight.Lang;
using Data;
using GameResources;
using Level;
using Platforms;
using Scenes;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DamageInsight.SaveSlots;

/// <summary>
/// Plays another save slot (docs/SAVE_SLOTS.md): the game being played is kept in its slot (its run too, as the game
/// stored it when you entered the room), the other slot's game and Codex come in, and the game goes back to the title
/// screen like its own "return to title", only without ending the run. An empty slot starts a new game.
/// </summary>
internal static class SlotSwitcher
{
    /// <summary>True while a switch or a copy is under way.</summary>
    public static bool Busy { get; private set; }

    public static int SlotCount => Mathf.Clamp(Plugin.SaveSlotCount.Value, 3, SlotStore.MaxSlots);

    /// <summary>The slot the settings page's buttons use (1-based).</summary>
    public static int Selected => Mathf.Clamp(Plugin.SaveSlotSelected.Value, 1, SlotCount);

    private static SaveFileModel Model => PersistentSingleton<PlatformManager>.Instance.platform.data;

    private static string CodexFolder => CodexTracker.Folder;

    /// <summary>What the settings page's save slot rows show (the catalog itself is plain logic).</summary>
    public static void ConnectSettingsPage()
    {
        UI.SettingsCatalog.SlotCount = () => SlotCount;
        UI.SettingsCatalog.SlotValue = slot => slot == SlotStore.Active ? Loc.F("{0} (playing)", slot)
            : SlotStore.HasGame(slot) ? slot.ToString(CultureInfo.InvariantCulture) : Loc.F("{0} (empty)", slot);
        UI.SettingsCatalog.SlotDetails = Details;
    }

    // ---------------------------------------------------------------- reading a game save

    /// <summary>Every value of a game save as (key, value text).</summary>
    private static List<(string key, string value)> ValuesOf(SaveFileModel model)
    {
        var values = new List<(string, string)>();
        values.AddRange(model.StringData.Select(i => (i.Key, i.Value ?? "")));
        values.AddRange(model.IntData.Select(i => (i.Key, i.Value.ToString(CultureInfo.InvariantCulture))));
        values.AddRange(model.FloatData.Select(i => (i.Key, i.Value.ToString("R", CultureInfo.InvariantCulture))));
        values.AddRange(model.BoolData.Select(i => (i.Key, i.Value ? "true" : "false")));
        values.AddRange(model.UlongData.Select(i => (i.Key, i.Value.ToString(CultureInfo.InvariantCulture))));
        return values;
    }

    /// <summary>The slot's details for the help panel (several lines).</summary>
    public static string Details(int slot)
    {
        try
        {
            SaveFileModel model;
            if (slot == SlotStore.Active)
                model = Model;
            else
            {
                string json = SlotStore.ReadSave(slot);
                if (json == null)
                    return Loc.F("Slot {0} is empty. Play it to start a new game (intro and tutorial); your other slots stay as they are.", slot);
                model = SaveFileModel.FromJson(json);
            }
            var values = ValuesOf(model);
            var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values)
                byKey[key] = value;
            var lines = SlotSummary.Lines(slot, slot == SlotStore.Active, SlotStore.LastPlayedAt(slot),
                key => byKey.TryGetValue(key, out var v) ? v : null, SlotSummary.WitchLevels(values), Place);
            return string.Join("\n", lines);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Save slots: slot {slot} can't be read: {e.Message}");
            return Loc.F("Slot {0} can't be read.", slot);
        }
    }

    /// <summary>"Chapter name · stage name, room 4" in the game's language.</summary>
    public static string Place(int chapterIndex, int stage, int path)
    {
        string chapterKey, stageKey;
        if (chapterIndex == (int)Chapter.Type.HardmodeChapter6)
        {
            chapterKey = "map/chapter6";
            stageKey = "map/chapter6/0";
        }
        else
        {
            // Dark Mirror chapters use the normal chapters' names (as the game's own stage title does).
            var type = (Chapter.Type)(chapterIndex >= (int)Chapter.Type.HardmodeChapter1 ? chapterIndex - 6 : chapterIndex);
            chapterKey = $"map/{type}";
            stageKey = $"map/{type}/{stage}";
        }
        string chapter = Localization.TryGetLocalizedString(chapterKey, out var c) && !string.IsNullOrWhiteSpace(c) ? c : Loc.F("Chapter {0}", chapterIndex);
        string stageName = Localization.TryGetLocalizedString(stageKey, out var s) && !string.IsNullOrWhiteSpace(s) ? s : "";
        return Loc.F("{0}, room {1}", stageName.Length > 0 ? chapter + " · " + stageName : chapter, path + 1);
    }

    // ---------------------------------------------------------------- switching

    /// <summary>Why <paramref name="slot"/> can't be played now (a message for the player), or null.</summary>
    public static string CannotSwitch(int slot)
    {
        if (Busy)
            return Loc.T("Please wait: the save slots are busy.");
        if (slot == SlotStore.Active)
            return Loc.F("Slot {0} is the game you are playing.", slot);
        // The daily run borrows the Codex of the slot it came from: it goes back there, and only there.
        if (SlotStore.Active == SlotStore.DailySlot && slot != DamageInsight.Daily.DailyRun.Origin)
            return Loc.T("You are playing today's daily run: leave it first (Codex, Daily).");
        if (Singleton<Service>.Instance?.levelManager == null || Scene<GameBase>.instance == null)
            return Loc.T("Switch save slots during the game (castle or run).");
        return null;
    }

    /// <summary>
    /// Plays <paramref name="slot"/>. <paramref name="closeMenus"/> closes the page that asked (the pause menu).
    /// Returns a message when it can't.
    /// </summary>
    public static string Switch(int slot, Action closeMenus) => Switch(slot, closeMenus, keepCodex: false);

    /// <summary>
    /// <paramref name="keepCodex"/>: the Codex folder stays where it is (the daily slot borrows the Codex of the slot it
    /// is entered from, and gives it back when it is left).
    /// </summary>
    public static string Switch(int slot, Action closeMenus, bool keepCodex)
    {
        string why = CannotSwitch(slot);
        if (why != null)
            return why;
        Busy = true;
        int from = SlotStore.Active;
        bool codexMoved = false;
        try
        {
            closeMenus?.Invoke();
            CodexTracker.SaveIfNeeded(force: true);
            FightRecorder.ForgetSlot();
            DamageInsight.History.RunJournal.ForgetSlot();
            BackupGameSave();

            // 1. The Codex: this slot's folder goes to the slot, the next slot's folder (if any) takes its place.
            if (!keepCodex)
            {
                FightRecorder.WhileNoFilmIsSaved(() =>
                {
                    Directory.CreateDirectory(SlotStore.SlotFolder(from));
                    if (Directory.Exists(SlotStore.CodexPath(from)))
                        Directory.Delete(SlotStore.CodexPath(from), recursive: true);
                    if (Directory.Exists(CodexFolder))
                        Directory.Move(CodexFolder, SlotStore.CodexPath(from));
                    codexMoved = true;
                    if (Directory.Exists(SlotStore.CodexPath(slot)))
                        Directory.Move(SlotStore.CodexPath(slot), CodexFolder);
                });
            }

            // 2. The game save: kept in the slot being left; the next slot's comes in (or a new game). Everything that
            // can fail (files) happens before the game's save data in memory is replaced.
            var live = Model;
            SlotStore.WriteSave(from, live.ToJson());
            string json = SlotStore.ReadSave(slot);
            var next = json != null ? SaveFileModel.FromJson(json) : new SaveFileModel();
            SlotStore.SetActive(slot, DateTime.Now);

            // The game's own settings (keys, sound, language) stay as they are.
            ReplaceKeepingSettings(live.StringData, next.StringData, i => i.Key);
            ReplaceKeepingSettings(live.IntData, next.IntData, i => i.Key);
            ReplaceKeepingSettings(live.FloatData, next.FloatData, i => i.Key);
            ReplaceKeepingSettings(live.BoolData, next.BoolData, i => i.Key);
            ReplaceKeepingSettings(live.UlongData, next.UlongData, i => i.Key);
            PersistentSingleton<PlatformManager>.Instance.SaveDataToFile();
            Plugin.Log.LogInfo($"Save slots: now playing slot {slot} ({(json == null ? "new game" : "its saved game")}); slot {from} kept.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Save slots: switching to slot {slot} failed: {e}");
            // Before the slot marker moved, nothing of the game changed: put the Codex folders back as they were.
            if (codexMoved && SlotStore.Active == from)
                RestoreCodex(from, slot);
            Busy = false;
            return Loc.F("Switching to slot {0} failed; nothing was changed. See BepInEx/LogOutput.log.", slot);
        }

        ForgetCodex();
        ReturnToTitle();
        Busy = false;
        return null;
    }

    private static void RestoreCodex(int from, int slot)
    {
        try
        {
            FightRecorder.WhileNoFilmIsSaved(() =>
            {
                if (Directory.Exists(CodexFolder) && !Directory.Exists(SlotStore.CodexPath(slot)))
                    Directory.Move(CodexFolder, SlotStore.CodexPath(slot));
                if (Directory.Exists(SlotStore.CodexPath(from)) && !Directory.Exists(CodexFolder))
                    Directory.Move(SlotStore.CodexPath(from), CodexFolder);
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Save slots: could not put the Codex folders back: " + e.Message);
        }
    }

    /// <summary>The live save's values become <paramref name="next"/>'s, except the game's settings (keys "Settings/...").</summary>
    private static void ReplaceKeepingSettings<T>(List<T> live, List<T> next, Func<T, string> key)
    {
        static bool Setting(string k) => k != null && k.StartsWith("Settings/", StringComparison.OrdinalIgnoreCase);
        var keep = live.Where(i => Setting(key(i))).ToList();
        var incoming = next.Where(i => !Setting(key(i))).ToList();
        live.Clear();
        live.AddRange(incoming);
        live.AddRange(keep);
    }

    /// <summary>Everything the mod keeps in memory about the Codex is read again from the next slot's folder.</summary>
    private static void ForgetCodex()
    {
        Patches.Guard.Run("Save slots (Codex)", () =>
        {
            CodexTracker.ForgetSlot();
            DamageInsight.Achievements.AchievementStore.ForgetSlot();
            DamageInsight.Rewards.RewardStore.ForgetSlot();
            CodexAnimations.ForgetSlot();
            CodexPortraits.ForgetSlot();
            MoveHints.ForgetSlot();
            RunRecorder.ForgetSlot();
            CodexWindow.ForgetSlot();
            CodexMigrations.Run(); // a slot's Codex may come from an older version of the mod
        });
    }

    /// <summary>
    /// The game's own "return to title" (Service.ResetGameScene) without ending the run, so the next slot's run
    /// continues after "press any key". The chapter is loaded afresh then (its stages come from that run's seed).
    /// </summary>
    private static void ReturnToTitle()
    {
        var service = Singleton<Service>.Instance;
        var levels = service.levelManager;
        Patches.Guard.Run("Save slots (leave the game)", () =>
        {
            GameData.Generic.tutorial.Stop();
            PersistentSingleton<SoundManager>.Instance.StopBackGroundMusic();
            levels.Reset();
        });
        levels._currentChapterIndex = -1;
        SceneManager.LoadScene("Main");
        levels.ClearEvents();
        GameData.Initialize();
        PoolObject.DespawnAll();
        PoolObject.Clear();
        PersistentSingleton<PlatformManager>.Instance.SaveDataToFile();
    }

    /// <summary>A copy of the game's save files before every switch (SaveSlots/GameSaveBackup, the 5 newest are kept).</summary>
    private static void BackupGameSave()
    {
        try
        {
            string backups = Path.Combine(SlotStore.Folder, "GameSaveBackup");
            var files = Directory.GetFiles(Application.persistentDataPath, "*.dat");
            if (files.Length == 0)
                return;
            string target = Path.Combine(backups, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(target);
            foreach (var file in files)
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            foreach (var old in Directory.GetDirectories(backups).OrderByDescending(d => d, StringComparer.Ordinal).Skip(5))
                Directory.Delete(old, recursive: true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Save slots: could not back up the game's save: " + e.Message);
        }
    }

    // ---------------------------------------------------------------- copying and clearing

    /// <summary>Why the current game can't be copied into <paramref name="slot"/> now, or null.</summary>
    public static string CannotCopy(int slot)
    {
        if (Busy)
            return Loc.T("Please wait: the save slots are busy.");
        if (slot == SlotStore.Active)
            return Loc.F("Slot {0} is the game you are playing.", slot);
        return null;
    }

    /// <summary>
    /// Copies the game being played (and its Codex) into <paramref name="slot"/>, replacing what it held. The Codex
    /// folder is copied in the background; <paramref name="done"/> gets the message for the player.
    /// </summary>
    public static void Copy(int slot, Action<string> done)
    {
        string why = CannotCopy(slot);
        if (why != null)
        {
            done(why);
            return;
        }
        Busy = true;
        CodexTracker.SaveIfNeeded(force: true);
        string json = Model.ToJson();
        string codex = CodexFolder;
        var task = Task.Run(() =>
        {
            FightRecorder.WhileNoFilmIsSaved(() => SlotStore.CopyFolder(codex, SlotStore.CodexPath(slot)));
            SlotStore.WriteSave(slot, json);
        });
        CoroutineProxy.instance.StartCoroutine(WaitForCopy(task, slot, done));
    }

    private static IEnumerator WaitForCopy(Task task, int slot, Action<string> done)
    {
        while (!task.IsCompleted)
            yield return null;
        Busy = false;
        if (task.IsFaulted)
        {
            Plugin.Log.LogError($"Save slots: copying into slot {slot} failed: {task.Exception?.GetBaseException()}");
            done(Loc.F("Copying into slot {0} failed. See BepInEx/LogOutput.log.", slot));
            yield break;
        }
        Plugin.Log.LogInfo($"Save slots: the game being played was copied into slot {slot}.");
        done(Loc.F("Your game was copied into slot {0}.", slot));
    }

    public static string Clear(int slot)
    {
        string why = CannotCopy(slot);
        if (why != null)
            return why;
        if (!SlotStore.HasGame(slot))
            return Loc.F("Slot {0} is empty.", slot);
        SlotStore.Clear(slot);
        Plugin.Log.LogInfo($"Save slots: slot {slot} cleared.");
        return Loc.F("Slot {0} is empty now.", slot);
    }
}
