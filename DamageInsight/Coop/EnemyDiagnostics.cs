using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// "Log even more" (Umut 2026-11-20): what a player hits that the co-op doesn't know. A hit of this game's player on
/// an enemy-side character without a net id stays on this game only (the host never hears of it): Umut's chapter 4
/// shrines ("golden pumpkins that shine a light on the unit they protect": broken on the client, still on the host)
/// were not in any trace, so the next test names them: trace `hit-unbound` (name, type; 3 per name and room) and,
/// once per name, the parts of the object (its components up to the map) in the BepInEx log.
/// </summary>
internal sealed partial class EnemySync
{
    private readonly Dictionary<(uint, string), int> _unboundHits = new();
    private static readonly HashSet<string> UnboundDescribed = new();

    private void NoteUnboundHit(Character target, in Damage damage, double dealt)
    {
        if (_map == null || target == null || _ids.ContainsKey(target) || CoopCopies.IsCopy(target) || CoopCopies.IsPlayerSide(target))
            return;
        var attacker = damage.attacker.character;
        if (attacker == null || CoopCopies.BelongsToCopy(attacker) || !CoopCopies.IsPlayerSide(attacker))
            return;
        string name = CoopTrace.NameOf(target);
        var key = (_room, name);
        _unboundHits.TryGetValue(key, out int n);
        _unboundHits[key] = ++n;
        if (_unboundHits.Count > 500)
            _unboundHits.Clear();
        if (n <= 3)
            CoopTrace.Write("hit-unbound", _room, name: name, what: target.type.ToString(), amount: dealt,
                after: target.health != null ? target.health.currentHealth : double.NaN);
        if (UnboundDescribed.Count < 200 && UnboundDescribed.Add(name))
            Plugin.Log.LogWarning($"[Coop] this player hit something the host doesn't share: {name} ({target.type}); its parts: {Describe(target)}");
    }

    /// <summary>The object's components and those of its parents up to the map (type names), for the log.</summary>
    private static string Describe(Character target)
    {
        var parts = new List<string>();
        for (var t = target.transform; t != null; t = t.parent)
        {
            if (t.GetComponent<Level.Map>() != null)
            {
                parts.Add("[map " + t.name + "]");
                break;
            }
            var types = t.GetComponents<MonoBehaviour>().Where(c => c != null).Select(c => c.GetType().Name).Distinct();
            parts.Add(t.name + ": " + string.Join(",", types));
            if (parts.Count >= 6)
                break;
        }
        return string.Join(" < ", parts);
    }

    [HarmonyPatch(typeof(Health), nameof(Health.TakeDamage),
        new[] { typeof(Damage), typeof(double) }, new[] { ArgumentType.Ref, ArgumentType.Out })]
    public static class UnboundHits
    {
        private static void Postfix(Health __instance, bool __result, ref Damage damage, ref double dealtDamage)
        {
            if (__result || Current == null || !CoopTrace.On)
                return;
            try
            {
                Current.NoteUnboundHit(__instance.owner, damage, dealtDamage);
            }
            catch (Exception)
            {
                // a diagnostic only
            }
        }
    }
}
