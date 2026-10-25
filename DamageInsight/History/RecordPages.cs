using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Codex;
using DamageInsight.Lang;

namespace DamageInsight.History;

/// <summary>
/// The Records page's texts (0.13, spec B; book: You → Records): a row per kind of record for the left page (with a
/// headline number) and the right page of each as PageBlocks text, from <see cref="RunRecords"/> (the run history) and
/// <see cref="GameRecords"/> (the game's own counters). Plain logic, no Unity.
/// </summary>
public static class RecordPages
{
    public enum Page { Totals, Bests, Skulls, Bosses, Deaths, Game }

    /// <summary>The ids the book selects on the left page ("records:bests").</summary>
    public const string IdPrefix = "records:";

    /// <summary>A link to a run's page ("run:&lt;id&gt;"): the book opens it in the Runs tab.</summary>
    public const string RunLink = "run:";

    private const string Dim = RunPages.DimColor;
    private const int ListMax = 12, SkullMax = 25;

    /// <summary>The hidden boss and the emperor of the game's own records, named like the run pages name them.</summary>
    private const string HiddenBossId = "enemy:DarkSkul1", EmperorId = "enemy:Alexander1";

    public static string IdOf(Page page) => IdPrefix + page.ToString().ToLowerInvariant();

    /// <summary>The page of a selected id; anything else (nothing selected yet) is the totals.</summary>
    public static Page PageOf(string id)
    {
        foreach (Page page in Enum.GetValues(typeof(Page)))
            if (IdOf(page) == id)
                return page;
        return Page.Totals;
    }

    public static string Title(Page page) => page switch
    {
        Page.Bests => Loc.T("Bests"),
        Page.Skulls => Loc.T("Skulls"),
        Page.Bosses => Loc.T("Bosses"),
        Page.Deaths => Loc.T("Deaths"),
        Page.Game => Loc.T("The game's records"),
        _ => Loc.T("Totals"),
    };

    public static string FilterName(RecordFilter filter) => filter switch
    {
        RecordFilter.Normal => Loc.T("Normal"),
        RecordFilter.DarkMirror => Loc.T("Dark Mirror"),
        _ => Loc.T("All"),
    };

    /// <summary>A page's picture (page icon spec): the most played skull, the most defeated end boss, or one of our icons.</summary>
    public static string Icon(Page page, RunRecords r) => page switch
    {
        Page.Bests => "pix:crown",
        Page.Skulls => r.Skulls.Count > 0 ? "codex:" + r.Skulls[0].Id : "pix:heart",
        Page.Bosses => r.Bosses.Where(b => b.Defeated > 0).OrderByDescending(b => b.Defeated).FirstOrDefault() is { } boss ? "codex:" + boss.Id : "pix:star",
        Page.Deaths => "pix:bones",
        Page.Game => "pix:door",
        _ => "pix:hourglass",
    };

    /// <summary>The right page's line under the title: which runs count, or (the game's records) since when.</summary>
    public static string Subtitle(Page page, RunRecords r) =>
        page == Page.Game
            ? Loc.T("Since the save began")
            : $"{FilterName(r.Filter)}  ·  {Loc.F("{0} runs · {1} won", r.Runs, r.Wins)}";

