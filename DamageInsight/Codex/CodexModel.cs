using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.Codex;

public enum CodexCategory
{
    Enemies,
    Bosses,
    Skulls,
    Items,
    Essences,
    Inscriptions,
    DarkAbilities,
}

/// <summary>One page of the Codex: an enemy, boss, skull, item, essence or inscription.</summary>
public sealed class CodexEntry
{
    public string Id = "";          // "enemy:CarleonRecruit", "item:HopeCutter", ...
    public CodexCategory Category;
    public string Name = "";        // localized name
    public string Group = "";       // "Chapter 1", "Boss", "Legendary", ...
    public int Order;               // sort order inside the category
    public string Key = "";         // the game's key (enum name or prefab name)
}

/// <summary>What the player has done with one entry. Plain data, saved to progress.json.</summary>
public sealed class EntryProgress
{
    public int Seen;
    public int Kills;
    public int DeathsBy;
    public int PickedUp;
    public double DamageDealt;      // by the player to this enemy
    public double DamageTaken;      // by the player from this enemy
    public double BestHit;          // the player's biggest hit on it
    public double WorstHit;         // its biggest hit on the player
    public double MaxHp;            // highest maximum HP seen

    // Only for entries the game has no key for (e.g. Dark Mirror bosses): how to show them.
    public string Name = "";
    public string Category = "";

    /// <summary>A piece of a boss fight without a mind of its own (Pope's dark crystals): shown on that boss's page.</summary>
    public string PartOf = "";

    /// <summary>Bosses/adventurers whose fights it appeared in (their summons), comma-separated entry ids.</summary>
    public string SummonedBy = "";

    /// <summary>Moves this boss/adventurer was seen doing, "|"-separated, per mode (Normal / Dark Mirror).</summary>
    public string Moves = "", MovesDark = "";

    public IEnumerable<string> MovesSeen(bool dark) => (dark ? MovesDark : Moves).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Notes a seen move; true if it is new.</summary>
    public bool AddMove(string label, bool dark)
    {
        if (string.IsNullOrEmpty(label) || MovesSeen(dark).Contains(label))
            return false;
        string list = dark ? MovesDark : Moves;
        list = list.Length == 0 ? label : list + "|" + label;
        if (dark) MovesDark = list; else Moves = list;
        return true;
    }

    public bool IsSummonedBy(string bossId) => ("," + SummonedBy + ",").Contains("," + bossId + ",");

    public void AddSummoner(string bossId)
    {
        if (!IsSummonedBy(bossId))
            SummonedBy = SummonedBy.Length == 0 ? bossId : SummonedBy + "," + bossId;
    }
}

/// <summary>All Codex progress, keyed by entry id. Plain logic, no Unity.</summary>
public sealed class CodexProgress
{
    public readonly Dictionary<string, EntryProgress> Entries = new();

    public EntryProgress Get(string id) =>
        Entries.TryGetValue(id, out var p) ? p : Entries[id] = new EntryProgress();

    public EntryProgress Peek(string id) => Entries.TryGetValue(id, out var p) ? p : null;

    public string ToJson()
    {
        var sb = new StringBuilder("{\"version\":1,\"entries\":{");
        bool first = true;
        foreach (var pair in Entries.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            var p = pair.Value;
            sb.Append(LogJson.Quote(pair.Key)).Append(":{");
            sb.Append("\"seen\":").Append(p.Seen)
              .Append(",\"kills\":").Append(p.Kills)
              .Append(",\"deathsBy\":").Append(p.DeathsBy)
              .Append(",\"pickedUp\":").Append(p.PickedUp)
              .Append(",\"damageDealt\":").Append(Num(p.DamageDealt))
              .Append(",\"damageTaken\":").Append(Num(p.DamageTaken))
              .Append(",\"bestHit\":").Append(Num(p.BestHit))
              .Append(",\"worstHit\":").Append(Num(p.WorstHit))
              .Append(",\"maxHp\":").Append(Num(p.MaxHp));
            if (p.Name.Length > 0)
                sb.Append(",\"name\":").Append(LogJson.Quote(p.Name)).Append(",\"category\":").Append(LogJson.Quote(p.Category));
            if (p.PartOf.Length > 0)
                sb.Append(",\"partOf\":").Append(LogJson.Quote(p.PartOf));
            if (p.SummonedBy.Length > 0)
                sb.Append(",\"summonedBy\":").Append(LogJson.Quote(p.SummonedBy));
            if (p.Moves.Length > 0)
                sb.Append(",\"moves\":").Append(LogJson.Quote(p.Moves));
            if (p.MovesDark.Length > 0)
                sb.Append(",\"movesDark\":").Append(LogJson.Quote(p.MovesDark));
            sb
              .Append('}');
        }
        return sb.Append("}}").ToString();
    }

