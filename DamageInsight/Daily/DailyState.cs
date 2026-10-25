using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.Daily;

/// <summary>
/// A day's try on this PC: "armed" (the daily slot waits in the castle), "started" (the run left the castle),
/// "ended" (the run is over, the name for the board is asked), "finished" (handed to the server, or kept in Pending).
/// </summary>
public sealed class DailyTry
{
    public string Date = "";
    public string State = "";
    /// <summary>The day's run as the server gave it when the try was armed.</summary>
    public int Seed, Level;
    public string Mode = "normal";
    /// <summary>An ended run's numbers (DailyResult JSON) until the name is given.</summary>
    public string Numbers = "";
    /// <summary>The whole run (its run history file) until the server took it: others can look at it from the board.</summary>
    public string Run = "";
    /// <summary>The run history's id of the daily run.</summary>
    public string RunId = "";
    public string Result = "";
    public int Points, Rank, Of;
    /// <summary>A finished run's body not sent yet (no connection): sent again later.</summary>
    public string Pending = "";

    public bool Played => State is "started" or "ended" or "finished";
}

/// <summary>
/// The daily run's state on this PC (not per save slot): the random player id the server knows this player by (made
/// once, nothing personal), the name last used on the board, and the tries of the last days. Daily/state.json.
/// Plain logic and the file.
/// </summary>
public sealed class DailyState
{
    public const int KeepDays = 60;

    public string PlayerId = "";
    public string Name = "";
    /// <summary>The save slot the daily slot was entered from (it goes back there).</summary>
    public int Origin = 1;
    public readonly Dictionary<string, DailyTry> Tries = new();

    /// <summary>The server's counters of this player (GET /api/me), kept for the Achievements page when offline.</summary>
    public int LikesReceived, Streak, BestStreak, DaysPlayed;

    /// <summary>SkulInsight's own daily achievements earned (id → when, UTC): the daily player's, the same in every slot.</summary>
    public readonly Dictionary<string, DateTime> Achievements = new();

    /// <summary>Takes the server's counters; the daily achievements they reach that weren't earned yet (now earned).</summary>
    public List<string> UpdateCounters(int likes, int streak, int bestStreak, int daysPlayed, DateTime nowUtc)
    {
        LikesReceived = Math.Max(0, likes);
        Streak = Math.Max(0, streak);
        BestStreak = Math.Max(BestStreak, Math.Max(streak, bestStreak)); // a removed run doesn't take a streak back
        DaysPlayed = Math.Max(0, daysPlayed);
        var earned = new List<string>();
        foreach (var info in DamageInsight.Achievements.AchievementCatalog.Daily)
            if (!Achievements.ContainsKey(info.Id) && Counter(info.Counts) >= info.Target)
            {
                Achievements[info.Id] = nowUtc;
                earned.Add(info.Id);
            }
        return earned;
    }

    /// <summary>The counter a daily achievement counts ("likes", "streak").</summary>
    public int Counter(string counts) => counts == "likes" ? LikesReceived : BestStreak;

    public DailyTry TryOf(string date) => Tries.TryGetValue(date ?? "", out var t) ? t : null;

    public DailyTry Ensure(string date)
    {
        if (!Tries.TryGetValue(date, out var t))
            Tries[date] = t = new DailyTry { Date = date };
        return t;
    }

    /// <summary>
    /// Follows the server's word on a day's try (GET /api/me, Umut 2026-10-18: "if no run from me has been recorded,
    /// then I can do the run"): a try this PC played and handed over that the server doesn't have (the admin removed
    /// it) is dropped, so the day can be played again. A try not handed over yet (no connection) stays. True when dropped.
    /// </summary>
    public bool FollowServer(string date, string serverState)
    {
        var t = TryOf(date);
        if (t == null || serverState != "none" || t.State != "finished" || t.Pending.Length > 0)
            return false;
        Tries.Remove(date);
        return true;
    }

