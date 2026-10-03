using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Codex;
using Xunit;
using Kind = DamageInsight.Codex.AttackGraph.Kind;

namespace DamageInsight.Tests;

/// <summary>
/// Saved graphs (<c>&lt;key&gt;_graph.json</c>, written in the game next to each report) load and classify like the
/// live ones. The game's own dumps (local only) are classified into graph-review.txt next to the test binaries.
/// </summary>
public class AttackGraphSnapshotTests
{
    [Fact]
    public void A_saved_graph_classifies_like_the_original()
    {
        var original = new AttackGraph(typeof(Characters.AI.AIController), BossAttacks.StepTypes);
        original.StepKey = s => s.ToString().Split('#')[0];
        original.Build(Enumerable.Empty<AttackGraph.Node>(), addMore: g =>
        {
            AttackGraph.Unit Add(System.Type type, string name, Kind kind, string label, params string[] steps) =>
                g.AddTreeUnit(new AttackGraph.Node(type, new object(), ""), name, kind, steps, label, null);
            var loop = Add(typeof(Characters.AI.Behaviours.InfiniteLoop), "InfiniteLoop", Kind.Dispatcher, "Behaviours");
            var pick = Add(typeof(object), "RandomBehaviour", Kind.Dispatcher, "Random behaviour");
            g.Link(loop, pick);
            var seq = Add(typeof(object), "Sequence", Kind.Sequence, "Fist slam");
            g.Link(pick, seq);
            g.Link(seq, Add(typeof(object), "PlayAnimations", Kind.Plain, "Intro", "intro"));
            var repeat = Add(typeof(Characters.AI.Behaviours.Repeat), "Repeat", Kind.Dispatcher, "Fist slam");
            g.Link(seq, repeat);
            g.Link(repeat, Add(typeof(object), "PlayAnimations", Kind.Plain, "Fist slam", "slam"));
            var bomb = Add(typeof(object), "Sequence", Kind.Sequence, "Sequence");
            g.Link(pick, bomb);
            g.Link(bomb, Add(typeof(object), "PlayAnimations", Kind.Plain, "Energy bomb", "bomb"));
            g.Link(bomb, Add(typeof(object), "PlayAnimations", Kind.Plain, "Groggy", "groggy"));
            g.Link(pick, Add(typeof(object), "RunAction", Kind.Plain, "Back dash long", "backdash#1"));
            g.Link(pick, Add(typeof(object), "RunAction", Kind.Plain, "Back dash short", "backdash#2"));
        });

        string json = original.SnapshotJson(original.StepKey, _ => null, s => s.ToString());
        var loaded = AttackGraph.FromSnapshot(json);

        string Summary(AttackGraph g, System.Func<object, string> text) =>
            string.Join("; ", g.Attacks.Select(a => a.Label + "=" + string.Join("/", a.Steps.Select(text))));
        Assert.Equal(Summary(original, s => s.ToString()), Summary(loaded, loaded.StepText));
    }

    [Fact]
    public void Real_graph_dumps_are_classified_for_review()
    {
        // The dumps the game wrote (local only; SKUL_CODEX_DEBUG overrides the Steam folder).
        string folder = System.Environment.GetEnvironmentVariable("SKUL_CODEX_DEBUG") ??
                        @"C:\Program Files (x86)\Steam\steamapps\common\Skul\BepInEx\DamageInsight\Codex\Debug";
        if (!Directory.Exists(folder))
            return;
        var sb = new StringBuilder();
        foreach (var file in Directory.GetFiles(folder, "*_graph.json").OrderBy(f => f))
        {
            var g = AttackGraph.FromSnapshot(File.ReadAllText(file));
            sb.Append("== ").Append(Path.GetFileName(file).Replace("_graph.json", "")).Append(" (").Append(g.Attacks.Count(a => !a.IsTail))
              .Append(" moves)\n");
            foreach (var a in g.Attacks)
            {
                sb.Append(a.IsTail ? "   (tail) " : "   ").Append(a.Label).Append("  = ")
                  .Append(string.Join(", ", a.Steps.Select(g.StepText))).Append('\n');
                if (!a.IsTail && a.Units.Count > 0 && MoveHints.HintFor(a.Units.Where(u => u.Entry).DefaultIfEmpty(a.Units[0])) is { Length: > 0 } hint)
                    sb.Append("      when: ").Append(hint).Append('\n');
            }
        }
        File.WriteAllText(Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "graph-review.txt"), sb.ToString(), new UTF8Encoding(false));
    }
}
