using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DamageInsight.Rewards;

/// <summary>
/// The kinds of coins (Umut 2026-10-26: "different coin types should exist"): each kind has its own summons per run,
/// so a run never has to choose between a skull and an item.
/// </summary>
public enum CoinKind { Skull, Item, Adventurer, Omen }

/// <summary>
/// The rules of the bestiary rewards (0.15, docs/V015_SPEC.md part B), as plain logic: which gear gives a coin when
/// it breaks, which skull a coin gives back, an enemy's quest step, which adventurer a fighter is and the legendary
/// item its coins summon. No Unity.
/// </summary>
public static class RewardRules
{
    /// <summary>The six adventurers (their characters are "AdventurerWarrior", "VeteranHero", "AdventurerHero_Conversation", ...).</summary>
    public static readonly string[] Adventurers = { "Warrior", "Cleric", "Hero", "Magician", "Archer", "Thief" };

    /// <summary>
    /// The adventurer's legendary item: the rarest of the three its corpse can drop (weight 10 of 100, 7 % chance, only
    /// in the Dark Mirror; tools/unity/adventurer_drops.py).
    /// </summary>
    public static string ChosenItem(string adventurer) => adventurer switch
    {
        "Warrior" => "TheChosenWarriorsArmor",
        "Cleric" => "TheChosenClericsBible",
        "Hero" => "TheChosenHerosCirclet",
        "Magician" => "TheChosenMagesBadge",
        "Archer" => "TheChosenHuntersArrow",
        "Thief" => "TheChosenThiefsTwinSwords",
        _ => "",
    };

    /// <summary>Which adventurer a character is, by its key or object name ("" for anyone else).</summary>
    public static string AdventurerOf(string name)
    {
        if (string.IsNullOrEmpty(name) || (name.IndexOf("Adventurer", StringComparison.Ordinal) < 0 && name.IndexOf("Veteran", StringComparison.Ordinal) < 0))
            return "";
        foreach (string adventurer in Adventurers)
            if (name.IndexOf(adventurer, StringComparison.Ordinal) >= 0)
                return adventurer;
        return "";
    }

    private static readonly Regex TierSuffix = new(@"^(.*?)(?:_(\d+))?$");

    /// <summary>
    /// The skull a skull's coin gives back: its first tier ("Warrior_3", raised with bones, gives "Warrior";
    /// Umut 2026-10-25: yes). <paramref name="firstTier"/> is the Codex's awakening path when known.
    /// </summary>
    public static string BaseSkull(string name, string firstTier = null) =>
        !string.IsNullOrEmpty(firstTier) ? firstTier : TierSuffix.Match(name ?? "").Groups[1].Value;

    /// <summary>
    /// The kills one step of an enemy's quest takes: <paramref name="runs"/> runs' worth of that enemy (the Codex's
    /// kills per run from the level analysis), at least 10, at most 150, so a footman met by the hundred and a rare
    /// enemy pay about equally often. Developer builds: a fifth of it (at least 2), quick to see with the same ratio
    /// between common and rare enemies (Umut 2026-10-26: "the ratio of mobs is not the same").
    /// </summary>
    public static int QuestStep(double killsPerRun, double runs, bool developer = false)
    {
        int step = (int)Math.Max(10, Math.Min(150, Math.Round(killsPerRun * runs)));
        return developer ? Math.Max(2, (int)Math.Round(step / 5.0)) : step;
    }

    public static readonly CoinKind[] CoinKinds = { CoinKind.Skull, CoinKind.Item, CoinKind.Adventurer, CoinKind.Omen };

    /// <summary>A coin's kind by the gear it summons ("skull:...", an adventurer's "item:TheChosen...", other items).</summary>
    public static CoinKind KindOf(string gearId) =>
        gearId != null && gearId.StartsWith("skull:", StringComparison.Ordinal) ? CoinKind.Skull
        : gearId != null && gearId.StartsWith("item:TheChosen", StringComparison.Ordinal) ? CoinKind.Adventurer
        : CoinKind.Item;

    /// <summary>
    /// Coins one summon takes (Umut 2026-10-26: "Hopefully not just one"): the setting for legendary skulls, items and
    /// Omen items; an adventurer's coin alone, as it already takes several wins over that adventurer.
    /// </summary>
    public static int CoinCost(CoinKind kind, int perSummon) => kind == CoinKind.Adventurer ? 1 : Math.Max(1, perSummon);

    /// <summary>
    /// How many quest steps a full run (all five chapters) finishes on average: each normal enemy's kills per run
    /// (the level analysis) over its step, added up. Fractions carry over between runs, so the average holds.
    /// </summary>
    public static double QuestsPerRun(IEnumerable<double> killsPerRun, double runs)
    {
        double quests = 0;
        foreach (double perRun in killsPerRun)
            if (perRun > 0)
                quests += perRun / QuestStep(perRun, runs);
        return quests;
    }

    /// <summary>Kills a dark enemy (elite) counts for toward its quest.</summary>
    public const int DarkEnemyKills = 2;
}
