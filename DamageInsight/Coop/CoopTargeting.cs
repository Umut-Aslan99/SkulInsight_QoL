using System;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using Characters.AI;
using HarmonyLib;
using Level;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Who enemies go for in co-op (COOP_PLAN §4.1, §6; GAME_FLOW hooks 4-5):
/// - `AIController.FindClosestPlayerBody`: the game's loop starts at the second candidate and never compares the
///   first one's distance (with one player it doesn't matter); co-op picks the really closest player that isn't down.
/// - The preloader's resolvers (package E): code that reads "the player" gets the right one. Owner reads (gear and
///   abilities): a teammate copy's code gets the copy, an enemy's code its target. Target reads (enemy code): the
///   enemy's target. Anything else, or no co-op: this game's player, as without co-op.
/// </summary>
public static class CoopTargeting
{
    /// <summary>A player who is down (revive, step 4) is never picked. Set by the revive rules.</summary>
    public static Func<Character, bool> IsDown = _ => false;

    private static bool Active => CoopWorld.Current?.Active == true;

    [HarmonyPatch(typeof(AIController), nameof(AIController.FindClosestPlayerBody), new[] { typeof(Collider2D) })]
    public static class ClosestPlayer
    {
        private static bool Prefix(AIController __instance, Collider2D collider, ref Character __result)
        {
            if (!Active)
                return true;
            collider.enabled = true;
            var targets = AIController._playerOverlapper.OverlapCollider(collider).GetComponents<Target>();
            collider.enabled = false;
            Character best = null;
            float bestDistance = float.MaxValue;
            foreach (var target in targets)
            {
                var c = target?.character;
                if (c == null || c.health == null || c.health.dead || IsDown(c))
                    continue;
                float distance = Physics2D.Distance(c.collider, __instance.character.collider).distance;
                if (distance < bestDistance)
                {
                    best = c;
                    bestDistance = distance;
                }
            }
            __result = best;
            return false;
        }
    }

    // ------------------------------------------------------------------ the preloader's resolvers

    /// <summary>Owner kind: whose gear or ability runs (a teammate copy's → the copy; an enemy's → its target).</summary>
    public static Character ResolveOwner(LevelManager manager, object self, int siteId)
    {
        var player = manager.player;
        if (!Active || self == null)
            return player;
        try
        {
            var c = CharacterOf(self);
            if (c == null)
                return Traced("Owner", self, null, siteId, player, false);
            if (CoopCopies.BelongsToCopy(c))
                return LeaderOf(c);
            if (!IsEnemy(c))
                return player;
            var target = TargetOf(c);
            return Traced("Owner", self, c, siteId, target ?? Standing(c, player), target == null);
        }
        catch (Exception e)
        {
            Failed(siteId, e);
            return player; // inside the game's code: never throw, fall back to the game's own answer
        }
    }

    /// <summary>Target kind: enemy code aims at the enemy's target.</summary>
    public static Character ResolveTarget(LevelManager manager, object self, int siteId)
    {
        var player = manager.player;
        if (!Active || self == null)
            return player;
        try
        {
            var c = CharacterOf(self);
            if (c == null)
                return Traced("Target", self, null, siteId, player, false);
            if (!IsEnemy(c))
                return player;
            var target = TargetOf(c);
            return Traced("Target", self, c, siteId, target ?? Standing(c, player), target == null);
        }
        catch (Exception e)
        {
            Failed(siteId, e);
            return player;
        }
    }

    // ------------------------------------------------------------------ trace (test copies only)

    private static readonly HashSet<(Type, int)> TracedMisses = new();
    private static readonly Dictionary<(Character, int), int> TracedAnswers = new();
    private static uint _tracedRoom;

    /// <summary>
    /// Writes what a resolver answered (CoopTrace, test copies only) and returns <paramref name="answer"/>:
    /// "resolve-miss" once per code type and read site whose character wasn't found (the game's player is used there,
    /// wrong for an enemy aiming at a teammate); "resolve" when the answer for an enemy and read site changes (slot of
    /// the answer; tools/coop/compare-trace.py checks the client's puppets answer what the host's enemies did).
    /// </summary>
    private static Character Traced(string kind, object self, Character enemy, int siteId, Character answer, bool noTarget)
    {
        if (!CoopTrace.On)
            return answer;
        uint room = EnemySync.TraceRoom;
        if (room != _tracedRoom)
        {
            _tracedRoom = room;
            TracedAnswers.Clear();
        }
        if (enemy == null)
        {
            if (TracedMisses.Add((self.GetType(), siteId)))
                CoopTrace.Write("resolve-miss", room, what: $"{kind} #{siteId} {self.GetType().FullName}");
            return answer;
        }
        int slot = EnemySync.TraceSlotOf(answer);
        if (!TracedAnswers.TryGetValue((enemy, siteId), out int last) || last != slot)
        {
            TracedAnswers[(enemy, siteId)] = slot;
            CoopTrace.Write("resolve", room, EnemySync.TraceIdOf(enemy), CoopTrace.NameOf(enemy),
                $"{kind} #{siteId} {self.GetType().Name}" + (noTarget ? " no target" : ""), slot: slot);
        }
        return answer;
    }

