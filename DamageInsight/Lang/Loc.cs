using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DamageInsight.Describe;

namespace DamageInsight.Lang;

/// <summary>
/// The mod's translations. The English text is the key: <c>Loc.T("can't crit")</c> gives the text in the current
/// language, <c>Loc.F("{0} hits of {1}", n, x)</c> formats (with the invariant culture, like all our numbers),
/// <c>Loc.P("{0} kill", "{0} kills", n)</c> picks the plural form the language needs. Anything without a
/// translation stays English. Plain logic, no Unity (tests use it directly).
/// </summary>
/// <remarks>
/// Translations live in Lang/&lt;code&gt;.json (embedded): <c>{"strings": {"English": "translation" or [plural forms]}}</c>.
/// A file with the same name in BepInEx/DamageInsight/Lang overrides single entries, so players can fix wording
/// without a new release. <see cref="LanguageWatcher"/> follows the game's language setting.
/// </remarks>
public static class Loc
{
    public const string English = "en";

    /// <summary>The game's languages in its own order: GameData.Settings.language is an index into this.</summary>
    public static readonly string[] GameOrder = { "ko", "en", "ja", "zh-Hans", "zh-Hant", "de", "es", "pt-BR", "ru", "pl", "fr" };

    /// <summary>Every language we have, with its own name (the settings page lists them).</summary>
    public static readonly (string code, string name)[] Languages =
    {
        ("en", "English"), ("ko", "한국어"), ("ja", "日本語"), ("zh-Hans", "简体中文"), ("zh-Hant", "繁體中文"),
        ("de", "Deutsch"), ("es", "Español"), ("pt-BR", "Português (Brasil)"), ("ru", "Русский"), ("pl", "Polski"),
        ("fr", "Français"),
    };

    private sealed class Table
    {
        public readonly string Code;
        public readonly Dictionary<string, object> Strings;

        public Table(string code, Dictionary<string, object> strings)
        {
            Code = code;
            Strings = strings;
        }
    }

    private static readonly Table EnglishTable = new(English, new Dictionary<string, object>());
    private static Table _current = EnglishTable;

    [ThreadStatic] private static Table _scoped; // tests: a language for this thread only

    private static Table Active => _scoped ?? _current;

    /// <summary>The language in use ("en", "de", "zh-Hans"...).</summary>
    public static string Current => Active.Code;

    /// <summary>Goes up with every language change; caches of translated text compare it.</summary>
    public static int Version { get; private set; }

    /// <summary>After the language changed (texts built before are in the old language).</summary>
    public static event Action Changed;

    /// <summary>Called with every English key looked up (tests collect the texts the mod really produces).</summary>
    public static Action<string> Recorder;

    /// <summary>Where players can put corrections (set by the plugin; null in tests).</summary>
    public static string OverrideFolder;

    // ---------------------------------------------------------------- lookups

