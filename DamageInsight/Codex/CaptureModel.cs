using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>A textured triangle of a frame, in pixel space around the figure's origin (feet / root).</summary>
public struct CapTri
{
    public Vector2 A, B, C;       // pixels, origin = feet
    public Vector2 UA, UB, UC;    // texture uv (0..1 over the whole texture)
    public int Region;            // index into the job's pixel regions
    public Color Tint;
    public bool Additive, Premultiplied;
}

/// <summary>A rectangle of texture pixels read back from the GPU (bottom row first).</summary>
public sealed class PixelRegion
{
    public int X, Y, W, H, TextureW, TextureH;
    public Color32[] Pixels;
}

/// <summary>One animation to draw: its frames (triangles) and how long each shows.</summary>
public sealed class CapClip
{
    public string Label = "";
    public readonly List<List<CapTri>> Frames = new();
    public readonly List<float> Durations = new();
}

/// <summary>A finished sheet: equal cells, one frame per cell, bottom row first (like a texture).</summary>
public sealed class SheetData
{
    public Color32[] Pixels;
    public int Width, Height, CellWidth, CellHeight, Columns, Count;
}

/// <summary>
/// Draws captured frames into sprite sheets. Pure C#: runs on a background thread, so the game never waits.
/// Blending is premultiplied (back to front, additive slots added), converted to straight alpha at the end.
/// </summary>
public static class CaptureRenderer
{
    public const int MaxCell = 1024, MaxSheet = 4096;

    public static SheetData Render(CapClip clip, IReadOnlyList<PixelRegion> regions)
    {
        if (clip.Frames.Count == 0)
            return null;
        // Common bounds of all frames (pixel space), so the frames stay aligned.
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var frame in clip.Frames)
            foreach (var t in frame)
                foreach (var p in new[] { t.A, t.B, t.C })
                {
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
        if (maxX < minX)
            return null;
        int fullW = Math.Min(MaxCell, (int)Math.Ceiling(maxX - minX) + 2);
        int fullH = Math.Min(MaxCell, (int)Math.Ceiling(maxY - minY) + 2);
        var origin = new Vector2(-minX + 1, -minY + 1);

        var cells = new List<Color32[]>();
        int cropX0 = fullW, cropY0 = fullH, cropX1 = -1, cropY1 = -1;
        foreach (var frame in clip.Frames)
        {
            var cell = DrawFrame(frame, regions, origin, fullW, fullH);
            for (int y = 0; y < fullH; y++)
                for (int x = 0; x < fullW; x++)
                    if (cell[y * fullW + x].a > 0)
                    {
                        if (x < cropX0) cropX0 = x;
                        if (x > cropX1) cropX1 = x;
                        if (y < cropY0) cropY0 = y;
                        if (y > cropY1) cropY1 = y;
                    }
            cells.Add(cell);
        }
        if (cropX1 < 0)
            return null; // nothing visible

        int cellW = cropX1 - cropX0 + 1, cellH = cropY1 - cropY0 + 1;
        int columns = Math.Max(1, Math.Min(cells.Count, MaxSheet / cellW));
        int count = Math.Min(cells.Count, columns * Math.Max(1, MaxSheet / cellH));
        int rows = (count + columns - 1) / columns;
        int sheetW = columns * cellW, sheetH = rows * cellH;
        var sheet = new Color32[sheetW * sheetH];
        for (int i = 0; i < count; i++)
        {
            int row = i / columns, col = i % columns;
            int baseX = col * cellW, baseY = sheetH - (row + 1) * cellH;
            for (int y = 0; y < cellH; y++)
                Array.Copy(cells[i], (y + cropY0) * fullW + cropX0, sheet, (baseY + y) * sheetW + baseX, cellW);
        }
        return new SheetData { Pixels = sheet, Width = sheetW, Height = sheetH, CellWidth = cellW, CellHeight = cellH, Columns = columns, Count = count };
    }

