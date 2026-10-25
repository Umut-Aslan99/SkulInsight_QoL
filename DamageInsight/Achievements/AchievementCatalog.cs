using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Lang;

namespace DamageInsight.Achievements;

/// <summary>What an achievement is about (tips, the page's links and the progress line).</summary>
public enum AchievementKind
{
    Other,
    Boss,        // a chapter's boss defeated
    NoHit,       // a boss defeated without getting hit
    Skull,       // a legendary skull picked up
    DarkMirror,  // the Dark Mirror (its levels, machines, the hidden boss)
    Daily,       // SkulInsight's own, for daily runs (thumbs up received, days in a row): the daily player's, not a slot's
}

/// <summary>
/// One of Skul's achievements: Steam's 101 (texts in the game's 11 languages from Steam's community page, built by
/// tools/achievements/steam_table.py into achievements.json) or one of the two the game counts but Steam doesn't
/// have (no-hit clears of Alexander and the Dark Skeleton).
/// </summary>
public sealed class AchievementInfo
{
    /// <summary>The game's Achievement.Type name, which is Steam's API name.</summary>
    public string Id = "";
    public int Order;
    public bool OnSteam = true;
    /// <summary>The share of Steam players who have it (%), from the table (Steam's own number when it answers).</summary>
    public double Percent;
    /// <summary>The two extras: the Steam achievement whose no-hit form they are.</summary>
    public string BaseId = "";
    public AchievementKind Kind;
    /// <summary>Counted over the save (adventurers, heart quartz, ...): how many it needs (0: not counted).</summary>
    public int Target;
    /// <summary>A Dark Mirror level's clear (0-10), else -1.</summary>
    public int DarkMirrorLevel = -1;
    /// <summary>Gives the story away: veiled until earned, revealed by holding it, or until <see cref="LiftedBy"/> is met.</summary>
    public bool Secret;
    /// <summary>The Codex page (a boss) whose meeting lifts the veil.</summary>
    public string LiftedBy = "";
    /// <summary>The boss it is about (its Codex page), "" for none.</summary>
    public string Boss = "";

    internal readonly Dictionary<string, string> Names = new(), Descriptions = new();

    /// <summary>The mod's daily ones: what they count ("likes": thumbs up received, "streak": the best days in a row) and their pixel icon.</summary>
    public string Counts = "", Pixel = "";

    public string Name => Kind == AchievementKind.Daily ? DailyName : OnSteam ? Text(Names) : Loc.F("{0} (no hit)", Text(Base?.Names));

    public string Description => Kind == AchievementKind.Daily
        ? Counts == "likes" ? Loc.F("Receive {0} thumbs up on your daily runs", Target)
            : Loc.P("Play the daily run {0} day in a row", "Play the daily run {0} days in a row", Target)
        : OnSteam ? Text(Descriptions) : Loc.F("{0} without getting hit", Text(Base?.Descriptions));

    private string DailyName => Id switch
    {
        "DailyLikes5" => Loc.T("First Fans"),
        "DailyLikes10" => Loc.T("Crowd Pleaser"),
        "DailyLikes25" => Loc.T("Fan Favourite"),
        "DailyLikes50" => Loc.T("Star of the Board"),
        "DailyLikes100" => Loc.T("Legend of the Board"),
        "DailyStreak3" => Loc.T("Coming Back"),
        "DailyStreak7" => Loc.T("A Week of Bones"),
        "DailyStreak14" => Loc.T("Two Weeks Strong"),
        "DailyStreak30" => Loc.T("Daily Devotion"),
        _ => Id,
    };

    private AchievementInfo Base => AchievementCatalog.Get(BaseId);

    private static string Text(Dictionary<string, string> texts) => texts == null ? "" :
        texts.TryGetValue(Loc.Current, out var text) && text.Length > 0 ? text
        : texts.TryGetValue(Loc.English, out var english) ? english : "";

    /// <summary>Our own advice for the hard ones, read from the game's code (Umut 2026-10-04, CODEX_FINAL §6.4).</summary>
    public string Tip
    {
        get
        {
            if (Kind == AchievementKind.NoHit)
                return Loc.T("Only the fight counts: hits before the boss's map don't matter, every phase of the fight does. A hit counts when it takes at least 1 health; a hit your shield takes completely, or one you dodge, doesn't.");
            if (Kind == AchievementKind.Daily)
                return Counts == "likes"
                    ? Loc.T("Other players give thumbs up to your runs on the daily board (Codex, Daily); your own don't count.")
                    : Loc.T("Days in a row with a daily run on the board. Your longest streak counts, also one from before 0.14.");
            if (DarkMirrorLevel >= 0)
                return Loc.T("Each level counts on its own: destroy the Dark Mirror at exactly this level (a higher level doesn't give the lower ones).");
            return Id switch
            {
                "SkeletonKing" => Loc.T("Little Bone (the skull you start with) must be the skull you hold the moment the First Hero falls. Swapping to it right before the last hit is enough."),
                "ColdBlood" => Loc.T("Free none of the NPCs caged on the stages in that run, then defeat the First Hero."),
                "Concentration" => Loc.T("Your last health has to go to a trap (spikes, saws, ...), not to an enemy."),
                "HeroSlayer" => Loc.T("Counts every adventurer you defeated in this save, over all runs."),
                "Darkheart" => Loc.T("Counts all the Dark Quartz Hearts you ever earned in this save; spending them doesn't lower the count."),
                "BoneSpecialist" => Loc.T("Counts every fragment you took in this save, over all runs."),
                "WitchsHelp" => Loc.T("Counts the different inscriptions you modified in this save, over all runs."),
                _ => "",
            };
        }
    }
}

