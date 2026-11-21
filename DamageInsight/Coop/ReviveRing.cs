using System;
using Characters;
using DamageInsight.Coop.Rules;
using DamageInsight.UI;
using GameResources;
using InControl;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.Coop;

/// <summary>
/// The ring over a downed player (Umut 2026-11-14: "ring over health that fills up (and has a nice animation, high
/// quality. not just a standard ring, but maybe make it pulse and give it a heartbeat sound"). Every game draws the
/// same ring over the same player:
/// - Down, a teammate may revive: an empty ring with a heart that beats slowly (here, come and help).
/// - A teammate holds Interact: the ring fills clockwise from red over amber to green, a bright tip runs ahead, the
///   heart and a glow throb with a heartbeat that gets faster towards the end, a wave runs out with every beat and
///   the heartbeat sound plays in step (Rules.ReviveBeat gives the times, ReviveSound the sound).
/// - The hold breaks: the ring runs empty, the heart is slow again.
/// - The revive lands: a white flash, the ring swells and fades, two waves, sparks, and the revive sound. Back by
///   themselves at the room's end: the same, smaller and quieter.
/// - Down until the room is cleared (no revive left): a grey heart that stands still.
/// Drawn on the teammates' tag canvas above the name tag; off screen it is pinned to the edge like the tag. Its
/// pictures are made in code (no files).
/// </summary>
internal sealed class ReviveRing
{
    private const float Size = 84f, Lift = TeammateTags.TagHeight + 12f + Size / 2f, EdgeGap = 50f;
    private const int Tex = 128, SparkCount = 12;
    private const float Half = Tex / 2f;
    // the ring's band on the picture, in pixels from the centre, and its middle in canvas units
    private const float BandInner = 45f, BandOuter = 60f, BandRadius = Size / 2f * ((BandInner + BandOuter) / 2f / Half);
    private const double AppearSeconds = 0.22, BurstSeconds = 0.65, RippleSeconds = 0.5;

    private static readonly Color HeartRed = new Color32(0xFF, 0x54, 0x64, 0xFF), Amber = new Color32(0xFF, 0xC8, 0x5A, 0xFF),
        LifeGreen = new Color32(0x78, 0xFF, 0xA0, 0xFF);
    private static readonly Color[] SparkColors = { Color.white, LifeGreen, new Color32(0xFF, 0xE8, 0x9A, 0xFF) };

    private enum Phase { Hidden, Shown, Burst }

    private sealed class Ring
    {
        public RectTransform Root, Body;
        public CanvasGroup Group, BodyGroup;
        public Image Glow, Track, Fill, Tip, Heart, Flash, Key;
        public readonly Image[] Ripples = new Image[2];
        public readonly double[] RippleAt = { double.NegativeInfinity, double.NegativeInfinity };
        public readonly float[] RippleStrength = new float[2];
        public readonly Image[] Sparks = new Image[SparkCount];
        public readonly ReviveRingModel Model = new();
        public Phase Phase;
        public double Since;
        public bool Strong, Active = true;
    }

    private static Sprite _band, _outline, _wave, _soft, _dot, _heartGrey;

    private readonly TeammateTags _tags;
    private readonly Ring[] _rings = new Ring[PartyState.MaxPlayers];

    public ReviveRing(TeammateTags tags) => _tags = tags;

    /// <summary>The downed player this game's player stands next to and could revive right now (-1: nobody): their
    /// ring shows the key to hold, as the game's own picture of that key or button.</summary>
    public int PromptSlot = -1;

    /// <summary>Someone's hold on this player is this far (the reviver's own game: every frame; a teammate's hold:
    /// per message).</summary>
    public void Hold(int slot, int by, int hold, float progress, float holdSeconds, double now)
    {
        if (PartyState.IsSlot(slot))
            Get(slot).Model.Report(by, hold, progress, holdSeconds, now);
    }

    /// <summary>That hold broke: the ring runs empty.</summary>
    public void Stop(int slot, int by, int hold)
    {
        if (PartyState.IsSlot(slot))
            _rings[slot]?.Model.Stop(by, hold);
    }

