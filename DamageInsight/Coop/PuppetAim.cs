using System.Collections.Generic;
using Characters;
using Characters.AI;
using Characters.AI.Behaviours.Attacks;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Client: the aim that an enemy's AI behaviour sets up around its attack, done again for the host's moves. A
/// puppet's AI is off (EnemySync), so a behaviour like `CircularProjectileAttack.CRun` (fire mages: the staff and
/// spell axis turned to the target's side, every frame with `_continuousLooking`) or `HorizontalProjectileAttack`
/// never ran on a client: the spell came out of an axis still turned the old way (Umut 2026-11-03: "the fire mage
/// aims a little differently"). The aim operations inside the move (`TakeAim` & co.) read the AI's target, which the
/// host's move event sets, so they need nothing here. `MultiCircularProjectileAttack` aims at a point its AI picks
/// (`lookTarget`), not at the target: not covered (not seen in chapters 1-2 yet).
/// </summary>
internal static class PuppetAim
{
    private static readonly Dictionary<Character, ActionAttack[]> Behaviours = new();
    private static readonly List<(Character enemy, CircularProjectileAttack attack, Character target)> Following = new();

    /// <summary>Before the host's move <paramref name="action"/> is played on a puppet aiming at <paramref name="target"/>.</summary>
    public static void Before(Character enemy, Characters.Actions.Action action, Character target)
    {
        if (enemy == null || action == null || target == null)
            return;
        foreach (var behaviour in BehavioursOf(enemy))
        {
            if (behaviour == null || behaviour.attack != action)
                continue;
            switch (behaviour)
            {
                case CircularProjectileAttack circular:
                    Aim(circular, enemy, target);
                    if (circular._continuousLooking)
                        Following.Add((enemy, circular, target));
                    break;
                case HorizontalProjectileAttack horizontal:
                    Aim(horizontal, enemy, target);
                    break;
            }
        }
    }

    /// <summary>Every frame (client): `_continuousLooking` attacks keep turning to the target while they run.</summary>
    public static void Update()
    {
        for (int i = Following.Count - 1; i >= 0; i--)
        {
            var (enemy, attack, target) = Following[i];
            if (enemy == null || attack == null || target == null || attack.attack == null || !attack.attack.running
                || enemy.health == null || enemy.health.dead)
            {
                Following.RemoveAt(i);
                continue;
            }
            Aim(attack, enemy, target);
        }
    }

    public static void Clear()
    {
        Behaviours.Clear();
        Following.Clear();
    }

    private static ActionAttack[] BehavioursOf(Character enemy)
    {
        if (Behaviours.TryGetValue(enemy, out var found))
            return found;
        var ai = enemy.GetComponentInChildren<AIController>(true);
        if (ai == null)
            ai = enemy.GetComponentInParent<AIController>();
        var list = new List<ActionAttack>();
        if (ai != null)
            foreach (var attack in ai.GetComponentsInChildren<ActionAttack>(true))
                if (attack is CircularProjectileAttack or HorizontalProjectileAttack)
                    list.Add(attack);
        return Behaviours[enemy] = list.ToArray();
    }

    // Same as CircularProjectileAttack.CRun (its TakeAim runs before the attack starts, so it never turns anything).
    private static void Aim(CircularProjectileAttack attack, Character enemy, Character target)
    {
        if (attack._weaponAxisPosition == null || attack._centerAxisPosition == null)
            return;
        Vector3 toTarget = target.transform.position - enemy.transform.position;
        enemy.lookingDirection = toTarget.x > 0f ? Character.LookingDirection.Right : Character.LookingDirection.Left;
        float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
        Vector3 weapon = attack._originalScale, center = attack._originalScale;
        if ((angle > 90f && angle < 270f) || angle < -90f)
        {
            weapon.x *= -1f;
            center.y *= -1f;
            center.x *= -1f;
        }
        attack._weaponAxisPosition.localScale = weapon;
        attack._centerAxisPosition.localScale = center;
    }

    // Same as HorizontalProjectileAttack.CRun.
    private static void Aim(HorizontalProjectileAttack attack, Character enemy, Character target)
    {
        if (attack._weapon == null)
            return;
        if ((target.transform.position - enemy.transform.position).x < 0f)
        {
            enemy.lookingDirection = Character.LookingDirection.Left;
            Vector3 scale = attack._originalScale;
            scale.y *= -1f;
            attack._weapon.localScale = scale * -1f;
        }
        else
        {
            enemy.lookingDirection = Character.LookingDirection.Right;
            attack._weapon.localScale = attack._originalScale;
        }
    }
}
