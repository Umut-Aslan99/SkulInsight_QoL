#if DEV // a developer tool: release builds don't lead bosses
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Characters;
using Characters.AI;
using UnityEngine;
using B = Characters.AI.Behaviours;
using C = Characters.AI.Conditions;

namespace DamageInsight.Codex;

/// <summary>
/// Behaviour-block bosses (Yggdrasil, Pope, First Hero 1-3): their AI runs a tree of blocks (Sequence, Selector,
/// WeightedSelector, Conditional, Chance, ...), led like a BehaviorDesigner tree. Blocks run as coroutines from the AI's
/// own loop, so a way is only taken from the part of the tree the boss is in now: one of its blocks started in the last
/// 20 s (Pope's second phase is no way while his first phase loops).
/// </summary>
public static partial class MoveDirector
{
    private const float LiveWindow = 20f;

    /// <summary>Block conditions that may be made to pass: HP, cooldowns, distance and position checks.</summary>
    public static readonly Type[] ForcedConditions =
    {
        typeof(C.HealthCondition), typeof(C.CoolDown), typeof(C.BehaviourCoolTime), typeof(C.BetweenTargetAndWall),
        typeof(C.CompareDistanceFromWall), typeof(C.CheckCollision), typeof(C.TargetIsGrounded),
    };

    private static readonly Dictionary<Character, BlockTree> Blocks = new();
    private static readonly HashSet<object> Watched = new();            // the blocks of the led bosses
    private static readonly Dictionary<object, float> LastRun = new();  // block → when it last started

    /// <summary>A block started (hooks on BehaviourInfo and the selectors): tells which part of its tree the boss is in.</summary>
    public static void Ran(object block)
    {
        if (Watched.Count > 0 && Watched.Contains(block))
            LastRun[block] = Time.unscaledTime;
    }

    private static BlockTree BlockTreeOf(Character boss)
    {
        if (!Blocks.TryGetValue(boss, out var tree))
        {
            foreach (var gone in Blocks.Keys.Where(k => k == null).ToList())
                Blocks.Remove(gone);
            Blocks[boss] = tree = new BlockTree(boss.GetComponentsInChildren<AIController>(true));
            Watched.Clear();
            LastRun.Clear();
            foreach (var t in Blocks.Values)
                Watched.UnionWith(t.Objects);
            if (tree.Count > 0)
                Plugin.Log.LogInfo($"Move director: blocks of {boss.name}: {tree.Summary()}.");
        }
        return tree.Count > 0 ? tree : null;
    }

    private sealed class BlockTree : ILiveTree
    {
        private readonly List<object> _objects = new();
        private readonly List<int> _parents = new(), _slots = new(), _guards = new();
        private readonly List<Kind> _kinds = new();
        private readonly List<List<int>> _children = new();
        private readonly Dictionary<object, List<int>> _index = new();

        public int Count => _objects.Count;
        public IEnumerable<object> Objects => _objects;

        public BlockTree(IEnumerable<AIController> ais)
        {
            var seen = new HashSet<object>();
            foreach (var ai in ais.Where(a => a != null))
                foreach (var root in Roots(ai, 0, seen))
                    Add(root, -1, -1, 0);
        }

        /// <summary>A block and all below it; a block met twice is a node at each place (not below itself).</summary>
        private void Add(object block, int parent, int slot, int depth)
        {
            if (block == null || block is UnityEngine.Object o && o == null || depth > 30 || Above(parent, block))
                return;
            int node = New(block, parent, slot, KindOfBlock(block), true);
            if (block is B.Conditional conditional)
                _guards[node] = New(conditional._condition, -1, -1,
                    conditional._condition != null && ForcedConditions.Contains(conditional._condition.GetType()) ? Kind.Force : Kind.Other, false);
            else if (block is B.Chance or B.CoolTime or B.Count)
                _guards[node] = New(block, -1, -1, Kind.Force, false); // the gate's own check: forced through the block itself
            var children = ChildrenOf(block);
            for (int i = 0; i < children.Count; i++)
                Add(children[i], node, i, depth + 1);
        }

        private int New(object block, int parent, int slot, Kind kind, bool indexed)
        {
            int node = _objects.Count;
            _objects.Add(block);
            _parents.Add(parent);
            _slots.Add(slot);
            _kinds.Add(kind);
            _guards.Add(-1);
            _children.Add(new List<int>());
            if (parent >= 0)
                _children[parent].Add(node);
            if (block != null && indexed)
            {
                if (!_index.TryGetValue(block, out var list))
                    _index[block] = list = new List<int>();
                list.Add(node);
            }
            return node;
        }

        private bool Above(int node, object block)
        {
            for (; node >= 0; node = _parents[node])
                if (ReferenceEquals(_objects[node], block))
                    return true;
            return false;
        }

        public object Task(int node) => _objects[node];
        public int IndexOf(object task) => _index.TryGetValue(task, out var list) ? list[0] : -1;
        public IEnumerable<int> IndicesOf(object task) => _index.TryGetValue(task, out var list) ? list : Enumerable.Empty<int>();
        public int Parent(int node) => _parents[node];
        public int Slot(int node) => _slots[node];
        public IReadOnlyList<int> Children(int node) => _children[node];
        public Kind KindOf(int node) => _kinds[node];
        public int Guard(int node) => _guards[node];
        public bool Holds(int state) => false;
        public bool Makes(int setter, int state, bool pass) => false;
        public IEnumerable<int> Setters => Enumerable.Empty<int>();