    /// <summary>A left page row: a line or two under its title, the headline number on the right and what it is.</summary>
    public static (string detail, string headline, string caption) Row(Page page, RunRecords r, GameRecords game, RunLookup look)
    {
        const string none = "–";
        switch (page)
        {
            case Page.Bests:
                return (r.BiggestHit != null ? Loc.F("{0}: {1}", Loc.T("Biggest hit"), RunPages.N(r.BiggestHit.Value)) : "",
                    r.FastestWin != null ? RunPages.Time((int)r.FastestWin.Value) : none,
                    r.FastestWin != null ? Loc.T("Fastest win") : Loc.T("No win yet"));
            case Page.Skulls:
                if (r.Skulls.Count == 0)
                    return ("", none, "");
                return (SkullName(r.Skulls[0], look) + "\n" + Loc.P("{0} skull played", "{0} skulls played", r.Skulls.Count),
                    Share(r.Skulls[0].Seconds, r.SkullSeconds), Loc.T("Most played"));
            case Page.Bosses:
                var defeated = r.Bosses.Where(b => b.Defeated > 0).ToList();
                return (string.Join("  ·  ", defeated.Select(b => $"{RunPages.BossName(b.Id, look)} {b.Defeated}")),
                    defeated.Sum(b => b.Defeated).ToString(CultureInfo.InvariantCulture), Loc.T("End bosses defeated"));
            case Page.Deaths:
                return (r.Killers.Count > 0 ? Loc.F("Most often: {0}", KillerName(r.Killers[0], look)) : "",
                    r.Deaths.ToString(CultureInfo.InvariantCulture), Loc.T("Deaths"));
            case Page.Game:
                string deaths = game.Deaths >= 0 ? Loc.F("{0}: {1}", Loc.T("Deaths"), RunPages.N(game.Deaths)) + "\n" : "";
                return (deaths + Loc.P("{0} best stage time", "{0} best stage times", game.BestTimes.Count),
                    game.ClearedLevel >= 0 ? game.ClearedLevel.ToString(CultureInfo.InvariantCulture) : none,
                    Loc.T("Highest Dark Mirror level cleared"));
            default:
                return (Loc.F("{0} runs · {1} won", r.Runs, r.Wins), RunPages.Time((int)Math.Min(int.MaxValue, r.PlaySeconds)), Loc.T("Play time"));
        }
    }

