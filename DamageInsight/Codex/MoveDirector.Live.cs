#if DEV // a developer tool: release builds don't lead bosses
using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using UnityEngine;
using BD = BehaviorDesigner.Runtime;
using BDTasks = BehaviorDesigner.Runtime.Tasks;
using BDMath = BehaviorDesigner.Runtime.Tasks.Unity.Math;

namespace DamageInsight.Codex;

/// <summary>
/// The live side of <see cref="MoveDirector"/>: for every boss being filmed whose moves come from a tree (BehaviorDesigner
/// or behaviour blocks), the next missing move and the way to it, refreshed 4 times a second from
/// <see cref="FightRecorder"/>'s Update; for the others the attacks that may be swapped (MoveDirector.Coroutines).
/// The hooks (<see cref="Patches.PreferNewMovesPatches"/>) only look up what a selector takes and what a check returns.
/// </summary>
public static partial class MoveDirector
{
    private const float Every = 0.25f;
    private const float Budget = 30f;       // time a move gets (a form change included) before it is put aside
    private const float StandStill = 8f;    // a way that leads nowhere: the boss starts nothing meanwhile
    private const float Started = 12f;      // a move that started is filmed when it ends: not asked for again meanwhile
    private const float AsideLong = 90f, AsideShort = 15f;

    /// <summary>Checks that may be made to pass: they only gate a branch, nothing in the move depends on them.</summary>
    public static readonly Type[] ForcedTypes =
    {
        typeof(BDTasks.CompareCharacterDistance), typeof(BDTasks.CompareFromWall), typeof(BDTasks.HealthComparison),
        typeof(BDTasks.ChronometerCoolDown), typeof(BDTasks.CanUseChronometerCoolDown), typeof(BDTasks.RandomProbability),
    };

    private sealed class Led
    {
        public string Key, Target, Text, SwapText;
        public Character Boss;
        public AttackGraph Graph;
        public string Waiting;                                      // a move started: its take is waited for (Pacing)
        public float WaitUntil, HeldSince;
        public readonly List<BD.Behavior> Held = new();             // trees paused meanwhile
        public float Since;
        public Plan Plan;
        public readonly Dictionary<string, float> Aside = new();   // move → put aside until
        public readonly Dictionary<string, int> Swaps = new();     // coroutine AIs: move → times swapped in
    }

    private static readonly Dictionary<Character, Led> Bosses = new();
    private static readonly Dictionary<object, int> ChildOf = new();    // live composite → the child to take
    private static readonly Dictionary<object, bool> ForceOf = new();   // live check → its result
    private static readonly Dictionary<BD.BehaviorManager.BehaviorTree, TaskTree> Trees = new();
    private static float _next;

    /// <summary>For a selector of a led boss: the child on the way to its next move.</summary>
    public static bool ChildFor(object composite, out int child)
    {
        child = -1;
        return ChildOf.Count > 0 && ChildOf.TryGetValue(composite, out child);
    }

    /// <summary>For a check of a led boss: the result that keeps it on the way to its next move.</summary>
    public static bool Forced(object check, out bool pass)
    {
        pass = false;
        return ForceOf.Count > 0 && ForceOf.TryGetValue(check, out pass);
    }

    /// <summary>For the move counter: "Next: Melee attack (first To balance switching)", or null.</summary>
    public static string TargetText(Character boss) =>
        boss != null && Bosses.TryGetValue(boss, out var led) ? led.Text ?? led.SwapText : null;

    /// <summary>Called by FightRecorder's Update: the next moves and their ways, at most 4 times a second.</summary>
    public static void Tick()
    {
        if (!MovePicker.On)
        {
            if (Bosses.Count > 0 || ChildOf.Count > 0 || ForceOf.Count > 0)
            {
                foreach (var held in Bosses.Values)
                    Release(held);
                Bosses.Clear();
                ChildOf.Clear();
                ForceOf.Clear();
                Trees.Clear();
                Blocks.Clear();
                SwapOwners.Clear();
            }
            return;
        }
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + Every;
        Listen();
        Patches.Guard.Run("Move director", Refresh);
    }

