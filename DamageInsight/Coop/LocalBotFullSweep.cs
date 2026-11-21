using System.Collections.Generic;
using System.Linq;
using Services;
using Singletons;
using UnityEngine;
using GameAction = Characters.Actions.Action;

namespace DamageInsight.Coop;

/// <summary>
/// The full skull sweep (`--skulinsight-bot-skulls=fullsweep:...`, Testing/FullSweepPlan, docs/coop/packages/FS.md):
/// Umut's sweep spec (T.md "Skull sweep"): one game acts at a time. In this game's turns (the session clock's slots,
/// FullSweepPlan) the bot plays its pair through on purpose: per skull the basic attack combo, a jump attack, a dash,
/// every skill of the skull (Weapon.SetSkills through its whole list, each used once), a full gauge (what it sets off,
/// a transformation), then the swap to the other skull; at the end the swap back. Every action gets a trace mark
/// `sweep-act` ("skull|action") for the report (sync-report.py "Full sweep"). In the other game's turns it stands
/// still (invulnerable like every autorun player). After its turn the next pair is equipped (Kit).
/// </summary>
internal sealed partial class LocalBot
{
    private readonly Testing.FullSweepPlan _full;
    private System.Collections.IEnumerator _turn;
    private long _lastTurnSlot = long.MinValue;
    private double _clock, _turnEnd;
    private string _actSkull = "";
    private bool _fullDone;

    private void FullSweep(Characters.Character player, float now)
    {
        var inventory = player.playerComponents?.inventory?.weapon;
        double? clock = CoopRuntime.SessionClock;
        if (inventory == null || clock == null || _playlist == null || _kitState != 2 || _fullDone)
            return; // stands still
        _clock = clock.Value;
        if (_turn == null)
        {
            if (_playlist.Count == 0)
            {
                FinishSweep();
                return;
            }
            long slot = _full.Slot(_clock);
            var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
            if (slot == _lastTurnSlot || !_full.CanBegin(_clock) || Level.Map.Instance == null || chapter == null
                || chapter.type == Level.Chapter.Type.Castle || inventory.current == null)
                return;
            _lastTurnSlot = slot;
            _turnEnd = _full.TurnEnd(slot);
            _actSkull = Loadout.GearName(inventory.current.name);
            CoopTrace.Write("sweep-turn", EnemySync.TraceRoom,
                what: string.Join(",", inventory.weapons.Where(w => w != null).Select(w => Loadout.GearName(w.name))), amount: _playIndex / 2);
            _turn = Turn(player, inventory);
        }
        if (_clock > _turnEnd)
        {
            // the slot is nearly over: the other game's turn begins soon
            Act("cut");
            EndTurn(player, inventory);
            return;
        }
        Approach(player, now);
        bool more;
        try
        {
            more = _turn.MoveNext();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[CoopTest] bot: the full sweep's turn with {_actSkull} failed: {e.GetType().Name}: {e.Message}");
            Act("failed");
            more = false;
        }
        if (!more)
            EndTurn(player, inventory);
    }

    /// <summary>In its turn the bot goes to the nearest enemy, if any (on-hit effects), and faces it; it does not jump
    /// or wander (every move it plays is one of the turn's actions).</summary>
    private void Approach(Characters.Character player, float now)
    {
        if (player.runningMotion != null)
            return;
        var prey = Prey(player, now); // no flying to it (Reach): out of reach, the actions are played where the bot is
        if (prey == null)
            return;
        float dx = prey.transform.position.x - player.transform.position.x;
        if (Mathf.Abs(dx) > 2.5f)
            player.movement.MoveHorizontal(dx > 0 ? Vector2.right : Vector2.left);
        else
            player.ForceToLookAt(dx >= 0 ? Characters.Character.LookingDirection.Right : Characters.Character.LookingDirection.Left);
    }

