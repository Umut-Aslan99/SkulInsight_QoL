using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DamageInsight.Crash;

/// <summary>Whose code an error came from, read from its stack trace (worst first when compared).</summary>
public enum Blame
{
    /// <summary>Only the game's (or Unity's) own code.</summary>
    Game,
    /// <summary>Another mod's code, or a game method that only another mod hooks.</summary>
    OtherMod,
    /// <summary>A game method SkulInsight hooks is in the trace, SkulInsight's own code is not: maybe involved.</summary>
    Hooked,
    /// <summary>SkulInsight's own code is in the trace.</summary>
    Mod,
}

/// <summary>What <see cref="ErrorClassifier.Classify"/> found.</summary>
public readonly struct Classification
{
    public readonly Blame Blame;
    /// <summary>The frame that decided it ("DamageInsight.UI.MiniLog.Update", "Level.Chapter::ChangeMap", a mod's name).</summary>
    public readonly string Detail;
    /// <summary>The first frame of the trace: where the error happened.</summary>
    public readonly string Where;

    public Classification(Blame blame, string detail, string where)
    {
        Blame = blame;
        Detail = detail ?? "";
        Where = where ?? "";
    }
}

/// <summary>
/// Reads a stack trace and says whose code is in it (crash reports, 0.12.2). Plain logic, no Unity: the game side
/// gives it the game methods each Harmony owner patches (<see cref="SetHooks"/>) and the namespaces of the other
/// loaded mods (<see cref="SetOtherMods"/>). Thread-safe after those calls (Unity reports errors from any thread).
/// Patched game methods show in traces as "(wrapper dynamic-method) Type.DMD&lt;Type::Method&gt;(...)".
/// </summary>
public sealed class ErrorClassifier
{
    /// <summary>The namespace of SkulInsight's own code.</summary>
    public const string ModNamespace = "DamageInsight.";

