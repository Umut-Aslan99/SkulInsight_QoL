using System.Collections.Generic;

namespace DamageInsight.Codex;

/// <summary>
/// The Codex page layout (Umut 2026-10-12: "start boxing things to create proper sections, with icons; each skull shows
/// its skills as boxes with their icon, or a ? until a won run had them"). A page is still built as rich text, with
/// markers the window turns into blocks: the text before the first section, then sections (a framed box with an icon
/// and a title) holding text and skill tiles. Plain logic, no Unity.
/// </summary>
public static class PageBlocks
{
    // Private-use characters: never in game or translation texts.
    private const char SectionStart = '', SectionMid = '', SectionEnd = '';
    private const char TileStart = '', TileMid = '', TileEnd = '';
    private const char SectionClose = '\uE006';

    /// <summary>Ends the current section: what follows (a story, counters) is shown without a box.</summary>
    public static string Close() => SectionClose.ToString();

    /// <summary>A section marker: a framed box titled <paramref name="title"/> with an icon (IconLibrary spec).</summary>
    public static string Section(string title, string icon) => $"{SectionStart}{icon ?? ""}{SectionMid}{title}{SectionEnd}";

    /// <summary>A tile marker: a square with the icon (an IconLibrary spec, "" for "?") and text next to it.</summary>
    public static string Tile(string icon, string text) => $"{TileStart}{icon ?? ""}{TileMid}{text}{TileEnd}";

    public enum Kind { Text, Tile }

    public sealed class Item
    {
        public Kind Kind;
        public string Text = "";
        public string Icon = "";
    }

    public sealed class Block
    {
        /// <summary>Null for the text before the first section (shown without a box).</summary>
        public string Title;
        public string Icon = "";
        public readonly List<Item> Items = new();
    }

    public static List<Block> Parse(string text)
    {
        var blocks = new List<Block>();
        var current = new Block();
        blocks.Add(current);
        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            string t = buffer.ToString().Trim('\n', ' ');
            buffer.Clear();
            if (t.Length > 0)
                current.Items.Add(new Item { Kind = Kind.Text, Text = t });
        }

        int i = 0;
        text ??= "";
        while (i < text.Length)
        {
            char c = text[i];
            if (c == SectionStart && TryRead(text, i, SectionMid, SectionEnd, out string icon, out string title, out int next))
            {
                Flush();
                current = new Block { Title = title, Icon = icon };
                blocks.Add(current);
                i = next;
            }
            else if (c == SectionClose)
            {
                Flush();
                current = new Block();
                blocks.Add(current);
                i++;
            }
            else if (c == TileStart && TryRead(text, i, TileMid, TileEnd, out string tileIcon, out string tileText, out int after))
            {
                Flush();
                current.Items.Add(new Item { Kind = Kind.Tile, Icon = tileIcon, Text = tileText.Trim('\n', ' ') });
                i = after;
            }
            else
            {
                buffer.Append(c);
                i++;
            }
        }
        Flush();
        blocks.RemoveAll(b => b.Title == null && b.Items.Count == 0);
        return blocks;
    }

    private static bool TryRead(string text, int start, char mid, char end, out string first, out string second, out int next)
    {
        first = second = "";
        next = start + 1;
        int m = text.IndexOf(mid, start + 1);
        int e = m < 0 ? -1 : text.IndexOf(end, m + 1);
        if (m < 0 || e < 0)
            return false;
        first = text.Substring(start + 1, m - start - 1);
        second = text.Substring(m + 1, e - m - 1);
        next = e + 1;
        return true;
    }

    /// <summary>The page as plain text without markers (searches, tests, the old single-text view).</summary>
    public static string Flatten(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in Parse(text))
        {
            if (block.Title != null)
                sb.Append('\n').Append(block.Title).Append('\n');
            foreach (var item in block.Items)
                sb.Append(item.Text).Append('\n');
        }
        return sb.ToString();
    }
}
