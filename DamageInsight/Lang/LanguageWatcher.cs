using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DamageInsight.Lang;

/// <summary>
/// Keeps <see cref="Loc"/> on the language the player wants: the game's own language setting (the game raises no
/// event when it changes, so we look twice a second), or the one picked in our settings.
/// </summary>
public static class LanguageWatcher
{
    public const string Auto = "auto";

    private static float _nextCheck;

    public static void Tick()
    {
        if (Time.unscaledTime < _nextCheck && Loc.Version > 0)
            return;
        _nextCheck = Time.unscaledTime + 0.5f;
        string wanted = Wanted();
        if (wanted == Loc.Current && Loc.Version > 0)
            return;
        Loc.Use(wanted);
        FontFallback.For(Loc.Current);
        Plugin.Log.LogInfo($"Language: {Loc.Current}" + (Plugin.Language.Value == Auto ? " (the game's language)" : " (chosen in the mod's settings)"));
    }

    /// <summary>The language code the player wants now.</summary>
    public static string Wanted()
    {
        string chosen = Plugin.Language?.Value ?? Auto;
        if (chosen != Auto && Loc.Languages.Any(l => l.code == chosen))
            return chosen;
        try
        {
            int index = Data.GameData.Settings.language;
            return index >= 0 && index < Loc.GameOrder.Length ? Loc.GameOrder[index] : Loc.English;
        }
        catch (Exception)
        {
            return Loc.English; // the game's data isn't loaded yet
        }
    }
}

/// <summary>
/// The game's fonts are fixed atlases holding only the characters the game's own texts use (Korean: 1225
/// syllables). For Korean, Japanese and Chinese we add a font from Windows that draws any missing character, as a
/// last fallback behind the game's fonts, so the game's own look stays wherever it has the character.
/// </summary>
public static class FontFallback
{
    private static readonly Dictionary<string, string[]> Candidates = new()
    {
        ["ko"] = new[] { "malgunbd.ttf", "malgun.ttf", "gulim.ttc", "NotoSansCJK-Bold.ttc", "NotoSansCJK-Regular.ttc", "NotoSansKR" },
        ["ja"] = new[] { "YuGothB.ttc", "meiryob.ttc", "meiryo.ttc", "YuGothM.ttc", "msgothic.ttc", "NotoSansCJK-Bold.ttc", "NotoSansCJK-Regular.ttc", "NotoSansJP" },
        ["zh-Hans"] = new[] { "msyhbd.ttc", "msyh.ttc", "simhei.ttf", "simsun.ttc", "NotoSansCJK-Bold.ttc", "NotoSansCJK-Regular.ttc", "NotoSansSC" },
        ["zh-Hant"] = new[] { "msjhbd.ttc", "msjh.ttc", "mingliu.ttc", "NotoSansCJK-Bold.ttc", "NotoSansCJK-Regular.ttc", "NotoSansTC" },
    };

    private static readonly Dictionary<string, TMP_FontAsset> Made = new();

    /// <summary>Makes sure the fallback for <paramref name="code"/> is in place (nothing for other languages).</summary>
    public static void For(string code)
    {
        if (!Candidates.TryGetValue(code, out var names) || Made.ContainsKey(code))
            return;
        Made[code] = null; // try once per language
        try
        {
            string path = FindFont(names);
            if (path == null)
            {
                Plugin.Log.LogWarning($"Fonts: no system font for {code} found; characters the game's fonts lack show as boxes.");
                return;
            }
            var font = new Font(path);
            var asset = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (asset == null)
            {
                Plugin.Log.LogWarning($"Fonts: {path} could not be used for {code}.");
                return;
            }
            asset.name = "SkulInsight fallback " + code;
            UnityEngine.Object.DontDestroyOnLoad(font);
            UnityEngine.Object.DontDestroyOnLoad(asset);
            Made[code] = asset;
            AddEverywhere(asset);
            Plugin.Log.LogInfo($"Fonts: {Path.GetFileName(path)} draws the characters the game's fonts lack ({code}).");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Fonts: the fallback for {code} failed ({e.Message}); characters the game's fonts lack show as boxes.");
        }
    }

    private static string FindFont(string[] names)
    {
        string[] installed;
        try
        {
            installed = Font.GetPathsToOSFonts() ?? new string[0];
        }
        catch (Exception)
        {
            installed = new string[0];
        }
        string windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        foreach (var name in names)
        {
            var hit = installed.FirstOrDefault(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase))
                      ?? installed.FirstOrDefault(p => Path.GetFileName(p).StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
                return hit;
            string direct = Path.Combine(windows, name);
            if (File.Exists(direct))
                return direct;
        }
        return null;
    }

    /// <summary>Behind every TMP font: the global fallback list, and each loaded font's own list as well.</summary>
    private static void AddEverywhere(TMP_FontAsset asset)
    {
        try
        {
            var global = TMP_Settings.fallbackFontAssets;
            if (global != null && !global.Contains(asset))
                global.Add(asset);
        }
        catch (Exception)
        {
            // no TMP settings asset in this build: the per-font lists below do the job
        }
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == asset || font.name.StartsWith("SkulInsight fallback"))
                continue;
            font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(asset))
                font.fallbackFontAssetTable.Add(asset);
        }
    }
}
