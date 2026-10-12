using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DamageInsight.Describe;
using DamageInsight.Lang;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

/// <summary>
/// QA corpus (docs/QA_CAPTURE_PLAN.md, docs/QA_LOG.md): every text the mod writes into a description, for every
/// skull, skill, swap, item, quintessence and inscription step / tuned form in the full gear scan, built with the
/// mod's own layout (DescriptionLayout) at neutral stats, in every language, with automatic flags. Runs only with
/// QA_CORPUS set (an output folder, or "1" for SkulModding\QA\corpus); QA_LANGS limits the languages ("en,de").
/// tools/qa/analyze.py turns the result into a report.
/// </summary>
public class QaCorpusTests
{
    private readonly ITestOutputHelper _out;
    public QaCorpusTests(ITestOutputHelper output) => _out = output;

    private static readonly Regex Tags = new("<[^>]+>");
    private static readonly Regex CodeLabel = new(@"[A-Za-z]+_\d|[a-z][A-Z][a-z]|_[A-Za-z]");

    private sealed class Entry
    {
        public string Category = "", Name = "", Display = "", Part = "", Game = "", Full = "", Short = "", Compact = "";
        public readonly List<string> Flags = new();
    }

    [FixtureFact]
    public void Qa_corpus_writes_every_description()
    {
        string folder = Fixture.FullScanFolder();
        string target = Environment.GetEnvironmentVariable("QA_CORPUS");
        if (folder == null || string.IsNullOrWhiteSpace(target))
            return;
        if (target == "1")
            target = Path.Combine(Path.GetDirectoryName(folder)!, "QA", "corpus");
        var langs = (Environment.GetEnvironmentVariable("QA_LANGS") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        if (langs.Count == 0)
            langs = Loc.Languages.Select(l => l.code).ToList();

        var scan = new Scan(folder);
        try
        {
            foreach (string lang in langs)
            {
                Loc.Use(lang);
                scan.ClearAnalyses(); // analyses hold translated labels
                var entries = scan.Entries();
                Write(Path.Combine(target, lang), entries);
                _out.WriteLine($"{lang}: {entries.Count} texts, {entries.Count(e => e.Flags.Count > 0)} flagged");
            }
        }
        finally
        {
            Loc.Use(Loc.English);
            DescriptionFormatter.Short = false;
        }
    }

    /// <summary>The gear scan on disk, analyzed like the game does it (bodies, summons, upgrades, inscriptions).</summary>
    private sealed class Scan
    {
        private readonly string _folder;
        private readonly Dictionary<string, Breakdown> _analyses = new();
        private readonly Node _statuses;
        private readonly double _skulMin, _skulMax;

        public Scan(string folder)
        {
            _folder = folder;
            _statuses = GearDoc.ParseNode(File.ReadAllText(Path.Combine(folder, "statuses.json")));
            var skul = Analyze("weapons", "Skul");
            (_skulMin, _skulMax) = (skul?.BaseMin ?? 0, skul?.BaseMax ?? 0);
        }

        public void ClearAnalyses() => _analyses.Clear();

        private string FileOf(string category, string name) => Path.Combine(_folder, category, name + ".json");

        private Breakdown Analyze(string category, string name)
        {
            string key = category + "/" + name;
            if (_analyses.TryGetValue(key, out var b))
                return b;
            string file = FileOf(category, name);
            b = File.Exists(file) ? GearAnalyzer.Analyze(GearDoc.Parse(File.ReadAllText(file))) : null;
            _analyses[key] = b;
            return b;
        }

        private static Node Meta(string file) => GearDoc.ParseNode(File.ReadAllText(file)).Child("meta");

        /// <summary>Summoned characters linked in the gear's data (inline "$character" summaries), with hits.</summary>
        private List<(string, Breakdown)> Summons(string raw)
        {
            var list = new List<(string, Breakdown)>();
            foreach (string name in Regex.Matches(raw, "\"\\$character\":\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
            {
                var b = Analyze("characters", name);
                if (b == null || !b.Sections.Any(s => s.HasHits))
                    continue;
                string file = FileOf("characters", name);
                string display = Meta(file).Str("displayName") ?? "";
                string essence = FileOf("essences", name);
                if (display.Length == 0 && File.Exists(essence))
                    display = Meta(essence).Str("displayName") ?? ""; // the game's quintessence name (English in the scan)
                if (display.Length == 0)
                    display = SummonNames.For(name);
                display = display.Length > 0 ? display : name.Replace("_", " ").Trim();
                if (list.All(x => x.Item1 != display))
                    list.Add((display, b));
            }
            return list;
        }

        /// <summary>Transformed bodies (StartWeaponPolymorph's "_polymorphWeapon"), as the game finds them.</summary>
        private List<(bool, Breakdown)> Bodies(string raw, string self)
        {
            var list = new List<(bool, Breakdown)>();
            foreach (string name in Regex.Matches(raw, "\"\\$go\":\"([^\"]*Polymorph[^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
            {
                if (name == self)
                    continue;
                var b = Analyze("weapons", name);
                if (b != null && b.Sections.Any(s => s.HasHits && s.Kind != "Passive"))
                    list.Add((name.Contains("Swap"), b));
            }
            return list;
        }

        private StatSnapshot StatsFor(Breakdown b)
        {
            var stats = StatSnapshot.Neutral.Copy();
            bool skull = b != null && b.Category == "weapons" && b.BaseMax > 0;
            stats.SkullMin = skull ? b.BaseMin : _skulMin; // items: a fresh run's Little Bone
            stats.SkullMax = skull ? b.BaseMax : _skulMax;
            return stats;
        }

        public List<Entry> Entries()
        {
            var entries = new List<Entry>();
            foreach (string file in Directory.GetFiles(Path.Combine(_folder, "weapons"), "*.json").OrderBy(f => f))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("Ref_") || name.Contains("Polymorph"))
                    continue; // reference copies and transformed bodies (listed in their skull's text)
                string raw = File.ReadAllText(file);
                var meta = Meta(file);
                var b = Analyze("weapons", name);
                var stats = StatsFor(b);
                var summons = Summons(raw);
                var bodies = Bodies(raw, name);
                string display = meta.Str("displayName") ?? "";
                Add(entries, "skull", name, display, "passive", meta.Str("description"), b,
                    () => DescriptionLayout.Weapon(b, summons, bodies, stats), bodies.Select(x => x.Item2));
                Add(entries, "swap", name, display, "swap", meta.Str("activeDescription"), b, () => DescriptionLayout.Swap(b, stats));
                foreach (var skill in b.Sections.Where(s => s.Kind == "Skill").Select(s => s.Key).Distinct())
                    Add(entries, "skill", name, display, skill, "", b, () => DescriptionLayout.Skill(b, skill, stats));
            }

            Func<string, Breakdown> item = n => Analyze("items", n);
            Func<string, string> itemName = n =>
            {
                string f = FileOf("items", n);
                string d = File.Exists(f) ? Meta(f).Str("displayName") ?? "" : "";
                return d.Length > 0 ? d : n;
            };
            foreach (string file in Directory.GetFiles(Path.Combine(_folder, "items"), "*.json").OrderBy(f => f))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var meta = Meta(file);
                var b = Analyze("items", name);
                var stats = StatsFor(b);
                var summons = Summons(File.ReadAllText(file));
                Add(entries, "item", name, meta.Str("displayName") ?? "", "effect", meta.Str("description"), b,
                    () => DescriptionLayout.Item(b, item, itemName, summons, stats));
            }

            foreach (string file in Directory.GetFiles(Path.Combine(_folder, "essences"), "*.json").OrderBy(f => f))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var meta = Meta(file);
                var b = Analyze("essences", name);
                var stats = StatsFor(b);
                var summons = Summons(File.ReadAllText(file));
                string game = Join(meta.Str("description"), meta.Str("activeDescription"));
                Add(entries, "quintessence", name, meta.Str("displayName") ?? "", "active", game, b,
                    () => DescriptionLayout.Essence(b, summons, stats));
            }

            foreach (string file in Directory.GetFiles(Path.Combine(_folder, "inscriptions"), "*.json").OrderBy(f => f))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var meta = Meta(file);
                var b = Analyze("inscriptions", name);
                var stats = StatsFor(b);
                int steps = (meta.Str("steps") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
                for (int step = 1; step < steps; step++)
                {
                    int s = step;
                    Add(entries, "inscription", name, meta.Str("displayName") ?? "", $"step {step}", meta.Str($"desc{step}"), b, () =>
                    {
                        var lines = new List<string>();
                        string status = StatusDescriptions.StatusOfInscription(name);
                        if (s == 1 && status.Length > 0)
                            lines.Add(StatusDescriptions.Describe(_statuses, status, stats));
                        lines.AddRange(DescriptionLayout.InscriptionParts(b, s, stats));
                        return string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
                    });
                }
                string super = meta.Str("superDescription") ?? "";
                if (super.Length > 0)
                    Add(entries, "inscription", name, meta.Str("displayName") ?? "", "tuned", super, b,
                        () => string.Join("\n", DescriptionLayout.InscriptionParts(b, Section.SuperStep, stats)));
            }
            return entries;
        }

        private static string Join(params string[] parts) => string.Join("\n", parts.Where(p => !string.IsNullOrEmpty(p)));

        private static void Add(List<Entry> entries, string category, string name, string display, string part, string game,
            Breakdown b, Func<string> build, IEnumerable<Breakdown> bodies = null)
        {
            var e = new Entry { Category = category, Name = name, Display = display, Part = part, Game = game ?? "" };
            try
            {
                if (b != null)
                {
                    DescriptionFormatter.Short = false;
                    e.Full = build();
                    DescriptionFormatter.Short = true;
                    e.Short = build();
                    DescriptionFormatter.Short = false;
                    DescriptionLayout.Compact = true;
                    e.Compact = build();
                    if (category == "skull" && e.Compact.Length > 0)
                        e.Compact += "\n" + DescriptionLayout.CodexFooter("K");
                }
            }
            catch (Exception ex)
            {
                e.Flags.Add("error:" + ex.GetType().Name + ":" + ex.Message.Replace('\t', ' ').Replace('\n', ' '));
            }
            finally
            {
                DescriptionFormatter.Short = false;
                DescriptionLayout.Compact = false;
            }
            if (b != null)
                Flag(e, b, category, part, bodies);
            if (e.Full.Length > 0 || e.Game.Length > 0 || e.Flags.Count > 0)
                entries.Add(e);
        }

        /// <summary>Automatic checks on the structure behind a text.</summary>
        private static void Flag(Entry e, Breakdown b, string category, string part, IEnumerable<Breakdown> bodies)
        {
            IEnumerable<Section> sections = category switch
            {
                "skull" => b.Sections.Where(s => s.Kind is "Basic" or "Jump" or "Dash" or "Passive")
                    .Concat((bodies ?? Enumerable.Empty<Breakdown>()).SelectMany(x => x.Sections.Where(s => s.Kind != "Passive" && s.Kind != "Swap"))),
                "swap" => b.Sections.Where(s => s.Kind == "Swap"),
                "skill" => b.Sections.Where(s => s.Kind == "Skill" && s.Key == part),
                "inscription" => b.Sections.Where(s => s.Kind == "Inscription"),
                _ => b.Sections,
            };
            foreach (var section in sections.Where(s => s.HasHits))
            {
                var steps = section.Steps.Where(st => st.Hits.Count > 0).ToList();
                // Steps that are all the same (a dash's 4 equal parts): candidates for one merged line.
                bool identical = steps.Count >= 2 && steps.All(st => st.Hits.Count == steps[0].Hits.Count
                    && st.Hits.Zip(steps[0].Hits, (x, y) => x.SameAs(y) && x.Count == y.Count).All(same => same));
                if (identical)
                    e.Flags.Add($"identical-steps:{section.Kind}:{steps.Count}");
                // Otherwise the same extra hit in every step: usually a link the analyzer should not follow (King Arthur).
                else if (steps.Count >= 3)
                    foreach (var hit in steps[0].Hits)
                        if (steps.All(st => st.Hits.Count > 1 && st.Hits.Any(h => h.SameAs(hit) && h.Owner == hit.Owner)))
                        {
                            e.Flags.Add($"same-hit-every-step:{section.Kind}:{hit.Owner}:{N(hit.MultMax)}");
                            break;
                        }
                foreach (var hit in steps.SelectMany(st => st.Hits))
                {
                    if (hit.MultMax > 30)
                        e.Flags.Add($"huge-multiplier:{section.Kind}:{hit.Owner}:{N(hit.MultMax)}");
                    if (hit.MultMax <= 0)
                        e.Flags.Add($"zero-multiplier:{section.Kind}:{hit.Owner}");
                    if (hit.Count == 0)
                        e.Flags.Add($"repeats:{section.Kind}:{hit.Owner}");
                }
                foreach (string label in steps.Select(st => st.Label).Append(section.Title).Where(l => !string.IsNullOrEmpty(l)))
                    if (CodeLabel.IsMatch(label))
                        e.Flags.Add($"code-label:{label}");
            }
            if (category == "skull" && b.Sections.Count(s => s.Kind == "Basic" && s.HasHits) > 1)
                e.Flags.Add("several-basic-combos");
            e.Flags.Sort();
            var distinct = e.Flags.Distinct().ToList();
            e.Flags.Clear();
            e.Flags.AddRange(distinct);
        }
    }

    private static string Plain(string text) => Tags.Replace(text ?? "", "");

    private static string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void Write(string dir, List<Entry> entries)
    {
        Directory.CreateDirectory(dir);
        var tsv = new StringBuilder("category\tname\tdisplay\tpart\tgame_chars\tfull_chars\tfull_lines\tshort_chars\tshort_lines\tcompact_chars\tcompact_lines\tlongest_line\tflags\n");
        var jsonl = new StringBuilder();
        var txt = new StringBuilder();
        foreach (var e in entries)
        {
            string full = Plain(e.Full), shortText = Plain(e.Short), compact = Plain(e.Compact), game = Plain(e.Game);
            int compactLines = compact.Length == 0 ? 0 : compact.Split('\n').Length;
            int fullLines = full.Length == 0 ? 0 : full.Split('\n').Length;
            int shortLines = shortText.Length == 0 ? 0 : shortText.Split('\n').Length;
            int longest = full.Length == 0 ? 0 : full.Split('\n').Max(l => l.Length);
            tsv.Append($"{e.Category}\t{e.Name}\t{e.Display}\t{e.Part}\t{game.Length}\t{full.Length}\t{fullLines}\t{shortText.Length}\t{shortLines}\t{compact.Length}\t{compactLines}\t{longest}\t{string.Join(" ", e.Flags)}\n");
            jsonl.Append('{')
                .Append($"\"category\":{Json(e.Category)},\"name\":{Json(e.Name)},\"display\":{Json(e.Display)},\"part\":{Json(e.Part)},")
                .Append($"\"game\":{Json(game)},\"full\":{Json(full)},\"short\":{Json(shortText)},\"compact\":{Json(compact)},\"rich\":{Json(e.Full)},")
                .Append($"\"flags\":[{string.Join(",", e.Flags.Select(Json))}]")
                .Append("}\n");
            txt.Append($"==== {e.Category} {e.Name} ({e.Display}) [{e.Part}] {string.Join(" ", e.Flags)}\n");
            if (game.Length > 0)
                txt.Append(game).Append('\n');
            if (full.Length > 0)
                txt.Append("--\n").Append(full).Append('\n');
            if (compact.Length > 0 && compact != full)
                txt.Append("-- compact\n").Append(compact).Append('\n');
            txt.Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "corpus.tsv"), tsv.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "corpus.jsonl"), jsonl.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "corpus.txt"), txt.ToString(), new UTF8Encoding(false));
    }

    private static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