    /// <summary>Makes the player id once (a random id: the server can't tell who it is).</summary>
    public void EnsurePlayer()
    {
        if (PlayerId.Length < 8)
            PlayerId = Guid.NewGuid().ToString("N");
    }

    /// <summary>Forgets tries older than <see cref="KeepDays"/> days (dates are "yyyy-MM-dd", so they sort).</summary>
    public void Trim(string today)
    {
        if (!DateTime.TryParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return;
        string oldest = day.AddDays(-KeepDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var date in Tries.Keys.Where(d => string.CompareOrdinal(d, oldest) < 0).ToList())
            Tries.Remove(date);
    }

    public string ToJson()
    {
        var sb = new StringBuilder("{\n");
        sb.Append("\"player\":").Append(LogJson.Quote(PlayerId)).Append(",\n\"name\":").Append(LogJson.Quote(Name))
          .Append(",\n\"origin\":").Append(Origin)
          .Append(",\n\"me\":{\"likes\":").Append(LikesReceived).Append(",\"streak\":").Append(Streak).Append(",\"best\":").Append(BestStreak)
          .Append(",\"days\":").Append(DaysPlayed).Append("},\n\"achievements\":{")
          .Append(string.Join(",", Achievements.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a =>
              LogJson.Quote(a.Key) + ":" + LogJson.Quote(a.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))))
          .Append("},\n\"tries\":[");
        sb.Append(string.Join(",", Tries.Values.OrderBy(t => t.Date, StringComparer.Ordinal).Select(t =>
            "\n {\"date\":" + LogJson.Quote(t.Date) + ",\"state\":" + LogJson.Quote(t.State) + ",\"run\":" + LogJson.Quote(t.RunId) +
            ",\"result\":" + LogJson.Quote(t.Result) + ",\"points\":" + t.Points + ",\"rank\":" + t.Rank + ",\"of\":" + t.Of +
            ",\"seed\":" + t.Seed + ",\"mode\":" + LogJson.Quote(t.Mode) + ",\"level\":" + t.Level +
            ",\"numbers\":" + LogJson.Quote(t.Numbers) + ",\"record\":" + LogJson.Quote(t.Run) + ",\"pending\":" + LogJson.Quote(t.Pending) + "}")));
        sb.Append("\n]\n}\n");
        return sb.ToString();
    }

    public static DailyState FromJson(string json)
    {
        var state = new DailyState();
        var n = DailyInfo.Parse(json);
        if (n.IsNull)
            return state;
        state.PlayerId = n.Str("player") ?? "";
        state.Name = n.Str("name") ?? "";
        state.Origin = Math.Max(1, (int)n.Num("origin", 1));
        var me = n.Child("me");
        if (!me.IsNull)
        {
            state.LikesReceived = (int)me.Num("likes");
            state.Streak = (int)me.Num("streak");
            state.BestStreak = (int)me.Num("best");
            state.DaysPlayed = (int)me.Num("days");
        }
        if (n.Raw("achievements") is Dictionary<string, object> achievements)
            foreach (var pair in achievements)
                if (pair.Value is string text && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                    state.Achievements[pair.Key] = at;
        foreach (var t in n.List("tries"))
        {
            var date = t.Str("date") ?? "";
            if (date.Length == 0)
                continue;
            state.Tries[date] = new DailyTry
            {
                Date = date, State = t.Str("state") ?? "", RunId = t.Str("run") ?? "", Result = t.Str("result") ?? "",
                Points = (int)t.Num("points"), Rank = (int)t.Num("rank"), Of = (int)t.Num("of"), Pending = t.Str("pending") ?? "",
                Seed = (int)t.Num("seed"), Mode = t.Str("mode") ?? "normal", Level = (int)t.Num("level"), Numbers = t.Str("numbers") ?? "",
                Run = t.Str("record") ?? "",
            };
        }
        return state;
    }

    public static DailyState Load(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path, Encoding.UTF8)) : new DailyState(); }
        catch (Exception) { return new DailyState(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }
}
