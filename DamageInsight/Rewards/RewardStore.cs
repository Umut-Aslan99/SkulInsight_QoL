using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.Rewards;

/// <summary>An enemy's endless quest: kills toward the next step, and the steps done.</summary>
public sealed class QuestProgress
{
    public int Kills;
    public int Done;
}

/// <summary>
/// The bestiary rewards of the save slot being played (0.15, docs/V015_SPEC.md part B), in Codex/rewards.json so they
/// move with the slot: coins (a legendary item or skull broken, an adventurer defeated often enough: each summons
/// that gear once in a later run), Omen coins, inscription activations, the kill quests and what they paid, and the
/// run in progress (summons used, inscriptions raised). The achievement rewards need no file: they follow the slot's
/// achievements. Plain logic, no Unity.
/// </summary>
public static class RewardStore
{
    public const int Format = 1;

    /// <summary>The Codex folder (set by the plugin; tests use a temporary folder).</summary>
    public static string Folder = "";

    private static string FilePath => Path.Combine(Folder, "rewards.json");

    private static bool _loaded;
    private static readonly Dictionary<string, int> CoinCounts = new();
    private static readonly Dictionary<string, int> AdventurerKills = new();
    private static readonly Dictionary<string, QuestProgress> Quests = new();
    private static int _omenCoins, _activations, _coinsEarned, _summons;
    private static long _questQuartz, _questGold;

    private static string _run = "";
    private static readonly Dictionary<CoinKind, int> SummonsByKind = new();
    private static readonly List<string> Activated = new();
    private static readonly HashSet<string> Maxed = new();
    private static readonly List<string> Summoned = new();
    private static bool _maxedPaid;

    /// <summary>Increases on every change (the book redraws).</summary>
    public static int Version { get; private set; }

    private static void Ensure()
    {
        if (!_loaded)
            Load();
    }

    // ------------------------------------------------------------------ coins

    /// <summary>Coins held for a gear (its Codex id: "item:TheChosenWarriorsArmor", "skull:Samurai").</summary>
    public static int Coins(string gearId)
    {
        Ensure();
        return gearId != null && CoinCounts.TryGetValue(gearId, out int n) ? n : 0;
    }

    /// <summary>Every gear with coins held, in name order.</summary>
    public static List<KeyValuePair<string, int>> AllCoins
    {
        get
        {
            Ensure();
            return CoinCounts.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        }
    }

    public static void AddCoin(string gearId)
    {
        Ensure();
        if (string.IsNullOrEmpty(gearId))
            return;
        CoinCounts[gearId] = Coins(gearId) + 1;
        _coinsEarned++;
        Changed();
    }

    /// <summary>Uses a summon's coins in the run in progress (the caller checks the per-run limit).</summary>
    public static bool UseCoin(string gearId, int cost = 1)
    {
        Ensure();
        cost = Math.Max(1, cost);
        if (Coins(gearId) < cost)
            return false;
        CoinCounts[gearId] -= cost;
        if (CoinCounts[gearId] == 0)
            CoinCounts.Remove(gearId);
        Summon(gearId, RewardRules.KindOf(gearId));
        return true;
    }

    public static int OmenCoins
    {
        get
        {
            Ensure();
            return _omenCoins;
        }
    }

    public static void AddOmenCoins(int count)
    {
        Ensure();
        if (count <= 0)
            return;
        _omenCoins += count;
        _coinsEarned += count;
        Changed();
    }

    /// <summary>Uses a summon's Omen coins on the Omen item chosen.</summary>
    public static bool UseOmenCoin(string gearId, int cost = 1)
    {
        Ensure();
        cost = Math.Max(1, cost);
        if (_omenCoins < cost)
            return false;
        _omenCoins -= cost;
        Summon(gearId, CoinKind.Omen);
        return true;
    }

    private static void Summon(string gearId, CoinKind kind)
    {
        _summons++;
        SummonsByKind[kind] = SummonsThisRun(kind) + 1;
        Summoned.Add(gearId ?? "");
        Changed();
    }

    /// <summary>
    /// Gear summoned in this run: it earns no coin back (breaking it, or ending the run with a summoned Omen item,
    /// would otherwise give its coin again every run).
    /// </summary>
    public static bool SummonedThisRun(string gearId)
    {
        Ensure();
        return gearId != null && Summoned.Contains(gearId);
    }

