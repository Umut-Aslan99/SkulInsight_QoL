using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.History;

public enum RunResult
{
    InProgress,
    Death,
    Ending,
    Abandoned,
}

/// <summary>Something that happened in a run, at the game's play time <see cref="T"/> (seconds).</summary>
public sealed class RunEvent
{
    public int T;
    /// <summary>One of <see cref="RunEvents"/>.</summary>
    public string Kind = "";
    /// <summary>The thing it is about: a Codex id (skull, item, ...), an inscription key, a boss name.</summary>
    public string Id = "";
    public string Name = "";
    /// <summary>The thing it replaced or turned into (skull dropped for the new one, item an item turned into).</summary>
    public string Other = "";
    public string OtherName = "";
    /// <summary>A level or step (dark abilities, inscriptions); 0 when none.</summary>
    public int Value;
}

/// <summary>The kinds of <see cref="RunEvent"/>. Stored in the run files: never rename.</summary>
public static class RunEvents
{
    public const string Start = "start";            // the run's first skulls (Id = held skull)
    public const string SkullTaken = "skull";        // Id taken, Other dropped for it (or empty)
    public const string SkullDropped = "skull-";     // Id dropped, nothing taken in its place
    public const string SkullAwakened = "awaken";    // Other awakened into Id
    public const string ItemTaken = "item";
    public const string ItemLost = "item-";          // dropped, sold, destroyed
    public const string ItemChanged = "item~";       // Other turned into Id
    public const string EssenceTaken = "essence";
    public const string EssenceLost = "essence-";
    public const string EssenceChanged = "essence~";
    public const string DarkAbility = "dark";        // Value = level (1 when bought)
    public const string Fragment = "fragment";
    public const string Inscription = "insc";        // Id = inscription key, Name = its name, Value = step reached
    public const string InscriptionTuned = "tuned";  // Id = inscription key: it took its tuned (true) form
    public const string Revive = "revive";
    public const string Boss = "boss";               // Id = Codex id, Name = boss name
    public const string Adventurer = "adventurer";
}

/// <summary>A map of the run: where, what kind, when it was entered and how long it took.</summary>
public sealed class RunMap
{
    public int T;
    public string Chapter = "";
    public int Stage;
    public string StageName = "";
    public string Map = "";
    public string Type = "";
    public int Seconds;
}

/// <summary>One piece of the final build.</summary>
public sealed class RunGear
{
    /// <summary>"skull", "item", "essence", "dark", "fragment", "insc".</summary>
    public string Kind = "";
    public string Id = "";
    public string Name = "";
    public string Rarity = "";
    /// <summary>Skulls: their skills (names, " · "); held skull: "held".</summary>
    public string Extra = "";
    /// <summary>Dark abilities: level; inscriptions: count.</summary>
    public int Value;
    /// <summary>Inscriptions: the step reached.</summary>
    public int Step;
}

/// <summary>A row of the run's damage table (the combat log's GearTable, summed over the whole run).</summary>
public sealed class RunDamage
{
    public string Group = "";
    public string Name = "";
    public double Amount;
    public int Hits;
    public double Biggest;
}

/// <summary>
/// One run of the run history (0.13): its result, the end screen's numbers, the final build, the damage split, the
/// maps and a timeline. Stored as <c>Codex/Runs/&lt;Id&gt;.json</c> (per save slot through the Codex folder). Plain
/// data with its own JSON writer and reader.
/// </summary>
public sealed class RunRecord
{
    public const int FormatVersion = 1;

    /// <summary>The start time as a file name: "2026-10-16_14-03-22".</summary>
    public string Id = "";
    /// <summary>The game's run seed (GameData.Save.randomSeed): the same run after quitting and continuing.</summary>
    public int Seed;
    public DateTime Started;
    public DateTime Ended;
    public RunResult Result;
    public bool DarkMirror;
    public int Level;
    /// <summary>The game's rookie mode (GameData.Settings.easyMode, normal mode only) was on at some point of the run.</summary>
    public bool Easy;
    /// <summary>The daily run's date when this run was a day's daily run (0.13), else "".</summary>
    public string Daily = "";
    /// <summary>Starred in the Runs list: never shrunk.</summary>
    public bool Favourite;
    /// <summary>Shrunk to its list row and numbers (an old run without a star): no timeline, damage or picture.</summary>
    public bool Shrunk;
    public string ModVersion = "";