    private Ring Get(int slot)
    {
        var ring = _rings[slot];
        if (ring == null || ring.Root == null) // none yet, or its canvas was taken away with the session's tags
            ring = _rings[slot] = Build(slot);
        return ring;
    }

    /// <summary>Is a teammate's hold on this player seen right now? (For the test trace.)</summary>
    public bool Holding(int slot) => PartyState.IsSlot(slot) && _rings[slot]?.Model.Holding == true;

    /// <summary>How full this player's ring is shown right now (0 when it isn't shown).</summary>
    public float Progress(int slot) =>
        PartyState.IsSlot(slot) && _rings[slot] is { Phase: Phase.Shown } ring ? ring.Model.Progress : 0f;

    /// <summary>This player is up again: the flash. <paramref name="strong"/>: revived by a teammate.</summary>
    public void Burst(int slot, bool strong, double now)
    {
        if (!PartyState.IsSlot(slot))
            return;
        var ring = Get(slot);
        ring.Phase = Phase.Burst;
        ring.Since = now;
        ring.Strong = strong;
        ring.Model.Reset();
        ReviveSound.Landed(strong);
    }

    /// <summary>Every frame, for every slot. <paramref name="body"/>: this game's player or the teammate's copy
    /// (null: not in this map).</summary>
    public void Tick(int slot, Character body, bool down, bool canRevive, double now)
    {
        if (!PartyState.IsSlot(slot))
            return;
        var ring = _rings[slot];
        if (ring == null || ring.Root == null)
        {
            if (!down)
                return;
            ring = Get(slot);
        }
        if (down && ring.Phase != Phase.Shown)
        {
            ring.Phase = Phase.Shown;
            ring.Since = now;
        }
        else if (!down && ring.Phase == Phase.Shown)
        {
            ring.Phase = Phase.Hidden; // up without a revive (they left, the session ended)
            ring.Model.Reset();
        }
        if (ring.Phase == Phase.Burst && now - ring.Since >= BurstSeconds)
            ring.Phase = Phase.Hidden;

        if (ring.Phase == Phase.Hidden || !TeammateTags.Spot(body, out var at, out _, out _))
        {
            Show(ring, false);
            return;
        }
        Show(ring, true);
        DrawKey(ring, ring.Phase == Phase.Shown && canRevive && slot == PromptSlot);
        var size = UiKit.CanvasSize;
        ring.Root.anchoredPosition = new Vector2(
            Mathf.Clamp(at.x, EdgeGap, size.x - EdgeGap), Mathf.Clamp(at.y + Lift, EdgeGap, size.y - EdgeGap));
        if (ring.Phase == Phase.Shown)
            DrawShown(ring, canRevive, now);
        else
            DrawBurst(ring, now);
    }

    public void Remove(int slot)
    {
        if (!PartyState.IsSlot(slot))
            return;
        var ring = _rings[slot];
        _rings[slot] = null;
        if (ring?.Root != null)
            UnityEngine.Object.Destroy(ring.Root.gameObject);
    }

    public void Destroy()
    {
        for (int slot = 0; slot < _rings.Length; slot++)
            Remove(slot);
    }

    // ------------------------------------------------------------------ drawing

    private static void Show(Ring ring, bool on)
    {
        if (ring.Active == on)
            return;
        ring.Active = on;
        ring.Root.gameObject.SetActive(on);
    }

