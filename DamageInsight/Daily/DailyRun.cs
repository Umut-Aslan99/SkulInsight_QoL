using System;
using System.Linq;
using Characters;
using Data;
using DamageInsight.Codex;
using DamageInsight.History;
using DamageInsight.Lang;
using DamageInsight.SaveSlots;
using Level;
using Platforms;
using Services;
using Singletons;

namespace DamageInsight.Daily;

/// <summary>
/// The daily run's flow in the game (0.13, docs/DAILY_RUN.md): "Play today's run" makes the daily slot from an
/// evened-out copy of the player's game and switches to it (it borrows the Codex); in its castle the day's mode, Dark
/// Mirror level and seed are set; leaving the castle starts the try (the server counts it); the end screen ends it;
/// back in the castle the name for the board is asked and the result is sent; "Back to my game" returns.
/// </summary>
public static class DailyRun
{
    public static bool InDailySlot => SlotStore.Active == SlotStore.DailySlot;
    public static int Origin => DailyService.State.Origin;

    /// <summary>Today's try on this PC, or null.</summary>
    public static DailyTry TodaysTry => DailyService.Today == null ? null : DailyService.State.TryOf(DailyService.Today.Date);

    /// <summary>A try whose run ended and waits for its name (any day: a run can end after midnight).</summary>
    public static DailyTry Ended => DailyService.State.Tries.Values.FirstOrDefault(t => t.State == "ended");

    private static bool _castleSwitching;
    private static string _seedNoticeFor = "";

    // ------------------------------------------------------------------ playing

    /// <summary>Why today's run can't be played now (a message for the player), or null.</summary>
    public static string CannotPlay()
    {
        if (!DailyService.Enabled)
            return Loc.T("Turn on daily runs first.");
        var day = DailyService.Today;
        if (day == null)
            return Loc.T("Today's run isn't loaded yet. Is the game online?");
        var t = DailyService.State.TryOf(day.Date);
        if (t != null && t.Played)
            return Loc.T("You played today's run. The next one comes at midnight (UTC).");
        if (!InCastle())
            return Loc.T("Go to the castle first.");
        if (SlotSwitcher.Busy)
            return Loc.T("Please wait: the save slots are busy.");
        return null;
    }

    /// <summary>"Play today's run": returns a message when it can't.</summary>
    public static string Play(Action closeMenus)
    {
        string why = CannotPlay();
        if (why != null)
            return why;
        var day = DailyService.Today;
        try
        {
            if (!InDailySlot)
            {
                var live = PersistentSingleton<PlatformManager>.Instance.platform.data;
                var copy = SaveFileModel.FromJson(live.ToJson());
                DailyProfile.Even(copy, Facts());
                SlotStore.WriteSave(SlotStore.DailySlot, copy.ToJson());
                DailyService.State.Origin = SlotStore.Active;
            }
            var t = DailyService.State.Ensure(day.Date);
            t.State = "armed";
            t.Seed = day.Seed;
            t.Mode = day.Mode;
            t.Level = day.Level;
            DailyService.SaveState();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Daily run: the daily slot could not be made: {e}");
            return Loc.T("Today's run could not be prepared. See BepInEx/LogOutput.log.");
        }
        Plugin.Log.LogInfo($"Daily run: {day.Date} ({day.Mode} {day.Level}) armed; slot {SlotStore.DailySlot} from slot {Origin}");
        return InDailySlot ? null : SlotSwitcher.Switch(SlotStore.DailySlot, closeMenus, keepCodex: true);
    }

    /// <summary>"Back to my game": the slot the daily slot was entered from.</summary>
    public static string Leave(Action closeMenus)
    {
        if (!InDailySlot)
            return null;
        if (!InCastle())
            return Loc.T("Go to the castle first.");
        if (Ended != null)
            return Loc.T("Send your run to the board first (or skip it).");
        return SlotSwitcher.Switch(Origin, closeMenus, keepCodex: true);
    }

