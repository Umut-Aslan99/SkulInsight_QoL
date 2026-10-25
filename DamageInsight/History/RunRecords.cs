using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DamageInsight.History;

/// <summary>Which runs the Records page counts: all, the normal ones (rookie mode too) or the Dark Mirror ones.</summary>
public enum RecordFilter
{
    All,
    Normal,
    DarkMirror,
}

/// <summary>
/// The Records page's numbers (0.13, spec B): the slot's run history added up. Totals, bests (each with the run that
/// set it), time per skull, end bosses, killers and places of death, Dark Mirror levels. Counts every finished run,
/// shrunk ones too (a summary keeps its numbers, build, skull times and end bosses); the run being played joins when it
/// ends. Plain logic, no Unity.
/// </summary>
public sealed class RunRecords
{
    /// <summary>A best of all runs: its value and the run that set it (its page is a link).</summary>
    public sealed class Best
    {
        public double Value;
        public RunRecord Run;
    }

    /// <summary>A skull over all runs: runs it was played in, time held, wins it was held at the end of.</summary>
    public sealed class SkullTally
    {
        public string Id = "", Name = "";
        public int Runs, Seconds, Wins;
    }

    /// <summary>An end boss (RunPages.EndBosses): how often it was defeated and how often it killed you.</summary>
    public sealed class BossTally
    {
        public string Id = "";
        public int Defeated, KilledYou;
    }

    /// <summary>Something counted: a killer (Id = its Codex id when it has a page) or a place of death.</summary>
    public sealed class Tally
    {
        public string Id = "", Name = "";
        public int Count;
    }

    /// <summary>A Dark Mirror level: finished runs, wins and the fastest win there (seconds, 0 for none).</summary>
    public sealed class LevelTally
    {
        public int Level, Runs, Wins, FastestWin;
    }

    public RecordFilter Filter;
    public int Runs, Wins, Deaths, Abandoned;
    public long PlaySeconds;
    public double Kills, EliteKills, BossKills, AdventurerKills, Damage, Taken, Healed, Gold, DarkQuartz, Bones, Swaps, Revives;
    /// <summary>Heal orbs left behind (0.14) and the runs that counted them (older runs don't know).</summary>
    public double HealsMissed, HealthMissed;
    public int RunsWithHeals;

    /// <summary>The start of the slot's run history (its oldest run, whatever the filter), or default without runs.</summary>
    public DateTime Since;

    public Best FastestWin, HighestLevelWon, MostDamage, BiggestHit, MostKills, LongestRun;

    /// <summary>Most played first.</summary>
    public readonly List<SkullTally> Skulls = new();

    /// <summary>Every end boss, in the order of RunPages.EndBosses (the page shows the ones met).</summary>
    public readonly List<BossTally> Bosses = new();

    /// <summary>Most deaths first.</summary>
    public readonly List<Tally> Killers = new(), Places = new();

    /// <summary>Highest level first.</summary>
    public readonly List<LevelTally> Levels = new();

    /// <summary>Wins out of the runs that ended: an abandoned run never ended, so it doesn't count against it.</summary>
    public double WinRate => Wins + Deaths > 0 ? (double)Wins / (Wins + Deaths) : 0;

    public int SkullSeconds => Skulls.Sum(s => s.Seconds);

    public static bool Fits(RunRecord r, RecordFilter filter) => filter switch
    {
        RecordFilter.Normal => !r.DarkMirror,
        RecordFilter.DarkMirror => r.DarkMirror,
        _ => true,
    };

