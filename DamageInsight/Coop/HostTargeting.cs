using System;
using System.Collections.Generic;
using Characters;
using DamageInsight.Coop.Rules;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Host: who each normal enemy or elite goes for, by the co-op rule (package D's <see cref="EnemyTargeting"/>,
/// Umut 2026-11-07 "hit or much closer"): the game's own sight picks the first target; after that the enemy switches
/// to another player who hits it, or who is less than half as far away, never sooner than 2 s (elites 3 s) after the
/// last switch; a target that is down, gone or out of reach is dropped at once. Before, an enemy kept the first player
/// it saw until the other hit it (an alchemist threw potions at the host only). Bosses keep their own targeting (the
/// threat table comes later). The target goes to clients with every move as before.
/// </summary>
internal sealed class HostTargeting
{
    /// <summary>A player this near is noticed (can be switched to without hitting); farther, only the current target.</summary>
    public const float NoticeRange = 12f;
    /// <summary>Farther than this the target is lost (another room part).</summary>
    public const float ReachRange = 40f;
    private const double Interval = 0.25;

    private readonly TargetingSettings _settings = new();
    private readonly Dictionary<Character, EnemyTargeting> _rules = new();
    private readonly List<TargetCandidate> _candidates = new(4);
    private readonly Func<int, Character> _playerOf;
    private readonly Func<Character, int> _slotOf;
    private double _next;

    public HostTargeting(Func<int, Character> playerOf, Func<Character, int> slotOf)
    {
        _playerOf = playerOf;
        _slotOf = slotOf;
    }

    public void Clear()
    {
        _rules.Clear();
        _next = 0;
    }

    /// <summary>An enemy took damage: the attacker's slot counts at once (the rule may switch now or after the lock).</summary>
    public void OnDamaged(Character enemy, Character attacker, double now)
    {
        var rule = RuleOf(enemy);
        if (rule == null || attacker == null)
            return;
        int slot = _slotOf(attacker);
        if (slot < 0)
        {
            var leader = attacker.GetComponent<Characters.Minion>()?.leader?.player;
            slot = leader != null ? _slotOf(leader) : -1;
        }
        if (slot < 0)
            return;
        rule.OnDamagedBy(slot, now);
        Apply(enemy, rule, now);
    }

    /// <summary>Every <see cref="Interval"/>; true when it ran this time.</summary>
    /// <param name="again">A second list right after one that ran (summons after the room's enemies).</param>
    public bool Tick(IReadOnlyList<Character> enemies, double now, bool again = false)
    {
        if (!again)
        {
            if (now < _next)
                return false;
            _next = now + Interval;
        }
        for (int i = 0; i < enemies.Count; i++)
        {
            var rule = RuleOf(enemies[i]);
            if (rule != null)
                Apply(enemies[i], rule, now);
        }
        return true;
    }

    private EnemyTargeting RuleOf(Character enemy)
    {
        if (enemy == null || enemy.type is Character.Type.Boss or Character.Type.Trap or Character.Type.Dummy
                or Character.Type.Player or Character.Type.PlayerMinion)
            return null;
        if (!_rules.TryGetValue(enemy, out var rule))
        {
            var category = enemy.type is Character.Type.Named or Character.Type.Adventurer ? EnemyCategory.Elite : EnemyCategory.Normal;
            _rules[enemy] = rule = new EnemyTargeting(category, _settings);
        }
        return rule;
    }

    private void Apply(Character enemy, EnemyTargeting rule, double now)
    {
        if (enemy.health == null || enemy.health.dead || !enemy.gameObject.activeInHierarchy)
            return;
        var current = CoopTargeting.TargetOf(enemy);
        if (current == null && rule.Target < 0)
            return; // nobody noticed yet: the game's own sight picks the first target
        Vector2 at = enemy.transform.position;
        _candidates.Clear();
        for (int slot = 0; slot < 4; slot++)
        {
            var player = _playerOf(slot);
            if (player == null)
                continue;
            float distance = Vector2.Distance(at, player.transform.position);
            bool targetable = player.health != null && !player.health.dead && !CoopTargeting.IsDown(player);
            _candidates.Add(new TargetCandidate(slot, targetable, distance,
                noticed: distance <= NoticeRange || player == current,
                inReach: distance <= ReachRange,
                hidden: player.stealth != null && player.stealth.value));
        }
        int want = rule.Update(now, _candidates);
        if (want < 0)
            return; // keep whatever the game has
        var target = _playerOf(want);
        if (target == null || target == current)
            return;
        CoopTargeting.SetTarget(enemy, target);
        CoopTrace.Write("retarget", EnemySync.TraceRoom, EnemySync.TraceIdOf(enemy), CoopTrace.NameOf(enemy),
            current != null ? "from slot " + _slotOf(current) : "first", slot: want);
    }
}