    /// <summary>What an evened-out profile sets, read from the game (gear references, the Witch, Darktech).</summary>
    private static DailyProfileFacts Facts()
    {
        var facts = new DailyProfileFacts();
        var gear = GameResources.GearResource.instance;
        if (gear != null)
            foreach (var reference in gear.weapons.Cast<GameResources.GearReference>().Concat(gear.items.Cast<GameResources.GearReference>())
                         .Concat(gear.essences.Cast<GameResources.GearReference>()))
                if (reference != null && reference.needUnlock)
                    facts.GearUnlockKeys.Add($"gear/unlocked/{reference.type}/{reference.name}");
        var upgrades = Characters.Gear.Upgrades.UpgradeResource.instance?.upgradeReferences;
        if (upgrades != null)
            foreach (var upgrade in upgrades)
                if (upgrade != null && upgrade.needUnlock)
                    facts.GearUnlockKeys.Add($"gear/unlocked/{Characters.Gear.Gear.Type.Upgrade}/{upgrade.name}");
        var witch = WitchBonus.instance;
        foreach (var (tree, name) in new[] { ((WitchBonus.Tree)witch.skull, "skull"), (witch.body, "body"), (witch.soul, "soul") })
            if (tree?.list != null)
                for (int i = 0; i < tree.list.Count; i++)
                    facts.WitchTop[$"Progress/WitchMastery/{name}/{i}"] = tree.list[i].maxLevel;
        foreach (Hardmode.Darktech.DarktechData.Type machine in Enum.GetValues(typeof(Hardmode.Darktech.DarktechData.Type)))
            facts.Darktech.Add(machine.ToString());
        facts.TopDarkMirrorLevel = GameData.HardmodeProgress.maxLevel;
        return facts;
    }

    // ------------------------------------------------------------------ in the daily slot (every frame)

    public static void Tick()
    {
        if (!InDailySlot)
            return;
        // The armed try keeps its own seed, mode and level: the castle is set up even if the connection dropped since.
        var t = DailyService.State.Tries.Values.Where(x => x.State == "armed").OrderByDescending(x => x.Date, StringComparer.Ordinal).FirstOrDefault();
        if (t == null)
            return;
        var levels = Singleton<Service>.Instance?.levelManager;
        var chapter = levels?.currentChapter;
        if (chapter == null || levels.player == null || !IsCastle(chapter.type))
            return;
        bool wantDark = t.Mode == "darkmirror";
        bool inDark = chapter.type == Chapter.Type.HardmodeCastle;
        if (wantDark != inDark)
        {
            // The other castle: the game's own reset loads it (and draws a new seed, set again below).
            if (!_castleSwitching)
            {
                _castleSwitching = true;
                Plugin.Log.LogInfo($"Daily run: to the {(wantDark ? "Dark Mirror" : "normal")} castle");
                levels.ResetGame(wantDark ? Chapter.Type.HardmodeCastle : Chapter.Type.Castle);
            }
            return;
        }
        _castleSwitching = false;
        if (wantDark && GameData.HardmodeProgress.hardmodeLevel != t.Level)
            GameData.HardmodeProgress.hardmodeLevel = t.Level;
        var save = GameData.Save.instance;
        if (save.randomSeed != t.Seed)
        {
            save._randomSeed.value = t.Seed;
            save._randomSeed.Save();
        }
        if (_seedNoticeFor != t.Date)
        {
            _seedNoticeFor = t.Date;
            DamageInsight.UI.Notice.Show("daily", Loc.F("Today's run is ready ({0}): leave the castle to begin. One try!", DailyPages.Mode(t.Mode, t.Level)), 8f);
        }
    }

    // ------------------------------------------------------------------ no continuing (Umut 2026-10-18)

    /// <summary>
    /// The daily try that was left during its run, or null: the game loads the daily slot's game (only the title
    /// screen does, never in the middle of a run) while a try is still running. The game saves a run when a map is
    /// entered (health, gear, place) and a continued run starts that map again: closing the game mid-map would heal
    /// the player and undo the map (a lost fight, a choice). So a daily run can't be continued: it ends where it was
    /// left (also after the game's own "back to title", which drops the run, or a crash).
    /// </summary>
    public static DailyTry Interrupted(bool inDailySlot, System.Collections.Generic.IEnumerable<DailyTry> tries) =>
        inDailySlot ? tries.Where(t => t.State == "started").OrderByDescending(t => t.Date, StringComparer.Ordinal).FirstOrDefault() : null;

