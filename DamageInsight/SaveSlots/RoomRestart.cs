using System;
using System.Collections;
using System.Linq;
using DamageInsight.Daily;
using DamageInsight.Lang;
using DamageInsight.UI;
using Data;
using Level;
using Platforms;
using Scenes;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.SaveSlots;

/// <summary>
/// Restart room (0.16; Tythos's viewers through Umut 2026-10-28: newer players "take a lot of damage in some rooms and
/// need to save-scum. Having to quit the entire game each time is really inconvenient"). The game saves a run each time
/// a room is entered (Chapter: currencies, progress, then the file; LevelManager.Save: health, gear, the place), and
/// closing the game and continuing starts that room again. This does the same without closing the game: the game's data
/// as it was saved at the room's start goes back in place, the game leaves to the title without ending the run (the
/// save slot switch's way) and continues at once (its own Continue, without "press any key"). The key (F5, pressed
/// twice within 3 seconds) does it; no menu button (Umut 2026-10-30: "It kind of just works with F5").
/// SkulInsight's rewards go back to the room's start too (no quest farming); the run history notes each restart.
/// A daily run can't be restarted: like leaving the game, it ends where it was (DailyRun.RefuseContinue) and goes on
/// the board as it was (Umut: "save scumming on daily run should be treated the same as when the player leaves").
/// </summary>
public static class RoomRestart
{
    private static string _save, _rewards;
    private static int _seed, _slot = -1;
    private static bool _restarting;
    private static float _keyArmedUntil = -1f;

    private static bool Enabled => Plugin.RoomRestartEnabled?.Value != false;

    /// <summary>
    /// The game saved the room that is being entered (Harmony postfix on LevelManager.InvokeOnMapChanged, which the
    /// game calls right after writing its save file): its data is kept to go back to.
    /// </summary>
    public static void OnRoomSaved()
    {
        if (!Enabled)
            return;
        var levels = Singleton<Service>.Instance?.levelManager;
        var chapter = levels?.currentChapter;
        var save = GameData.Save.instance;
        if (chapter == null || !InRunChapter(chapter.type) || save == null || !save.hasSave)
        {
            _save = _rewards = null;
            return;
        }
        _save = SlotSwitcher.Model.ToJson();
        _seed = save.randomSeed;
        _slot = SlotStore.Active;
        _rewards = Rewards.RewardStore.RoomSnapshot();
    }

    private static bool InRunChapter(Chapter.Type type) =>
        type is not (Chapter.Type.Castle or Chapter.Type.HardmodeCastle or Chapter.Type.Tutorial or Chapter.Type.Test);

    /// <summary>Restarting a room now would end today's daily run.</summary>
    public static bool EndsDailyRun => DailyRun.InDailySlot && DailyService.State.Tries.Values.Any(t => t.State == "started");

    /// <summary>Why the room can't be restarted now (a message for the player), or null.</summary>
    public static string CannotRestart()
    {
        if (!Enabled)
            return Loc.T("Restart room is switched off (SkulInsight settings).");
        if (_restarting || SlotSwitcher.Busy)
            return Loc.T("Please wait: the game is loading.");
        var levels = Singleton<Service>.Instance?.levelManager;
        var player = levels?.player;
        var chapter = levels?.currentChapter;
        if (player == null || chapter == null || !InRunChapter(chapter.type))
            return Loc.T("Restart room works during a run (not in the castle).");
        if (player.health == null || player.health.dead)
            return Loc.T("Not after dying: the run is over.");
        if (Crash.CrashReporter.ScreenBlack || Map.Instance == null)
            return Loc.T("Please wait until the room has loaded.");
        var save = GameData.Save.instance;
        if (!EndsDailyRun && (_save == null || _seed != save.randomSeed || _slot != SlotStore.Active || !save.hasSave))
            return Loc.T("This room has no save to go back to yet (it is made when you enter a room).");
        return null;
    }

    /// <summary>Every frame: the key (Restart Room/Key) restarts on its second press within 3 seconds.</summary>
    public static void Tick()
    {
        if (!Enabled || Plugin.RoomRestartKey == null || !Plugin.RoomRestartKey.Value.IsDown())
            return;
        if (TextInput.IsTyping() || SettingsPage.IsOpen || Codex.CodexWindow.IsOpen || CombatLogWindow.IsOpen)
            return;
        string why = CannotRestart();
        if (why != null)
        {
            Notice.Show("restart", why, 4f);
            return;
        }
        if (Time.unscaledTime > _keyArmedUntil)
        {
            _keyArmedUntil = Time.unscaledTime + 3f;
            string key = Plugin.RoomRestartKey.Value.ToString();
            Notice.Show("restart", EndsDailyRun
                ? Loc.F("Press {0} again to restart the room. This ends your daily run (it goes on the board as it was).", key)
                : Loc.F("Press {0} again to restart the room.", key), 3f);
            return;
        }
        _keyArmedUntil = -1f;
        Restart();
    }

    private static void Restart()
    {
        string why = CannotRestart();
        if (why != null)
        {
            Notice.Show("restart", why, 5f);
            return;
        }
        _restarting = true;
        bool daily = EndsDailyRun;
        try
        {
            Singleton<Service>.Instance.fadeInOut.FadeOutImmediately();
            Codex.CodexTracker.SaveIfNeeded(force: true);
            SaveFileModel roomSave = null;
            if (!daily)
            {
                roomSave = SaveFileModel.FromJson(_save);
                Rewards.RewardStore.RestoreRoom(_rewards);
                History.RunJournal.NoteRoomRestart();
            }
            Rewards.RewardStore.Flush();
            Plugin.Log.LogInfo(daily ? "Restart room: in a daily run (it ends there, like leaving the game)" : "Restart room: back to the room's start");
            Crash.CrashReporter.Note(daily ? "restart room (daily run: it ends)" : "restart room");
            // A daily run: no save goes back; continuing ends it (DailyRun.RefuseContinue), the name for the board follows.
            SlotSwitcher.ReturnToTitle(roomSave);
            CoroutineProxy.instance.StartCoroutine(ContinueAtTitle());
        }
        catch (Exception e)
        {
            _restarting = false;
            Plugin.Log.LogError($"Restart room failed: {e}");
            Notice.Show("restart", Loc.T("The room could not be restarted. See BepInEx/LogOutput.log."), 6f);
        }
    }

    /// <summary>The title screen loads; its "press any key" is skipped and the run continues as with the game's Continue.</summary>
    private static IEnumerator ContinueAtTitle()
    {
        float until = Time.unscaledTime + 30f;
        while (Scene<Main>.instance == null && Time.unscaledTime < until)
            yield return null;
        // Let the title scene wake up (its Awake and Start) before the game goes on from it.
        yield return null;
        yield return null;
        Patches.Guard.Run("Restart room (continue)", () =>
        {
            var main = Scene<Main>.instance;
            if (main == null)
                return;
            main.StopAllCoroutines(); // the title's own wait for a key
            main.StartGame();
        });
        // The game scene loads (LoadFromSave): the title stays skipped until the room is there again.
        until = Time.unscaledTime + 30f;
        while (Scene<Main>.instance != null && Time.unscaledTime < until)
            yield return null;
        _restarting = false;
    }

    /// <summary>A restart is passing the title screen (its logo and "press any key" are skipped).</summary>
    public static bool Continuing => _restarting;

    /// <summary>Save slot switch: the room's save belongs to the slot that is left.</summary>
    public static void ForgetSlot() => _save = _rewards = null;
}
