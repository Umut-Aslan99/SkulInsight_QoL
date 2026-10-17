using System.Globalization;
using System.Linq;
using DamageInsight.Codex;
using DamageInsight.History;
using DamageInsight.Lang;

namespace DamageInsight.Daily;

/// <summary>The Codex's Daily section texts (today's run, your try, what is sent). Plain logic.</summary>
public static class DailyPages
{
    public static string Mode(string mode, int level) => mode == "darkmirror" ? Loc.F("Dark Mirror {0}", level) : Loc.T("Normal");

    /// <summary>"13:21:05" until the next day's run.</summary>
    public static string Left(int seconds) => RunPages.Time(seconds);

    /// <summary>What the player's try of the day is at (one line). A finished try's points and place come from the
    /// day's board when it is loaded (Umut 2026-10-18: a run removed from the board still showed the place the server
    /// gave when it was sent; the place also changes as others play, and the day's bonuses come on top).</summary>
    public static string Status(DailyTry t, bool inDailySlot, Board board = null)
    {
        if (t == null || t.State == "")
            return Loc.T("Not played yet.");
        switch (t.State)
        {
            case "armed":
                return inDailySlot ? Loc.T("Ready: leave the castle to begin. One try!") : Loc.T("Not played yet.");
            case "started":
                return Loc.T("Running.");
            case "ended":
                return Loc.T("Over: enter your name for the board and send it.");
        }
        if (t.Pending.Length > 0)
            return Loc.T("Waiting to be sent (no connection yet).");
        if (t.Rank == -2)
            return Loc.T("Not sent to the board.");
        if (t.Rank == -1)
            return Loc.T("The server didn't take this run.");
        if (t.Rank == -3)
            return Loc.T("Not on the board: other mods were loaded (only runs with SkulInsight alone count).");
        if (board != null && board.Date == t.Date)
        {
            if (board.You != null)
                return Loc.F("On the board: {0} points, place {1} of {2}.", N(board.You.Points), board.You.Rank, board.Count);
            return Loc.T("No longer on the board (removed).");
        }
        return Loc.F("On the board: {0} points, place {1} of {2}.", N(t.Points), t.Rank, t.Of);
    }