    private void EndTurn(Characters.Character player, Characters.Player.WeaponInventory inventory)
    {
        _turn = null;
        Act("end");
        if (inventory.polymorphWeapon != null)
            inventory.Unpolymorph();
        player.CancelAction();
        _playIndex += 2;
        if (_playIndex >= _playlist.Count)
        {
            FinishSweep();
            return;
        }
        _kitState = 0; // Kit equips the next pair now (in the other game's turn)
    }

    private void FinishSweep()
    {
        if (_fullDone)
            return;
        _fullDone = true;
        Plugin.Log.LogInfo($"[CoopTest] bot: the full skull sweep is through its list ({_playlist.Count} skulls)");
        CoopTrace.Write("bot-sweep-done", EnemySync.TraceRoom, amount: _playlist.Count);
    }

    /// <summary>A mark for the report: from now on the moves, shots and effects belong to this action.</summary>
    private void Act(string what) => CoopTrace.Write("sweep-act", EnemySync.TraceRoom, what: _actSkull + "|" + what);

    /// <summary>One turn: the pair's first skull, the swap, the second skull, the swap back.</summary>
    private System.Collections.IEnumerator Turn(Characters.Character player, Characters.Player.WeaponInventory inventory)
    {
        var first = inventory.current;
        int count = inventory.weapons.Count(w => w != null);
        for (int n = 0; n < count; n++)
        {
            if (n > 0)
            {
                foreach (var step in Swap(player, inventory))
                    yield return step;
                if (inventory.current == first)
                    yield break; // the swap was refused
            }
            foreach (var step in OneSkull(player, inventory))
                yield return step;
        }
        if (count > 1 && inventory.current != first)
        {
            // back to the first skull: the other skull's swap move is seen too
            foreach (var step in Swap(player, inventory))
                yield return step;
        }
    }

    private IEnumerable<object> Swap(Characters.Character player, Characters.Player.WeaponInventory inventory)
    {
        if (inventory.polymorphWeapon != null)
            inventory.Unpolymorph();
        foreach (var step in Settle(player, 3f, 0.3f))
            yield return step;
        string from = _actSkull;
        // force: the swap's 8 s cooldown would refuse the swap back after a short skull
        if (!inventory.NextWeapon(true))
        {
            Act("swap-refused");
            yield break;
        }
        _actSkull = Loadout.GearName(inventory.current.name);
        Act("swap"); // the swap move played is the skull swapped to's
        CoopTrace.Write("bot-swap", EnemySync.TraceRoom, what: _actSkull, name: from);
        foreach (var step in Settle(player, 3f, 1.5f))
            yield return step;
    }