    public static CodexProgress FromJson(string json)
    {
        var progress = new CodexProgress();
        if (string.IsNullOrWhiteSpace(json))
            return progress;
        var entries = GearDoc.ParseNode(json).Child("entries");
        if (entries.IsNull)
            return progress;
        foreach (var (id, _) in entries.Fields())
        {
            var n = entries.Child(id);
            progress.Entries[id] = new EntryProgress
            {
                Seen = (int)n.Num("seen"),
                Kills = (int)n.Num("kills"),
                DeathsBy = (int)n.Num("deathsBy"),
                PickedUp = (int)n.Num("pickedUp"),
                DamageDealt = n.Num("damageDealt"),
                DamageTaken = n.Num("damageTaken"),
                BestHit = n.Num("bestHit"),
                WorstHit = n.Num("worstHit"),
                MaxHp = n.Num("maxHp"),
                Name = n.Str("name") ?? "",
                Category = n.Str("category") ?? "",
                PartOf = n.Str("partOf") ?? "",
                SummonedBy = n.Str("summonedBy") ?? "",
                Moves = n.Str("moves") ?? "",
                MovesDark = n.Str("movesDark") ?? "",
            };
        }
        return progress;
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Which enemy keys go where, from the numbering of Characters.Key.</summary>
public static class CodexGroups
{
    /// <summary>Category and group of an enemy key value, or null for things that aren't enemies (players, props, altars, minions).</summary>
    public static (CodexCategory category, string group)? ForEnemy(int key)
    {
        if (key >= 2000 && key < 3000)
            return (CodexCategory.Bosses, "Boss");
        if (key >= 1000 && key < 2000)
            return (CodexCategory.Enemies, "Adventurer mages");
        if (key >= 10000 && key < 60000)
        {
            int chapter = key / 10000;               // 1..4, 5 = Sentinel
            bool later = key % 10000 >= 5000;        // 15xxx, 25xxx, ...: added in later updates
            if (chapter >= 5)
                return (CodexCategory.Enemies, "Special");
            return (CodexCategory.Enemies, later ? $"Chapter {chapter} · more" : $"Chapter {chapter}");
        }
        return null;
    }
}

/// <summary>How much of an entry is revealed, from the player's progress.</summary>
public static class CodexTiers
{
    public const int Max = 3;

    /// <summary>Developer builds: everything met is shown fully (no kills needed to see pictures and films).</summary>
    public static bool RevealAllMet;

    /// <summary>0 = unknown ("???"), 1 = seen, 2 = known, 3 = mastered. Thresholds: <see cref="CodexBalance"/>.</summary>
    public static int Tier(CodexCategory category, EntryProgress p, string id = null)
    {
        if (p == null)
            return 0;
        if (RevealAllMet && (p.Seen > 0 || p.Kills > 0 || p.PickedUp > 0))
            return Max;
        int need2 = Need(category, id, 2), need3 = Need(category, id, 3);
        int have = category is CodexCategory.Enemies or CodexCategory.Bosses ? p.Kills : p.PickedUp;
        bool met = category == CodexCategory.Enemies ? p.Seen > 0 || p.Kills > 0 : p.Seen > 0;
        return have >= need3 ? 3 : have >= need2 ? 2 : met ? 1 : 0;
    }

    /// <summary>What a tier needs: kills (enemies), wins (bosses, adventurers) or pickups (gear).</summary>
    public static int Need(CodexCategory category, string id, int tier) => category switch
    {
        CodexCategory.Enemies => CodexBalance.EnemyKillsFor(id, tier),
        CodexCategory.Bosses => (int)(tier >= 3 ? CodexBalance.BossWins3.Value : CodexBalance.BossWins2.Value),
        _ => (int)(tier >= 3 ? CodexBalance.GearPicks3.Value : CodexBalance.GearPicks2.Value),
    };

    /// <summary>What unlocks the next tier, e.g. ("Kill 5 to learn more", 3, 5), or null at the top.</summary>
    public static (string text, int have, int need)? Next(CodexCategory category, EntryProgress p, string id = null)
    {
        int tier = Tier(category, p, id);
        bool enemy = category is CodexCategory.Enemies or CodexCategory.Bosses;
        int kills = p?.Kills ?? 0, picks = p?.PickedUp ?? 0;
        if (tier == 0)
            return (enemy ? "Encounter it to reveal it" : "Find it to reveal it", 0, 1);
        if (tier >= Max)
            return null;
        int need = Need(category, id, tier + 1);
        string goal = tier == 1 ? "to learn more" : "to master it";
        return category switch
        {
            CodexCategory.Bosses => (need == 1 ? $"Defeat it {goal}" : $"Defeat it {need} times {goal}", kills, need),
            CodexCategory.Enemies => ($"Kill {need} {goal}", kills, need),
            _ => (need == 1 ? $"Pick it up {goal}" : $"Pick it up {need} times {goal}", picks, need),
        };
    }
}

/// <summary>
/// Forgiving search for the Codex: every letter of the query must appear in order ("crlrc" finds "Carleon
/// Recruit"); matches at word starts and consecutive letters score higher. Plain logic.
/// </summary>
public static class FuzzySearch
{
    /// <summary>A score (higher = better) or -1 if the text doesn't match.</summary>
    public static int Score(string query, string text)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0;
        query = query.Trim().ToLowerInvariant();
        string lower = (text ?? "").ToLowerInvariant();

        int direct = lower.IndexOf(query, StringComparison.Ordinal);
        if (direct >= 0)
            return 1000 - direct + (direct == 0 || !char.IsLetterOrDigit(lower[direct - 1]) ? 200 : 0);

        int score = 0, ti = 0, streak = 0;
        foreach (char c in query)
        {
            if (c == ' ')
                continue;
            int found = lower.IndexOf(c, ti);
            if (found < 0)
                return -1;
            bool wordStart = found == 0 || !char.IsLetterOrDigit(lower[found - 1]);
            streak = found == ti ? streak + 1 : 0;
            score += 10 + (wordStart ? 15 : 0) + streak * 5 - Math.Min(found - ti, 10);
            ti = found + 1;
        }
        return score;
    }

    /// <summary>The entries matching the query, best first (all entries in their order for an empty query).</summary>
    public static List<T> Filter<T>(IEnumerable<T> items, string query, Func<T, string> text)
    {
        if (string.IsNullOrWhiteSpace(query))
            return items.ToList();
        return items.Select(i => (item: i, score: Score(query, text(i))))
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.item)
            .ToList();
    }
}
