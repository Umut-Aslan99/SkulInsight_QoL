using System.Collections.Generic;
using Characters;
using Characters.Gear;
using Characters.Operations;
using HarmonyLib;
using Services;
using Singletons;

namespace DamageInsight.Coop;

/// <summary>
/// The teammate copies in this game (RemoteAvatar) and what keeps them from acting on it (docs/coop/GAME_FLOW.md §10,
/// hooks 2 and 3): a copy's attacks, its summons' and projectiles' (they attack as the copy) deal no damage,
/// knockback or statuses here (its owner's game sends the real hits), and its gear doesn't count as gear in this
/// world (it would leave this player's reward pool, and destroying it would count and play like the player's own).
/// </summary>
public static class CoopCopies
{
    private static readonly HashSet<Character> Copies = new();

    public static int Count => Copies.Count;

    /// <summary>The teammates' copies on this game (some may have been destroyed with their map).</summary>
    internal static IEnumerable<Character> All => Copies;

    internal static void Register(Character copy)
    {
        if (copy != null)
            Copies.Add(copy);
    }

    internal static void Unregister(Character copy) => Copies.Remove(copy);

    public static bool IsCopy(Character character) => character != null && Copies.Count > 0 && Copies.Contains(character);

    /// <summary>A copy, or a minion or gear character (a quintessence's) whose player is a copy.</summary>
    public static bool BelongsToCopy(Character attacker)
    {
        if (attacker == null || Copies.Count == 0)
            return false;
        if (Copies.Contains(attacker))
            return true;
        if (attacker.type == Character.Type.Player)
            return false;
        var player = PlayerBehind(attacker);
        return player != null && Copies.Contains(player);
    }

    /// <summary>
    /// The player (this game's or a teammate's copy) a character fights for: the player itself, a minion's leader (a
    /// minion without a known leader is its own answer), or the owner of the gear the character is part of. A
    /// quintessence acts through its own character, and 21 of the 23 such characters are typed TrashMob (Archdemon,
    /// Banshee, Cyclops, Ifrit, Medusa, ...; GearScan): a check by type alone never sent a client's quintessence hits
    /// to the host (Umut 2026-11-20: the Archdemon left enemies at 1 health, then they healed back). Null: not on the
    /// players' side (an enemy, a trap, the world).
    /// </summary>
    public static Character PlayerBehind(Character character)
    {
        if (character == null)
            return null;
        if (character.type == Character.Type.Player)
            return character;
        // a character this mod gave a player (a teammate's Gravedigger spirit: TeammateGraves)
        if (Adopted.Count > 0 && Adopted.TryGetValue(character, out var adopter))
        {
            if (adopter != null)
                return adopter;
            Adopted.Remove(character);
        }
        // a quintessence's character: the game detaches it from its quintessence (TeammateEssences)
        var essence = TeammateEssences.Of(character);
        if (essence != null)
        {
            var holder = essence.owner;
            return holder != null && holder.type == Character.Type.Player ? holder : null;
        }
        if (character.type == Character.Type.PlayerMinion)
        {
            var minion = character.GetComponent<Minion>();
            var leader = minion != null && minion.leader != null ? minion.leader.player : null;
            return leader != null ? leader : character;
        }
        // kept: "none" (a real null) or a living owner; a destroyed owner (Unity's == says null) is looked up again
        if (GearOwners.TryGetValue(character, out var known) && (ReferenceEquals(known, null) || known != null))
            return known;
        if (GearOwners.Count > 256)
            GearOwners.Clear();
        var gear = character.GetComponentInParent<Gear>();
        var owner = gear != null ? gear.owner : null;
        if (owner != null && owner.type != Character.Type.Player)
            owner = null;
        // an unequipped gear has no owner yet: only a found owner is kept (an enemy's "none" is kept too)
        if (owner != null || gear == null)
            GearOwners[character] = owner;
        return owner;
    }

    /// <summary>On the players' side (see <see cref="PlayerBehind"/>).</summary>
    public static bool IsPlayerSide(Character character) => PlayerBehind(character) != null;

