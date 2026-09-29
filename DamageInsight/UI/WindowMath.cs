using System;

namespace DamageInsight.UI;

/// <summary>A window rectangle by its edges, measured from the screen's top-left corner, y pointing down.</summary>
public struct Edges
{
    public float Left, Top, Right, Bottom;

    public Edges(float left, float top, float width, float height)
    {
        Left = left;
        Top = top;
        Right = left + width;
        Bottom = top + height;
    }

    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public override string ToString() => $"({Left:0},{Top:0} {Width:0}x{Height:0})";
}

/// <summary>
/// Moving and resizing a window inside the screen: minimum size, stays fully on screen,
/// and edges snap to the screen border when close. Plain math, so it can be unit tested.
/// </summary>
public static class WindowMath
{
    public const float SnapDistance = 24f;

    /// <summary>Moves the whole window by (dx, dy) (dy pointing down), keeping it on screen.</summary>
    public static Edges Move(Edges e, float dx, float dy, float screenWidth, float screenHeight)
    {
        float w = e.Width, h = e.Height;
        float left = Clamp(e.Left + dx, 0f, Math.Max(0f, screenWidth - w));
        float top = Clamp(e.Top + dy, 0f, Math.Max(0f, screenHeight - h));

        // Snap to the nearest screen border.
        if (left < SnapDistance) left = 0f;
        else if (screenWidth - (left + w) < SnapDistance) left = Math.Max(0f, screenWidth - w);
        if (top < SnapDistance) top = 0f;
        else if (screenHeight - (top + h) < SnapDistance) top = Math.Max(0f, screenHeight - h);

        return new Edges(left, top, w, h);
    }

    /// <summary>
    /// Drags one corner by (dx, dy). <paramref name="leftEdge"/>/<paramref name="topEdge"/> say which edges
    /// that corner moves. The opposite edges stay put.
    /// </summary>
    public static Edges Resize(Edges e, bool leftEdge, bool topEdge, float dx, float dy,
        float minWidth, float minHeight, float screenWidth, float screenHeight)
    {
        if (leftEdge)
            e.Left = SnapLow(Clamp(e.Left + dx, 0f, e.Right - minWidth));
        else
            e.Right = SnapHigh(Clamp(e.Right + dx, e.Left + minWidth, screenWidth), screenWidth);

        if (topEdge)
            e.Top = SnapLow(Clamp(e.Top + dy, 0f, e.Bottom - minHeight));
        else
            e.Bottom = SnapHigh(Clamp(e.Bottom + dy, e.Top + minHeight, screenHeight), screenHeight);

        return e;
    }

    /// <summary>Makes a saved window fit the current screen (e.g. after a resolution change).</summary>
    public static Edges Fit(Edges e, float minWidth, float minHeight, float screenWidth, float screenHeight)
    {
        float w = Clamp(e.Width, Math.Min(minWidth, screenWidth), screenWidth);
        float h = Clamp(e.Height, Math.Min(minHeight, screenHeight), screenHeight);
        return Move(new Edges(e.Left, e.Top, w, h), 0f, 0f, screenWidth, screenHeight);
    }

    private static float SnapLow(float value) => value < SnapDistance ? 0f : value;

    private static float SnapHigh(float value, float max) => max - value < SnapDistance ? max : value;

    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
}
