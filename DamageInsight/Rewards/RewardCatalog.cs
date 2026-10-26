using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DamageInsight.Achievements;
using DamageInsight.Lang;

namespace DamageInsight.Rewards;

/// <summary>The player's stats an achievement's reward raises (each in percent points: 1 = 1 %).</summary>
public enum RewardStat
{
    PhysicalAttack,
    MagicAttack,
    AttackSpeed,
    MovementSpeed,
    CooldownSpeed,
    CriticalChance,
    CriticalDamage,
    DamageTaken,   // negative: less damage taken
    Healing,       // healing received
    Gold,          // gold gained
}

/// <summary>What one achievement gives: stats for every run, or more damage against one kind of target.</summary>
public sealed class AchievementReward
{
    public string AchievementId = "";
    public string Group = "";
    public readonly List<(RewardStat Stat, double Percent)> Stats = new();

    /// <summary>
    /// The damage the player deals to this target is multiplied by <see cref="Multiplier"/>: a boss's Codex page
    /// ("enemy:Yggdrasil", all its phases), <see cref="RewardCatalog.Adventurers"/>, <see cref="RewardCatalog.LittleBone"/>
    /// (while holding it) or a boss page with <see cref="RewardCatalog.DarkMirrorPrefix"/> (only in the Dark Mirror).
    /// </summary>
    public string Target = "";
    public double Multiplier = 1.0;
}

/// <summary>What a slot's rewards add up to.</summary>
public sealed class RewardTotals
{
    public readonly Dictionary<RewardStat, double> Stats = new();
    public readonly Dictionary<string, double> Targets = new();
    public int Count;

    public double Stat(RewardStat stat) => Stats.TryGetValue(stat, out double value) ? value : 0;

    /// <summary>The damage multiplier against a target (1 without a reward).</summary>
    public double Target(string key) => key != null && Targets.TryGetValue(key, out double value) ? value : 1.0;
}

/// <summary>
/// A reward for each of the game's 103 achievements (0.15, Umut 2026-10-25: "rewards for each achievement as well that
/// thematically fits ... Something minuscule, but it should add up in the long run"; the hard ones "+5 or 10%"
/// against that boss; groups that fit one stat). The table is docs/V015_SPEC.md part A. SkulInsight's daily
/// achievements give none (later: skull skins). Stat rewards add up (percent points, like the Witch's), target
/// multipliers multiply. Plain logic, no Unity.
/// </summary>
public static class RewardCatalog
{
    public const string Adventurers = "adventurers";
    public const string LittleBone = "skull:Skul";
    public const string DarkMirrorPrefix = "darkmirror:";

    public const double BossMultiplier = 1.03, NoHitMultiplier = 1.10;

    /// <summary>The groups in the order the Rewards tab shows them.</summary>
    public static readonly string[] Groups =
        { "boss", "nohit", "darkmirror", "skull", "danger", "shops", "world", "dark", "hidden", "special" };

