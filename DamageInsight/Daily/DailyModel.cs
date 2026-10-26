using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.History;
using DamageInsight.Recording;

namespace DamageInsight.Daily;

/// <summary>
/// The day's boards (0.15, Umut 2026-10-26: "Normal ... DM random level ... DM 10", each "with dlc and without"):
/// "normal", "dm", "dm10", and the same with "+dlc". A try is kept under its day and board (the "normal" board under
/// the day alone, as before the boards).
/// </summary>
public static class DailyBoards
{
    public static readonly string[] Bases = { "normal", "dm", "dm10" };

    public static string Of(string baseName, bool dlc) => dlc ? baseName + "+dlc" : baseName;

    public static string BaseOf(string board) => (board ?? "normal").Split('+')[0];

    public static bool HasDlc(string board) => board != null && board.EndsWith("+dlc", StringComparison.Ordinal);

    public static bool IsBoard(string board) => Bases.Any(b => board == b || board == b + "+dlc");

    /// <summary>The key a try is kept under: the day for "normal", "day|board" for the others.</summary>
    public static string Key(string date, string board) =>
        string.IsNullOrEmpty(board) || board == "normal" ? date ?? "" : $"{date}|{board}";

    public static string DateOf(string key) => (key ?? "").Split('|')[0];
}

/// <summary>A board's run of the day as the server gives it (GET /api/dailies, or /api/daily from an older server).</summary>
public sealed class DailyInfo
{
    public string Date = "";
    public string Board = "normal";
    public bool Dlc;
    public int Seed;
    /// <summary>"normal" or "darkmirror".</summary>
    public string Mode = "normal";
    public int Level;
    public int Formula;
    public int SecondsLeft;

    public bool DarkMirror => Mode == "darkmirror";

    public static DailyInfo FromJson(string json)
    {
        var n = Parse(json);
        if (n.IsNull || n.Str("date") is not { Length: > 0 } date)
            return null;
        return new DailyInfo
        {
            Date = date, Seed = (int)n.Num("seed"), Mode = n.Str("mode") ?? "normal", Level = (int)n.Num("level"),
            Formula = (int)n.Num("formula"), SecondsLeft = (int)n.Num("secondsLeft"),
            Board = n.Str("board") is { Length: > 0 } board ? board : "normal", Dlc = n.Bool("dlc"),
        };
    }

    /// <summary>Every board of the day (GET /api/dailies); null when the answer has none (an older server).</summary>
    public static List<DailyInfo> ListFromJson(string json)
    {
        var n = Parse(json);
        if (n.IsNull || n.Str("date") is not { Length: > 0 } date || !n.Has("boards"))
            return null;
        var list = new List<DailyInfo>();
        foreach (var b in n.List("boards"))
        {
            string board = b.Str("board") ?? "";
            if (!DailyBoards.IsBoard(board))
                continue;
            list.Add(new DailyInfo
            {
                Date = date, Board = board, Dlc = b.Bool("dlc"), Seed = (int)b.Num("seed"), Mode = b.Str("mode") ?? "normal",
                Level = (int)b.Num("level"), Formula = (int)n.Num("formula"), SecondsLeft = (int)n.Num("secondsLeft"),
            });
        }
        return list.Count > 0 ? list : null;
    }

    internal static Node Parse(string json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? default : GearDoc.ParseNode(json); }
        catch (Exception) { return default; }
    }
}

/// <summary>One line of a board: the day's (a run) or the month's (a player's sum).</summary>
public sealed class BoardEntry
{
    public int Rank, Points, PlaySeconds, StagesReached, BossKills, Kills, Days, Wins;
    public string Name = "", Result = "", Skull = "";
    public readonly List<string> EndBosses = new();
    public bool You;
    /// <summary>The line's id for looking at its run (says nothing about the player behind it), and whether it was sent.</summary>
    public string Id = "";
    public bool HasRun;
    /// <summary>The run's own points; <see cref="Points"/> adds the day's record bonuses.</summary>
    public int Base, Likes, Streak;
    /// <summary>The player asking liked this run.</summary>
    public bool Liked;
    /// <summary>The day's records this run holds: (key, points, value).</summary>
    public readonly List<(string key, int points, int value)> Bonuses = new();

    public bool Won => Result == "win";

    internal static BoardEntry FromNode(Node n)
    {
        var e = new BoardEntry
        {
            Rank = (int)n.Num("rank"), Points = (int)n.Num("points"), PlaySeconds = (int)n.Num("playSeconds"),
            StagesReached = (int)n.Num("stagesReached"), BossKills = (int)n.Num("bossKills"), Kills = (int)n.Num("kills"),
            Days = (int)n.Num("days"), Wins = (int)n.Num("wins"), Name = n.Str("name") ?? "", Result = n.Str("result") ?? "",
            Skull = n.Str("skull") ?? "", You = n.Bool("you"), Id = n.Str("id") ?? "", HasRun = n.Bool("hasRun"),
            Base = (int)n.Num("base"), Likes = (int)n.Num("likes"), Streak = (int)n.Num("streak"), Liked = n.Bool("liked"),
        };
        if (n.Raw("endBosses") is List<object> bosses)
            e.EndBosses.AddRange(bosses.OfType<string>());
        foreach (var b in n.List("bonuses"))
            e.Bonuses.Add((b.Str("key") ?? "", (int)b.Num("points"), (int)b.Num("value")));
        return e;
    }
}