    /// <summary>Coins earned and summons made over the slot's life (the Rewards tab).</summary>
    public static (int earned, int summons) CoinTotals
    {
        get
        {
            Ensure();
            return (_coinsEarned, _summons);
        }
    }

    /// <summary>Counts a defeated adventurer; true when this kill earned its coin (every <paramref name="needed"/>).</summary>
    public static bool CountAdventurerKill(string adventurer, int needed)
    {
        Ensure();
        if (string.IsNullOrEmpty(adventurer))
            return false;
        int kills = (AdventurerKills.TryGetValue(adventurer, out int n) ? n : 0) + 1;
        bool coin = kills >= Math.Max(1, needed);
        AdventurerKills[adventurer] = coin ? 0 : kills;
        if (coin)
            Changed();
        else
            ChangedSoon();
        return coin;
    }

    public static int AdventurerProgress(string adventurer)
    {
        Ensure();
        return adventurer != null && AdventurerKills.TryGetValue(adventurer, out int n) ? n : 0;
    }

    // ------------------------------------------------------------------ inscription activations

    public static int Activations
    {
        get
        {
            Ensure();
            return _activations;
        }
    }

    /// <summary>
    /// An inscription reached its top step: true the first time in this run (one activation earned then).
    /// </summary>
    public static bool NoteMaxed(string inscription)
    {
        Ensure();
        if (string.IsNullOrEmpty(inscription) || _maxedPaid || !Maxed.Add(inscription))
            return false;
        Changed();
        return true;
    }

