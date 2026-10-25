using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using DamageInsight.Lang;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.History;

/// <summary>
/// The heal orbs of the map you are on (0.14, Umut: "the purple heal balls the game drops are kind of hard to see.
/// Many beginners miss them"). They are the game's Level.Potion (Small from enemies, Medium / Large from the
/// adventurer party and boss chests; the bottles are another thing, PurchasablePotion). Every dropped orb is noted
/// (Patches/HealOrbPatches), taken ones are marked; when you leave the map, or die on it, the small orbs still lying
/// there count as heals missed if you weren't at full health, with the health they would have restored
/// (HealOrbMath). The run keeps them (RunNumbers.HealsMissed / HealthMissed). When a map is cleared while you are hurt
/// and orbs are left, a short text above you says so, once (setting Heal Orbs/Reminder). Orbs get their look from
/// UI/HealOrbLook.
/// </summary>
public static class HealOrbs
{
    private sealed class Orb
    {
        public Potion Potion;
        public int Size;
        public int Heal;
        public bool Taken;
    }

    private static readonly List<Orb> Orbs = new();
    private static Map _map;
    private static double _hp, _max = -1, _healFactor = 1;
    private static float _clearedAt = -1f;
    private static bool _reminded;
    private static string[] _sizeNames;

    /// <summary>LevelManager.DropPotion made an orb.</summary>
    public static void Dropped(Potion potion)
    {
        if (potion == null || DamageInsight.Codex.CodexTracker.Paused)
            return;
        int size = SizeOf(potion);
        Orbs.Add(new Orb { Potion = potion, Size = size, Heal = potion._healAmount });
        UI.HealOrbLook.Attach(potion, size); // it follows the setting, also when it changes later
    }

    /// <summary>Potion.InteractWith: the orb is taken when the game will use it (the same check as the game's).</summary>
    public static void Touched(Potion potion, Character character)
    {
        if (potion == null || character == null)
            return;
        var orb = Orbs.FirstOrDefault(o => o.Potion == potion);
        if (orb == null)
            return;
        bool used = character.health.percent != 1.0
                    || character.ability.GetInstance<Characters.Abilities.Savable.LifeChange>() != null
                    || character.ability.GetInstance<Characters.Abilities.Upgrades.Glutton>() != null;
        if (used)
            orb.Taken = true;
    }

    /// <summary>Every frame: the map you are on, your health, the reminder.</summary>
    public static void Tick(Character player)
    {
        var map = Map.Instance;
        if (map != _map)
        {
            Close(died: false);
            _map = map;
            _reminded = false;
            _clearedAt = -1f;
        }
        if (player == null || player.health == null || player.health.dead)
            return;
        _hp = player.health.currentHealth;
        _max = player.health.maximumHealth;
        _healFactor = player.stat.GetFinal(Stat.Kind.TakingHealAmount);
        Remind(player);
    }

    /// <summary>
    /// The map is left (or the run ended on it): the small orbs still lying there are heals missed when the player
    /// wasn't at full health; on a death the whole bar was missing.
    /// </summary>
    public static void Close(bool died)
    {
        if (Orbs.Count == 0)
            return;
        var left = Orbs.Where(o => !o.Taken && o.Size == 0).Select(o => o.Heal * _healFactor).ToList();
        Orbs.Clear();
        if (_max <= 0 || left.Count == 0)
            return;
        var (orbs, health) = HealOrbMath.Missed(left, died ? 0 : _hp, _max);
        if (orbs > 0)
            RunJournal.AddHealsMissed(orbs, health);
    }

    /// <summary>The run is over without a death on this map (a win): nothing on it counts any more.</summary>
    public static void Forget() => Orbs.Clear();

    private static void Remind(Character player)
    {
        if (_reminded || Plugin.HealOrbReminder?.Value == false || _hp >= _max - 0.5)
            return;
        var waves = _map != null ? _map.waveContainer : null;
        if (waves == null || waves.enemyWaves == null || waves.enemyWaves.Length == 0 || waves.state != EnemyWaveContainer.State.Empty)
        {
            _clearedAt = -1f;
            return;
        }
        int left = Orbs.Count(o => !o.Taken && o.Potion != null);
        if (left == 0)
            return;
        if (_clearedAt < 0f)
            _clearedAt = Time.time;
        if (Time.time - _clearedAt < 1.5f)
            return;
        _reminded = true;
        Singleton<Service>.Instance?.floatingTextSpawner?.SpawnBuff(Loc.P("{0} healing orb left!", "{0} healing orbs left!", left),
            player.transform.position + Vector3.up * 2.4f, "#7CF59A");
    }

    /// <summary>0 small, 1 medium, 2 large: the game's prefab names (CommonResource.potions).</summary>
    private static int SizeOf(Potion potion)
    {
        if (_sizeNames == null)
        {
            var common = GameResources.CommonResource.instance;
            _sizeNames = common == null ? new string[0]
                : new[] { common.smallPotion?.name ?? "", common.mediumPotion?.name ?? "", common.largePotion?.name ?? "" };
        }
        int index = Array.IndexOf(_sizeNames, potion.name);
        return index < 0 ? 0 : index;
    }
}
