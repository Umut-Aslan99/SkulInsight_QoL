using System.Collections.Generic;

namespace DamageInsight.Coop;

/// <summary>
/// Net ids of a room's traps and dummies (EnemySync, shared life): from each one's path in the map, not its place in a
/// list, so a random decoration that only one game has (EssenceSanctuary: 3 on the host, 2 on the client) can't shift
/// the others onto the wrong object. Ids have the top bit set (wave enemies count up from 1). Two with the same path
/// get "#1", "#2"... in hierarchy order; a hash collision skips the later one (it stays this game's own). Plain logic.
/// </summary>
public sealed class LifeIds
{
    public const ushort Flag = 0x8000;
    private readonly Dictionary<string, int> _seen = new();
    private readonly HashSet<ushort> _used = new();

    public static bool IsLifeId(ushort id) => (id & Flag) != 0;

    /// <summary>The id for the next character at <paramref name="path"/>, or null when it collides with an earlier one.</summary>
    public ushort? Next(string path)
    {
        _seen.TryGetValue(path, out int n);
        _seen[path] = n + 1;
        ushort id = (ushort)(Flag | (Fnv1a(n == 0 ? path : path + "#" + n) & 0x7FFF));
        return _used.Add(id) ? id : null;
    }

    private static uint Fnv1a(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }
}
