using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// Makes a panel movable (drag its header) and resizable (drag a corner), kept on screen and snapping
/// to the screen border (see WindowMath). The panel is anchored to the canvas' top-left corner.
/// </summary>
public sealed class WindowFrame : MonoBehaviour
{
    public RectTransform Panel;
    public float MinWidth = 560f;
    public float MinHeight = 420f;

    /// <summary>Called whenever the size changed (to re-layout contents) and after a drag ended (to save).</summary>
    public event Action<Edges> Resized;
    public event Action<Edges> DragEnded;

    public Edges Current { get; private set; }

    public static WindowFrame Attach(RectTransform panel, Edges initial, float minWidth, float minHeight)
    {
        var frame = panel.gameObject.AddComponent<WindowFrame>();
        frame.Panel = panel;
        frame.MinWidth = minWidth;
        frame.MinHeight = minHeight;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);

        // Header strip for dragging: behind everything else, so chips in the header stay clickable.
        var header = UiKit.Image("DragHandle", panel, new Color(0, 0, 0, 0));
        var hr = header.rectTransform;
        hr.anchorMin = new Vector2(0f, 1f);
        hr.anchorMax = new Vector2(1f, 1f);
        hr.pivot = new Vector2(0.5f, 1f);
        hr.anchoredPosition = Vector2.zero;
        hr.sizeDelta = new Vector2(0f, 58f);
        hr.SetAsFirstSibling();
        header.gameObject.AddComponent<DragHandle>().Frame = frame;

        frame.Apply(WindowMath.Fit(initial, minWidth, minHeight, UiKit.CanvasSize.x, UiKit.CanvasSize.y));
        return frame;
    }

    /// <summary>Adds the four corner grips. Call after all content, so they sit on top.</summary>
    public void AddCornerGrips()
    {
        foreach (var (left, top) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            var grip = UiKit.Image($"Grip {(top ? "T" : "B")}{(left ? "L" : "R")}", Panel, new Color(0, 0, 0, 0));
            var gr = grip.rectTransform;
            gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(left ? 0f : 1f, top ? 1f : 0f);
            gr.anchoredPosition = Vector2.zero;
            gr.sizeDelta = new Vector2(26f, 26f);
            var handle = grip.gameObject.AddComponent<ResizeHandle>();
            handle.Frame = this;
            handle.LeftEdge = left;
            handle.TopEdge = top;

            // A small L-shaped mark so the corners are recognisable.
            Color mark = new Color(0.95f, 0.91f, 0.85f, 0.45f);
            var h = UiKit.Image("MarkH", gr, mark).rectTransform;
            var v = UiKit.Image("MarkV", gr, mark).rectTransform;
            foreach (var r in new[] { h, v })
            {
                r.anchorMin = r.anchorMax = r.pivot = gr.pivot;
                r.anchoredPosition = new Vector2(left ? 5f : -5f, top ? -5f : 5f);
                r.GetComponent<Image>().raycastTarget = false;
            }
            h.sizeDelta = new Vector2(12f, 2f);
            v.sizeDelta = new Vector2(2f, 12f);
        }
    }

    private void Update()
    {
        // Keep the window on screen if the resolution changes.
        var fitted = WindowMath.Fit(Current, MinWidth, MinHeight, UiKit.CanvasSize.x, UiKit.CanvasSize.y);
        if (!Same(fitted, Current))
            Apply(fitted);
    }

    internal void MoveBy(Vector2 screenDelta)
    {
        Vector2 d = screenDelta / UiKit.CanvasScale;
        Apply(WindowMath.Move(Current, d.x, -d.y, UiKit.CanvasSize.x, UiKit.CanvasSize.y));
    }

    internal void ResizeBy(bool leftEdge, bool topEdge, Vector2 screenDelta)
    {
        Vector2 d = screenDelta / UiKit.CanvasScale;
        Apply(WindowMath.Resize(Current, leftEdge, topEdge, d.x, -d.y, MinWidth, MinHeight,
            UiKit.CanvasSize.x, UiKit.CanvasSize.y));
    }

    internal void EndDrag() => DragEnded?.Invoke(Current);

    private void Apply(Edges e)
    {
        bool sizeChanged = Math.Abs(e.Width - Current.Width) > 0.01f || Math.Abs(e.Height - Current.Height) > 0.01f;
        Current = e;
        Panel.anchoredPosition = new Vector2(e.Left, -e.Top);
        Panel.sizeDelta = new Vector2(e.Width, e.Height);
        if (sizeChanged)
            Resized?.Invoke(e);
    }

    private static bool Same(Edges a, Edges b) =>
        Math.Abs(a.Left - b.Left) < 0.01f && Math.Abs(a.Top - b.Top) < 0.01f &&
        Math.Abs(a.Right - b.Right) < 0.01f && Math.Abs(a.Bottom - b.Bottom) < 0.01f;

    // "left,top,width,height" in reference pixels, for the config file.
    public static string Serialize(Edges e) =>
        string.Join(",", new[] { e.Left, e.Top, e.Width, e.Height }.Select(v => v.ToString("0", CultureInfo.InvariantCulture)));

    public static bool TryParse(string text, out Edges edges)
    {
        edges = default;
        var parts = (text ?? "").Split(',');
        if (parts.Length != 4)
            return false;
        var v = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                return false;
        edges = new Edges(v[0], v[1], v[2], v[3]);
        return v[2] > 0 && v[3] > 0;
    }
}

internal sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public WindowFrame Frame;
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) => Frame.MoveBy(eventData.delta);
    public void OnEndDrag(PointerEventData eventData) => Frame.EndDrag();
}

internal sealed class ResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public WindowFrame Frame;
    public bool LeftEdge, TopEdge;
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) => Frame.ResizeBy(LeftEdge, TopEdge, eventData.delta);
    public void OnEndDrag(PointerEventData eventData) => Frame.EndDrag();
}