    private static void DrawShown(Ring ring, bool canRevive, double now)
    {
        float appear = Mathf.Clamp01((float)((now - ring.Since) / AppearSeconds));
        bool beat = ring.Model.Tick(now, canRevive);
        bool holding = ring.Model.Holding;
        float p = ring.Model.Progress, pulse = ring.Model.Pulse;
        var color = FillColor(p);
        if (beat)
        {
            // one wave for the lub, a smaller one for the dub
            ring.RippleAt[0] = now;
            ring.RippleStrength[0] = holding ? 1f : 0.45f;
            ring.RippleAt[1] = now + ReviveBeat.DubSeconds;
            ring.RippleStrength[1] = holding ? 0.6f : 0.25f;
            if (holding)
                ReviveSound.Beat(p);
        }
        ring.Group.alpha = appear * (canRevive ? 1f : 0.7f);
        ring.BodyGroup.alpha = 1f;
        float body = EaseOutBack(appear) * (1f + (holding ? 0.09f : 0.045f) * pulse);
        ring.Body.localScale = new Vector3(body, body, 1f);

        ring.Track.color = new Color(1f, 1f, 1f, canRevive ? 0.22f : 0.12f);
        ring.Fill.fillAmount = p;
        ring.Fill.color = color;
        bool tip = p > 0.01f && p < 0.995f;
        ring.Tip.enabled = tip;
        if (tip)
        {
            float angle = p * Mathf.PI * 2f; // clockwise from the top, like the fill
            ring.Tip.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * BandRadius;
            float s = 1f + 0.35f * pulse;
            ring.Tip.rectTransform.localScale = new Vector3(s, s, 1f);
        }
        var heart = canRevive ? PixelIcons.Get("heart") : HeartGrey();
        if (ring.Heart.sprite != heart)
            ring.Heart.sprite = heart;
        float hs = 1f + (holding ? 0.32f : 0.18f) * pulse;
        ring.Heart.rectTransform.localScale = new Vector3(hs, hs, 1f);
        ring.Heart.color = Color.white;

        // a glow behind the ring that throbs with the heart while someone holds
        float glow = holding ? 0.22f + 0.4f * pulse : canRevive ? 0.12f * pulse : 0f;
        ring.Glow.color = new Color(color.r, color.g, color.b, glow);
        for (int i = 0; i < ring.Ripples.Length; i++)
        {
            float t = (float)((now - ring.RippleAt[i]) / RippleSeconds);
            var wave = ring.Ripples[i];
            if (t < 0f || t >= 1f)
            {
                wave.enabled = false;
                continue;
            }
            wave.enabled = true;
            float scale = 1f + 0.7f * EaseOut(t);
            wave.rectTransform.localScale = new Vector3(scale, scale, 1f);
            var c = holding ? color : HeartRed;
            wave.color = new Color(c.r, c.g, c.b, 0.6f * ring.RippleStrength[i] * (1f - t) * (1f - t));
        }
        ring.Flash.enabled = false;
        foreach (var spark in ring.Sparks)
            spark.enabled = false;
    }

