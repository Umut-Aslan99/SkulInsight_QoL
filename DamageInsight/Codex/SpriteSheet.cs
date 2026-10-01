using System;
using System.Collections.Generic;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>A finished sheet: equal cells, one frame per cell, as PNG.</summary>
public sealed class SpriteSheet
{
    public byte[] Png;
    public int CellWidth, CellHeight, Columns, Count;
    public bool HasPixels;
}

/// <summary>
/// Collects rebuilt frames (SpriteCopy.Rasterize: Skul's sprites are packed tightly, so each frame is rebuilt from
/// its triangles) one by one, then packs them into a sheet: all pivots line up (the feet stay in place) and the
/// empty border around all frames is cut off. Adding frames one at a time lets the capture spread its work over
/// several game frames.
/// </summary>
public sealed class SheetBuilder
{
    private readonly List<(Color32[] pixels, int w, int h, Vector2 pivot)> _frames = new();

    public void Add(Color32[] pixels, int w, int h, Vector2 pivot) => _frames.Add((pixels, w, h, pivot));

    public SpriteSheet Finish(int maxSize)
    {
        if (_frames.Count == 0)
            return null;

        // A common frame around the pivot.
        float left = 0, right = 0, below = 0, above = 0;
        foreach (var f in _frames)
        {
            left = Mathf.Max(left, f.pivot.x);
            right = Mathf.Max(right, f.w - f.pivot.x);
            below = Mathf.Max(below, f.pivot.y);
            above = Mathf.Max(above, f.h - f.pivot.y);
        }
        int fullW = Mathf.CeilToInt(left + right), fullH = Mathf.CeilToInt(below + above);

        // The part of that frame any picture uses (so the empty border can be cut off for all frames alike).
        int minX = fullW, minY = fullH, maxX = -1, maxY = -1;
        foreach (var f in _frames)
        {
            int ox = Mathf.RoundToInt(left - f.pivot.x), oy = Mathf.RoundToInt(below - f.pivot.y);
            for (int y = 0; y < f.h; y++)
            {
                for (int x = 0; x < f.w; x++)
                {
                    if (f.pixels[y * f.w + x].a == 0)
                        continue;
                    int tx = x + ox, ty = y + oy;
                    if (tx < minX) minX = tx;
                    if (tx > maxX) maxX = tx;
                    if (ty < minY) minY = ty;
                    if (ty > maxY) maxY = ty;
                }
            }
        }
        if (maxX < 0)
            return new SpriteSheet { HasPixels = false };

        int cellW = maxX - minX + 1, cellH = maxY - minY + 1;
        int columns = Mathf.Max(1, Mathf.Min(_frames.Count, maxSize / cellW));
        int count = Mathf.Min(_frames.Count, columns * Mathf.Max(1, maxSize / cellH));
        int rows = (count + columns - 1) / columns;
        int sheetW = columns * cellW, sheetH = rows * cellH;
        var sheetPixels = new Color32[sheetW * sheetH];
        for (int i = 0; i < count; i++)
        {
            var f = _frames[i];
            int row = i / columns, col = i % columns;
            int baseX = col * cellW, baseY = sheetH - (row + 1) * cellH;
            int ox = Mathf.RoundToInt(left - f.pivot.x) - minX, oy = Mathf.RoundToInt(below - f.pivot.y) - minY;
            for (int y = 0; y < f.h; y++)
            {
                int ty = y + oy;
                if (ty < 0 || ty >= cellH)
                    continue;
                for (int x = 0; x < f.w; x++)
                {
                    int tx = x + ox;
                    var c = f.pixels[y * f.w + x];
                    if (c.a == 0 || tx < 0 || tx >= cellW)
                        continue;
                    sheetPixels[(baseY + ty) * sheetW + baseX + tx] = c;
                }
            }
        }
        var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
        sheet.SetPixels32(sheetPixels);
        sheet.Apply();
        var png = sheet.EncodeToPNG();
        UnityEngine.Object.Destroy(sheet);
        return new SpriteSheet { Png = png, CellWidth = cellW, CellHeight = cellH, Columns = columns, Count = count, HasPixels = true };
    }
}
