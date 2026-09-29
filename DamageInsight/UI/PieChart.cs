using System.Collections.Generic;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>Draws a donut-shaped pie chart into a texture. Slices start at the top and go clockwise.</summary>
public sealed class PieChart
{
    private readonly int Size;
    private const float InnerRadius = 0.56f; // hole in the middle, as a fraction of the outer radius

    public Texture2D Texture { get; }
    private readonly Color32[] _pixels;

    public PieChart(int size = 256)
    {
        Size = size;
        _pixels = new Color32[size * size];
        Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Draw(new List<(double, Color)>());
    }

    public void Draw(IReadOnlyList<(double value, Color color)> slices)
    {
        double total = 0;
        foreach (var s in slices)
            total += s.value;

        // Cumulative end angle (0..1 of a full turn) per slice.
        var ends = new float[slices.Count];
        double sum = 0;
        for (int i = 0; i < slices.Count; i++)
        {
            sum += slices[i].value;
            ends[i] = total > 0 ? (float)(sum / total) : 0f;
        }

        Color32 empty = new Color32(0x3A, 0x30, 0x44, 0xFF);
        float half = Size / 2f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                // Soft edges (1 pixel wide) for the outer and inner circle.
                float edge = 1f / half;
                float alpha = Mathf.Clamp01((1f - r) / edge) * Mathf.Clamp01((r - InnerRadius) / edge);
                if (alpha <= 0f)
                {
                    _pixels[y * Size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                Color color = empty;
                if (total > 0)
                {
                    // Angle from 12 o'clock, clockwise, as 0..1.
                    float turn = Mathf.Atan2(dx, dy) / (2f * Mathf.PI);
                    if (turn < 0f)
                        turn += 1f;
                    for (int i = 0; i < ends.Length; i++)
                    {
                        if (turn <= ends[i])
                        {
                            color = slices[i].color;
                            break;
                        }
                    }
                }
                color.a *= alpha;
                _pixels[y * Size + x] = color;
            }
        }
        Texture.SetPixels32(_pixels);
        Texture.Apply();
    }
}
