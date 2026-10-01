using System;
using System.IO;
using System.Reflection;
using System.Text;
using DamageInsight.Describe;

namespace DamageInsight.Codex;

/// <summary>
/// The hand-written Codex texts (codex_content.json, embedded in the mod): per entry an "about" text and per move
/// (the label under the picture) what it does and how to deal with it.
/// </summary>
public static class CodexContent
{
    private static Node? _root;

    private static Node Root
    {
        get
        {
            if (_root != null)
                return _root.Value;
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DamageInsight.Codex.codex_content.json");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                _root = GearDoc.ParseNode(reader.ReadToEnd()).Child("entries");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Codex: texts could not be loaded: {e.Message}");
                _root = GearDoc.ParseNode("{}");
            }
            return _root.Value;
        }
    }

    public static string About(string entryKey) => Root.Child(entryKey).Str("about") ?? "";

    /// <summary>(what it does, how to deal with it) for a move, or null if nothing is written yet.</summary>
    public static (string what, string tip)? Move(string entryKey, string label)
    {
        var move = Root.Child(entryKey).Child("moves").Child(label);
        if (move.IsNull)
            return null;
        return (move.Str("what") ?? "", move.Str("tip") ?? "");
    }
}
