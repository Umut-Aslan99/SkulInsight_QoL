using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Codex;
using DamageInsight.Lang;
using DamageInsight.Recording;

namespace DamageInsight.History;

/// <summary>What the run pages need from the Codex (names in the current language, which ids have a page).</summary>
public sealed class RunLookup
{
    /// <summary>The Codex name of an id in the current language, or null (then the name kept in the run is used).</summary>
    public Func<string, string> Name = _ => null;
    /// <summary>The id has a Codex page (its name becomes a link).</summary>
    public Func<string, bool> HasPage = _ => false;
    /// <summary>The gear behind a damage row only known by a code name (GearTable.ResolveOrigin), or null.</summary>
    public Func<string, (GearGroup group, string name)?> Origin = _ => null;
    /// <summary>The game's own name of a stat (its "stat/&lt;key&gt;/name" text), or null.</summary>
    public Func<string, string> StatName = _ => null;
    /// <summary>A boss's title ("Emperor of Carleon"), or "".</summary>
    public Func<string, string> Title = _ => "";
}

/// <summary>
/// The run history's texts (0.13, after Umut's League of Legends match history reference): a list row's words and
/// the run page's tabs (Overview, Damage, Timeline) as PageBlocks text. Plain logic, no Unity.
/// </summary>
public static class RunPages
{
    /// <summary>The run page's tabs; Points only on a daily board's run (its points part by part, DailyPages.PointsPage).</summary>
    public enum Tab { Overview, Damage, Timeline, Points }

    public const string WinColor = "#2F6E9E", DeathColor = "#9C3B2A", DimColor = "#8A7058", LiveColor = "#4F7A2A";

    public static string ResultWord(RunRecord r) => r.Result switch
    {
        RunResult.Ending => Loc.T("Victory"),
        RunResult.Death => Loc.T("Defeat"),
        RunResult.Abandoned => Loc.T("Abandoned"),
        _ => Loc.T("In progress"),
    };

    public static string ResultColor(RunRecord r) => r.Result switch
    {
        RunResult.Ending => WinColor,
        RunResult.Death => DeathColor,
        RunResult.Abandoned => DimColor,
        _ => LiveColor,
    };

    /// <summary>The badge on the row's portrait: a crown, crossed bones, a white flag or an hourglass.</summary>
    public static string ResultIcon(RunRecord r) => r.Result switch
    {
        RunResult.Ending => "pix:crown",
        RunResult.Death => "pix:bones",
        RunResult.Abandoned => "pix:flag",
        _ => "pix:hourglass",
    };

    /// <summary>"Normal", "Rookie mode" or "Dark Mirror 7" ("Daily run · Dark Mirror 7" for a day's daily run).</summary>
    public static string Mode(RunRecord r)
    {
        string mode = r.DarkMirror ? Loc.F("Dark Mirror {0}", r.Level) : r.Easy ? Loc.T("Rookie mode") : Loc.T("Normal");
        return r.Daily.Length > 0 ? Loc.T("Daily run") + " · " + mode : mode;
    }

    /// <summary>Who decided the run, for the row's portrait: a win's last boss, a death's killer (id, name).</summary>
    public static (string id, string name) Decider(RunRecord r)
    {
        switch (r.Result)
        {
            case RunResult.Ending when r.LastBossId.Length > 0:
                return (EndBossOf(r.LastBossId) ?? r.LastBossId, r.LastBoss);
            case RunResult.Ending:
                // Runs recorded before the last boss was kept: the timeline's last boss.
                var boss = r.Events.LastOrDefault(e => e.Kind == RunEvents.Boss);
                return boss == null ? ("", "") : (EndBossOf(boss.Id) ?? boss.Id, boss.Name);
            case RunResult.Death:
                return (r.KillerId, r.Killer);
            default:
                return ("", "");
        }
    }

    /// <summary>The game's end screen texts for a run (UI.GameResult's string keys) and its panel.</summary>
    public sealed class ResultScreen
    {
        public string Title = "", Subtitle = "", Stage = "", Panel = "normal";
        /// <summary>The "your enemy" label under the picture (deaths only).</summary>
        public bool YourEnemy;
    }