/// <summary>
/// Every achievement in the game's order (Platforms.Achievement.Type). The texts come from the embedded achievements.json;
/// kinds, counts, tips and veils are ours (from the game's code: AchievementTrackers, PlayerKillCounter, ...). Plain logic,
/// no Unity.
/// </summary>
public static class AchievementCatalog
{
    /// <summary>The game counts these (Achievement.Type) but Steam has no such achievement: base → extra.</summary>
    private static readonly (string id, string baseId)[] Extras = { ("TruePeace_2", "TruePeace"), ("TrueSalvation_2", "TrueSalvation") };

    private static readonly string[] Bosses =
        { "SavedElderEnt", "TheendofGoldManeKnights", "FakeGoddessGone", "LeoniasFall", "FirstHeroLastBattle", "TruePeace", "TrueSalvation" };

    private static readonly string[] NoHits =
    {
        "SavedElderEnt_2", "TheendofGoldManeKnights_2", "FakeGoddessGone_2", "LeoniasFall_2", "FirstHeroLastBattle_2",
        "WorldsBest", "TruePeace_2", "TrueSalvation_2",
    };

    /// <summary>The Dark Mirror's level clears in level order (DarkHearoClearTracker).</summary>
    public static readonly string[] DarkMirrorLevels =
    {
        "BrokenWorld", "DifficultAdventure", "CourageousAct", "ResultOfEffort", "SelfConfidence", "PatienceAndPerseverance",
        "ChallengeSpirit", "AmazingStep", "PhenomenalSuccess", "GreatAchievement", "ImpossibleIsNothing",
    };

    private static readonly string[] DarkMirror =
    {
        "AnotherHarmonia", "NewBeginning", "BestCondition", "TheCursedOne", "HateTheSinnerButLoveTheSin", "Darkheart",
        "TerribleOmen", "SpecialGuest", "Allin", "CareerChoice", "BlessingOfSpiderGod", "DarkAbility", "TheWarBegins",
        "ThePowerofScience", "WitchsHelp", "HiddenExistence", "DarkTechTank", "BoneSpecialist",
    };

    /// <summary>The legendary skulls: the game gives one when a picked-up legendary skull's name contains its id.</summary>
    private static readonly string[] NotSkulls = { "HallofGods" };

    private static readonly Dictionary<string, int> Targets = new()
    {
        ["HeroSlayer"] = 100, ["Darkheart"] = 100, ["BoneSpecialist"] = 3, ["WitchsHelp"] = 5,
    };

    /// <summary>
    /// The veiled ones [DECISION 2026-10-25: Steam hides all 101, so we veil only those that give the story away]:
    /// id → the boss whose Codex page lifts the veil once met.
    /// </summary>
    private static readonly Dictionary<string, string> Secrets = new()
    {
        ["FirstHeroLastBattle"] = "enemy:FirstHero1", ["FirstHeroLastBattle_2"] = "enemy:FirstHero1",
        ["SkeletonKing"] = "enemy:FirstHero1", ["ColdBlood"] = "enemy:FirstHero1", ["WorldsBest"] = "enemy:FirstHero1",
        ["TheWarBegins"] = "enemy:Alexander1", ["TruePeace"] = "enemy:Alexander1", ["TruePeace_2"] = "enemy:Alexander1",
        ["TrueSalvation"] = "enemy:DarkSkul1", ["TrueSalvation_2"] = "enemy:DarkSkul1", ["DarkTechTank"] = "enemy:DarkSkul1",
        ["HiddenExistence"] = "enemy:DarkSkul1", ["BoneSpecialist"] = "enemy:DarkSkul1",
    };

    /// <summary>The bosses' Codex pages (their first phase).</summary>
    private static readonly Dictionary<string, string> BossPages = new()
    {
        ["SavedElderEnt"] = "enemy:Yggdrasil", ["SavedElderEnt_2"] = "enemy:Yggdrasil",
        ["TheendofGoldManeKnights"] = "enemy:LeianaShortHair", ["TheendofGoldManeKnights_2"] = "enemy:LeianaShortHair",
        ["FakeGoddessGone"] = "enemy:Chimera", ["FakeGoddessGone_2"] = "enemy:Chimera",
        ["LeoniasFall"] = "enemy:Pope", ["LeoniasFall_2"] = "enemy:Pope",
        ["FirstHeroLastBattle"] = "enemy:FirstHero1", ["FirstHeroLastBattle_2"] = "enemy:FirstHero1",
        ["SkeletonKing"] = "enemy:FirstHero1", ["ColdBlood"] = "enemy:FirstHero1", ["WorldsBest"] = "enemy:FirstHero1",
        ["TruePeace"] = "enemy:Alexander1", ["TruePeace_2"] = "enemy:Alexander1",
        ["TrueSalvation"] = "enemy:DarkSkul1", ["TrueSalvation_2"] = "enemy:DarkSkul1",
    };

