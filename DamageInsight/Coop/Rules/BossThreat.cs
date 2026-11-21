using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Rules;

/// <summary>
/// A boss's threat table (§4.1): "Each player has threat: damage dealt to the boss in the last 8 s, plus a bonus for
/// being close, plus a 'turn' bonus that grows while the boss is chasing someone else. The boss picks its target when
/// a pattern starts and never switches in the middle of an attack. With two players this spreads the pressure: nobody
/// is chased the whole fight, and the one dealing the most damage is chased a bit more." The formula and numbers:
/// <see cref="BossThreatSettings"/>. [DECISION] Damage counts as each player's share of the party's recent damage, so
/// the weights mean the same in chapter 1 and with a late-run build. [DECISION] Bosses ignore invisibility (they find
/// the player in the game too); only down or gone players are never picked. [DECISION] The turn bonus only grows while
/// a player is up: someone just revived isn't chased at once. Ties: the closer player, then the lower slot.
/// Runs on the host, one per boss (or per boss part that picks on its own); damage is kept in 16 time buckets per
/// player, so nothing is allocated during the fight.
/// </summary>
public sealed class BossThreat
{
    private const int Buckets = 16;

    private readonly BossThreatSettings _settings;
    private readonly long[,] _bucketIndex = new long[PartyState.MaxPlayers, Buckets];
    private readonly float[,] _bucketDamage = new float[PartyState.MaxPlayers, Buckets];
    private readonly double[] _waitingSince = new double[PartyState.MaxPlayers];
    private readonly bool[] _wasTargetable = new bool[PartyState.MaxPlayers];
    private readonly int[] _picked = new int[PartyState.MaxPlayers];
    private readonly double _bucketSeconds;

    public BossThreat(BossThreatSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Clamp();
        _bucketSeconds = _settings.WindowSeconds / Buckets;
        for (int p = 0; p < PartyState.MaxPlayers; p++)
            for (int b = 0; b < Buckets; b++)
                _bucketIndex[p, b] = long.MinValue;
    }

    /// <summary>The slot the current pattern is aimed at, -1 = none. Changes only in <see cref="OnPatternStart"/>.</summary>
    public int Target { get; private set; } = -1;

    public int Patterns { get; private set; }

    /// <summary>How many patterns were aimed at this player (for tests and the debug overlay).</summary>
    public int TimesPicked(int slot) => PartyState.IsSlot(slot) ? _picked[slot] : 0;

    /// <summary>A player's hit on the boss (any of its parts).</summary>
    public void OnDamage(int slot, float damage, double now)
    {
        if (!PartyState.IsSlot(slot) || !(damage > 0))
            return;
        long index = (long)Math.Floor(now / _bucketSeconds);
        int b = (int)(((index % Buckets) + Buckets) % Buckets);
        if (_bucketIndex[slot, b] != index)
        {
            _bucketIndex[slot, b] = index;
            _bucketDamage[slot, b] = 0;
        }
        _bucketDamage[slot, b] += damage;
    }

    /// <summary>The player's recent damage, older hits fading out linearly over the window.</summary>
    public double RecentDamage(int slot, double now)
    {
        if (!PartyState.IsSlot(slot))
            return 0;
        double sum = 0, window = _settings.WindowSeconds;
        for (int b = 0; b < Buckets; b++)
        {
            long index = _bucketIndex[slot, b];
            if (index == long.MinValue)
                continue;
            double age = now - (index + 0.5) * _bucketSeconds;
            if (age < 0)
                age = 0;
            if (age >= window)
                continue;
            sum += _bucketDamage[slot, b] * (1 - age / window);
        }
        return sum;
    }

    /// <summary>The boss starts a new pattern: picks (or keeps) its target for the whole pattern and returns it.</summary>
    public int OnPatternStart(double now, IReadOnlyList<TargetCandidate> players)
    {
        double total = 0;
        for (int i = 0; i < players.Count; i++)
        {
            var c = players[i];
            if (!PartyState.IsSlot(c.Slot))
                continue;
            if (!c.Targetable)
            {
                _wasTargetable[c.Slot] = false;
                continue;
            }
            if (!_wasTargetable[c.Slot])
            {
                _wasTargetable[c.Slot] = true;
                _waitingSince[c.Slot] = now; // up again (or new): the turn bonus starts from 0
            }
            total += RecentDamage(c.Slot, now);
        }

        int best = -1;
        double bestThreat = double.MinValue;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            var c = players[i];
            if (!c.Targetable || !PartyState.IsSlot(c.Slot))
                continue;
            double threat = Threat(c, now, total);
            // Within 1e-6 counts as a tie (the settings are floats; rounding noise shouldn't pick the target).
            bool better = threat > bestThreat + 1e-6 ||
                          (Math.Abs(threat - bestThreat) <= 1e-6 &&
                           (c.Distance < bestDistance || (c.Distance == bestDistance && c.Slot < best)));
            if (!better)
                continue;
            best = c.Slot;
            bestThreat = threat;
            bestDistance = c.Distance;
        }

        if (best != Target)
        {
            if (Target >= 0)
                _waitingSince[Target] = now; // the old target now waits for its turn
            Target = best;
        }
        if (best >= 0)
        {
            _picked[best]++;
            Patterns++;
        }
        return best;
    }

    /// <summary>This player's threat right now (the debug overlay shows it). total = the party's recent damage.</summary>
    public double Threat(TargetCandidate c, double now, double total)
    {
        double share = total > 0 ? RecentDamage(c.Slot, now) / total : 0;
        double threat = _settings.DamageWeight * share;
        double close = 1 - c.Distance / _settings.CloseDistance;
        if (close > 0)
            threat += _settings.ClosenessBonus * Math.Min(1, close);
        if (c.Slot == Target)
            threat += _settings.CurrentTargetBonus;
        else
            threat += Math.Min(_settings.TurnBonusMax, _settings.TurnBonusPerSecond * Math.Max(0, now - _waitingSince[c.Slot]));
        return threat;
    }
}
