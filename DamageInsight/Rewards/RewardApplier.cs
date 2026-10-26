using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Data;
using DamageInsight.Achievements;
using DamageInsight.Codex;
using DamageInsight.Daily;
using UnityEngine;

namespace DamageInsight.Rewards;

/// <summary>
/// Puts the slot's achievement rewards on the player (0.15): one set of stat values attached to the player's stats
/// (PercentPoint like the Witch's bonuses; damage taken as the game's Product kind), the gold multiplier, and a
/// damage handler for the per-target rewards (a boss, adventurers, holding Little Bone). Follows the settings and
/// the slot's achievements live; nothing in daily runs or while the QA capture plays.
/// </summary>
public static class RewardApplier
{
    /// <summary>Rewards are on for the slot being played (settings, not the daily slot, not the QA capture).</summary>
    public static bool On => Plugin.RewardsEnabled?.Value == true && !DailyRun.InDailySlot && !CodexTracker.Paused;

    public static bool AchievementsOn => On && Plugin.RewardsAchievements?.Value == true;

    /// <summary>The handler's owner, so the combat log's calculation names it ("Achievement rewards").</summary>
    public sealed class DamageBonus
    {
        internal bool OnGiveDamage(ITarget target, ref Damage damage)
        {
            try
            {
                if (AchievementsOn && target?.character is { } character)
                {
                    double multiplier = MultiplierAgainst(character);
                    if (multiplier != 1.0)
                        damage.percentMultiplier *= multiplier;
                }
            }
            catch (Exception)
            {
                // never in the way of the game's damage
            }
            return false;
        }
    }

    private static readonly DamageBonus Bonus = new();
    private static readonly object GoldKey = new();
    private static Character _player;
    private static Stat.Values _values;
    private static bool _goldOn;
    private static int _totalsVersion = -1;
    private static RewardTotals _totals = new();
    private static string _valuesKey = "";
    private static float _nextTick;
    private static readonly Dictionary<int, string> TargetKeys = new();
    private static int _skullFrame = -1;
    private static bool _littleBone;

    /// <summary>What the slot's earned achievements add up to.</summary>
    public static RewardTotals Totals
    {
        get
        {
            if (_totalsVersion != AchievementStore.Version)
            {
                _totalsVersion = AchievementStore.Version;
                _totals = RewardCatalog.Sum(AchievementStore.All.Select(e => e.Id));
            }
            return _totals;
        }
    }

    public static void Tick(Character player)
    {
        if (player != _player)
        {
            Detach();
            if (_player != null)
                Safe(() => _player.onGiveDamage.Remove(Bonus.OnGiveDamage));
            _player = player;
            TargetKeys.Clear();
            if (player != null)
                player.onGiveDamage.Add(0, Bonus.OnGiveDamage);
        }
        if (player == null || Time.unscaledTime < _nextTick)
            return;
        _nextTick = Time.unscaledTime + 0.5f;
        if (!AchievementsOn)
        {
            Detach();
            return;
        }
        var totals = Totals;
        string key = string.Join(",", totals.Stats.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
        if (_values != null && key != _valuesKey)
            Detach();
        if (_values == null)
        {
            _values = ValuesOf(totals);
            _valuesKey = key;
        }
        if (_values.values.Length > 0 && !player.stat.Contains(_values))
        {
            player.stat.AttachValues(_values);
            player.stat.SetNeedUpdate();
        }
        double gold = totals.Stat(RewardStat.Gold) / 100.0;
        if (gold > 0)
        {
            GameData.Currency.gold.multiplier.AddOrUpdate(GoldKey, gold);
            _goldOn = true;
        }
        else if (_goldOn)
        {
            GameData.Currency.gold.multiplier.Remove(GoldKey);
            _goldOn = false;
        }
    }

    private static void Detach()
    {
        if (_values != null && _player != null)
            Safe(() =>
            {
                if (_player.stat.Contains(_values))
                {
                    _player.stat.DetachValues(_values);
                    _player.stat.SetNeedUpdate();
                }
            });
        _values = null;
        if (_goldOn)
        {
            Safe(() => GameData.Currency.gold.multiplier.Remove(GoldKey));
            _goldOn = false;
        }
    }

    private static Stat.Values ValuesOf(RewardTotals totals)
    {
        var list = new List<Stat.Value>();
        void Point(RewardStat stat, Stat.Kind kind)
        {
            double percent = totals.Stat(stat);
            if (percent != 0)
                list.Add(new Stat.Value(Stat.Category.PercentPoint, kind, percent / 100.0));
        }
        Point(RewardStat.PhysicalAttack, Stat.Kind.PhysicalAttackDamage);
        Point(RewardStat.MagicAttack, Stat.Kind.MagicAttackDamage);
        Point(RewardStat.AttackSpeed, Stat.Kind.AttackSpeed);
        Point(RewardStat.MovementSpeed, Stat.Kind.MovementSpeed);
        Point(RewardStat.CooldownSpeed, Stat.Kind.CooldownSpeed);
        Point(RewardStat.CriticalChance, Stat.Kind.CriticalChance);
        Point(RewardStat.CriticalDamage, Stat.Kind.CriticalDamage);
        Point(RewardStat.Healing, Stat.Kind.TakingHealAmount);
        // Damage taken is a product of Percent values (0.97 = 3 % less).
        double taken = totals.Stat(RewardStat.DamageTaken);
        if (taken != 0)
            list.Add(new Stat.Value(Stat.Category.Percent, Stat.Kind.TakingDamage, Math.Max(0.5, 1.0 + taken / 100.0)));
        return new Stat.Values(list.ToArray());
    }

    /// <summary>The damage multiplier of the per-target rewards against this character (1 for none).</summary>
    public static double MultiplierAgainst(Character target)
    {
        var totals = Totals;
        if (totals.Targets.Count == 0)
            return 1.0;
        double multiplier = 1.0;
        string page = TargetKey(target);
        if (page.Length > 0)
        {
            multiplier *= totals.Target(page);
            if (CodexMode.DarkMirrorNow)
                multiplier *= totals.Target(RewardCatalog.DarkMirrorPrefix + page);
        }
        if (target.type == Character.Type.Adventurer)
            multiplier *= totals.Target(RewardCatalog.Adventurers);
        if (HoldingLittleBone())
            multiplier *= totals.Target(RewardCatalog.LittleBone);
        return multiplier;
    }

    /// <summary>A boss's first page ("enemy:Yggdrasil" for all of its phases), "" for everyone else.</summary>
    private static string TargetKey(Character target)
    {
        int id = target.GetInstanceID();
        if (TargetKeys.TryGetValue(id, out string key))
            return key;
        key = "";
        if (CodexTracker.EntryOf(target) is { } entry)
            key = CodexCatalog.BossPageOf(entry.id);
        if (TargetKeys.Count > 500)
            TargetKeys.Clear();
        TargetKeys[id] = key;
        return key;
    }

    private static bool HoldingLittleBone()
    {
        if (_skullFrame == Time.frameCount)
            return _littleBone;
        _skullFrame = Time.frameCount;
        var current = _player?.playerComponents?.inventory?.weapon?.current;
        _littleBone = current != null && current.name.Replace("(Clone)", "").Trim() == "Skul";
        return _littleBone;
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // a destroyed player or stat: nothing left to take back
        }
    }
}
