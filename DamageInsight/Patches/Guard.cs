using System;
using System.Collections.Generic;

namespace DamageInsight.Patches;

/// <summary>
/// Every Harmony hook runs its own code through Guard: an error in our code is logged (once per place) and
/// swallowed, so it can never reach the game's code. An exception escaping a hook would abort the game method it
/// is attached to, e.g. a boss intro cutscene, which leaves the boss invulnerable. PatchSafetyTests checks that
/// every hook uses Guard (or try/catch).
/// </summary>
public static class Guard
{
    private static readonly HashSet<string> Reported = new();

    public static void Run(string where, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Report(where, e);
        }
    }

    /// <summary>For description hooks: the text to append, or "" if our code failed.</summary>
    public static string Text(string where, Func<string> text)
    {
        try
        {
            return text() ?? "";
        }
        catch (Exception e)
        {
            Report(where, e);
            return "";
        }
    }

    public static void Report(string where, Exception e)
    {
        if (Reported.Add(where))
            Plugin.Log.LogWarning($"{where} failed; the game continues without it (logged once): {e}");
    }
}