    /// <summary>The game's play time when the run left the castle and at its end (seconds, PlaytimeTimer).</summary>
    public int StartPlay, EndPlay;

    /// <summary>Where the run ended (or is now).</summary>
    public string EndChapter = "";
    public int EndStage;
    public string EndStageName = "";

    /// <summary>A death: who dealt the last hit (name, Codex id if it has a page).</summary>
    public string Killer = "";
    public string KillerId = "";
    /// <summary>An ending: "normal", "darkmirror", "emperor" (the game's ending kinds).</summary>
    public string Ending = "";
    /// <summary>
    /// The game's end screen for this run (its string keys, so it shows in any language later): title, subtitle,
    /// stage name key (endings; deaths show <see cref="EndStageName"/>), and its panel ("normal", "hardmode", "emperor10").
    /// </summary>
    public string ScreenTitle = "", ScreenSubtitle = "", ScreenStage = "", ScreenPanel = "";
    /// <summary>The last boss killed in the run (a win's final boss), name and Codex id.</summary>
    public string LastBoss = "";
    public string LastBossId = "";
    /// <summary>The end bosses defeated (RunPages.EndBosses, first phases' ids, comma separated): kept for summaries,
    /// which have no timeline.</summary>
    public string EndBosses = "";

    /// <summary>The end screen's numbers and a few of our own (keys: <see cref="RunNumbers"/>).</summary>
    public readonly Dictionary<string, double> Numbers = new();

    public readonly List<RunMap> Maps = new();
    public readonly List<RunEvent> Events = new();
    /// <summary>Seconds each skull was the one held (Codex id → seconds), and its name.</summary>
    public readonly Dictionary<string, int> SkullSeconds = new();
    public readonly Dictionary<string, string> SkullNames = new();
    public readonly List<RunGear> Build = new();
    public readonly List<RunDamage> Damage = new();
    /// <summary>Damage taken per attacker (name → amount, hits).</summary>
    public readonly Dictionary<string, (double amount, int hits)> DamageTaken = new();

    /// <summary>The death cam picture was saved next to the file (<c>&lt;Id&gt;.jpg</c>).</summary>
    public bool Picture;

    public int PlaySeconds => Math.Max(0, EndPlay - StartPlay);

    public double Number(string key) => Numbers.TryGetValue(key, out double v) ? v : 0;

