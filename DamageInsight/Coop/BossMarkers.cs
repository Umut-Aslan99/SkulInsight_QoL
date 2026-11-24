using System;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using Characters.AI;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// The points an enemy's AI places just before it starts a move, which the move then reads. US tester's run 2026-11-22
/// (COOP_LOG 65 (d)): the twin sisters' cross dash on a client showed no warning lines where they meet.
/// `GoldenAideAI.CastTwinMeteor` / `CastPredictTwinMeteor` put `_twinMeteorDestination` at the target's x (or a
/// random spot near it) and then start `_twinMeteorPreparing`, which draws toward it; a puppet's AI never runs on a
/// client, so the point stayed where it was. Now the host's move event carries the AI's Transform fields that moved
/// since that enemy's last move (from the room's origin, <see cref="EnemyMarker"/>), and a client puts them there
/// before it plays the move. Only fields of the AI's own class (and its bases below <see cref="AIController"/>), not
/// the character's own transform or anything above it.
/// </summary>
// REVIEW(fable): a network message change (trigger 1: an optional tail on Motion, ignored by 0.17.0) and host-made
// state on clients (trigger 2); built from the game code, not yet seen in a game.
internal static class BossMarkers
{
    private const int Max = 16;
    private const float MovedSqr = 0.0004f; // 2 cm

    private static readonly Dictionary<Type, FieldInfo[]> FieldsOf = new();
    private static readonly Dictionary<(Character, int), Vector2> Sent = new();

    public static void Clear()
    {
        PointsCache.Clear();
        Sent.Clear();
    }

    /// <summary>Host: the AI's points that moved since this enemy's last move, from <paramref name="origin"/>; null: none.</summary>
    public static List<EnemyMarker> Changed(Character enemy, Vector2 origin)
    {
        var points = PointsOf(enemy);
        if (points.Length == 0)
            return null;
        List<EnemyMarker> changed = null;
        for (int i = 0; i < points.Length && i < 255; i++)
        {
            var t = Get(points[i]);
            if (t == null || NotItsOwn(enemy, t))
                continue;
            // A point the AI moved has a new LOCAL position (the sisters' sign destination sits under their own body);
            // one that only rides along with the body (a fire point) keeps its local position and is never sent, so a
            // client never shifts it against its own puppet. The first sight is the baseline (both games start from
            // the same prefab).
            Vector2 local = t.localPosition;
            if (!Sent.TryGetValue((enemy, i), out var last))
            {
                Sent[(enemy, i)] = local;
                continue;
            }
            if ((last - local).sqrMagnitude < MovedSqr)
                continue;
            Sent[(enemy, i)] = local;
            Vector2 at = (Vector2)t.position - origin;
            changed ??= new List<EnemyMarker>(4);
            changed.Add(new EnemyMarker { Index = (byte)i, X = at.x, Y = at.y });
            if (changed.Count >= Max)
                break;
        }
        return changed;
    }

    /// <summary>Client: puts the host's points in place before the move plays. The number of points put.</summary>
    public static int Apply(Character enemy, List<EnemyMarker> markers, Vector2 origin)
    {
        if (markers == null || markers.Count == 0)
            return 0;
        var points = PointsOf(enemy);
        int put = 0;
        foreach (var marker in markers)
        {
            var t = marker.Index < points.Length ? Get(points[marker.Index]) : null;
            if (t == null || NotItsOwn(enemy, t))
                continue;
            t.position = new Vector3(origin.x + marker.X, origin.y + marker.Y, t.position.z);
            put++;
        }
        return put;
    }

    /// <summary>A place where a point is kept: an AI's Transform field, or a tree's Transform variable.</summary>
    private readonly struct Point
    {
        public readonly object Owner;
        public readonly FieldInfo Field;
        public readonly BehaviorDesigner.Runtime.SharedTransform Variable;

        public Point(object owner, FieldInfo field) { Owner = owner; Field = field; Variable = null; }
        public Point(BehaviorDesigner.Runtime.SharedTransform variable) { Owner = null; Field = null; Variable = variable; }
    }

    private static Transform Get(Point p) =>
        p.Variable != null ? p.Variable.Value : p.Field?.GetValue(p.Owner) as Transform;

    private static readonly Dictionary<Character, Point[]> PointsCache = new();

    /// <summary>The enemy's points, the same list on every game (same prefab): its AI's Transform fields, then the
    /// Transform variables of its behaviour trees by name (the dark First Hero's wall raid keeps its aim points
    /// there: `_originTransform` / `_destinationTransform` of CanUseDarkHeroWallRaid).</summary>
    private static Point[] PointsOf(Character enemy)
    {
        if (enemy == null)
            return Array.Empty<Point>();
        if (PointsCache.TryGetValue(enemy, out var known))
            return known;
        if (PointsCache.Count > 2000)
            PointsCache.Clear();
        var list = new List<Point>();
        var ai = enemy.GetComponentInChildren<AIController>(true) ?? enemy.GetComponentInParent<AIController>();
        if (ai != null)
            foreach (var f in Fields(ai.GetType()))
                list.Add(new Point(ai, f));
        try
        {
            var trees = new List<BehaviorDesigner.Runtime.Behavior>(enemy.GetComponentsInChildren<BehaviorDesigner.Runtime.Behavior>(true));
            var container = enemy.GetComponentInParent<Enemy>();
            if (container?.behaviors != null)
                foreach (var tree in container.behaviors)
                    if (tree != null && !trees.Contains(tree))
                        trees.Add(tree);
            trees.Sort((a, b) => string.CompareOrdinal(TreePath(a, enemy), TreePath(b, enemy)));
            foreach (var tree in trees)
            {
                var variables = tree.GetAllVariables();
                if (variables == null)
                    continue;
                var shared = new List<BehaviorDesigner.Runtime.SharedTransform>();
                foreach (var v in variables)
                    if (v is BehaviorDesigner.Runtime.SharedTransform t)
                        shared.Add(t);
                shared.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var t in shared)
                    list.Add(new Point(t));
            }
        }
        catch (Exception) { /* a tree that isn't loaded yet: its points stay out */ }
        return PointsCache[enemy] = list.ToArray();
    }

    private static string TreePath(BehaviorDesigner.Runtime.Behavior tree, Character enemy)
    {
        var root = enemy.transform.root;
        var names = new List<string>();
        for (var t = tree.transform; t != null && t != root; t = t.parent)
            names.Add(t.name);
        names.Reverse();
        return string.Join("/", names) + "#" + tree.BehaviorName;
    }

    /// <summary>The Transform fields of an AI class and its bases below AIController, in a fixed order (the same build
    /// on every game, so an index names the same field).</summary>
    private static FieldInfo[] Fields(Type type)
    {
        if (FieldsOf.TryGetValue(type, out var fields))
            return fields;
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(AIController) && t != typeof(MonoBehaviour); t = t.BaseType)
        {
            var own = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Array.Sort(own, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var f in own)
                if (f.FieldType == typeof(Transform))
                    list.Add(f);
        }
        return FieldsOf[type] = list.ToArray();
    }

    /// <summary>
    /// Not a point to move: the character's own transform or one above it (the snapshots move those), or anything of
    /// another character (a tree variable may hold the player's transform: a client must never move a player).
    /// </summary>
    private static bool NotItsOwn(Character enemy, Transform t)
    {
        for (var e = enemy.transform; e != null; e = e.parent)
            if (e == t)
                return true;
        var owner = t.GetComponentInParent<Character>();
        return owner != null && owner != enemy;
    }
}
