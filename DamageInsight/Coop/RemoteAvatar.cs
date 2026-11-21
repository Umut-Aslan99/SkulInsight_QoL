using System;
using System.Linq;
using Characters;
using Characters.Controllers;
using Characters.Player;
using DamageInsight.Coop.Sync;
using GameResources;
using Scenes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DamageInsight.Coop;

/// <summary>
/// A teammate drawn in this game (phase 0: position, facing, walk/jump/fall animation, health). It is a copy of the
/// game's player prefab with everything that belongs to "the one player" removed before it wakes up
/// (docs/coop/GAME_FLOW.md §1.4): no keyboard, no game over, no camera, no achievements or end screen numbers, no
/// interaction with doors and chests. It never becomes `LevelManager.player`, takes no damage (`cinematic`) and has
/// its own physics off: its owner's game moves it, through the snapshot buffer.
/// </summary>
internal sealed class RemoteAvatar
{
    /// <summary>The key of this mod's flags on the copy (cinematic).</summary>
    private static readonly object Key = new();

    /// <summary>Player-only parts of the prefab (GAME_FLOW §1.4 step 2).</summary>
    private static readonly Type[] Stripped =
    {
        typeof(PlayerInput), typeof(HandlePlayerDeath), typeof(PlayerCameraController), typeof(PlayerCriticalEffect),
        typeof(AchievementTrackers.PlayerAchievementTracker), typeof(PlayerGameResultDataTracker),
        typeof(PlayerKillCounter), typeof(PlayerEasyModeBuff), typeof(Characters.Movements.StuckResolver),
        typeof(CharacterInteraction),
    };

    private static bool _componentsLogged;

    public readonly int Slot;
    public Character Character { get; private set; }
    private bool _shown = true;
    private float _health = -1, _maxHealth = -1;

    private RemoteAvatar(int slot, Character character)
    {
        Slot = slot;
        Character = character;
    }

    public bool Alive => Character != null;
    public bool Shown => Alive && _shown;

    /// <summary>Makes the copy; null when the game isn't ready (no prefab or no GameBase yet).</summary>
    public static RemoteAvatar Create(int slot, string name)
    {
        var prefab = CommonResource.instance?.player;
        var gameBase = Scene<GameBase>.instance;
        if (prefab == null || gameBase == null)
            return null;
        // Inside an inactive holder the copy's Awake hasn't run yet: strip it first, then let it wake.
        var holder = new GameObject("SkulInsightCoop_Holder");
        holder.SetActive(false);
        holder.transform.SetParent(gameBase.transform, false);
        Character copy = null;
        try
        {
            copy = Object.Instantiate(prefab, holder.transform);
            LogComponents(copy.gameObject);
            Strip(copy.gameObject);
            copy.name = $"SkulInsightCoop_P{slot + 1}";
            copy.transform.SetParent(gameBase.transform, true);
        }
        finally
        {
            Object.Destroy(holder);
        }
        copy.cinematic.Attach(Key);   // takes no damage on this game (its owner's game decides its health)
        if (copy.movement != null)
            copy.movement.enabled = false; // no gravity or collisions here: the owner's game moves it
        CoopCopies.Register(copy);
        // The default skull it woke up with isn't gear of this world (reward pool, destroy effects).
        foreach (var weapon in copy.playerComponents?.inventory?.weapon?.weapons ?? Array.Empty<Characters.Gear.Weapons.Weapon>())
            CoopCopies.Disown(weapon);
        Plugin.Log.LogInfo($"[Coop] teammate {name} (slot {slot}) drawn as {copy.name}");
        return new RemoteAvatar(slot, copy);
    }

