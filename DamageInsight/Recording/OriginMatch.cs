using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.Recording;

/// <summary>
/// Finds the gear behind a hit's origin that is only known by an object's code name (Umut's DM 10 run, 2026-10-16:
/// "Skeleton spear 4 endless rush" is the Pikeman Captain's skill, "Unbreakable pride" the item Indomitable Will,
/// "Mystery bright light spirit attack" the Mystery inscription). The origin, without spaces and case, starts with a
/// gear's key (Skeleton_Spear_4, UnbreakablePride, Mystery); the longest key wins. Plain logic.
/// </summary>
public sealed class OriginMatch<T> where T : class
{
    public const int MinKey = 4;

    private readonly List<(string key, T value)> _keys;

    public OriginMatch(IEnumerable<(string key, T value)> keys) =>
        _keys = keys.Select(k => (Normalize(k.key), k.value))
            .Where(k => k.Item1.Length >= MinKey)
            .OrderByDescending(k => k.Item1.Length)
            .ToList();

    /// <summary>The value whose key the origin starts with (the longest such key), or null.</summary>
    public T Find(string origin)
    {
        string n = Normalize(origin);
        if (n.Length < MinKey)
            return null;
        foreach (var (key, value) in _keys)
            if (n.StartsWith(key, StringComparison.Ordinal))
                return value;
        return null;
    }

    public static string Normalize(string s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
