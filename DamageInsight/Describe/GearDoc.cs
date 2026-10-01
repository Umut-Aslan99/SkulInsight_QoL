using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DamageInsight.Describe;

/// <summary>
/// A parsed gear scan (the JSON written by ObjectGraphWriter), with {"$ref": id} links resolved.
/// Used both by the tests (on saved scan files) and in-game (on a freshly serialized live gear),
/// so both run exactly the same description code. Plain logic, no Unity.
/// </summary>
public sealed class GearDoc
{
    private readonly Dictionary<long, Dictionary<string, object>> _byId = new();

    public Node Root { get; }
    public IReadOnlyDictionary<string, string> Meta { get; }
    public IReadOnlyList<Node> Components { get; }

    private GearDoc(Dictionary<string, object> root)
    {
        Index(root);
        Root = new Node(this, root);
        Meta = root.TryGetValue("meta", out var m) && m is Dictionary<string, object> meta
            ? meta.ToDictionary(kv => kv.Key, kv => kv.Value as string ?? "")
            : new Dictionary<string, string>();
        Components = Root.List("components").ToList();
    }

    public static GearDoc Parse(string json) => new((Dictionary<string, object>)new JsonParser(json).ParseValue());

    /// <summary>Parses any JSON value (e.g. the status settings) and returns it as a node.</summary>
    public static Node ParseNode(string json) => Parse("{\"value\":" + json + "}").Root.Child("value");

    /// <summary>Plain JSON: objects → Dictionary, arrays → List, numbers → double, strings, bools, null.</summary>
    internal static object ParseRaw(string json) => new JsonParser(json).ParseValue();

    internal object Resolve(object value)
    {
        if (value is Dictionary<string, object> d && d.Count == 1 && d.TryGetValue("$ref", out var r) && r is double id
            && _byId.TryGetValue((long)id, out var target))
            return target;
        return value;
    }

    private void Index(object value)
    {
        switch (value)
        {
            case Dictionary<string, object> d:
                if (d.TryGetValue("$id", out var id) && id is double n)
                    _byId[(long)n] = d;
                foreach (var v in d.Values)
                    Index(v);
                break;
            case List<object> list:
                foreach (var v in list)
                    Index(v);
                break;
        }
    }

    /// <summary>Minimal JSON parser: objects → Dictionary, arrays → List, numbers → double.</summary>
    private sealed class JsonParser
    {
        private readonly string _s;
        private int _i;

        public JsonParser(string s)
        {
            _s = s;
            if (_s.Length > 0 && _s[0] == '﻿')
                _i = 1; // BOM
        }

        public object ParseValue()
        {
            SkipWhitespace();
            char c = _s[_i];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': _i += 4; return true;
                case 'f': _i += 5; return false;
                case 'n': _i += 4; return null;
                default: return ParseNumber();
            }
        }

        private Dictionary<string, object> ParseObject()
        {
            var d = new Dictionary<string, object>();
            _i++;
            SkipWhitespace();
            if (_s[_i] == '}') { _i++; return d; }
            while (true)
            {
                SkipWhitespace();
                string key = ParseString();
                SkipWhitespace();
                _i++; // ':'
                d[key] = ParseValue();
                SkipWhitespace();
                if (_s[_i++] == '}') return d;
            }
        }

        private List<object> ParseArray()
        {
            var list = new List<object>();
            _i++;
            SkipWhitespace();
            if (_s[_i] == ']') { _i++; return list; }
            while (true)
            {
                list.Add(ParseValue());
                SkipWhitespace();
                if (_s[_i++] == ']') return list;
            }
        }

        private string ParseString()
        {
            var sb = new StringBuilder();
            _i++; // opening quote
            while (true)
            {
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = _s[_i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber)); _i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        private double ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0)
                _i++;
            return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void SkipWhitespace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                _i++;
        }
    }
}

/// <summary>One object in a gear scan: a component or a serialized class, with typed field access.</summary>
public readonly struct Node
{
    private readonly GearDoc _doc;
    internal readonly Dictionary<string, object> Data;

    internal Node(GearDoc doc, Dictionary<string, object> data)
    {
        _doc = doc;
        Data = data;
    }

    public bool IsNull => Data == null;

    /// <summary>Full type name, e.g. "Characters.Operations.HitInfo".</summary>
    public string Type => Str("$type") ?? (Data != null && Data.ContainsKey("$prefab") ? "$prefab" : Data != null && Data.ContainsKey("$character") ? "$character" : "");

    /// <summary>Last part of the type name, e.g. "HitInfo".</summary>
    public string ShortType
    {
        get
        {
            string t = Type;
            int dot = t.LastIndexOf('.');
            return dot >= 0 ? t.Substring(dot + 1) : t;
        }
    }

    public string Path => Str("$path");
    public string GameObject => Str("$go");

    public bool Is(params string[] shortTypes) => shortTypes.Contains(ShortType);

    public bool Has(string field) => Data != null && Data.ContainsKey(field);

    public object Raw(string field) =>
        Data != null && Data.TryGetValue(field, out var v) ? _doc.Resolve(v) : null;

    public Node Child(string field) =>
        Raw(field) is Dictionary<string, object> d ? new Node(_doc, d) : default;

    public double Num(string field, double fallback = 0) => Raw(field) is double d ? d : fallback;

    public string Str(string field) => Data != null && Data.TryGetValue(field, out var v) ? v as string : null;

    public bool Bool(string field) => Raw(field) is bool b && b;

    /// <summary>
    /// The nodes in a list-like field: a JSON array, or a serialized container
    /// (SubcomponentArray "_components", ReorderableArray "values", prefab "components").
    /// </summary>
    public IEnumerable<Node> List(string field)
    {
        object v = Raw(field);
        if (v is Dictionary<string, object> d)
        {
            var container = new Node(_doc, d);
            foreach (var key in new[] { "_components", "values", "components" })
                if (container.Has(key))
                    return container.List(key);
            return Enumerable.Empty<Node>();
        }
        return v is List<object> list ? Nodes(list) : Enumerable.Empty<Node>();
    }

    /// <summary>Numbers in a JSON array field (e.g. a Vector2 or a float list).</summary>
    public IReadOnlyList<double> Numbers(string field)
    {
        object v = Raw(field);
        if (v is Dictionary<string, object> d && new Node(_doc, d).Raw("values") is List<object> values)
            v = values;
        return v is List<object> list ? list.OfType<double>().ToList() : (IReadOnlyList<double>)Array.Empty<double>();
    }

    /// <summary>All fields with their (resolved) values, in order.</summary>
    public IEnumerable<(string name, object value)> Fields()
    {
        if (Data == null)
            yield break;
        foreach (var kv in Data)
            if (!kv.Key.StartsWith("$"))
                yield return (kv.Key, _doc.Resolve(kv.Value));
    }

    internal IEnumerable<Node> Nodes(IEnumerable<object> values)
    {
        foreach (var v in values)
            if (_doc.Resolve(v) is Dictionary<string, object> d)
                yield return new Node(_doc, d);
    }

    internal Node Wrap(object value) => _doc.Resolve(value) is Dictionary<string, object> d ? new Node(_doc, d) : default;

    public override string ToString() => $"{ShortType} {Path ?? GameObject}";
}
