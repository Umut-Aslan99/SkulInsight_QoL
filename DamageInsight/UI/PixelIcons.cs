using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// The mod's own small icons (0.13 run history: a crown for a win, crossed bones for a death, a white flag for an
/// abandoned run, ...), drawn as pixel art in code so they match Skul's look and need no files. Each icon is an 11x11
/// pattern: one letter per pixel ('.' is see-through), the letters are colours of the palette below. Shown with
/// point filtering, so they stay sharp at any size. Spec for page icons: "pix:&lt;name&gt;".
/// </summary>
public static class PixelIcons
{
    private static readonly Dictionary<char, Color32> Palette = new()
    {
        ['y'] = new Color32(0xE8, 0xB8, 0x4A, 0xFF), // gold
        ['Y'] = new Color32(0xFF, 0xE8, 0x9A, 0xFF), // light gold
        ['o'] = new Color32(0xA8, 0x74, 0x24, 0xFF), // dark gold
        ['r'] = new Color32(0xB8, 0x3A, 0x2E, 0xFF), // red
        ['R'] = new Color32(0x7A, 0x1E, 0x18, 0xFF), // dark red
        ['w'] = new Color32(0xF2, 0xEC, 0xDC, 0xFF), // bone
        ['g'] = new Color32(0x9A, 0x8F, 0x80, 0xFF), // grey
        ['k'] = new Color32(0x3A, 0x2A, 0x1E, 0xFF), // ink
        ['b'] = new Color32(0x6B, 0x4A, 0x2E, 0xFF), // brown
        ['B'] = new Color32(0x9C, 0x6B, 0x3C, 0xFF), // wood
        ['c'] = new Color32(0x7F, 0xC8, 0xE0, 0xFF), // sand-glass blue
        ['e'] = new Color32(0x14, 0x5C, 0x2C, 0xFF), // dark green (the heal plus's outline)
        ['E'] = new Color32(0x3D, 0xDC, 0x6E, 0xFF), // green
        ['l'] = new Color32(0xB8, 0xFF, 0xC8, 0xFF), // light green
    };