    /// <summary>The game is about to load the daily slot's game (LevelManager.LoadGame): a daily run in progress ends
    /// as abandoned instead of continuing, the game loads the castle, where the name for the board is asked.</summary>
    public static void RefuseContinue()
    {
        var t = Interrupted(InDailySlot, DailyService.State.Tries.Values);
        if (t == null)
            return;
        var save = GameData.Save.instance;
        if (save.hasSave)
        {
            save.hasSave = false;
            save.SaveAll();
            PersistentSingleton<PlatformManager>.Instance.SaveDataToFile();
        }
        var run = RunJournal.AbandonOpen();
        if (t.State == "started")
        {
            // No run file to end it with (it was never written): nothing to send.
            t.State = "finished";
            t.Result = "abandoned";
            t.Rank = -2;
            DailyService.SaveState();
        }
        Plugin.Log.LogInfo($"Daily run: {t.Date} was left during the run; not continued ({(run != null ? "run " + run.Id : "no run file")})");
        DamageInsight.UI.Notice.Show("daily", Loc.T("Your daily run ended: it was left during the run (game closed or back to the title screen), and a daily run can't be continued. It goes on the board as it was."), 12f);
    }

    /// <summary>A new run begins (RunJournal): the day's date when it is today's daily run, else null.</summary>
    public static string OnRunBegins(int seed)
    {
        if (!InDailySlot)
            return null;
        var t = DailyService.State.Tries.Values.FirstOrDefault(x => x.State == "armed" && x.Seed == seed);
        if (t == null)
            return null;
        t.State = "started";
        DailyService.SaveState();
        DailyService.SendStart(t.Date);
        Plugin.Log.LogInfo($"Daily run: {t.Date} started");
        return t.Date;
    }

    /// <summary>A run ended (RunJournal): a daily run's numbers wait for the name.</summary>
    public static void OnRunEnded(RunRecord run)
    {
        if (string.IsNullOrEmpty(run.Daily))
            return;
        var t = DailyService.State.TryOf(run.Daily);
        if (t == null || t.State is "ended" or "finished")
            return;
        t.RunId = run.Id;
        t.Result = run.Result == RunResult.Ending ? "win" : run.Result == RunResult.Death ? "death" : "abandoned";
        t.Numbers = ResultOf(run).NumbersJson();
        t.Run = run.ToJson();
        t.State = "ended";
        DailyService.SaveState();
        Plugin.Log.LogInfo($"Daily run: {t.Date} ended ({t.Result})");
    }

    /// <summary>The name is given (or the run is not sent): the result goes to the board.</summary>
    public static void Send(DailyTry t, string name)
    {
        DailyService.State.Name = name;
        DailyService.QueueFinish(t, name);
    }

    public static void Skip(DailyTry t)
    {
        t.State = "finished";
        t.Rank = -2; // not sent
        DailyService.SaveState();
    }

    /// <summary>A run's numbers for the board (the server turns them into points).</summary>
    internal static DailyResult ResultOf(RunRecord run)
    {
        var result = new DailyResult
        {
            Result = run.Result == RunResult.Ending ? "win" : run.Result == RunResult.Death ? "death" : "abandoned",
            PlaySeconds = run.PlaySeconds,
            Kills = (int)run.Number(RunNumbers.Kills),
            BossKills = (int)run.Number(RunNumbers.BossKills),
            StagesReached = run.Maps.Select(m => m.Chapter + "/" + m.Stage).Distinct().Count(),
            Maps = run.Maps.Count,
            TotalDamage = (long)run.Number(RunNumbers.TotalDamage),
            DamageTaken = (long)run.Number(RunNumbers.DamageTaken),
            GoldLeft = (long)run.Number(RunNumbers.GoldLeft),
        };
        var look = new RunLookup { Name = id => CodexPages.Entry(id)?.Name };
        result.EndBosses.AddRange(RunPages.Bosses(run, look).Select(b => b.name));
        var held = run.Build.FirstOrDefault(g => g.Kind == "skull" && g.Extra.StartsWith("held"));
        result.Skull = held == null ? "" : CodexPages.Entry(held.Id)?.Name ?? held.Name;
        // Only vanilla gear counts (Umut): other mods and gear the game's own catalog doesn't know go with the result.
        result.Mods.AddRange(DailyMods.Others().Select(m => m.guid));
        result.UnknownGear = run.Build.Any(g => g.Kind is "skull" or "item" or "essence" && CodexPages.Entry(g.Id) == null);
        return result;
    }

    internal static bool InCastle()
    {
        try
        {
            var levels = Singleton<Service>.Instance?.levelManager;
            return levels?.player != null && levels.currentChapter != null && IsCastle(levels.currentChapter.type);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsCastle(Chapter.Type type) => type is Chapter.Type.Castle or Chapter.Type.HardmodeCastle;
}