    public const string KeyTitle = "label/gameResult/title", KeySubtitle = "label/gameResult/subTitle",
        KeyHardSubtitle = "label/gameResult/hardmode/subTitle", KeyEndingTitle = "label/gameResult/ending/title",
        KeyEndingSubtitle = "label/gameResult/ending/subTitle", KeyEndingStage = "label/gameResult/ending/stageName",
        KeyHardEndingFirst = "label/gameResult/ending/hardmode/firstSubTitle", KeyHardEnding = "label/gameResult/ending/hardmode/subTitle",
        KeyEmperorEnding = "label/gameResult/ending/emperor/subTitle", KeyEmperor10Ending = "label/gameResult/ending/emperor10/subTitle",
        KeyEmperor10Stage = "label/gameResult/ending/emperor10/stageName";

    /// <summary>The end screen the game shows, from the same facts it decides by (GameResult.Show / ShowEndingResult).</summary>
    public static ResultScreen ScreenFor(bool ending, bool hardmode, int level, int maxLevel, int clearedLevel, bool emperorCleared)
    {
        bool top = hardmode && level == maxLevel && emperorCleared;
        var s = new ResultScreen { Panel = top ? "emperor10" : hardmode ? "hardmode" : "normal" };
        if (!ending)
        {
            s.Title = KeyTitle;
            s.Subtitle = hardmode ? KeyHardSubtitle : KeySubtitle;
            s.YourEnemy = true;
            return s;
        }
        s.Title = KeyEndingTitle;
        s.Subtitle = !hardmode ? KeyEndingSubtitle
            : emperorCleared ? (level == maxLevel ? KeyEmperor10Ending : KeyEmperorEnding)
            : level > clearedLevel ? KeyHardEndingFirst : KeyHardEnding;
        s.Stage = emperorCleared && level == maxLevel ? KeyEmperor10Stage : KeyEndingStage;
        return s;
    }

    /// <summary>A run's end screen: as kept when it ended, or worked out for runs kept before (Dark Mirror's top level is 10).</summary>
    public static ResultScreen Screen(RunRecord r)
    {
        if (r.ScreenTitle.Length > 0)
            return new ResultScreen
            {
                Title = r.ScreenTitle, Subtitle = r.ScreenSubtitle, Stage = r.ScreenStage,
                Panel = r.ScreenPanel.Length > 0 ? r.ScreenPanel : "normal", YourEnemy = r.Result != RunResult.Ending,
            };
        return ScreenFor(r.Result == RunResult.Ending, r.DarkMirror, r.Level, 10, r.Level, r.Ending == "emperor");
    }

    /// <summary>The end screen's "obtained" counts: what the run met plus what it holds at the end (as the game counts).
    /// The game's own essence line adds the item count by mistake; this one counts essences.</summary>
    public static (int skulls, int items, int essences) Obtained(RunRecord r) =>
        ((int)r.Number(RunNumbers.SkullsSeen) + r.Build.Count(g => g.Kind == "skull"),
         (int)r.Number(RunNumbers.ItemsSeen) + r.Build.Count(g => g.Kind == "item"),
         (int)r.Number(RunNumbers.EssencesSeen) + r.Build.Count(g => g.Kind == "essence"));

    /// <summary>Where the run ended (or is now): the stage's name, or "Chapter 2 · stage 1".</summary>
    public static string Where(RunRecord r)
    {
        if (r.EndStageName.Length > 0)
            return r.EndStageName;
        return r.EndChapter.Length == 0 ? "" : Loc.F("{0} · stage {1}", ChapterName(r.EndChapter), r.EndStage + 1);
    }