    private static Color32[] DrawFrame(List<CapTri> tris, IReadOnlyList<PixelRegion> regions, Vector2 origin, int w, int h)
    {
        var canvas = new Color[w * h]; // premultiplied
        foreach (var t in tris)
        {
            if (t.Region < 0 || t.Region >= regions.Count)
                continue;
            var region = regions[t.Region];
            if (region?.Pixels == null)
                continue;
            Vector2 a = t.A + origin, b = t.B + origin, c = t.C + origin;
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Math.Abs(area) < 1e-6f)
                continue;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x))));
            int x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y))));
            int y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.y, Math.Max(b.y, c.y))));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
                    float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f)
                        continue;
                    float u = t.UA.x * w0 + t.UB.x * w1 + t.UC.x * w2;
                    float v = t.UA.y * w0 + t.UB.y * w1 + t.UC.y * w2;
                    int sx = Clamp((int)Math.Floor(u * region.TextureW) - region.X, 0, region.W - 1);
                    int sy = Clamp((int)Math.Floor(v * region.TextureH) - region.Y, 0, region.H - 1);
                    Color tex = region.Pixels[sy * region.W + sx];
                    Color src = t.Premultiplied ? tex : new Color(tex.r * tex.a, tex.g * tex.a, tex.b * tex.a, tex.a);
                    src = new Color(src.r * t.Tint.r * t.Tint.a, src.g * t.Tint.g * t.Tint.a, src.b * t.Tint.b * t.Tint.a, src.a * t.Tint.a);
                    ref Color dst = ref canvas[y * w + x];
                    if (t.Additive)
                        dst = new Color(Math.Min(1f, dst.r + src.r), Math.Min(1f, dst.g + src.g), Math.Min(1f, dst.b + src.b), dst.a);
                    else if (src.a > 0f)
                        dst = src + dst * (1f - src.a);
                }
            }
        }
        var result = new Color32[w * h];
        for (int i = 0; i < canvas.Length; i++)
        {
            var c = canvas[i];
            if (c.a > 0.004f)
                result[i] = new Color32(ToByte(c.r / c.a), ToByte(c.g / c.a), ToByte(c.b / c.a), ToByte(c.a));
        }
        return result;
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    private static byte ToByte(float v) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);
}

/// <summary>
/// Groups a figure's animations into attacks: "FistSlam_Intro", "FistSlam", "FistSlam_Outro" become one "Fist slam"
/// in that order, "P2_SweepingCombo_Intro/_Left/_Right/_Outro" one "Phase 2 · Sweeping combo". Plain logic.
/// </summary>
public static class AnimationGrouping
{
    private static readonly string[] Order = { "former", "intro", "ready", "start", "", "loop", "left", "right", "attack", "end", "outro", "recovery" };

    private static readonly Regex Suffix = new(@"_(Former|Intro|Ready|Start|Loop|Left|Right|Attack|End|Outro|Recovery)$", RegexOptions.IgnoreCase);

    public static List<(string label, List<string> parts)> Group(IEnumerable<string> names)
    {
        var groups = new List<(string baseName, List<string> parts)>();
        foreach (var name in names)
        {
            string baseName = Suffix.Replace(name, "");
            int index = groups.FindIndex(g => g.baseName == baseName);
            if (index < 0)
                groups.Add((baseName, new List<string> { name }));
            else
                groups[index].parts.Add(name);
        }
        return groups
            .Select(g => (Label(g.baseName), g.parts.OrderBy(PartRank).ToList()))
            .ToList();
    }

    private static int PartRank(string name)
    {
        var m = Suffix.Match(name);
        string suffix = m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
        int rank = Array.IndexOf(Order, suffix);
        return rank < 0 ? Order.Length : rank;
    }

    /// <summary>"P2_BothFistPowerSlam" → "Phase 2 · Both fist power slam"; "Idle_CutScene" → "Idle cut scene".</summary>
    public static string Label(string baseName)
    {
        string phase = "";
        var m = Regex.Match(baseName, @"^P(\d)_");
        if (m.Success)
        {
            phase = $"Phase {m.Groups[1].Value} · ";
            baseName = baseName.Substring(m.Length);
        }
        string words = Regex.Replace(baseName.Replace('_', ' '), @"(?<=[a-z])(?=[A-Z])", " ").Trim();
        words = Regex.Replace(words, @"\s+", " ");
        if (words.Length == 0)
            return phase.TrimEnd(' ', '·');
        return phase + char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
    }
}