    private static void Strip(GameObject root)
    {
        foreach (var type in Stripped)
        {
            foreach (var component in root.GetComponentsInChildren(type, true))
            {
                try
                {
                    Object.DestroyImmediate(component);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Coop] could not remove {type.Name} from the teammate copy: {e.Message}");
                }
            }
        }
    }

    /// <summary>Once per session: every component type on the prefab (GAME_FLOW §1.6: the full list of player-only
    /// parts needs a look at the real prefab).</summary>
    private static void LogComponents(GameObject root)
    {
        if (_componentsLogged)
            return;
        _componentsLogged = true;
        try
        {
            var types = root.GetComponentsInChildren<Component>(true).Where(c => c != null)
                .GroupBy(c => c.GetType().FullName).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
            Plugin.Log.LogInfo("[Coop] player prefab components: " + string.Join(", ", types));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] could not list the player prefab's components: " + e.Message);
        }
    }

    /// <summary>Shows or hides the copy (hidden while the teammate is in another map or no state arrived).</summary>
    public void Show(bool show)
    {
        if (!Alive || show == _shown)
            return;
        _shown = show;
        Character.gameObject.SetActive(show);
    }

    /// <summary>Puts the copy where its owner's game says it is (one sampled state per frame).</summary>
    public void Apply(in PlayerState s, Vector2 mapOrigin)
    {
        if (!Alive)
            return;
        Show(true);
        DropDeadAnimations(Character);
        var t = Character.transform;
        var position = t.position;
        position.x = mapOrigin.x + s.Position.X;
        position.y = mapOrigin.y + s.Position.Y;
        t.position = position;
        var facing = s.FacingRight ? Character.LookingDirection.Right : Character.LookingDirection.Left;
        if (Character.lookingDirection != facing)
            Character.lookingDirection = facing;
        StandingGround.Update(Character, s.Grounded); // enemies here read the copy's platform (Leiana's orbs)
        // What Movement writes every frame (Movement.cs), here from the owner's state.
        var p = Character.animationController.parameter;
        float speedX = Math.Abs(s.Velocity.X);
        p.grounded = s.Grounded;
        p.walk = s.Grounded && speedX > 0.1f;
        p.movementSpeed = speedX * 0.25f;
        p.ySpeed = s.Grounded ? 0f : s.Velocity.Y;
        if (s.MaxHealth > 0 && (s.Health != _health || s.MaxHealth != _maxHealth))
        {
            _health = s.Health;
            _maxHealth = s.MaxHealth;
            Character.health.SetHealth(Math.Max(1f, s.Health), s.MaxHealth);
        }
    }

    // ------------------------------------------------------------------ gear (skulls; items and quintessence: later)

    private Loadout _wanted, _applied;
    private bool _applying;

    /// <summary>Gives the copy the teammate's skulls (loaded by name like the save loader does), their skills and
    /// the current one. Runs as a coroutine on <paramref name="host"/>; a newer loadout while loading wins.</summary>
    /// <summary>
    /// A copy's animation list must not hold a destroyed animation: the game's CharacterAnimationController.Update sets
    /// every listed animation's speed each frame, and a dead one threw ~55 times a second (n3 --essences 2026-11-21,
    /// 10 083 errors: the Harpy quintessence's instance was destroyed under the copy with its animations listed, and
    /// the copy's next swap failed in StopAll). The cause is fixed in TeammateEssences.Discard; this catches any other
    /// way a dead one gets in (a few list entries, every state).
    /// </summary>
    // REVIEW(fable): a guard against a symptom (trigger 6), besides the Discard fix. COOP_LOG 64h.
    internal static void DropDeadAnimations(Characters.Character character)
    {
        var list = character != null && character.animationController != null ? character.animationController.animations : null;
        if (list == null)
            return;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null || list[i]._animator == null)
                list.RemoveAt(i);
        }
    }

    public void ApplyLoadout(Loadout loadout, MonoBehaviour host)
    {
        _wanted = loadout;
        if (!_applying && Alive && host != null && !loadout.Equals(_applied))
            host.StartCoroutine(CApplyLoadout());
    }

    private System.Collections.IEnumerator CApplyLoadout()
    {
        _applying = true;
        try
        {
            while (Alive && _wanted != null && !_wanted.Equals(_applied))
            {
                var target = _wanted;
                var inventory = Character.playerComponents?.inventory?.weapon;
                if (inventory == null)
                    yield break;
                for (int slot = 0; slot < Loadout.Slots && slot < inventory.weapons.Length; slot++)
                {
                    string name = target.Weapons[slot] ?? "";
                    var have = inventory.weapons[slot];
                    if (name.Length == 0 || (have != null && Loadout.GearName(have.name) == name))
                        continue;
                    var reference = GearResource.instance?.weapons?.FirstOrDefault(r => r != null && r.name == name);
                    if (reference == null)
                    {
                        Plugin.Log.LogWarning($"[Coop] teammate skull {name} not found in this game (DLC?)");
                        continue;
                    }
                    var request = reference.LoadAsync();
                    while (!request.isDone)
                        yield return null;
                    if (!Alive)
                        yield break;
                    var weapon = Singletons.Singleton<Services.Service>.Instance?.levelManager?.DropWeapon(request, Character.transform.position);
                    if (weapon == null)
                        continue;
                    CoopCopies.Disown(weapon);
                    if (inventory.weapons[slot] != null)
                        CoopCopies.Disown(inventory.weapons[slot]);
                    inventory.ForceEquipAt(weapon, slot);
                }
                for (int slot = 0; slot < Loadout.Slots && slot < inventory.weapons.Length; slot++)
                {
                    var weapon = inventory.weapons[slot];
                    var skills = target.Skills[slot];
                    if (weapon != null && skills != null && skills.Length > 0
                        && !weapon.currentSkills.Select(k => k.key).SequenceEqual(skills))
                        weapon.SetSkills(skills, ignoreLevel: false);
                }
                if (target.Current != inventory.currentIndex && target.Current < inventory.weapons.Length
                    && inventory.weapons[target.Current] != null)
                    inventory.ChangeWeapon(target.Current); // no swap effect: the swap move comes as its own motion
                _applied = target;
            }
        }
        finally
        {
            _applying = false;
        }
    }

    // ------------------------------------------------------------------ moves

    /// <summary>An action's path below its character, each name without "(Clone)": the same on every game for the
    /// same skull (the key of a motion event).</summary>
    public static string PathOf(Transform transform, Transform root)
    {
        var names = new System.Collections.Generic.List<string>();
        for (var t = transform; t != null && t != root; t = t.parent)
            names.Add(Loadout.GearName(t.name));
        names.Reverse();
        return string.Join("/", names);
    }

    private string _missingLogged = "";

    /// <summary>Plays the teammate's move on the copy: the same motion of the same action at the same speed. Its
    /// operations run (projectiles, summons, effects); their hits do nothing here (CoopCopies).</summary>
    public void PlayMotion(string actionPath, int motionIndex, float speed, bool nonblock = false)
    {
        if (!Alive || !_shown)
            return;
        SwapFirst(actionPath);
        if (!Play(Character, actionPath, motionIndex, speed, nonblock) && _missingLogged != actionPath)
        {
            _missingLogged = actionPath;
            Plugin.Log.LogInfo($"[Coop] teammate move not found on the copy: {actionPath}");
        }
    }

    /// <summary>
    /// A move of the copy's other skull (the swap move of the skull swapped to) is played on that skull: it becomes the
    /// copy's current one first. The owner's game sends the swap move at once and the new loadout (which skull is
    /// current) up to 0.25 s later, so the swap move played on the still inactive skull, where its operations can't
    /// start: the Gambler's slot machine never showed (Umut 2026-11-21), "Coroutine couldn't be started because the
    /// game object is inactive" in the runs' logs.
    /// </summary>
    private void SwapFirst(string actionPath)
    {
        try
        {
            var inventory = Character.playerComponents?.inventory?.weapon;
            var action = inventory != null ? FindAction(Character, actionPath) : null;
            if (action == null)
                return;
            Characters.Gear.Weapons.Weapon weapon = null;
            for (var t = action.transform; t != null && weapon == null; t = t.parent)
                weapon = t.GetComponent<Characters.Gear.Weapons.Weapon>();
            if (weapon == null || weapon == inventory.current)
                return;
            int index = System.Array.IndexOf(inventory.weapons, weapon);
            if (index < 0)
                return;
            inventory.ChangeWeapon(index); // no swap effect of its own: the move itself is the swap
            CoopTrace.Write("teammate-swap-early", CoopWorld.CurrentRoom(), what: actionPath);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("[Coop] teammate swap not applied early: " + e.Message);
        }
    }

    /// <summary>Plays a motion on any character by its action path (teammates and host-run enemies); false when the
    /// character has no such action (another skull, another form). <paramref name="nonblock"/>: a move that doesn't
    /// take the character over (NonblockMoves).</summary>
    public static bool Play(Characters.Character character, string actionPath, int motionIndex, float speed, bool nonblock = false)
    {
        var action = FindAction(character, actionPath);
        if (action == null)
            return false;
        var motions = action.motions;
        if (motions == null || motionIndex >= motions.Length || motions[motionIndex] == null)
            return true;
        var motion = motions[motionIndex];
        motion.action = action;
        if (nonblock)
        {
            character.DoActionNonBlock(motion);
            return true;
        }
        float multiplier = motion.speed > 0f ? speed / motion.speed : 1f;
        character.DoAction(motion, multiplier);
        return true;
    }

    /// <summary>The local player's moves that don't take the character over (the Alchemist's flask throw:
    /// `Action.DoActionNonBlock`) start no motion event; they are sent from here (COOP_LOG 47g).</summary>
    [HarmonyLib.HarmonyPatch(typeof(Characters.Character), nameof(Characters.Character.DoActionNonBlock))]
    public static class NonblockMoves
    {
        private static void Postfix(Characters.Character __instance, Characters.Actions.Motion motion)
        {
            if (__instance != null && __instance.type == Characters.Character.Type.Player && !CoopCopies.IsCopy(__instance))
                CoopRuntime.Instance?.OnNonblockMotion(__instance, motion);
        }
    }

    // Found actions per character: building every action's path for every replayed move was a string list per action
    // (a boss has 50+ actions, a fight replays several moves a second).
    private static readonly System.Collections.Generic.Dictionary<Characters.Character,
        System.Collections.Generic.Dictionary<string, Characters.Actions.Action>> FoundActions = new();

    /// <summary>A character's action by its path below the character, or null.</summary>
    public static Characters.Actions.Action FindAction(Characters.Character character, string actionPath)
    {
        if (!FoundActions.TryGetValue(character, out var found))
        {
            if (FoundActions.Count > 300)
                FoundActions.Clear(); // enemies of rooms long gone
            FoundActions[character] = found = new System.Collections.Generic.Dictionary<string, Characters.Actions.Action>(System.StringComparer.Ordinal);
        }
        // still the character's own (a skull swap replaces a player's actions)
        if (found.TryGetValue(actionPath, out var known) && known != null && character.actions.Contains(known))
            return known;
        foreach (var action in character.actions)
            if (action != null && PathOf(action.transform, character.transform) == actionPath)
            {
                found[actionPath] = action;
                return action;
            }
        return null;
    }

    public void Destroy()
    {
        if (Character != null)
        {
            CoopCopies.Unregister(Character);
            Object.Destroy(Character.gameObject);
        }
        Character = null;
    }
}