    /// <summary>The text in the current language.</summary>
    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english))
            return english ?? "";
        Recorder?.Invoke(english);
        return Active.Strings.TryGetValue(english, out var t) && t is string s && s.Length > 0 ? s : english;
    }

    /// <summary>A format ("{0} hits of {1}") translated, then filled in with the invariant culture.</summary>
    public static string F(string english, params object[] args)
    {
        string format = T(english);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, english, args); // a broken translation: keep English
        }
    }

    /// <summary>
    /// A count with its noun in the right plural form: P("{0} kill", "{0} kills", n). {0} is the count;
    /// further arguments are {1}, {2}...
    /// </summary>
    public static string P(string one, string other, long count, params object[] more)
    {
        string key = one + "|" + other;
        Recorder?.Invoke(key);
        string english = count == 1 ? one : other;
        string format = english;
        if (Active.Strings.TryGetValue(key, out var t) && t is List<object> forms && forms.Count > 0)
        {
            int index = Math.Min(PluralIndex(Active.Code, count), forms.Count - 1);
            if (forms[index] is string f && f.Length > 0)
                format = f;
        }
        var args = new object[more.Length + 1];
        args[0] = count;
        Array.Copy(more, 0, args, 1, more.Length);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, english, args);
        }
    }

    /// <summary>
    /// Marks a text for translation without translating it now (labels in tables, translated with
    /// <see cref="T"/> where they are shown). The text extraction test finds these.
    /// </summary>
    public static string N(string english) => english;

    /// <summary>
    /// The English ordinal ("2nd", "3rd", "5th"). Texts with an ordinal get it as {0} and the plain number as {1},
    /// so a translation can use either ("Every {0} swing" → "Jeder {1}. Schwung").
    /// </summary>
    public static string Ordinal(int n) => (n % 100) is >= 11 and <= 13 ? $"{n}th" : (n % 10) switch
    {
        1 => $"{n}st",
        2 => $"{n}nd",
        3 => $"{n}rd",
        _ => $"{n}th",
    };

    /// <summary>"a, b and c" in the current language.</summary>
    public static string Join(IEnumerable<string> items)
    {
        var list = items.Where(i => !string.IsNullOrEmpty(i)).ToList();
        if (list.Count <= 1)
            return list.FirstOrDefault() ?? "";
        return string.Join(Loc.T(", "), list.Take(list.Count - 1)) + Loc.T(" and ") + list[list.Count - 1];
    }

    /// <summary>"a or b" in the current language.</summary>
    public static string JoinOr(IEnumerable<string> items)
    {
        var list = items.Where(i => !string.IsNullOrEmpty(i)).ToList();
        if (list.Count <= 1)
            return list.FirstOrDefault() ?? "";
        return string.Join(Loc.T(", "), list.Take(list.Count - 1)) + Loc.T(" or ") + list[list.Count - 1];
    }

    // ---------------------------------------------------------------- names made from game data

    private static readonly Regex Numbered = new(@"^(.*\S)\s+(\d+)$");
    private static readonly Regex Bracketed = new(@"^(.*\S)\s+\(([^()]+)\)$");

    /// <summary>
    /// A name the mod made from the game's data (a boss move "Fist slam (enhanced)", an action "Powerbomb jump
    /// attack", "Phase 2 · Sweeping"): the whole name if it is translated, else its parts (" · " pieces, a
    /// trailing number, a bracketed remark). Unknown names stay English.
    /// </summary>
    public static string Name(string english)
    {
        if (string.IsNullOrEmpty(english))
            return english ?? "";
        if (english.Contains(" · "))
            return string.Join(" · ", english.Split(new[] { " · " }, StringSplitOptions.None).Select(Name));
        int colon = english.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0)
            return Name(english.Substring(0, colon)) + ": " + Name(english.Substring(colon + 2));
        Recorder?.Invoke(english);
        if (Active.Strings.TryGetValue(english, out var t) && t is string s && s.Length > 0)
            return s;
        var bracket = Bracketed.Match(english);
        if (bracket.Success && (Has(bracket.Groups[1].Value) || Has("(" + bracket.Groups[2].Value + ")")))
            return Name(bracket.Groups[1].Value) + " " + T("(" + bracket.Groups[2].Value + ")");
        var number = Numbered.Match(english);
        if (number.Success && Has(number.Groups[1].Value))
            return Name(number.Groups[1].Value) + " " + number.Groups[2].Value;
        return english;
    }

    private static bool Has(string english) => Active.Strings.ContainsKey(english);

    private static readonly Dictionary<string, Regex> TemplateRegexes = new();

    /// <summary>
    /// English text the mod stored earlier (e.g. a move hint on disk, "below 40 % HP"), translated by matching it
    /// against <paramref name="templates"/> ("below {0} % HP"): the matching template's translation is filled in
    /// with the parts it captured. Text no template matches stays as it is.
    /// </summary>
    public static string Phrase(string english, IEnumerable<string> templates)
    {
        if (string.IsNullOrEmpty(english))
            return english ?? "";
        if (Has(english))
            return T(english);
        foreach (var template in templates)
        {
            if (!TemplateRegexes.TryGetValue(template, out var regex))
            {
                string pattern = "^" + Regex.Replace(Regex.Escape(template), @"\\\{(\d+)}", m => $"(?<p{m.Groups[1].Value}>.+?)") + "$";
                TemplateRegexes[template] = regex = new Regex(pattern);
            }
            var match = regex.Match(english);
            if (!match.Success)
                continue;
            var args = new List<object>();
            for (int i = 0; match.Groups["p" + i].Success; i++)
                args.Add(match.Groups["p" + i].Value);
            return F(template, args.ToArray());
        }
        return english;
    }

    // ---------------------------------------------------------------- plural rules (CLDR, whole numbers)

    /// <summary>Which plural form a count takes: English-like [one, other], Russian/Polish [one, few, many], CJK [other].</summary>
    public static int PluralIndex(string code, long n)
    {
        n = Math.Abs(n);
        switch (code)
        {
            case "ko":
            case "ja":
            case "zh-Hans":
            case "zh-Hant":
                return 0;
            case "fr":
            case "pt-BR":
                return n <= 1 ? 0 : 1;
            case "ru":
                if (n % 10 == 1 && n % 100 != 11) return 0;
                return n % 10 is >= 2 and <= 4 && !(n % 100 is >= 12 and <= 14) ? 1 : 2;
            case "pl":
                if (n == 1) return 0;
                return n % 10 is >= 2 and <= 4 && !(n % 100 is >= 12 and <= 14) ? 1 : 2;
            default:
                return n == 1 ? 0 : 1;
        }
    }

    /// <summary>How many plural forms a language's entries have.</summary>
    public static int PluralForms(string code) => code switch
    {
        "ko" or "ja" or "zh-Hans" or "zh-Hant" => 1,
        "ru" or "pl" => 3,
        _ => 2,
    };

    // ---------------------------------------------------------------- loading

    /// <summary>Switches to <paramref name="code"/> (English if there is no such language).</summary>
    public static void Use(string code)
    {
        if (string.IsNullOrEmpty(code) || !Languages.Any(l => l.code == code))
            code = English;
        if (code == _current.Code && Version > 0)
            return;
        _current = code == English ? WithOverrides(EnglishTable) : Load(code);
        Version++;
        try
        {
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning($"Language change: a listener failed: {e}");
        }
    }

    /// <summary>Runs <paramref name="action"/> with <paramref name="code"/> on this thread only (tests).</summary>
    public static void With(string code, Action action)
    {
        var before = _scoped;
        _scoped = code == English ? EnglishTable : Load(code);
        try
        {
            action();
        }
        finally
        {
            _scoped = before;
        }
    }

    /// <summary>The embedded translation file for <paramref name="code"/> as text, or null.</summary>
    public static string Embedded(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"DamageInsight.Lang.{code}.json");
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>The "strings" of a translation file: English → text, or English → plural forms.</summary>
    public static Dictionary<string, object> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, object>();
        var root = GearDoc.ParseRaw(json) as Dictionary<string, object>;
        return root != null && root.TryGetValue("strings", out var s) && s is Dictionary<string, object> strings
            ? strings
            : new Dictionary<string, object>();
    }

    private static Table Load(string code)
    {
        var strings = new Dictionary<string, object>();
        try
        {
            foreach (var pair in Parse(Embedded(code)))
                strings[pair.Key] = pair.Value;
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning($"Language {code}: the built-in texts could not be read ({e.Message}); using English.");
        }
        return WithOverrides(new Table(code, strings));
    }

    /// <summary>Adds the player's own corrections (BepInEx/DamageInsight/Lang/&lt;code&gt;.json), if any.</summary>
    private static Table WithOverrides(Table table)
    {
        if (OverrideFolder == null)
            return table;
        string path = Path.Combine(OverrideFolder, table.Code + ".json");
        if (!File.Exists(path))
            return table;
        try
        {
            var strings = new Dictionary<string, object>(table.Strings);
            int count = 0;
            foreach (var pair in Parse(File.ReadAllText(path, Encoding.UTF8)))
            {
                strings[pair.Key] = pair.Value;
                count++;
            }
            Plugin.Log?.LogInfo($"Language {table.Code}: {count} corrections from {path}.");
            return new Table(table.Code, strings);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning($"Language {table.Code}: {path} could not be read ({e.Message}); ignoring it.");
            return table;
        }
    }
}
