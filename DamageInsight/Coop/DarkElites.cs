using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Abilities.Darks;
using HarmonyLib;
using Level;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Dark Mirror's dark elites in co-op (COOP_LOG 61 (1), 62). A dark elite is elected when its wave is made
/// (<c>DarkEnemySelector.ElectIn</c>): it gets a <c>DarkAbilityAttacher</c>, turns Named and stays "cinematic" (nobody
/// can target or hit it) until its intro, which its own behaviour tree starts (<c>IntroDarkEnemy</c> ->
/// <c>DarkEnemy.RunIntro</c>): the dark look, the boss-style health bar, and the abilities, rolled at that moment from
/// a random seeded with a counter that every dark enemy since the game started adds to (so two games roll different
/// ones). On a client a puppet's tree never runs, so its elite stayed cinematic for good: no health bar, no dark
/// ability, unhittable, while its replayed attacks still hit (UBDuck's test, Cannon Specialist + Conductor).
/// Now the host decides: clients elect nothing themselves; when the host's elite gets its abilities, the host names
/// them (<see cref="EnemyEventKind.DarkElite"/>), and the client makes its puppet the same elite with the same
/// abilities and runs its intro, in step with the host's moves.
/// </summary>
internal sealed partial class EnemySync
{
    /// <summary>Client: the host's abilities for an elite whose attacher was just made here, by its ability container.</summary>
    private static readonly Dictionary<DarkAbilityContainer, string[]> HostAbilities = new();

    /// <summary>Client: how long an elite event waits for its puppet to be shown (inactive: its wave isn't out yet).</summary>
    public const double DarkEliteWait = 3.0;
    private readonly Dictionary<EnemyEvent, double> _darkEliteWaits = new();

