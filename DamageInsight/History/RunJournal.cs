using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Characters;
using Characters.Gear.Synergy.Inscriptions;
using Data;
using DamageInsight.Codex;
using DamageInsight.Recording;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.History;

/// <summary>
/// The run history's recorder (0.13): one file per run in <c>Codex/Runs</c> (per save slot through the Codex folder)
/// with the result, the end screen's numbers, the final build, the damage split, the maps and a timeline. A run is
/// the game's run seed: quitting and continuing keeps it, a new run while one is still open marks the old one
/// abandoned. Written after every map (and every 30 s while something changed), so a crash loses little.
/// </summary>
public static class RunJournal
{
    public static string Folder => Path.Combine(CodexTracker.Folder, "Runs");
    private static string OpenMarker => Path.Combine(Folder, "open.txt");
    public static string PathOf(string id, string extension = ".json") => Path.Combine(Folder, id + extension);

    // The daily slot records always: a daily run's try, its result and its run for the board come from here.
    private static bool On => Plugin.RunHistory?.Value != false || DamageInsight.Daily.DailyRun.InDailySlot;

    /// <summary>The run being recorded (null in the castle or when none was found yet).</summary>
    public static RunRecord Current => _run;

    /// <summary>Increases whenever a run file was written (the book's Runs pages reload then).</summary>
    public static int Version { get; private set; }

    private static RunRecord _run;
    private static BuildSnapshot _last;
    private static readonly Dictionary<string, int> MaxSteps = new();
    private static readonly List<(string from, string to)> Changed = new();
    private static readonly RunDamageCounter DamageCounter = new();
    private static float _nextTick, _nextWrite, _settleUntil;
    private static bool _dirty;
    private static int _lastPlay;
    private static string _held = "";
    private static Map _mapRef;
    private static RunMap _map;
    private static int _lastRevive;

    // The last enemy (or trap) that hurt the player: the killer when the player dies right after.
    private static string _lastAttacker = "", _lastAttackerId = "";
    private static float _lastAttackerAt = -100f;

    public static void Attach()
    {
        DamageLog.Recorded += OnRecord;
        Application.quitting += () => DamageInsight.Patches.Guard.Run("Run history: save at quit", () => Save());
    }

    /// <summary>Save slot switch: the run being recorded belongs to the slot left behind (its file goes with it).</summary>
    internal static void ForgetSlot()
    {
        Save();
        Reset();
        _index = null;
        _loaded = null;
        Version++;
    }

    // ------------------------------------------------------------------ the list (Codex: You, Runs)

    private static List<RunRecord> _index;
    private static RunRecord _loaded;

    /// <summary>Every run of this slot as a summary, newest first; the run being played is its live state.</summary>
    public static List<RunRecord> Runs()
    {
        if (_index == null)
        {
            try { _index = RunIndex.Load(Folder, out _); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Run history: the run list could not be read: {e.Message}");
                _index = new List<RunRecord>();
            }
        }
        var list = new List<RunRecord>(_index);
        if (_run != null && _run.Result == RunResult.InProgress)
        {
            list.RemoveAll(r => r.Id == _run.Id);
            list.Insert(0, _run.Summary());
        }
        return list;
    }

