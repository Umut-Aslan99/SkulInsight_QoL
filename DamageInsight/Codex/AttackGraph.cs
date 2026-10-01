using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace DamageInsight.Codex;

/// <summary>
/// A boss's attacks, read from the game's own code and objects. Three kinds of AI are understood:
/// <list type="bullet">
/// <item>Coroutine AIs: each attack is one coroutine that starts several steps in a row ("CastRush": dash, ready,
/// rush a, ready, rush b, ...), often through helper coroutines and helper behaviours with steps of their own.
/// Reading which fields and methods every coroutine uses (its IL) gives each attack's steps in order. Methods that pick
/// among many attacks ("RunPattern", "Combat", "CProcess") are dispatchers; attacks a dispatcher runs back to back
/// in one pattern (a backstep, then a meteor) form one combined attack.</item>
/// <item>Behaviour-block trees and BehaviorDesigner trees: the tree is added from the live objects
/// (<see cref="Build"/>'s <c>treeOf</c> / <see cref="AddTreeUnit"/>): selectors and loops are dispatchers, a sequence
/// under a selector is one attack, a leaf under a selector is one attack.</item>
/// </list>
/// Steps are actions, or animation infos for bosses that play animations directly (Chimera). Works on live
/// components (in the game) and on types alone (tests, where every field is named by its path).
/// </summary>
public sealed partial class AttackGraph
{
    public enum Kind { Plain, Sequence, Dispatcher }

    /// <summary>One coroutine of one object, or one node of a tree: an attack, a helper, or a dispatcher.</summary>
    public sealed class Unit
    {
        public Node Owner;
        public MethodInfo Method;             // null for tree nodes read from data (BehaviorDesigner tasks)
        public string Name = "";              // for reports: "GoldenAideAI.CastRush", "Sequence 'Slash combo'"
        public Type StateMachine;             // the compiled coroutine class (null for plain methods and tasks)
        public string Label = "";
        public string Tag;                    // a name the game's designers gave this node, if any
        public string Note;                   // for reports: the condition/weights it checks ("HealthCondition(LessThan 0.8)")
        public Kind Kind;
        public readonly List<object> Steps = new();          // actions/animations, in the order they are used (distinct)
        public readonly List<object> Own = new();            // the steps it starts itself (not through calls)
        public readonly List<Unit> Calls = new();            // coroutines or tree children it runs
        public readonly List<Unit> CalledBy = new();
        public readonly List<List<Unit>> Runs = new();       // calls not separated by a jump: run back to back
        public bool OnAi, Dispatcher, Entry;
        public bool Borrowed;                 // the label was taken from a child (the node itself has a generic name)
        // What the rules read of the live objects, kept as plain values so a saved graph (Snapshot) classifies the same.
        public string TypeName, MethodName;   // "Repeat", "CRun"; MethodName null for tree nodes read from data
        public bool MethodPublic;
        public string RawLabel = "";         // the label before classification
        public override string ToString() => Name;
    }

    /// <summary>An attack as the Codex shows it.</summary>
    public sealed class Attack
    {
        public string Label = "";
        public readonly List<Unit> Units = new();
        public readonly List<object> Steps = new();

        /// <summary>
        /// Only the last step of other attacks (an escape the AI runs right after them): shown and filmed as part of
        /// the attack before it, not on its own.
        /// </summary>
        public bool IsTail;

        /// <summary>The label before AddAttack numbered it ("Push" for "Push 2"), if it was numbered.</summary>
        public string BaseLabel;
    }

    /// <summary>A live object (Value) or, without one, a type reached through a path of field names.</summary>
    public readonly struct Node
    {
        public readonly Type Type;
        public readonly object Value;
        public readonly string Path;
        public Node(Type type, object value, string path) { Type = type; Value = value; Path = path; }
        public object Key => Value ?? Path;
        public bool IsValid => Type != null;
    }

    public readonly List<Unit> Units = new();
    public readonly List<Attack> Attacks = new();

    /// <summary>Set by the reader when part of the AI could not be read yet (a tree not loaded): try again later.</summary>
    public bool Incomplete;

    /// <summary>Behaviours nothing runs and that match no attack (unused leftovers); listed in the report only.</summary>
    public readonly List<Unit> Unused = new();
    private readonly Dictionary<Unit, Attack> _attackOf = new();
    private readonly Dictionary<(object, MethodInfo), Unit> _memo = new();
    private readonly Type _aiType;
    private readonly Type[] _stepTypes;
    private readonly Func<Node, string> _nameOf;

    private const int MaxDepth = 10;
    private readonly Dictionary<Type, Node> _aiRoots = new();   // types only: a field of an AI's type is that AI
    private static readonly Dictionary<MethodBase, List<KeyValuePair<OpCode, object>>> IlCache = new();

    /// <param name="aiType">The game's AIController.</param>
    /// <param name="stepTypes">What counts as a step (Characters.Actions.Action; AnimationInfo).</param>
    /// <param name="nameOf">Readable name of an object (its game object name in the game).</param>
    public AttackGraph(Type aiType, IEnumerable<Type> stepTypes, Func<Node, string> nameOf = null)
    {
        _aiType = aiType;
        _stepTypes = stepTypes.ToArray();
        _nameOf = nameOf ?? (n => n.Type.Name);
    }

    private bool IsStep(Type type) => _stepTypes.Any(t => t.IsAssignableFrom(type));

    /// <summary>The attack a unit belongs to as a whole (null for helpers and dispatchers).</summary>
    public Attack AttackOf(Unit unit) => unit != null && _attackOf.TryGetValue(unit, out var a) ? a : null;