    private static void Refresh()
    {
        var films = FightRecorder.ForDirector();
        foreach (var gone in Bosses.Keys.Where(b => b == null || films.All(f => f.Boss != b)).ToList())
        {
            Release(Bosses[gone]);
            Bosses.Remove(gone);
        }
        ChildOf.Clear();
        ForceOf.Clear();
        SwapOwners.Clear();
        foreach (var film in films)
        {
            if (!Bosses.TryGetValue(film.Boss, out var led))
                Bosses[film.Boss] = led = new Led { Key = film.Key };
            led.Boss = film.Boss;
            led.Graph = film.Graph;
            WatchCoroutines(film, led);
            if (StillWaiting(led, film))
                continue; // one move at a time: its take first
            var trees = TreesOf(film.Boss);
            if (trees.Count == 0)
                continue;
            Step(led, film, trees);
            if (led.Plan?.Tree is not ILiveTree tree)
                continue;
            foreach (var pair in led.Plan.Child)
                ChildOf[tree.Task(pair.Key)] = pair.Value;
            foreach (var pair in led.Plan.Force)
                ForceOf[tree.Task(pair.Key)] = pair.Value;
        }
        if (Trees.Count > 20)
            Trees.Clear();
    }

    /// <summary>One boss: is its move done, put aside or still on its way; if there's none, the next one.</summary>
    private static void Step(Led led, FightRecorder.Directed film, List<ILiveTree> trees)
    {
        float now = Time.unscaledTime;
        if (led.Target != null)
        {
            string why = null;
            Plan plan = null;
            if (!film.Missing.Contains(led.Target))
                why = "on film";
            else if (film.Segment == led.Target)
            {
                Wait(led, led.Target); // (normally seen at once by OnStarted)
                Plugin.Log.LogInfo($"Move director: {led.Key}: {led.Target} started.");
                led.Target = null;
                return;
            }
            else if (now - led.Since > Budget)
                why = Aside(led, now + AsideLong, $"not done within {Budget:0} s");
            else if (now - Mathf.Max(led.Since, film.LastMove) > StandStill)
                why = Aside(led, now + AsideLong, $"the boss started nothing for {StandStill:0} s");
            else if ((plan = PlanFor(film, trees, led.Target)) == null)
                why = Aside(led, now + AsideShort, "no way to it now");
            if (why == null)
            {
                led.Plan = plan;
                return;
            }
            Plugin.Log.LogInfo($"Move director: {led.Key}: {led.Target} {why}.");
            led.Target = null;
        }
        var open = film.Missing.Where(m => !led.Aside.TryGetValue(m, out float until) || now >= until).ToList();
        var (move, next) = Choose(open, m => PlanFor(film, trees, m));
        led.Plan = next;
        led.Target = move;
        led.Since = now;
        if (move == null)
        {
            led.Text = null; // nothing to lead to now (a coroutine AI's swaps show their own line)
            return;
        }
        string first = next.Hops > 0 ? LabelAt(film, (ILiveTree)next.Tree, next.Goal) : null;
        led.Text = $"Director: next {move}" + (first != null ? $" (first {first})" : "");
        Plugin.Log.LogInfo($"Move director: {led.Key}: next {move}" + (first != null ? $", first {first} ({next.Hops} step(s))" : "") +
                           $" ({open.Count} of {film.Missing.Count} missing moves open).");
    }

    private static string Aside(Led led, float until, string why)
    {
        led.Aside[led.Target] = until;
        return $"put aside ({why})";
    }

    /// <summary>
    /// The best way to any block of the move, in any of the boss's trees (fewest form changes), from the part of the
    /// tree the boss is in now (not Pope's second phase while he is in his first).
    /// </summary>
    private static Plan PlanFor(FightRecorder.Directed film, List<ILiveTree> trees, string move)
    {
        var attack = film.Graph.Attacks.FirstOrDefault(a => a.Label == move && !a.IsTail);
        if (attack == null)
            return null;
        Plan best = null;
        foreach (var unit in attack.Units)
            foreach (var tree in trees)
                foreach (int node in unit.Owner.Value != null ? tree.IndicesOf(unit.Owner.Value) : Enumerable.Empty<int>())
                    if (PlanTo(tree, node) is { } plan && (best == null || plan.Hops < best.Hops) && tree.Reachable(plan))
                        best = plan;
        return best;
    }

    /// <summary>The move a node belongs to ("To balance switching" for its SetInt), for the log.</summary>
    private static string LabelAt(FightRecorder.Directed film, ILiveTree tree, int node)
    {
        var units = film.Graph.Units.Where(u => u.Owner.Value != null).GroupBy(u => u.Owner.Value).ToDictionary(g => g.Key, g => g.First());
        for (int i = 0; i < 40 && node >= 0; i++, node = tree.Parent(node))
            if (units.TryGetValue(tree.Task(node), out var unit) && film.Graph.AttackOf(unit) is { } attack)
                return attack.Label;
        return "a form change";
    }

