using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.Lang;

/// <summary>
/// Text a run kept in its player's language, shown in the viewer's (0.16.1; Umut 2026-11-01: "Wenn ich deren [Runs]
/// öffne, so sind sie auf chinesisch. Ich kann deren Sprache aber nicht verstehen"). A run file keeps names as they were
/// shown (skills, damage table rows, enemies, stages); the game holds every language's text of its string table, so a
/// name found in any language's column is shown from the viewer's column. The mod's own words are found in its
/// translation files the same way, and "A: B" / "A · B" made of them part by part. Anything else stays as it was.
/// Old runs and runs from other players are shown in the viewer's language this way too.
/// </summary>
public static class AnyLanguage
{
    /// <summary>
    /// The game's string table: each row's text per language, columns in <see cref="Loc.GameOrder"/>; null while the
    /// game hasn't loaded it (tests set their own).
    /// </summary>
    public static Func<IReadOnlyList<string[]>> GameRows = ReadGameRows;

    private static Dictionary<string, string[]> _game;   // a text in any language → its row
    private static Dictionary<string, string> _mod;      // the mod's text in any language → its English key
    // How each language writes "{0}: {1}" (zh, ja "：", fr " : ", the others ": "): longest first.
    private static readonly string[] Separators = { " : ", "：", ": " };
    private const int Longest = 80;

    /// <summary>The text in the viewer's language where it is a name or word of the game or the mod.</summary>
    public static string Here(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";
        Ensure();
        return Translate(text, 0);
    }

    /// <summary>Forgets the tables (tests; a new game table).</summary>
    public static void Reset()
    {
        _game = null;
        _mod = null;
    }

    private static string Translate(string text, int depth)
    {
        if (depth > 4)
            return text;
        if (text.Contains(" · "))
            return string.Join(" · ", text.Split(new[] { " · " }, StringSplitOptions.None).Select(p => Translate(p, depth + 1)));
        if (Whole(text) is { } whole)
            return whole;
        foreach (string separator in Separators)
        {
            int at = text.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0 && at + separator.Length < text.Length)
                return Loc.F("{0}: {1}", Translate(text.Substring(0, at), depth + 1), Translate(text.Substring(at + separator.Length), depth + 1));
        }
        return text;
    }

    private static string Whole(string text)
    {
        if (_game != null && _game.TryGetValue(text, out var row))
        {
            int column = Array.IndexOf(Loc.GameOrder, Loc.Current);
            if (column >= 0 && column < row.Length && !string.IsNullOrWhiteSpace(row[column]))
                return row[column];
        }
        return _mod != null && _mod.TryGetValue(text, out var english) ? Loc.T(english) : null;
    }

    private static void Ensure()
    {
        if (_mod == null)
        {
            var mod = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (code, _) in Loc.Languages)
            {
                if (code == "en")
                    continue;
                try
                {
                    foreach (var pair in Loc.Parse(Loc.Embedded(code)))
                    {
                        if (pair.Value is string s && s.Length > 0 && s.Length <= Longest && !mod.ContainsKey(s))
                            mod[s] = pair.Key;
                        if (pair.Key.Length <= Longest && !mod.ContainsKey(pair.Key) && !pair.Key.Contains("{"))
                            mod[pair.Key] = pair.Key; // English as kept by an English game
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log?.LogWarning($"Run texts: the {code} texts could not be read ({e.Message})");
                }
            }
            _mod = mod;
        }
        if (_game == null)
        {
            IReadOnlyList<string[]> rows = null;
            try
            {
                rows = GameRows?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("Run texts: the game's string table could not be read: " + e.Message);
            }
            if (rows == null)
                return; // not loaded yet: asked again next time
            var game = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row == null)
                    continue;
                foreach (string s in row)
                    if (!string.IsNullOrWhiteSpace(s) && s.Length <= Longest && !game.ContainsKey(s))
                        game[s] = row;
            }
            _game = game;
        }
    }

    private static IReadOnlyList<string[]> ReadGameRows()
    {
        var table = GameResources.LocalizationStringResource.instance;
        return table?.stringsByLanguage?.Select(r => r.strings).ToList();
    }
}