    private static readonly HashSet<int> FailedSites = new();

    private static void Failed(int siteId, Exception e)
    {
        if (FailedSites.Add(siteId))
            Plugin.Log.LogWarning($"[Coop] player read #{siteId}: the resolver failed ({e.Message}); this game's player is used there");
    }

    private static bool IsEnemy(Character c) => c.type is Character.Type.TrashMob or Character.Type.Named
        or Character.Type.Adventurer or Character.Type.Boss or Character.Type.Summoned;

    private static Character LeaderOf(Character c)
    {
        if (CoopCopies.IsCopy(c))
            return c;
        var minion = c.GetComponent<Minion>();
        return minion?.leader?.player ?? c;
    }

    /// <summary>Where an enemy keeps its target: its AIController's `target`, or (enemies run by a BehaviorDesigner
    /// tree, no AIController) the tree's shared "Target" variable, which their moves read (`ToBDTarget` and the like).</summary>
    private sealed class TargetSource
    {
        public AIController Ai;
        public BehaviorDesigner.Runtime.Behavior Tree;

        // read each time: the tree may swap the variable object (SetVariable)
        public BehaviorDesigner.Runtime.SharedCharacter TreeTarget =>
            Tree != null ? Tree.GetVariable(TreeTargetName) as BehaviorDesigner.Runtime.SharedCharacter : null;
    }

    private const string TreeTargetName = "Target";
    private static readonly Dictionary<Character, TargetSource> Sources = new();

    private static TargetSource SourceOf(Character enemy)
    {
        if (Sources.TryGetValue(enemy, out var source))
            return source;
        if (Sources.Count > 2000)
            Sources.Clear();
        source = new TargetSource
        {
            Ai = enemy.GetComponentInChildren<AIController>(true) ?? enemy.GetComponentInParent<AIController>(),
        };
        // An AI that sits BESIDE the character under the Enemy container (not above or below it): the one that runs
        // this character. Without it the target was unknown, the host's moves went out without a target and a
        // client's puppet aimed at its own player.
        var container = source.Ai == null ? enemy.GetComponentInParent<Enemy>() : null;
        if (container != null)
        {
            AIController only = null;
            int count = 0;
            foreach (var ai in container.GetComponentsInChildren<AIController>(true))
            {
                if (ai == null)
                    continue;
                if (ai.character == enemy)
                {
                    source.Ai = ai;
                    break;
                }
                only = ai;
                count++;
            }
            if (source.Ai == null && count == 1 && only.character == null)
                source.Ai = only;
        }
        if (source.Ai == null)
        {
            var trees = enemy.GetComponentsInChildren<BehaviorDesigner.Runtime.Behavior>(true);
            if (trees.Length == 0)
                trees = enemy.GetComponentsInParent<BehaviorDesigner.Runtime.Behavior>(true);
            if (trees.Length == 0 && container?.behaviors != null)
            {
                trees = new BehaviorDesigner.Runtime.Behavior[container.behaviors.Count];
                container.behaviors.CopyTo(trees, 0);
            }
            foreach (var tree in trees)
                if (tree != null && tree.GetVariable(TreeTargetName) is BehaviorDesigner.Runtime.SharedCharacter)
                {
                    source.Tree = tree;
                    break;
                }
        }
        Sources[enemy] = source;
        return source;
    }

    /// <summary>The enemy's current target (a player or a teammate copy), or null.</summary>
    public static Character TargetOf(Character enemy)
    {
        var target = RawTargetOf(enemy);
        return Up(target) ? target : null;
    }

    /// <summary>What the enemy keeps as its target, also when that one is down or dead (null: none).</summary>
    public static Character RawTargetOf(Character enemy)
    {
        var source = SourceOf(enemy);
        return source.Ai != null ? source.Ai.target : source.TreeTarget?.Value;
    }

    /// <summary>A player (or copy) that can be fought: alive and not down.</summary>
    public static bool Up(Character c) => c != null && c.health != null && !c.health.dead && !IsDown(c);