    private static readonly (string group, RewardStat[] stats, double percent, string[] ids)[] StatGroups =
    {
        ("danger", new[] { RewardStat.DamageTaken }, -1, new[] { "TheLegendBegins", "Concentration", "NewBeginning" }),
        ("shops", new[] { RewardStat.Gold }, 1, new[] { "WhattoBuy", "BrainPoolOperation", "StockManagement", "SpecialGuest", "Allin" }),
        ("world", new[] { RewardStat.MovementSpeed }, 1, new[]
            { "GoHome", "DemonCastleRestoration", "DwarfChandelier", "Manners", "Chatterbox", "HallofGods", "ThePowerofScience", "TheWarBegins" }),
        ("dark", new[] { RewardStat.CooldownSpeed }, 1, new[]
        {
            "BestCondition", "TheCursedOne", "DarkAbility", "HateTheSinnerButLoveTheSin", "TerribleOmen", "CareerChoice", "WitchsHelp",
            "Darkheart", "BlessingOfSpiderGod",
        }),
        ("hidden", new[] { RewardStat.CriticalChance }, 1, new[] { "AnotherHarmonia", "HiddenExistence", "DarkTechTank", "BoneSpecialist" }),
        ("special", new[] { RewardStat.CriticalDamage }, 3, new[] { "ColdBlood" }),
        ("special", new[] { RewardStat.Healing }, 5, new[] { "RookieWelcome" }),
        // The legendary skulls by the skull's type (the gear scan's Weapon.Category): Power, Speed, Balance.
        ("skull", new[] { RewardStat.PhysicalAttack }, 0.5, new[]
        {
            "Skeleton_Shield", "EntSkul", "Ghoul", "Minotaurus", "AquaSkull", "Warrior", "Genie", "LivingArmor", "Berserker", "Predator",
            "Yaksha", "GraveDigger", "DavyJones", "GuanYu", "Hercules",
        }),
        ("skull", new[] { RewardStat.AttackSpeed }, 0.5, new[]
        {
            "Skeleton_Spear", "WereWolf", "Thief", "Clown", "GhostRider", "BombSkul", "RockStar", "Ninja", "Samurai", "Fighter", "StoneMonkey",
            "Prisoner", "KingArthur", "Dominator",
        }),
        ("skull", new[] { RewardStat.MagicAttack }, 0.5, new[]
        {
            "Skeleton_Sword", "Recruit", "Gargoyle", "Mummy", "GlacialSkull", "Alchemist", "Hunter", "DarkPaladin", "HighWarlock", "ArchLich",
            "GrimReaper", "Gambler", "Wizard", "Thor", "Spartan",
        }),
    };

    private static Dictionary<string, AchievementReward> _byId;

    private static Dictionary<string, AchievementReward> ById => _byId ??= Build();

    public static IEnumerable<AchievementReward> All => ById.Values;

    /// <summary>The reward of an achievement (null for SkulInsight's daily ones).</summary>
    public static AchievementReward For(string achievementId) =>
        achievementId != null && ById.TryGetValue(achievementId, out var reward) ? reward : null;

    private static Dictionary<string, AchievementReward> Build()
    {
        var map = new Dictionary<string, AchievementReward>();
        foreach (var info in AchievementCatalog.All)
        {
            AchievementReward reward = null;
            if (info.Kind == AchievementKind.Boss && info.Boss.Length > 0)
                reward = new AchievementReward { Group = "boss", Target = info.Boss, Multiplier = BossMultiplier };
            else if (info.Kind == AchievementKind.NoHit && info.Boss.Length > 0)
                reward = new AchievementReward
                {
                    Group = "nohit", Multiplier = NoHitMultiplier,
                    // The Strongest: the First Hero in the Dark Mirror without getting hit.
                    Target = info.Id == "WorldsBest" ? DarkMirrorPrefix + info.Boss : info.Boss,
                };
            else if (info.DarkMirrorLevel >= 0)
            {
                reward = new AchievementReward { Group = "darkmirror" };
                reward.Stats.Add((RewardStat.PhysicalAttack, 1));
                reward.Stats.Add((RewardStat.MagicAttack, 1));
            }
            else if (info.Id == "HeroSlayer")
                reward = new AchievementReward { Group = "special", Target = Adventurers, Multiplier = 1.05 };
            else if (info.Id == "SkeletonKing")
                reward = new AchievementReward { Group = "special", Target = LittleBone, Multiplier = 1.10 };
            else
                foreach (var (group, stats, percent, ids) in StatGroups)
                    if (Array.IndexOf(ids, info.Id) >= 0)
                    {
                        reward = new AchievementReward { Group = group };
                        foreach (var stat in stats)
                            reward.Stats.Add((stat, percent));
                        break;
                    }
            if (reward == null)
                continue;
            reward.AchievementId = info.Id;
            map[info.Id] = reward;
        }
        return map;
    }