    private static readonly Regex FrameTail = new(@"\s*(\[0x[0-9a-f]+\]\s*in\s*<[^>]*>:\d+|\(at\s[^)]*\))\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _ownHarmonyPrefix;
    private volatile Dictionary<string, string[]> _hooks = new();
    private volatile (string prefix, string mod)[] _otherMods = Array.Empty<(string, string)>();

    /// <param name="ownHarmonyPrefix">SkulInsight's Harmony ids start with this ("docrun.skulinsight_qol").</param>
    public ErrorClassifier(string ownHarmonyPrefix) => _ownHarmonyPrefix = ownHarmonyPrefix ?? "";

    /// <summary>Patched game methods ("Namespace.Type::Method", nested types with '+') and the Harmony ids patching each.</summary>
    public void SetHooks(IEnumerable<KeyValuePair<string, IEnumerable<string>>> hooks) =>
        _hooks = hooks.GroupBy(h => h.Key)
            .ToDictionary(g => g.Key, g => g.SelectMany(h => h.Value ?? Enumerable.Empty<string>()).Distinct().ToArray());

    /// <summary>How many patched game methods are known (to refresh only when it changes).</summary>
    public int HookCount => _hooks.Count;

    /// <summary>Namespaces of other mods' code ("SomeMod.Patches") with the mod's name.</summary>
    public void SetOtherMods(IEnumerable<(string ns, string mod)> namespaces) =>
        _otherMods = namespaces.Where(n => !string.IsNullOrEmpty(n.ns))
            .Select(n => (n.ns.TrimEnd('.') + ".", n.mod))
            .OrderByDescending(n => n.Item1.Length)
            .ToArray();

    /// <summary>Whether the Harmony owner id belongs to SkulInsight.</summary>
    public bool IsOwn(string owner) => _ownHarmonyPrefix.Length > 0 && owner != null && owner.StartsWith(_ownHarmonyPrefix, StringComparison.Ordinal);

    public Classification Classify(string stackTrace)
    {
        var frames = Frames(stackTrace);
        string where = frames.FirstOrDefault() ?? "";

        // Our own code anywhere in the trace (a hook's body, a MonoBehaviour of ours, a lambda we gave the game).
        foreach (string frame in frames)
            if (StartsWithNamespace(frame, ModNamespace))
                return new Classification(Blame.Mod, frame, where);

        // A patched game method: ours, or only other mods'.
        var hooks = _hooks;
        string otherHook = null, unknownHook = null;
        foreach (string frame in frames)
        {
            string key = HookKey(frame);
            if (key == null)
                continue;
            if (!hooks.TryGetValue(key, out var owners) || owners.Length == 0)
            {
                unknownHook ??= key; // patched, but by whom isn't known (yet) in this session
                continue;
            }
            if (owners.Any(IsOwn))
                return new Classification(Blame.Hooked, key, where);
            otherHook ??= $"{key} ({string.Join(", ", owners)})";
        }

        // Another mod's own code.
        var mods = _otherMods;
        foreach (string frame in frames)
            foreach (var (prefix, mod) in mods)
                if (StartsWithNamespace(frame, prefix))
                    return new Classification(Blame.OtherMod, $"{mod}: {frame}", where);
        if (otherHook != null)
            return new Classification(Blame.OtherMod, otherHook, where);
        // Harmony patched it and SkulInsight is installed: it may be ours (a hook made later, e.g. in a boss fight).
        if (unknownHook != null)
            return new Classification(Blame.Hooked, unknownHook + " (patched by a mod; which one isn't known here)", where);

        return new Classification(Blame.Game, where, where);
    }

    /// <summary>
    /// "Type::Method" of a patched method's frame ("(wrapper dynamic-method) T.DMD&lt;T::M&gt;(...)"), else null. Counts
    /// the angle brackets: coroutine types have their own ("RunAction+&lt;CRun&gt;d__1::MoveNext").
    /// </summary>
    public static string HookKey(string frame)
    {
        int start = frame?.IndexOf("DMD<", StringComparison.Ordinal) ?? -1;
        if (start < 0)
            return null;
        start += 4;
        int depth = 1;
        for (int i = start; i < frame.Length; i++)
        {
            if (frame[i] == '<')
                depth++;
            else if (frame[i] == '>' && --depth == 0)
            {
                string key = frame.Substring(start, i - start);
                return key.Contains("::") ? key : null;
            }
        }
        return null;
    }

    /// <summary>
    /// The frames of a trace, cleaned: "at " and Mono's "[0x00024] in &lt;hash&gt;:0" / Unity's "(at file:line)" are
    /// removed, so the same error in two sessions reads the same.
    /// </summary>
    public static List<string> Frames(string stackTrace)
    {
        var frames = new List<string>();
        if (string.IsNullOrEmpty(stackTrace))
            return frames;
        foreach (string raw in stackTrace.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("at ", StringComparison.Ordinal))
                line = line.Substring(3).TrimStart();
            line = FrameTail.Replace(line, "");
            if (line.StartsWith("--- End of", StringComparison.Ordinal) || line.StartsWith("Rethrow as", StringComparison.Ordinal))
                continue;
            frames.Add(line);
        }
        return frames;
    }

    /// <summary>Whether a frame's method starts with a namespace ("(wrapper ...) X" counts with X).</summary>
    private static bool StartsWithNamespace(string frame, string prefix)
    {
        string f = frame;
        if (f.StartsWith("(wrapper", StringComparison.Ordinal))
        {
            int close = f.IndexOf(')');
            f = close >= 0 ? f.Substring(close + 1).TrimStart() : f;
            // A patched method's wrapper names the game type; only its DMD part could be ours (never: we patch game code).
            return f.StartsWith(prefix, StringComparison.Ordinal) && !f.Contains("DMD<");
        }
        return f.StartsWith(prefix, StringComparison.Ordinal);
    }
}
