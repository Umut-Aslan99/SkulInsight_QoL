using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace DamageInsight.Tools;

/// <summary>
/// Writes the serialized data of a Unity object graph (a gear prefab and everything it references)
/// as JSON: every serialized field (public or [SerializeField]) of every MonoBehaviour and plain
/// [Serializable] class, following references to other prefabs (projectiles, runners...).
/// Visual/audio data is skipped. Each object appears once; later references are {"$ref": id}.
/// Development tool for GearScan.
/// </summary>
public sealed class ObjectGraphWriter
{
    private const int MaxDepth = 40;
    private const int MaxArrayLength = 300;
    private const int MaxNodes = 40000;

    private readonly StringBuilder _sb = new();
    private readonly Dictionary<object, int> _ids = new(ReferenceComparer.Instance);
    private readonly HashSet<object> _emitted = new(ReferenceComparer.Instance);
    private int _nextId = 1;
    private int _nodes;
    public bool Truncated { get; private set; }

    /// <summary>Characters (minions, summons) referenced by the written data; only summarized inline.</summary>
    public readonly List<Characters.Character> ReferencedCharacters = new();

    /// <summary>
    /// Addressables links (AssetReference GUIDs) met in the written data: prefabs the game loads on demand,
    /// e.g. Fairy Tale's Oberon or projectiles. GearScan loads and saves them separately.
    /// </summary>
    public readonly List<string> ReferencedAssets = new();

    // Types whose contents are irrelevant for numbers and would bloat the output.
    private static readonly HashSet<string> SkippedTypeNames = new()
    {
        "EffectInfo", "SoundInfo", "AnimationCurve", "Gradient", "Sprite", "Texture", "Texture2D", "AudioClip",
        "Material", "Shader", "Mesh", "AnimationClip", "RuntimeAnimatorController", "AnimatorOverrideController",
        "Animator", "SpriteRenderer", "ParticleSystem", "Transform", "RectTransform", "Font", "TMP_FontAsset",
        "Collider2D", "BoxCollider2D", "CircleCollider2D", "PolygonCollider2D", "CapsuleCollider2D", "Rigidbody2D",
        "PoolObject", "SkeletonAnimation", "SkeletonDataAsset", "Camera", "Light2D", "LineRenderer", "TrailRenderer",
    };

    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

    public string Result => _sb.ToString();

    /// <summary>Writes {"meta": ..., "components": [every MonoBehaviour under root, with its path]}.</summary>
    public void WriteRoot(GameObject root, IDictionary<string, string> meta)
    {
        var components = root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToList();
        foreach (var c in components)
            Id(c); // pre-assign ids so references inside the hierarchy become $ref

        _sb.Append("{\"meta\":{");
        _sb.Append(string.Join(",", meta.Select(kv => $"{Quote(kv.Key)}:{Quote(kv.Value)}")));
        _sb.Append("},\"components\":[");
        for (int i = 0; i < components.Count; i++)
        {
            if (i > 0) _sb.Append(',');
            WriteComponent(components[i], PathOf(components[i].transform, root.transform), 0);
        }
        _sb.Append("]");
        if (Truncated)
            _sb.Append(",\"$truncated\":true");
        _sb.Append('}');
    }

    /// <summary>Writes any object (e.g. a settings ScriptableObject) as a single JSON value.</summary>
    public void WriteValue(object value) => WriteAny(value, 0);

    private void WriteComponent(Component c, string path, int depth)
    {
        _emitted.Add(c);
        _nodes++;
        _sb.Append("{\"$id\":").Append(Id(c)).Append(",\"$type\":").Append(Quote(TypeName(c.GetType())));
        _sb.Append(",\"$go\":").Append(Quote(c.gameObject.name));
        if (path != null)
            _sb.Append(",\"$path\":").Append(Quote(path));
        WriteFields(c, depth);
        _sb.Append('}');
    }

