using System;
using System.Collections.Generic;

namespace DamageInsight.Codex;

/// <summary>
/// Developer/PreferNewMoves: bosses do the moves the Codex still misses one after another. For bosses run by a tree
/// (BehaviorDesigner: Dark Skul, First Dark Hero, King Alexander, adventurers; behaviour blocks: Yggdrasil, Pope, First
/// Hero) the way from the tree's root to the next missing move is planned and the tree is led along it:
/// <list type="bullet">
/// <item>selectors on the way take the branch towards the move (the branches before it are skipped);</item>
/// <item>checks that only gate the way (distance, wall, HP, cooldown, chance) are made to pass;</item>
/// <item>a check on a value the tree sets itself (Dark Skul's form, First Dark Hero's step) is reached by first going to
/// the node that sets it: the boss changes its form, then does the move;</item>
/// <item>checks on things that must really happen (grabbed the player, head thrown, skill ready) are left to the game:
/// the tree then does what it does instead (throws its head first), and a move that never comes is put aside.</item>
/// </list>
/// Bosses run by their own code (Leiana sisters, Awakened Leiana, Chimera) have no tree to lead: when they start a move
/// that is filmed already, a missing move of the same kind starts instead (MoveDirector.Coroutines).
/// Plain logic on an abstract tree (<see cref="ITree"/>); the live part reads the running trees (developer builds).
/// </summary>
public static partial class MoveDirector
{
    public enum Kind
    {
        Other,      // actions, waits, checks that must really happen: as the game decides
        Selector,   // tries its children in order: starts at the child on the way
        PickOne,    // runs the one child it picks (WeightedSelector, OneByOneSelector): picks the child on the way
        Sequence,   // runs its children while they succeed: the checks before the child on the way must pass
        Inverter,   // flips its child's result
        Pass,       // a wrapper whose result is its child's (a block's BehaviourInfo)
        Gate,       // runs its child only if its own check passes (ConditionalEvaluator, see Guard)
        Spent,      // no way through: a RunOnlyOnce that ran, a branch the designers weighted 0
        Force,      // a check that may be made to pass or fail
        State,      // a check on a value the tree sets itself
        Setter,     // sets such a value
    }

    /// <summary>A tree as the director sees it: nodes are numbers, the root has no parent.</summary>
    public interface ITree
    {
        int Parent(int node);
        int Slot(int node);                                 // its index in its parent's list of children in the game
        IReadOnlyList<int> Children(int node);
        Kind KindOf(int node);
        int Guard(int node);                                // a Gate's check (a node without parent), else -1
        bool Holds(int state);                              // whether a State check passes now
        bool Makes(int setter, int state, bool pass);       // whether running the setter makes the check pass (fail)
        IEnumerable<int> Setters { get; }
    }

    /// <summary>The way to one node: what the selectors on it take and what the checks on it return.</summary>
    public sealed class Plan
    {
        public ITree Tree;
        public int Goal;                                    // the move, or a setter to run first
        public int Hops;                                    // setters on the way (form changes)
        public int Waiting = -1;                            // with Hops: the check the setter makes pass
        public readonly Dictionary<int, int> Child = new(); // Selector/PickOne → the child to take
        public readonly Dictionary<int, bool> Force = new(); // Force check → its result
    }

    /// <summary>
    /// The way from the root to <paramref name="target"/>, or, when a check on a set value blocks it, the way to a setter
    /// that unblocks it (at most <paramref name="maxHops"/> in a row). Null: no way now (a spent branch, nothing sets the
    /// value). A move that is a sequence or gate gets its own leading checks passed too ("Head hunting": far away first).
    /// </summary>
    public static Plan PlanTo(ITree tree, int target, int maxHops = 2)
    {
        var plan = new Plan { Tree = tree, Goal = target };
        int blocked = -1;
        bool need = true;

        void Require(int node, bool pass, int depth)
        {
            if (node < 0 || depth > 8)
                return;
            switch (tree.KindOf(node))
            {
                case Kind.Force:
                    plan.Force[node] = pass;
                    break;
                case Kind.State when blocked < 0 && tree.Holds(node) != pass:
                    blocked = node;
                    need = pass;
                    break;
                case Kind.Inverter when tree.Children(node).Count > 0:
                    Require(tree.Children(node)[0], !pass, depth + 1);
                    break;
                case Kind.Pass when tree.Children(node).Count > 0:
                    Require(tree.Children(node)[0], pass, depth + 1);
                    break;
                case Kind.Gate:
                    Require(tree.Guard(node), pass, depth + 1);
                    break;
            }
        }

        int own = target;
        while (tree.KindOf(own) == Kind.Pass && tree.Children(own).Count > 0)
            own = tree.Children(own)[0];
        switch (tree.KindOf(own))
        {
            case Kind.Sequence:
                foreach (int child in tree.Children(own))
                    Require(child, true, 0);
                break;
            case Kind.Gate:
                Require(tree.Guard(own), true, 0);
                break;
        }
        for (int node = target, parent; ; node = parent)
        {
            if (tree.KindOf(node) == Kind.Spent)
                return null;
            if ((parent = tree.Parent(node)) < 0)
                break;
            int slot = tree.Slot(node);
            switch (tree.KindOf(parent))
            {
                case Kind.Selector or Kind.PickOne:
                    plan.Child[parent] = slot;
                    break;
                case Kind.Sequence:
                    foreach (int sibling in tree.Children(parent))
                        if (tree.Slot(sibling) < slot)
                            Require(sibling, true, 0);
                    break;
                case Kind.Gate:
                    Require(tree.Guard(parent), true, 0);
                    break;
            }
        }
        if (blocked < 0)
            return plan;
        if (maxHops <= 0)
            return null;
        Plan best = null;
        foreach (int setter in tree.Setters)
            if (tree.Makes(setter, blocked, need) && PlanTo(tree, setter, maxHops - 1) is { } via && (best == null || via.Hops < best.Hops))
                best = via;
        if (best == null)
            return null;
        best.Hops++;
        best.Waiting = blocked;
        return best;
    }

    /// <summary>
    /// The move to go for next: the first one (in the book's order) with a way and the fewest form changes on it, so
    /// the moves of the current form come first. (null, null) when none of them has a way now.
    /// </summary>
    public static (string move, Plan plan) Choose(IEnumerable<string> missing, Func<string, Plan> planFor)
    {
        string bestMove = null;
        Plan best = null;
        foreach (var move in missing)
        {
            if (planFor(move) is not { } plan || best != null && plan.Hops >= best.Hops)
                continue;
            (bestMove, best) = (move, plan);
            if (plan.Hops == 0)
                break;
        }
        return (bestMove, best);
    }
}