    /// <summary>The boss's BehaviorDesigner trees, else the behaviour-block trees of its AI (none: a coroutine AI).</summary>
    private static List<ILiveTree> TreesOf(Character boss)
    {
        var list = new List<ILiveTree>();
        var manager = BD.BehaviorManager.instance;
        foreach (var behavior in manager?.behaviorTreeMap != null ? BossAttacks.TreesOf(boss) : new List<BD.Behavior>())
            if (behavior != null && manager.behaviorTreeMap.TryGetValue(behavior, out var running) && running?.taskList is { Count: > 0 })
            {
                if (!Trees.TryGetValue(running, out var tree) || tree.Count != running.taskList.Count)
                {
                    Trees[running] = tree = new TaskTree(running);
                    Plugin.Log.LogInfo($"Move director: tree of {boss.name}: {tree.Summary()}.");
                }
                list.Add(tree);
            }
        if (list.Count == 0 && BlockTreeOf(boss) is { } blocks)
            list.Add(blocks);
        return list;
    }

    /// <summary>A tree the director leads: nodes are its live objects (tasks, blocks, checks).</summary>
    private interface ILiveTree : ITree
    {
        object Task(int node);
        IEnumerable<int> IndicesOf(object task);       // a block can sit at several places

        /// <summary>Whether the boss is in a part of the tree the way can be taken from now.</summary>
        bool Reachable(Plan plan);
    }

    /// <summary>A running BehaviorDesigner tree; a ConditionalEvaluator's check is a node of its own after the tasks.</summary>
    private sealed class TaskTree : ILiveTree
    {
        private readonly BD.BehaviorManager.BehaviorTree _running;
        private readonly List<BDTasks.Task> _tasks;
        private readonly Dictionary<object, int> _index = new();
        private readonly Kind[] _kinds;
        private readonly int[] _guards;
        private readonly List<int> _setters = new();
        private static readonly int[] None = new int[0];

        public int Count { get; }

        public TaskTree(BD.BehaviorManager.BehaviorTree running)
        {
            _running = running;
            Count = running.taskList.Count;
            _tasks = new List<BDTasks.Task>(running.taskList);
            _guards = Enumerable.Repeat(-1, Count).ToArray();
            for (int i = 0; i < Count; i++)
                if (_tasks[i] is BDTasks.ConditionalEvaluator { conditionalTask: { } check })
                {
                    _guards[i] = _tasks.Count;
                    _tasks.Add(check);
                }
            for (int i = 0; i < _tasks.Count; i++)
                if (_tasks[i] != null && !_index.ContainsKey(_tasks[i]))
                    _index[_tasks[i]] = i;
            _kinds = _tasks.Select(KindOfTask).ToArray();
            for (int i = 0; i < _kinds.Length; i++)
                if (_kinds[i] == Kind.Setter)
                    _setters.Add(i);
            // A check on a value nothing in this tree sets is not a state the director can change: as the game decides.
            var written = new HashSet<object>(_setters.Select(s => VariableOf(SetterTarget(_tasks[s]))).Where(v => v != null));
            for (int i = 0; i < _kinds.Length; i++)
                if (_kinds[i] == Kind.State && !CheckedVariables(_tasks[i]).Any(written.Contains))
                    _kinds[i] = Kind.Other;
        }

        public object Task(int node) => _tasks[node];
        public int IndexOf(object task) => _index.TryGetValue(task, out int i) ? i : -1;
        public IEnumerable<int> IndicesOf(object task) => _index.TryGetValue(task, out int i) ? new[] { i } : None;
        public bool Reachable(Plan plan) => true; // the root repeats: every branch comes round
        public int Parent(int node) => node < Count ? _running.parentIndex[node] : -1;
        public int Slot(int node) => node < Count ? _running.relativeChildIndex[node] : -1;
        public IReadOnlyList<int> Children(int node) =>
            node < Count && node < _running.childrenIndex.Count && _running.childrenIndex[node] is { } c ? c : None;
        public int Guard(int node) => node < Count ? _guards[node] : -1;
        public IEnumerable<int> Setters => _setters;

