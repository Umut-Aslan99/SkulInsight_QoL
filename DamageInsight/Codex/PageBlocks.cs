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
    private const char RowStart = '\uE007', RowField = '\uE008', RowEnd = '\uE009';

    /// <summary>Ends the current section: what follows (a story, counters) is shown without a box.</summary>
    public static string Close() => SectionClose.ToString();

    /// <summary>A section marker: a framed box titled <paramref name="title"/> with an icon (IconLibrary spec).</summary>
    public static string Section(string title, string icon) => $"{SectionStart}{icon ?? ""}{SectionMid}{title}{SectionEnd}";

    /// <summary>A tile marker: a square with the icon (an IconLibrary spec, "" for "?") and text next to it.</summary>
    public static string Tile(string icon, string text) => $"{TileStart}{icon ?? ""}{TileMid}{text}{TileEnd}";

    /// <summary>A row of icons (0.13 run pages): each an icon spec and the page id a click opens ("" for none).</summary>
    public static string Icons(IEnumerable<(string icon, string link)> icons)
    {
        var fields = new List<string>();
        foreach (var (icon, link) in icons)
        {
            fields.Add(icon ?? "");
            fields.Add(link ?? "");
        }
        return Row("icons", fields.ToArray());
    }

    /// <summary>A bar row: a label, its value text and how full the bar is (0..1).</summary>
    public static string Bar(string label, string value, double fraction) =>
        Row("bar", label, value, Clamp01(fraction).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A line with a small icon in front (the run timeline).</summary>
    public static string Line(string icon, string text) => Row("line", icon, text);

    private static string Row(string kind, params string[] fields) =>
        RowStart + kind + RowField + string.Join(RowField.ToString(), fields) + RowEnd;

    private static double Clamp01(double v) => double.IsNaN(v) ? 0 : v < 0 ? 0 : v > 1 ? 1 : v;

    public enum Kind { Text, Tile, Icons, Bar, Line }

    public sealed class Item
    {
        public Kind Kind;
        public string Text = "";
        public string Icon = "";
        /// <summary>Icons: the (icon, link) pairs.</summary>
        public List<(string icon, string link)> Icons = new();
        /// <summary>Bar: the value text and how full the bar is.</summary>
        public string Value = "";
        public double Fill;
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
            else if (c == RowStart && text.IndexOf(RowEnd, i + 1) is var rowEnd && rowEnd > 0)
            {
                Flush();
                var fields = text.Substring(i + 1, rowEnd - i - 1).Split(RowField);
                if (ReadRow(fields) is { } row)
                    current.Items.Add(row);
                i = rowEnd + 1;
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

    private static Item ReadRow(string[] f)
    {
        switch (f[0])
        {
            case "icons":
                var item = new Item { Kind = Kind.Icons };
                for (int k = 1; k + 1 < f.Length; k += 2)
                    item.Icons.Add((f[k], f[k + 1]));
                return item;
            case "bar" when f.Length >= 4:
                return new Item
                {
                    Kind = Kind.Bar, Text = f[1], Value = f[2],
                    Fill = double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? Clamp01(v) : 0,
                };
            case "line" when f.Length >= 3:
                return new Item { Kind = Kind.Line, Icon = f[1], Text = f[2] };
            default:
                return null;
        }
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
                sb.Append(item.Text).Append(item.Value.Length > 0 ? " " + item.Value : "").Append('\n');
        }
        return sb.ToString();
    }
}