    /// <summary>Whether any unit uses this step (to tell attack actions from loose ones).</summary>
    public bool Uses(object step) => Units.Any(u => u.Steps.Contains(step));

    // ------------------------------------------------------------------ building

    /// <summary>
    /// Reads the AIs among <paramref name="objects"/> (all their coroutines) and the other objects' CRun coroutines.
    /// <paramref name="treeOf"/> tells, for a live object, whether it is a tree block (sequence / selector) and which
    /// children it runs (with the name the designers gave each); <paramref name="addMore"/> can add tree units read
    /// from data (BehaviorDesigner). Then decides which units are attacks.
    /// </summary>
    public AttackGraph Build(IEnumerable<Node> objects,
        Func<Node, (Kind kind, IEnumerable<(Node child, string tag)> children)> treeOf = null,
        Action<AttackGraph> addMore = null)
    {
        var list = objects.Where(n => n.IsValid).ToList();
        foreach (var node in list.Where(n => _aiType.IsAssignableFrom(n.Type)))
            _aiRoots[node.Type] = node;
        foreach (var node in list.Where(n => _aiType.IsAssignableFrom(n.Type)))
        {
            foreach (var method in CoroutinesOf(node.Type))
                Analyze(node, method, 0).OnAi = true;
            if (node.Value != null)
                for (var t = node.Type; t != null && t.Assembly == node.Type.Assembly && t.Name != "AIController"; t = t.BaseType)
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType) && f.GetValue(node.Value) is UnityEngine.Object held && held != null &&
                            _aiHeld.Add(held))
                            _aiHeldOrder.Add(held);
        }
        foreach (var node in list.Where(n => !_aiType.IsAssignableFrom(n.Type)))
            if (RunOf(node.Type) is { } run)
                Analyze(node, run, 0);
        if (treeOf != null)
            foreach (var unit in Units.Where(u => !u.OnAi && u.Owner.Value != null).ToList())
            {
                var (kind, children) = treeOf(unit.Owner);
                if (kind == Kind.Plain)
                    continue;
                unit.Kind = kind;
                foreach (var (child, tag) in children)
                    if (RunOf(child.Type) is { } childRun)
                    {
                        var c = Analyze(child, childRun, 1);
                        if (!string.IsNullOrWhiteSpace(tag) && c.Tag == null)
                            c.Tag = tag;
                        Link(unit, c);
                    }
            }
        addMore?.Invoke(this);
        if (ExtraSteps != null)
            foreach (var unit in Units.Where(u => u.Owner.Value != null).ToList())
                foreach (var step in ExtraSteps(unit.Owner) ?? Enumerable.Empty<object>())
                    AddStep(unit, step, own: true);
        PropagateSteps();
        Classify();
        return this;
    }

    /// <summary>
    /// Tree children are linked after their parents were read, so steps are collected again bottom-up: a unit's own
    /// steps (in place), then those of everything it runs, in order.
    /// </summary>
    private void PropagateSteps()
    {
        var done = new Dictionary<Unit, List<object>>();
        var open = new HashSet<Unit>();
        List<object> Collect(Unit u)
        {
            if (done.TryGetValue(u, out var ready))
                return ready;
            if (!open.Add(u))
                return u.Steps; // a cycle: what it has so far
            var list = u.Kind == Kind.Plain ? new List<object>(u.Steps) : new List<object>(u.Own);
            foreach (var c in u.Calls)
                foreach (var step in Collect(c))
                    if (!list.Contains(step))
                        list.Add(step);
            open.Remove(u);
            return done[u] = list;
        }
        foreach (var u in Units.ToList())
            Collect(u);
        foreach (var pair in done)
        {
            pair.Key.Steps.Clear();
            pair.Key.Steps.AddRange(pair.Value);
        }
    }

    private static MethodInfo RunOf(Type type) => AccessTools.Method(type, "CRun") is { } run && IsCoroutine(run) ? run : null;

    /// <summary>Adds a tree node read from data (a BehaviorDesigner task); link it with <see cref="Link"/>.</summary>
    public Unit AddTreeUnit(Node owner, string name, Kind kind, IEnumerable<object> steps, string label, string tag)
    {
        var unit = new Unit { Owner = owner, Name = name, Kind = kind, Label = Tidy(label ?? ""), Tag = tag, TypeName = owner.Type?.Name };
        unit.RawLabel = unit.Label;
        foreach (var step in steps)
            AddStep(unit, step, own: true);
        Units.Add(unit);
        return unit;
    }

    /// <summary>A tree edge: <paramref name="parent"/> runs <paramref name="child"/> (its steps count for the parent).</summary>
    public void Link(Unit parent, Unit child)
    {
        if (child == null || child == parent)
            return;
        if (!parent.Calls.Contains(child))
            parent.Calls.Add(child);
        if (!child.CalledBy.Contains(parent))
            child.CalledBy.Add(parent);
        if (parent.Runs.Count == 0)
            parent.Runs.Add(new List<Unit>());
        if (parent.Kind == Kind.Sequence)
            parent.Runs[0].Add(child);
        else
            parent.Runs.Add(new List<Unit> { child });
        foreach (var step in child.Steps)
            AddStep(parent, step);
    }

    private static IEnumerable<MethodInfo> CoroutinesOf(Type aiType)
    {
        var chain = new List<Type>();
        for (var t = aiType; t != null && t.Assembly == aiType.Assembly && t.Name != "AIController"; t = t.BaseType)
            chain.Insert(0, t);
        foreach (var t in chain)
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (IsCoroutine(m) && !m.IsGenericMethod && !m.Name.Contains("<"))
                    yield return m;
    }

    private static bool IsCoroutine(MethodInfo m) => typeof(IEnumerator).IsAssignableFrom(m.ReturnType);

    private static List<KeyValuePair<OpCode, object>> Il(MethodBase body)
    {
        lock (IlCache)
        {
            if (IlCache.TryGetValue(body, out var il))
                return il;
            try { il = PatchProcessor.ReadMethodBody(body).ToList(); }
            catch (Exception) { il = new List<KeyValuePair<OpCode, object>>(); }
            return IlCache[body] = il;
        }
    }

    private Unit Analyze(Node owner, MethodInfo method, int depth)
    {
        if (_memo.TryGetValue((owner.Key, method), out var unit))
            return unit;
        unit = new Unit
        {
            Owner = owner, Method = method, Name = $"{owner.Type.Name}.{method.Name}", Label = LabelOf(owner, method),
            TypeName = owner.Type.Name, MethodName = method.Name, MethodPublic = method.IsPublic,
        };
        unit.RawLabel = unit.Label;
        _memo[(owner.Key, method)] = unit;
        Units.Add(unit);

        var stateMachine = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        unit.StateMachine = stateMachine;
        MethodBase body = stateMachine != null ? AccessTools.Method(stateMachine, "MoveNext") : method;
        if (body == null)
            return unit;

        var recent = new List<Node>();     // objects loaded since the last call: the next call's receiver is among them
        var run = new List<Unit>();
        foreach (var pair in Il(body))
        {
            var op = pair.Key;
            if ((op == OpCodes.Ldfld || op == OpCodes.Ldflda) && pair.Value is FieldInfo field)
            {
                if (stateMachine != null && field.DeclaringType == stateMachine)
                {
                    if (field.Name.EndsWith("4__this"))
                        recent.Add(owner);
                    continue;
                }
                // A field of the owner, or of a plain data object loaded from it (ChimeraAnimation._phase1.bite).
                Node holder = field.DeclaringType.IsAssignableFrom(owner.Type)
                    ? owner
                    : recent.LastOrDefault(n => n.IsValid && !typeof(UnityEngine.Object).IsAssignableFrom(n.Type) &&
                                                field.DeclaringType.IsAssignableFrom(n.Type));
                if (!holder.IsValid)
                    continue;
                var child = Child(holder, field);
                string relative = (child.Path ?? "").Substring(Math.Min((owner.Path ?? "").Length, (child.Path ?? "").Length)).TrimStart('/');
                if (IsStep(field.FieldType))
                {
                    if (child.Value != null || owner.Value == null)
                        AddStep(unit, child.Key, relative, own: true);
                }
                else if (StepElement(field.FieldType) != null)
                {
                    // A list of actions (RunActions._actions): every element is a step, in order.
                    if (child.Value is IEnumerable items)
                        foreach (var item in items)
                            if (item != null && !(item is UnityEngine.Object o && o == null))
                                AddStep(unit, item, relative, own: true);
                    if (owner.Value == null)
                        AddStep(unit, child.Path + "[]", relative, own: true);
                }
                else if (child.IsValid)
                    recent.Add(child);
            }
            else if (op == OpCodes.Ldarg_0 && stateMachine == null)
                recent.Add(owner);
            else if (stateMachine != null && IsLoadLocal(op))
                recent.Add(owner); // coroutines keep "this" in a local; the receiver is chosen by type below
            else if ((op == OpCodes.Call || op == OpCodes.Callvirt) && pair.Value is MethodInfo called)
            {
                var receivers = recent;
                recent = new List<Node>();
                if (called.IsStatic || !IsCoroutine(called) || depth >= MaxDepth)
                    continue;
                var receiver = receivers.LastOrDefault(n => called.DeclaringType.IsAssignableFrom(n.Type));
                if (!receiver.IsValid || receiver.Type.Assembly != _aiType.Assembly)
                    continue;
                if (receiver.Value == null && _aiRoots.TryGetValue(receiver.Type, out var root))
                    receiver = root; // ChimeraCombat._chimera is the Chimera itself
                var target = AccessTools.Method(receiver.Type, called.Name, called.GetParameters().Select(p => p.ParameterType).ToArray()) ?? called;
                var callee = Analyze(receiver, target, depth + 1);
                if (callee == unit)
                    continue;
                if (!unit.Calls.Contains(callee))
                    unit.Calls.Add(callee);
                if (!callee.CalledBy.Contains(unit))
                    callee.CalledBy.Add(unit);
                foreach (var step in callee.Steps)
                    AddStep(unit, step);
                run.Add(callee);
            }
            else if (op == OpCodes.Br || op == OpCodes.Br_S || op == OpCodes.Leave || op == OpCodes.Leave_S)
            {
                if (run.Count > 0)
                    unit.Runs.Add(run);
                run = new List<Unit>();
            }
        }
        if (run.Count > 0)
            unit.Runs.Add(run);
        return unit;
    }

    private static bool IsLoadLocal(OpCode op) =>
        op == OpCodes.Ldloc_0 || op == OpCodes.Ldloc_1 || op == OpCodes.Ldloc_2 || op == OpCodes.Ldloc_3 ||
        op == OpCodes.Ldloc_S || op == OpCodes.Ldloc;

    /// <summary>The field path each step was found under ("_rushA", "_darkRush/_fristAttack", "_phase1/bite").</summary>
    public readonly Dictionary<object, string> StepPath = new();

    private void AddStep(Unit unit, object step, string path = null, bool own = false)
    {
        if (step == null)
            return;
        if (path != null && !StepPath.ContainsKey(step))
            StepPath[step] = path;
        if (!unit.Steps.Contains(step))
            unit.Steps.Add(step);
        if (own && !unit.Own.Contains(step))
            unit.Own.Add(step);
    }

    /// <summary>The step type of an array or list of steps (Action[], List&lt;Action&gt;), else null.</summary>
    private Type StepElement(Type type)
    {
        Type element = type.IsArray ? type.GetElementType()
            : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) ? type.GetGenericArguments().FirstOrDefault() : null;
        return element != null && IsStep(element) ? element : null;
    }

    private static Node Child(Node owner, FieldInfo field)
    {
        object value = null;
        if (owner.Value != null)
        {
            try { value = field.GetValue(owner.Value); } catch (Exception) { /* unreadable: treat as missing */ }
            if (value is UnityEngine.Object o && o == null)
                value = null;
            if (value == null)
                return default;
        }
        return new Node(value?.GetType() ?? field.FieldType, value, owner.Path + "/" + field.Name);
    }

    // ------------------------------------------------------------------ deciding what is an attack

    /// <summary>Main loops by name: "Combat", "CCombat", "Process…", "RunPattern", "Loop".</summary>
    private static readonly Regex LoopName = new(@"^C?(Combat|Process\w*|RunPattern|Loop)$");

    /// <summary>Waiting is not an attack ("Idle", "CastSkippableIdle", the Idle behaviour).</summary>
    private static readonly Regex IdleName = new(@"(?i)^(cast)?(skip+able)?\s*idle\b|\bidle$|^sleep$");

    private static bool IsIdle(Unit u) =>
        IdleName.IsMatch(u.MethodName ?? "") || IdleName.IsMatch(u.TypeName ?? "") && u.MethodName == "CRun" ||
        IdleName.IsMatch(u.Label);

    private void Classify()
    {
        // Dispatchers: tree selectors and loops, the AI's main loop, anything that covers most of an AI's steps,
        // and whatever calls those.
        foreach (var u in Units)
            u.Dispatcher = u.Kind == Kind.Dispatcher || (u.OnAi && u.MethodName == "CProcess") ||
                           (u.MethodName != null && LoopName.IsMatch(u.MethodName) && u.Calls.Count > 0);
        foreach (var group in Units.Where(u => u.OnAi).GroupBy(u => u.Owner.Key))
        {
            int all = group.SelectMany(u => u.Steps).Distinct().Count();
            foreach (var u in group)
                u.Dispatcher |= u.Calls.Count >= 3 && all >= 6 && u.Steps.Count >= all * 0.6;
        }
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var u in Units.Where(u => !u.Dispatcher && u.Calls.Any(c => c.Dispatcher)))
                changed = u.Dispatcher = true;
        }

        foreach (var u in Units)
        {
            if (u.Dispatcher || u.Steps.Count == 0 || IsIdle(u))
                continue;
            var callers = u.CalledBy.Where(c => !c.Dispatcher).ToList();
            u.Entry = u.OnAi
                ? u.MethodPublic || callers.Count == 0
                : u.CalledBy.Count == 0 || u.CalledBy.Any(c => c.Dispatcher);
        }
        ResolveLabels();
        ClassifyTrees();

        // Patterns a dispatcher runs back to back ("backstep, then meteor in ground 2") are one attack.
        var combos = new List<List<Unit>>();
        var alone = new HashSet<Unit>();
        var inCombo = new HashSet<Unit>();
        foreach (var d in Units.Where(u => u.Dispatcher && u.Kind == Kind.Plain && !_tree.Contains(u)))
            foreach (var run in d.Runs)
            {
                var entries = run.Where(u => u.Entry).Distinct().ToList();
                if (entries.Count == 1)
                    alone.Add(entries[0]);
                else if (entries.Count > 1 && !combos.Any(c => c.SequenceEqual(entries)))
                {
                    combos.Add(entries);
                    inCombo.UnionWith(entries);
                }
            }

        var orphans = new List<Unit>();
        foreach (var u in Units.Where(u => u.Entry))
        {
            // A behaviour nothing runs (Chimera's bite, started by an animation event) belongs to the AI's attack
            // with the same name, if there is one.
            if (!u.OnAi && u.CalledBy.Count == 0 && u.Kind == Kind.Plain)
            {
                orphans.Add(u);
                continue;
            }
            var combo = combos.FirstOrDefault(c => c[0] == u);
            if (combo != null)
                AddAttack(combo);
            bool onlyInCombos = inCombo.Contains(u) && !alone.Contains(u) && u.CalledBy.All(c => c.Dispatcher);
            if (!onlyInCombos)
                AddAttack(new List<Unit> { u });
        }
        foreach (var combo in combos.Where(c => !c[0].Entry))
            AddAttack(combo);
        foreach (var u in orphans)
        {
            var same = Attacks.FirstOrDefault(a => string.Equals(a.Label, u.Label, StringComparison.OrdinalIgnoreCase));
            if (same == null)
            {
                // Nothing runs it and no attack has its name: an unused leftover (Awakened Leiana's rising pierce).
                if (Attacks.Count > 0)
                    Unused.Add(u);
                else
                    AddAttack(new List<Unit> { u });
                continue;
            }
            same.Units.Add(u);
            foreach (var step in u.Steps.Where(s => !same.Steps.Contains(s)))
                same.Steps.Add(step);
            _attackOf[u] = same;
        }

        MergeDuplicates();

        foreach (var a in Attacks)
            a.IsTail = a.Steps.Count == 1 &&
                       Attacks.Count(o => o != a && o.Steps.Count > 1 && Equals(o.Steps[o.Steps.Count - 1], a.Steps[0])) >= 2;
    }

    /// <summary>
    /// One move built several times in the game data is one move in the book:
    /// <list type="bullet">
    /// <item>the same designer name in several branches ("Push" in the main and the sub pattern, one with a potion
    /// after it; AddAttack numbered them "Push 2");</item>
    /// <item>the same action under names sharing their first words ("Back dash long/short/middle" → "Back dash",
    /// "Landing short/long" → "Landing", "Triple dance saint field" → "Triple dance"); Pope's Baptism and Worship
    /// share only an animation and stay apart;</item>
    /// <item>a designer typo of another move that is part of it ("Consecation" in "Consecration").</item>
    /// </list>
    /// </summary>
    private void MergeDuplicates()
    {
        string Key(object step) => StepKey?.Invoke(step) ?? step?.ToString() ?? "";
        // Distinct: once "Back dash long" and "short" are one move it has the back dash twice; "middle" has it once.
        List<string> Keys(Attack a) => a.Steps.Select(Key).Distinct().ToList();
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < Attacks.Count && !merged; i++)
                for (int j = i + 1; j < Attacks.Count && !merged; j++)
                {
                    Attack a = Attacks[i], b = Attacks[j];
                    string label = null;
                    string baseA = a.BaseLabel ?? a.Label, baseB = b.BaseLabel ?? b.Label;
                    if ((a.BaseLabel != null || b.BaseLabel != null) && baseA == baseB)
                        label = baseA;
                    else if (Keys(a).SequenceEqual(Keys(b)) && CommonWords(a.Label, b.Label) is { Length: > 0 } common)
                        label = common;
                    else if (IsTypo(a.Label, b.Label) && (Keys(a).All(Keys(b).Contains) || Keys(b).All(Keys(a).Contains)))
                        label = a.Steps.Count >= b.Steps.Count ? a.Label : b.Label;
                    // The same move with and without its end motion ("Slam" / "Slam (end)", "Dash after landing ->
                    // slam" / "… -> slam (end)").
                    else if (WithoutEnd(a.Label) is { Length: > 0 } baseName && baseName == WithoutEnd(b.Label) &&
                             (Keys(a).All(Keys(b).Contains) || Keys(b).All(Keys(a).Contains)))
                        label = baseName;
                    if (label == null)
                        continue;
                    Absorb(a, b, label);
                    merged = true;
                }
        }
    }

    private void Absorb(Attack into, Attack other, string label)
    {
        into.Label = label;
        into.BaseLabel = null;
        foreach (var u in other.Units)
        {
            if (!into.Units.Contains(u))
                into.Units.Add(u);
            _attackOf[u] = into;
        }
        foreach (var step in other.Steps.Where(s => !into.Steps.Contains(s)))
            into.Steps.Add(step);
        Attacks.Remove(other);
    }

    /// <summary>The leading words two names share ("Back dash long" + "Back dash short" → "Back dash"), or "".</summary>
    private static string CommonWords(string a, string b)
    {
        var wa = a.Split(' ');
        var wb = b.Split(' ');
        int n = 0;
        while (n < wa.Length && n < wb.Length && string.Equals(wa[n], wb[n], StringComparison.OrdinalIgnoreCase))
            n++;
        return string.Join(" ", wa.Take(n)).Trim();
    }

    /// <summary>A name without an end-motion suffix: "Slam (end)", "Slam end motion", "Slam + end motion" → "Slam".</summary>
    private static string WithoutEnd(string label) =>
        Regex.Replace(label, @"(?i)\s*(\(end\)|\+\s*end motion|end motion|\bend)\s*$", "").Trim();

    /// <summary>
    /// A typo of the other name: one or two letters off in a long name ("Consecation" / "Consecration"); short names
    /// that far apart are different words (Leiana's "Dash" and "Rush").
    /// </summary>
    private static bool IsTypo(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        return Distance(a, b) <= Math.Min(2, Math.Min(a.Length, b.Length) / 5);
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// Tree nodes are named by their designers' tag, else by their object; a node with only a generic name
    /// ("Sequence", "Selector (1)") takes the name of its biggest child.
    /// </summary>
    private void ResolveLabels()
    {
        foreach (var u in Units.Where(u => !string.IsNullOrWhiteSpace(u.Tag)))
            u.Label = Tidy(u.Tag);
        for (int pass = 0; pass < 4; pass++)
            foreach (var u in Units.Where(u => IsGeneric(u) && u.Calls.Count > 0))
            {
                var best = u.Calls.Where(c => !IsGeneric(c) && c.Steps.Count > 0)
                    .OrderByDescending(c => c.Steps.Count).ThenBy(c => IsMovement(c.Label) ? 1 : 0).FirstOrDefault();
                if (best != null)
                {
                    u.Label = best.Label;
                    u.Borrowed = true;
                }
            }
    }

    private static readonly Regex GenericName = new(
        @"(?i)^\s*(sequence|selector|uniform selector|weighted selector|random( behaviour| selector| sequence)?|repeat(er)?|" +
        @"infinite loop|time loop|chance|cool ?time|count|conditional|height|behaviour( info)?|weight|run character actions?|" +
        @"parallel\w*|priority selector|utility selector|game object|root|entry|attack|action|\[\d+\]|\d+)\s*(\(\d+\))?\s*$");

    private static bool IsGeneric(Unit u) => string.IsNullOrWhiteSpace(u.Label) || GenericName.IsMatch(u.Label);

    /// <summary>
    /// A readable name from an object name, designer tag or task name: designer notes ("[ o ]", "(test )"), idle tails
    /// ("-> skippable idle") and characters the book font can't show (Korean) are removed; "ArrowShot" and
    /// "Dash_Long" become "Arrow shot" and "Dash long".
    /// </summary>
    public static string Tidy(string name)
    {
        name = Regex.Replace(name ?? "", @"[^\x20-\x7E]", " ");
        name = Regex.Replace(name, @"\[\s*o\s*\]|\(\s*o\s*\)|\(\s*test\s*\)", " ", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s*->\s*(long\s+|skip+able\s+|skip\s+)?idle\b(\s*\(skip\))?", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s*->\s*$", "");
        name = Regex.Replace(name.Trim(), @"\s+", " ");
        if (name.Length == 0)
            return "";
        name = Regex.Replace(Recording.OwnerNames.Humanize(name), @"(?<=[a-z])(?=\d)", " ");
        return Regex.Replace(name, @"(?<=\w)\(", " (");
    }

    private static readonly Regex Movement = new(
        @"(?i)^(shadow step|back ?step|back ?dash|dash( \w+)?|teleport\w*( \w+)*|jump|take ?off|wait|move|walk|escape|\w+ escape|.*\bescape)$");

    private static bool IsMovement(string label) => Movement.IsMatch(label ?? "");

    private HashSet<Unit> _tree = new();

    /// <summary>Whether a unit belongs to a tree (behaviour blocks, BehaviorDesigner): decided top-down.</summary>
    public bool InTree(Unit unit) => _tree.Contains(unit);

    private static readonly Regex LoopType = new(@"Repeat|Loop|Until|Restart");

    private static readonly Regex Groggy = new(@"(?i)^groggy\b");

    /// <summary>Names of a piece of a move, not of a move ("Intro", "Outro", "Ready", "End motion").</summary>
    private static readonly Regex PartWord = new(@"(?i)^(intro|outro|ready|start|end|finish)\b");

    /// <summary>
    /// Designer names that describe the decision, not the move ("Phase a - out of middle range", "Dash / back dash",
    /// "Anywhere ( cool time)"): not used as attack names.
    /// </summary>
    private static readonly Regex Structural = new(
        @"(?i)\bphase\b|range|distance|success|any ?where|cool ?time|weight|sequence|selector|^key$|\bnot\b|between|/|%|\bif\b|condition|check|\?|^c: |special ?skill ?(main|sub)?$");

    private static bool Nice(string label) => !string.IsNullOrWhiteSpace(label) && !GenericName.IsMatch(label) && !Structural.IsMatch(label);

    /// <summary>Whether a designer name names a move (not a decision like "Run special skill" or "Phase a").</summary>
    public static bool NamesAMove(string label) => Nice(Tidy(label));

    /// <summary>
    /// Trees (behaviour blocks, BehaviorDesigner) are decided from the top: a block that chooses between moves or
    /// repeats them (a selector with two or more moves below it, a loop, or a sequence containing such a block) is a
    /// dispatcher; the first block below one that is not is a whole attack, whatever it runs inside (its dash, its
    /// end motion, a random wait).
    /// </summary>
    private void ClassifyTrees()
    {
        var tree = new HashSet<Unit>();
        var open = new Stack<Unit>(Units.Where(u => u.Kind != Kind.Plain || (u.MethodName == null && u.Owner.Value != null)));
        while (open.Count > 0)
        {
            var u = open.Pop();
            if (tree.Add(u))
                foreach (var c in u.Calls)
                    open.Push(c);
        }
        _tree = tree;
        if (tree.Count == 0)
            return;

        var container = new Dictionary<Unit, bool>();
        bool IsContainer(Unit u)
        {
            if (container.TryGetValue(u, out bool known))
                return known;
            container[u] = false; // cycles: not a container
            bool result = u.Kind switch
            {
                // A block that repeats one move a few times (Yggdrasil's "Fist slam" 2-3 times) is part of that move.
                Kind.Dispatcher when IsRepeatOfOneMove(u) => false,
                Kind.Dispatcher => LoopType.IsMatch(u.TypeName ?? "") ||
                                   u.Calls.Count(c => c.Steps.Count > 0) >= 2 || u.Calls.Any(IsContainer),
                // A sequence with a move of its own may end in a pick between single moves (the Veteran Magician's
                // Phoenix landing, then "Rising (low)" or "Rising (high)"): those are its endings, not new attacks.
                // One that ends in the boss being stunned (Yggdrasil: "Energy bomb", then "Groggy") holds two moves.
                Kind.Sequence => EndsInGroggy(u) || u.Calls.Any(c => IsContainer(c) && !(IsVariantPick(c) && HasOwnMove(u))),
                // A wrapper (BehaviourInfo) with nothing of its own is what it wraps.
                _ => u.Own.Count == 0 && u.Calls.Count == 1 && IsContainer(u.Calls[0]),
            };
            return container[u] = result;
        }

        // A pick (not a loop) whose moving choices are each one single move: the move itself, or a sequence / pick
        // with only that one moving part (First Hero: after a slash, "end motion + idle" or "landing / triple dash").
        bool IsVariantPick(Unit u)
        {
            u = Unwrap(u);
            return u.Kind == Kind.Dispatcher && !LoopType.IsMatch(u.TypeName ?? "") &&
                   u.Calls.Where(c => c.Steps.Count > 0).All(IsSingleMove);
        }
        bool IsSingleMove(Unit u)
        {
            for (int i = 0; i < 10; i++)
            {
                u = Unwrap(u);
                if (u.Kind == Kind.Plain)
                    return u.Own.Count > 0 || u.Steps.Count == 0;
                if (u.Kind == Kind.Dispatcher && LoopType.IsMatch(u.TypeName ?? ""))
                    return false;
                var moving = u.Calls.Where(c => c.Steps.Count > 0).ToList();
                if (moving.Count == 0)
                    return true;
                if (moving.Count > 1)
                    return false;
                u = moving[0];
            }
            return false;
        }
        bool HasOwnMove(Unit sequence) => sequence.Calls.Any(c => c.Steps.Count > 0 && !IsContainer(c));
        bool IsRepeatOfOneMove(Unit u)
        {
            if (u.TypeName != "Repeat") // the behaviour block with a count; not endless loops
                return false;
            var moving = u.Calls.Where(c => c.Steps.Count > 0).ToList();
            return moving.Count == 1 && !IsContainer(moving[0]);
        }
        bool EndsInGroggy(Unit sequence)
        {
            var moving = sequence.Calls.Where(c => c.Steps.Count > 0).ToList();
            return moving.Count >= 2 && Groggy.IsMatch(moving[moving.Count - 1].Label);
        }
        Unit Unwrap(Unit u)
        {
            for (int i = 0; i < 8 && u.Kind == Kind.Plain && u.Own.Count == 0 && u.Calls.Count == 1; i++)
                u = u.Calls[0];
            return u;
        }

        foreach (var u in tree)
        {
            u.Entry = false;
            if (!u.OnAi)
                u.Dispatcher = false;
        }
        var visited = new HashSet<Unit>();
        var rootOf = new Dictionary<Unit, int>();
        int currentRoot = 0;
        void Visit(Unit u)
        {
            if (!visited.Add(u))
                return;
            if (IsContainer(u))
            {
                u.Dispatcher = true;
                foreach (var c in u.Calls)
                    Visit(c);
            }
            else if (u.Steps.Count > 0 && !IsIdle(u))
            {
                u.Entry = true;
                u.Label = AttackLabel(u);
                rootOf[u] = currentRoot;
            }
        }
        // When the AI runs a tree, blocks the AI never reaches are leftovers in the prefab (First Hero: 100+ of them,
        // "Hook 2", "Dash breakaway"): not attacks. Trees without an AI (BehaviorDesigner) start at their own roots.
        // In the order the fight runs them: the tree the AI's coroutines start, then the ones it holds (in field order,
        // Yggdrasil's _behaviours before _phase2Sequence), then the rest.
        int Rank(Unit r) =>
            r.CalledBy.Any(c => c.OnAi) ? 0
            : r.Owner.Value != null && _aiHeldOrder.IndexOf(r.Owner.Value) is var i && i >= 0 ? 1 + i
            : int.MaxValue;
        var roots = tree.Where(u => !u.CalledBy.Any(tree.Contains)).OrderBy(Rank).ToList();
        bool aiRunsTree = roots.Any(r => r.CalledBy.Any(c => c.OnAi) || (r.Owner.Value != null && _aiHeld.Contains(r.Owner.Value)));
        // Only a tree the AI holds but doesn't start from its coroutines is a later phase (Yggdrasil's phase 2
        // sequence, started when phase 1 dies); every tree its coroutines start belongs to the fight as it is
        // (First Hero 2 starts a small "look at" tree next to its main one).
        int later = 0;
        foreach (var root in roots)
        {
            bool reached = root.CalledBy.Count > 0 || (root.Owner.Value != null && _aiHeld.Contains(root.Owner.Value));
            if (aiRunsTree && !reached)
            {
                if (root.Steps.Count > 0)
                    Unused.Add(root);
                continue;
            }
            int rank = Rank(root);
            bool laterPhase = rank > 0 && rank < int.MaxValue;
            currentRoot = laterPhase ? later + 1 : 0;
            int before = rootOf.Count;
            Visit(root);
            if (laterPhase && rootOf.Count > before)
                later++;
        }
        // Several trees the AI runs one after the other: the later ones are the fight's later phases. Without a first
        // phase of its own (no tree started from its coroutines), the first later one is the start.
        int first = rootOf.Count > 0 ? rootOf.Values.Min() : 0;
        if (aiRunsTree && rootOf.Values.Any(v => v > first))
            foreach (var pair in rootOf.Where(p => p.Value > first))
                pair.Key.Label = $"Phase {pair.Value - first + 1} · {pair.Key.Label}";
    }

    /// <summary>
    /// The name of a tree attack: follow the blocks that only wrap one moving part down to its core; the deepest
    /// designer name on that way that names a move wins ("Hero landing", "Triple dash slash"); without one, a core
    /// made of several moves is named after them ("Dash chase + Slam").
    /// </summary>
    /// <summary>
    /// A name from a step (set by the reader: an action's animation, "DivineImpact_Ready" → "Divine impact"), used
    /// when no block on the way names the move.
    /// </summary>
    public Func<object, string> StepLabel;

    /// <summary>Steps the IL can't see, from the live object (Yggdrasil's blocks name animations by tag).</summary>
    public Func<Node, IEnumerable<object>> ExtraSteps;

    /// <summary>
    /// What a step looks like (action name + animation), to spot the same move built twice (distance variants of a
    /// dash are separate objects with the same action). Without it, steps are compared as objects.
    /// </summary>
    public Func<object, string> StepKey;

    // Objects the AI holds in its own fields: a tree it starts from a plain method (Yggdrasil's phase 2 sequence).
    private readonly HashSet<object> _aiHeld = new();
    private readonly List<object> _aiHeldOrder = new(); // in field order: _behaviours before _phase2Sequence

    private string AttackLabel(Unit root)
    {
        var chain = new List<Unit> { root };
        var node = root;
        for (int i = 0; i < 12; i++)
        {
            var moving = node.Calls.Where(c => c.Steps.Count > 0).ToList();
            if (node.Own.Count > 0 || moving.Count != 1)
                break;
            node = moving[0];
            chain.Add(node);
        }
        // A sequence named after its first part ("Intro", taken from its child) doesn't name the move when a block
        // above it does ("Fist power slam" > Sequence[Intro, Slam, Outro]). A move name taken from a child still wins
        // (First Hero: "Horizontal slash + end motion" > Sequence[Horizontal slash, End motion] is "Horizontal slash").
        bool named = chain.Any(n => !n.Borrowed && Nice(n.Label));
        foreach (var n in Enumerable.Reverse(chain))
            if (Nice(n.Label) && !(named && n.Borrowed && n.Kind == Kind.Sequence && PartWord.IsMatch(n.Label)))
                return n.Label;
        var parts = node.Calls.Where(c => c.Steps.Count > 0).Select(BestName)
            .Where(l => l.Length > 0 && !Regex.IsMatch(l, @"(?i)\bidle\b|\bend\b|\bskip|^cast(ing)?\b")).Distinct().Take(3).ToList();
        if (parts.Count > 0)
            return string.Join(" + ", parts);
        if (StepLabel != null)
            foreach (var step in node.Steps)
                if (StepLabel(step) is { } fromStep && Nice(fromStep))
                    return fromStep;
        return chain.Select(n => n.Label).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
    }

    private static string BestName(Unit u)
    {
        for (int i = 0; i < 12 && u != null; i++)
        {
            if (Nice(u.Label))
                return u.Label;
            u = u.Own.Count == 0 ? u.Calls.Where(c => c.Steps.Count > 0).OrderByDescending(c => c.Steps.Count).FirstOrDefault() : null;
        }
        return "";
    }

    /// <summary>Whether <paramref name="from"/> runs <paramref name="to"/>, directly or through helpers.</summary>
    public static bool Reaches(Unit from, Unit to)
    {
        var seen = new HashSet<Unit>();
        var open = new Stack<Unit>(new[] { from });
        while (open.Count > 0)
            foreach (var c in open.Pop().Calls)
            {
                if (c == to)
                    return true;
                if (seen.Add(c))
                    open.Push(c);
            }
        return false;
    }

    private void AddAttack(List<Unit> units)
    {
        var steps = units.SelectMany(u => u.Steps).Distinct().ToList();
        // The same steps under another name (CastTwinMeteor / CastPredictTwinMeteor) are the same attack.
        var same = Attacks.FirstOrDefault(a => a.Steps.SequenceEqual(steps));
        var attack = same ?? new Attack { Label = units.OrderByDescending(u => u.Steps.Count).First().Label };
        if (same == null)
        {
            attack.Steps.AddRange(steps);
            Attacks.Add(attack);
        }
        foreach (var u in units)
        {
            attack.Units.Add(u);
            if (!_attackOf.ContainsKey(u) || units.Count == 1) // on its own it keeps its own name
                _attackOf[u] = attack;
        }
        // Two attacks with one label get a number for now; MergeDuplicates joins them afterwards.
        if (same == null && Attacks.Count(a => a.Label == attack.Label) > 1)
        {
            attack.BaseLabel = attack.Label;
            attack.Label += " " + Attacks.Count(a => a.Label.StartsWith(attack.Label));
        }
    }

    private string LabelOf(Node owner, MethodInfo method)
    {
        if (method.Name == "CRun" && !_aiType.IsAssignableFrom(owner.Type))
        {
            // Pope's move blocks are all called "Attack": a boss-specific block type names the move (Baptism,
            // Nervousness, Worship); framework blocks (ActionAttack, RunAction, Sequence) don't.
            string name = Tidy(_nameOf(owner));
            string ns = owner.Type.Namespace ?? "";
            if (GenericName.IsMatch(name) && ns.StartsWith("Characters.AI.Behaviours.") && !ns.EndsWith(".Attacks"))
                name = Tidy(owner.Type.Name);
            return name;
        }
        return Tidy(Regex.Replace(method.Name, "^(Cast|Order|Run|Do|C)(?=[A-Z])", ""));
    }

    // ------------------------------------------------------------------ report

    /// <summary>A readable table of the attacks and how they were found (for Codex/Debug and tests).</summary>
    public string Report(Func<object, string> stepName)
    {
        var sb = new StringBuilder();
        foreach (var a in Attacks)
            sb.Append(a.Label).Append(a.IsTail ? " (tail)" : "").Append(" = ").Append(string.Join(" + ", a.Units.Select(u => u.ToString())))
              .Append("\n    ").Append(string.Join(", ", a.Steps.Select(stepName))).Append('\n');
        sb.Append("dispatchers: ").Append(string.Join(", ", Units.Where(u => u.Dispatcher))).Append('\n');
        sb.Append("helpers: ").Append(string.Join(", ", Units.Where(u => !u.Entry && !u.Dispatcher && u.Steps.Count > 0))).Append('\n');
        if (Unused.Count > 0)
            sb.Append("unused (nothing runs them): ").Append(string.Join(", ", Unused.Select(u => $"{u} \"{u.Label}\""))).Append('\n');
        return sb.ToString();
    }

    /// <summary>The tree(s) below the dispatchers nothing else runs, indented (for Codex/Debug).</summary>
    public string TreeReport()
    {
        var sb = new StringBuilder();
        var seen = new HashSet<Unit>();
        void Write(Unit u, int depth)
        {
            sb.Append(' ', depth * 2).Append(u.Dispatcher ? "[dispatch] " : u.Kind == Kind.Sequence ? "[sequence] " : u.Entry ? "[attack] " : "")
              .Append(u.Name).Append(" \"").Append(u.Label).Append("\" steps=").Append(u.Steps.Count)
              .Append(string.IsNullOrEmpty(u.Note) ? "" : "  {" + u.Note + "}").Append('\n');
            if (!seen.Add(u) || depth > 12)
                return;
            foreach (var c in u.Calls)
                Write(c, depth + 1);
        }
        foreach (var root in Units.Where(u => u.CalledBy.Count == 0 && (u.Dispatcher || u.Calls.Count > 0)))
            Write(root, 0);
        return sb.ToString();
    }
}
