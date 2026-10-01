using System;
using System.IO;
using System.Linq;
using DamageInsight.Codex;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.Rendering;

namespace DamageInsight.Tools;

/// <summary>
/// Developer test (F8): reads the player's current sprite with four different methods, saves each result as PNG
/// to Codex/Probe/ and logs how many visible pixels each one got, plus everything about the sprite and texture.
/// Tells us which way of copying sprite pixels works in Skul.
/// </summary>
public static class CaptureProbe
{
    public static void Run()
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        if (player == null)
            return;
        var body = player.GetComponentsInChildren<Characters.CharacterAnimation>(true)
            .FirstOrDefault(a => a.spriteRenderer != null && a.spriteRenderer.sprite != null);
        var sprite = body?.spriteRenderer.sprite;
        if (sprite == null)
        {
            Plugin.Log.LogInfo("[Probe] no player sprite found");
            return;
        }
        var texture = sprite.texture;
        Rect r = sprite.textureRect;
        Plugin.Log.LogInfo($"[Probe] sprite '{sprite.name}' rect {sprite.rect} textureRect {r} packed {sprite.packed} " +
                           $"packingMode {(sprite.packed ? sprite.packingMode.ToString() : "-")} pivot {sprite.pivot}; " +
                           $"texture '{texture.name}' {texture.width}x{texture.height} {texture.format} readable {texture.isReadable} " +
                           $"mips {texture.mipmapCount} graphicsFormat {texture.graphicsFormat}; renderer material '{body.spriteRenderer.sharedMaterial?.name}' " +
                           $"shader '{body.spriteRenderer.sharedMaterial?.shader?.name}'; device {SystemInfo.graphicsDeviceType}");

        string folder = Path.Combine(CodexTracker.Folder, "Probe");
        Directory.CreateDirectory(folder);
        int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);

        Try("A readable GetPixels32", folder, w, h, () =>
            texture.isReadable ? texture.GetPixels(x, y, w, h) : null);

        Try("B CopyTexture to RT + ReadPixels", folder, w, h, () =>
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.CopyTexture(texture, 0, 0, 0, 0, texture.width, texture.height, rt, 0, 0, 0, 0);
                return ReadRect(rt, x, y, w, h);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        });

        Try("C Blit (default material) + ReadPixels", folder, w, h, () =>
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(texture, rt);
                return ReadRect(rt, x, y, w, h);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        });

        Try("E atlas read + triangle rasterize (the Codex capture path)", folder, Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height), () =>
        {
            var atlas = SpriteCopy.AtlasPixels(texture);
            Plugin.Log.LogInfo($"[Probe] atlas: {atlas.Count(c => c.a > 0)} of {atlas.Length} pixels visible; " +
                               $"mesh: {sprite.vertices.Length} vertices, {sprite.triangles.Length / 3} triangles, ppu {sprite.pixelsPerUnit}");
            var pixels = SpriteCopy.Rasterize(sprite, atlas, texture.width, texture.height, out _, out _);
            return pixels?.Select(c => (Color)c).ToArray();
        });

        Try("D CopyTexture region into a Texture2D + AsyncGPUReadback (sync wait)", folder, w, h, () =>
        {
            var request = AsyncGPUReadback.Request(texture, 0, x, w, y, h, 0, 1, TextureFormat.RGBA32);
            request.WaitForCompletion();
            if (request.hasError)
                throw new Exception("readback error");
            return request.GetData<Color32>().ToArray().Select(c => (Color)c).ToArray();
        });
    }

    private static Color[] ReadRect(RenderTexture rt, int x, int y, int w, int h)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(x, y, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        var pixels = tex.GetPixels();
        UnityEngine.Object.Destroy(tex);
        return pixels;
    }

    private static void Try(string name, string folder, int w, int h, Func<Color[]> read)
    {
        try
        {
            var pixels = read();
            if (pixels == null)
            {
                Plugin.Log.LogInfo($"[Probe] {name}: not possible");
                return;
            }
            int visible = pixels.Count(p => p.a > 0.01f);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(folder, name.Substring(0, 1) + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Plugin.Log.LogInfo($"[Probe] {name}: {visible} of {pixels.Length} pixels visible");
        }
        catch (Exception e)
        {
            Plugin.Log.LogInfo($"[Probe] {name}: failed: {e.GetType().Name} {e.Message}");
        }
    }
}