    private static readonly Dictionary<string, string[]> Patterns = new()
    {
        ["crown"] = new[]
        {
            "...........",
            "y....Y....y",
            "yy..yyy..yy",
            "yyy.yyy.yyy",
            "yyyyyyyyyyy",
            "yyYyyRyyYyy",
            "yyyyyyyyyyy",
            "ooooooooooo",
            "...........",
            "...........",
            "...........",
        },
        ["bones"] = new[]
        {
            "ww.......ww",
            "wwg.....gww",
            ".wwg...gww.",
            "..wwg.gww..",
            "...wwgww...",
            "....www....",
            "...wwgww...",
            "..wwg.gww..",
            ".wwg...gww.",
            "wwg.....gww",
            "ww.......ww",
        },
        ["flag"] = new[]
        {
            "bwwwwww....",
            "bwwwwwwwww.",
            "bwwwwgwwww.",
            "bwwwwwwwww.",
            "bwwwwwwg...",
            "b..........",
            "b..........",
            "b..........",
            "b..........",
            "b..........",
            "bb.........",
        },
        ["hourglass"] = new[]
        {
            "bbbbbbbbbbb",
            ".b.......b.",
            ".bcccccccb.",
            "..bcccccb..",
            "...bcccb...",
            "....bcb....",
            "...b.c.b...",
            "..b..c..b..",
            ".b..ccc..b.",
            ".bcccccccb.",
            "bbbbbbbbbbb",
        },
        ["heal"] = new[]
        {
            "....eee....",
            "...elEEe...",
            "...elEEe...",
            ".eeelEEeee.",
            "ellllllEEEe",
            "eEEElEEEEEe",
            "eEEElEEEEEe",
            ".eeeEEEeee.",
            "...eEEEe...",
            "...eEEEe...",
            "....eee....",
        },
        ["bottle"] = new[]
        {
            "....bbb....",
            "....BBB....",
            "....www....",
            "...wrrrw...",
            "..wrrrrrw..",
            ".wrrYrrrrw.",
            ".wrYrrrrrw.",
            ".wrrrrrrrw.",
            ".wRrrrrrRw.",
            "..wRRRRRw..",
            "...wwwww...",
        },
        ["download"] = new[]
        {
            "....www....",
            "....www....",
            "....www....",
            "....www....",
            ".wwwwwwwww.",
            "..wwwwwww..",
            "...wwwww...",
            "....www....",
            ".....w.....",
            "w.........w",
            "wwwwwwwwwww",
        },
        ["chest"] = new[]
        {
            "...........",
            ".BBBBBBBBB.",
            "BbbbbbbbbbB",
            "BbbbbbbbbbB",
            "BBBBByBBBBB",
            "BbbbyYybbbB",
            "BbbbbybbbbB",
            "BbbbbbbbbbB",
            "BbbbbbbbbbB",
            "BBBBBBBBBBB",
            "...........",
        },
        ["trophy"] = new[]
        {
            "..yyyyyyy..",
            "yyyYyyyyoyy",
            "y.yYyyyyo.y",
            "y.yYyyyyo.y",
            ".yyYyyyyoy.",
            "...yYyyo...",
            "....yyo....",
            "....yyo....",
            "...yyyyo...",
            "..bbbbbbb..",
            "..BBBBBBB..",
        },
        ["star"] = new[]
        {
            ".....y.....",
            "....yyy....",
            "....yYy....",
            "yyyyyYyyyyy",
            ".yyyyYyyyy.",
            "..yyyyyyy..",
            "..yyyyyyy..",
            ".yyyy.yyyy.",
            ".yyy...yyy.",
            "yy.......yy",
            "...........",
        },
        ["heart"] = new[]
        {
            "...........",
            ".rr.....rr.",
            "rrYr...rrrr",
            "rYrrr.rrrrr",
            "rrrrrrrrrrr",
            "rrrrrrrrrrR",
            ".rrrrrrrrR.",
            "..rrrrrrR..",
            "...rrrrR...",
            "....rrR....",
            ".....R.....",
        },
        ["thumb"] = new[]
        {
            "....kk.....",
            "...kwk.....",
            "...kwk.....",
            "..kwwk.....",
            "kkkwwkkkkk.",
            "kbkwwwwwwwk",
            "kbkwwwwkkk.",
            "kbkwwwwwwwk",
            "kbkwwwwkkk.",
            "kbkwwwwwwk.",
            "kkkkkkkkk..",
        },
        ["door"] = new[]
        {
            "...bbbbb...",
            "..bBBBBBb..",
            ".bBBBkBBBb.",
            "bBBBBkBBBBb",
            "bBBBBkBBBBb",
            "bBBBBkBBYBb",
            "bBBBBkBBBBb",
            "bBBBBkBBBBb",
            "bBBBBkBBBBb",
            "bBBBBkBBBBb",
            "bbbbbbbbbbb",
        },
    };

    private static readonly Dictionary<string, Sprite> Cache = new();

    public static IReadOnlyCollection<string> Names => Patterns.Keys.Concat(new[] { "star_empty" }).ToList();

    /// <summary>The pixels of an icon, top row first (null for an unknown name). "star_empty" is the star's outline.</summary>
    public static Color32[,] Pixels(string name)
    {
        bool outline = name == "star_empty";
        if (!Patterns.TryGetValue(outline ? "star" : name, out var rows))
            return null;
        int h = rows.Length, w = rows[0].Length;
        var pixels = new Color32[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y, x] = Palette.TryGetValue(rows[y][x], out var c) ? c : new Color32(0, 0, 0, 0);
        if (!outline)
            return pixels;
        // The outline: filled pixels with an empty (or outside) neighbour, in ink; the inside see-through.
        var edge = new Color32[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (pixels[y, x].a == 0)
                    continue;
                bool border = Empty(x - 1, y) || Empty(x + 1, y) || Empty(x, y - 1) || Empty(x, y + 1);
                edge[y, x] = border ? Palette['o'] : new Color32(0, 0, 0, 0);
            }
        return edge;

        bool Empty(int x, int y) => x < 0 || y < 0 || x >= w || y >= h || pixels[y, x].a == 0;
    }

    /// <summary>The icon as a sprite (cached), or null for an unknown name.</summary>
    public static Sprite Get(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (Cache.TryGetValue(name, out var cached) && cached != null)
            return cached;
        var pixels = Pixels(name);
        if (pixels == null)
            return null;
        int h = pixels.GetLength(0), w = pixels.GetLength(1);
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var flat = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                flat[(h - 1 - y) * w + x] = pixels[y, x]; // Unity's rows start at the bottom
        texture.SetPixels32(flat);
        texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "pix:" + name;
        Cache[name] = sprite;
        return sprite;
    }
}