    /// <summary>
    /// Enemy code without a target of its own asks for "the player": the standing player nearest to the enemy, this
    /// game's player or a teammate's copy, kept while it isn't much farther (<see cref="Rules.NoTargetPick"/>); nobody
    /// standing: this game's player still. History: always this game's player at first; then (COOP_LOG 65, Dark Skul's
    /// second form keeps no target and hit the downed host's body for 54 s) this game's player while it stood, else the
    /// nearest copy; now the nearest (COOP_LOG 68: the DM chapter 4 Veteran Thief started with no target, took the host
    /// outside the arena and never came to the client inside it). The game code usually stores the answer as the
    /// enemy's target (an adventurer's start, `CPlayStartOption`), which the host then sends to the clients.
    /// </summary>
    // REVIEW(fable): host/client authority of who an enemy without a target goes for (trigger 2); from the traces, not yet run.
    internal static Character Standing(Character enemy, Character player)
    {
        if (player == null || CoopCopies.Count == 0)
            return player;
        Vector2 at = enemy != null ? (Vector2)enemy.transform.position : (Vector2)player.transform.position;
        StandingCandidates.Clear();
        StandingDistances.Clear();
        AddCandidate(player, at); // first: ties go to this game's player
        foreach (var copy in CoopCopies.All)
            if (copy != null && copy.gameObject.activeInHierarchy)
                AddCandidate(copy, at);
        int last = enemy != null && LastStanding.TryGetValue(enemy, out var before) ? StandingCandidates.IndexOf(before) : -1;
        int pick = Rules.NoTargetPick.Pick(StandingDistances, last);
        if (pick < 0)
            return player;
        var chosen = StandingCandidates[pick];
        if (enemy != null)
        {
            if (LastStanding.Count > 500)
                LastStanding.Clear();
            LastStanding[enemy] = chosen;
        }
        return chosen;
    }

    private static readonly List<Character> StandingCandidates = new();
    private static readonly List<float> StandingDistances = new();
    private static readonly Dictionary<Character, Character> LastStanding = new();

    private static void AddCandidate(Character c, Vector2 at)
    {
        StandingCandidates.Add(c);
        StandingDistances.Add(Up(c) ? Vector2.Distance(at, c.transform.position) : -1f);
    }

    /// <summary>Sets an enemy's target (a client's puppet: the host's choice).</summary>
    public static void SetTarget(Character enemy, Character target)
    {
        var source = SourceOf(enemy);
        if (source.Ai != null)
            source.Ai.target = target;
        else if (source.TreeTarget != null)
            source.TreeTarget.Value = target;
    }

    // `self` of a rewritten read: a component, an ability instance, an operation, or a compiler-made closure or
    // coroutine (its `<>4__this` leads to the real one). The way to its character is found once per type.
    private static readonly Dictionary<Type, Func<object, object>> Steps = new();

    public static Character CharacterOf(object self)
    {
        for (int depth = 0; self != null && depth < 4; depth++)
        {
            switch (self)
            {
                case Character c:
                    return c;
                case Component component:
                    return OwnerOf(component);
            }
            var type = self.GetType();
            if (!Steps.TryGetValue(type, out var step))
                Steps[type] = step = StepFor(type);
            if (step == null)
                return null;
            self = step(self);
        }
        return null;
    }

    /// <summary>A component's character: above it, or (an enemy's AI and its spells sit beside the character, not
    /// under it: a thorn treant aimed at the wrong player on a client) its AI's character or its Enemy container's.</summary>
    private static Character OwnerOf(Component component)
    {
        // kept per component: every rewritten player read of every enemy asked this, a walk up the hierarchy each
        if (Owners.TryGetValue(component, out var known) && known != null)
            return known;
        if (Owners.Count > 4000)
            Owners.Clear();
        var owner = FindOwner(component);
        if (owner != null)
            Owners[component] = owner;
        return owner;
    }

    private static readonly Dictionary<Component, Character> Owners = new();

    private static Character FindOwner(Component component)
    {
        var c = component.GetComponentInParent<Character>();
        if (c != null)
            return c;
        var ai = component.GetComponentInParent<AIController>();
        if (ai != null && ai.character != null)
            return ai.character;
        var enemy = component.GetComponentInParent<Enemy>();
        if (enemy?.characters != null)
            foreach (var first in enemy.characters)
                if (first != null)
                    return first;
        return null;
    }

    private static Func<object, object> StepFor(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string name in new[] { "<>4__this", "owner", "_owner", "character", "_character" })
        {
            var field = type.GetField(name, all);
            if (field != null && (name == "<>4__this" || typeof(Character).IsAssignableFrom(field.FieldType)))
                return field.GetValue;
            var property = type.GetProperty(name, all);
            if (property != null && property.GetIndexParameters().Length == 0 && typeof(Character).IsAssignableFrom(property.PropertyType))
                return o => property.GetValue(o);
        }
        return null;
    }
}