    public static RunRecords Of(IEnumerable<RunRecord> runs, RecordFilter filter = RecordFilter.All)
    {
        var all = runs.Where(r => r != null).ToList();
        var records = new RunRecords { Filter = filter };
        if (all.Count > 0)
            records.Since = all.Min(r => r.Started);
        // Oldest first: on a tie, the run that got there first keeps the record.
        var counted = all.Where(r => r.Result != RunResult.InProgress && Fits(r, filter))
            .OrderBy(r => r.Started).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();

        var skulls = new Dictionary<string, SkullTally>();
        var bosses = new Dictionary<string, BossTally>();
        foreach (var (_, first) in RunPages.EndBosses)
            if (!bosses.ContainsKey(first))
                records.Bosses.Add(bosses[first] = new BossTally { Id = first });
        var killers = new Dictionary<string, Tally>();
        var places = new Dictionary<string, Tally>();
        var levels = new Dictionary<int, LevelTally>();

        foreach (var r in counted)
        {
            bool won = r.Result == RunResult.Ending;
            // A run found halfway (no file at its start) only knows part of its time: no time records from it.
            bool wholeTime = r.Number(RunNumbers.JoinedLate) <= 0 && r.PlaySeconds > 0;
            records.Runs++;
            if (won)
                records.Wins++;
            else if (r.Result == RunResult.Death)
                records.Deaths++;
            else
                records.Abandoned++;
            records.PlaySeconds += r.PlaySeconds;
            // The end screen's numbers (an abandoned run has none: it never reached an end screen).
            records.Kills += r.Number(RunNumbers.Kills);
            records.EliteKills += r.Number(RunNumbers.EliteKills);
            records.BossKills += r.Number(RunNumbers.BossKills);
            records.AdventurerKills += r.Number(RunNumbers.AdventurerKills);
            records.Damage += r.Number(RunNumbers.TotalDamage);
            records.Taken += r.Number(RunNumbers.DamageTaken);
            records.Healed += r.Number(RunNumbers.Healed);
            records.Gold += r.Number(RunNumbers.Gold);
            records.DarkQuartz += r.Number(RunNumbers.DarkQuartz);
            records.Bones += r.Number(RunNumbers.Bones);
            records.Swaps += r.Number(RunNumbers.Swaps);
            records.Revives += r.Number(RunNumbers.Revives);
            if (r.Numbers.ContainsKey(RunNumbers.HealsMissed))
            {
                records.RunsWithHeals++;
                records.HealsMissed += r.Number(RunNumbers.HealsMissed);
                records.HealthMissed += r.Number(RunNumbers.HealthMissed);
            }

            if (won && wholeTime)
                Better(ref records.FastestWin, r.PlaySeconds, r, lower: true);
            if (won && r.DarkMirror && (records.HighestLevelWon == null || r.Level > records.HighestLevelWon.Value))
                records.HighestLevelWon = new Best { Value = r.Level, Run = r };
            Better(ref records.MostDamage, r.Number(RunNumbers.TotalDamage), r);
            Better(ref records.BiggestHit, r.Number(RunNumbers.BestDamage), r);
            Better(ref records.MostKills, r.Number(RunNumbers.Kills), r);
            if (wholeTime)
                Better(ref records.LongestRun, r.PlaySeconds, r);

            // Skulls: every skull held for a while, and the one held at the end (a win counts for it).
            var held = r.Build.FirstOrDefault(g => g.Kind == "skull" && g.Extra.StartsWith("held", StringComparison.Ordinal));
            var played = r.SkullSeconds.Where(p => p.Value > 0).Select(p => p.Key).ToList();
            if (held != null && !played.Contains(held.Id))
                played.Add(held.Id);
            foreach (var id in played)
            {
                if (!skulls.TryGetValue(id, out var skull))
                    skulls[id] = skull = new SkullTally { Id = id };
                skull.Runs++;
                skull.Seconds += r.SkullSeconds.TryGetValue(id, out int seconds) ? seconds : 0;
                if (won && held?.Id == id)
                    skull.Wins++;
                // The newest run's name wins (runs come oldest first).
                string name = r.SkullNames.TryGetValue(id, out var n) ? n : held?.Id == id ? held.Name : "";
                if (name.Length > 0)
                    skull.Name = name;
            }

            foreach (var id in RunPages.EndBossIds(r))
                if (bosses.TryGetValue(id, out var boss))
                    boss.Defeated++;

            if (r.DarkMirror)
            {
                if (!levels.TryGetValue(r.Level, out var level))
                    levels[r.Level] = level = new LevelTally { Level = r.Level };
                level.Runs++;
                if (won)
                {
                    level.Wins++;
                    if (wholeTime && (level.FastestWin == 0 || r.PlaySeconds < level.FastestWin))
                        level.FastestWin = r.PlaySeconds;
                }
            }

            if (r.Result != RunResult.Death)
                continue;
            if (r.Killer.Length > 0 || r.KillerId.Length > 0)
            {
                // By Codex id when it has a page (one row whatever language it was named in), else by name.
                string key = r.KillerId.Length > 0 ? r.KillerId : "name:" + r.Killer;
                if (!killers.TryGetValue(key, out var killer))
                    killers[key] = killer = new Tally { Id = r.KillerId };
                killer.Count++;
                if (r.Killer.Length > 0)
                    killer.Name = r.Killer;
                if (RunPages.EndBossOf(r.KillerId) is { } bossId && bosses.TryGetValue(bossId, out var boss))
                    boss.KilledYou++;
            }
            string where = PlaceKey(r);
            if (where.Length > 0)
            {
                if (!places.TryGetValue(where, out var place))
                    places[where] = place = new Tally { Id = where };
                place.Count++;
                place.Name = RunPages.Where(r);
            }
        }

        records.Skulls.AddRange(skulls.Values.OrderByDescending(s => s.Seconds).ThenByDescending(s => s.Runs).ThenBy(s => s.Id, StringComparer.Ordinal));
        records.Killers.AddRange(killers.Values.OrderByDescending(k => k.Count).ThenBy(k => k.Name, StringComparer.Ordinal));
        records.Places.AddRange(places.Values.OrderByDescending(p => p.Count).ThenBy(p => p.Name, StringComparer.Ordinal));
        records.Levels.AddRange(levels.Values.OrderByDescending(l => l.Level));
        return records;
    }