    /// <summary>Everything one skull can do: attacks, every skill, the gauge.</summary>
    private IEnumerable<object> OneSkull(Characters.Character player, Characters.Player.WeaponInventory inventory)
    {
        var weapon = inventory.current;
        if (weapon == null)
            yield break;
        _actSkull = Loadout.GearName(weapon.name);

        // the basic attack combo: pressed again and again, as a player does
        Act("attack");
        foreach (var step in Press(player, GameAction.Type.BasicAttack, 3.5f, 0.25f))
            yield return step;
        foreach (var step in Settle(player, 2f, 1f))
            yield return step;

        // a jump attack (some skulls have their own), then a dash (some dash with an attack)
        if (Began(player, GameAction.Type.Jump))
        {
            foreach (var step in Hold(0.2f))
                yield return step;
            Act("jump-attack");
            foreach (var step in Press(player, GameAction.Type.JumpAttack, 1.2f, 0.25f))
                yield return step;
            foreach (var step in Settle(player, 2f, 1.2f))
                yield return step;
        }
        if (Began(player, GameAction.Type.Dash))
        {
            Act("dash");
            foreach (var step in Settle(player, 2f, 1.2f))
                yield return step;
        }

        // every skill of the skull: its list in rounds of as many as it holds (Weapon.SetSkills), each used once
        var used = new HashSet<string>();
        if (weapon.skills != null && weapon.currentSkills != null)
        {
            var pool = weapon.skills.Where(k => k != null && !k.hasAlways).ToList();
            int slots = weapon.currentSkills.Count(k => k != null && !k.hasAlways);
            int rounds = slots > 0 && pool.Count > slots ? (pool.Count + slots - 1) / slots : 1;
            for (int round = 0; round < rounds; round++)
            {
                if (inventory.current != weapon || inventory.polymorphWeapon != null)
                    break;
                if (rounds > 1)
                {
                    var keys = new string[slots];
                    for (int i = 0; i < slots; i++)
                        keys[i] = pool[(round * slots + i) % pool.Count].key;
                    weapon.UnapplyAllSkillChanges();
                    weapon.SetSkills(keys, false);
                    weapon.ApplyAllSkillChanges();
                    foreach (var step in Hold(0.3f))
                        yield return step;
                }
                foreach (var skill in weapon.currentSkills.Where(k => k != null).ToList())
                {
                    if (!used.Add(skill.key))
                        continue;
                    bool started = false;
                    float until = Time.unscaledTime + 3f;
                    while (!started && Time.unscaledTime < until)
                    {
                        if (skill.action != null && skill.action.gameObject.activeInHierarchy && player.runningMotion == null
                            && skill.action.TryStart())
                            started = true;
                        else
                            yield return null;
                    }
                    Act((started ? "skill:" : "skill-refused:") + skill.key);
                    if (started)
                    {
                        CoopTrace.Write("bot-skill", EnemySync.TraceRoom, name: _actSkull, what: skill.key);
                        foreach (var step in Settle(player, 5f, 1.5f))
                            yield return step;
                    }
                }
            }
        }

        // a full gauge, and what it sets off (a transformation, a stronger attack or skill)
        if (inventory.current == weapon && weapon.gauge is Characters.Gear.Weapons.Gauges.ValueGauge gauge && !gauge.isMax())
        {
            gauge.FillUp();
            Act("gauge");
            CoopTrace.Write("bot-gauge", EnemySync.TraceRoom, what: _actSkull);
            bool transformed = false;
            float until = Time.unscaledTime + 6f;
            int press = 0;
            while (Time.unscaledTime < until || (inventory.polymorphWeapon != null && Time.unscaledTime < until + 8f))
            {
                if (!transformed && inventory.polymorphWeapon != null)
                {
                    transformed = true;
                    Act("transform");
                }
                Try(player, press++ % 3 == 2 ? GameAction.Type.Skill : GameAction.Type.BasicAttack);
                foreach (var step in Hold(0.4f))
                    yield return step;
            }
            if (inventory.polymorphWeapon != null)
                inventory.Unpolymorph();
            foreach (var step in Settle(player, 3f, 1f))
                yield return step;
        }
    }

    /// <summary>Presses one kind of action every <paramref name="every"/> seconds for <paramref name="seconds"/>.</summary>
    private static IEnumerable<object> Press(Characters.Character player, GameAction.Type type, float seconds, float every)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
        {
            Try(player, type);
            float next = Time.unscaledTime + every;
            while (Time.unscaledTime < next)
                yield return null;
        }
    }

    /// <summary>Waits until the running move is over (at most <paramref name="most"/> s), then <paramref name="gap"/> s
    /// more (so the other game's census lines of this action are not mixed with the next one's).</summary>
    private static IEnumerable<object> Settle(Characters.Character player, float most, float gap)
    {
        float until = Time.unscaledTime + most;
        while (player.runningMotion != null && Time.unscaledTime < until)
            yield return null;
        foreach (var step in Hold(gap))
            yield return step;
    }

    private static IEnumerable<object> Hold(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
            yield return null;
    }

    /// <summary>Like <see cref="Try"/>, and says whether a move began.</summary>
    private static bool Began(Characters.Character player, GameAction.Type type)
    {
        foreach (var action in player.actions)
        {
            if (action != null && action.type == type && action.gameObject.activeInHierarchy && action.TryStart())
                return true;
        }
        return false;
    }
}