/// <summary>A board (GET /api/board or /api/board/month).</summary>
public sealed class Board
{
    public string Date = "", Month = "", Mode = "", Level = "";
    /// <summary>Which of the day's boards (an older server: "normal").</summary>
    public string Name = "normal";
    public int Count;
    /// <summary>The day's difficulty factor (the points and the bonuses are multiplied by it).</summary>
    public double Factor = 1;
    public readonly List<BoardEntry> Entries = new();
    public BoardEntry You;

    public static Board FromJson(string json)
    {
        var n = DailyInfo.Parse(json);
        if (n.IsNull || !n.Has("entries"))
            return null;
        var board = new Board
        {
            Date = n.Str("date") ?? "", Month = n.Str("month") ?? "", Mode = n.Str("mode") ?? "",
            Level = ((int)n.Num("level")).ToString(CultureInfo.InvariantCulture), Count = (int)n.Num("count"),
            Factor = n.Num("factor", 1), Name = n.Str("board") is { Length: > 0 } name ? name : "normal",
        };
        foreach (var e in n.List("entries"))
            board.Entries.Add(BoardEntry.FromNode(e));
        var you = n.Child("you");
        if (!you.IsNull)
            board.You = BoardEntry.FromNode(you);
        return board;
    }
}

/// <summary>A board line's whole run and its points part by part (GET /api/runs/detail).</summary>
public sealed class RunDetail
{
    public string Date = "", Mode = "normal";
    public int Level;
    public double Factor = 1;
    public BoardEntry Entry;
    /// <summary>The points before the difficulty factor: (key, the run's number, points).</summary>
    public readonly List<(string key, long value, int points)> Breakdown = new();
    /// <summary>The run as its player's run history kept it (null when it wasn't sent: an older version).</summary>
    public RunRecord Run;

    public static RunDetail FromJson(string json)
    {
        var n = DailyInfo.Parse(json);
        if (n.IsNull || n.Child("entry").IsNull)
            return null;
        var d = new RunDetail
        {
            Date = n.Str("date") ?? "", Mode = n.Str("mode") ?? "normal", Level = (int)n.Num("level"), Factor = n.Num("factor", 1),
            Entry = BoardEntry.FromNode(n.Child("entry")),
        };
        foreach (var p in n.List("breakdown"))
            d.Breakdown.Add((p.Str("key") ?? "", (long)p.Num("value"), (int)p.Num("points")));
        var run = n.Child("run");
        if (!run.IsNull && RunRecord.FromNode(run) is { } record)
        {
            // Someone else's run: no death cam picture came with it, and its star is its player's.
            record.Picture = false;
            record.Favourite = false;
            d.Run = record;
        }
        return d;
    }
}

/// <summary>What the server said to a finished run (POST /api/runs/finish).</summary>
public sealed class FinishAnswer
{
    public string Name = "";
    public int Points, Rank, Of;
    /// <summary>The run was played with other mods (or gear the vanilla game doesn't know): not on the boards.</summary>
    public bool Modded;

    public static FinishAnswer FromJson(string json)
    {
        var n = DailyInfo.Parse(json);
        if (n.IsNull || !n.Bool("ok"))
            return null;
        return new FinishAnswer
        {
            Name = n.Str("name") ?? "", Points = (int)n.Num("points"), Rank = (int)n.Num("rank"), Of = (int)n.Num("of"), Modded = n.Bool("modded"),
        };
    }
}

/// <summary>The numbers a finished daily run sends (the server turns them into points).</summary>
public sealed class DailyResult
{
    public string Result = "death";   // "win", "death", "abandoned"
    public int PlaySeconds, Kills, BossKills, StagesReached, Maps;
    public long TotalDamage, DamageTaken, GoldLeft;
    public readonly List<string> EndBosses = new();
    public string Skull = "";
    /// <summary>The other mods loaded (GUIDs) and gear the vanilla game doesn't know: such a run doesn't count.</summary>
    public readonly List<string> Mods = new();
    public bool UnknownGear;

    public string NumbersJson()
    {
        var sb = new StringBuilder("{");
        sb.Append("\"result\":").Append(LogJson.Quote(Result))
          .Append(",\"playSeconds\":").Append(PlaySeconds)
          .Append(",\"kills\":").Append(Kills)
          .Append(",\"bossKills\":").Append(BossKills)
          .Append(",\"stagesReached\":").Append(StagesReached)
          .Append(",\"totalDamage\":").Append(TotalDamage)
          .Append(",\"damageTaken\":").Append(DamageTaken)
          .Append(",\"goldLeft\":").Append(GoldLeft)
          .Append(",\"maps\":").Append(Maps)
          .Append(",\"endBosses\":[").Append(string.Join(",", EndBosses.Select(LogJson.Quote))).Append(']')
          .Append(",\"skull\":").Append(LogJson.Quote(Skull))
          .Append(",\"mods\":[").Append(string.Join(",", Mods.Select(LogJson.Quote))).Append(']')
          .Append(",\"unknownGear\":").Append(UnknownGear ? "true" : "false")
          .Append('}');
        return sb.ToString();
    }
}