    private void WriteFields(object o, int depth)
    {
        foreach (var f in SerializedFields(o.GetType()))
        {
            object v;
            try { v = f.GetValue(o); }
            catch { continue; }
            if (v == null || (v is UnityEngine.Object uo && uo == null) || IsSkipped(v.GetType()))
                continue;
            _sb.Append(',').Append(Quote(f.Name)).Append(':');
            WriteAny(v, depth + 1);
        }
    }

    private void WriteAny(object v, int depth)
    {
        if (v == null || (v is UnityEngine.Object uo0 && uo0 == null))
        {
            _sb.Append("null");
            return;
        }
        if (depth > MaxDepth || _nodes > MaxNodes)
        {
            Truncated = true;
            _sb.Append("\"$cut\"");
            return;
        }

        Type t = v.GetType();
        switch (v)
        {
            case string s: _sb.Append(Quote(s)); return;
            case bool b: _sb.Append(b ? "true" : "false"); return;
            case float f: _sb.Append(Num(f)); return;
            case double d: _sb.Append(Num(d)); return;
            case Enum e: _sb.Append(Quote(e.ToString())); return;
            case Vector2 v2: _sb.Append($"[{Num(v2.x)},{Num(v2.y)}]"); return;
            case Vector3 v3: _sb.Append($"[{Num(v3.x)},{Num(v3.y)},{Num(v3.z)}]"); return;
            case Vector2Int vi: _sb.Append($"[{vi.x},{vi.y}]"); return;
        }
        if (t.IsPrimitive)
        {
            _sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            return;
        }

        if (v is UnityEngine.Object unityObject)
        {
            WriteUnityObject(unityObject, depth);
            return;
        }
        if (v is IList list)
        {
            _sb.Append('[');
            int n = Math.Min(list.Count, MaxArrayLength);
            bool first = true;
            for (int i = 0; i < n; i++)
            {
                object item = list[i];
                if (item != null && IsSkipped(item.GetType()))
                    continue;
                if (!first) _sb.Append(',');
                first = false;
                WriteAny(item, depth + 1);
            }
            _sb.Append(']');
            return;
        }
        if (v is IDictionary)
        {
            _sb.Append("\"$dictionary\"");
            return;
        }

        if (v is UnityEngine.AddressableAssets.AssetReference link && !string.IsNullOrEmpty(link.AssetGUID)
            && !ReferencedAssets.Contains(link.AssetGUID))
            ReferencedAssets.Add(link.AssetGUID);

        // Plain serializable class or struct.
        if (!t.IsValueType)
        {
            if (_emitted.Contains(v))
            {
                _sb.Append("{\"$ref\":").Append(Id(v)).Append('}');
                return;
            }
            _emitted.Add(v);
        }
        _nodes++;
        _sb.Append('{');
        if (!t.IsValueType)
            _sb.Append("\"$id\":").Append(Id(v)).Append(',');
        _sb.Append("\"$type\":").Append(Quote(TypeName(t)));
        WriteFields(v, depth);
        WriteExtras(v);
        _sb.Append('}');
    }