    private static void DrawBurst(Ring ring, double now)
    {
        float t = Mathf.Clamp01((float)((now - ring.Since) / BurstSeconds));
        float k = ring.Strong ? 1f : 0.6f;
        ring.Group.alpha = 1f;

        // the ring: full and white-hot, it swells and fades
        float tb = Mathf.Clamp01(t / 0.6f);
        float body = 1f + 0.5f * k * EaseOut(tb);
        ring.Body.localScale = new Vector3(body, body, 1f);
        ring.BodyGroup.alpha = (1f - tb) * (1f - tb);
        ring.Track.color = new Color(1f, 1f, 1f, 0.22f);
        ring.Fill.fillAmount = 1f;
        ring.Fill.color = Color.Lerp(Color.white, LifeGreen, tb);
        ring.Tip.enabled = false;
        var heart = PixelIcons.Get("heart");
        if (ring.Heart.sprite != heart)
            ring.Heart.sprite = heart;
        float hs = 1f + 0.6f * EaseOut(tb);
        ring.Heart.rectTransform.localScale = new Vector3(hs, hs, 1f);
        ring.Glow.color = new Color(LifeGreen.r, LifeGreen.g, LifeGreen.b, 0.5f * k * (1f - t));

        float tf = Mathf.Clamp01(t / 0.5f);
        ring.Flash.enabled = true;
        float flash = k * (0.5f + 2.2f * EaseOut(tf));
        ring.Flash.rectTransform.localScale = new Vector3(flash, flash, 1f);
        ring.Flash.color = new Color(1f, 1f, 1f, 0.95f * k * (1f - tf) * (1f - tf));

        for (int i = 0; i < ring.Ripples.Length; i++)
        {
            var wave = ring.Ripples[i];
            wave.enabled = true;
            float scale = 1f + (i == 0 ? 1.7f : 1.1f) * k * EaseOut(t);
            wave.rectTransform.localScale = new Vector3(scale, scale, 1f);
            var c = i == 0 ? LifeGreen : Color.white;
            wave.color = new Color(c.r, c.g, c.b, (i == 0 ? 0.8f : 0.5f) * (1f - t) * (1f - t));
        }
        for (int i = 0; i < ring.Sparks.Length; i++)
        {
            var spark = ring.Sparks[i];
            spark.enabled = ring.Strong;
            if (!ring.Strong)
                continue;
            float angle = Mathf.PI * 2f * i / SparkCount + 0.3f;
            float reach = 56f + 17f * (i * 7 % 3); // three lengths, mixed around the circle
            var at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (reach * EaseOut(t)) + Vector2.up * (24f * t);
            spark.rectTransform.anchoredPosition = at;
            float s = Mathf.Lerp(1f, 0.3f, t);
            spark.rectTransform.localScale = new Vector3(s, s, 1f);
            var c = SparkColors[i % SparkColors.Length];
            spark.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(t / 0.08f) * Mathf.Pow(1f - t, 1.2f));
        }
    }

    /// <summary>Red (a heart that needs help) over amber to green (life is back).</summary>
    private static Color FillColor(float p) =>
        p < 0.6f ? Color.Lerp(HeartRed, Amber, p / 0.6f) : Color.Lerp(Amber, LifeGreen, (p - 0.6f) / 0.4f);

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    /// <summary>Lands a little past 1 and settles: the ring pops in.</summary>
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float y = x - 1f;
        return 1f + c3 * y * y * y + c1 * y * y;
    }

    // ------------------------------------------------------------------ building

    private Ring Build(int slot)
    {
        Sprites();
        var ring = new Ring { Root = UiKit.Rect("ReviveRing P" + (slot + 1), _tags.Layer) };
        ring.Root.anchorMin = ring.Root.anchorMax = Vector2.zero;
        ring.Root.pivot = new Vector2(0.5f, 0.5f);
        ring.Root.sizeDelta = new Vector2(Size, Size);
        ring.Group = ring.Root.gameObject.AddComponent<CanvasGroup>();
        ring.Group.blocksRaycasts = ring.Group.interactable = false;

        ring.Glow = Part("Glow", ring.Root, _soft, Size * 2.1f, Color.clear);
        for (int i = 0; i < ring.Ripples.Length; i++)
            ring.Ripples[i] = Part("Wave" + i, ring.Root, _wave, Size, Color.clear);

        ring.Body = UiKit.Rect("Body", ring.Root);
        Center(ring.Body, Size);
        ring.BodyGroup = ring.Body.gameObject.AddComponent<CanvasGroup>();
        ring.BodyGroup.blocksRaycasts = ring.BodyGroup.interactable = false;
        Part("Shade", ring.Body, _soft, Size * 1.55f, new Color(0f, 0f, 0f, 0.5f));
        Part("Outline", ring.Body, _outline, Size, new Color(0.05f, 0.03f, 0.06f, 0.8f));
        ring.Track = Part("Track", ring.Body, _band, Size, new Color(1f, 1f, 1f, 0.22f));
        ring.Fill = Part("Fill", ring.Body, _band, Size, HeartRed);
        ring.Fill.type = Image.Type.Filled;
        ring.Fill.fillMethod = Image.FillMethod.Radial360;
        ring.Fill.fillOrigin = (int)Image.Origin360.Top;
        ring.Fill.fillClockwise = true;
        ring.Fill.fillAmount = 0f;
        ring.Tip = Part("Tip", ring.Body, _dot, 17f, Color.white);
        ring.Heart = Part("Heart", ring.Body, PixelIcons.Get("heart"), 33f, Color.white);
        ring.Key = Part("Key", ring.Body, null, KeyHeight, Color.white);
        ring.Key.rectTransform.anchoredPosition = new Vector2(Size * 0.46f, -Size * 0.46f);
        ring.Key.preserveAspect = true;
        ring.Key.gameObject.SetActive(false);

        ring.Flash = Part("Flash", ring.Root, _soft, Size, Color.clear);
        for (int i = 0; i < ring.Sparks.Length; i++)
            ring.Sparks[i] = Part("Spark" + i, ring.Root, _dot, 11f, Color.clear);
        return ring;
    }

    private const float KeyHeight = 42f; // 30 was too small to read in a test window (COOP_LOG 54j)

    /// <summary>The key badge at the ring's lower right: bobs while it waits, sits pressed while the hold runs.</summary>
    private static void DrawKey(Ring ring, bool on)
    {
        if (ring.Key == null)
            return;
        var icon = on ? KeyIcon() : null;
        bool show = icon != null;
        if (ring.Key.gameObject.activeSelf != show)
            ring.Key.gameObject.SetActive(show);
        if (!show)
            return;
        if (ring.Key.sprite != icon)
        {
            ring.Key.sprite = icon;
            var size = icon.rect.size;
            ring.Key.rectTransform.sizeDelta = new Vector2(size.y > 0f ? size.x / size.y * KeyHeight : KeyHeight, KeyHeight);
        }
        float scale = ring.Model.Holding ? 0.86f : 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f);
        ring.Key.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>The game's own picture of the key or button that interacts, for the device used last (what its
    /// menus and prompts show; UI.Pause.KeyBinder does the same). Null: none known.</summary>
    private static Sprite KeyIcon()
    {
        try
        {
            var map = UserInput.KeyMapper.Map;
            var action = map?.Interaction;
            var resource = CommonResource.instance;
            if (action == null || resource == null)
                return null;
            foreach (BindingSource binding in action.Bindings)
                if (UserInput.KeyMap.SimplifyBindingSourceType(binding.BindingSourceType) == map.SimplifiedLastInputType)
                    return resource.TryGetKeyIcon(binding, out var sprite, outline: true) ? sprite : null;
        }
        catch
        {
            // no icon: the prompt's text still says what to do
        }
        return null;
    }

    private static Image Part(string name, Transform parent, Sprite sprite, float size, Color color)
    {
        var image = UiKit.Image(name, parent, color, sprite);
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        Center(image.rectTransform, size);
        return image;
    }

    private static void Center(RectTransform rect, float size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
    }

    /// <summary>The ring's pictures: white shapes with soft edges, coloured where they are shown.</summary>
    private static void Sprites()
    {
        if (_band == null)
            _band = Disc(d => Band(d, BandInner, BandOuter));
        if (_outline == null)
            _outline = Disc(d => Band(d, BandInner - 3.5f, BandOuter + 3.5f));
        if (_wave == null)
            _wave = Disc(d => Band(d, 55f, 61f));
        if (_soft == null)
            _soft = Disc(d => Mathf.Pow(Mathf.Clamp01(1f - d / Half), 2f));
        if (_dot == null)
            _dot = Disc(d => Mathf.Clamp01((Half * 0.62f - d) / (Half * 0.5f) + 0.5f));
    }

    /// <summary>1 inside a band between two radii, with an edge of about a pixel and a half.</summary>
    private static float Band(float d, float inner, float outer) =>
        Mathf.Clamp01(Mathf.Min(d - inner, outer - d) / 1.5f + 0.5f);

    private static Sprite Disc(Func<float, float> alphaAt)
    {
        var texture = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[Tex * Tex];
        for (int y = 0; y < Tex; y++)
            for (int x = 0; x < Tex; x++)
            {
                float dx = x + 0.5f - Half, dy = y + 0.5f - Half;
                float a = Mathf.Clamp01(alphaAt(Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * Tex + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>The pixel heart without its colour: a heart that stands still.</summary>
    private static Sprite HeartGrey()
    {
        if (_heartGrey != null)
            return _heartGrey;
        var pixels = PixelIcons.Pixels("heart");
        int h = pixels.GetLength(0), w = pixels.GetLength(1);
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var flat = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = pixels[y, x];
                byte grey = (byte)Mathf.Clamp(Mathf.RoundToInt(0.45f * c.r + 0.4f * c.g + 0.15f * c.b + 40f), 0, 255);
                flat[(h - 1 - y) * w + x] = new Color32(grey, grey, (byte)Mathf.Min(255, grey + 12), c.a); // rows start at the bottom
            }
        texture.SetPixels32(flat);
        texture.Apply(false, true);
        _heartGrey = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return _heartGrey;
    }
}