    private static readonly Dictionary<Character, Character> GearOwners = new();
    private static readonly Dictionary<Character, Character> Adopted = new();

    /// <summary>Forgets what <see cref="PlayerBehind"/> found for a character (it is part of something else now).</summary>
    internal static void Forget(Character character)
    {
        if (character != null)
            GearOwners.Remove(character);
    }

    /// <summary><paramref name="character"/> fights for <paramref name="player"/> from now on (see PlayerBehind).</summary>
    internal static void Adopt(Character character, Character player)
    {
        if (character == null || player == null)
            return;
        if (Adopted.Count > 128)
        {
            var gone = new List<Character>();
            foreach (var pair in Adopted)
                if (pair.Key == null || pair.Value == null)
                    gone.Add(pair.Key);
            foreach (var key in gone)
                Adopted.Remove(key);
        }
        Adopted[character] = player;
        GearOwners.Remove(character);
    }

    internal static void Unadopt(Character character)
    {
        if (character != null)
            Adopted.Remove(character);
    }

    /// <summary>A copy's gear leaves the game's gear registry (the reward pool skips registered gear) and is never
    /// "destroyed" like the player's (counted, sound, effect) when the copy changes it.</summary>
    internal static void Disown(Gear gear)
    {
        if (gear == null)
            return;
        gear.destructible = false;
        var manager = Singleton<Service>.Instance?.gearManager;
        if (manager == null)
            return;
        switch (gear)
        {
            case Characters.Gear.Weapons.Weapon weapon:
                manager.UnregisterWeaponInstance(weapon);
                break;
            case Characters.Gear.Items.Item item:
                manager.UnregisterItemInstance(item);
                break;
            case Characters.Gear.Quintessences.Quintessence essence:
                manager.UnregisterEssenceInstance(essence);
                break;
        }
    }

    // ------------------------------------------------------------------ a copy's idle "wait" move

    /// <summary>A skull's idle "wait" move (after 20 s without a key) checks the player's keys: a copy has none (its
    /// PlayerInput is stripped), so the check stopped with an error each time a copy appeared (Player log), and a copy
    /// must not start moves by itself anyway (its owner's game sends them).</summary>
    [HarmonyPatch(typeof(Characters.Gear.Weapons.SkulWait), "CCheckWait")]
    public static class NoIdleWait
    {
        private static void Postfix(Characters.Gear.Weapons.SkulWait __instance, ref System.Collections.IEnumerator __result) =>
            __result = UnlessCopy(__instance, __result);

        private static System.Collections.IEnumerator UnlessCopy(Characters.Gear.Weapons.SkulWait wait, System.Collections.IEnumerator original)
        {
            yield return null; // a copy is registered right after it appears
            if (wait == null)
                yield break;
            var owner = wait._weapon != null ? wait._weapon.owner : null;
            if (IsCopy(owner) || IsCopy(wait.GetComponentInParent<Character>()))
                yield break;
            while (original.MoveNext())
                yield return original.Current;
        }
    }

    // ------------------------------------------------------------------ a move with a missing operation

    private static readonly HashSet<string> BrokenMotionsLogged = new();

    /// <summary>An action of a character without player input (a teammate's copy) can't be started by a button:
    /// "no" instead of the game's NullReferenceException. `ReservedActionAbility` (a skull's follow-up move that
    /// waits for a button during another move) asks this every frame while its key move runs, also on the copy the
    /// motion replay plays the key move on: 3903 errors on the host in one run (COOP_LOG 50i, 51), 211 on a client
    /// in the skull sweep. The teammate's own game starts the follow-up and sends it like any move.</summary>
    [HarmonyPatch(typeof(Characters.Actions.Action), nameof(Characters.Actions.Action.PassPrecondition))]
    public static class NoButtonWithoutInput
    {
        private static bool Prefix(Characters.Actions.Action __instance, ref bool __result)
        {
            if (__instance._input != null)
                return true;
            __result = false;
            return false;
        }
    }