    // ------------------------------------------------------------------ JSON

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n\"format\":").Append(FormatVersion)
          .Append(",\"id\":").Append(Q(Id))
          .Append(",\"seed\":").Append(Seed)
          .Append(",\"started\":").Append(Q(Date(Started)))
          .Append(",\"ended\":").Append(Q(Ended == default ? "" : Date(Ended)))
          .Append(",\"result\":").Append(Q(Result.ToString()))
          .Append(",\"darkMirror\":").Append(DarkMirror ? "true" : "false")
          .Append(",\"level\":").Append(Level)
          .Append(",\"easy\":").Append(Easy ? "true" : "false")
          .Append(",\"daily\":").Append(Q(Daily))
          .Append(",\"favourite\":").Append(Favourite ? "true" : "false")
          .Append(",\"shrunk\":").Append(Shrunk ? "true" : "false")
          .Append(",\"mod\":").Append(Q(ModVersion))
          .Append(",\n\"startPlay\":").Append(StartPlay)
          .Append(",\"endPlay\":").Append(EndPlay)
          .Append(",\"endChapter\":").Append(Q(EndChapter))
          .Append(",\"endStage\":").Append(EndStage)
          .Append(",\"endStageName\":").Append(Q(EndStageName))
          .Append(",\"killer\":").Append(Q(Killer))
          .Append(",\"killerId\":").Append(Q(KillerId))
          .Append(",\"ending\":").Append(Q(Ending))
          .Append(",\"screenTitle\":").Append(Q(ScreenTitle))
          .Append(",\"screenSubtitle\":").Append(Q(ScreenSubtitle))
          .Append(",\"screenStage\":").Append(Q(ScreenStage))
          .Append(",\"screenPanel\":").Append(Q(ScreenPanel))
          .Append(",\"lastBoss\":").Append(Q(LastBoss))
          .Append(",\"lastBossId\":").Append(Q(LastBossId))
          .Append(",\"endBosses\":").Append(Q(EndBosses))
          .Append(",\"picture\":").Append(Picture ? "true" : "false");

        sb.Append(",\n\"numbers\":{");
        sb.Append(string.Join(",", Numbers.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Q(p.Key) + ":" + N(p.Value))));
        sb.Append('}');

        sb.Append(",\n\"skullSeconds\":{");
        sb.Append(string.Join(",", SkullSeconds.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Q(p.Key) + ":" + p.Value)));
        sb.Append("},\"skullNames\":{");
        sb.Append(string.Join(",", SkullNames.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Q(p.Key) + ":" + Q(p.Value))));
        sb.Append('}');

        sb.Append(",\n\"build\":[");
        sb.Append(string.Join(",", Build.Select(g =>
            "\n {\"kind\":" + Q(g.Kind) + ",\"id\":" + Q(g.Id) + ",\"name\":" + Q(g.Name) + ",\"rarity\":" + Q(g.Rarity) +
            ",\"extra\":" + Q(g.Extra) + ",\"value\":" + g.Value + ",\"step\":" + g.Step + "}")));
        sb.Append("]");

        sb.Append(",\n\"damage\":[");
        sb.Append(string.Join(",", Damage.Select(d =>
            "\n {\"group\":" + Q(d.Group) + ",\"name\":" + Q(d.Name) + ",\"amount\":" + N(d.Amount) + ",\"hits\":" + d.Hits +
            ",\"biggest\":" + N(d.Biggest) + "}")));
        sb.Append("]");

        sb.Append(",\n\"damageTaken\":[");
        sb.Append(string.Join(",", DamageTaken.OrderByDescending(p => p.Value.amount).Select(p =>
            "\n {\"name\":" + Q(p.Key) + ",\"amount\":" + N(p.Value.amount) + ",\"hits\":" + p.Value.hits + "}")));
        sb.Append("]");

        sb.Append(",\n\"maps\":[");
        sb.Append(string.Join(",", Maps.Select(m =>
            "\n {\"t\":" + m.T + ",\"chapter\":" + Q(m.Chapter) + ",\"stage\":" + m.Stage + ",\"stageName\":" + Q(m.StageName) +
            ",\"map\":" + Q(m.Map) + ",\"type\":" + Q(m.Type) + ",\"seconds\":" + m.Seconds + "}")));
        sb.Append("]");

        sb.Append(",\n\"events\":[");
        sb.Append(string.Join(",", Events.Select(e =>
            "\n {\"t\":" + e.T + ",\"kind\":" + Q(e.Kind) + ",\"id\":" + Q(e.Id) + ",\"name\":" + Q(e.Name) +
            (e.Other.Length > 0 ? ",\"other\":" + Q(e.Other) + ",\"otherName\":" + Q(e.OtherName) : "") +
            (e.Value != 0 ? ",\"value\":" + e.Value : "") + "}")));
        sb.Append("]\n}\n");
        return sb.ToString();
    }

    /// <summary>Reads a run file; null when it isn't one (broken or empty).</summary>
    public static RunRecord FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        Node n;
        try { n = GearDoc.ParseNode(json); }
        catch (Exception) { return null; }
        return FromNode(n);
    }

    /// <summary>A run from its JSON object (a run file, or one entry of the index); null when it isn't one.</summary>
    public static RunRecord FromNode(Node n)
    {
        if (n.IsNull || n.Str("id") is not { Length: > 0 } id)
            return null;

        var r = new RunRecord
        {
            Id = id,
            Seed = (int)n.Num("seed"),
            Started = ParseDate(n.Str("started")),
            Ended = ParseDate(n.Str("ended")),
            Result = Enum.TryParse(n.Str("result"), out RunResult result) ? result : RunResult.Abandoned,
            DarkMirror = n.Bool("darkMirror"),
            Level = (int)n.Num("level"),
            Easy = n.Bool("easy"),
            Daily = n.Str("daily") ?? "",
            Favourite = n.Bool("favourite"),
            Shrunk = n.Bool("shrunk"),
            ModVersion = n.Str("mod") ?? "",
            StartPlay = (int)n.Num("startPlay"),
            EndPlay = (int)n.Num("endPlay"),
            EndChapter = n.Str("endChapter") ?? "",
            EndStage = (int)n.Num("endStage"),
            EndStageName = n.Str("endStageName") ?? "",
            Killer = n.Str("killer") ?? "",
            KillerId = n.Str("killerId") ?? "",
            Ending = n.Str("ending") ?? "",
            ScreenTitle = n.Str("screenTitle") ?? "",
            ScreenSubtitle = n.Str("screenSubtitle") ?? "",
            ScreenStage = n.Str("screenStage") ?? "",
            ScreenPanel = n.Str("screenPanel") ?? "",
            LastBoss = n.Str("lastBoss") ?? "",
            LastBossId = n.Str("lastBossId") ?? "",
            EndBosses = n.Str("endBosses") ?? "",
            Picture = n.Bool("picture"),
        };
        var numbers = n.Child("numbers");
        foreach (var (key, value) in numbers.Fields())
            if (value is double d)
                r.Numbers[key] = d;
        foreach (var (key, value) in n.Child("skullSeconds").Fields())
            if (value is double d)
                r.SkullSeconds[key] = (int)d;
        foreach (var (key, value) in n.Child("skullNames").Fields())
            if (value is string s)
                r.SkullNames[key] = s;
        foreach (var g in n.List("build"))
            r.Build.Add(new RunGear
            {
                Kind = g.Str("kind") ?? "", Id = g.Str("id") ?? "", Name = g.Str("name") ?? "", Rarity = g.Str("rarity") ?? "",
                Extra = g.Str("extra") ?? "", Value = (int)g.Num("value"), Step = (int)g.Num("step"),
            });
        foreach (var d in n.List("damage"))
            r.Damage.Add(new RunDamage
            {
                Group = d.Str("group") ?? "", Name = d.Str("name") ?? "", Amount = d.Num("amount"), Hits = (int)d.Num("hits"),
                Biggest = d.Num("biggest"),
            });
        foreach (var d in n.List("damageTaken"))
            if (d.Str("name") is { } name)
                r.DamageTaken[name] = (d.Num("amount"), (int)d.Num("hits"));
        foreach (var m in n.List("maps"))
            r.Maps.Add(new RunMap
            {
                T = (int)m.Num("t"), Chapter = m.Str("chapter") ?? "", Stage = (int)m.Num("stage"), StageName = m.Str("stageName") ?? "",
                Map = m.Str("map") ?? "", Type = m.Str("type") ?? "", Seconds = (int)m.Num("seconds"),
            });
        foreach (var e in n.List("events"))
            r.Events.Add(new RunEvent
            {
                T = (int)e.Num("t"), Kind = e.Str("kind") ?? "", Id = e.Str("id") ?? "", Name = e.Str("name") ?? "",
                Other = e.Str("other") ?? "", OtherName = e.Str("otherName") ?? "", Value = (int)e.Num("value"),
            });
        return r;
    }

    /// <summary>
    /// The run without its details (timeline, maps, damage rows, damage taken, picture): what the list and the
    /// records need. Old runs without a star are kept like this; the index holds every run like this.
    /// </summary>
    public RunRecord Shrink()
    {
        var copy = FromJson(ToJson());
        // What the list row needs from the timeline: the end bosses and the last boss.
        if (Events.Any(e => e.Kind == RunEvents.Boss))
        {
            copy.EndBosses = string.Join(",", RunPages.EndBossIds(Events));
            var last = Events.Last(e => e.Kind == RunEvents.Boss);
            if (copy.LastBossId.Length == 0)
            {
                copy.LastBossId = last.Id;
                copy.LastBoss = last.Name;
            }
        }
        copy.Events.Clear();
        copy.Maps.Clear();
        copy.Damage.Clear();
        copy.DamageTaken.Clear();
        copy.Picture = false;
        copy.Shrunk = true;
        return copy;
    }

    /// <summary>The run as the index keeps it: without its details, but saying whether its file still has them.</summary>
    public RunRecord Summary()
    {
        var copy = Shrink();
        copy.Shrunk = Shrunk;
        copy.Picture = Picture;
        return copy;
    }

    public static string IdFor(DateTime start) => start.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

    private static string Q(string s) => LogJson.Quote(s ?? "");
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Date(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string s) =>
        DateTime.TryParseExact(s ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default;
}

