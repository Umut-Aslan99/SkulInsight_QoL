using System;
using System.Collections.Generic;
using Characters;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Whose kill an enemy was (COOP_LOG 54). Enemies really die only on the host, so everything the game and this mod
/// hang on "my hit killed it" was dead on a client:
/// - the game raises a player's Character.onKilled only when that player's own hit leaves the target dead
///   (Character.Attack). A client's enemy never dies by its own hit (the host says when), so every on-kill effect
///   of a client's skull and items stayed silent: Grim Reaper's and Arch Lich's souls, kill-count stat bonuses,
///   Arson, Victory Baton, quintessences that trigger on a kill, the game's kill counter;
/// - this mod's own book (CodexTracker), run history and kill quests count a kill in the hit that killed, which a
///   client's game never has: no kills in the book, no quest gold or dark quartz (Umut's "gold is not shared",
///   the part that was the mod's own).
/// So the host's death message names the slot of the player whose hit killed (255: nobody's), and a client
/// - counts the kill for its book, run and quests whoever of the team struck (on the host a teammate's kill counts
///   the same way: the hit lands there as the teammate copy's), and
/// - when the kill was its own, raises onKilled on the character that struck last (the player, or a minion, which
///   passes it on to its leader) with that last hit, as the game does after a killing hit.
/// </summary>
internal sealed partial class EnemySync
{
    public const byte NobodysKill = 255;

    // Host: the player's slot behind the hit that is taking an enemy's health now, by enemy id. Health.TakeHealth
    // raises the death events inside itself, BEFORE the hit's onTookDamage, so the death has to find its hit here.
    private readonly Dictionary<ushort, (int frame, byte slot)> _blows = new();
    // Client: this player's last hit on each puppet (who struck, and the hit as the puppet took it).
    private readonly Dictionary<ushort, (Character by, Damage hit)> _ownHits = new();

    public int KillsCredited;

    /// <summary>Host: every enemy with an id tells which hit is about to take its health.</summary>
    private void WatchBlows(Character enemy, ushort id)
    {
        Health.BeforeTakeHealth onBlow = damage => _blows[id] = (Time.frameCount, SlotBehind(damage.attacker.character));
        enemy.health.OnBeforeTakeHealth += onBlow;
        _unsubscribe.Add(() =>
        {
            if (enemy != null && enemy.health != null)
                enemy.health.OnBeforeTakeHealth -= onBlow;
        });
    }

    /// <summary>The slot of the player behind an attacker: the player or teammate copy itself, or the leader of a
    /// minion (a minion with no known leader is this game's own player's).</summary>
    private byte SlotBehind(Character attacker)
    {
        var player = CoopCopies.PlayerBehind(attacker); // the player, a minion's leader or a quintessence's owner
        if (player == null)
            return NobodysKill;
        int slot = _slotOf(player);
        if (slot < 0)
            slot = _session.LocalSlot;
        return slot is >= 0 and < NobodysKill ? (byte)slot : NobodysKill;
    }

    /// <summary>Host: whose hit is killing this enemy right now (255: no player's, e.g. a script's Kill).</summary>
    private byte KillerOf(ushort id) =>
        _blows.TryGetValue(id, out var blow) && blow.frame == Time.frameCount ? blow.slot : NobodysKill;

    /// <summary>Client: this player's (or its minion's) hit on a puppet, kept for the kill it may turn out to be.</summary>
    private void NoteOwnHit(ushort id, Character by, in Damage hit) => _ownHits[id] = (by, hit);

    /// <summary>Client: the host says this enemy died, by the hit of the player at <paramref name="slot"/>.</summary>
    private void CreditKill(Character enemy, ushort id, byte slot)
    {
        bool byPlayer = slot != NobodysKill;
        try
        {
            if (byPlayer)
                DamageInsight.Codex.CodexTracker.OnTeamKill(enemy);
            DamageInsight.History.RunJournal.OnTeamKill(enemy, byPlayer);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] a team kill was not counted: " + e.Message);
        }
        if (slot != _session.LocalSlot)
        {
            _ownHits.Remove(id);
            return;
        }
        // This player's own kill: the game's on-kill effects, as after a killing hit of its own.
        var me = _playerOf(_session.LocalSlot);
        Character by = me;
        Damage hit;
        if (_ownHits.TryGetValue(id, out var last))
        {
            _ownHits.Remove(id);
            if (last.by != null)
                by = last.by;
            hit = last.hit;
        }
        else if (me != null && enemy.collider != null)
            hit = Damage.CreateKillDamage(me, enemy);
        else
            return;
        if (by == null)
            return;
        var target = enemy.GetComponentInChildren<Target>(true);
        if (target == null)
        {
            CoopTrace.Write("kill-uncredited", _room, id, CoopTrace.NameOf(enemy), "no target part", slot: slot);
            return;
        }
        KillsCredited++;
        CoopTrace.Write("kill-credited", _room, id, CoopTrace.NameOf(enemy), by == me ? null : "by " + by.name, slot: slot);
        try
        {
            by.onKilled?.Invoke(target, ref hit);
        }
        catch (Exception e)
        {
            // one skull's or item's on-kill effect failing must not stop the others' deaths from arriving
            Plugin.Log.LogError("[Coop] an on-kill effect failed for " + CoopTrace.NameOf(enemy) + ": " + e);
        }
    }

    /// <summary>For the test trace: the book counted a kill (host: in the killing hit; client: by the host's word).</summary>
    private void OnKillCounted(Character enemy)
    {
        if (CoopTrace.On && _map != null)
            CoopTrace.Write("kill-counted", _room, _ids.TryGetValue(enemy, out var id) ? id : -1, CoopTrace.NameOf(enemy));
    }
}
