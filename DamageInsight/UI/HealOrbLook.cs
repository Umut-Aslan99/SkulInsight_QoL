using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// A heal orb's look (0.14, Umut: "the purple heal balls the game drops are kind of hard to see. Many beginners miss
/// them ... change the color of it to a green or the sprite to an actual big + sign", then "rewrite the purple orbs to
/// become the existing small potion icon"): the orb's own sprites are hidden and a bigger picture stands in its place,
/// the game's potion bottle of the same size (PortablePotion / AdvancedPotion / KingPotion, found in the loaded game
/// data; our own drawn bottle when it isn't there) or a green "+", with a soft green glow and a slow bob. The orb itself
/// (pickup, healing) stays the game's. Setting Heal Orbs/Look: Game / Potion / Plus, followed live.
/// </summary>
public sealed class HealOrbLook : MonoBehaviour
{
    public enum Kind { Game, Potion, Plus }

    public static Kind Current =>
        Enum.TryParse(Plugin.HealOrbLookSetting?.Value, out Kind kind) ? kind : Kind.Potion;

    private static readonly string[] Bottles = { "PortablePotion", "AdvancedPotion", "KingPotion" };
    private static readonly Dictionary<string, Sprite> Found = new();
    private static float _nextSearch;
    private static Sprite _glowSprite;

    /// <summary>The picture's height in the world: about twice the game's own small bottle (11 pixels at 32 per unit).</summary>
    private static readonly float[] Heights = { 0.7f, 0.85f, 1.0f };

    private int _size;
    private Kind _kind = Kind.Game;
    private SpriteRenderer _shown;
    private bool _drawnBottle;
    private float _nextLook;
    private SpriteRenderer[] _originals = Array.Empty<SpriteRenderer>();
    private Transform _holder;
    private float _height, _phase;

    /// <param name="size">0 small, 1 medium, 2 large (Level.Potion.Size).</param>
    public static void Attach(Component potion, int size)
    {
        var look = potion.gameObject.AddComponent<HealOrbLook>();
        look._size = Mathf.Clamp(size, 0, 2);
        look._phase = UnityEngine.Random.value * 6f;
    }

    private void LateUpdate()
    {
        var want = Current;
        if (want != _kind)
            Patches.Guard.Run("Heal orb look", () => Apply(want));
        if (_kind == Kind.Game || _holder == null)
            return;
        // Our own drawn bottle stands in until the game's picture is loaded (it isn't in the castle): then it takes over.
        if (_drawnBottle && Time.unscaledTime >= _nextLook)
        {
            _nextLook = Time.unscaledTime + 3f;
            if (Bottle(_size) is { } game)
                Show(game);
        }
        foreach (var original in _originals)
            if (original != null && original.enabled)
                original.enabled = false;
        // A slow bob and a soft pulse of the glow: something you notice from the corner of your eye.
        float t = Time.time * 2.2f + _phase;
        _holder.localPosition = new Vector3(0f, _height * 0.08f * Mathf.Sin(t), 0f);
        float pulse = 1f + 0.06f * Mathf.Sin(t * 1.3f);
        _holder.localScale = new Vector3(pulse, pulse, 1f);
    }

    private void Apply(Kind kind)
    {
        if (_holder != null)
            Destroy(_holder.gameObject);
        _holder = null;
        foreach (var original in _originals)
            if (original != null)
                original.enabled = true;
        _kind = kind;
        if (kind == Kind.Game)
            return;

        // The orb's own renderers, collected once (before our picture exists: a destroyed one lingers to the frame's end).
        if (_originals.Length == 0)
            _originals = GetComponentsInChildren<SpriteRenderer>(true);
        var first = _originals.FirstOrDefault(r => r.sprite != null) ?? _originals.FirstOrDefault();
        // A fixed size (the orb's own sprites are much bigger than the orb looks: 1.5x of them made a giant bottle).
        _height = Heights[_size];

        _holder = new GameObject("SkulInsight heal look").transform;
        _holder.SetParent(transform, false);
        var game = kind == Kind.Plus ? null : Bottle(_size);
        _drawnBottle = kind == Kind.Potion && game == null;
        var sprite = kind == Kind.Plus ? PixelIcons.Get("heal") : game ?? PixelIcons.Get("bottle");
        int layer = first != null ? first.sortingLayerID : 0, order = first != null ? first.sortingOrder : 0;
        Glow(layer, order);
        _shown = new GameObject("Picture").AddComponent<SpriteRenderer>();
        _shown.transform.SetParent(_holder, false);
        _shown.sortingLayerID = layer;
        _shown.sortingOrder = order + 2;
        Show(sprite);
        foreach (var original in _originals)
            if (original != null)
                original.enabled = false;
    }

    private void Show(Sprite sprite)
    {
        if (_shown == null || sprite == null)
            return;
        _shown.sprite = sprite;
        _drawnBottle = _drawnBottle && sprite.name.StartsWith("pix:", StringComparison.Ordinal);
        float scale = _height / Mathf.Max(0.01f, sprite.bounds.size.y);
        _shown.transform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>A soft green halo behind the picture.</summary>
    private void Glow(int layer, int order)
    {
        if (_glowSprite == null)
        {
            const int n = 32;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * n + x] = new Color(0.45f, 1f, 0.55f, a * a * 0.55f);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _glowSprite = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
        var glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
        glow.transform.SetParent(_holder, false);
        glow.sprite = _glowSprite;
        glow.sortingLayerID = layer;
        glow.sortingOrder = order + 1;
        float scale = _height * 1.9f / _glowSprite.bounds.size.y;
        glow.transform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>The game's potion bottle of this size, looked up once among the loaded sprites (null if not loaded).</summary>
    private static Sprite Bottle(int size)
    {
        string name = Bottles[Mathf.Clamp(size, 0, 2)];
        if (Found.TryGetValue(name, out var sprite) && sprite != null)
            return sprite;
        if (Time.unscaledTime < _nextSearch)
            return null;
        _nextSearch = Time.unscaledTime + 20f; // not on every orb: the search goes through every loaded sprite
        foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            if (Array.IndexOf(Bottles, candidate.name) >= 0)
                Found[candidate.name] = candidate;
        if (Found.Count > 0)
            Plugin.Log.LogInfo($"Heal orbs: the game's potion pictures found ({string.Join(", ", Found.Keys)})");
        return Found.TryGetValue(name, out sprite) ? sprite : null;
    }
}
