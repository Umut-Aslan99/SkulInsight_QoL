using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Characters;
using Characters.Gear.Synergy.Inscriptions;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// Collects Codex progress while you play and keeps it in BepInEx/DamageInsight/Codex/progress.json:
/// enemies seen/killed/damage, deaths, gear seen and picked up, inscriptions completed. Also captures an
/// enemy's portrait (its current sprite) the first time you meet it, into Codex/Portraits/&lt;key&gt;.png.
/// </summary>
public static class CodexTracker
{
    public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "DamageInsight", "Codex");
    private static string ProgressPath => Path.Combine(Folder, "progress.json");
    public static string PortraitPath(string key) => Path.Combine(Folder, "Portraits", key + ".png");

    private static CodexProgress _progress;
    private static bool _dirty;
    private static float _nextSave;

    /// <summary>Increases on every change, so the book knows when to redraw.</summary>
    public static int Version { get; private set; }

    // Once per run and object: a gear popup or re-equip must not count twice.
    private static readonly HashSet<int> CountedThisRun = new();
    private static int _runPlayer;

    public static CodexProgress Progress => _progress ??= Load();

    private static CodexProgress Load()
    {
        try
        {
            return File.Exists(ProgressPath) ? CodexProgress.FromJson(File.ReadAllText(ProgressPath, Encoding.UTF8)) : new CodexProgress();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not read progress.json, starting fresh (the old file is kept as .bak): {e.Message}");
            try { File.Copy(ProgressPath, ProgressPath + ".bak", overwrite: true); } catch (Exception) { /* nothing to keep */ }
            return new CodexProgress();
        }
    }

    /// <summary>Writes progress.json if something changed (at most every few seconds, and when the game quits).</summary>
    public static void SaveIfNeeded(bool force = false)
    {
        if (!_dirty || (!force && Time.unscaledTime < _nextSave))
            return;
        _nextSave = Time.unscaledTime + 5f;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = ProgressPath + ".tmp";
            File.WriteAllText(temp, Progress.ToJson(), new UTF8Encoding(false));
            if (File.Exists(ProgressPath))
                File.Delete(ProgressPath);
            File.Move(temp, ProgressPath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not save progress: {e.Message}");
        }
    }

    /// <summary>Saved data was changed from outside (a migration): save it soon.</summary>
    internal static void MarkChanged() => Changed();

    private static void Changed()
    {
        _dirty = true;
        Version++;
    }

    private static bool Enabled => Plugin.CodexEnabled.Value;

    // ------------------------------------------------------------------ enemies

    /// <summary>A hit landed on <paramref name="target"/> (from DamageRecordPatch).</summary>
    public static void OnHit(Character target, in Damage damage, double dealt)
    {
        if (!Enabled || target == null)
            return;
        Character attacker = damage.attacker.character;
        bool playerSide = attacker != null && (attacker.type == Character.Type.Player || attacker.type == Character.Type.PlayerMinion);

        if (target.type == Character.Type.Player)
        {
            // An enemy hit you.
            if (attacker == null || EntryOf(attacker) == null)
                return;
            // A boss attacking you is a fight too, even before you could hit it (Pope behind his barrier).
            if (BossAttacks.Covers(attacker) && !attacker.health.dead)
                FightRecorder.Watch(attacker, EntryOf(attacker).Value.key);
            var p = See(attacker);
            p.DamageTaken += dealt;
            p.WorstHit = Math.Max(p.WorstHit, dealt);
            if (target.health.dead)
                p.DeathsBy++;
            Changed();
            return;
        }

        if (!playerSide || EntryOf(target) == null)
            return;
        var enemy = See(target);
        if (BossAttacks.Covers(target) && !target.health.dead)
            FightRecorder.Watch(target, EntryOf(target).Value.key); // film its attacks from now on
        enemy.DamageDealt += dealt;
        enemy.BestHit = Math.Max(enemy.BestHit, dealt);
        if (target.health.dead && CountedThisRun.Add(target.GetInstanceID()))
        {
            enemy.Kills++;
            RunRecorder.OnKilled(EntryOf(target).Value.id);
            // Kills are when the enemy is out of the fight: capturing its animations can't disturb anything.
            CodexAnimations.RequestCapture(target, EntryOf(target).Value.key, CodexWindow.Instance);
        }
        Changed();
    }

    /// <summary>Marks an enemy as seen (once per spawned enemy), records its HP and captures its portrait once.</summary>
    /// <summary>
    /// The Codex entry of a character: by its game key, or, for bosses/elites/adventurers the game gave no key
    /// (Dark Mirror bosses), by its object name. Null for everything else (props, summons, the player).
    /// </summary>
    public static (string id, string key, CodexCategory category)? EntryOf(Character c)
    {
        if (CodexGroups.ForEnemy((int)c.key) is { } group)
            return (CodexCatalog.EnemyId(c.key), c.key.ToString(), group.category);
        if (c.key != Characters.Key.Unspecified ||
            c.type is not (Character.Type.Boss or Character.Type.Named or Character.Type.Adventurer))
            return null;
        string clean = CodexCatalog.CleanName(c.name);
        if (clean.Length == 0)
            return null;
        // Adventurers are boss fights too (their own group in the Bosses tab).
        return (CodexCatalog.NamedId(clean), clean,
            c.type is Character.Type.Boss or Character.Type.Adventurer ? CodexCategory.Bosses : CodexCategory.Enemies);
    }

    private static EntryProgress See(Character enemy)
    {
        var entry = EntryOf(enemy).Value;
        var p = Progress.Get(entry.id);
        if (entry.id.StartsWith("named:") && p.Name.Length == 0)
        {
            p.Name = CodexCatalog.DisplayName(entry.key);
            p.Category = entry.category.ToString();
            CodexCatalog.EnsureNamed(entry.id, p.Name, entry.category);
        }
        if (CountedThisRun.Add(~enemy.GetInstanceID()))
        {
            p.Seen++;
            RunRecorder.OnAppeared(entry.id);
            LinkToFight(enemy, entry.id, p);
        }
        if (enemy.health != null)
            p.MaxHp = Math.Max(p.MaxHp, enemy.health.maximumHealth);
        if (CodexTiers.RevealAllMet)
            CodexAnimations.RequestCapture(enemy, entry.key, CodexWindow.Instance); // dev: capture on first contact
        return p;
    }

    /// <summary>
    /// Ties what shows up in a boss fight to that boss: boss-type pieces without a mind of their own (Pope's dark
    /// crystals) become part of the boss's page; ordinary enemies appearing in the fight are listed as its summons
    /// (they keep their own pages).
    /// </summary>
    private static void LinkToFight(Character enemy, string id, EntryProgress p)
    {
        if (BossAttacks.Covers(enemy))
        {
            if (p.PartOf.Length > 0 || BossAttacks.HasAi(enemy))
                return;
            var owner = FightRecorder.MainBoss(search: true);
            if (owner != null && owner != enemy && EntryOf(owner) is { } boss && boss.id != id)
                p.PartOf = boss.id;
            return;
        }
        var fight = FightRecorder.MainBoss();
        if (fight != null && EntryOf(fight) is { } summoner && summoner.id != id)
            p.AddSummoner(summoner.id);
    }

    /// <summary>A boss or adventurer was seen doing a move (from the fight recorder), per mode.</summary>
    public static void MarkMoveSeen(Character boss, bool dark, string label)
    {
        if (!Enabled || boss == null || EntryOf(boss) is not { } entry)
            return;
        if (Progress.Get(entry.id).AddMove(label, dark))
            Changed();
    }

    /// <summary>The moves already seen for this boss in that mode (for twins such as the Leiana sisters: by either).</summary>
    public static HashSet<string> MovesSeen(Character boss, bool dark)
    {
        if (boss == null || EntryOf(boss) is not { } entry)
            return new HashSet<string>();
        string page = CodexCatalog.TwinOf.TryGetValue(entry.id, out var primary) ? primary : entry.id;
        return Page(page) is { } p ? new HashSet<string>(p.MovesSeen(dark)) : new HashSet<string>();
    }

    /// <summary>
    /// The progress a book page shows: an entry's own, or for a page showing several entries (the Leiana sisters)
    /// all of them together (counts added, best values kept, seen moves joined).
    /// </summary>
    public static EntryProgress Page(string id)
    {
        var own = Progress.Peek(id);
        var members = CodexCatalog.TwinOf.Where(t => t.Value == id).Select(t => Progress.Peek(t.Key)).Where(m => m != null).ToList();
        if (members.Count == 0)
            return own;
        var page = new EntryProgress();
        foreach (var p in new[] { own }.Concat(members).Where(p => p != null))
        {
            page.Seen += p.Seen;
            page.Kills += p.Kills;
            page.DeathsBy += p.DeathsBy;
            page.PickedUp += p.PickedUp;
            page.DamageDealt += p.DamageDealt;
            page.DamageTaken += p.DamageTaken;
            page.BestHit = Math.Max(page.BestHit, p.BestHit);
            page.WorstHit = Math.Max(page.WorstHit, p.WorstHit);
            page.MaxHp = Math.Max(page.MaxHp, p.MaxHp);
            foreach (var m in p.MovesSeen(false)) page.AddMove(m, false);
            foreach (var m in p.MovesSeen(true)) page.AddMove(m, true);
        }
        return page;
    }

    // ------------------------------------------------------------------ gear and inscriptions

    /// <summary>A gear popup was opened for <paramref name="gear"/> (seen).</summary>
    public static void OnGearSeen(Characters.Gear.Gear gear)
    {
        if (!Enabled || gear == null || !CountedThisRun.Add(gear.GetInstanceID() ^ 0x5EE5))
            return;
        Progress.Get(CodexCatalog.GearId(gear.type, gear.name.Replace("(Clone)", "").Trim())).Seen++;
        Changed();
    }

    /// <summary>A gear piece was equipped (picked up) by the player.</summary>
    public static void OnGearEquipped(Characters.Gear.Gear gear)
    {
        if (!Enabled || gear == null || !CountedThisRun.Add(gear.GetInstanceID()))
            return;
        var p = Progress.Get(CodexCatalog.GearId(gear.type, gear.name.Replace("(Clone)", "").Trim()));
        p.PickedUp++;
        p.Seen = Math.Max(p.Seen, 1);
        Changed();
    }

    /// <summary>A dark ability was taken (UpgradeObject.Attach).</summary>
    public static void OnDarkAbilityAttached(Characters.Gear.Upgrades.UpgradeObject upgrade)
    {
        if (!Enabled || upgrade == null || !CountedThisRun.Add(upgrade.GetInstanceID()))
            return;
        string name = upgrade.reference?.name ?? CodexCatalog.CleanName(upgrade.name);
        var p = Progress.Get(CodexCatalog.DarkId(name));
        p.PickedUp++;
        p.Seen = Math.Max(p.Seen, 1);
        Changed();
    }

    /// <summary>An inscription's count changed: collected (count > 0) and completed (max step) once per run.</summary>
    public static void OnInscriptionUpdated(Inscription inscription)
    {
        if (!Enabled || inscription == null || inscription.count <= 0)
            return;
        var id = CodexCatalog.InscriptionId(inscription.key);
        var p = Progress.Get(id);
        bool changed = false;
        if (CountedThisRun.Add(id.GetHashCode()))
        {
            p.Seen++;
            changed = true;
        }
        if (inscription.isMaxStep && CountedThisRun.Add(id.GetHashCode() ^ 0x3A3A))
        {
            p.PickedUp++;
            changed = true;
        }
        if (changed)
            Changed();
    }

    /// <summary>Called every frame by the plugin: a new player object means a new run (per-run counting resets).</summary>
    public static void Tick(Character player)
    {
        if (player != null && player.GetInstanceID() != _runPlayer)
        {
            _runPlayer = player.GetInstanceID();
            CountedThisRun.Clear();
        }
        SaveIfNeeded();
    }
}
