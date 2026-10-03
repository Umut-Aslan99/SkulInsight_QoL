using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Characters;
using HarmonyLib;
using UnityEngine;
using BD = BehaviorDesigner.Runtime;
using BDTasks = BehaviorDesigner.Runtime.Tasks;

namespace DamageInsight.Codex;

/// <summary>
/// The attacks of a live boss or adventurer (<see cref="AttackGraph"/>), built once per spawned character: its AI's
/// coroutines, its behaviour-block tree (Characters.AI.Behaviours: selectors, sequences, leaves) and its
/// BehaviorDesigner tree (adventurers). The report goes to Codex/Debug/&lt;key&gt;_attacks.txt, so a wrong grouping
/// can be checked without another fight.
/// </summary>
public static class BossAttacks
{
    private static readonly Dictionary<int, AttackGraph> Graphs = new();
    private static readonly Dictionary<Type, bool> Runnable = new();

    /// <summary>What counts as one step of an attack: an action, or an animation played directly (Chimera).</summary>
    public static readonly Type[] StepTypes = { typeof(Characters.Actions.Action), typeof(CharacterAnimationController.AnimationInfo) };

    /// <summary>Characters whose attacks are read and filmed: bosses and adventurers.</summary>
    public static bool Covers(Character c) => c != null && c.type is Character.Type.Boss or Character.Type.Adventurer;

    /// <summary>
    /// Whether a character has a mind of its own (an AI or a behaviour tree). Boss-type characters without one are
    /// pieces of a fight (Pope's dark crystals), shown on the boss's page instead of their own.
    /// </summary>
    public static bool HasAi(Character c) =>
        c != null && (c.GetComponentInChildren<Characters.AI.AIController>(true) != null || c.GetComponentInChildren<BD.Behavior>(true) != null);

    public static AttackGraph Of(Character enemy)
    {
        int id = enemy.GetInstanceID();
        if (Graphs.TryGetValue(id, out var graph))
            return graph;
        if (Graphs.Count > 64)
            Graphs.Clear();
        var started = DateTime.UtcNow;
        var game = typeof(Characters.AI.AIController).Assembly;
        var nodes = enemy.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(c => c != null && c.GetType().Assembly == game && (c is Characters.AI.AIController || HasRun(c.GetType())))
            .Select(c => new AttackGraph.Node(c.GetType(), c, ""))
            .ToList();
        graph = new AttackGraph(typeof(Characters.AI.AIController), StepTypes, n => n.Value is Component c ? c.name : n.Type.Name)
            { StepLabel = StepName, StepKey = StepKeyOf, ExtraSteps = TaggedAnimations }
            .Build(nodes, TreeOf, g => AddBehaviorDesigner(g, enemy));
        if (graph.Incomplete)
            return graph; // a behaviour tree isn't loaded yet: read it again next time
        foreach (var unit in graph.Units)
            unit.Note = NoteOf(unit.Owner.Value);
        AddSections(graph);
        Graphs[id] = graph;
        if (graph.Attacks.Count > 0)
            Plugin.Log.LogInfo($"Codex: {CodexCatalog.CleanName(enemy.name)} has {graph.Attacks.Count} attacks " +
                               $"(read in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms).");
        return graph;
    }