    /// <summary>Host: an enemy's dark abilities were just attached: every client makes the same enemy the same elite.</summary>
    private void OnHostDarkElite(Character enemy, IEnumerable<DarkAbility> abilities)
    {
        if (!IsHost || _map == null || enemy == null)
            return;
        if (!_ids.TryGetValue(enemy, out var id))
        {
            CoopTrace.Write("dark-elite-unbound", _room, name: CoopTrace.NameOf(enemy));
            return;
        }
        string names = string.Join("|", (abilities ?? Enumerable.Empty<DarkAbility>()).Where(a => a != null).Select(a => a.name));
        int level = Singletons.Singleton<Hardmode.HardmodeManager>.Instance?.currentLevel ?? 0;
        CoopTrace.Write("dark-elite", _room, id, CoopTrace.NameOf(enemy), names, amount: level);
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.DarkElite, NetId = id, ActionPath = names, Extra = (byte)Math.Max(0, Math.Min(255, level)),
        });
    }

    /// <summary>Client: the host's elite: the same constructor (the host's level), the host's abilities, the intro.</summary>
    private void PlayDarkElite(EnemyEvent e)
    {
        var enemy = EnemyOf(e.NetId);
        if (enemy == null || enemy.health == null || enemy.health.dead)
        {
            _darkEliteWaits.Remove(e);
            CoopTrace.Write("dark-elite-missing", _room, e.NetId, CoopTrace.NameOf(enemy), enemy == null ? WhyMissing(e.NetId) : "dead");
            return;
        }
        if (!enemy.gameObject.activeInHierarchy)
        {
            // its wave isn't out here yet (a coroutine can't start on it): wait a moment, as a summon waits for its prefab
            if (!_darkEliteWaits.TryGetValue(e, out double since))
                _darkEliteWaits[e] = since = Now;
            if (Now - since < DarkEliteWait)
            {
                _held.Add((Now + 0.1, e));
                return;
            }
            ActivateLikeHost(e.NetId, enemy, true);
        }
        _darkEliteWaits.Remove(e);
        if (enemy.GetComponentInChildren<DarkAbilityAttacher>(true) != null)
        {
            CoopTrace.Write("dark-elite-twice", _room, e.NetId, CoopTrace.NameOf(enemy));
            return;
        }
        var constructors = DarkEnemySelector.instance != null ? DarkEnemySelector.instance._constructors : null;
        if (constructors == null || constructors.Length == 0)
        {
            CoopTrace.Write("dark-elite-failed", _room, e.NetId, CoopTrace.NameOf(enemy), "no constructors");
            return;
        }
        var constructor = constructors[Math.Min(e.Extra, constructors.Length - 1)];
        string[] names = (e.ActionPath ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        if (!constructor.Provide(enemy))
        {
            CoopTrace.Write("dark-elite-failed", _room, e.NetId, CoopTrace.NameOf(enemy), "Provide");
            return;
        }
        var attacher = enemy.GetComponentInChildren<DarkAbilityAttacher>(true);
        if (attacher != null && attacher._darkAbilityContainer != null)
            HostAbilities[attacher._darkAbilityContainer] = names;
        var dark = enemy.GetComponent<DarkEnemy>();
        if (dark != null)
            dark.RunIntro(); // the look, the health bars, the abilities (Attach), the intro's operations
        else
            attacher?.Attach();
        NoteDarkAbilityPrefabs(attacher);
        string got = attacher != null && attacher._abilities != null ? string.Join("|", attacher._abilities.Where(a => a != null).Select(a => a.name)) : "";
        CoopTrace.Write(got == e.ActionPath ? "dark-elite-applied" : "dark-elite-differs", _room, e.NetId, CoopTrace.NameOf(enemy),
            got == e.ActionPath ? got : "host " + e.ActionPath + ", here " + got, amount: e.Extra);
    }

    // ------------------------------------------------------------------ the dark abilities that summon

    /// <summary>
    /// Two dark abilities make characters themselves (an Ability, no summon operation): Recharge (Umut's chapter 4
    /// "shrines": golden things that beam light at the elite and shield it; finding 7) and Ballista (the dark ability
    /// "Cannoneer"). A client's elite runs its abilities here too since the elites are in step, so each made its own
    /// unsynced shrine / cannoneer (bot run 2026-11-20 22:24: Cannoneer x3 on the client) while the host's came as
    /// summons whose prefab the client didn't know (summon-not-found). Now: a puppet's never summon (the host's come),
    /// the client knows the prefabs from the elite's abilities, and the host names the elite as the summoner so the
    /// client's shrine beams at it.
    /// </summary>
    internal static Character HostDarkSummoner;

    /// <summary>Client: the characters an elite's dark abilities can make, by name (PlaySummon finds them here).</summary>
    private void NoteDarkAbilityPrefabs(DarkAbilityAttacher attacher)
    {
        if (attacher?._abilities == null)
            return;
        foreach (var ability in attacher._abilities)
        {
            if (ability?._abilityComponents == null)
                continue;
            foreach (var component in ability._abilityComponents)
            {
                object inner = component?.GetType().GetProperty("ability")?.GetValue(component);
                if (inner == null)
                    continue;
                for (var t = inner.GetType(); t != null && t != typeof(object); t = t.BaseType)
                    foreach (var field in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                                      | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        object value = field.GetValue(inner);
                        if (value is Character one && one != null)
                            _spawnPrefabs[Loadout.GearName(one.name)] = one;
                        else if (value is Character[] many)
                            foreach (var c in many)
                                if (c != null)
                                    _spawnPrefabs[Loadout.GearName(c.name)] = c;
                    }
            }
        }
    }

    [HarmonyPatch]
    public static class DarkAbilitySummons
    {
        private static readonly Dictionary<Type, System.Reflection.PropertyInfo> Owners = new();

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(Ballista.Instance), typeof(Recharge.Instance) })
            {
                var method = AccessTools.DeclaredMethod(type, "TryToSummon");
                if (method != null)
                    yield return method;
                else
                    Plugin.Log.LogWarning($"[Coop] dark ability {type.DeclaringType?.Name}: no TryToSummon (not watched)");
            }
        }

        private static Character OwnerOf(object instance)
        {
            var type = instance.GetType();
            if (!Owners.TryGetValue(type, out var property))
                Owners[type] = property = AccessTools.Property(type, "owner");
            return property?.GetValue(instance) as Character;
        }

        private static bool Prefix(object __instance)
        {
            var sync = Current;
            if (sync == null)
                return true;
            var owner = OwnerOf(__instance);
            if (sync.IsClient && IsPuppet(owner))
                return false; // the host's elite summons; its summon comes as an event
            if (sync.IsHost)
                HostDarkSummoner = owner;
            return true;
        }

        private static void Finalizer() => HostDarkSummoner = null;
    }

    /// <summary>Clients: no elite is elected here (the host's come as events). Its counters stay untouched.</summary>
    [HarmonyPatch(typeof(DarkEnemySelector), nameof(DarkEnemySelector.ElectIn))]
    public static class NoElectionOnClients
    {
        private static bool Prefix(ICollection<Character> candidates)
        {
            var sync = Current;
            if (sync == null || !sync.IsClient)
                return true;
            CoopTrace.Write("dark-elect-skipped", CoopWorld.CurrentRoom(), amount: candidates?.Count ?? 0);
            return false;
        }
    }

    /// <summary>Host: an elite's abilities are attached (its intro): named to the clients.</summary>
    [HarmonyPatch(typeof(DarkAbilityAttacher), nameof(DarkAbilityAttacher.Attach))]
    public static class HostElite
    {
        private static void Postfix(DarkAbilityAttacher __instance)
        {
            try
            {
                var sync = Current;
                if (sync != null && sync.IsHost)
                    sync.OnHostDarkElite(__instance._owner, __instance._abilities);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] dark elite (host): " + e.Message);
            }
        }
    }

    /// <summary>Client: the elite's abilities are the host's (by name), not this game's own roll.</summary>
    [HarmonyPatch(typeof(DarkAbilityContainer), nameof(DarkAbilityContainer.GetDarkAbility))]
    public static class HostAbilityRoll
    {
        private static bool Prefix(DarkAbilityContainer __instance, ref ICollection<DarkAbility> __result)
        {
            if (!HostAbilities.TryGetValue(__instance, out var names))
                return true;
            HostAbilities.Remove(__instance);
            var elected = new List<DarkAbility>();
            var pool = __instance._weightedDarkAbility?.components ?? Array.Empty<WeightedDarkAbility>();
            foreach (string name in names)
            {
                var prefab = pool.FirstOrDefault(w => w != null && w.key != null && w.key.name == name)?.key;
                if (prefab == null)
                {
                    CoopTrace.Write("dark-ability-missing", CoopWorld.CurrentRoom(), name: CoopTrace.NameOf(__instance._owner), what: name);
                    continue;
                }
                var ability = UnityEngine.Object.Instantiate(prefab, __instance._owner.attach.transform);
                ability.name = prefab.name;
                ability.Initialize();
                elected.Add(ability);
            }
            __instance._electedAbilities = elected;
            __result = elected;
            return false;
        }
    }
}