    private void WriteUnityObject(UnityEngine.Object o, int depth)
    {
        // Already written, or part of the scanned hierarchy (written in the top-level list): reference it.
        if (_emitted.Contains(o) || _ids.ContainsKey(o))
        {
            _sb.Append("{\"$ref\":").Append(Id(o)).Append('}');
            return;
        }
        switch (o)
        {
            case Component c:
                // A component outside the scanned hierarchy (e.g. a projectile or runner prefab): write it inline.
                if (c is Characters.Character character)
                {
                    WriteCharacterSummary(character);
                    return;
                }
                WriteComponent(c, null, depth);
                return;
            case GameObject go:
                // A referenced prefab: list its components (inline, once).
                _emitted.Add(go);
                _sb.Append("{\"$id\":").Append(Id(go)).Append(",\"$prefab\":").Append(Quote(go.name)).Append(",\"components\":[");
                bool first = true;
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    if (!first) _sb.Append(',');
                    first = false;
                    WriteAny(mb, depth + 1);
                }
                _sb.Append("]}");
                return;
            case ScriptableObject so:
                _emitted.Add(so);
                _sb.Append("{\"$id\":").Append(Id(so)).Append(",\"$type\":").Append(Quote(TypeName(so.GetType())));
                _sb.Append(",\"$asset\":").Append(Quote(so.name));
                WriteFields(so, depth);
                _sb.Append('}');
                return;
            default:
                _sb.Append("{\"$unity\":").Append(Quote(TypeName(o.GetType()))).Append(",\"name\":").Append(Quote(o.name)).Append('}');
                return;
        }
    }

    // Characters (minions, summons) are huge; keep only their name and damage numbers.
    private void WriteCharacterSummary(Characters.Character character)
    {
        _emitted.Add(character);
        if (!ReferencedCharacters.Contains(character))
            ReferencedCharacters.Add(character);
        _sb.Append("{\"$id\":").Append(Id(character)).Append(",\"$character\":").Append(Quote(character.gameObject.name));
        _sb.Append(",\"key\":").Append(Quote(character.key.ToString()));
        _sb.Append(",\"attackDamage\":[");
        bool first = true;
        foreach (var ad in character.GetComponentsInChildren<Characters.AttackDamage>(true))
        {
            if (!first) _sb.Append(',');
            first = false;
            _sb.Append($"{{\"go\":{Quote(ad.gameObject.name)},\"min\":{ad.minAttackDamage},\"max\":{ad.maxAttackDamage}}}");
        }
        _sb.Append("]}");
    }

    /// <summary>Human-readable additions for a few types whose raw data is hard to read.</summary>
    private void WriteExtras(object v)
    {
        if (v is Characters.Stat.Value sv)
        {
            string kind = StatKindName(sv.kindIndex);
            _sb.Append(",\"$kind\":").Append(Quote(kind));
            _sb.Append(",\"$category\":").Append(Quote(sv.categoryIndex switch
            {
                0 => "Constant", 1 => "Fixed", 2 => "Percent", 3 => "PercentPoint", 4 => "Final", _ => sv.categoryIndex.ToString(),
            }));
        }
    }

    private static Dictionary<int, string> _statKindNames;

    private static string StatKindName(int index)
    {
        if (_statKindNames == null)
        {
            _statKindNames = new Dictionary<int, string>();
            foreach (var f in typeof(Characters.Stat.Kind).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.GetValue(null) is Characters.Stat.Kind k)
                    _statKindNames[k.index] = f.Name;
            }
        }
        return _statKindNames.TryGetValue(index, out var name) ? name : index.ToString();
    }

    private int Id(object o)
    {
        if (!_ids.TryGetValue(o, out int id))
            _ids[o] = id = _nextId++;
        return id;
    }

    private static bool IsSkipped(Type t)
    {
        for (var x = t; x != null; x = x.BaseType)
            if (SkippedTypeNames.Contains(x.Name))
                return true;
        return typeof(Delegate).IsAssignableFrom(t);
    }

    /// <summary>Instance fields Unity would serialize: public or [SerializeField], not [NonSerialized], up the hierarchy.</summary>
    private static FieldInfo[] SerializedFields(Type type)
    {
        if (FieldCache.TryGetValue(type, out var cached))
            return cached;
        var fields = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object)
                            && t != typeof(Component) && t != typeof(UnityEngine.Object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsNotSerialized || f.Name.Contains("k__BackingField"))
                    continue;
                if (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                    fields.Add(f);
            }
        }
        return FieldCache[type] = fields.ToArray();
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent)
            parts.Add(x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string TypeName(Type t) => t.FullName?.Replace('+', '.') ?? t.Name;

    private static string Num(double d) =>
        double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
