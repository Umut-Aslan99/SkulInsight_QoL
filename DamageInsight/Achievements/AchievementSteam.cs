using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

namespace DamageInsight.Achievements;

/// <summary>
/// What Steam knows about the achievements (Steamworks.NET in Platforms.Steam.dll, already started by the game): your
/// account's state and unlock time, the share of all players and the icons. Icons are cached on disk once loaded
/// (BepInEx/DamageInsight/AchievementIcons, the same for every slot) and come in colour and grey. Steam missing or
/// not answering only leaves those parts out.
/// </summary>
public static class AchievementSteam
{
    private static string IconFolder => Path.Combine(BepInEx.Paths.BepInExRootPath, "DamageInsight", "AchievementIcons");

    private static bool _broken;
    private static bool _percentsRequested;
    private static readonly Dictionary<string, (Sprite colour, Sprite grey)> Icons = new();
    private static readonly HashSet<string> Waiting = new();
    private static readonly Dictionary<string, int> Tries = new();
    private const int MaxTries = 40; // about 20 s of asking: then Steam has no icon for it
    private static float _nextIconTry;

    private static bool Ready
    {
        get
        {
            if (_broken)
                return false;
            try
            {
                return SteamAPI.IsSteamRunning();
            }
            catch (Exception e)
            {
                // Steamworks missing (another platform's build): never asked again.
                _broken = true;
                Plugin.Log.LogInfo($"Achievements: Steam is not there ({e.GetType().Name}); Steam's state, percentages and icons are left out");
                return false;
            }
        }
    }

    /// <summary>Your Steam account's state of an achievement; false when Steam can't say (not a Steam achievement).</summary>
    public static bool TryState(string id, out bool achieved, out DateTime? atUtc)
    {
        achieved = false;
        atUtc = null;
        if (!Ready)
            return false;
        try
        {
            if (!SteamUserStats.GetAchievementAndUnlockTime(id, out achieved, out uint unlock))
                return false;
            if (achieved && unlock > 0)
                atUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unlock);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The share of all Steam players who have it (%), once Steam answered the request; else null.</summary>
    public static double? Percent(string id)
    {
        if (!Ready)
            return null;
        try
        {
            if (!_percentsRequested)
            {
                _percentsRequested = true;
                SteamUserStats.RequestGlobalAchievementPercentages();
            }
            return SteamUserStats.GetAchievementAchievedPercent(id, out float percent) ? percent : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The icon in colour (earned) or grey (not yet). From the disk cache, else from Steam (which may need a moment:
    /// null until then; ask again on the next drawing).
    /// </summary>
    public static Sprite Icon(string id, bool grey)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (Icons.TryGetValue(id, out var both))
            return grey ? both.grey : both.colour;
        var texture = FromDisk(id) ?? FromSteam(id);
        if (texture == null)
            return null;
        var pair = (MakeSprite(texture), MakeSprite(Greyed(texture)));
        Icons[id] = pair;
        return grey ? pair.Item2 : pair.Item1;
    }

    /// <summary>Icons asked for but not loaded yet (the page draws again while there are some).</summary>
    public static bool AnyWaiting => Waiting.Count > 0;

    private static Texture2D FromDisk(string id)
    {
        try
        {
            string path = Path.Combine(IconFolder, id + ".png");
            if (!File.Exists(path))
                return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Texture2D FromSteam(string id)
    {
        if (!Ready || Time.unscaledTime < _nextIconTry && Waiting.Contains(id) || Tries.TryGetValue(id, out int asked) && asked > MaxTries)
            return null;
        try
        {
            // 0 = not loaded yet: Steam fetches it now and has it a moment later.
            int handle = SteamUserStats.GetAchievementIcon(id);
            if (handle == 0 || !SteamUtils.GetImageSize(handle, out uint width, out uint height) || width == 0 || height == 0)
            {
                int tries = Tries.TryGetValue(id, out int t) ? t + 1 : 1;
                Tries[id] = tries;
                if (tries > MaxTries)
                {
                    Waiting.Remove(id);
                    return null;
                }
                Waiting.Add(id);
                _nextIconTry = Time.unscaledTime + 0.5f;
                return null;
            }
            var rgba = new byte[width * height * 4];
            if (!SteamUtils.GetImageRGBA(handle, rgba, rgba.Length))
                return null;
            // Steam's rows go top to bottom, a texture's bottom to top.
            var flipped = new byte[rgba.Length];
            int row = (int)width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(rgba, y * row, flipped, ((int)height - 1 - y) * row, row);
            var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(flipped);
            texture.Apply();
            Waiting.Remove(id);
            try
            {
                Directory.CreateDirectory(IconFolder);
                File.WriteAllBytes(Path.Combine(IconFolder, id + ".png"), texture.EncodeToPNG());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Achievements: icon {id} not cached: {e.Message}");
            }
            return texture;
        }
        catch (Exception)
        {
            Waiting.Remove(id);
            return null;
        }
    }

    /// <summary>A sepia-grey copy, a little faded (not earned yet).</summary>
    private static Texture2D Greyed(Texture2D source)
    {
        var pixels = source.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            float grey = (p.r * 0.3f + p.g * 0.59f + p.b * 0.11f) / 255f * 0.75f + 0.08f;
            pixels[i] = new Color(grey * 1.06f, grey, grey * 0.9f, p.a / 255f);
        }
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private static Sprite MakeSprite(Texture2D texture)
    {
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