    /// <summary>A whole run (for its page): the run being played, or its file.</summary>
    public static RunRecord Load(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (_run != null && _run.Id == id)
        {
            _run.Damage.Clear();
            _run.Damage.AddRange(DamageCounter.Rows());
            if (_last != null)
            {
                _run.Build.Clear();
                _run.Build.AddRange(ToBuild(_last));
            }
            return _run;
        }
        if (_loaded != null && _loaded.Id == id)
            return _loaded;
        try
        {
            string path = PathOf(id);
            _loaded = File.Exists(path) ? RunRecord.FromJson(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: run {id} not read: {e.Message}");
            _loaded = null;
        }
        return _loaded;
    }

    /// <summary>Stars or unstars a run: a starred run is never shrunk.</summary>
    public static void SetFavourite(string id, bool favourite)
    {
        var run = _run != null && _run.Id == id ? _run : Load(id);
        if (run == null || run.Favourite == favourite)
            return;
        run.Favourite = favourite;
        if (run == _run)
        {
            _dirty = true;
            Save(force: true);
        }
        else
        {
            Write(run);
        }
        UpdateIndex(run);
    }

    private static void UpdateIndex(RunRecord run)
    {
        try
        {
            Runs();
            _index = RunIndex.Upsert(_index, run);
            RunIndex.Save(Folder, _index);
            Version++;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: the run list was not updated: {e.Message}");
        }
    }

    /// <summary>Old runs without a star shrink to their summary (setting General/RunHistoryKeep: how many stay whole).</summary>
    private static void Shrink()
    {
        try
        {
            Runs();
            int keep = Math.Max(20, Plugin.RunHistoryKeep?.Value ?? 100);
            var ids = RunIndex.ToShrink(_index, keep);
            if (ids.Count == 0)
                return;
            foreach (var id in ids)
            {
                var run = RunRecord.FromJson(File.ReadAllText(PathOf(id), Encoding.UTF8));
                if (run == null)
                    continue;
                var small = run.Shrink();
                if (!Write(small))
                    continue;
                if (File.Exists(PathOf(id, ".jpg")))
                    File.Delete(PathOf(id, ".jpg"));
                _index = RunIndex.Upsert(_index, small);
            }
            RunIndex.Save(Folder, _index);
            Plugin.Log.LogInfo($"Run history: {ids.Count} old run(s) shrunk to their summary");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: old runs not shrunk: {e.Message}");
        }
    }

    private static void Reset()
    {
        _run = null;
        _last = null;
        MaxSteps.Clear();
        Changed.Clear();
        DamageCounter.Load(Array.Empty<RunDamage>());
        _mapRef = null;
        _map = null;
        _held = "";
        _dirty = false;
        _startPending = false;
        KilledThisRun.Clear();
    }

    // ------------------------------------------------------------------ every frame (from Plugin.Update)

    public static void Tick(Character player)
    {
        if (!On || Time.unscaledTime < _nextTick)
            return;
        _nextTick = Time.unscaledTime + 0.5f;

        var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
        if (chapter == null)
            return;
        if (!IsRunChapter(chapter.type))
        {
            if (_run != null && IsCastle(chapter.type))
            {
                // Back in the castle: an end screen closed the run, or (should not happen) something else ended it.
                if (_run.Result == RunResult.InProgress)
                    Finish(RunResult.Abandoned, "");
                Reset();
            }
            return;
        }
        if (player == null)
            return;
        if (_run == null)
        {
            Begin();
            _settleUntil = Time.unscaledTime + 1.5f; // a continued run's inventory loads over a few frames
            return;
        }
        if (_run.Result != RunResult.InProgress || Time.unscaledTime < _settleUntil)
            return;
        if (_startPending)
        {
            RecordStart(player);
            return;
        }

        int play = GameData.Progress.playTime;
        var snapshot = Snapshot(player);
        bool sameSkulls = _last != null && _last.Skulls.Select(s => s.Id).OrderBy(x => x).SequenceEqual(snapshot.Skulls.Select(s => s.Id).OrderBy(x => x));

        // Time per skull, and swaps (the held skull changed while the two skulls stayed the same).
        if (_held.Length > 0 && play > _lastPlay)
        {
            _run.SkullSeconds[_held] = (_run.SkullSeconds.TryGetValue(_held, out int s) ? s : 0) + (play - _lastPlay);
            _dirty = true;
        }
        if (snapshot.Held != _held && _held.Length > 0 && sameSkulls)
            _run.Numbers[RunNumbers.Swaps] = _run.Number(RunNumbers.Swaps) + 1;
        _held = snapshot.Held;
        foreach (var skull in snapshot.Skulls)
            _run.SkullNames[skull.Id] = skull.Name;
        _lastPlay = play;

        // The map.
        var map = Map.Instance;
        if (map != null && map != _mapRef)
        {
            if (_map != null)
                _map.Seconds = Math.Max(0, play - _map.T);
            _mapRef = map;
            _map = new RunMap
            {
                T = play,
                Chapter = chapter.type.ToString(),
                Stage = chapter.stageIndex,
                StageName = Safe(() => chapter.stageName),
                Map = map.name.Replace("(Clone)", "").Trim(),
                Type = map.type.ToString(),
            };
            _run.Maps.Add(_map);
            _run.Numbers[RunNumbers.Maps] = _run.Maps.Count;
            _dirty = true;
            _nextWrite = 0f; // write now: a new map is a good save point
        }
        else if (_map != null)
        {
            _map.Seconds = Math.Max(0, play - _map.T);
        }

        // The build.
        if (_last != null)
        {
            var events = BuildDiff.Compare(_last, snapshot, play, Changed, MaxSteps);
            if (events.Count > 0)
            {
                _run.Events.AddRange(events);
                _dirty = true;
            }
        }
        Changed.Clear();
        _last = snapshot;

        // Revives (the Reassemble mastery and items that bring you back).
        int revive = GameData.Progress.revive;
        if (revive > _lastRevive)
        {
            _run.Events.Add(new RunEvent { T = play, Kind = RunEvents.Revive });
            _dirty = true;
        }
        _lastRevive = revive;

        if (!_run.Easy && !_run.DarkMirror && Safe(() => GameData.Settings.easyMode))
        {
            _run.Easy = true;
            _dirty = true;
        }
        _run.EndPlay = play;
        _run.EndChapter = chapter.type.ToString();
        _run.EndStage = chapter.stageIndex;
        if (_dirty && Time.unscaledTime >= _nextWrite)
            Save();
    }

    private static void Begin()
    {
        int seed = GameData.Save.instance.randomSeed;
        var open = ReadOpen();
        var (resume, abandon) = RunResume.Decide(open == null ? Array.Empty<RunRecord>() : new[] { open }, seed);
        foreach (var old in abandon)
        {
            old.Result = RunResult.Abandoned;
            if (old.Ended == default)
                old.Ended = Safe(() => File.GetLastWriteTime(PathOf(old.Id)));
            Write(old);
            UpdateIndex(old);
        }

        int play = GameData.Progress.playTime;
        if (resume != null)
        {
            _run = resume;
            DamageCounter.Load(resume.Damage);
            foreach (var e in resume.Events.Where(e => e.Kind == RunEvents.Inscription))
                MaxSteps[e.Id] = Math.Max(MaxSteps.TryGetValue(e.Id, out int m) ? m : 0, e.Value);
            _last = FromBuild(resume.Build);
            foreach (var pair in _last.Inscriptions)
                MaxSteps[pair.Key] = Math.Max(MaxSteps.TryGetValue(pair.Key, out int m) ? m : 0, pair.Value.step);
            _held = _last.Held;
            _startPending = false;
            _lastPlay = play;
            _lastRevive = GameData.Progress.revive;
            Plugin.Log.LogInfo($"Run history: continuing the run of {resume.Started:g}");
            return;
        }

        var now = DateTime.Now;
        var chapter = Singleton<Service>.Instance.levelManager.currentChapter;
        bool joinedLate = chapter.type is not (Chapter.Type.Chapter1 or Chapter.Type.HardmodeChapter1) || chapter.stageIndex > 0
                          || GameData.Progress.kills > 0;
        _run = new RunRecord
        {
            Id = RunRecord.IdFor(now),
            Seed = seed,
            Started = now,
            DarkMirror = CodexMode.DarkMirrorNow,
            Level = CodexMode.DarkMirrorNow ? Safe(() => GameData.HardmodeProgress.hardmodeLevel) : 0,
            ModVersion = MyPluginInfo.PLUGIN_VERSION,
            StartPlay = play, // the game's play time also counts the castle before the run; the run starts here
            EndPlay = play,
        };
        // Counted from 0.14 on: a run that has the keys counted them (older runs show nothing).
        _run.Numbers[RunNumbers.HealsMissed] = 0;
        _run.Numbers[RunNumbers.HealthMissed] = 0;
        if (joinedLate)
            _run.Numbers[RunNumbers.JoinedLate] = 1;
        else
            _run.Daily = DamageInsight.Daily.DailyRun.OnRunBegins(seed) ?? "";
        _last = null; // the first snapshot (after settling) is the start
        _lastPlay = play;
        _lastRevive = GameData.Progress.revive;
        _startPending = !joinedLate; // a run found halfway (no file yet) has no known start
        Plugin.Log.LogInfo($"Run history: a new run ({(_run.DarkMirror ? "Dark Mirror " + _run.Level : "normal")})");
    }

    private static bool _startPending;

    /// <summary>After settling: the run's starting build as "start" + "taken" events (a new run only).</summary>
    private static void RecordStart(Character player)
    {
        _startPending = false;
        int play = GameData.Progress.playTime;
        var snapshot = Snapshot(player);
        _run.Events.Add(new RunEvent { T = play, Kind = RunEvents.Start, Id = snapshot.Held, Name = snapshot.Skulls.FirstOrDefault(s => s.Id == snapshot.Held)?.Name ?? "" });
        var events = BuildDiff.Compare(new BuildSnapshot(), snapshot, play, null, MaxSteps);
        _run.Events.AddRange(events);
        _last = snapshot;
        _held = snapshot.Held;
        _dirty = true;
        _nextWrite = 0f;
    }

    // ------------------------------------------------------------------ hits

    /// <summary>Every recorded hit: the player's damage goes into the run's damage table.</summary>
    private static void OnRecord(DamageRecord record)
    {
        if (_run == null || _run.Result != RunResult.InProgress)
            return;
        try
        {
            if (GearTable.KeyOf(record) is { } key)
            {
                DamageCounter.Add(key.group, key.name, record.Amount);
                _dirty = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: a hit was not counted: {e.Message}");
        }
    }

    /// <summary>A hit landed (DamageRecordPatch): who hurt the player last, and boss and adventurer kills.</summary>
    public static void OnHit(Character target, in Damage damage, double dealt)
    {
        if (!On || _run == null || _run.Result != RunResult.InProgress || target == null)
            return;
        var attacker = damage.attacker.character;
        if (target.type == Character.Type.Player)
        {
            if (attacker != null)
            {
                _lastAttackerId = CodexTracker.EntryOf(attacker)?.id ?? "";
                _lastAttacker = NameOf(attacker, _lastAttackerId);
            }
            else if (damage.attacker.trap != null)
            {
                _lastAttacker = Loc_Trap();
                _lastAttackerId = "";
            }
            _lastAttackerAt = Time.unscaledTime;
            // Damage taken per attacker, by the name the Codex gives it.
            string by = _lastAttacker.Length > 0 ? _lastAttacker : "?";
            var old = _run.DamageTaken.TryGetValue(by, out var v) ? v : (0d, 0);
            _run.DamageTaken[by] = (old.Item1 + dealt, old.Item2 + 1);
            _dirty = true;
            return;
        }
        if (!target.health.dead || attacker == null || attacker.type is not (Character.Type.Player or Character.Type.PlayerMinion))
            return;
        if (!KilledThisRun.Add(target.GetInstanceID()))
            return;
        bool boss = target.type == Character.Type.Boss;
        bool adventurer = target.type == Character.Type.Adventurer;
        if (!boss && !adventurer)
            return;
        string id = CodexTracker.EntryOf(target)?.id ?? "";
        string name = NameOf(target, id);
        _run.Events.Add(new RunEvent
        {
            T = GameData.Progress.playTime,
            Kind = boss ? RunEvents.Boss : RunEvents.Adventurer,
            Id = id,
            Name = name,
        });
        if (adventurer)
            _run.Numbers[RunNumbers.AdventurerKills] = _run.Number(RunNumbers.AdventurerKills) + 1;
        else
        {
            _run.LastBoss = name;
            _run.LastBossId = id;
        }
        _dirty = true;
    }

    private static readonly HashSet<int> KilledThisRun = new();

    /// <summary>A character's name as the Codex shows it (the game's own object names say "Scarecraw", "Dark", ...).</summary>
    private static string NameOf(Character character, string codexId) =>
        (codexId.Length > 0 ? Safe(() => CodexPages.Entry(codexId)?.Name) : null) ??
        RunPages.AttackerName(DamageLog.NameOf(character), id => Safe(() => CodexPages.Entry(id)?.Name));

    private static string Loc_Trap() => DamageInsight.Lang.Loc.T("Trap");

    /// <summary>The game reported an item or quintessence turning into another (ItemInventory / QuintessenceInventory.Change).</summary>
    public static void OnGearChanged(Characters.Gear.Gear from, Characters.Gear.Gear to)
    {
        if (_run == null || from == null || to == null)
            return;
        Changed.Add((GearId(from), GearId(to)));
    }

    // ------------------------------------------------------------------ the end

    /// <summary>The end screen opened (UI.GameResult.Show: a death; ShowEndingResult: an ending).</summary>
    public static void OnGameResult(bool ending)
    {
        if (!On || _run == null || _run.Result != RunResult.InProgress)
            return;
        string kind = "";
        var hm = Singleton<Hardmode.HardmodeManager>.Instance;
        bool emperor = Safe(() => hm.emperorCleared);
        if (ending)
            kind = !CodexMode.DarkMirrorNow ? "normal" : emperor ? "emperor" : "darkmirror";
        // The screen the game shows right now, by the same facts it decided by (kept as string keys).
        try
        {
            var screen = RunPages.ScreenFor(ending, hm.hardmode, hm.currentLevel, hm.maxLevel, hm.clearedLevel, emperor);
            _run.ScreenTitle = screen.Title;
            _run.ScreenSubtitle = screen.Subtitle;
            _run.ScreenStage = screen.Stage;
            _run.ScreenPanel = screen.Panel;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: end screen kind not read: {e.Message}");
        }
        // The heal orbs on the last map: a death left them behind; after a win they don't matter.
        if (ending)
            HealOrbs.Forget();
        else
        {
            try
            {
                HealOrbs.Close(died: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Run history: heal orbs not counted: {e.Message}");
            }
        }
        Finish(ending ? RunResult.Ending : RunResult.Death, kind);
    }

    /// <summary>Heal orbs left behind (History/HealOrbs): added to the run in progress.</summary>
    public static void AddHealsMissed(int orbs, double health)
    {
        if (!On || _run == null || _run.Result != RunResult.InProgress || orbs <= 0)
            return;
        _run.Numbers[RunNumbers.HealsMissed] = _run.Number(RunNumbers.HealsMissed) + orbs;
        _run.Numbers[RunNumbers.HealthMissed] = _run.Number(RunNumbers.HealthMissed) + Math.Round(health);
        _dirty = true;
        Plugin.Log.LogInfo($"Run history: {orbs} heal orb(s) left behind ({Math.Round(health)} health)");
    }

    private static void Finish(RunResult result, string ending)
    {
        var run = _run;
        var player = Singleton<Service>.Instance?.levelManager?.player;
        if (player != null && Time.unscaledTime >= _settleUntil)
        {
            // The last changes before the end (an item taken a moment ago).
            var snapshot = Snapshot(player);
            if (_last != null)
                run.Events.AddRange(BuildDiff.Compare(_last, snapshot, GameData.Progress.playTime, Changed, MaxSteps));
            _last = snapshot;
        }
        run.Result = result;
        run.Ending = ending;
        run.Ended = DateTime.Now;
        if (result != RunResult.Abandoned)
        {
            run.EndPlay = GameData.Progress.playTime;
            ReadEndScreen(run);
            ReadStats(run);
            var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
            if (chapter != null)
            {
                run.EndChapter = chapter.type.ToString();
                run.EndStage = chapter.stageIndex;
                run.EndStageName = Safe(() => chapter.stageName);
            }
        }
        if (_map != null)
            _map.Seconds = Math.Max(0, run.EndPlay - _map.T);
        if (result == RunResult.Death && Time.unscaledTime - _lastAttackerAt < 15f)
        {
            run.Killer = _lastAttacker;
            run.KillerId = _lastAttackerId;
        }
        if (result != RunResult.Abandoned)
            run.Picture = SavePicture(run.Id, result == RunResult.Death);
        Save(force: true);
        Safe(() => { if (File.Exists(OpenMarker)) File.Delete(OpenMarker); return 0; });
        KilledThisRun.Clear();
        UpdateIndex(run);
        Shrink();
        DamageInsight.Patches.Guard.Run("Daily run (end)", () => DamageInsight.Daily.DailyRun.OnRunEnded(run));
        Plugin.Log.LogInfo($"Run history: run ended ({result}{(ending.Length > 0 ? " " + ending : "")}, {run.Maps.Count} maps)");
    }

    /// <summary>
    /// Ends the run in progress as abandoned (0.13 daily run: a daily run can't be continued, Umut 2026-10-18). The run
    /// in memory (back at the title screen in the same session) or the open run file (the game was closed or crashed;
    /// it holds the run as it was at most 30 s before). Returns the run, or null when there was none.
    /// </summary>
    public static RunRecord AbandonOpen()
    {
        if (_run != null && _run.Result == RunResult.InProgress)
        {
            var current = _run;
            Finish(RunResult.Abandoned, "");
            Reset();
            return current;
        }
        var open = ReadOpen();
        if (open == null || open.Result != RunResult.InProgress)
            return null;
        open.Result = RunResult.Abandoned;
        if (open.Ended == default)
            open.Ended = Safe(() => File.GetLastWriteTime(PathOf(open.Id)));
        Write(open);
        UpdateIndex(open);
        Safe(() => { if (File.Exists(OpenMarker)) File.Delete(OpenMarker); return 0; });
        DamageInsight.Patches.Guard.Run("Daily run (end)", () => DamageInsight.Daily.DailyRun.OnRunEnded(open));
        Plugin.Log.LogInfo($"Run history: the open run of {open.Started:g} ended as abandoned");
        return open;
    }

    /// <summary>The stats a run page shows at the end (the You page's sheet: the game's panel and the hidden damage stats).</summary>
    internal static readonly Stat.Kind[] EndStats =
    {
        Stat.Kind.Health, Stat.Kind.PhysicalAttackDamage, Stat.Kind.MagicAttackDamage, Stat.Kind.AttackDamage,
        Stat.Kind.BasicAttackSpeed, Stat.Kind.MovementSpeed, Stat.Kind.CriticalChance, Stat.Kind.CriticalDamage,
        Stat.Kind.SkillCooldownSpeed, Stat.Kind.SwapCooldownSpeed, Stat.Kind.EssenceCooldownSpeed, Stat.Kind.ChargingSpeed,
        Stat.Kind.TakingDamage, Stat.Kind.BasicAttackDamage, Stat.Kind.SkillAttackDamage, Stat.Kind.ProjectileAttackDamage,
    };

    /// <summary>The player's stats now, into the run's numbers.</summary>
    private static void ReadStats(RunRecord run)
    {
        var stat = Singleton<Service>.Instance?.levelManager?.player?.stat;
        if (stat == null)
            return;
        foreach (var kind in EndStats)
        {
            try { run.Numbers[RunNumbers.StatPrefix + kind] = stat.GetFinal(kind); }
            catch (Exception) { }
        }
    }

    /// <summary>The end screen's numbers (the game's counters for this run).</summary>
    private static void ReadEndScreen(RunRecord run)
    {
        void Set(string key, Func<double> read)
        {
            try { run.Numbers[key] = read(); }
            catch (Exception) { }
        }
        Set(RunNumbers.PlayTime, () => GameData.Progress.playTime);
        Set(RunNumbers.Kills, () => GameData.Progress.kills);
        Set(RunNumbers.EliteKills, () => GameData.Progress.eliteKills);
        Set(RunNumbers.BossKills, () => GameData.Progress.bossKills);
        Set(RunNumbers.TotalDamage, () => GameData.Progress.totalDamage);
        Set(RunNumbers.DamageTaken, () => GameData.Progress.totalTakingDamage);
        Set(RunNumbers.BestDamage, () => GameData.Progress.bestDamage);
        Set(RunNumbers.Healed, () => GameData.Progress.totalHeal);
        Set(RunNumbers.LostHealth, () => GameData.Progress.lostHealth);
        Set(RunNumbers.Gold, () => GameData.Currency.gold.income);
        Set(RunNumbers.GoldLeft, () => GameData.Currency.gold.balance);
        Set(RunNumbers.DarkQuartz, () => GameData.Currency.darkQuartz.income);
        Set(RunNumbers.Bones, () => GameData.Currency.bone.income);
        Set(RunNumbers.HeartQuartz, () => GameData.Currency.heartQuartz.income);
        Set(RunNumbers.SkullsSeen, () => GameData.Progress.encounterWeaponCount);
        Set(RunNumbers.ItemsSeen, () => GameData.Progress.encounterItemCount);
        Set(RunNumbers.EssencesSeen, () => GameData.Progress.encounterEssenceCount);
        Set(RunNumbers.Revives, () => GameData.Progress.revive);
        Set(RunNumbers.Deaths, () => GameData.Progress.deaths);
    }

    // The end screen's picture: the game renders its "death cam" 1.6 s after a death and at an ending's last scene
    // (CameraController.RenderEndingScene); RunHistoryPatches calls OnPhoto right after each render.
    private static byte[] _photo;
    private static string _photoRun;

    /// <summary>The game just took the end screen's picture: keep it for the run's end.</summary>
    public static void OnPhoto()
    {
        if (!On || _run == null || _run.Result != RunResult.InProgress)
            return;
        _photo = CapturePhoto();
        _photoRun = _photo == null ? null : _run.Id;
    }

    private static bool SavePicture(string id, bool death)
    {
        try
        {
            // A death always renders the picture first; if that render was missed, take what the camera holds.
            byte[] photo = _photoRun == id ? _photo : death ? CapturePhoto() : null;
            _photo = null;
            _photoRun = null;
            if (photo == null)
                return false;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(PathOf(id, ".jpg"), photo);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: the end screen's picture was not saved: {e.Message}");
            return false;
        }
    }

    private static byte[] CapturePhoto()
    {
        Texture2D copy = null;
        var previous = RenderTexture.active;
        try
        {
            var rt = GameResources.CommonResource.instance?.deathCamRenderTexture;
            if (rt == null)
                return null;
            RenderTexture.active = rt;
            copy = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            copy.Apply();
            return copy.EncodeToJPG(85);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: the end screen's picture was not taken: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (copy != null)
                UnityEngine.Object.Destroy(copy);
        }
    }

    // ------------------------------------------------------------------ files

    private static void Save(bool force = false)
    {
        var run = _run;
        if (run == null || (!_dirty && !force))
            return;
        run.Damage.Clear();
        run.Damage.AddRange(DamageCounter.Rows());
        if (_last != null)
        {
            run.Build.Clear();
            run.Build.AddRange(ToBuild(_last));
        }
        if (run.Result == RunResult.InProgress)
            ReadStats(run);
        if (Write(run) && run.Result == RunResult.InProgress)
            Safe(() => { File.WriteAllText(OpenMarker, run.Id + "\n" + run.Seed, new UTF8Encoding(false)); return 0; });
        _dirty = false;
        _nextWrite = Time.unscaledTime + 30f;
    }

    private static bool Write(RunRecord run)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = PathOf(run.Id), temp = path + ".tmp";
            File.WriteAllText(temp, run.ToJson(), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
            Version++;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: run file not written: {e.Message}");
            return false;
        }
    }

    /// <summary>The run still marked open (open.txt), or null.</summary>
    private static RunRecord ReadOpen()
    {
        try
        {
            if (!File.Exists(OpenMarker))
                return null;
            string id = File.ReadAllText(OpenMarker).Split('\n')[0].Trim();
            string path = PathOf(id);
            return File.Exists(path) ? RunRecord.FromJson(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Run history: the open run could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>Every run of this slot, newest first.</summary>
    public static List<RunRecord> LoadAll()
    {
        var runs = new List<RunRecord>();
        if (!Directory.Exists(Folder))
            return runs;
        foreach (var file in Directory.GetFiles(Folder, "*.json"))
        {
            try
            {
                if (RunRecord.FromJson(File.ReadAllText(file, Encoding.UTF8)) is { } run)
                    runs.Add(run);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Run history: {Path.GetFileName(file)} not read: {e.Message}");
            }
        }
        return runs.OrderByDescending(r => r.Started).ToList();
    }

    // ------------------------------------------------------------------ the build

    private static BuildSnapshot Snapshot(Character player)
    {
        var snapshot = new BuildSnapshot();
        var inventory = player.playerComponents?.inventory;
        if (inventory == null)
            return snapshot;
        var weapons = inventory.weapon;
        if (weapons?.weapons != null)
        {
            foreach (var weapon in weapons.weapons)
            {
                if (weapon == null)
                    continue;
                string next = Safe(() => weapon.upgradable ? CodexCatalog.GearId(weapon.type, weapon.nextLevelReference.name) : "");
                snapshot.Skulls.Add(new BuildSnapshot.Piece
                {
                    Id = GearId(weapon),
                    Name = Safe(() => weapon.displayName),
                    Rarity = weapon.rarity.ToString(),
                    Next = next ?? "",
                    Extra = Safe(() => string.Join(" · ", weapon.currentSkills.Where(s => s != null).Select(s => s.displayName))),
                });
            }
            if (weapons.current != null)
                snapshot.Held = GearId(weapons.current);
        }
        foreach (var item in inventory.item?.items ?? new List<Characters.Gear.Items.Item>())
            if (item != null)
                snapshot.Items.Add(new BuildSnapshot.Piece { Id = GearId(item), Name = Safe(() => item.displayName), Rarity = item.rarity.ToString() });
        foreach (var essence in inventory.quintessence?.items ?? new List<Characters.Gear.Quintessences.Quintessence>())
            if (essence != null)
                snapshot.Essences.Add(new BuildSnapshot.Piece { Id = GearId(essence), Name = Safe(() => essence.displayName), Rarity = essence.rarity.ToString() });
        foreach (var upgrade in inventory.upgrade?.upgrades ?? new List<Characters.Gear.Upgrades.UpgradeObject>())
            if (upgrade?.reference != null)
                snapshot.Darks.Add(new BuildSnapshot.Piece { Id = CodexCatalog.DarkId(upgrade.reference.name), Name = Safe(() => upgrade.displayName), Level = upgrade.level }); // the game counts from 1
        var fragment = Safe(() => inventory.fragment?.fragments[0]);
        if (fragment != null)
            snapshot.Fragment = new BuildSnapshot.Piece { Id = "fragment:" + fragment.name.Replace("(Clone)", "").Trim(), Name = Safe(() => fragment.displayName) };
        var inscriptions = inventory.synergy?.inscriptions;
        if (inscriptions != null)
        {
            foreach (var key in Inscription.keys)
            {
                if (key == Inscription.Key.None)
                    continue;
                var inscription = inscriptions[key];
                if (inscription != null && inscription.count > 0)
                {
                    snapshot.Inscriptions[key.ToString()] = (Safe(() => inscription.name), inscription.count, inscription.step);
                    if (Safe(() => inscription.isSuper))
                        snapshot.Tuned.Add(key.ToString());
                }
            }
        }
        return snapshot;
    }

    private static List<RunGear> ToBuild(BuildSnapshot s)
    {
        var build = new List<RunGear>();
        foreach (var p in s.Skulls)
            build.Add(new RunGear { Kind = "skull", Id = p.Id, Name = p.Name, Rarity = p.Rarity, Extra = (p.Id == s.Held ? "held|" : "|") + p.Extra });
        foreach (var p in s.Essences)
            build.Add(new RunGear { Kind = "essence", Id = p.Id, Name = p.Name, Rarity = p.Rarity });
        foreach (var p in s.Items)
            build.Add(new RunGear { Kind = "item", Id = p.Id, Name = p.Name, Rarity = p.Rarity });
        foreach (var p in s.Darks)
            build.Add(new RunGear { Kind = "dark", Id = p.Id, Name = p.Name, Value = p.Level });
        if (s.Fragment != null)
            build.Add(new RunGear { Kind = "fragment", Id = s.Fragment.Id, Name = s.Fragment.Name });
        foreach (var pair in s.Inscriptions.OrderByDescending(p => p.Value.step).ThenByDescending(p => p.Value.count))
            build.Add(new RunGear { Kind = "insc", Id = pair.Key, Name = pair.Value.name, Value = pair.Value.count, Step = pair.Value.step,
                                    Extra = s.Tuned.Contains(pair.Key) ? "tuned" : "" });
        return build;
    }

    /// <summary>A snapshot from a run file's build (a continued run's starting point).</summary>
    internal static BuildSnapshot FromBuild(IEnumerable<RunGear> build)
    {
        var s = new BuildSnapshot();
        foreach (var g in build)
        {
            var piece = new BuildSnapshot.Piece { Id = g.Id, Name = g.Name, Rarity = g.Rarity, Level = g.Value };
            switch (g.Kind)
            {
                case "skull":
                    int bar = g.Extra.IndexOf('|');
                    piece.Extra = bar >= 0 ? g.Extra.Substring(bar + 1) : g.Extra;
                    s.Skulls.Add(piece);
                    if (g.Extra.StartsWith("held"))
                        s.Held = g.Id;
                    break;
                case "item": s.Items.Add(piece); break;
                case "essence": s.Essences.Add(piece); break;
                case "dark": s.Darks.Add(piece); break;
                case "fragment": s.Fragment = piece; break;
                case "insc":
                    s.Inscriptions[g.Id] = (g.Name, g.Value, g.Step);
                    if (g.Extra == "tuned")
                        s.Tuned.Add(g.Id);
                    break;
            }
        }
        return s;
    }

    private static string GearId(Characters.Gear.Gear gear) =>
        CodexCatalog.GearId(gear.type, gear.name.Replace("(Clone)", "").Trim());

    private static bool IsRunChapter(Chapter.Type type) =>
        type is >= Chapter.Type.Chapter1 and <= Chapter.Type.Chapter5 or >= Chapter.Type.HardmodeChapter1 and <= Chapter.Type.HardmodeChapter6;

    private static bool IsCastle(Chapter.Type type) => type is Chapter.Type.Castle or Chapter.Type.HardmodeCastle;

    private static T Safe<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception) { return default; }
    }
}
