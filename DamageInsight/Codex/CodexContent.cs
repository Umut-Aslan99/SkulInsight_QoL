using System;
using System.IO;
using System.Reflection;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Lang;

namespace DamageInsight.Codex;

/// <summary>
/// The hand-written Codex texts (codex_content.json, embedded in the mod): per entry an "about" text and per move
/// (the label under the picture) what it does and how to deal with it. Translations sit in
/// codex_content.&lt;language&gt;.json with the same keys; whatever a translation lacks comes from the English file.
/// </summary>
public static class CodexContent
{
    private static Node? _english;
    private static (string code, Node root)? _translated;

    static CodexContent() => Loc.Changed += () => _translated = null;

    private static Node Load(string resource)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null)
                return GearDoc.ParseNode("{}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return GearDoc.ParseNode(reader.ReadToEnd()).Child("entries");
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning($"Codex: texts could not be loaded ({resource}): {e.Message}");
            return GearDoc.ParseNode("{}");
        }
    }

    private static Node English => _english ??= Load("DamageInsight.Codex.codex_content.json");

    private static Node Translated
    {
        get
        {
            string code = Loc.Current;
            if (_translated == null || _translated.Value.code != code)
                _translated = (code, code == Loc.English ? GearDoc.ParseNode("{}") : Load($"DamageInsight.Codex.codex_content.{code}.json"));
            return _translated.Value.root;
        }
    }

    private static string Text(Func<Node, Node> at, string field) =>
        at(Translated).Str(field) is { Length: > 0 } translated ? translated : at(English).Str(field);

    public static string About(string entryKey) => Text(root => root.Child(entryKey), "about") ?? "";

    /// <summary>(what it does, how to deal with it) for a move, or null if nothing is written yet.</summary>
    public static (string what, string tip)? Move(string entryKey, string label)
    {
        Node At(Node root) => root.Child(entryKey).Child("moves").Child(label);
        if (At(English).IsNull && At(Translated).IsNull)
            return null;
        return (Text(At, "what") ?? "", Text(At, "tip") ?? "");
    }
}
