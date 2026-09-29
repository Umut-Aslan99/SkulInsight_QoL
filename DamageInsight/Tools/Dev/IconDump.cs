using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using GameResources;
using UnityEngine;

namespace DamageInsight.Tools;

/// <summary>
/// Development tool: saves the game's icons as PNG files so we can browse them and pick icons.
/// Output: BepInEx\DamageInsight\IconDump\&lt;category&gt;\&lt;sprite name&gt;.png
/// </summary>
/// <remarks>
/// Game textures are usually not CPU-readable, so each texture is first copied to a
/// RenderTexture on the GPU and read back from there.
/// </remarks>
public static class IconDump
{
    public static string OutputFolder => Path.Combine(Paths.BepInExRootPath, "DamageInsight", "IconDump");

    public static IEnumerator Run(Action<int> onDone)
    {
        var sets = new List<(string category, IEnumerable<Sprite> sprites)>();

        var common = CommonResource.instance;
        if (common != null)
        {
            sets.Add(("inscriptions", common._keywordIcons));
            sets.Add(("inscriptions_full", common._keywordFullactiveIcons));
            sets.Add(("inscriptions_inactive", common._keywordDeactiveIcons));
            sets.Add(("inscriptions_super", common._keywordSuperIcons));
            sets.Add(("misc_named", new[]
            {
                common._flexibleSpineIcon, common._soulAccelerationIcon,
                common._reassembleIcon, common._curseOfLightIcon,
            }));
        }

        var gear = GearResource.instance;
        if (gear != null)
        {
            sets.Add(("skills", gear._skillIcons));
            sets.Add(("item_buffs", gear._itemBuffIcons));
            sets.Add(("quintessences_hud", gear._quintessenceHudIcons));
            sets.Add(("skulls_hud_main", gear._weaponHudMainIcons));
            sets.Add(("skulls_hud_sub", gear._weaponHudSubIcons));
            sets.Add(("gear_thumbnails", gear._gearThumbnails));
        }

        // Everything else that is currently loaded and icon-sized (small), e.g. HUD and UI icons.
        var known = new HashSet<Sprite>(sets.SelectMany(s => s.sprites ?? Enumerable.Empty<Sprite>()));
        var small = Resources.FindObjectsOfTypeAll<Sprite>()
            .Where(s => s != null && !known.Contains(s) && s.rect.width <= 48 && s.rect.height <= 48 && s.rect.width >= 6)
            .GroupBy(s => s.name).Select(g => g.First())
            .Take(4000);
        sets.Add(("other_small_sprites", small));

        int saved = 0;
        foreach (var (category, sprites) in sets)
        {
            if (sprites == null)
                continue;
            string folder = Path.Combine(OutputFolder, category);
            Directory.CreateDirectory(folder);

            // Group by texture so each atlas is copied to the GPU buffer only once.
            foreach (var byTexture in sprites.Where(s => s != null && s.texture != null).GroupBy(s => s.texture))
            {
                saved += SaveSprites(byTexture.Key, byTexture, folder);
                yield return null; // spread the work over frames to avoid a long freeze
            }
        }

        onDone(saved);
    }

    private static int SaveSprites(Texture2D texture, IEnumerable<Sprite> sprites, string folder)
    {
        int saved = 0;
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            foreach (var sprite in sprites)
            {
                try
                {
                    Rect r = sprite.textureRect;
                    int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                    if (w <= 0 || h <= 0)
                        continue;
                    var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                    copy.Apply();
                    File.WriteAllBytes(UniquePath(folder, sprite.name), copy.EncodeToPNG());
                    UnityEngine.Object.Destroy(copy);
                    saved++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Icon dump: could not save '{sprite.name}': {e.Message}");
                }
            }
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
        return saved;
    }

    private static string UniquePath(string folder, string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        string path = Path.Combine(folder, name + ".png");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name}_{i}.png");
        return path;
    }
}