    /// <summary>The pit of King Alexander's arenas (`EmperorFall`) kills minions and puts a player back on the
    /// ground with damage. A teammate's copy that falls in is neither (its player's own game puts it back), and a
    /// character without health threw there: 20 NullReferenceExceptions on each game in the fourth `dm6r2`.</summary>
    [HarmonyPatch(typeof(Level.Traps.EmperorFall), "OnTriggerEnter2D")]
    public static class NoPitForCopies
    {
        private static bool Prefix(UnityEngine.Collider2D collision)
        {
            var character = collision != null ? collision.GetComponent<Character>() : null;
            return character == null || (!IsCopy(character) && character.health != null);
        }
    }

    /// <summary>While co-op runs: a move whose operation list has a missing entry is stopped without an error. The
    /// game's own loop threw (NullReferenceException, COOP_LOG 46f: 6 x when a move ended, 1 x when the host's stun
    /// was put on a puppet, which also stopped the enemies' sync for that frame), and a move that threw while ending
    /// stays "running", so its character could start no other move. Same result as the game's loop otherwise; the
    /// trace names the move (`motion-broken`) to find where it comes from.</summary>
    [HarmonyPatch(typeof(Characters.Actions.Motion), "StopAllOperations")]
    public static class SafeStopAllOperations
    {
        private static bool Prefix(Characters.Actions.Motion __instance)
        {
            if (CoopRuntime.Instance == null)
                return true;
            var infos = __instance._operationInfos;
            bool broken = infos == null;
            if (infos != null)
            {
                foreach (var info in infos)
                {
                    if (info == null || info.operation == null)
                    {
                        broken = true;
                        continue;
                    }
                    if (!info.stay)
                        info.operation.Stop();
                }
            }
            if (broken)
            {
                string owner = __instance.action != null && __instance.action.owner != null ? CoopTrace.NameOf(__instance.action.owner) : "?";
                string what = owner + ": " + (__instance.action != null ? __instance.action.name : "?") + "/" + __instance.name
                    + (infos == null ? " (destroyed)" : "");
                if (BrokenMotionsLogged.Add(what))
                {
                    Plugin.Log.LogInfo("[Coop] a move with a missing operation was stopped: " + what);
                    CoopTrace.Write("motion-broken", CoopWorld.CurrentRoom(), name: owner, what: what);
                }
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ hook 2: a copy's hits do nothing here

    [HarmonyPatch(typeof(Character), nameof(Character.TryAttackCharacter),
        new[] { typeof(ITarget), typeof(Damage) }, new[] { ArgumentType.Normal, ArgumentType.Ref })]
    public static class NoAttack
    {
        // Projectile.onTargetHit and SweepAttack run knockback, statuses and hit effects only when this returns true.
        private static bool Prefix(Character __instance, ref bool __result)
        {
            if (!BelongsToCopy(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AttackDamageable),
        new[] { typeof(ITarget), typeof(Damage) }, new[] { ArgumentType.Normal, ArgumentType.Ref })]
    public static class NoDamageable
    {
        private static bool Prefix(Character __instance) => !BelongsToCopy(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.TryKillCharacter), new[] { typeof(Character), typeof(HitInfo) })]
    public static class NoKillCharacter
    {
        private static bool Prefix(Character __instance) => !BelongsToCopy(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.TryKillTarget), new[] { typeof(ITarget) })]
    public static class NoKillTarget
    {
        private static bool Prefix(Character __instance) => !BelongsToCopy(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.TryKillTarget),
        new[] { typeof(ITarget), typeof(Damage) }, new[] { ArgumentType.Normal, ArgumentType.Ref })]
    public static class NoKillTargetWithDamage
    {
        private static bool Prefix(Character __instance) => !BelongsToCopy(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GiveStatus))]
    public static class NoStatus
    {
        private static bool Prefix(Character __instance, ref bool __result)
        {
            if (!BelongsToCopy(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Level.DestructibleObject), nameof(Level.DestructibleObject.Hit),
        new[] { typeof(Character), typeof(Damage) }, new[] { ArgumentType.Normal, ArgumentType.Ref })]
    public static class NoPropHit
    {
        private static bool Prefix(Character from) => !BelongsToCopy(from);
    }
}