    /// <summary>What the given achievements add up to.</summary>
    public static RewardTotals Sum(IEnumerable<string> achievementIds)
    {
        var totals = new RewardTotals();
        foreach (string id in achievementIds.Distinct())
        {
            if (For(id) is not { } reward)
                continue;
            totals.Count++;
            foreach (var (stat, percent) in reward.Stats)
                totals.Stats[stat] = totals.Stat(stat) + percent;
            if (reward.Target.Length > 0)
                totals.Targets[reward.Target] = totals.Target(reward.Target) * reward.Multiplier;
        }
        return totals;
    }

    // ------------------------------------------------------------------ texts

    public static string GroupName(string group) => group switch
    {
        "boss" => Loc.T("Bosses defeated"),
        "nohit" => Loc.T("Bosses without getting hit"),
        "darkmirror" => Loc.T("Dark Mirror levels"),
        "skull" => Loc.T("Legendary skulls"),
        "danger" => Loc.T("Danger"),
        "shops" => Loc.T("Shops"),
        "world" => Loc.T("The castle and the world"),
        "dark" => Loc.T("Dark abilities and inscriptions"),
        "hidden" => Loc.T("The hidden boss's trail"),
        "special" => Loc.T("Special"),
        _ => group,
    };

    /// <summary>"1", "0.5", "13.3" (invariant, at most one decimal).</summary>
    public static string Number(double percent) =>
        Math.Round(Math.Abs(percent), 1).ToString("0.#", CultureInfo.InvariantCulture);

    public static string StatText(RewardStat stat, double percent)
    {
        string n = Number(percent);
        return stat switch
        {
            RewardStat.PhysicalAttack => Loc.F("Physical attack +{0} %", n),
            RewardStat.MagicAttack => Loc.F("Magic attack +{0} %", n),
            RewardStat.AttackSpeed => Loc.F("Attack speed +{0} %", n),
            RewardStat.MovementSpeed => Loc.F("Movement speed +{0} %", n),
            RewardStat.CooldownSpeed => Loc.F("Cooldown speed +{0} %", n),
            RewardStat.CriticalChance => Loc.F("Critical chance +{0} %", n),
            RewardStat.CriticalDamage => Loc.F("Critical damage +{0} %", n),
            RewardStat.DamageTaken => Loc.F("Damage taken -{0} %", n),
            RewardStat.Healing => Loc.F("Healing received +{0} %", n),
            RewardStat.Gold => Loc.F("Gold gained +{0} %", n),
            _ => stat.ToString(),
        };
    }

    /// <summary>"Damage to Yggdrasil +3 %" (a multiplier shown as its percent).</summary>
    /// <param name="bossName">The name of a boss page ("enemy:Yggdrasil" → "Yggdrasil").</param>
    public static string TargetText(string target, double multiplier, Func<string, string> bossName)
    {
        string n = Number((multiplier - 1.0) * 100.0);
        if (target == Adventurers)
            return Loc.F("Damage to adventurers +{0} %", n);
        if (target == LittleBone)
            return Loc.F("Damage while holding Little Bone +{0} %", n);
        if (target.StartsWith(DarkMirrorPrefix, StringComparison.Ordinal))
            return Loc.F("Damage to {0} in the Dark Mirror +{1} %", bossName(target.Substring(DarkMirrorPrefix.Length)), n);
        return Loc.F("Damage to {0} +{1} %", bossName(target), n);
    }

    /// <summary>The reward in one line: "Physical attack +1 % · Magic attack +1 %".</summary>
    public static string Text(AchievementReward reward, Func<string, string> bossName)
    {
        // Dark Mirror levels raise both attacks: one line instead of two.
        if (reward.Stats.Count == 2 && reward.Stats.All(s => s.Percent == reward.Stats[0].Percent)
            && reward.Stats.Any(s => s.Stat == RewardStat.PhysicalAttack) && reward.Stats.Any(s => s.Stat == RewardStat.MagicAttack))
            return Loc.F("Physical and magic attack +{0} %", Number(reward.Stats[0].Percent));
        var parts = reward.Stats.Select(s => StatText(s.Stat, s.Percent)).ToList();
        if (reward.Target.Length > 0)
            parts.Add(TargetText(reward.Target, reward.Multiplier, bossName));
        return string.Join(" · ", parts);
    }
}