    private static List<AchievementInfo> _all;
    private static Dictionary<string, AchievementInfo> _byId;

    private static List<AchievementInfo> _daily;

    /// <summary>SkulInsight's own achievements for daily runs (the daily player's, the same in every slot).</summary>
    public static IReadOnlyList<AchievementInfo> Daily
    {
        get
        {
            if (_all == null)
                Load(ReadEmbedded());
            return _daily;
        }
    }

    public static IReadOnlyList<AchievementInfo> All
    {
        get
        {
            if (_all == null)
                Load(ReadEmbedded());
            return _all;
        }
    }

    public static int SteamCount => All.Count(a => a.OnSteam);

    public static AchievementInfo Get(string id)
    {
        if (_all == null)
            Load(ReadEmbedded());
        return id != null && _byId.TryGetValue(id, out var info) ? info : null;
    }

    private static string ReadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DamageInsight.Achievements.achievements.json");
        if (stream == null)
            return "{\"achievements\":[]}";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Reads the table (tests pass the file's text).</summary>
    internal static void Load(string json)
    {
        var list = new List<AchievementInfo>();
        foreach (var node in GearDoc.ParseNode(json).List("achievements"))
        {
            var info = new AchievementInfo { Id = node.Str("id") ?? "", Percent = node.Num("percent") };
            Fill(info.Names, node.Raw("name"));
            Fill(info.Descriptions, node.Raw("desc"));
            list.Add(info);
            foreach (var (id, baseId) in Extras.Where(e => e.baseId == info.Id))
                list.Add(new AchievementInfo { Id = id, BaseId = baseId, OnSteam = false });
        }
        // The game's order (Steam's list has a few the other way round: Prisoner, GraveDigger, StoneMonkey).
        list = list.OrderBy(a => Enum.TryParse(a.Id, out Platforms.Achievement.Type type) ? (int)type : int.MaxValue).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var info = list[i];
            info.Order = i;
            info.Kind = NoHits.Contains(info.Id) ? AchievementKind.NoHit
                : Bosses.Contains(info.Id) ? AchievementKind.Boss
                : DarkMirror.Contains(info.Id) || DarkMirrorLevels.Contains(info.Id) ? AchievementKind.DarkMirror
                : IsSkull(info) ? AchievementKind.Skull
                : AchievementKind.Other;
            info.DarkMirrorLevel = Array.IndexOf(DarkMirrorLevels, info.Id);
            info.Target = Targets.TryGetValue(info.Id, out int target) ? target : 0;
            info.Secret = Secrets.TryGetValue(info.Id, out var lift);
            info.LiftedBy = lift ?? "";
            info.Boss = BossPages.TryGetValue(info.Id, out var boss) ? boss : "";
        }
        _all = list;
        _daily = new List<AchievementInfo>();
        foreach (var (counts, target, pixel) in new[]
        {
            ("likes", 5, "thumb"), ("likes", 10, "thumb"), ("likes", 25, "thumb"), ("likes", 50, "thumb"), ("likes", 100, "thumb"),
            ("streak", 3, "hourglass"), ("streak", 7, "hourglass"), ("streak", 14, "hourglass"), ("streak", 30, "hourglass"),
        })
            _daily.Add(new AchievementInfo
            {
                Id = (counts == "likes" ? "DailyLikes" : "DailyStreak") + target, OnSteam = false, Kind = AchievementKind.Daily,
                Counts = counts, Target = target, Pixel = pixel, Order = list.Count + _daily.Count,
            });
        _byId = list.Concat(_daily).ToDictionary(a => a.Id);
    }

    private static void Fill(Dictionary<string, string> into, object raw)
    {
        if (raw is Dictionary<string, object> texts)
            foreach (var pair in texts)
                if (pair.Value is string text)
                    into[pair.Key] = text;
    }

    /// <summary>"Acquire &lt;a legendary skull&gt;" (Steam's English text; the Dark Mirror's "Acquire ..." ones are sorted out before).</summary>
    private static bool IsSkull(AchievementInfo info) =>
        !NotSkulls.Contains(info.Id) && info.Descriptions.TryGetValue(Loc.English, out var text) && text.StartsWith("Acquire ", StringComparison.Ordinal);

    /// <summary>
    /// The achievement a picked-up legendary skull gives (the game's TrackHeadLootAchievement: the first id in the
    /// game's order that the skull's name contains, ignoring case).
    /// </summary>
    public static AchievementInfo ForSkull(string skullName) =>
        string.IsNullOrEmpty(skullName) ? null : All.FirstOrDefault(a => skullName.IndexOf(a.Id, StringComparison.OrdinalIgnoreCase) >= 0);
}