    private static void Better(ref Best best, double value, RunRecord run, bool lower = false)
    {
        if (value <= 0)
            return;
        if (best == null || (lower ? value < best.Value : value > best.Value))
            best = new Best { Value = value, Run = run };
    }

    /// <summary>
    /// Where a run ended, the same stage in normal and Dark Mirror runs (the Dark Mirror's chapters 1-5 are the normal
    /// ones' stages, with the same names): "Chapter3/1". Runs without a chapter go by their stage name.
    /// </summary>
    public static string PlaceKey(RunRecord r) =>
        r.EndChapter.Length > 0 ? GameRecords.NormalChapter(r.EndChapter) + "/" + r.EndStage.ToString(CultureInfo.InvariantCulture) : r.EndStageName;
}

/// <summary>
/// The game's own permanent records of the save (they go back to its start, unlike the run history): deaths, Dark
/// Mirror clears, the hidden boss and the best time per stage (GameData.Record, the time the loading screen shows when
/// a stage begins). Read in the game by the Records page; plain data, no Unity.
/// </summary>
public sealed class GameRecords
{
    /// <summary>The save keys of the best stage times: "Record/bestTime/&lt;Chapter.Type&gt;/&lt;stage&gt;".</summary>
    public const string BestTimePrefix = "Record/bestTime/";

    public sealed class StageTime
    {
        /// <summary>The game's Chapter.Type name: "Chapter2", "HardmodeChapter6".</summary>
        public string Chapter = "";
        public int Stage;
        public int Seconds;
        /// <summary>The stage's name in the current language (filled in the game), or "".</summary>
        public string Name = "";

        public bool DarkMirror => Chapter.StartsWith("Hardmode", StringComparison.Ordinal);
    }

    /// <summary>The save's deaths (GameData.Progress.deaths); -1 when it could not be read.</summary>
    public int Deaths = -1;
    /// <summary>The highest Dark Mirror level cleared (-1: none yet) and how many clears.</summary>
    public int ClearedLevel = -1, ClearedCount;
    /// <summary>The highest Dark Mirror level the hidden boss was defeated at (-1: never).</summary>
    public int HiddenBossLevel = -1;
    /// <summary>The Demon King Castle's emperor was defeated.</summary>
    public bool EmperorCleared;

    /// <summary>In the game's chapter order, normal chapters first.</summary>
    public readonly List<StageTime> BestTimes = new();

    /// <summary>Takes a save value if it is a best stage time ("Record/bestTime/Chapter2/1"); the game's "none" is int.MaxValue.</summary>
    public bool AddBestTime(string key, int seconds)
    {
        if (key == null || !key.StartsWith(BestTimePrefix, StringComparison.Ordinal) || seconds <= 0 || seconds == int.MaxValue)
            return false;
        var parts = key.Substring(BestTimePrefix.Length).Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage))
            return false;
        BestTimes.RemoveAll(t => t.Chapter == parts[0] && t.Stage == stage);
        BestTimes.Add(new StageTime { Chapter = parts[0], Stage = stage, Seconds = seconds });
        BestTimes.Sort((a, b) => Order(a).CompareTo(Order(b)));
        return true;
    }

    private static int Order(StageTime t) => (t.DarkMirror ? 1000 : 0) + ChapterNumber(t.Chapter) * 100 + t.Stage;

    public static int ChapterNumber(string chapter) =>
        int.TryParse(new string(chapter.Where(char.IsDigit).ToArray()), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    /// <summary>The normal chapter a Dark Mirror chapter repeats ("HardmodeChapter2" → "Chapter2"; its sixth stays its own).</summary>
    public static string NormalChapter(string chapter) =>
        chapter.StartsWith("Hardmode", StringComparison.Ordinal) && chapter != "HardmodeChapter6" ? chapter.Substring("Hardmode".Length) : chapter;

    /// <summary>The game's text key of a stage's name, as Chapter.stageName builds it (the Demon King Castle has one name).</summary>
    public static string StageNameKey(string chapter, int stage) =>
        chapter == "HardmodeChapter6" ? "map/chapter6/0" : $"map/{NormalChapter(chapter)}/{stage.ToString(CultureInfo.InvariantCulture)}";
}
