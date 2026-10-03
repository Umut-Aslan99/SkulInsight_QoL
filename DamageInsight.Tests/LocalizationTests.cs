using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DamageInsight.Codex;
using DamageInsight.Describe;
using DamageInsight.Lang;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The template run records every text the mod looks up; nothing else may run meanwhile.</summary>
[CollectionDefinition("Loc", DisableParallelization = true)]
public class LocCollection
{
}

/// <summary>
/// Translations: every English text the mod can show is in Lang/template.json (from the code, plus names made from
/// game data: boss moves, animations, skull actions, summons), and every language file translates all of it with the
/// same placeholders and rich-text tags. To refresh the template after changing texts: set UPDATE_LANG_TEMPLATE=1 and
/// run the tests (game data from the gear scan and the Codex folder is added when it is on this machine).
/// </summary>
[Collection("Loc")]
public class LocalizationTests
{
    // ---------------------------------------------------------------- the template

    [Fact]
    public void Template_has_every_text_of_the_code()
    {
        var code = CodeTexts();
        var template = Template.Load();
        if (Environment.GetEnvironmentVariable("UPDATE_LANG_TEMPLATE") == "1")
        {
            var names = DataNames(out bool found);
            template = new Template(code, found ? names.Where(n => !code.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList() : template.Names);
            template.Save();
        }
        var missing = code.Where(k => !template.Strings.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Not in Lang/template.json (run the tests with UPDATE_LANG_TEMPLATE=1): " + string.Join(" | ", missing));
    }

    [Fact]
    public void Template_has_every_name_from_the_game_data()
    {
        var names = DataNames(out bool found);
        if (!found)
            return; // no gear scan / Codex data on this machine
        var template = Template.Load();
        var known = new HashSet<string>(template.Strings.Concat(template.Names));
        var missing = names.Where(n => !known.Contains(n)).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, "Names not in Lang/template.json (run with UPDATE_LANG_TEMPLATE=1): " + string.Join(" | ", missing.Take(80)));
    }

    [Fact]
    public void Texts_are_literal_where_they_are_looked_up()
    {
        // Loc.T($"…") would look up a different key every time: the text must be a plain literal.
        var bad = SourceFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Loc\.[TFPN]\(\s*\$""").Cast<Match>().Select(m => Path.GetFileName(f)))
            .ToList();
        Assert.True(bad.Count == 0, "Interpolated text in a Loc call: " + string.Join(", ", bad));
    }

    // ---------------------------------------------------------------- the languages

    public static IEnumerable<object[]> Translations() =>
        Loc.Languages.Where(l => l.code != Loc.English).Select(l => new object[] { l.code });

    [Theory]
    [MemberData(nameof(Translations))]
    public void Language_translates_every_text(string code)
    {
        var template = Template.Load();
        var strings = LanguageFile(code);
        var problems = new List<string>();
        foreach (var key in template.Strings.Concat(template.Names))
        {
            if (!strings.TryGetValue(key, out var value))
            {
                problems.Add("missing: " + key);
                continue;
            }
            problems.AddRange(Check(code, key, value));
        }
        var known = new HashSet<string>(template.Strings.Concat(template.Names));
        problems.AddRange(strings.Keys.Where(k => !known.Contains(k)).Select(k => "not used any more: " + k));
        Assert.True(problems.Count == 0, $"{code}: {problems.Count} problems\n" + string.Join("\n", problems.Take(60)));
    }

    /// <summary>
    /// The language files are inside the mod's DLL (MSBuild once moved "codex_content.ko.json" into a Korean
    /// satellite assembly because of the ".ko", and the release ships the DLL only).
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void Language_files_are_inside_the_dll(string code)
    {
        var names = typeof(Loc).Assembly.GetManifestResourceNames();
        Assert.Contains($"DamageInsight.Lang.{code}.json", names);
        Assert.Contains($"DamageInsight.Codex.codex_content.{code}.json", names);
        Assert.DoesNotContain("DamageInsight.Lang.template.json", names);
    }

    // Texts whose {0} is an English ordinal ("3rd") and {1} the plain number: a translation may use either.
    private static readonly HashSet<string> OrdinalTexts = new() { "Every {0} swing", "Every {0} shockwave", "every {0} time" };

    private static IEnumerable<string> Check(string code, string key, object value)
    {
        bool plural = key.Contains('|') && Regex.IsMatch(key, @"\{0\}");
        if (plural)
        {
            var english = key.Split('|');
            if (!(value is List<object> forms) || forms.Count != Loc.PluralForms(code) || forms.Any(f => !(f is string s) || s.Length == 0))
            {
                yield return $"needs {Loc.PluralForms(code)} plural forms: {key}";
                yield break;
            }
            var allowed = new HashSet<string>(english.SelectMany(Placeholders));
            foreach (string form in forms)
            {
                if (Placeholders(form).Any(p => !allowed.Contains(p)))
                    yield return $"unknown placeholder in \"{form}\" for {key}";
                if (!Tags(form).SequenceEqual(Tags(english[1])))
                    yield return $"tags differ in \"{form}\" for {key}";
            }
            yield break;
        }
        if (!(value is string text) || text.Length == 0)
        {
            yield return "empty: " + key;
            yield break;
        }
        var expected = new HashSet<string>(Placeholders(key));
        var actual = new HashSet<string>(Placeholders(text));
        if (OrdinalTexts.Contains(key))
        {
            if (!actual.IsSubsetOf(new[] { "0", "1" }) || actual.Count == 0)
                yield return $"needs {{0}} or {{1}}: \"{text}\" for {key}";
        }
        else if (!expected.SetEquals(actual))
            yield return $"placeholders differ: \"{text}\" for {key}";
        if (!Tags(text).SequenceEqual(Tags(key)))
            yield return $"tags differ: \"{text}\" for {key}";
    }

    private static IEnumerable<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)(?:[,:][^}]*)?\}").Cast<Match>().Select(m => m.Groups[1].Value);

    private static List<string> Tags(string s) =>
        Regex.Matches(s, @"<[^<>]+>").Cast<Match>().Select(m => m.Value).OrderBy(t => t, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Translations (and the Codex notes) only use characters the game's own fonts have. The fonts are fixed atlases
    /// of the characters the game's texts use; for Korean, Japanese and Chinese a Windows font would fill gaps at
    /// runtime, but it looks different, so the translations avoid needing it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void Language_uses_only_characters_of_the_game_font(string code)
    {
        string fonts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "font_chars.json");
        if (!File.Exists(fonts))
            return; // game data, not in the public repository
        var tables = (Dictionary<string, object>)GearDoc.ParseRaw(File.ReadAllText(fonts));
        // The game's Latin font falls back to its CJK fonts (they hold "–" and "·"), so any of them will do; the
        // Codex stars are used in English too (the settings page spells them out where the font lacks them).
        var chars = new HashSet<char>(tables.Values.Cast<string>().SelectMany(s => s).Concat(" \n★☆"));
        var texts = LanguageFile(code).Values
            .SelectMany(value => value is List<object> forms ? forms.Cast<string>() : new[] { (string)value }).ToList();
        string notes = Path.Combine(RepoRoot(), "DamageInsight", "Codex", $"codex_content.{code}.json");
        if (File.Exists(notes))
            texts.Add(File.ReadAllText(notes, Encoding.UTF8));
        var missing = new SortedSet<char>();
        foreach (var text in texts)
            foreach (char c in Regex.Replace(text, @"<[^<>]+>", ""))
                if (!chars.Contains(c) && c != (char)13)
                    missing.Add(c);
        Assert.True(missing.Count == 0, $"{code}: the game font has no {string.Join(" ", missing.Select(c => $"'{c}' U+{(int)c:X4}"))}");
    }

    // ---------------------------------------------------------------- Loc itself

    [Theory]
    [InlineData("en", 1, 0)]
    [InlineData("en", 2, 1)]
    [InlineData("en", 0, 1)]
    [InlineData("fr", 0, 0)]
    [InlineData("fr", 1, 0)]
    [InlineData("fr", 2, 1)]
    [InlineData("ru", 1, 0)]
    [InlineData("ru", 3, 1)]
    [InlineData("ru", 5, 2)]
    [InlineData("ru", 11, 2)]
    [InlineData("ru", 21, 0)]
    [InlineData("ru", 22, 1)]
    [InlineData("ru", 112, 2)]
    [InlineData("pl", 1, 0)]
    [InlineData("pl", 4, 1)]
    [InlineData("pl", 5, 2)]
    [InlineData("pl", 21, 2)]
    [InlineData("pl", 22, 1)]
    [InlineData("ko", 1, 0)]
    [InlineData("ja", 7, 0)]
    public void Plural_rules(string code, long n, int form) => Assert.Equal(form, Loc.PluralIndex(code, n));

    [Fact]
    public void English_is_the_default_and_formats_invariantly()
    {
        Assert.Equal("can't crit", Loc.T("can't crit"));
        Assert.Equal("1.5 s", Loc.F("{0} s", 1.5));
        Assert.Equal("1 kill", Loc.P("{0} kill", "{0} kills", 1));
        Assert.Equal("3 kills", Loc.P("{0} kill", "{0} kills", 3));
        Assert.Equal("Every 3rd swing", Loc.F("Every {0} swing", Loc.Ordinal(3), 3));
        Assert.Equal("11th 12th 13th 21st 22nd", string.Join(" ", new[] { 11, 12, 13, 21, 22 }.Select(Loc.Ordinal)));
        Assert.Equal("a, b and c", Loc.Join(new[] { "a", "b", "c" }));
        Assert.Equal("a or b", Loc.JoinOr(new[] { "a", "b" }));
    }

    [Fact]
    public void A_translation_is_used_and_falls_back_to_English()
    {
        Loc.With("de", () =>
        {
            Assert.Equal("de", Loc.Current);
            Assert.NotEqual("can't crit", Loc.T("can't crit"));
            Assert.Equal("no such text 12345", Loc.T("no such text 12345"));
            Assert.Contains("3", Loc.P("{0} kill", "{0} kills", 3));
        });
        Assert.Equal("en", Loc.Current);
    }

    [Fact]
    public void Stored_hints_are_translated_by_their_phrase()
    {
        Loc.With("de", () =>
        {
            string hint = MoveHints.Localize("below 40 % HP · at most every 80 s");
            Assert.Contains("40", hint);
            Assert.Contains("80", hint);
            Assert.DoesNotContain("below", hint);
            Assert.Equal("something unknown", MoveHints.Localize("something unknown"));
        });
    }

    [Fact]
    public void Names_are_translated_whole_or_by_their_parts()
    {
        Loc.With("de", () =>
        {
            Assert.NotEqual("Phase 2 · Sweeping", Loc.Name("Phase 2 · Sweeping"));
            Assert.EndsWith(" 7", Loc.Name("Attack 7"));
            Assert.Equal("Not a known name", Loc.Name("Not a known name"));
        });
    }

    // ---------------------------------------------------------------- texts in the code

    private static IEnumerable<string> SourceFiles()
    {
        string root = Path.Combine(RepoRoot(), "DamageInsight");
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private const string Literal = @"""((?:[^""\\]|\\.)*)""";

    /// <summary>Every text given to Loc.T / F / N (one key) and Loc.P ("one|other"), in source order.</summary>
    public static List<string> CodeTexts()
    {
        var keys = new List<string>();
        var seen = new HashSet<string>();
        foreach (var file in SourceFiles())
        {
            // Comment lines (doc comments show Loc calls as examples) don't count; the line count stays the same.
            string source = Regex.Replace(File.ReadAllText(file), @"(?m)^[ \t]*//.*$", "");
            var found = new List<(int at, string key)>();
            foreach (Match m in Regex.Matches(source, @"Loc\.[TFN]\(\s*" + Literal))
                found.Add((m.Index, Unescape(m.Groups[1].Value)));
            foreach (Match m in Regex.Matches(source, @"Loc\.P\(\s*" + Literal + @"\s*,\s*" + Literal))
                found.Add((m.Index, Unescape(m.Groups[1].Value) + "|" + Unescape(m.Groups[2].Value)));
            foreach (var (_, key) in found.OrderBy(f => f.at))
                if (key.Length > 0 && seen.Add(key))
                    keys.Add(key);
        }
        return keys;
    }

    private static string Unescape(string s) => Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch
    {
        "n" => "\n",
        "t" => "\t",
        "r" => "\r",
        _ => m.Groups[1].Value,
    });

    // ---------------------------------------------------------------- names from game data

    /// <summary>
    /// The names the mod makes from game data (all looked up with Loc.Name): every gear in the scan analysed and
    /// described, every Codex clip and boss move, the enemy groups. <paramref name="found"/>: whether data was there.
    /// </summary>
    public static HashSet<string> DataNames(out bool found)
    {
        var names = new HashSet<string>();
        var gate = new object();
        found = false;
        Loc.Recorder = k =>
        {
            lock (gate)
                names.Add(k);
        };
        try
        {
            string scan = Fixture.FullScanFolder();
            if (scan != null)
            {
                found = true;
                foreach (var file in Directory.GetFiles(scan, "*.json", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file) == "statuses.json")
                    {
                        var settings = GearDoc.ParseNode(File.ReadAllText(file));
                        foreach (var status in new[] { "Poison", "Burn", "Bleed", "Freeze", "Stun" })
                            StatusDescriptions.Describe(settings, status, StatSnapshot.Neutral);
                        StatusDescriptions.SuperBleed(settings, StatSnapshot.Neutral, true, 0.45);
                        StatusDescriptions.SuperBleed(settings, StatSnapshot.Neutral, true, null);
                        continue;
                    }
                    Breakdown b;
                    try
                    {
                        b = GearAnalyzer.Analyze(GearDoc.Parse(File.ReadAllText(file)));
                    }
                    catch (Exception)
                    {
                        continue; // not a gear scan
                    }
                    bool summon = file.Contains(Path.DirectorySeparatorChar + "characters" + Path.DirectorySeparatorChar);
                    foreach (var section in b.Sections.Where(s => s.HasHits || s.Notes.Count > 0))
                        DescriptionFormatter.Section(section, StatSnapshot.Neutral, summon ? Loc.Name(WeaponRefiner.Humanize(section.Key)) : null);
                }
            }

            for (int key = 0; key < 60000; key++)
                if (CodexGroups.ForEnemy(key) is { } group)
                    Loc.Name(group.group);

            string codex = Environment.GetEnvironmentVariable("SKUL_CODEX") ??
                           @"C:\Program Files (x86)\Steam\steamapps\common\Skul\BepInEx\DamageInsight\Codex";
            if (Directory.Exists(codex))
            {
                found = true;
                foreach (var sub in new[] { "Animations", "Replays" })
                {
                    if (!Directory.Exists(Path.Combine(codex, sub)))
                        continue;
                    foreach (var file in Directory.GetFiles(Path.Combine(codex, sub), "*.json", SearchOption.AllDirectories))
                        foreach (var clip in GearDoc.ParseNode(File.ReadAllText(file)).List("clips"))
                            if (clip.Str("label") is { Length: > 0 } label)
                                Loc.Name(label);
                }
                string debug = Path.Combine(codex, "Debug");
                if (Directory.Exists(debug))
                    foreach (var file in Directory.GetFiles(debug, "*_graph.json"))
                        foreach (var attack in AttackGraph.FromSnapshot(File.ReadAllText(file)).Attacks)
                            Loc.Name(attack.Label);
            }
        }
        finally
        {
            Loc.Recorder = null;
        }
        return names;
    }

    // ---------------------------------------------------------------- files

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DamageInsight", "Plugin.cs")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repository root");
    }

    private static string LangFolder => Path.Combine(RepoRoot(), "DamageInsight", "Lang");

    private static Dictionary<string, object> LanguageFile(string code)
    {
        string path = Path.Combine(LangFolder, code + ".json");
        Assert.True(File.Exists(path), $"Lang/{code}.json is missing");
        return Loc.Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    private sealed class Template
    {
        public readonly List<string> Strings, Names;

        public Template(List<string> strings, List<string> names)
        {
            Strings = strings;
            Names = names;
        }

        private static string FilePath => Path.Combine(LangFolder, "template.json");

        public static Template Load()
        {
            if (!File.Exists(FilePath))
                return new Template(new List<string>(), new List<string>());
            var root = (Dictionary<string, object>)GearDoc.ParseRaw(File.ReadAllText(FilePath, Encoding.UTF8));
            List<string> Keys(string name) => root.TryGetValue(name, out var o) && o is Dictionary<string, object> d ? d.Keys.ToList() : new List<string>();
            return new Template(Keys("strings"), Keys("names"));
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"_note\": ").Append(Json("Every English text the mod shows (generated by the tests: UPDATE_LANG_TEMPLATE=1). " +
                "'strings' come from the code, 'names' from game data (boss moves, animations, skull actions, summons). " +
                "A language file (de.json...) translates all of them under \"strings\". Keys with | are plurals (one|other): " +
                "translate them as a list with the forms the language needs (en/de/es/fr/pt-BR 2, ru/pl 3: one, few, many; " +
                "ko/ja/zh 1). Keep {0} placeholders and <tags>.")).Append(",\n");
            Section(sb, "strings", Strings);
            sb.Append(",\n");
            Section(sb, "names", Names);
            sb.Append("\n}\n");
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }

        private static void Section(StringBuilder sb, string name, List<string> keys)
        {
            sb.Append("  ").Append(Json(name)).Append(": {");
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                string value = key.Contains('|') && key.Contains("{0}")
                    ? "[" + string.Join(", ", key.Split('|').Select(Json)) + "]"
                    : Json(key);
                sb.Append(i == 0 ? "\n" : ",\n").Append("    ").Append(Json(key)).Append(": ").Append(value);
            }
            sb.Append(keys.Count > 0 ? "\n  }" : "}");
        }

        private static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
