using System;
using System.Collections.Generic;
using Characters;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using Level;

namespace DamageInsight.Coop;

/// <summary>
/// Enemy health by party size (COOP_PLAN 4.7: "Per-player rewards make each player about as strong as alone. Two
/// players deal almost twice the damage, and enemies split their attention. So enemies need much more health").
/// Every game scales every enemy it binds (EnemySync: a room's waves, the map's bosses, summons) by the same
/// multiplier, as a Percent value on the enemy's Health stat: the game keeps the health share when the maximum
/// changes, the health bars read right on every game, and the host's health numbers (which clients follow) fit the
/// clients' maximum. Normal enemies and enemies' summons, elites (Named, Adventurer) and bosses have their own
/// column (Rules.BalanceSettings). The numbers are the host's (setting Coop/Balance; CoopHostRules sends them to
/// every client when it joins and when the host changes them). The party size is the number of players when the
/// enemy is bound; it isn't changed when someone leaves mid-room (Rules.CoopBalance). One player: x 1.0, nothing
/// happens. Enemy damage stays as it is.
/// </summary>
// REVIEW(fable): a gameplay change on every enemy of a co-op run (REVIEW_QUEUE entry 3, COOP_LOG 57f). Check the categories (Summoned = normal?) and that a boss's health steps / phases still work.
internal sealed class CoopHealthScaling
{
    public static CoopHealthScaling Current { get; private set; }

    private readonly CoopSession _session;
    private readonly BalanceSettings _settings;
    // Static, per map: a rejoin in the same room makes a new instance (package DC), and the room's enemies, already
    // scaled, got the multiplier a second time (1.6 x 1.6 on the client's health bars; COOP_LOG 60).
    private static readonly HashSet<Character> _scaled = new();
    // what was attached to each enemy (by category), so a changed number (Coop/EnemyHealth's slider) swaps it live
    private static readonly Dictionary<Character, (Stat.Values values, EnemyCategory category)> _attached = new();
    private static Map _map;
    private int _count;
    private float _last;

    public CoopHealthScaling(CoopSession session, BalanceSettings settings)
    {
        _session = session;
        _settings = settings;
        Current = this;
    }

    public void Dispose()
    {
        if (Current == this)
            Current = null;
    }

    /// <summary>An enemy this game just bound: its maximum health x the party's multiplier (once per enemy).</summary>
    public void Scale(Character enemy)
    {
        if (enemy == null || enemy.health == null || enemy.stat == null)
            return;
        if (Map.Instance != _map)
        {
            if (_count > 0)
                CoopTrace.Write("balance", amount: _last, what: _count + " enemies");
            _map = Map.Instance;
            _scaled.Clear();
            _attached.Clear();
            _count = 0;
        }
        EnemyCategory? category = enemy.type switch
        {
            Character.Type.TrashMob or Character.Type.Summoned => EnemyCategory.Normal,
            Character.Type.Named or Character.Type.Adventurer => EnemyCategory.Elite,
            Character.Type.Boss => EnemyCategory.Boss,
            _ => null, // traps, dummies, players and their minions
        };
        if (category == null || !_scaled.Add(enemy))
            return;
        float multiplier = _settings.Get(_session.Roster.Count, category.Value);
        _count++;
        _last = multiplier;
        if (Math.Abs(multiplier - 1f) < 0.001f)
            return;
        Attach(enemy, multiplier, category.Value);
    }

    private static void Attach(Character enemy, float multiplier, EnemyCategory category)
    {
        // Character.Update sees the changed stat and sets the new maximum with the same health share
        var values = new Stat.Values(new Stat.Value(Stat.Category.Percent, Stat.Kind.Health, multiplier));
        enemy.stat.AttachValues(values);
        enemy.stat.SetNeedUpdate();
        _attached[enemy] = (values, category);
    }

    /// <summary>The numbers changed (the host moved the slider; on a client: the host's new numbers came): every enemy
    /// scaled on this map gets the new multiplier at once, keeping its health share. The party size is today's.</summary>
    public void Rescale()
    {
        if (_attached.Count == 0 && _scaled.Count == 0)
            return;
        int changed = 0;
        float last = 1f;
        foreach (var enemy in new List<Character>(_scaled))
        {
            if (enemy == null || enemy.stat == null || enemy.health == null || enemy.health.dead)
                continue;
            EnemyCategory? category = enemy.type switch
            {
                Character.Type.TrashMob or Character.Type.Summoned => EnemyCategory.Normal,
                Character.Type.Named or Character.Type.Adventurer => EnemyCategory.Elite,
                Character.Type.Boss => EnemyCategory.Boss,
                _ => null,
            };
            if (category == null)
                continue;
            float multiplier = _settings.Get(_session.Roster.Count, category.Value);
            if (_attached.TryGetValue(enemy, out var had))
            {
                enemy.stat.DetachValues(had.values);
                _attached.Remove(enemy);
            }
            if (Math.Abs(multiplier - 1f) >= 0.001f)
                Attach(enemy, multiplier, category.Value);
            else
                enemy.stat.SetNeedUpdate();
            changed++;
            last = multiplier;
        }
        CoopTrace.Write("balance-live", amount: last, what: changed + " enemies");
        Plugin.Log.LogInfo($"[Coop] enemy health now x{last:0.###} (party of {_session.Roster.Count}): {changed} enemies changed");
    }
}
