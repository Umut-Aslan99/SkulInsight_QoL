using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DamageInsight.Codex;

/// <summary>
/// A saved graph: every unit as the rules read it before classifying (names, kinds, labels, tags, steps, calls,
/// which trees the AI holds), written next to a boss's report as <c>&lt;key&gt;_graph.json</c>. Loaded again
/// (<see cref="FromSnapshot"/>), it classifies exactly like in the game, so naming rules can be fixed and tested
/// without fighting the boss again.
/// </summary>
public sealed partial class AttackGraph
{
    public string SnapshotJson(Func<object, string> stepKey, Func<object, string> stepLabel, Func<object, string> describe)
    {
        var unitIndex = new Dictionary<Unit, int>();
        for (int i = 0; i < Units.Count; i++)
            unitIndex[Units[i]] = i;
        var owners = new Dictionary<object, int>();
        var steps = new Dictionary<object, int>();
        var stepList = new List<object>();
        int Owner(Unit u)
        {
            var key = u.Owner.Key ?? u;
            if (!owners.TryGetValue(key, out int i))
                owners[key] = i = owners.Count;
            return i;
        }
        int Step(object s)
        {
            if (!steps.TryGetValue(s, out int i))
            {
                steps[s] = i = stepList.Count;
                stepList.Add(s);
            }
            return i;
        }
        string Ids(IEnumerable<int> ids) => "[" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + "]";

        var sb = new StringBuilder("{\"units\":[\n");
        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            sb.Append(i == 0 ? "" : ",\n").Append('{')
              .Append("\"n\":").Append(Str(u.Name))
              .Append(",\"t\":").Append(Str(u.TypeName))
              .Append(",\"m\":").Append(Str(u.MethodName))
              .Append(",\"p\":").Append(u.MethodPublic ? "true" : "false")
              .Append(",\"k\":").Append(Str(u.Kind.ToString()))
              .Append(",\"l\":").Append(Str(u.RawLabel))
              .Append(",\"g\":").Append(Str(u.Tag))
              .Append(",\"note\":").Append(Str(u.Note))
              .Append(",\"ai\":").Append(u.OnAi ? "true" : "false")
              .Append(",\"o\":").Append(Owner(u))
              .Append(",\"own\":").Append(Ids(u.Own.Select(Step)))
              .Append(",\"steps\":").Append(Ids(u.Steps.Select(Step)))
              .Append(",\"calls\":").Append(Ids(u.Calls.Where(unitIndex.ContainsKey).Select(c => unitIndex[c])))
              .Append(",\"runs\":[").Append(string.Join(",", u.Runs.Select(r => Ids(r.Where(unitIndex.ContainsKey).Select(c => unitIndex[c]))))).Append(']')
              .Append('}');
        }
        var held = _aiHeldOrder.Where(owners.ContainsKey).Select(o => owners[o]);
        sb.Append("\n],\"held\":").Append(Ids(held)).Append(",\"steps\":[\n");
        for (int i = 0; i < stepList.Count; i++)
        {
            var s = stepList[i];
            sb.Append(i == 0 ? "" : ",\n").Append('{')
              .Append("\"key\":").Append(Str(Safe(stepKey, s)))
              .Append(",\"label\":").Append(Str(Safe(stepLabel, s)))
              .Append(",\"text\":").Append(Str(Safe(describe, s) ?? s?.ToString()))
              .Append('}');
        }
        return sb.Append("\n]}").ToString();
    }

    /// <summary>A graph from <see cref="SnapshotJson"/>, classified. Steps are "s&lt;n&gt;" strings.</summary>
    public static AttackGraph FromSnapshot(string json)
    {
        var root = (Dictionary<string, object>)Describe.GearDoc.ParseRaw(json);
        var graph = new AttackGraph(typeof(object), Array.Empty<Type>());
        var stepData = ((List<object>)root["steps"]).Cast<Dictionary<string, object>>().ToList();
        var keys = new Dictionary<object, string>();
        var labels = new Dictionary<object, string>();
        var texts = new Dictionary<object, string>();
        for (int i = 0; i < stepData.Count; i++)
        {
            string id = "s" + i;
            keys[id] = stepData[i]["key"] as string ?? id;
            labels[id] = stepData[i]["label"] as string;
            texts[id] = stepData[i]["text"] as string ?? id;
        }
        graph.StepKey = s => keys.TryGetValue(s, out var k) ? k : s?.ToString();
        graph.StepLabel = s => labels.TryGetValue(s, out var l) ? l : null;
        graph.StepText = s => texts.TryGetValue(s, out var t) ? t : s?.ToString();

        var data = ((List<object>)root["units"]).Cast<Dictionary<string, object>>().ToList();
        var ownerObjects = new Dictionary<int, object>();
        object OwnerObject(int i) => ownerObjects.TryGetValue(i, out var o) ? o : ownerObjects[i] = new object();
        List<int> Ints(object list) => ((List<object>)list).Select(x => (int)(double)x).ToList();
        foreach (var d in data)
        {
            var u = new Unit
            {
                Owner = new Node(typeof(object), OwnerObject((int)(double)d["o"]), ""),
                Name = d["n"] as string ?? "",
                TypeName = d["t"] as string,
                MethodName = d["m"] as string,
                MethodPublic = d["p"] is true,
                Kind = (Kind)Enum.Parse(typeof(Kind), (string)d["k"]),
                Label = Tidy(d["l"] as string ?? ""),
                Tag = d["g"] as string,
                Note = d.TryGetValue("note", out var note) ? note as string : null,
                OnAi = d["ai"] is true,
            };
            u.RawLabel = u.Label;
            u.Own.AddRange(Ints(d["own"]).Select(i => (object)("s" + i)));
            u.Steps.AddRange(Ints(d["steps"]).Select(i => (object)("s" + i)));
            graph.Units.Add(u);
        }
        for (int i = 0; i < data.Count; i++)
        {
            var u = graph.Units[i];
            foreach (int c in Ints(data[i]["calls"]))
            {
                u.Calls.Add(graph.Units[c]);
                graph.Units[c].CalledBy.Add(u);
            }
            foreach (var run in (List<object>)data[i]["runs"])
                u.Runs.Add(Ints(run).Select(c => graph.Units[c]).ToList());
        }
        foreach (int o in Ints(root["held"]))
            if (graph._aiHeld.Add(OwnerObject(o)))
                graph._aiHeldOrder.Add(OwnerObject(o));
        graph.Classify();
        return graph;
    }

    /// <summary>How a step reads in reports of a loaded graph (the text saved with it).</summary>
    public Func<object, string> StepText;

    private static string Safe(Func<object, string> f, object s)
    {
        try { return f?.Invoke(s); }
        catch (Exception) { return null; }
    }

    private static string Str(string s)
    {
        if (s == null)
            return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        return sb.Append('"').ToString();
    }
}
