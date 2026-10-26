using System;
using System.Globalization;
using System.Text;
using DamageInsight.Codex;
using DamageInsight.History;
using DamageInsight.Lang;

namespace DamageInsight.Achievements;

/// <summary>What the book shows of one achievement: this slot's state, Steam's, the progress and the links.</summary>
public sealed class AchievementView
{
    public AchievementInfo Info;
    /// <summary>This slot's, or null (not earned here).</summary>
    public EarnedAchievement Earned;
    public bool Unveiled = true;
    /// <summary>Your Steam account has it (null: Steam can't say).</summary>
    public bool? OnSteamAccount;
    public DateTime? SteamAt;
    /// <summary>Steam's own share of all players (null: the table's).</summary>
    public double? Percent;
    public (int have, int need)? Progress;
    /// <summary>The legendary skull's Codex page and name (skull achievements), the boss's name (boss ones).</summary>
    public string SkullPage = "", SkullName = "", BossName = "";
    /// <summary>The date of the run it was earned in (the link's text), "" when that run is gone.</summary>
    public string RunDate = "";
    /// <summary>Daily runs are turned on (SkulInsight's daily achievements need them).</summary>
    public bool DailyEnabled = true;
    /// <summary>Its reward in words (0.15), "" for none; and why rewards don't apply now ("" when they do).</summary>
    public string Reward = "", RewardOff = "";
}

/// <summary>The Achievements tab's texts (0.14): the tiles and the page of the selected one. Plain logic, no Unity.</summary>
public static class AchievementPages
{
    /// <summary>A tile's page id in the book.</summary>
    public const string Prefix = "ach:";

    public static string IdOf(AchievementInfo info) => Prefix + info.Id;

    public static AchievementInfo InfoOf(string pageId) =>
        pageId != null && pageId.StartsWith(Prefix, StringComparison.Ordinal) ? AchievementCatalog.Get(pageId.Substring(Prefix.Length)) : null;

    public static string Title(AchievementView v) => v.Unveiled ? v.Info.Name : Loc.T("Hidden achievement");

    public static string TileText(AchievementView v) => v.Unveiled ? v.Info.Description : Loc.T("Hold the mouse button on it to reveal it.");

    public static string Subtitle(AchievementView v)
    {
        var e = v.Earned;
        if (v.Info.Kind == AchievementKind.Daily)
            return e == null ? Loc.T("Not earned yet") : Loc.F("Earned on {0}", Day(e.At));
        if (e == null)
            return v.Unveiled ? Loc.T("Not earned in this slot yet") : Loc.T("Hidden achievement");
        return e.How == EarnedHow.Save ? Loc.T("Earned in this slot (before 0.14)") : Loc.F("Earned in this slot on {0}", Day(e.At));
    }