    /// <summary>
    /// What a decision block checks, with its values from the game data, for the report (and later the "When" hints):
    /// a Conditional's condition ("HealthCondition(compare=LessThan, percent=0.8)"), a decorator's own settings
    /// (CoolTime value, Chance), a weighted pick's weights, a BehaviorDesigner condition's shared values.
    /// </summary>
    private static string NoteOf(object value)
    {
        try
        {
            switch (value)
            {
                case Characters.AI.Behaviours.Conditional conditional:
                    return conditional._condition != null ? Settings(conditional._condition, typeof(MonoBehaviour)) : null;
                case Characters.AI.Behaviours.WeightedSelector weighted:
                    return "weights: " + string.Join(", ", (weighted._weights?.components ?? Array.Empty<Characters.AI.Behaviours.Weight>())
                        .Where(w => w != null).Select(w => $"{(w._tag is { Length: > 0 } t ? t : w.key != null ? w.key.name : "?")}={w.value}"));
                case Characters.AI.Behaviours.Decorator decorator:
                    return Settings(decorator, typeof(Characters.AI.Behaviours.Decorator));
                case BDTasks.Task task when task is BDTasks.Conditional || !(task is BDTasks.ParentTask) && StepsOf(task).Count == 0:
                    return Settings(task, typeof(BDTasks.Task));
                case BDTasks.ParentTask parent when parent.GetType().Name.Contains("Weighted"):
                    return Settings(parent, typeof(BDTasks.ParentTask));
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"Type(name=value, ...)" for the simple settings of an object (numbers, enums, text, shared values).</summary>
    private static string Settings(object o, Type stop)
    {
        var parts = new List<string>();
        foreach (var f in Fields(o.GetType(), stop))
        {
            var v = Read(f, o);
            if (v is BD.SharedVariable shared)
                v = shared.GetValue();
            if (v == null || v is UnityEngine.Object || v is Delegate)
                continue;
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string || v is Vector2 || v is Vector2Int)
                parts.Add($"{f.Name.TrimStart('_')}={(v is float fl ? fl.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : v)}");
            else if (v is System.Collections.IList list && list.Count <= 8 && list.Cast<object>().All(x => x != null && (x.GetType().IsPrimitive || x is string)))
                parts.Add($"{f.Name.TrimStart('_')}=[{string.Join(",", list.Cast<object>())}]");
        }
        return parts.Count == 0 ? o.GetType().Name : $"{o.GetType().Name}({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Fight parts as label sections ("Pair phase · Twin meteor"), which the book groups like Yggdrasil's phases.
    /// The Leiana sisters: the twin attacks the conductor AI starts on both of them (CastTwin…, CastPredictTwin…,
    /// EscapeForTwin…) are the pair phase, the rest the single phase; the intro and the awakening stand apart.
    /// </summary>
    private static void AddSections(AttackGraph graph)
    {
        if (!graph.Units.Any(u => u.OnAi && u.Owner.Type == typeof(Characters.AI.TwinSister.GoldenAideAI)))
            return;
        foreach (var attack in graph.Attacks)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(attack.Label, @"(?i)^(intro|awakening)$") || attack.Label.Contains(" · "))
                continue;
            bool pair = attack.Units.Any(u => u.Method != null &&
                System.Text.RegularExpressions.Regex.IsMatch(u.Method.Name, "^(CastTwin|CastPredictTwin|EscapeForTwin)"));
            attack.Label = (pair ? "Pair phase · " : "Single phase · ") + attack.Label;
        }
    }

    /// <summary>What a step looks like: its action's name and animations (distance variants of a dash look alike).</summary>
    private static string StepKeyOf(object step)
    {
        try
        {
            switch (step)
            {
                case Characters.Actions.Action action:
                    return action.name + "|" + string.Join("/", (action.motions ?? Array.Empty<Characters.Actions.Motion>())
                        .Where(m => m?.animationInfo != null).SelectMany(m => ClipNames(m.animationInfo)));
                case CharacterAnimationController.AnimationInfo info:
                    return "anim|" + string.Join("/", ClipNames(info));
                default:
                    return step?.ToString() ?? "";
            }
        }
        catch (Exception)
        {
            return step?.ToString() ?? "";
        }
    }

    /// <summary>
    /// Yggdrasil's blocks (PlayAnimations, Sweeping, Awakening) play animations by tag through its animation
    /// controller; the controller's phase 1 / phase 2 tables turn the tags into animations (the steps of its moves).
    /// </summary>
    private static IEnumerable<object> TaggedAnimations(AttackGraph.Node node)
    {
        if (!(node.Value is MonoBehaviour owner))
            yield break;
        Characters.AI.YggdrasillElderEnt.YggdrasillAnimationController controller = null;
        var tags = new List<Characters.AI.YggdrasillElderEnt.YggdrasillAnimation.Tag>();
        foreach (var f in Fields(owner.GetType(), typeof(MonoBehaviour)))
        {
            var v = Read(f, owner);
            if (v is Characters.AI.YggdrasillElderEnt.YggdrasillAnimationController c && c != null)
                controller = c;
            else if (v is Characters.AI.YggdrasillElderEnt.YggdrasillAnimation.Tag tag)
                tags.Add(tag);
            else if (v is Characters.AI.YggdrasillElderEnt.YggdrasillAnimation.Tag[] many)
                tags.AddRange(many);
        }
        if (controller == null)
            yield break;
        foreach (var tag in tags)
            if ((controller._phase1Mapper != null && controller._phase1Mapper.TryGetValue(tag, out var info)) ||
                (controller._phase2Mapper != null && controller._phase2Mapper.TryGetValue(tag, out info)))
                if (info != null)
                    yield return info;
    }

    /// <summary>A move name from a step's first animation: "DivineImpact_Ready" → "Divine impact" (null if none).</summary>
    private static string StepName(object step)
    {
        try
        {
            var info = step is Characters.Actions.Action action
                ? action.motions?.FirstOrDefault(m => m?.animationInfo?.values != null)?.animationInfo
                : step as CharacterAnimationController.AnimationInfo;
            string clip = info?.values?.FirstOrDefault(v => v?.clip != null)?.clip.name;
            if (string.IsNullOrEmpty(clip))
                return null;
            clip = System.Text.RegularExpressions.Regex.Replace(clip,
                @"(_(Ready|Attack|Loop|End|Start|Intro|Outro|In|Out)\d*)+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return AttackGraph.Tidy(clip);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasRun(Type type)
    {
        if (!Runnable.TryGetValue(type, out bool has))
            Runnable[type] = has = AccessTools.Method(type, "CRun") is { } m && typeof(IEnumerator).IsAssignableFrom(m.ReturnType);
        return has;
    }

    // ------------------------------------------------------------------ behaviour-block trees

    private static readonly HashSet<string> Selectors = new()
    {
        "Selector", "UniformSelector", "WeightedSelector", "RandomBehaviour", "Repeat", "InfiniteLoop", "TimeLoop",
        "Chance", "CoolTime", "Count", "Conditional", "Height",
    };

    /// <summary>
    /// Whether a live object is a tree block, and the blocks it runs: a sequence runs its children one after the
    /// other (one attack), selectors and loops pick or repeat them (dispatchers). Children are found in the block's
    /// fields, lists and subcomponent arrays; a BehaviourInfo or Weight wrapper gives the designers' name (tag).
    /// </summary>
    private static (AttackGraph.Kind, IEnumerable<(AttackGraph.Node, string)>) TreeOf(AttackGraph.Node node)
    {
        var type = node.Type;
        var kind = AttackGraph.Kind.Plain;
        if (type == typeof(Characters.AI.Behaviours.Sequence))
            kind = AttackGraph.Kind.Sequence;
        else if (typeof(Characters.AI.Behaviours.Decorator).IsAssignableFrom(type) ||
                 (type.Namespace == "Characters.AI.Behaviours" && Selectors.Contains(type.Name)) ||
                 type == typeof(Characters.AI.Pope.Sequence))
            kind = AttackGraph.Kind.Dispatcher;
        if (kind == AttackGraph.Kind.Plain || !(node.Value is MonoBehaviour owner))
            return (kind, Enumerable.Empty<(AttackGraph.Node, string)>());
        return (kind, ChildrenOf(owner));
    }

    private static List<(AttackGraph.Node, string)> ChildrenOf(MonoBehaviour owner)
    {
        var result = new List<(AttackGraph.Node, string)>();
        var seen = new HashSet<object>();
        void Visit(object value, string tag, int depth)
        {
            if (value == null || depth > 5 || value is Delegate || value is string || value.GetType().IsValueType)
                return;
            if (value is UnityEngine.Object o && o == null)
                return;
            if (value is Characters.AI.Behaviours.Weight weight)
            {
                Visit(weight.key, weight._tag, depth + 1);
                return;
            }
            if (value is Characters.AI.Behaviours.Behaviour || value is Characters.AI.Behaviours.BehaviourInfo)
            {
                var mb = (MonoBehaviour)value;
                if (mb != owner && seen.Add(mb))
                    result.Add((new AttackGraph.Node(mb.GetType(), mb, ""), tag ?? (mb is Characters.AI.Behaviours.BehaviourInfo info ? info._tag : null)));
                return;
            }
            if (value is UnityEngine.Object || !seen.Add(value))
                return; // other components and assets are not children; plain objects only once
            if (value is IEnumerable list)
            {
                foreach (var item in list)
                    Visit(item, null, depth + 1);
                return;
            }
            // A plain data object (a subcomponent array, Pope's phase holders): look inside.
            foreach (var f in Fields(value.GetType(), typeof(object)))
                Visit(Read(f, value), null, depth + 1);
        }
        foreach (var f in Fields(owner.GetType(), typeof(MonoBehaviour)))
            Visit(Read(f, owner), null, 0);
        return result;
    }

    private static readonly Dictionary<(Type, Type), FieldInfo[]> FieldCache = new();

    private static FieldInfo[] Fields(Type type, Type stop)
    {
        if (FieldCache.TryGetValue((type, stop), out var fields))
            return fields;
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != stop && t != typeof(object); t = t.BaseType)
            list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        return FieldCache[(type, stop)] = list.ToArray();
    }

    private static object Read(FieldInfo f, object owner)
    {
        try { return f.GetValue(owner); }
        catch (Exception) { return null; }
    }

    // ------------------------------------------------------------------ BehaviorDesigner trees

    /// <summary>
    /// Adventurers (and some Dark Mirror bosses) run BehaviorDesigner trees. Their tasks are added as tree units:
    /// sequences are attacks, selectors and decorators dispatchers, and tasks that start actions (RunCharacterAction,
    /// RunCharacterActions, ...) leaves with those actions as steps.
    /// </summary>
    private static void AddBehaviorDesigner(AttackGraph graph, Character enemy)
    {
        foreach (var behavior in enemy.GetComponentsInChildren<BD.Behavior>(true))
        {
            // The running tree: BehaviorDesigner copies referenced sub-trees ("Earthquake", "SpecialSkill_Main") into
            // it when it loads, and these task objects are the ones that run. The saved tree only has the references.
            var manager = BD.BehaviorManager.instance;
            BD.BehaviorManager.BehaviorTree running = null;
            if (manager != null && manager.behaviorTreeMap != null)
                manager.behaviorTreeMap.TryGetValue(behavior, out running);
            if (running == null || running.taskList == null || running.taskList.Count == 0)
            {
                if (behavior.isActiveAndEnabled)
                    graph.Incomplete = true; // not loaded yet
                continue;
            }
            var tasks = running.taskList;
            var units = new AttackGraph.Unit[tasks.Count];
            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                if (task == null || task.Disabled)
                    continue;
                var kind = task is BDTasks.ParentTask
                    ? task.GetType().Name is "Sequence" or "RandomSequence" or "Parallel" or "ParallelComplete"
                        ? AttackGraph.Kind.Sequence
                        : AttackGraph.Kind.Dispatcher
                    : AttackGraph.Kind.Plain;
                var steps = kind == AttackGraph.Kind.Plain ? StepsOf(task) : new List<object>();
                units[i] = graph.AddTreeUnit(new AttackGraph.Node(task.GetType(), task, ""),
                    $"{task.GetType().Name} '{task.FriendlyName}'", kind, steps, LabelOf(task, steps), null);
            }
            for (int i = 0; i < tasks.Count; i++)
            {
                var children = running.childrenIndex != null && i < running.childrenIndex.Count ? running.childrenIndex[i] : null;
                if (units[i] == null || children == null)
                    continue;
                foreach (int c in children)
                    if (c >= 0 && c < units.Length && units[c] != null)
                        graph.Link(units[i], units[c]);
            }
        }
    }

    /// <summary>The actions a task starts: fields holding an action, a shared action variable, or a list of actions.</summary>
    private static List<object> StepsOf(BDTasks.Task task)
    {
        var steps = new List<object>();
        // Conditions (CanUseAction, CheckActionRunning) only look at an action; cancelling one doesn't play it.
        if (task is BDTasks.Conditional || task.GetType().Name.StartsWith("Cancel"))
            return steps;
        foreach (var f in Fields(task.GetType(), typeof(BDTasks.Task)))
        {
            var value = Read(f, task);
            if (value is BD.SharedCharacterAction shared)
                value = shared.Value;
            if (value is Characters.Actions.Action action && action != null)
                steps.Add(action);
            else if (value is IEnumerable<Characters.Actions.Action> actions)
                steps.AddRange(actions.Where(a => a != null));
        }
        return steps;
    }

    /// <summary>The designers' name of a task if it is readable (the book font has no Korean), else its first action's.</summary>
    private static string LabelOf(BDTasks.Task task, List<object> steps)
    {
        string name = task.FriendlyName ?? "";
        bool readable = name.Length > 0 && name.All(ch => ch < 128) &&
                        !string.Equals(name.Replace(" ", ""), task.GetType().Name, StringComparison.OrdinalIgnoreCase);
        var first = steps.OfType<Characters.Actions.Action>().FirstOrDefault();
        // "Run Special Skill" names the decision; the action says what it is ("SwordAuraWave(SpecialSkill)").
        if (readable && (first == null || AttackGraph.NamesAMove(name)))
            return AttackGraph.Tidy(name);
        return first != null ? AttackGraph.Tidy(first.name) : "";
    }

    // ------------------------------------------------------------------ helpers for the book

    /// <summary>The attacks shown in the book: everything but tails, which are shown inside the attacks they end.</summary>
    public static IEnumerable<AttackGraph.Attack> Shown(AttackGraph graph) => graph.Attacks.Where(a => !a.IsTail);

    /// <summary>Whether a step is only used on the Dark Mirror difficulty ("_meteorInGroundAttackOnHardmode").</summary>
    public static bool HardmodeOnly(AttackGraph graph, object step) =>
        graph.StepPath.TryGetValue(step, out var path) && path.IndexOf("hardmode", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Writes the attack table, the tree and the actions no attack uses to Codex/Debug for checking.</summary>
    public static void WriteReport(Character enemy, string key, AttackGraph graph)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("Attacks of ").Append(key).Append(" (").Append(enemy.type).Append("), read from the game's AI.\n");
            sb.Append("Spine figure: ").Append(enemy.GetComponentInChildren<Spine.Unity.SkeletonRenderer>(true) != null ? "yes" : "no")
              .Append("; AI components: ")
              .Append(string.Join(", ", enemy.GetComponentsInChildren<MonoBehaviour>(true)
                  .Where(c => c is Characters.AI.AIController || c is BD.Behavior).Select(c => c.GetType().Name).Distinct()))
              .Append("\n\n");
            sb.Append(graph.Report(step => Describe(graph, step)));
            sb.Append("\ntree:\n").Append(graph.TreeReport());
            var loose = enemy.GetComponentsInChildren<Characters.Actions.Action>(true).Where(a => !graph.Uses(a)).ToList();
            sb.Append("\nactions no attack uses: ").Append(loose.Count == 0 ? "none" : string.Join(", ", loose.Select(a => Describe(graph, a))));
            string path = Path.Combine(CodexTracker.Folder, "Debug", key + "_attacks.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            MoveHints.Write(key, graph);
            // The graph as the rules read it, to fix naming rules offline (AttackGraph.FromSnapshot).
            File.WriteAllText(Path.Combine(CodexTracker.Folder, "Debug", key + "_graph.json"),
                graph.SnapshotJson(StepKeyOf, StepName, step => Describe(graph, step)), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not write the attack report of {key}: {e.Message}");
        }
    }

    private static string Describe(AttackGraph graph, object step)
    {
        graph.StepPath.TryGetValue(step, out var path);
        string where = path != null ? " <" + path + ">" : "";
        if (step is CharacterAnimationController.AnimationInfo info)
            return $"animation{where} [{string.Join("/", ClipNames(info))}]";
        if (!(step is Characters.Actions.Action action))
            return step?.ToString() ?? "?";
        string clips = "";
        try
        {
            clips = string.Join("/", (action.motions ?? Array.Empty<Characters.Actions.Motion>())
                .Where(m => m != null && m.animationInfo != null).SelectMany(m => ClipNames(m.animationInfo).Take(1)));
        }
        catch (Exception) { /* no motions */ }
        return $"{action.name}{where}{(clips.Length > 0 ? " [" + clips + "]" : "")}";
    }

    private static IEnumerable<string> ClipNames(CharacterAnimationController.AnimationInfo info) =>
        info?.values == null ? Enumerable.Empty<string>() : info.values.Where(v => v?.clip != null).Select(v => v.clip.name);
}
