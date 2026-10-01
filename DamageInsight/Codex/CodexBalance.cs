using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.Codex;

/// <summary>
/// The Codex progression numbers in one place, adjustable while developing (the balance page in the book, or the
/// "Codex balance" config section in developer builds); release builds use the defaults.
/// Normal enemies are paced in runs, not kills: the kills a tier needs are the runs it should take times how many of
/// that enemy a run has (<see cref="KillsPerRun"/>, from the level analysis), rounded and clamped, so a footman
/// met by the hundred and a rare enemy met twice a run both take about the same number of runs.
/// </summary>
public static partial class CodexBalance
{
    public sealed class Setting
    {
        public string Key, Group, Label, Help;
        public float Default, Min, Max, Step;
        public Func<float> Get = () => 0f;
        public Action<float> Set = _ => { };
        public float Value { get => Get(); set => Set(Math.Max(Min, Math.Min(Max, value))); }
    }

    public static readonly List<Setting> All = new();

    private static Setting Add(string key, string group, string label, float value, float min, float max, float step, string help)
    {
        float current = value;
        var s = new Setting
        {
            Key = key, Group = group, Label = label, Help = help, Default = value, Min = min, Max = max, Step = step,
            Get = () => current, Set = v => current = v,
        };
        All.Add(s);
        return s;
    }

    // Normal enemies: runs to each tier, with limits in kills.
    public static readonly Setting EnemyRuns2 = Add("EnemyRuns2", "Enemies", "Runs to ★★", 1f, 0.2f, 10f, 0.2f,
        "How many runs' worth of this enemy it takes to see it in colour.");
    public static readonly Setting EnemyRuns3 = Add("EnemyRuns3", "Enemies", "Runs to ★★★", 5f, 1f, 50f, 1f,
        "How many runs' worth of this enemy it takes to master it (all animations).");
    public static readonly Setting EnemyMin2 = Add("EnemyMin2", "Enemies", "★★ at least (kills)", 3f, 1f, 50f, 1f, "Lower limit for ★★.");
    public static readonly Setting EnemyMin3 = Add("EnemyMin3", "Enemies", "★★★ at least (kills)", 10f, 1f, 200f, 1f, "Lower limit for ★★★.");
    public static readonly Setting EnemyMax3 = Add("EnemyMax3", "Enemies", "★★★ at most (kills)", 500f, 10f, 5000f, 10f, "Upper limit for ★★★.");
    public static readonly Setting DefaultKillsPerRun = Add("DefaultKillsPerRun", "Enemies", "Kills per run if unknown", 5f, 1f, 100f, 1f,
        "For enemies the level analysis has no number for.");

    // Bosses and adventurers: wins.
    public static readonly Setting BossWins2 = Add("BossWins2", "Bosses", "Boss wins to ★★", 1f, 1f, 10f, 1f, "");
    public static readonly Setting BossWins3 = Add("BossWins3", "Bosses", "Boss wins to ★★★", 3f, 1f, 30f, 1f, "");

    // Skulls, items, quintessences: pickups.
    public static readonly Setting GearPicks2 = Add("GearPicks2", "Gear", "Pickups to ★★", 1f, 1f, 10f, 1f, "");
    public static readonly Setting GearPicks3 = Add("GearPicks3", "Gear", "Pickups to ★★★", 5f, 1f, 50f, 1f, "");

    /// <summary>Kills of an enemy in an average run that reaches it (entry id → kills), from the level analysis.</summary>
    public static readonly Dictionary<string, float> KillsPerRun = new();

    static CodexBalance()
    {
        foreach (var (id, perRun) in Rates)
            KillsPerRun[id] = perRun;
    }

    /// <summary>Kills a normal enemy needs for ★★ / ★★★.</summary>
    public static int EnemyKillsFor(string entryId, int tier)
    {
        float perRun = entryId != null && KillsPerRun.TryGetValue(entryId, out float r) ? r : DefaultKillsPerRun.Value;
        float raw = perRun * (tier >= 3 ? EnemyRuns3.Value : EnemyRuns2.Value);
        int kills = Nice(raw);
        return tier >= 3
            ? Clamp(kills, (int)EnemyMin3.Value, (int)EnemyMax3.Value)
            : Clamp(kills, (int)EnemyMin2.Value, Math.Max((int)EnemyMin2.Value, EnemyKillsFor(entryId, 3) - 1));
    }

    /// <summary>A round number near <paramref name="value"/>: 1-10 as is, then 5s, 10s, 25s, 50s.</summary>
    public static int Nice(float value)
    {
        int v = (int)Math.Round(value);
        int step = v <= 10 ? 1 : v <= 50 ? 5 : v <= 100 ? 10 : v <= 250 ? 25 : 50;
        return Math.Max(1, (int)Math.Round(v / (double)step) * step);
    }

    private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

    /// <summary>Binds every setting to the config (developer builds), so changes stay and can be edited there too.</summary>
    public static void Bind(BepInEx.Configuration.ConfigFile config)
    {
        foreach (var s in All)
        {
            var entry = config.Bind("Codex balance", s.Key, s.Default, s.Label + (s.Help.Length > 0 ? ". " + s.Help : ""));
            s.Get = () => entry.Value;
            s.Set = v => entry.Value = v;
        }
    }

    public static IEnumerable<IGrouping<string, Setting>> Groups => All.GroupBy(s => s.Group);
}
