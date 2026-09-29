using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>
/// An exception escaping a Harmony hook aborts the game method it is attached to. That once aborted a boss intro
/// cutscene (the HP-number hook), leaving the boss invulnerable. So every hook must run its code through
/// Guard (or its own try/catch). This test reads the hook sources and fails for any unguarded hook.
/// </summary>
public class PatchSafetyTests
{
    /// <summary>Hooks that are safe without a guard: they only set or clear a field, or only pass game code through.</summary>
    private static readonly HashSet<string> Trivial = new()
    {
        "StatusContextPatch.cs:Prefix",     // sets DamageSources.CurrentStatus
        "CursorPatch.cs:Postfix",           // sets Cursor.visible
        "DamageTracePatch.cs:Prefix",       // TraceGive/TraceTake run the game's handlers; our part is in Note (guarded)
    };

    [Fact]
    public void EveryHookIsGuarded()
    {
        string folder = PatchFolder();
        Assert.True(folder != null, "DamageInsight/Patches not found next to the test output.");

        var unguarded = new List<string>();
        foreach (var file in Directory.GetFiles(folder, "*.cs"))
        {
            string source = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            foreach (Match m in Regex.Matches(source,
                         @"(?<attrs>(\[\s*Harmony(Prefix|Postfix)\s*\][^\n]*\n\s*(\[[^\n]*\]\s*\n\s*)*)?)(private|public|internal) static (void|bool) (?<name>\w+)\((?<args>[^)]*)\)\s*(?<body>=>|\{)"))
            {
                string method = m.Groups["name"].Value;
                bool isHook = method is "Prefix" or "Postfix" || m.Groups["attrs"].Value.Contains("Harmony");
                if (!isHook || Trivial.Contains($"{name}:{method}"))
                    continue;
                string body = Body(source, m.Index + m.Length, m.Groups["body"].Value == "=>");
                if (!body.Contains("Guard.") && !body.Contains("try") && !body.Contains("Safe("))
                    unguarded.Add($"{name}: {method}");
            }
        }
        Assert.True(unguarded.Count == 0, "Unguarded hooks: " + string.Join(", ", unguarded));
    }

    private static string Body(string source, int start, bool expression)
    {
        if (expression)
        {
            int end = source.IndexOf(";\n", start, StringComparison.Ordinal);
            return source.Substring(start, (end < 0 ? source.Length : end) - start);
        }
        int depth = 1, i = start;
        while (depth > 0 && i < source.Length)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            i++;
        }
        return source.Substring(start, i - start);
    }

    private static string PatchFolder()
    {
        for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "DamageInsight", "Patches");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
