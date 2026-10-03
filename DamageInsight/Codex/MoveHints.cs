using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DamageInsight.Lang;

namespace DamageInsight.Codex;

/// <summary>
/// "When" hints for boss moves, written from the game's own AI: the conditions on the way from the AI's main loop to a
/// move (HP range, distance, cooldown, chance, stage or form, a grab that has to catch you, helping another
/// adventurer). Built when a fight starts (<see cref="Write"/> → <c>Codex/Hints/&lt;key&gt;.json</c>), read by the book.
/// </summary>
public static class MoveHints
{
    /// <summary>Every phrase a hint is made of (they are stored in English and translated when shown).</summary>
    private static readonly string[] Phrases =
    {
        Loc.N("below {0} % HP"), Loc.N("above {0} % HP"), Loc.N("at most every {0} s"), Loc.N("{0} % chance"),
        Loc.N("when you are within {0} units"), Loc.N("when you are more than {0} units away"), Loc.N("when you are far away"),
        Loc.N("when you are farther away"), Loc.N("when you are not too far"), Loc.N("when you are close"),
        Loc.N("only if the grab catches you"), Loc.N("at {0}–{1} % HP"), Loc.N("in stage {0}"), Loc.N("in Balance form"),
        Loc.N("in Power form"), Loc.N("in Speed form"), Loc.N("while helping another adventurer"),
    };

    /// <summary>A stored (English) hint in the current language.</summary>
    public static string Localize(string hint) =>
        string.Join(" · ", (hint ?? "").Split(new[] { " · " }, StringSplitOptions.None).Select(p => Loc.Phrase(p, Phrases)));

    /// <summary>Move label → hint ("below 40 % HP · at most every 80 s"), for every move that has conditions.</summary>
    public static Dictionary<string, string> Build(AttackGraph graph)
    {
        var hints = new Dictionary<string, string>();
        foreach (var attack in graph.Attacks.Where(a => !a.IsTail && a.Units.Count > 0))
        {
            string hint = HintFor(attack.Units.Where(u => u.Entry).DefaultIfEmpty(attack.Units[0]));
            if (hint.Length > 0 && !hints.ContainsKey(attack.Label))
                hints[attack.Label] = hint;
        }
        return hints;
    }

    /// <summary>
    /// The conditions on every way from the AI down to <paramref name="entry"/>, outermost first: a condition counts
    /// only if each way checks it (an adventurer's move used both alone and as a helper has no "helper" hint).
    /// </summary>
    public static string HintFor(AttackGraph.Unit entry) => HintFor(new[] { entry });

    /// <summary>The same for a move built several times (a copy in each pattern): only what every copy's way checks.</summary>
    public static string HintFor(IEnumerable<AttackGraph.Unit> entries)
    {
        var paths = new List<List<string>>();
        void Walk(AttackGraph.Unit node, List<string> parts, HashSet<AttackGraph.Unit> onPath)
        {
            if (paths.Count >= 512)
                return;
            var parents = node.CalledBy.Where(p => !onPath.Contains(p)).ToList();
            if (parents.Count == 0 || onPath.Count > 40)
            {
                paths.Add(parts);
                return;
            }
            foreach (var parent in parents)
            {
                var next = new List<string>(parts);
                // A sequence checks its conditions before the step that leads here (BehaviorDesigner: "Health
                // Comparison", "Can Use Action", then the move).
                if (parent.Kind == AttackGraph.Kind.Sequence)
                    foreach (var sibling in parent.Calls.TakeWhile(c => c != node).Where(c => c.Steps.Count == 0))
                        Add(next, Phrase(sibling));
                Add(next, Phrase(parent));
                onPath.Add(parent);
                Walk(parent, next, onPath);
                onPath.Remove(parent);
            }
        }
        foreach (var entry in entries)
            Walk(entry, new List<string>(), new HashSet<AttackGraph.Unit> { entry });
        if (paths.Count == 0)
            return "";
        var common = paths.Skip(1).Aggregate(new HashSet<string>(paths[0]), (set, path) => { set.IntersectWith(path); return set; });
        var ordered = paths[0].Where(common.Contains).Distinct().ToList();
        ordered.Reverse();
        // "at 65–30 % HP" already says "above 30 % HP" (the check inside that range).
        foreach (var range in ordered.Select(p => RangePhrase.Match(p)).Where(m => m.Success).ToList())
            ordered.RemoveAll(p => p == $"above {range.Groups[2].Value} % HP" || p == $"below {range.Groups[1].Value} % HP");
        return string.Join(" · ", ordered.Take(4));
    }

    private static void Add(List<string> parts, string phrase)
    {
        if (!string.IsNullOrEmpty(phrase))
            parts.Add(phrase);
    }