    public static string Text(AchievementView v)
    {
        var sb = new StringBuilder();
        var info = v.Info;
        if (!v.Unveiled)
        {
            sb.Append(PageBlocks.Section(Loc.T("Hidden achievement"), "pix:trophy"));
            sb.Append(Loc.T("It gives part of the story away. Hold the mouse button on it (on its tile or on the picture above) for 4 seconds to reveal it. It also shows by itself once you meet what it is about, or when you earn it.")).Append('\n');
            sb.Append(Dim(Loc.T("You can turn the veils off on the settings page (Codex: Veil secret achievements)."))).Append('\n');
            return sb.ToString();
        }

        sb.Append(PageBlocks.Section(Loc.T("How to earn it"), "pix:flag"));
        sb.Append(info.Description).Append('\n');
        if (info.Boss.Length > 0 && v.BossName.Length > 0)
            sb.Append(Loc.F("Boss: {0}", CodexPages.LinkTo(info.Boss, v.BossName))).Append('\n');
        if (v.SkullPage.Length > 0)
            sb.Append(Loc.F("Skull: {0}", CodexPages.LinkTo(v.SkullPage, v.SkullName))).Append('\n');
        if (v.Progress.HasValue)
        {
            var (have, need) = v.Progress.Value;
            sb.Append(PageBlocks.Bar(Loc.T("Progress in this save"), $"{RunPages.N(have)} / {RunPages.N(need)}", need > 0 ? (double)have / need : 0));
        }

        string tip = info.Tip;
        if (tip.Length > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Tip"), "pix:star"));
            sb.Append(tip).Append('\n');
        }

        if (v.Reward.Length > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Reward"), "pix:chest"));
            sb.Append(v.Reward).Append('\n');
            sb.Append(Dim(v.RewardOff.Length > 0 ? v.RewardOff
                : v.Earned != null ? Loc.T("Earned: it counts in every run of this slot.") : Loc.T("Earn it in this slot and it counts in every run."))).Append('\n');
        }

        if (info.Kind == AchievementKind.Daily)
        {
            sb.Append(PageBlocks.Section(Loc.T("Daily runs"), "pix:thumb"));
            sb.Append(v.Earned == null ? Loc.T("Not earned yet.") : Loc.F("Earned on {0}.", Day(v.Earned.At))).Append('\n');
            if (!v.DailyEnabled)
                sb.Append(Loc.T("Turn on Daily runs (online) on the settings page to earn it.")).Append('\n');
            sb.Append(Dim(Loc.T("SkulInsight's own achievement for daily runs: it belongs to your daily player, the same in every save slot, and is not on Steam."))).Append('\n');
            return sb.ToString();
        }

        sb.Append(PageBlocks.Section(Loc.T("This slot"), "pix:crown"));
        sb.Append(Earned(v)).Append('\n');

        sb.Append(PageBlocks.Section("Steam", "pix:heart"));
        if (!info.OnSteam)
            sb.Append(Loc.T("Not a Steam achievement: the game checks it like the others, but Steam has no such achievement. SkulInsight keeps it for each slot.")).Append('\n');
        else
        {
            sb.Append(v.OnSteamAccount switch
            {
                true when v.SteamAt.HasValue => Loc.F("Your Steam account has it (since {0}).", Day(v.SteamAt.Value)),
                true => Loc.T("Your Steam account has it."),
                false => Loc.T("Your Steam account doesn't have it yet."),
                _ => Loc.T("Steam didn't answer (offline?)."),
            }).Append('\n');
            double percent = v.Percent ?? info.Percent;
            sb.Append(Dim(Loc.F("{0} % of all players have it.", percent.ToString("0.0", CultureInfo.InvariantCulture)))).Append('\n');
        }
        return sb.ToString();
    }

    private static string Earned(AchievementView v)
    {
        var e = v.Earned;
        if (e == null)
            return Loc.T("Not earned in this slot yet. Every save slot earns its achievements on its own, also the ones your Steam account has.");
        return e.How switch
        {
            EarnedHow.Save => Loc.T("Proven by this slot's save: it was earned before SkulInsight kept achievements (0.14)."),
            EarnedHow.Steam => Loc.F("Taken over from your Steam account (earned there on {0}).", Day(e.At)),
            _ when e.Run.Length > 0 && v.RunDate.Length > 0 =>
                Loc.F("Earned on {0}, in the run of {1}.", Day(e.At), CodexPages.LinkTo(RecordPages.RunLink + e.Run, v.RunDate)),
            _ => Loc.F("Earned on {0}.", Day(e.At)),
        };
    }

    /// <summary>The header line: this slot's count and Steam's.</summary>
    public static string Count(int slot, int total, int steam, int steamTotal) =>
        steam < 0 ? Loc.F("{0} / {1} in this slot", slot, total) : Loc.F("{0} / {1} in this slot · Steam {2} / {3}", slot, total, steam, steamTotal);

    private static string Day(DateTime utc) => RunPages.Date(utc.ToLocalTime());

    private static string Dim(string text) => $"<color={RunPages.DimColor}>{text}</color>";
}
