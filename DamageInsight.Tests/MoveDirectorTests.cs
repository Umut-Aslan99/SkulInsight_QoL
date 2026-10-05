using System.Collections.Generic;
using System.Linq;
using DamageInsight.Codex;
using Xunit;
using Kind = DamageInsight.Codex.MoveDirector.Kind;

namespace DamageInsight.Tests;

/// <summary>
/// The move director's way planning (Developer/PreferNewMoves) on a small tree shaped like Dark Skul's phase 2: a form
/// value set by a form change, form branches checking it, far/close branches checking the distance.
/// </summary>
public class MoveDirectorTests
{
    /// <summary>A tree of numbered nodes; State checks compare one value (the form) with a number, Setters set it.</summary>
    private sealed class Tree : MoveDirector.ITree
    {
        private readonly List<int> _parent = new();
        private readonly List<Kind> _kind = new();
        private readonly List<List<int>> _children = new();
        private readonly Dictionary<int, int> _checks = new(), _sets = new(), _guards = new(), _slots = new();
        public int Form;

        public int Add(Kind kind, int parent = -1)
        {
            _parent.Add(parent);
            _kind.Add(kind);
            _children.Add(new List<int>());
            int node = _parent.Count - 1;
            if (parent >= 0)
                _children[parent].Add(node);
            return node;
        }

        public int Check(int form, int parent)
        {
            int node = Add(Kind.State, parent);
            _checks[node] = form;
            return node;
        }

        public int Set(int form, int parent)
        {
            int node = Add(Kind.Setter, parent);
            _sets[node] = form;
            return node;
        }

        public int Gate(Kind guard, int parent)
        {
            int node = Add(Kind.Gate, parent);
            _guards[node] = Add(guard);
            return node;
        }

        public void Spend(int node) => _kind[node] = Kind.Spent;
        public void SetSlot(int node, int slot) => _slots[node] = slot;
        public int Parent(int node) => _parent[node];
        public int Slot(int node) =>
            _slots.TryGetValue(node, out int slot) ? slot : _parent[node] < 0 ? -1 : _children[_parent[node]].IndexOf(node);
        public IReadOnlyList<int> Children(int node) => _children[node];
        public Kind KindOf(int node) => _kind[node];
        public int Guard(int node) => _guards.TryGetValue(node, out int g) ? g : -1;
        public bool Holds(int state) => Form == _checks[state];
        public bool Makes(int setter, int state, bool pass) => (_sets[setter] == _checks[state]) == pass;
        public IEnumerable<int> Setters => _sets.Keys;
    }

    // Dark Skul 2 in small: root → top selector [form change, patterns]; balance = form 0, power = form 1.
    private readonly Tree _t = new();
    private readonly int _top, _change, _changeCooldown, _intro, _toBalance, _toBalanceSet, _typeChange, _toPowerSet;
    private readonly int _patterns, _balanceForm, _balanceSplit, _far, _farDistance, _farPick, _dash, _headHunting, _headHuntingDistance;
    private readonly int _close, _closePick, _melee, _swing, _powerForm, _punch;

    public MoveDirectorTests()
    {
        int root = _t.Add(Kind.Sequence);
        _top = _t.Add(Kind.Selector, root);
        _change = _t.Add(Kind.Sequence, _top);
        _changeCooldown = _t.Add(Kind.Force, _change);
        _t.Add(Kind.Selector, _change);                                   // outro of the current form: as the game does
        _intro = _t.Add(Kind.Selector, _change);
        _toBalance = _t.Add(Kind.Sequence, _intro);
        _toBalanceSet = _t.Set(0, _toBalance);
        _t.Add(Kind.Other, _toBalance);                                   // "To balance switching"
        _typeChange = _t.Add(Kind.PickOne, _intro);
        _t.Set(0, _t.Add(Kind.Sequence, _typeChange));
        _toPowerSet = _t.Set(1, _t.Add(Kind.Sequence, _typeChange));
        _patterns = _t.Add(Kind.Selector, _top);
        _balanceForm = _t.Add(Kind.Sequence, _patterns);
        _t.Check(0, _balanceForm);
        _balanceSplit = _t.Add(Kind.Selector, _balanceForm);
        _far = _t.Add(Kind.Sequence, _balanceSplit);
        _farDistance = _t.Add(Kind.Force, _far);
        _farPick = _t.Add(Kind.PickOne, _far);
        _dash = _t.Add(Kind.Other, _farPick);
        _headHunting = _t.Add(Kind.Sequence, _farPick);
        _headHuntingDistance = _t.Add(Kind.Force, _headHunting);
        _t.Add(Kind.Other, _headHunting);
        _close = _t.Add(Kind.Sequence, _balanceSplit);
        _closePick = _t.Add(Kind.PickOne, _close);
        _melee = _t.Add(Kind.Other, _closePick);
        _swing = _t.Add(Kind.Other, _closePick);
        _powerForm = _t.Add(Kind.Sequence, _patterns);
        _t.Check(1, _powerForm);
        _punch = _t.Add(Kind.Other, _powerForm);
    }

    [Fact]
    public void A_move_of_the_current_form_is_reached_directly()
    {
        var plan = MoveDirector.PlanTo(_t, _swing);
        Assert.NotNull(plan);
        Assert.Equal(0, plan.Hops);
        Assert.Equal(_swing, plan.Goal);
        Assert.Equal(1, plan.Child[_top]);
        Assert.Equal(0, plan.Child[_patterns]);
        Assert.Equal(1, plan.Child[_balanceSplit]);
        Assert.Equal(1, plan.Child[_closePick]);
        Assert.Empty(plan.Force);
    }

