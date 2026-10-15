using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Scenes;
using UI.GearPopup;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>
/// Details beside the box (0.12.2, Umut's report: "the skill details land right on top of the skull's box"). The game
/// draws the ground / shop popup's detail view (held Down: the skill box, two skill boxes, or an item's two
/// inscription boxes) at the popup's centre, on top of it (prefab: DetailContainer at the container's centre). Since
/// 0.12 that key also shows the mod's numbers in the popup (Describe/ExtraInfo), and the game's boxes covered them.
/// The boxes sit next to the popup instead (always; Umut: no setting needed): left and right of it when both
/// sides have room, else both on the roomier side; vertically on the popup's centre, kept on screen. No room: the
/// game's own place. Only the ground popup (the swap menu has its own full-screen layout).
/// </summary>
[HarmonyPatch(typeof(GearPopup), "ProcessDetailView")]
public static class DetailsBesidePatch
{
    /// <summary>Space between the popup and a box, and between two boxes (popup canvas units, ~3 px each at 1080p).</summary>
    private const float Gap = 4f;

    /// <summary>Each box's own local position (the prefab's), to put it back when the setting is off.</summary>
    private static readonly Dictionary<int, Vector3> Home = new();

    private static readonly Vector3[] Corners = new Vector3[4];

    private static void Postfix(GearPopup __instance) => Guard.Run("Details beside the box", () => Layout(__instance));

    private static void Layout(GearPopup popup)
    {
        var detail = popup._detailContainer;
        if (detail == null || !detail.activeSelf)
            return;
        var popupCanvas = Scene<GameBase>.instance?.uiManager?.gearPopupCanvas;
        if (popupCanvas == null || popupCanvas.gearPopup != popup || popupCanvas._canvas == null)
            return;

        var boxes = new List<RectTransform>();
        foreach (Transform child in detail.transform)
            if (child.gameObject.activeSelf && child is RectTransform rect)
            {
                if (!Home.ContainsKey(rect.GetInstanceID()))
                    Home[rect.GetInstanceID()] = rect.localPosition;
                boxes.Add(rect);
            }
        if (boxes.Count == 0)
            return;
        foreach (var box in boxes)
            box.localPosition = Home[box.GetInstanceID()]; // decide from the game's place every frame
        if (boxes.Count > 2)
            return;

        var window = (RectTransform)popup.transform;
        var (windowLeft, windowRight, windowBottom, windowTop) = Bounds(window);
        var (screenLeft, screenRight, screenBottom, screenTop) = Bounds(popupCanvas._canvas);
        float scale = window.lossyScale.x;
        float gap = Gap * scale;
        // The left box of a pair has its pivot on its right edge (Skill 1, Keyword 1), the right box on its left edge.
        var ordered = boxes.OrderByDescending(b => b.pivot.x).ToList();
        var widths = ordered.Select(b => Width(b)).ToList();

        var placement = BesideLayout.Place(windowLeft, windowRight, screenLeft, screenRight, gap, widths);
        if (placement == null)
            return; // no room on this screen: the game's own place

        float centreY = (windowBottom + windowTop) / 2f;
        for (int i = 0; i < ordered.Count; i++)
        {
            var box = ordered[i];
            float height = Height(box);
            float bottom = Mathf.Clamp(centreY - height / 2f, screenBottom + gap, Mathf.Max(screenBottom + gap, screenTop - gap - height));
            var position = box.position;
            position.x = placement[i] + box.pivot.x * widths[i];
            position.y = bottom + box.pivot.y * height;
            box.position = position;
        }
    }

    private static (float left, float right, float bottom, float top) Bounds(RectTransform rect)
    {
        rect.GetWorldCorners(Corners); // bottom-left, top-left, top-right, bottom-right
        return (Corners[0].x, Corners[2].x, Corners[0].y, Corners[2].y);
    }

    private static float Width(RectTransform rect)
    {
        var (left, right, _, _) = Bounds(rect);
        return right - left;
    }

    private static float Height(RectTransform rect)
    {
        var (_, _, bottom, top) = Bounds(rect);
        return top - bottom;
    }
}

/// <summary>Where the detail boxes go beside the popup (plain logic, tested).</summary>
public static class BesideLayout
{
    /// <summary>
    /// The left edge of each box (in order left to right), or null if they don't fit beside the popup.
    /// One box: on the right if it fits there, else on the left. Two: one on each side if both sides have room, else
    /// both on the side that fits them.
    /// </summary>
    public static float[] Place(float popupLeft, float popupRight, float screenLeft, float screenRight, float gap, IReadOnlyList<float> widths)
    {
        float roomLeft = popupLeft - screenLeft - 2 * gap, roomRight = screenRight - popupRight - 2 * gap;
        if (widths.Count == 1)
        {
            float w = widths[0];
            if (roomRight >= w)
                return new[] { popupRight + gap };
            if (roomLeft >= w)
                return new[] { popupLeft - gap - w };
            return null;
        }
        if (widths.Count == 2)
        {
            float a = widths[0], b = widths[1];
            if (roomLeft >= a && roomRight >= b)
                return new[] { popupLeft - gap - a, popupRight + gap };
            if (roomRight >= a + gap + b)
                return new[] { popupRight + gap, popupRight + gap + a + gap };
            if (roomLeft >= a + gap + b)
                return new[] { popupLeft - gap - b - gap - a, popupLeft - gap - b };
            return null;
        }
        return null;
    }
}