        public bool Reachable(Plan plan)
        {
            float since = Time.unscaledTime - LiveWindow;
            for (int node = plan.Goal; node >= 0; node = _parents[node])
                if (LastRun.TryGetValue(_objects[node], out float at) && at >= since)
                    return true;
            return false;
        }

        public string Summary() =>
            $"{Count} nodes, {_kinds.Count(k => k is Kind.Selector or Kind.PickOne)} selectors, " +
            $"{_kinds.Count(k => k == Kind.Gate)} gates, {_kinds.Count(k => k == Kind.Force)} checks that can pass";

        private static Kind KindOfBlock(object block) => block switch
        {
            B.BehaviourInfo => Kind.Pass,
            B.Sequence => Kind.Sequence,
            B.Selector => Kind.Selector,
            B.WeightedSelector or B.UniformSelector or B.RandomBehaviour => Kind.PickOne,
            B.Conditional or B.Chance or B.CoolTime or B.Count => Kind.Gate,
            _ => Kind.Other,
        };

        /// <summary>A block's children in the game's order (the index a selector picks by).</summary>
        private static List<object> ChildrenOf(object block)
        {
            switch (block)
            {
                case B.BehaviourInfo info:
                    return new List<object> { info._behaviour };
                case B.Sequence sequence:
                    return (sequence._children?._components ?? new B.BehaviourInfo[0]).Cast<object>().ToList();
                case B.Selector selector:
                    return (selector._children?._components ?? new B.BehaviourInfo[0]).Cast<object>().ToList();
                case B.RandomBehaviour random:
                    return (random._behaviours?._components ?? new B.BehaviourInfo[0]).Cast<object>().ToList();
                case B.WeightedSelector weighted:
                    return (weighted._weights?._components ?? new B.Weight[0]).Select(w => (object)w?.key).ToList();
                case B.UniformSelector uniform:
                    return (uniform._weights?._components ?? new B.Weight[0]).Select(w => (object)w?.key).ToList();
                case B.Behaviour behaviour:
                    // Decorators (_behaviour) and the game's own blocks: every block they hold, in field order.
                    var list = new List<object>();
                    foreach (var field in FieldsOf(behaviour.GetType(), typeof(B.Behaviour)))
                        foreach (var value in HeldBlocks(Read(field, behaviour)))
                            list.Add(value);
                    return list;
                default:
                    return new List<object>();
            }
        }

        /// <summary>The blocks a field value holds: a block, a BehaviourInfo, or an array/list of them.</summary>
        private static IEnumerable<object> HeldBlocks(object value)
        {
            switch (value)
            {
                case B.Behaviour or B.BehaviourInfo:
                    yield return value;
                    break;
                case B.Weight weight:
                    yield return weight.key;
                    break;
                case B.BehaviourInfo.Subcomponents infos:
                    foreach (var info in infos._components ?? new B.BehaviourInfo[0])
                        yield return info;
                    break;
                case B.Behaviour.Subcomponents behaviours:
                    foreach (var b in behaviours._components ?? new B.Behaviour[0])
                        yield return b;
                    break;
                case IEnumerable items and not string and not Component:
                    foreach (var item in items)
                        if (item is B.Behaviour or B.BehaviourInfo)
                            yield return item;
                    break;
            }
        }

        /// <summary>
        /// The AI's top blocks: its block fields, also inside its own helper objects (Pope's phase sequence holds one
        /// block per phase).
        /// </summary>
        private static IEnumerable<object> Roots(object owner, int depth, HashSet<object> seen)
        {
            if (owner == null || depth > 3 || !seen.Add(owner))
                yield break;
            foreach (var field in FieldsOf(owner.GetType(), typeof(MonoBehaviour)))
            {
                var value = Read(field, owner);
                if (value is B.Behaviour or B.BehaviourInfo)
                {
                    if (seen.Add(value))
                        yield return value;
                }
                else if (value != null && value is not AIController && value is not Character && !(value is UnityEngine.Object u && u == null) &&
                         value.GetType().Namespace is { } ns && ns.StartsWith("Characters.AI") && !value.GetType().IsArray)
                    foreach (var root in Roots(value, depth + 1, seen))
                        yield return root;
            }
        }

        private static readonly Dictionary<(Type, Type), FieldInfo[]> FieldCache = new();

        private static FieldInfo[] FieldsOf(Type type, Type stop)
        {
            if (FieldCache.TryGetValue((type, stop), out var fields))
                return fields;
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != stop && t != typeof(object); t = t.BaseType)
                list.InsertRange(0, t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.FieldType.IsPrimitive && f.FieldType != typeof(string)));
            return FieldCache[(type, stop)] = list.ToArray();
        }

        private static object Read(FieldInfo field, object owner)
        {
            try { return field.GetValue(owner); }
            catch (Exception) { return null; }
        }
    }
}
#endif