    /// <summary>The inscriptions that reached their top step in this run (paid when it ends).</summary>
    public static IEnumerable<string> MaxedThisRun
    {
        get
        {
            Ensure();
            return Maxed.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// The run ended (won or lost; Umut 2026-10-26: "at the end of the entire run"): one activation for each
    /// inscription that reached its top step in it, once. Returns how many were paid.
    /// </summary>
    public static int PayMaxed()
    {
        Ensure();
        if (_maxedPaid)
            return 0;
        _maxedPaid = true;
        _activations += Maxed.Count;
        Changed();
        return Maxed.Count;
    }

    /// <summary>Activations given straight away (the developer builds' test key).</summary>
    public static void AddActivations(int count)
    {
        Ensure();
        if (count <= 0)
            return;
        _activations += count;
        Changed();
    }

    /// <summary>Uses an activation on an inscription in the run in progress (the caller checks the per-run limit).</summary>
    public static bool UseActivation(string inscription)
    {
        Ensure();
        if (_activations <= 0 || string.IsNullOrEmpty(inscription))
            return false;
        _activations--;
        Activated.Add(inscription);
        Changed();
        return true;
    }

    /// <summary>The inscriptions raised by an activation in this run (one entry per activation).</summary>
    public static IReadOnlyList<string> ActivatedThisRun
    {
        get
        {
            Ensure();
            return Activated;
        }
    }

    // ------------------------------------------------------------------ kill quests

    public static QuestProgress Quest(string enemyId)
    {
        Ensure();
        return enemyId != null && Quests.TryGetValue(enemyId, out var q) ? q : new QuestProgress();
    }

    /// <summary>Counts kills toward an enemy's quest; returns how many steps they finished (usually 0 or 1).</summary>
    public static int CountKill(string enemyId, int step, int kills = 1)
    {
        Ensure();
        if (string.IsNullOrEmpty(enemyId) || kills <= 0)
            return 0;
        if (!Quests.TryGetValue(enemyId, out var q))
            Quests[enemyId] = q = new QuestProgress();
        step = Math.Max(1, step);
        q.Kills += kills;
        int done = 0;
        while (q.Kills >= step)
        {
            q.Kills -= step;
            q.Done++;
            done++;
        }
        if (done > 0)
            Changed();
        else
            ChangedSoon();
        return done;
    }

    public static void AddPaid(int darkQuartz, int gold)
    {
        Ensure();
        _questQuartz += Math.Max(0, darkQuartz);
        _questGold += Math.Max(0, gold);
        Changed();
    }

    /// <summary>The quests done over the slot's life and what they paid.</summary>
    public static (int done, long darkQuartz, long gold) QuestTotals
    {
        get
        {
            Ensure();
            return (Quests.Values.Sum(q => q.Done), _questQuartz, _questGold);
        }
    }

    // ------------------------------------------------------------------ the run in progress

    /// <summary>A run began (or a continued run was loaded: the same id keeps its summons and activations).</summary>
    public static void BeginRun(string runId)
    {
        Ensure();
        runId ??= "";
        if (runId == _run)
            return;
        _run = runId;
        SummonsByKind.Clear();
        Activated.Clear();
        Maxed.Clear();
        _maxedPaid = false;
        Summoned.Clear();
        Changed();
    }

    public static string Run
    {
        get
        {
            Ensure();
            return _run;
        }
    }

    /// <summary>Coins of this kind used in the run in progress.</summary>
    public static int SummonsThisRun(CoinKind kind)
    {
        Ensure();
        return SummonsByKind.TryGetValue(kind, out int n) ? n : 0;
    }

    public static int SummonsThisRunTotal
    {
        get
        {
            Ensure();
            return SummonsByKind.Values.Sum();
        }
    }

    /// <summary>Save slot switch: the next slot's Codex folder is in place; its file is read afresh. (The switch calls
    /// <see cref="Flush"/> before it moves the folders: a kill counted since then belongs to the slot that was left.)</summary>
    public static void ForgetSlot()
    {
        _loaded = false;
        _dirtySince = -1;
        Version++;
    }

    private static void Changed()
    {
        Version++;
        Save();
    }

    // Kills come many a second with some builds: each one wrote the whole file (0.15.0, a stutter in big fights; Umut
    // 2026-10-28 "the games performance somehow is suffering quite a lot especially with some builds"). They are saved
    // a few seconds later instead, and at the latest when the game closes or the save slot changes.
    private const double SaveDelaySeconds = 3;
    private static double _dirtySince = -1;

    /// <summary>The clock for the delayed save (seconds; tests set their own).</summary>
    public static Func<double> Clock = () => Environment.TickCount / 1000.0;

    private static void ChangedSoon()
    {
        Version++;
        if (_dirtySince < 0)
            _dirtySince = Clock();
    }

    /// <summary>Every frame: a delayed save once its time has come.</summary>
    public static void Tick()
    {
        if (_dirtySince >= 0 && Clock() - _dirtySince >= SaveDelaySeconds)
            Flush();
    }

    /// <summary>Writes changes that wait for their delayed save now (before a slot switch, at quit).</summary>
    public static void Flush()
    {
        if (_dirtySince < 0)
            return;
        Save();
    }

    // ------------------------------------------------------------------ the file

    private static void Clear()
    {
        CoinCounts.Clear();
        AdventurerKills.Clear();
        Quests.Clear();
        _omenCoins = _activations = _coinsEarned = _summons = 0;
        _questQuartz = _questGold = 0;
        _run = "";
        SummonsByKind.Clear();
        Activated.Clear();
        Maxed.Clear();
        _maxedPaid = false;
        Summoned.Clear();
    }

    private static void Load()
    {
        _loaded = true;
        Clear();
        try
        {
            if (!File.Exists(FilePath))
                return;
            Read(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch (Exception)
        {
            // An unreadable file: kept as .bak, the slot's coins and quests start again.
            try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch (Exception) { /* nothing to keep */ }
            Clear();
        }
    }

    /// <summary>The store as it is now (taken when a room is entered: a room restart puts it back).</summary>
    public static string RoomSnapshot()
    {
        Ensure();
        return ToJson();
    }

    /// <summary>
    /// Restart room (0.16): coins, quests and activations go back to what they were when the room was entered, like the
    /// game's own room save (no quest farming by restarting a room; a summon made in the room is given back with its coins).
    /// </summary>
    public static void RestoreRoom(string snapshot)
    {
        if (string.IsNullOrEmpty(snapshot))
            return;
        Ensure();
        string before = ToJson();
        try
        {
            Clear();
            Read(snapshot);
        }
        catch (Exception)
        {
            Clear();
            Read(before);
        }
        Changed();
    }

    private static void Read(string text)
    {
        var root = GearDoc.ParseNode(text);
        ReadCounts(root.Raw("coins"), CoinCounts);
        ReadCounts(root.Raw("adventurerKills"), AdventurerKills);
        if (root.Raw("quests") is Dictionary<string, object> quests)
            foreach (var pair in quests)
                if (pair.Value is List<object> pairOfNumbers && pairOfNumbers.Count == 2)
                    Quests[pair.Key] = new QuestProgress { Kills = Int(pairOfNumbers[0]), Done = Int(pairOfNumbers[1]) };
        _omenCoins = (int)root.Num("omenCoins");
        _activations = (int)root.Num("activations");
        _coinsEarned = (int)root.Num("coinsEarned");
        _summons = (int)root.Num("summons");
        _questQuartz = (long)root.Num("questDarkQuartz");
        _questGold = (long)root.Num("questGold");
        _run = root.Str("run") ?? "";
        if (root.Raw("summonsThisRun") is Dictionary<string, object> summons)
            foreach (var pair in summons)
                if (Enum.TryParse(pair.Key, out CoinKind kind) && Int(pair.Value) > 0)
                    SummonsByKind[kind] = Int(pair.Value);
        if (root.Raw("activatedThisRun") is List<object> activated)
            Activated.AddRange(activated.OfType<string>());
        _maxedPaid = root.Bool("maxedPaid");
        if (root.Raw("maxedThisRun") is List<object> maxed)
            foreach (var key in maxed.OfType<string>())
                Maxed.Add(key);
        if (root.Raw("summonedThisRun") is List<object> summoned)
            Summoned.AddRange(summoned.OfType<string>());
    }

    private static int Int(object value) => value is double d ? (int)d : value is long l ? (int)l : value is int i ? i : 0;

    private static void ReadCounts(object raw, Dictionary<string, int> into)
    {
        if (raw is Dictionary<string, object> counts)
            foreach (var pair in counts)
                if (Int(pair.Value) > 0)
                    into[pair.Key] = Int(pair.Value);
    }

    private static string Counts(Dictionary<string, int> counts) =>
        "{" + string.Join(", ", counts.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => LogJson.Quote(p.Key) + ": " + p.Value)) + "}";

    private static string Strings(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(LogJson.Quote)) + "]";

    internal static string ToJson()
    {
        Ensure();
        var sb = new StringBuilder();
        sb.Append("{\n  \"format\": ").Append(Format)
          .Append(",\n  \"coins\": ").Append(Counts(CoinCounts))
          .Append(",\n  \"omenCoins\": ").Append(_omenCoins)
          .Append(",\n  \"activations\": ").Append(_activations)
          .Append(",\n  \"coinsEarned\": ").Append(_coinsEarned)
          .Append(",\n  \"summons\": ").Append(_summons)
          .Append(",\n  \"adventurerKills\": ").Append(Counts(AdventurerKills))
          .Append(",\n  \"questDarkQuartz\": ").Append(_questQuartz)
          .Append(",\n  \"questGold\": ").Append(_questGold)
          .Append(",\n  \"quests\": {");
        bool first = true;
        foreach (var pair in Quests.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(first ? "\n" : ",\n").Append("    ").Append(LogJson.Quote(pair.Key)).Append(": [")
              .Append(pair.Value.Kills).Append(", ").Append(pair.Value.Done).Append(']');
            first = false;
        }
        sb.Append(first ? "}" : "\n  }")
          .Append(",\n  \"run\": ").Append(LogJson.Quote(_run))
          .Append(",\n  \"summonsThisRun\": {").Append(string.Join(", ", SummonsByKind.Where(p => p.Value > 0).OrderBy(p => p.Key)
              .Select(p => LogJson.Quote(p.Key.ToString()) + ": " + p.Value))).Append('}')
          .Append(",\n  \"activatedThisRun\": ").Append(Strings(Activated))
          .Append(",\n  \"maxedThisRun\": ").Append(Strings(Maxed.OrderBy(k => k, StringComparer.Ordinal)))
          .Append(",\n  \"maxedPaid\": ").Append(_maxedPaid ? "true" : "false")
          .Append(",\n  \"summonedThisRun\": ").Append(Strings(Summoned))
          .Append("\n}\n");
        return sb.ToString();
    }

    private static void Save()
    {
        _dirtySince = -1;
        if (string.IsNullOrEmpty(Folder))
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(temp, FilePath);
        }
        catch (Exception)
        {
            // Kept in memory; the next change tries again.
        }
    }
}