        public Kind KindOf(int node)
        {
            if (_tasks[node] is BDTasks.RunOnlyOnce once && once.executionStatus != BDTasks.TaskStatus.Inactive)
                return Kind.Spent;
            // Weight 0 means never, unless it is only the previous pick left out this time.
            if (Parent(node) is int parent and >= 0 && _tasks[parent] is BDTasks.WeightedSelector picker &&
                picker._weights is { } weights && Slot(node) is int slot and >= 0 && slot < weights.Count &&
                weights[slot] <= 0f && slot != picker._prevChildIndex)
                return Kind.Spent;
            return _kinds[node];
        }

        /// <summary>For the log: how the tree was read (form checks and setters must be found for form changes).</summary>
        public string Summary() =>
            $"{Count} tasks, {_kinds.Count(k => k == Kind.State)} checks on set values, {_setters.Count} setters, " +
            $"{_kinds.Count(k => k == Kind.Force)} gating checks, {_guards.Count(g => g >= 0)} evaluators";

        public bool Holds(int state) =>
            _tasks[state] is BDTasks.Conditional check && check.OnUpdate() == BDTasks.TaskStatus.Success;

        public bool Makes(int setter, int state, bool pass)
        {
            var variable = VariableOf(SetterTarget(_tasks[setter]));
            if (variable == null)
                return false;
            switch (_tasks[setter], _tasks[state])
            {
                case (BDMath.SetInt set, BDMath.IntComparison check):
                    bool first = Equals(VariableOf(check.integer1), variable), second = Equals(VariableOf(check.integer2), variable);
                    if (!first && !second)
                        return false;
                    int a = first ? set.intValue.Value : check.integer1.Value, b = second ? set.intValue.Value : check.integer2.Value;
                    return Compare(check.operation, a, b) == pass;
                case (BDMath.SetBool set, BDMath.BoolComparison check):
                    bool one = Equals(VariableOf(check.bool1), variable), two = Equals(VariableOf(check.bool2), variable);
                    if (!one && !two)
                        return false;
                    return ((one ? set.boolValue.Value : check.bool1.Value) == (two ? set.boolValue.Value : check.bool2.Value)) == pass;
                default:
                    return false;
            }
        }

        private static bool Compare(BDMath.IntComparison.Operation op, int a, int b) => op switch
        {
            BDMath.IntComparison.Operation.LessThan => a < b,
            BDMath.IntComparison.Operation.LessThanOrEqualTo => a <= b,
            BDMath.IntComparison.Operation.EqualTo => a == b,
            BDMath.IntComparison.Operation.NotEqualTo => a != b,
            BDMath.IntComparison.Operation.GreaterThanOrEqualTo => a >= b,
            _ => a > b,
        };

        private static Kind KindOfTask(BDTasks.Task task)
        {
            switch (task)
            {
                case null:
                    return Kind.Other;
                case BDTasks.Inverter:
                    return Kind.Inverter;
                case BDTasks.ConditionalEvaluator:
                    return Kind.Gate;
                case BDMath.IntComparison or BDMath.BoolComparison:
                    return Kind.State;
                case BDMath.SetInt or BDMath.SetBool:
                    return Kind.Setter;
            }
            if (ForcedTypes.Contains(task.GetType()))
                return Kind.Force;
            return task.GetType().Name switch
            {
                "Selector" => Kind.Selector,
                "WeightedSelector" or "OneByOneSelector" => Kind.PickOne,
                "Sequence" or "RandomSequence" => Kind.Sequence,
                _ => Kind.Other,
            };
        }

        private static BD.SharedVariable SetterTarget(BDTasks.Task task) => task switch
        {
            BDMath.SetInt set => set.storeResult,
            BDMath.SetBool set => set.storeResult,
            _ => null,
        };

        private static IEnumerable<object> CheckedVariables(BDTasks.Task task) => (task switch
        {
            BDMath.IntComparison c => new BD.SharedVariable[] { c.integer1, c.integer2 },
            BDMath.BoolComparison c => new BD.SharedVariable[] { c.bool1, c.bool2 },
            _ => new BD.SharedVariable[0],
        }).Select(VariableOf).Where(v => v != null);

        /// <summary>
        /// A tree variable: its name when it is a named one, else the value object itself (a setter and a check then
        /// match only if they hold the very same object; a plain number in a check never matches a setter).
        /// </summary>
        private static object VariableOf(BD.SharedVariable variable) =>
            variable == null ? null : variable.IsShared && !string.IsNullOrEmpty(variable.Name) ? variable.Name : variable;
    }
}
#endif