    private static readonly Regex HealthBelow = new(@"Health(?:Condition|Comparison)\((?:compare|operation)=(\w+), (?:percent|value)=([\d.]+)");
    private static readonly Regex Cooldown = new(@"(?:^|\W)(?:CoolDown\(coolTime|ChronometerCoolDown\(duration)=([\d.]+)");
    private static readonly Regex ChanceNote = new(@"(?:Chance\(successChance|RandomProbability\(successProbability)=([\d.]+)");
    private static readonly Regex Distance = new(@"CompareCharacterDistance\(axis=\w+, distance=([\d.]+), comparer=(\w+)");
    private static readonly Regex Trigger = new(@"EnterTrigger\(inverter=(True|False)");
    private static readonly Regex PhaseRange = new(@"(?i)^phase [a-c]\s*\(\s*(\d+)\s*%\s*~\s*(\d+)\s*%\s*\)");
    private static readonly Regex Range = new(@"(?i)\b(short|middle|long) range\b");
    private static readonly Regex Step = new(@"(?i)^step == (\d)\?");
    private static readonly Regex RangePhrase = new(@"^at (\d+)–(\d+) % HP$");

    /// <summary>What one block checks, in words, or null.</summary>
    public static string Phrase(AttackGraph.Unit unit)
    {
        string note = unit.Note ?? "", name = unit.Name ?? "";
        // A name taken from a child ("Phase c ..." on the selector above all phases) says nothing about this block.
        string label = unit.Borrowed ? "" : unit.Label ?? "";
        if (HealthBelow.Match(note) is { Success: true } h)
        {
            double value = double.Parse(h.Groups[2].Value, CultureInfo.InvariantCulture);
            int percent = (int)Math.Round(value <= 1 ? value * 100 : value);
            return h.Groups[1].Value.StartsWith("Less") ? $"below {percent} % HP" : $"above {percent} % HP";
        }
        if (Cooldown.Match(note) is { Success: true } c && double.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture) is var seconds && seconds >= 15)
            return $"at most every {seconds:0} s";
        if (ChanceNote.Match(note) is { Success: true } ch)
            return $"{double.Parse(ch.Groups[1].Value, CultureInfo.InvariantCulture) * 100:0} % chance";
        if (Distance.Match(note) is { Success: true } d)
            return d.Groups[2].Value.StartsWith("Less") ? $"when you are within {d.Groups[1].Value} units" : $"when you are more than {d.Groups[1].Value} units away";
        if (Trigger.Match(note) is { Success: true } t)
        {
            bool away = t.Groups[1].Value == "True";
            bool middle = Range.Match(label) is { Success: true } r && !r.Groups[1].Value.Equals("short", StringComparison.OrdinalIgnoreCase);
            return away ? (middle ? "when you are far away" : "when you are farther away") : (middle ? "when you are not too far" : "when you are close");
        }
        if (note.StartsWith("Grabbed"))
            return "only if the grab catches you";
        if (PhaseRange.Match(label) is { Success: true } ph)
            return $"at {ph.Groups[1].Value}–{ph.Groups[2].Value} % HP";
        if (Step.Match(label) is { Success: true } s)
            return $"in stage {s.Groups[1].Value}";
        if (name.Contains("밸런스"))
            return "in Balance form";
        if (name.Contains("파워"))
            return "in Power form";
        if (name.Contains("스피드"))
            return "in Speed form";
        if (name.Contains("서브 패턴"))
            return "while helping another adventurer";
        return null;
    }

    // ---------------------------------------------------------------- storage (one file per boss and mode)

    private static string FileOf(string key) => Path.Combine(CodexTracker.Folder, "Hints", key + ".json");

    public static void Write(string key, AttackGraph graph)
    {
        try
        {
            var hints = Build(graph);
            Directory.CreateDirectory(Path.GetDirectoryName(FileOf(key)));
            var sb = new StringBuilder("{");
            sb.Append(string.Join(",", hints.Select(p => "\n" + Json(p.Key) + ":" + Json(p.Value))));
            File.WriteAllText(FileOf(key), sb.Append("\n}").ToString(), new UTF8Encoding(false));
            Cache.Remove(key);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: could not write the move hints of {key}: {e.Message}");
        }
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new();

    /// <summary>The hint for a move of a boss (storage key: "Pope", "Pope@DM"), or null.</summary>
    public static string Get(string key, string label)
    {
        if (key == null || label == null)
            return null;
        if (!Cache.TryGetValue(key, out var hints))
        {
            hints = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FileOf(key)) && Describe.GearDoc.ParseRaw(File.ReadAllText(FileOf(key))) is Dictionary<string, object> d)
                    foreach (var pair in d)
                        if (pair.Value is string text)
                            hints[pair.Key] = text;
            }
            catch (Exception) { /* no hints */ }
            Cache[key] = hints;
        }
        return hints.TryGetValue(label, out var hint) ? hint : null;
    }

    private static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