    [Fact]
    public void Gating_checks_on_the_way_and_the_moves_own_leading_checks_are_passed()
    {
        var plan = MoveDirector.PlanTo(_t, _headHunting);
        Assert.True(plan.Force[_farDistance]);
        Assert.True(plan.Force[_headHuntingDistance]);
        Assert.Equal(0, plan.Child[_balanceSplit]);
        Assert.Equal(1, plan.Child[_farPick]);
        Assert.Equal(0, MoveDirector.PlanTo(_t, _dash).Child[_farPick]);
    }

    [Fact]
    public void A_move_of_another_form_goes_through_the_form_change_first()
    {
        _t.Form = 1;
        var plan = MoveDirector.PlanTo(_t, _melee);
        Assert.Equal(1, plan.Hops);
        Assert.Equal(_toBalanceSet, plan.Goal);
        Assert.Equal(0, plan.Child[_top]);
        Assert.Equal(0, plan.Child[_intro]);
        Assert.True(plan.Force[_changeCooldown]);
        // In the power form, its own moves need no change.
        Assert.Equal(0, MoveDirector.PlanTo(_t, _punch).Hops);
        Assert.Equal(1, MoveDirector.PlanTo(_t, _punch).Child[_patterns]);
    }

    [Fact]
    public void A_spent_branch_is_avoided_and_without_any_setter_there_is_no_way()
    {
        _t.Form = 1;
        _t.Spend(_toBalance);
        var plan = MoveDirector.PlanTo(_t, _melee);
        Assert.Equal(_typeChange, _t.Parent(_t.Parent(plan.Goal)));     // the random form change's "to balance"
        Assert.Equal(1, plan.Child[_intro]);
        Assert.Equal(0, plan.Child[_typeChange]);

        Assert.Null(MoveDirector.PlanTo(_t, _melee, maxHops: 0));
        _t.Form = 0;
        _t.Spend(_dash);                                                   // a move that ran its only time
        Assert.Null(MoveDirector.PlanTo(_t, _dash));
        var noSetter = new Tree();
        int seq = noSetter.Add(Kind.Sequence);
        noSetter.Check(5, seq);
        int move = noSetter.Add(Kind.Other, seq);
        Assert.Null(MoveDirector.PlanTo(noSetter, move));
    }

    [Fact]
    public void Inverted_checks_must_fail_and_gates_must_pass()
    {
        var t = new Tree();
        int seq = t.Add(Kind.Sequence);
        int inverter = t.Add(Kind.Inverter, seq);
        int check = t.Add(Kind.Force, inverter);
        int natural = t.Add(Kind.Other, seq);                              // grabbed, head thrown: never forced
        int gate = t.Gate(Kind.Force, seq);
        int move = t.Add(Kind.Other, gate);
        var plan = MoveDirector.PlanTo(t, move);
        Assert.False(plan.Force[check]);
        Assert.True(plan.Force[t.Guard(gate)]);
        Assert.False(plan.Force.ContainsKey(natural));
    }

    [Fact]
    public void Blocks_wrappers_pass_their_childs_checks_and_siblings_count_by_their_slot_in_the_game()
    {
        // Pope-like blocks: Sequence [Info → Conditional(cooldown) → Idle, Info → Chance → Consecration]; a sibling met
        // elsewhere first is missing from this list, so the slot (the game's index), not the list position, counts.
        var t = new Tree();
        int seq = t.Add(Kind.Sequence);
        int info = t.Add(Kind.Pass, seq);
        int conditional = t.Gate(Kind.Force, info);
        t.Add(Kind.Other, conditional);
        int info2 = t.Add(Kind.Pass, seq);
        int chance = t.Gate(Kind.Force, info2);
        int move = t.Add(Kind.Other, chance);
        var plan = MoveDirector.PlanTo(t, info2);                         // the attack is the BehaviourInfo
        Assert.True(plan.Force[t.Guard(conditional)]);
        Assert.True(plan.Force[t.Guard(chance)]);                          // the move's own gate, behind its wrapper
        Assert.True(MoveDirector.PlanTo(t, move).Force[t.Guard(chance)]);

        // Game order [shared block (listed elsewhere), check, move, later check]: only the check before the move counts.
        var s = new Tree();
        int sequence = s.Add(Kind.Sequence);
        int before = s.Add(Kind.Force, sequence);
        int target = s.Add(Kind.Other, sequence);
        int after = s.Add(Kind.Force, sequence);
        s.SetSlot(before, 1);
        s.SetSlot(target, 2);
        s.SetSlot(after, 3);
        var way = MoveDirector.PlanTo(s, target);
        Assert.True(way.Force[before]);
        Assert.False(way.Force.ContainsKey(after));
    }

    [Fact]
    public void The_next_move_is_the_first_without_a_form_change_else_the_one_with_the_fewest()
    {
        _t.Form = 1;
        var ways = new Dictionary<string, int> { ["Melee"] = _melee, ["Swing"] = _swing, ["Punch"] = _punch };
        MoveDirector.Plan Way(string m) => ways.TryGetValue(m, out int n) ? MoveDirector.PlanTo(_t, n) : null;

        Assert.Equal("Punch", MoveDirector.Choose(new[] { "Melee", "Unknown", "Punch", "Swing" }, Way).move);
        var (move, plan) = MoveDirector.Choose(new[] { "Unknown", "Swing", "Melee" }, Way);
        Assert.Equal("Swing", move);
        Assert.Equal(_toBalanceSet, plan.Goal);
        Assert.Null(MoveDirector.Choose(new[] { "Unknown" }, Way).move);
        Assert.Null(MoveDirector.Choose(Enumerable.Empty<string>(), Way).plan);
    }
}
