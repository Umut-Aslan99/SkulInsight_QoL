using System;
using System.Collections.Generic;
using System.Linq;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// Spine bosses (e.g. Yggdrasil: a front and a behind skeleton). On a kill only the skeleton data, skin, position
/// and draw order are noted. For the pictures, private copies of the skeletons (new Skeleton(data)) are posed at
/// each moment of an animation and their attachments (region and mesh pieces) turned into textured triangles in
/// draw order, with slot colours and blending. The boss on screen is never touched; drawing happens elsewhere.
/// </summary>
public static class SpineCapture
{
    /// <summary>One Spine skeleton of an enemy.</summary>
    public sealed class Part
    {
        public SkeletonData Data;
        public string Skin;
        public Matrix4x4 ToRoot;   // skeleton space → enemy root space
        public int Order;          // draw order between the enemy's skeletons (behind first)
        public string Name = "";
    }

    public const float Fps = 12f;
    public const int MaxFramesPerAnimation = 48, TargetPixels = 320;

    /// <summary>The enemy's Spine skeletons (effects excluded). Cheap: only references and transforms.</summary>
    public static List<Part> PartsOf(Characters.Character enemy, Func<Transform, bool> isEffect)
    {
        var parts = new List<Part>();
        var root = enemy.transform;
        foreach (var renderer in enemy.GetComponentsInChildren<SkeletonRenderer>(true))
        {
            if (renderer.skeletonDataAsset == null || isEffect(renderer.transform))
                continue;
            var data = renderer.skeletonDataAsset.GetSkeletonData(true);
            if (data == null)
                continue;
            var mesh = renderer.GetComponent<MeshRenderer>();
            parts.Add(new Part
            {
                Data = data,
                Skin = renderer.skeleton?.Skin?.Name ?? renderer.initialSkinName,
                ToRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                Order = mesh != null ? SortingLayer.GetLayerValueFromID(mesh.sortingLayerID) * 10000 + mesh.sortingOrder : 0,
                Name = renderer.name,
            });
        }
        return parts.OrderBy(p => p.Order).ToList();
    }

    /// <summary>Animation names of all skeletons, in the first skeleton's order.</summary>
    public static List<string> AnimationNames(List<Part> parts)
    {
        var names = new List<string>();
        foreach (var part in parts)
            foreach (var animation in part.Data.Animations)
                if (!names.Contains(animation.Name))
                    names.Add(animation.Name);
        return names;
    }

    public static float Duration(List<Part> parts, string animation) =>
        parts.Select(p => p.Data.FindAnimation(animation)?.Duration ?? 0f).DefaultIfEmpty(0f).Max();

    public static List<(Part part, Skeleton skeleton)> CreateSkeletons(List<Part> parts)
    {
        var list = new List<(Part, Skeleton)>();
        foreach (var part in parts)
        {
            var skeleton = new Skeleton(part.Data);
            if (!string.IsNullOrEmpty(part.Skin) && part.Data.FindSkin(part.Skin) != null)
                skeleton.SetSkin(part.Skin);
            skeleton.SetSlotsToSetupPose();
            skeleton.UpdateWorldTransform();
            list.Add((part, skeleton));
        }
        return list;
    }

    /// <summary>A textured triangle in the enemy's root space, before it is registered with a pixel region.</summary>
    public struct RawTri
    {
        public Vector2 A, B, C, UA, UB, UC;
        public Texture2D Texture;
        public Color Tint;
        public bool Additive, Premultiplied;
        public int Attachment;   // same value for all triangles of one attachment (they share a texture area)
    }

    /// <summary>Poses every skeleton at <paramref name="time"/> of the animation (setup pose if it has none).</summary>
    public static List<RawTri> Pose(List<(Part part, Skeleton skeleton)> skeletons, string animationName, float time)
    {
        var tris = new List<RawTri>();
        int attachmentId = 0;
        foreach (var (part, skeleton) in skeletons)
        {
            skeleton.SetToSetupPose();
            var animation = animationName != null ? part.Data.FindAnimation(animationName) : null;
            if (animation != null)
                animation.Apply(skeleton, 0f, Mathf.Min(time, animation.Duration), false, null, 1f, MixBlend.Setup, MixDirection.In);
            skeleton.UpdateWorldTransform();

            foreach (var slot in skeleton.DrawOrder)
            {
                if (slot?.Attachment == null || slot.A <= 0f)
                    continue;
                float[] world;
                float[] uv;
                int[] triangles;
                object rendererObject;
                Color tint;
                switch (slot.Attachment)
                {
                    case RegionAttachment region:
                        world = new float[8];
                        region.ComputeWorldVertices(slot.Bone, world, 0, 2);
                        uv = region.UVs;
                        triangles = new[] { 0, 1, 2, 2, 3, 0 };
                        rendererObject = region.RendererObject;
                        tint = new Color(slot.R * region.R, slot.G * region.G, slot.B * region.B, slot.A * region.A * skeleton.A);
                        break;
                    case MeshAttachment mesh:
                        world = new float[mesh.WorldVerticesLength];
                        mesh.ComputeWorldVertices(slot, 0, mesh.WorldVerticesLength, world, 0, 2);
                        uv = mesh.UVs;
                        triangles = mesh.Triangles;
                        rendererObject = mesh.RendererObject;
                        tint = new Color(slot.R * mesh.R, slot.G * mesh.G, slot.B * mesh.B, slot.A * mesh.A * skeleton.A);
                        break;
                    default:
                        continue; // bounding boxes, clipping, points: not drawn
                }
                if (!(rendererObject is AtlasRegion atlasRegion) || !(atlasRegion.page?.rendererObject is Material material)
                    || !(material.mainTexture is Texture2D texture) || uv == null)
                    continue;
                bool straight = material.HasProperty("_StraightAlphaInput") && material.GetFloat("_StraightAlphaInput") > 0.5f;
                // spine-unity keeps Spine's top-down v in some versions: detect it from the region's pixel position.
                bool flipV = atlasRegion.page.height > 0 && Mathf.Abs(atlasRegion.v - atlasRegion.y / (float)atlasRegion.page.height) < 1e-3f;
                bool additive = slot.Data.BlendMode == BlendMode.Additive;
                attachmentId++;

                Vector2 P(int i) => part.ToRoot.MultiplyPoint3x4(new Vector3(world[i * 2], world[i * 2 + 1]));
                Vector2 U(int i) => new(uv[i * 2], flipV ? 1f - uv[i * 2 + 1] : uv[i * 2 + 1]);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    tris.Add(new RawTri
                    {
                        A = P(a), B = P(b), C = P(c), UA = U(a), UB = U(b), UC = U(c),
                        Texture = texture, Tint = tint, Additive = additive, Premultiplied = !straight, Attachment = attachmentId,
                    });
                }
            }
        }
        return tris;
    }

    /// <summary>Pixels per unit so the figure's setup pose is about TargetPixels big.</summary>
    public static float ScaleFor(List<RawTri> setup)
    {
        if (setup.Count == 0)
            return 100f;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var t in setup)
            foreach (var p in new[] { t.A, t.B, t.C })
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
        float size = Mathf.Max(maxX - minX, maxY - minY);
        return size > 0 ? TargetPixels / size : 100f;
    }
}