    /// <summary>Where the numbers come from (Umut's spec: say which numbers count from 0.13 on).</summary>
    public static string Note(RunRecords r) =>
        r.Since == default
            ? Loc.T("The run history counts every run from version 0.13 on; the game's own records go back to the start of the save.")
            : Loc.F("The run history counts from its first run ({0}); the game's own records go back to the start of the save.",
                r.Since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    // ------------------------------------------------------------------ the pages

    public static string Text(Page page, RunRecords r, GameRecords game, RunLookup look)
    {
        if (page == Page.Game)
            return GameText(r.Filter, game, look);
        if (r.Runs == 0)
            return $"<color={Dim}>{(r.Filter == RecordFilter.All ? Loc.T("No runs yet. Every run you play from now on is kept here.") : Loc.T("No run fits this filter."))}</color>";
        string text = page switch
        {
            Page.Bests => BestsText(r, look),
            Page.Skulls => SkullsText(r, look),
            Page.Bosses => BossesText(r, look),
            Page.Deaths => DeathsText(r, game, look),
            _ => TotalsText(r),
        };
        return text + PageBlocks.Close() + $"<size=85%><color={Dim}>{Note(r)}</color></size>";
    }

    private static string TotalsText(RunRecords r)
    {
        var sb = new StringBuilder();
        sb.Append(PageBlocks.Section(Loc.T("Runs"), "pix:flag"));
        Pairs(sb, new List<(string, string)>
        {
            (Loc.T("Runs"), RunPages.N(r.Runs)),
            (Loc.T("Play time"), RunPages.Time((int)Math.Min(int.MaxValue, r.PlaySeconds))),
            (Loc.T("Victories"), RunPages.N(r.Wins)),
            (Loc.T("Win rate"), Percent(r.WinRate)),
            (Loc.T("Defeats"), RunPages.N(r.Deaths)),
            (Loc.T("Average run"), RunPages.Time((int)(r.PlaySeconds / Math.Max(1, r.Runs)))),
            (Loc.T("Abandoned"), RunPages.N(r.Abandoned)),
        });
        sb.Append(PageBlocks.Section(Loc.T("Numbers"), "inscription:Arms"));
        // The same numbers as a run page, added up.
        Pairs(sb, new List<(string, string)>
        {
            (Loc.T("Kills"), RunPages.N(r.Kills)),
            (Loc.T("Elite kills"), RunPages.N(r.EliteKills)),
            (Loc.T("Bosses"), RunPages.N(r.BossKills)),
            (Loc.T("Adventurers"), RunPages.N(r.AdventurerKills)),
            (Loc.T("Damage dealt"), RunPages.N(r.Damage)),
            (Loc.T("Damage taken"), RunPages.N(r.Taken)),
            (Loc.T("Healed"), RunPages.N(r.Healed)),
            (Loc.T("Gold earned"), RunPages.N(r.Gold)),
            (Loc.T("Dark quartz"), RunPages.N(r.DarkQuartz)),
            (Loc.T("Bones"), RunPages.N(r.Bones)),
            (Loc.T("Swaps"), RunPages.N(r.Swaps)),
            (Loc.T("Revives"), RunPages.N(r.Revives)),
        });
        if (r.RunsWithHeals > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Healing orbs"), "pix:heal"));
            Pairs(sb, new List<(string, string)>
            {
                (Loc.T("Heals missed"), RunPages.N(r.HealsMissed)),
                (Loc.T("Health missed"), RunPages.N(r.HealthMissed)),
                (Loc.T("Per run"), (r.HealsMissed / r.RunsWithHeals).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)),
                (Loc.T("Health per run"), RunPages.N(r.HealthMissed / r.RunsWithHeals)),
            });
            sb.Append($"<color={Dim}>{Loc.F("Small Healing Orbs left on a map while you weren't at full health (counted since 0.14, {0} runs).", r.RunsWithHeals)}</color>\n");
        }
        if (r.Filter != RecordFilter.Normal && r.Levels.Count > 0)
        {
            // Per Dark Mirror level instead of a filter chip for each of the 11 levels.
            sb.Append(PageBlocks.Section(Loc.T("Dark Mirror levels"), "pix:crown"));
            foreach (var level in r.Levels)
            {
                string fastest = level.FastestWin > 0
                    ? $"<size=80%><color={Dim}>  {Loc.F("{0}: {1}", Loc.T("Fastest win"), RunPages.Time(level.FastestWin))}</color></size>"
                    : "";
                sb.Append(PageBlocks.Bar(Loc.F("Dark Mirror {0}", level.Level) + fastest, Loc.F("{0} of {1} won", level.Wins, level.Runs),
                    level.Runs > 0 ? (double)level.Wins / level.Runs : 0));
            }
        }
        return sb.ToString();
    }

    private static string BestsText(RunRecords r, RunLookup look)
    {
        var sb = new StringBuilder();
        sb.Append(PageBlocks.Section(Loc.T("Bests"), "pix:crown"));
        // Each best with the skull held at the end of its run and a link to the run's page.
        void Add(RunRecords.Best best, string label, string value)
        {
            if (best == null)
                return;
            var run = best.Run;
            string about = $"{RunPages.Mode(run)} · <color={RunPages.ResultColor(run)}>{RunPages.ResultWord(run)}</color> · {RunLinkOf(run)}";
            sb.Append(PageBlocks.Tile(HeldIcon(run), $"{label}\n<size=125%><b>{value}</b></size>\n<size=85%><color={Dim}>{about}</color></size>"));
        }
        Add(r.FastestWin, Loc.T("Fastest win"), RunPages.Time((int)(r.FastestWin?.Value ?? 0)));
        Add(r.HighestLevelWon, Loc.T("Highest Dark Mirror level won"), Loc.F("Dark Mirror {0}", (int)(r.HighestLevelWon?.Value ?? 0)));
        Add(r.MostDamage, Loc.T("Most damage in a run"), RunPages.N(r.MostDamage?.Value ?? 0));
        Add(r.BiggestHit, Loc.T("Biggest hit"), RunPages.N(r.BiggestHit?.Value ?? 0));
        Add(r.MostKills, Loc.T("Most kills"), RunPages.N(r.MostKills?.Value ?? 0));
        Add(r.LongestRun, Loc.T("Longest run"), RunPages.Time((int)(r.LongestRun?.Value ?? 0)));
        if (r.FastestWin == null)
            sb.Append($"<color={Dim}>{Loc.T("No win yet")}</color>\n");
        return sb.ToString();
    }

    private static string SkullsText(RunRecords r, RunLookup look)
    {
        var sb = new StringBuilder();
        int total = r.SkullSeconds, most = r.Skulls.Count > 0 ? r.Skulls.Max(s => s.Seconds) : 0;
        sb.Append(PageBlocks.Section(Loc.T("Time per skull"), "pix:hourglass"));
        foreach (var skull in r.Skulls.Take(SkullMax))
        {
            string detail = $"<size=80%><color={Dim}>  {Loc.F("{0} runs · {1} won", skull.Runs, skull.Wins)}</color></size>";
            sb.Append(PageBlocks.Bar(RunPages.Ref(skull.Id, SkullName(skull, look), look) + detail,
                $"{RunPages.Time(skull.Seconds)}  ({Share(skull.Seconds, total)})", most > 0 ? (double)skull.Seconds / most : 0));
        }
        if (r.Skulls.Count > SkullMax)
            sb.Append($"<color={Dim}>{Loc.F("…and {0} more", r.Skulls.Count - SkullMax)}</color>\n");

        var winners = r.Skulls.Where(s => s.Wins > 0).OrderByDescending(s => s.Wins).ToList();
        if (winners.Count > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Held at the end of a win"), "pix:crown"));
            sb.Append(PageBlocks.Icons(winners.Select(s => ("codex:" + s.Id, look.HasPage(s.Id) ? s.Id : ""))));
            sb.Append(string.Join("  ·  ", winners.Select(s => $"{RunPages.Ref(s.Id, SkullName(s, look), look)} {s.Wins}"))).Append('\n');
        }
        return sb.ToString();
    }

    private static string BossesText(RunRecords r, RunLookup look)
    {
        var sb = new StringBuilder();
        sb.Append(PageBlocks.Section(Loc.T("End bosses"), "pix:crown"));
        var met = r.Bosses.Where(b => b.Defeated + b.KilledYou > 0).ToList();
        if (met.Count == 0)
            sb.Append($"<color={Dim}>{Loc.T("No end boss defeated yet.")}</color>\n");
        foreach (var boss in met)
        {
            string title = look.Title(boss.Id) is { Length: > 0 } t ? $" <color={Dim}>({t})</color>" : "";
            sb.Append(PageBlocks.Tile("codex:" + boss.Id,
                $"<b>{RunPages.Ref(boss.Id, RunPages.BossName(boss.Id, look), look)}</b>{title}\n" +
                Loc.F("Defeated: {0} · killed you: {1}", boss.Defeated, boss.KilledYou)));
        }
        return sb.ToString();
    }

    private static string DeathsText(RunRecords r, GameRecords game, RunLookup look)
    {
        var sb = new StringBuilder();
        sb.Append(PageBlocks.Section(Loc.T("Deaths"), "pix:bones"));
        sb.Append(Loc.T("In the run history")).Append("<pos=70%><b>").Append(RunPages.N(r.Deaths)).Append("</b>\n");
        // The game's counter isn't split by mode: only next to all runs.
        if (game.Deaths >= 0 && r.Filter == RecordFilter.All)
            sb.Append(Loc.T("In this save (the game's counter)")).Append("<pos=70%><b>").Append(RunPages.N(game.Deaths)).Append("</b>\n");
        if (r.Killers.Count > 0)
        {
            var first = r.Killers[0];
            sb.Append(PageBlocks.Section(Loc.T("Who killed you"), first.Id.Length > 0 ? "codex:" + first.Id : "pix:bones"));
            foreach (var killer in r.Killers.Take(ListMax))
                sb.Append(PageBlocks.Bar(RunPages.Ref(killer.Id, KillerName(killer, look), look), CountShare(killer.Count, r.Deaths),
                    (double)killer.Count / first.Count));
        }
        if (r.Places.Count > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Where you died"), "pix:door"));
            foreach (var place in r.Places.Take(ListMax))
                sb.Append(PageBlocks.Bar(place.Name, CountShare(place.Count, r.Deaths), (double)place.Count / r.Places[0].Count));
        }
        return sb.ToString();
    }

    private static string GameText(RecordFilter filter, GameRecords game, RunLookup look)
    {
        var sb = new StringBuilder();
        void Line(string label, string value) => sb.Append(label).Append("<pos=70%><b>").Append(value).Append("</b>\n");
        sb.Append(PageBlocks.Section(Loc.T("Since the save began"), "pix:hourglass"));
        if (game.Deaths >= 0)
            Line(Loc.T("Deaths"), RunPages.N(game.Deaths));
        Line(Loc.T("Highest Dark Mirror level cleared"), game.ClearedLevel >= 0 ? Loc.F("Dark Mirror {0}", game.ClearedLevel) : Loc.T("None"));
        Line(Loc.T("Dark Mirror clears"), RunPages.N(game.ClearedCount));
        Line(Loc.F("{0} defeated", RunPages.BossName(HiddenBossId, look)),
            game.HiddenBossLevel >= 0 ? Loc.F("Dark Mirror {0}", game.HiddenBossLevel) : Loc.T("Not yet"));
        Line(Loc.F("{0} defeated", RunPages.BossName(EmperorId, look)), game.EmperorCleared ? Loc.T("Yes") : Loc.T("Not yet"));

        bool explained = false;
        foreach (bool dark in new[] { false, true })
        {
            if (filter == (dark ? RecordFilter.Normal : RecordFilter.DarkMirror))
                continue;
            sb.Append(PageBlocks.Section(Loc.T("Best stage times") + " · " + (dark ? Loc.T("Dark Mirror") : Loc.T("Normal")), "pix:door"));
            var times = game.BestTimes.Where(t => t.DarkMirror == dark).ToList();
            if (times.Count == 0)
            {
                sb.Append($"<color={Dim}>{Loc.T("No best times yet.")}</color>\n");
                continue;
            }
            if (!explained)
            {
                sb.Append($"<size=85%><color={Dim}>{Loc.T("The quickest you reached each stage (the run's time when the stage began), as the game's loading screens show it.")}</color></size>\n");
                explained = true;
            }
            foreach (var t in times)
            {
                // The Demon King Castle's stages share one name: those get their number.
                bool shared = t.Name.Length > 0 && times.Count(o => o.Name == t.Name) > 1;
                string stage = t.Name.Length == 0 ? Loc.F("{0} · stage {1}", RunPages.ChapterName(t.Chapter), t.Stage + 1)
                    : $"<color={Dim}>{RunPages.ChapterName(t.Chapter)}</color>  " + (shared ? Loc.F("{0} · stage {1}", t.Name, t.Stage + 1) : t.Name);
                sb.Append(stage).Append("<pos=80%><b>").Append(RunPages.Time(t.Seconds)).Append("</b>\n");
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Two label / value pairs per line, like a run page's numbers.</summary>
    private static void Pairs(StringBuilder sb, List<(string label, string value)> pairs)
    {
        for (int i = 0; i < pairs.Count; i += 2)
        {
            sb.Append(pairs[i].label).Append("<pos=31%>").Append(pairs[i].value);
            if (i + 1 < pairs.Count)
                sb.Append("<pos=52%>").Append(pairs[i + 1].label).Append("<pos=83%>").Append(pairs[i + 1].value);
            sb.Append('\n');
        }
    }

    /// <summary>A link to a run's page, named by its start ("2026-10-16 14:03").</summary>
    public static string RunLinkOf(RunRecord run) => CodexPages.LinkTo(RunLink + run.Id, RunPages.Date(run.Started));

    /// <summary>The skull held at the end of a run (the best's picture), or a crown.</summary>
    private static string HeldIcon(RunRecord run) =>
        run.Build.FirstOrDefault(g => g.Kind == "skull" && g.Extra.StartsWith("held", StringComparison.Ordinal)) is { } held ? "codex:" + held.Id : "pix:crown";

    private static string SkullName(RunRecords.SkullTally skull, RunLookup look) =>
        look.Name(skull.Id) ?? (skull.Name.Length > 0 ? skull.Name : RunPages.Readable(skull.Id.Substring(skull.Id.IndexOf(':') + 1)));

    /// <summary>A killer in the current language (traps of old runs by their code names made readable, like a run page).</summary>
    private static string KillerName(RunRecords.Tally killer, RunLookup look) =>
        (killer.Id.Length > 0 ? look.Name(killer.Id) : null) ?? (killer.Id.Length > 0 ? killer.Name : RunPages.Readable(killer.Name));

    private static string Percent(double fraction) => (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Share(int part, int whole) => whole > 0 ? Percent((double)part / whole) : "";

    private static string CountShare(int count, int of) => $"{RunPages.N(count)}  ({Share(count, of)})";
}
