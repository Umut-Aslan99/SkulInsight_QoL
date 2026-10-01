using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// Which of an enemy's actions belong to one attack. Boss AIs chain several actions into one attack and name the
/// fields after it: "_goldenMeteorJump", "_goldenMeteorReady", "_goldenMeteorAttack", "_goldenMeteorLanding" are all
/// "Golden meteor"; "_rushReady", "_rushA" ... "_rushStanding" are "Rush". Small pattern components with only step
/// names ("DarkRush": _fristAttack, _secondAttack, _finishAttack, _standing) are named after the component.
/// </summary>
public static class AttackPatterns
{
    private static readonly HashSet<string> StepWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "jump", "ready", "prepare", "preparing", "attack", "attacks", "landing", "land", "standing", "stand", "finish",
        "end", "ending", "escape", "start", "and", "a", "b", "c", "d", "e", "fall", "loop", "first", "frist", "second",
        "third", "last", "on", "hardmode", "cool", "time", "cooltime", "cast", "casting", "action", "motion", "in", "air", "ground",
    };

    // "in", "air", "ground" are step words only at the end ("meteorInAirJump" stays "meteor in air").
    private static readonly HashSet<string> TrailingOnly = new(StringComparer.OrdinalIgnoreCase) { "in", "air", "ground", "on" };

    /// <summary>Pattern name for an action field ("_goldenMeteorReady" → "golden meteor"), "" if it is only step words.</summary>
    public static string PatternOf(string fieldName)
    {
        var words = Regex.Split(fieldName.TrimStart('_'), @"(?<=[a-z0-9])(?=[A-Z])|_").Where(w => w.Length > 0).ToList();
        // Cut at the first step word (not counting words that only end a name), then drop trailing step words.
        int cut = words.FindIndex(w => StepWords.Contains(w) && !TrailingOnly.Contains(w));
        if (cut >= 0)
            words = words.Take(cut).ToList();
        while (words.Count > 0 && StepWords.Contains(words[words.Count - 1]) && words.Count > 1 && TrailingOnly.Contains(words[words.Count - 1]) == false)
            words.RemoveAt(words.Count - 1);
        return string.Join(" ", words).ToLowerInvariant();
    }

    private static readonly Dictionary<Type, FieldInfo[]> Fields = new();

    /// <summary>Action → (attack label, order) for every action an AI or pattern component of the enemy refers to.</summary>
    public static Dictionary<Characters.Actions.Action, (string label, int order)> Map(Characters.Character enemy)
    {
        var map = new Dictionary<Characters.Actions.Action, (string, int)>();
        int order = 0;
        foreach (var component in enemy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component is Characters.Actions.Action || component is Characters.Actions.Motion)
                continue;
            var fields = FieldsOf(component.GetType());
            if (fields.Length == 0)
                continue;
            string componentLabel = Recording.OwnerNames.Humanize(component.GetType().Name);
            bool isAi = component.GetType().Name.EndsWith("AI") || component is Characters.AI.AIController;
            foreach (var field in fields)
            {
                if (field.Name.IndexOf("hardmode", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue; // the Dark Mirror variant of a step; the normal one is enough for the picture
                if (!(field.GetValue(component) is Characters.Actions.Action action) || action == null || map.ContainsKey(action))
                    continue;
                string pattern = PatternOf(field.Name);
                // "_attack" in an ordinary enemy AI is one move, not the name of the whole AI: leave it alone.
                if (pattern.Length == 0 && isAi)
                    continue;
                string label = pattern.Length > 0 ? char.ToUpperInvariant(pattern[0]) + pattern.Substring(1) : componentLabel;
                map[action] = (label, order++);
            }
        }
        return map;
    }

    private static int _cachedFor;
    private static Dictionary<Characters.Actions.Action, (string label, int order)> _cached;

    /// <summary>The attack an action belongs to ("Golden meteor"), or the action's own name if it is in none.</summary>
    public static string LabelOf(Characters.Character enemy, Characters.Actions.Action action)
    {
        if (enemy.GetInstanceID() != _cachedFor || _cached == null)
        {
            _cached = Map(enemy);
            _cachedFor = enemy.GetInstanceID();
        }
        return _cached.TryGetValue(action, out var group) ? group.label : CodexAnimations.ActionLabel(action);
    }

    private static FieldInfo[] FieldsOf(Type type)
    {
        if (Fields.TryGetValue(type, out var fields))
            return fields;
        var list = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            list.InsertRange(0, t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Characters.Actions.Action).IsAssignableFrom(f.FieldType)));
        return Fields[type] = list.ToArray();
    }
}