/// <summary>Keys of <see cref="RunRecord.Numbers"/>. Stored in the run files: never rename.</summary>
public static class RunNumbers
{
    // The game's end screen (GameData.Progress / Currency at the end).
    public const string PlayTime = "playTime";              // the end screen's time (counts the castle before the run too)
    public const string Kills = "kills";
    public const string EliteKills = "eliteKills";
    public const string BossKills = "bossKills";
    public const string AdventurerKills = "adventurerKills"; // ours: adventurers killed in this run
    public const string TotalDamage = "totalDamage";
    public const string DamageTaken = "damageTaken";
    public const string BestDamage = "bestDamage";
    public const string Healed = "healed";
    public const string LostHealth = "lostHealth";
    public const string Gold = "gold";
    public const string GoldLeft = "goldLeft";              // the gold balance at the end (the daily run's points)
    public const string DarkQuartz = "darkQuartz";
    public const string Bones = "bones";
    public const string HeartQuartz = "heartQuartz";
    public const string SkullsSeen = "skullsSeen";
    public const string ItemsSeen = "itemsSeen";
    public const string EssencesSeen = "essencesSeen";
    public const string Revives = "revives";
    public const string Deaths = "deaths";                 // the save's total deaths at the end (end screen)
    // Ours.
    public const string Swaps = "swaps";
    public const string Maps = "maps";
    public const string JoinedLate = "joinedLate";
    /// <summary>The player's stats at the end: "stat:" + the game's Stat.Kind name (its final value).</summary>
    public const string StatPrefix = "stat:";        // 1: recorded from the middle of the run on (no file at its start)
}

