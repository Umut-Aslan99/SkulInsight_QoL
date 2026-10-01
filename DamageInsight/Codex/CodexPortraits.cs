using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// Copies a sprite out of its (usually unreadable, packed) texture: the texture is drawn into a RenderTexture on
/// the GPU and the sprite's rectangle is read back from there. Same technique as the icon dump.
/// </summary>
public static class SpriteCopy
{
    private static Material _blitMaterial;

    /// <summary>
    /// Graphics.Blit's default material uses a built-in shader that Skul's render pipeline (URP) doesn't ship, so it
    /// silently draws nothing. The UI's default material samples _MainTex too and always exists.
    /// </summary>
    private static Material BlitMaterial =>
        _blitMaterial != null ? _blitMaterial : _blitMaterial = new Material(Canvas.GetDefaultCanvasMaterial());

    /// <summary>
    /// The texture's pixels in a temporary RenderTexture (release it with RenderTexture.ReleaseTemporary).
    /// For 32-bit textures the raw data is copied on the GPU (CopyTexture), which needs no shader at all; drawing
    /// with Blit is only the fallback, since Skul's pipeline lacks the default blit shader.
    /// </summary>
    public static RenderTexture ToRenderTexture(Texture2D texture)
    {
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, new Color(0, 0, 0, 0));
        RenderTexture.active = previous;
        bool raw32 = texture.format is TextureFormat.RGBA32 or TextureFormat.ARGB32 or TextureFormat.BGRA32;
        if (raw32 && SystemInfo.copyTextureSupport != UnityEngine.Rendering.CopyTextureSupport.None)
        {
            try
            {
                Graphics.CopyTexture(texture, 0, 0, 0, 0, texture.width, texture.height, rt, 0, 0, 0, 0);
                return rt;
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo($"Codex: CopyTexture failed ({texture.format}), drawing instead: {e.Message}");
            }
        }
        Graphics.Blit(texture, rt, BlitMaterial);
        return rt;
    }

    /// <summary>All pixels of a (usually unreadable) texture, read back from the GPU once (bottom row first).</summary>
    public static Color32[] AtlasPixels(Texture2D texture)
    {
        var rt = ToRenderTexture(texture);
        var previous = RenderTexture.active;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            return copy.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(copy);
        }
    }

    /// <summary>
    /// Rebuilds a sprite at its original size (sprite.rect) from its mesh: every triangle maps a piece of the
    /// original picture (vertices, in units around the pivot) to its place in the atlas (uv). Needed because tightly
    /// packed sprites have no rectangle in the atlas.
    /// </summary>
    public static Color32[] Rasterize(Sprite sprite, Color32[] atlas, int atlasW, int atlasH, out int w, out int h) =>
        Rasterize(sprite, atlas, 0, 0, atlasW, atlasH, atlasW, atlasH, out w, out h);

    /// <summary>
    /// Like Rasterize, but reads only the area of the atlas the sprite uses (its uv bounds) from a RenderTexture
    /// that already holds the atlas: a few kilobytes instead of a whole 4096² atlas per frame.
    /// </summary>
    public static Color32[] RasterizeFrom(Sprite sprite, RenderTexture atlas, out int w, out int h)
    {
        w = h = 0;
        var uv = sprite.uv;
        if (uv == null || uv.Length == 0)
            return null;
        float minU = 1, minV = 1, maxU = 0, maxV = 0;
        foreach (var p in uv)
        {
            minU = Mathf.Min(minU, p.x); maxU = Mathf.Max(maxU, p.x);
            minV = Mathf.Min(minV, p.y); maxV = Mathf.Max(maxV, p.y);
        }
        int x0 = Mathf.Clamp(Mathf.FloorToInt(minU * atlas.width) - 1, 0, atlas.width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(minV * atlas.height) - 1, 0, atlas.height - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(maxU * atlas.width) + 1, x0 + 1, atlas.width);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(maxV * atlas.height) + 1, y0 + 1, atlas.height);
        int rw = x1 - x0, rh = y1 - y0;

        var previous = RenderTexture.active;
        var region = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = atlas;
            region.ReadPixels(new Rect(x0, y0, rw, rh), 0, 0);
            region.Apply();
            var pixels = region.GetPixels32();
            return Rasterize(sprite, pixels, x0, y0, rw, rh, atlas.width, atlas.height, out w, out h);
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(region);
        }
    }

    /// <summary>The rasterizer: <paramref name="region"/> holds the atlas pixels of (rx, ry, rw, rh).</summary>
    private static Color32[] Rasterize(Sprite sprite, Color32[] region, int rx, int ry, int rw, int rh, int atlasW, int atlasH, out int w, out int h)
    {
        var atlas = region;
        w = Mathf.RoundToInt(sprite.rect.width);
        h = Mathf.RoundToInt(sprite.rect.height);
        if (w <= 0 || h <= 0 || atlas == null)
            return null;
        var vertices = sprite.vertices;
        var uv = sprite.uv;
        var triangles = sprite.triangles;
        float ppu = sprite.pixelsPerUnit;
        var pivot = sprite.pivot;
        var result = new Color32[w * h];

        var p = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            p[i] = new Vector2(vertices[i].x * ppu + pivot.x, vertices[i].y * ppu + pivot.y);

        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
            Vector2 a = p[i0], b = p[i1], c = p[i2];
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(area) < 1e-6f)
                continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    // Barycentric weights of the pixel centre.
                    float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
                    float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f)
                        continue;
                    float u = uv[i0].x * w0 + uv[i1].x * w1 + uv[i2].x * w2;
                    float v = uv[i0].y * w0 + uv[i1].y * w1 + uv[i2].y * w2;
                    int sx = Mathf.Clamp(Mathf.FloorToInt(u * atlasW) - rx, 0, rw - 1);
                    int sy = Mathf.Clamp(Mathf.FloorToInt(v * atlasH) - ry, 0, rh - 1);
                    result[y * w + x] = atlas[sy * rw + sx];
                }
            }
        }
        return result;
    }

    /// <summary>A readable copy of a (usually unreadable) texture, made on the GPU. Destroy it after use.</summary>
    public static Texture2D Readable(Texture2D texture)
    {
        if (texture.isReadable)
        {
            var direct = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            direct.SetPixels32(texture.GetPixels32());
            direct.Apply();
            return direct;
        }
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Graphics.Blit(texture, rt, BlitMaterial);
            RenderTexture.active = rt;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            return copy;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>A rectangle of pixels from a readable texture (bottom row first).</summary>
    public static Color32[] Region(Color32[] all, int width, int x, int y, int w, int h)
    {
        var region = new Color32[w * h];
        for (int row = 0; row < h; row++)
            Array.Copy(all, (y + row) * width + x, region, row * w, w);
        return region;
    }

    /// <summary>The sprite's pixels (bottom row first), or null.</summary>
    public static Color32[] Pixels(Sprite sprite, out int w, out int h)
    {
        var texture = sprite.texture;
        Rect r = sprite.textureRect;
        w = Mathf.RoundToInt(r.width);
        h = Mathf.RoundToInt(r.height);
        if (texture == null || w <= 0 || h <= 0)
            return null;
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, rt, BlitMaterial);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            copy.Apply();
            var pixels = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    public static byte[] ToPng(Sprite sprite, bool flipX)
    {
        var texture = sprite.texture;
        Rect r = sprite.textureRect;
        int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
        if (texture == null || w <= 0 || h <= 0)
            return null;

        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, rt, BlitMaterial);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            if (flipX)
            {
                var pixels = copy.GetPixels32();
                for (int y = 0; y < h; y++)
                    Array.Reverse(pixels, y * w, w);
                copy.SetPixels32(pixels);
            }
            copy.Apply();
            byte[] png = copy.EncodeToPNG();
            UnityEngine.Object.Destroy(copy);
            return png;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}

/// <summary>Enemy portraits captured by the tracker, loaded from Codex/Portraits as sprites (cached).</summary>
public static class CodexPortraits
{
    private static readonly Dictionary<string, Sprite> Cache = new();

    /// <summary>A still picture: the first frame of the captured animations (idle), else an older single portrait.</summary>
    public static Sprite Get(string key)
    {
        var still = CodexAnimations.Portrait(key).still;
        if (still != null)
            return still;
        if (Cache.TryGetValue(key, out var sprite))
            return sprite;
        sprite = null;
        string path = CodexTracker.PortraitPath(key);
        if (File.Exists(path))
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                if (texture.LoadImage(File.ReadAllBytes(path)))
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Codex: portrait {key} could not be loaded: {e.Message}");
            }
        }
        Cache[key] = sprite;
        return sprite;
    }

    public static void Forget(string key) => Cache.Remove(key);
}