    public static string ChapterName(string chapter)
    {
        string digits = new string(chapter.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return chapter;
        return Loc.F("Chapter {0}", digits);
    }

    public static string Time(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string Date(DateTime d) => d == default ? "" : d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string N(double v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Big numbers short for the list row: 950, 12.4K, 3.1M.</summary>
    public static string Short(double v)
    {
        v = Math.Round(v);
        if (v >= 1_000_000)
            return (v / 1_000_000).ToString(v >= 10_000_000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M";
        if (v >= 10_000)
            return (v / 1_000).ToString(v >= 100_000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "K";
        return N(v);
    }

    /// <summary>
    /// The bosses that end a run, by the start of their Codex ids (each phase has its own page): the First Hero (the
    /// ending), the First Dark Hero (the Dark Mirror's), Dark Skul (the hidden, optional boss) and King Alexander (the
    /// Demon King Castle). Umut 2026-10-17: "we don't need all bosses". Longest prefix first.
    /// </summary>
    public static readonly (string prefix, string first)[] EndBosses =
    {
        // In the Dark Mirror the First Hero turns into his dark form in the same fight: one boss (Umut 2026-10-17).
        ("enemy:FirstDarkHero", "enemy:FirstHero1"),
        ("enemy:FirstHero", "enemy:FirstHero1"),
        ("enemy:DarkSkul", "enemy:DarkSkul1"),
        ("enemy:Alexander", "enemy:Alexander1"),
    };

    /// <summary>The end boss a Codex id belongs to (its first phase's id), or null for every other boss.</summary>
    public static string EndBossOf(string id) =>
        string.IsNullOrEmpty(id) ? null : EndBosses.FirstOrDefault(b => id.StartsWith(b.prefix, StringComparison.Ordinal)).first;

    /// <summary>The end bosses in a timeline, in order, each once (their first phases' ids).</summary>
    public static List<string> EndBossIds(IEnumerable<RunEvent> events)
    {
        var ids = new List<string>();
        foreach (var e in events.Where(e => e.Kind == RunEvents.Boss))
            if (EndBossOf(e.Id) is { } boss && !ids.Contains(boss))
                ids.Add(boss);
        return ids;
    }

    /// <summary>The end bosses a run defeated (their first phases' ids), from its timeline or, in a summary, as kept.</summary>
    public static List<string> EndBossIds(RunRecord r) =>
        // A summary (the list's rows, the records) has no timeline: it keeps the end bosses' ids.
        r.Events.Any(e => e.Kind == RunEvents.Boss)
            ? EndBossIds(r.Events)
            : r.EndBosses.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>An end boss's name in the current language (its first phase's page), or its id made readable.</summary>
    public static string BossName(string id, RunLookup look) => look.Name(id) ?? Readable(id.Substring(id.IndexOf(':') + 1));

    /// <summary>The end bosses the run defeated, in order, each once (named and pictured by its first phase).</summary>
    public static List<(string id, string name)> Bosses(RunRecord r, RunLookup look) =>
        EndBossIds(r).Select(id => (id, BossName(id, look))).ToList();

    /// <summary>The list row's third line: a win's last bosses (Umut: "did we kill the First Hero, Dark Skul?"), else where it ended.</summary>
    public static string RowLine(RunRecord r, RunLookup look)
    {
        if (r.Result != RunResult.Ending)
            return Where(r);
        var bosses = Bosses(r, look);
        return bosses.Count == 0 ? Where(r) : string.Join(" · ", bosses.Select(b => b.name));
    }

    /// <summary>The list row's numbers line: kills · damage · gold.</summary>
    public static string NumbersLine(RunRecord r) =>
        Loc.F("{0} kills", Short(r.Number(RunNumbers.Kills))) + "  ·  " +
        Loc.F("{0} damage", Short(r.Number(RunNumbers.TotalDamage))) + "  ·  " +
        Loc.F("{0} gold", Short(r.Number(RunNumbers.Gold)));

    /// <summary>The skulls (held first), then the quintessence, the dark abilities and the fragment, then the items.</summary>
    public static (List<RunGear> top, List<RunGear> items) Gear(RunRecord r)
    {
        var skulls = r.Build.Where(g => g.Kind == "skull").OrderBy(g => g.Extra.StartsWith("held") ? 0 : 1).ToList();
        var top = skulls.Concat(r.Build.Where(g => g.Kind is "essence" or "dark" or "fragment")).ToList();
        return (top, r.Build.Where(g => g.Kind == "item").ToList());
    }

    // ------------------------------------------------------------------ the run page

    public static string Page(RunRecord r, Tab tab, RunLookup look) => tab switch
    {
        Tab.Damage => DamagePage(r, look),
        Tab.Timeline => TimelinePage(r, look),
        _ => Overview(r, look),
    };

    private static string Overview(RunRecord r, RunLookup look)
    {
        var sb = new StringBuilder();
        sb.Append(PageBlocks.Section(Loc.T("How it went"), ResultIcon(r)));
        string where = Where(r);
        string line = r.Result switch
        {
            RunResult.Ending when Decider(r).id.Length > 0 => Loc.F("Won: {0} defeated.", Ref(Decider(r).id, Decider(r).name, look)),
            RunResult.Ending => Loc.T("Won."),
            RunResult.Death when r.Killer.Length > 0 => Loc.F("Killed by {0} in {1}.", Ref(r.KillerId, r.KillerId.Length > 0 ? r.Killer : Readable(r.Killer), look), where),
            RunResult.Death => Loc.F("Died in {0}.", where),
            RunResult.Abandoned => Loc.F("Left in {0} (a new run started without an end).", where),
            _ => Loc.F("Still running: {0}.", where),
        };
        sb.Append(line).Append('\n');
        sb.Append(Loc.F("{0} · {1} maps · {2}", Mode(r), r.Maps.Count > 0 ? r.Maps.Count : (int)r.Number(RunNumbers.Maps), Time(r.PlaySeconds))).Append('\n');
        if (r.Number(RunNumbers.JoinedLate) > 0)
            sb.Append($"<color={DimColor}>{Loc.T("Recorded from the middle of the run on (it started before the run history knew it).")}</color>\n");
        if (r.Shrunk)
            sb.Append($"<color={DimColor}>{Loc.T("An old run: only its summary is kept (star a run to keep it whole).")}</color>\n");
        if (r.Number(RunNumbers.HealsMissed) > 0)
            sb.Append($"<color={DimColor}>{Loc.F("Healing orbs left behind while you were hurt: {0} ({1} health).", N(r.Number(RunNumbers.HealsMissed)), N(r.Number(RunNumbers.HealthMissed)))}</color>\n");
        if (r.Number(RunNumbers.Rewards) > 0)
            sb.Append($"<color={DimColor}>{Loc.F("Played with rewards on ({0} quests done, {1} coins earned).", N(r.Number(RunNumbers.QuestsDone)), N(r.Number(RunNumbers.CoinsEarned)))}</color>\n");
        var bosses = Bosses(r, look);
        if (bosses.Count > 0)
        {
            sb.Append(PageBlocks.Icons(bosses.Select(b => ("codex:" + b.id, look.HasPage(b.id) ? b.id : ""))));
            sb.Append(Loc.F("Bosses defeated: {0}", string.Join(" · ", bosses.Select(b =>
                Ref(b.id, b.name, look) + (look.Title(b.id) is { Length: > 0 } title ? $" <color={DimColor}>({title})</color>" : ""))))).Append('\n');
        }
        // The slot's achievements that came in this run (0.14; not on the daily board's runs of other players).
        var achievements = r.Daily.Length > 0 ? new List<string>()
            : DamageInsight.Achievements.AchievementStore.OfRun(r.Id).Select(e => DamageInsight.Achievements.AchievementCatalog.Get(e.Id))
                .Where(a => a != null).Select(a => CodexPages.LinkTo(DamageInsight.Achievements.AchievementPages.IdOf(a), a.Name)).ToList();
        if (achievements.Count > 0)
            sb.Append(Loc.F("Achievements: {0}", string.Join(" · ", achievements))).Append('\n');

        var (top, items) = Gear(r);
        if (top.Count + items.Count > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Build"), "inscription:Mutation"));
            if (top.Count > 0)
                sb.Append(PageBlocks.Icons(top.Select(g => ("codex:" + g.Id, look.HasPage(g.Id) ? g.Id : ""))));
            if (items.Count > 0)
                sb.Append(PageBlocks.Icons(items.Select(g => ("codex:" + g.Id, look.HasPage(g.Id) ? g.Id : ""))));
            foreach (var skull in top.Where(g => g.Kind == "skull"))
            {
                int bar = skull.Extra.IndexOf('|');
                string skills = bar >= 0 ? skull.Extra.Substring(bar + 1) : "";
                string held = skull.Extra.StartsWith("held") ? $" <color={DimColor}>({Loc.T("held")})</color>" : "";
                sb.Append($"<b>{Ref(skull.Id, skull.Name, look)}</b>{held}{(skills.Length > 0 ? ": " + skills : "")}\n");
            }
            foreach (var fragment in top.Where(g => g.Kind == "fragment"))
                sb.Append(Loc.F("Fragment: {0}", $"<b>{look.Name(fragment.Id) ?? fragment.Name}</b>")).Append('\n');
            var inscriptions = r.Build.Where(g => g.Kind == "insc" && g.Step > 0).ToList();
            if (inscriptions.Count > 0)
                sb.Append(string.Join("  ·  ", inscriptions.Select(g =>
                    $"{Ref("inscription:" + g.Id, g.Name, look)} {g.Value}" +
                    (g.Extra == "tuned" ? $" <color={DeathColor}><b>{Loc.T("Tuned")}</b></color>" : "")))).Append('\n');
        }

        sb.Append(PageBlocks.Section(Loc.T("Numbers"), "inscription:Arms"));
        var pairs = new List<(string label, string value)>
        {
            (Loc.T("Run time"), Time(r.PlaySeconds)),
            (Loc.T("Kills"), N(r.Number(RunNumbers.Kills))),
            (Loc.T("Elite kills"), N(r.Number(RunNumbers.EliteKills))),
            (Loc.T("Bosses"), N(r.Number(RunNumbers.BossKills))),
            (Loc.T("Adventurers"), N(r.Number(RunNumbers.AdventurerKills))),
            (Loc.T("Damage dealt"), N(r.Number(RunNumbers.TotalDamage))),
            (Loc.T("Biggest hit"), N(r.Number(RunNumbers.BestDamage))),
            (Loc.T("Damage taken"), N(r.Number(RunNumbers.DamageTaken))),
            (Loc.T("Healed"), N(r.Number(RunNumbers.Healed))),
            (Loc.T("Gold earned"), N(r.Number(RunNumbers.Gold))),
            (Loc.T("Dark quartz"), N(r.Number(RunNumbers.DarkQuartz))),
            (Loc.T("Bones"), N(r.Number(RunNumbers.Bones))),
            (Loc.T("Skulls seen"), N(r.Number(RunNumbers.SkullsSeen))),
            (Loc.T("Items seen"), N(r.Number(RunNumbers.ItemsSeen))),
            (Loc.T("Swaps"), N(r.Number(RunNumbers.Swaps))),
            (Loc.T("Revives"), N(r.Number(RunNumbers.Revives))),
        };
        // 0.14: heal orbs left behind (runs from before don't know).
        if (r.Numbers.ContainsKey(RunNumbers.HealsMissed))
        {
            pairs.Add((Loc.T("Heals missed"), N(r.Number(RunNumbers.HealsMissed))));
            pairs.Add((Loc.T("Health missed"), N(r.Number(RunNumbers.HealthMissed))));
        }
        for (int i = 0; i < pairs.Count; i += 2)
        {
            sb.Append(pairs[i].label).Append("<pos=31%>").Append(pairs[i].value);
            if (i + 1 < pairs.Count)
                sb.Append("<pos=52%>").Append(pairs[i + 1].label).Append("<pos=83%>").Append(pairs[i + 1].value);
            sb.Append('\n');
        }
        string stats = StatsText(r, look);
        if (stats.Length > 0)
        {
            sb.Append(PageBlocks.Section(r.Result == RunResult.InProgress ? Loc.T("Stats now") : Loc.T("Stats at the end"), "inscription:Fortress"));
            sb.Append(stats);
        }
        sb.Append(PageBlocks.Close());
        sb.Append($"<color={DimColor}>{Loc.F("Started {0}", Date(r.Started))}{(r.Ended != default ? "  ·  " + Loc.F("ended {0}", Date(r.Ended)) : "")}</color>");
        return sb.ToString();
    }

    /// <summary>The stats kept at the run's end, like the You page shows them (the game's names where it has them).</summary>
    private static string StatsText(RunRecord r, RunLookup look)
    {
        var sb = new StringBuilder();
        string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        string Signed(double v) => (v >= 0 ? "+" : "") + (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        string X(double v) => "x" + v.ToString("0.00", CultureInfo.InvariantCulture);
        void Row(string kind, string gameKey, string fallback, Func<double, string> format)
        {
            if (!r.Numbers.TryGetValue(RunNumbers.StatPrefix + kind, out double v))
                return;
            string name = (gameKey != null ? look.StatName(gameKey) : null) ?? Loc.T(fallback);
            sb.Append(name).Append("<pos=62%><b>").Append(format(v)).Append("</b>\n");
        }
        Row("Health", "health", "Health", v => N(v));
        Row("PhysicalAttackDamage", "physicalAttackDamage", "Physical attack", Pct);
        Row("MagicAttackDamage", "magicalAttackDamage", "Magic attack", Pct);
        Row("AttackDamage", null, "All damage", Pct);
        Row("CriticalChance", "criticalChance", "Crit chance", v => Signed(v - 1.0));
        Row("CriticalDamage", "criticalDamage", "Crit damage", X);
        Row("BasicAttackSpeed", "attackSpeed", "Attack speed", Pct);
        Row("MovementSpeed", "movementSpeed", "Movement speed", v => Pct(v / 5.0));
        Row("SkillCooldownSpeed", "skillCooldownSpeed", "Skill cooldown speed", Pct);
        Row("SwapCooldownSpeed", "swapCooldownSpeed", "Swap cooldown speed", Pct);
        Row("EssenceCooldownSpeed", "essenceCooldownSpeed", "Quintessence cooldown speed", Pct);
        Row("ChargingSpeed", "chargingSpeed", "Charge speed", Pct);
        Row("TakingDamage", "takingDamage", "Damage taken", X);
        Row("BasicAttackDamage", null, "Basic attack damage", Pct);
        Row("SkillAttackDamage", null, "Skill damage", Pct);
        Row("ProjectileAttackDamage", null, "Projectile damage", Pct);
        return sb.ToString();
    }

    /// <summary>Damage rows the run kept under "Other" whose code name belongs to a piece of gear go to its group.</summary>
    public static List<RunDamage> Regroup(IEnumerable<RunDamage> rows, RunLookup look)
    {
        var merged = new List<RunDamage>();
        foreach (var row in rows)
        {
            var target = row;
            if (row.Group == nameof(GearGroup.Other) && look.Origin(row.Name) is { } gear)
                target = new RunDamage
                {
                    Group = gear.group.ToString(),
                    Name = gear.group == GearGroup.Skulls ? Loc.F("{0}: {1}", gear.name, Loc.T("Skills")) : gear.name,
                    Amount = row.Amount, Hits = row.Hits, Biggest = row.Biggest,
                };
            var same = merged.FirstOrDefault(m => m.Group == target.Group && m.Name == target.Name);
            if (same == null)
            {
                merged.Add(new RunDamage { Group = target.Group, Name = target.Name, Amount = target.Amount, Hits = target.Hits, Biggest = target.Biggest });
                continue;
            }
            same.Amount += target.Amount;
            same.Hits += target.Hits;
            same.Biggest = Math.Max(same.Biggest, target.Biggest);
        }
        return merged
            .OrderBy(d => Enum.TryParse(d.Group, out GearGroup g) ? (int)g : 99)
            .ThenByDescending(d => d.Amount)
            .ToList();
    }

    private static string DamagePage(RunRecord r, RunLookup look)
    {
        var sb = new StringBuilder();
        if (r.Shrunk || (r.Damage.Count == 0 && r.DamageTaken.Count == 0))
        {
            sb.Append(r.Shrunk ? Loc.T("An old run: only its summary is kept (star a run to keep it whole).") : Loc.T("No damage was recorded in this run."));
            return sb.ToString();
        }
        var rows = Regroup(r.Damage, look);
        double total = rows.Sum(d => d.Amount);
        double max = rows.Count > 0 ? rows.Max(d => d.Amount) : 0;
        sb.Append(PageBlocks.Section(Loc.F("Damage dealt: {0}", N(total)), "inscription:Arms"));
        // The end screen's total counts every hit in full; this table counts the health each hit took.
        if (r.Number(RunNumbers.TotalDamage) > total * 1.05)
            sb.Append($"<size=85%><color={DimColor}>{Loc.F("The health your hits took. The end screen's total damage ({0}) counts every hit in full, also beyond an enemy's last health.", N(r.Number(RunNumbers.TotalDamage)))}</color></size>\n");
        foreach (var group in rows.GroupBy(d => d.Group))
        {
            string name = Enum.TryParse(group.Key, out GearGroup g) ? GearTable.GroupName(g) : group.Key;
            sb.Append($"<b>{name}</b>  <color={DimColor}>{N(group.Sum(d => d.Amount))}</color>\n");
            foreach (var row in group.OrderByDescending(d => d.Amount))
            {
                string share = total > 0 ? (row.Amount / total * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "";
                string detail = $"<size=80%><color={DimColor}>  {Loc.P("{0} hit", "{0} hits", row.Hits)} · {Loc.F("best {0}", N(row.Biggest))}</color></size>";
                sb.Append(PageBlocks.Bar(row.Name + detail, $"{N(row.Amount)}  ({share})", max > 0 ? row.Amount / max : 0));
            }
        }
        if (r.DamageTaken.Count > 0)
        {
            double taken = r.DamageTaken.Sum(p => p.Value.amount);
            double worst = r.DamageTaken.Max(p => p.Value.amount);
            sb.Append(PageBlocks.Section(Loc.F("Damage taken: {0}", N(taken)), "pix:heart"));
            // Parts of one boss (old runs kept their code names) are added up under its name.
            var byName = r.DamageTaken.GroupBy(p => AttackerName(p.Key, look.Name))
                .Select(g => (name: g.Key, amount: g.Sum(p => p.Value.amount), hits: g.Sum(p => p.Value.hits))).ToList();
            worst = byName.Count > 0 ? byName.Max(p => p.amount) : 0;
            foreach (var pair in byName.OrderByDescending(p => p.amount).Take(15))
                sb.Append(PageBlocks.Bar($"{pair.name}<size=80%><color={DimColor}>  {Loc.P("{0} hit", "{0} hits", pair.hits)}</color></size>",
                    N(pair.amount), worst > 0 ? pair.amount / worst : 0));
        }
        return sb.ToString();
    }

    private static string TimelinePage(RunRecord r, RunLookup look)
    {
        var sb = new StringBuilder();
        if (r.Shrunk)
        {
            sb.Append(Loc.T("An old run: only its summary is kept (star a run to keep it whole)."));
            return sb.ToString();
        }
        int played = r.SkullSeconds.Values.Sum();
        if (played > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Skulls played"), "pix:hourglass"));
            int most = r.SkullSeconds.Values.Max();
            foreach (var pair in r.SkullSeconds.OrderByDescending(p => p.Value))
            {
                string name = look.Name(pair.Key) ?? (r.SkullNames.TryGetValue(pair.Key, out var n) ? n : pair.Key);
                string share = (pair.Value * 100.0 / played).ToString("0", CultureInfo.InvariantCulture) + "%";
                sb.Append(PageBlocks.Bar(name, $"{Time(pair.Value)}  ({share})", most > 0 ? (double)pair.Value / most : 0));
            }
        }

        // The run stage by stage: its maps and what happened on each.
        var events = r.Events.OrderBy(e => e.T).ToList();
        int next = 0;
        string stage = null;
        for (int m = 0; m < r.Maps.Count; m++)
        {
            var map = r.Maps[m];
            string key = map.Chapter + "/" + map.Stage;
            if (key != stage)
            {
                stage = key;
                string title = map.StageName.Length > 0 ? map.StageName : Loc.F("{0} · stage {1}", ChapterName(map.Chapter), map.Stage + 1);
                sb.Append(PageBlocks.Section(title, "pix:door"));
            }
            int until = m + 1 < r.Maps.Count ? r.Maps[m + 1].T : int.MaxValue;
            if (map.Seconds > 0)
                sb.Append(PageBlocks.Line("", $"<color={DimColor}>{Time(map.T - r.StartPlay)}  {MapKind(map.Type)} · {Time(map.Seconds)}</color>"));
            // Events before the first map (the start) go with it.
            while (next < events.Count && (events[next].T < until || m == r.Maps.Count - 1))
            {
                if (EventLine(events[next], r, look) is { } eventLine)
                    sb.Append(eventLine);
                next++;
            }
        }
        if (r.Maps.Count == 0)
            foreach (var e in events)
                if (EventLine(e, r, look) is { } eventLine)
                    sb.Append(eventLine);
        if (sb.Length == 0)
            sb.Append(Loc.T("Nothing was recorded in this run yet."));
        return sb.ToString();
    }

    /// <summary>The game's map kinds (Level.Map.Type): Normal, Npc, Manual (fixed maps: boss rooms, ...), Special.</summary>
    private static string MapKind(string type) => type switch
    {
        "Npc" => Loc.T("Encounter"),
        "Special" => Loc.T("Special room"),
        _ => Loc.T("Room"),
    };

    /// <summary>One timeline line: time, icon and what happened (null for nothing to show).</summary>
    public static string EventLine(RunEvent e, RunRecord r, RunLookup look)
    {
        string a = Ref(e.Id, e.Name, look), b = Ref(e.Other, e.OtherName, look);
        string text = e.Kind switch
        {
            RunEvents.Start => Loc.F("Started with {0}", a),
            RunEvents.SkullTaken => e.Other.Length > 0 ? Loc.F("Took {0} (dropped {1})", a, b) : Loc.F("Took {0}", a),
            RunEvents.SkullDropped => Loc.F("Dropped {0}", a),
            RunEvents.SkullAwakened => Loc.F("{0} awakened into {1}", b, a),
            RunEvents.ItemTaken or RunEvents.EssenceTaken or RunEvents.Fragment => Loc.F("Took {0}", a),
            RunEvents.ItemLost or RunEvents.EssenceLost => Loc.F("Lost {0}", a),
            RunEvents.ItemChanged or RunEvents.EssenceChanged => Loc.F("{0} turned into {1}", b, a),
            RunEvents.DarkAbility => e.Value <= 1 ? Loc.F("Bought {0}", a) : Loc.F("{0} to level {1}", a, e.Value),
            RunEvents.Inscription => Loc.F("{0} reached step {1}", Ref("inscription:" + e.Id, e.Name, look), e.Value),
            RunEvents.InscriptionTuned => Loc.F("{0} took its tuned form", Ref("inscription:" + e.Id, e.Name, look)),
            RunEvents.Revive => Loc.T("Revived"),
            RunEvents.Boss or RunEvents.Adventurer => Loc.F("Defeated {0}", a),
            _ => null,
        };
        if (text == null)
            return null;
        string icon = e.Kind switch
        {
            RunEvents.Inscription or RunEvents.InscriptionTuned => "inscription:" + e.Id,
            RunEvents.Revive => "pix:heart",
            RunEvents.Start => "pix:flag",
            _ => e.Id.Length > 0 ? "codex:" + e.Id : "",
        };
        return PageBlocks.Line(icon, $"<color={DimColor}>{Time(e.T - r.StartPlay)}</color>  {text}");
    }

    /// <summary>An object name made readable: "ThornTrap" becomes "Thorn Trap" (traps have no name in the game's texts).</summary>
    /// <summary>Bosses whose parts have no name of their own: the game's object names and the Codex page they belong to.</summary>
    private static readonly (string part, string page)[] BossParts =
    {
        ("ShortHair", "enemy:LeianaShortHair"), ("LongHair", "enemy:LeianaShortHair"), ("DarkSkeleton", "enemy:DarkSkul1"),
        ("Emperor", "enemy:Alexander1"),
    };

    /// <summary>
    /// An attacker's name as a run keeps it, made readable (0.14: runs kept code names like "DarkSkeleton_Phase1",
    /// "Emperor_Renewal", "ShortHair", "PowderKeg"): a boss part by its boss's Codex name, another code name split into
    /// words. Names with spaces or in other scripts stay as they are.
    /// </summary>
    public static string AttackerName(string stored, Func<string, string> pageName)
    {
        if (string.IsNullOrEmpty(stored) || stored.Contains(' ') || stored.Any(c => c > 127))
            return stored ?? "";
        foreach (var (part, page) in BossParts)
            if (stored.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0 && pageName(page) is { Length: > 0 } name)
                return name;
        return stored.Contains('_') || stored.Skip(1).Any(char.IsUpper) ? Readable(stored) : stored;
    }

    public static string Readable(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name ?? "";
        name = name.Replace("(Clone)", "").Replace('_', ' ').Trim();
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A name in the current language, a link when it has a Codex page.</summary>
    internal static string Ref(string id, string fallback, RunLookup look)
    {
        if (string.IsNullOrEmpty(id))
            return fallback ?? "";
        string name = look.Name(id) ?? fallback ?? id;
        return look.HasPage(id) ? CodexPages.LinkTo(id, name) : name;
    }
}
