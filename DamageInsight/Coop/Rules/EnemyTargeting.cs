using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Rules;

/// <summary>What an enemy knows about one player this frame (the game side fills it per enemy).</summary>
public struct TargetCandidate
{
    public int Slot;
    /// <summary>Connected and not down. A player in a menu or loading stays targetable (§4.1: no hiding in menus).</summary>
    public bool Targetable;
    /// <summary>Invisible (an effect): can't be picked or kept by normal enemies. Bosses ignore it.</summary>
    public bool Hidden;
    /// <summary>Inside this enemy's sight (the game's detection range): needed to notice a new player.</summary>
    public bool Noticed;
    /// <summary>The enemy can still reach them (its chase range); out of reach drops the target at once.</summary>
    public bool InReach;
    /// <summary>World units from the enemy.</summary>
    public float Distance;

    public TargetCandidate(int slot, bool targetable, float distance, bool noticed = true, bool inReach = true,
        bool hidden = false)
    {
        Slot = slot;
        Targetable = targetable;
        Distance = distance;
        Noticed = noticed;
        InReach = inReach;
        Hidden = hidden;
    }

    internal bool CanKeep => Targetable && !Hidden && InReach;
    internal bool CanNotice => CanKeep && Noticed;

    internal static int Find(IReadOnlyList<TargetCandidate> players, int slot)
    {
        if (slot < 0)
            return -1;
        for (int i = 0; i < players.Count; i++)
            if (players[i].Slot == slot)
                return i;
        return -1;
    }
}

/// <summary>
/// The target of one normal enemy or elite (§4.1): "like the game: the closest player it notices, then whoever hits
/// it. To stop ping-pong, a target lock of 2 s after each switch (3 s for elites and mini-bosses); a new attacker
/// takes over only after the lock, or at once if the current target is out of reach, invisible or down."
/// [DECISION] The newest attacker other than the current target waits for the lock's end; the target's own hits
/// don't cancel it (two players both hitting share the enemy, one lock at a time). With no target (or a lost one),
/// the newest valid attacker comes first, else the closest noticed player. Umut 2026-11-07 ("hit or much closer"):
/// after the lock, a noticed player much closer than the target (<see cref="TargetingSettings.CloserRatio"/>) takes
/// over too (an alchemist kept throwing at the host only). Runs on the host per enemy; no allocation.
/// </summary>
public sealed class EnemyTargeting
{
    private readonly TargetingSettings _settings;
    private int _attacker = -1;

    public EnemyTargeting(EnemyCategory category, TargetingSettings settings)
    {
        Category = category;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Clamp();
    }

    public EnemyCategory Category { get; }

    /// <summary>The slot it attacks, -1 = none.</summary>
    public int Target { get; private set; } = -1;

    public double LockedUntil { get; private set; } = double.MinValue;

    /// <summary>How often the target changed (for tests and the debug overlay).</summary>
    public int Switches { get; private set; }

    /// <summary>A player's hit landed on this enemy.</summary>
    public void OnDamagedBy(int slot, double now)
    {
        if (PartyState.IsSlot(slot) && slot != Target)
            _attacker = slot;
    }

    /// <summary>Re-checks the target with this frame's view of the players; returns it (-1 = none).</summary>
    public int Update(double now, IReadOnlyList<TargetCandidate> players)
    {
        int current = TargetCandidate.Find(players, Target);
        if (Target >= 0 && (current < 0 || !players[current].CanKeep))
            Target = -1; // down, gone, out of reach or invisible: let go at once

        int attacker = TargetCandidate.Find(players, _attacker);
        if (attacker >= 0 && !players[attacker].CanKeep)
        {
            _attacker = -1;
            attacker = -1;
        }

        if (Target >= 0)
        {
            if (now < LockedUntil)
                return Target;
            if (attacker >= 0)
            {
                SwitchTo(_attacker, now);
                return Target;
            }
            float ratio = _settings.CloserRatio;
            if (ratio > 0f)
            {
                float nearest = players[current].Distance * ratio;
                int closer = -1;
                for (int i = 0; i < players.Count; i++)
                {
                    if (i == current || !players[i].CanNotice || players[i].Distance >= nearest)
                        continue;
                    nearest = players[i].Distance;
                    closer = players[i].Slot;
                }
                if (closer >= 0)
                    SwitchTo(closer, now);
            }
            return Target;
        }

        if (attacker >= 0)
        {
            SwitchTo(_attacker, now);
            return Target;
        }

        int closest = -1;
        float best = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].CanNotice || players[i].Distance >= best)
                continue;
            best = players[i].Distance;
            closest = players[i].Slot;
        }
        if (closest >= 0)
            SwitchTo(closest, now);
        return Target;
    }

    public void Reset()
    {
        Target = _attacker = -1;
        LockedUntil = double.MinValue;
    }

    private void SwitchTo(int slot, double now)
    {
        Target = slot;
        _attacker = -1;
        LockedUntil = now + _settings.LockSeconds(Category);
        Switches++;
    }
}
