using System.Collections.Generic;
using System.Linq;
using DamageInsight.Codex;
using UnityEngine;
using Xunit;

namespace DamageInsight.Tests;

public class CaptureModelTests
{
    [Fact]
    public void Grouping_JoinsIntroMainOutroInOrder_AndNamesPhases()
    {
        // Yggdrasil's real animation names (from the log).
        var names = new[]
        {
            "Idle", "P2_Idle", "FistSlam", "FistSlam_Intro", "FistSlam_Outro", "Laser", "Laser_Intro",
            "P2_SweepingCombo_Intro", "P2_SweepingCombo_Left", "P2_SweepingCombo_Outro", "P2_SweepingCombo_Right",
            "P2_Awakening", "P2_Awakening_Former", "Sweeping_Intro", "Sweeping_Left",
        };
        var groups = AnimationGrouping.Group(names);
        Assert.Equal(new[] { "Idle", "Phase 2 · Idle", "Fist slam", "Laser", "Phase 2 · Sweeping combo", "Phase 2 · Awakening", "Sweeping" },
            groups.Select(g => g.label));
        Assert.Equal(new[] { "FistSlam_Intro", "FistSlam", "FistSlam_Outro" }, groups[2].parts);
        Assert.Equal(new[] { "P2_SweepingCombo_Intro", "P2_SweepingCombo_Left", "P2_SweepingCombo_Right", "P2_SweepingCombo_Outro" }, groups[4].parts);
        Assert.Equal(new[] { "P2_Awakening_Former", "P2_Awakening" }, groups[5].parts);
    }

    [Fact]
    public void Renderer_DrawsATexturedQuad_AndCropsTheEmptyBorder()
    {
        // A 2x2 red texture region drawn as a 10x6 pixel quad above the feet.
        var red = new Color32(255, 0, 0, 255);
        var region = new PixelRegion { X = 0, Y = 0, W = 2, H = 2, TextureW = 2, TextureH = 2, Pixels = Enumerable.Repeat(red, 4).ToArray() };
        CapTri Tri(Vector2 a, Vector2 b, Vector2 c) => new()
        {
            A = a, B = b, C = c, UA = new Vector2(0.25f, 0.25f), UB = new Vector2(0.75f, 0.25f), UC = new Vector2(0.75f, 0.75f),
            Region = 0, Tint = Color.white,
        };
        var clip = new CapClip();
        clip.Frames.Add(new List<CapTri>
        {
            Tri(new Vector2(-5, 0), new Vector2(5, 0), new Vector2(5, 6)),
            Tri(new Vector2(-5, 0), new Vector2(5, 6), new Vector2(-5, 6)),
        });
        clip.Durations.Add(0.1f);

        var sheet = CaptureRenderer.Render(clip, new[] { region });
        Assert.NotNull(sheet);
        Assert.Equal((10, 6), (sheet.CellWidth, sheet.CellHeight));
        Assert.All(sheet.Pixels, p => Assert.Equal(red, p));
    }

    [Fact]
    public void Renderer_ReturnsNullWhenNothingIsVisible()
    {
        var clip = new CapClip();
        clip.Frames.Add(new List<CapTri>());
        Assert.Null(CaptureRenderer.Render(clip, new PixelRegion[0]));
    }
}