/// <summary>Sums the run's hits into damage-table rows (the combat log's GearTable keys). Plain logic.</summary>
public sealed class RunDamageCounter
{
    private readonly Dictionary<(GearGroup group, string name), GearTotal> _byGear = new();

    public void Add(GearGroup group, string name, double amount)
    {
        if (!_byGear.TryGetValue((group, name), out var total))
            _byGear[(group, name)] = total = new GearTotal();
        total.Amount += amount;
        total.Hits++;
        if (amount > total.Biggest)
            total.Biggest = amount;
    }

    public void Load(IEnumerable<RunDamage> rows)
    {
        _byGear.Clear();
        foreach (var row in rows)
            if (Enum.TryParse(row.Group, out GearGroup group))
                _byGear[(group, row.Name)] = new GearTotal { Amount = row.Amount, Hits = row.Hits, Biggest = row.Biggest };
    }

    /// <summary>The rows in table order (group, then most damage first).</summary>
    public List<RunDamage> Rows() =>
        _byGear.OrderBy(p => p.Key.group).ThenByDescending(p => p.Value.Amount)
            .Select(p => new RunDamage { Group = p.Key.group.ToString(), Name = p.Key.name, Amount = p.Value.Amount, Hits = p.Value.Hits, Biggest = p.Value.Biggest })
            .ToList();
}