    /// <summary>The right page of the Daily section.</summary>
    public static string TodayPage(DailyInfo day, DailyTry t, bool enabled, string problem, bool inDailySlot, string otherMods = "", Board board = null)
    {
        var sb = new System.Text.StringBuilder();
        if (!enabled)
        {
            sb.Append(PageBlocks.Section(Loc.T("Daily runs"), "pix:crown"));
            sb.Append(Loc.T("Every day one run for everyone: the same seed, the same difficulty, one try. Daily and monthly boards show who did best.")).Append('\n');
            sb.Append($"<color={RunPages.DimColor}>{Loc.T("This connects to the daily run server and sends a random player id made by the mod, the name you enter and your daily run (its numbers, build, damage and timeline: others can look at it on the board). Nothing else.")}</color>\n");
            return sb.ToString();
        }
        if (day == null)
        {
            sb.Append(problem.Length > 0 ? Loc.T("No connection to the daily run server.") : Loc.T("Loading today's run..."));
            return sb.ToString();
        }
        sb.Append(PageBlocks.Section(Loc.T("Today's run"), "pix:hourglass"));
        sb.Append($"<b>{day.Date}</b>  ·  {Mode(day.Mode, day.Level)}\n");
        sb.Append(Loc.F("A new run in {0} (midnight UTC).", Left(day.SecondsLeft))).Append('\n');
        sb.Append(PageBlocks.Section(Loc.T("Your try"), "pix:star"));
        sb.Append(Status(t, inDailySlot, board)).Append('\n');
        if (otherMods.Length > 0)
            sb.Append($"<color={RunPages.DeathColor}>{Loc.F("Other mods are loaded ({0}): today's run won't count on the board. Only runs with SkulInsight alone count.", otherMods)}</color>\n");
        sb.Append(PageBlocks.Section(Loc.T("How it works"), "pix:door"));
        sb.Append(Loc.T("Everyone gets the same seed and one try. You play in a daily save slot made from your game: every gear unlocked, the Witch at her top, every Dark Mirror level open, no DLC gear, no Steam achievements. Your own game waits as it is; \"Back to my game\" returns to it.")).Append('\n');
        sb.Append(Loc.T("One try means one go: closing the game or going back to the title screen during the run ends it (a daily run can't be continued), and it goes on the board as it was.")).Append('\n');
        sb.Append(Loc.T("Wins rank first, then how far a run got. Points: a win, stages, bosses, kills, damage, little damage taken, gold left and a fast win; harder days count more. The month adds up your days.")).Append('\n');
        sb.Append(Loc.T("The day's records (first win, fastest win, most damage, ...) and a daily streak give bonus points. Click a run on the board to look at it, see its points part by part and give it a thumbs up.")).Append('\n');
        if (problem.Length > 0)
            sb.Append($"<color={RunPages.DeathColor}>{Loc.T("No connection to the daily run server.")}</color>\n");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- a board run's points (Umut 2026-10-18)

    /// <summary>The Points tab of a board run: the run's points part by part, the difficulty, the day's records it holds,
    /// the streak bonus, the total, and how points are counted.</summary>
    public static string PointsPage(RunDetail d, BoardEntry e)
    {
        var sb = new System.Text.StringBuilder();
        e ??= d.Entry;
        sb.Append(PageBlocks.Section(Loc.T("Points"), "pix:star"));
        if (d.Breakdown.Count == 0)
            sb.Append($"<color={RunPages.DimColor}>{Loc.T("This run came from an older version: its points aren't split up.")}</color>\n");
        long sum = 0;
        double most = System.Math.Max(1, d.Breakdown.Select(p => System.Math.Abs((double)p.points)).DefaultIfEmpty(1).Max());
        foreach (var (key, value, points) in d.Breakdown)
        {
            sum += points;
            sb.Append(PageBlocks.Bar(PartName(key, value), Signed(points), System.Math.Abs(points) / most));
        }
        if (d.Breakdown.Count > 0)
            sb.Append($"{Loc.T("Sum")}: <b>{N(sum)}</b>  x{d.Factor.ToString("0.##", CultureInfo.InvariantCulture)} ({Mode(d.Mode, d.Level)})  =  <b>{N(e.Base)}</b>\n");

        var bonuses = e.Bonuses.Where(b => b.key != "streak").ToList();
        var streak = e.Bonuses.FirstOrDefault(b => b.key == "streak");
        sb.Append(PageBlocks.Section(Loc.T("The day's records"), "pix:crown"));
        if (bonuses.Count == 0)
            sb.Append($"<color={RunPages.DimColor}>{Loc.T("This run holds none of the day's records (yet).")}</color>\n");
        double best = System.Math.Max(1, bonuses.Select(b => (double)b.points).DefaultIfEmpty(1).Max());
        foreach (var (key, points, value) in bonuses)
            sb.Append(PageBlocks.Bar(BonusName(key, value), Signed(points), points / best));
        if (streak.key == "streak")
        {
            sb.Append(PageBlocks.Section(Loc.T("Daily streak"), "pix:hourglass"));
            sb.Append(PageBlocks.Bar(Loc.P("{0} day in a row", "{0} days in a row", streak.value), Signed(streak.points), 1));
        }
        sb.Append(PageBlocks.Section(Loc.T("Total"), "pix:star"));
        sb.Append($"<size=130%><b>{N(e.Points)}</b></size>  ·  {Loc.F("place {0}", e.Rank)}\n");

        sb.Append(PageBlocks.Section(Loc.T("How points are counted"), "pix:door"));
        sb.Append($"<color={RunPages.DimColor}>");
        sb.Append(Loc.T("A win 10,000 · a fast win 3 for every second under an hour · 500 per stage reached · 1,000 per boss · 5 per kill · 1 per 2,000 damage dealt · -2 per damage taken · 1 per 10 gold left. The sum is multiplied by the day's difficulty (Normal x1, Dark Mirror +0.15 per level).")).Append('\n');
        sb.Append(Loc.T("The day's records go to whoever holds them when the board is read: first win 3,000 · fastest win 3,000 · most damage 2,000 · least damage taken in a win 2,000 · most gold left 1,000 · most kills 1,000. A daily streak gives 250 for every day in a row before today (up to 2,500). Both are multiplied by the difficulty too.")).Append('\n');
        sb.Append("</color>");
        return sb.ToString();
    }

    /// <summary>A part of a run's points: what it counts and the run's number.</summary>
    public static string PartName(string key, long value) => key switch
    {
        "win" => Loc.T("Victory"),
        "speed" => $"{Loc.T("Fast win")}  <color={RunPages.DimColor}>{RunPages.Time((int)value)}</color>",
        "stages" => $"{Loc.T("Stages reached")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "bosses" => $"{Loc.T("Bosses")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "kills" => $"{Loc.T("Kills")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "damage" => $"{Loc.T("Damage dealt")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "taken" => $"{Loc.T("Damage taken")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "gold" => $"{Loc.T("Gold left")}  <color={RunPages.DimColor}>{N(value)}</color>",
        _ => key,
    };

    /// <summary>One of the day's records: its name and the number that holds it.</summary>
    public static string BonusName(string key, long value) => key switch
    {
        "firstClear" => Loc.T("First win of the day"),
        "fastest" => $"{Loc.T("Fastest win")}  <color={RunPages.DimColor}>{RunPages.Time((int)value)}</color>",
        "mostDamage" => $"{Loc.T("Most damage")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "leastTaken" => $"{Loc.T("Least damage taken in a win")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "mostGold" => $"{Loc.T("Most gold left")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "mostKills" => $"{Loc.T("Most kills")}  <color={RunPages.DimColor}>{N(value)}</color>",
        "streak" => Loc.P("{0} day in a row", "{0} days in a row", value),
        _ => key,
    };

    public static string Signed(long points) => points < 0 ? "-" + N(-points) : "+" + N(points);

    public static string N(double v) => System.Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
}
