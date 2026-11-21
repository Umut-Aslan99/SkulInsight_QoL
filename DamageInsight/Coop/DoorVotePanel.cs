using System;
using System.Collections.Generic;
using System.Globalization;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using DamageInsight.UI;
using Level;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.Coop;

/// <summary>
/// How a door vote looks (Umut 2026-11-15, on the plain text line it replaces: "it is readable but not that
/// beautiful. Maybe we can use small icons for doors, with a skull, a sword or gold coins icon at the bottom right
/// corner of the door icon, and then maybe a little skull icon with the name or so above the door."). Under the
/// top of the screen, without a word (so nothing to translate):
/// - One picture per door of the room, the left door on the left: the real door's own sprite, with a round badge
///   at its lower right for what the door leads to (gold, a skull, an item, ...).
/// - Above each door the players who voted for it: a small skull in the player's colour and the name. A new vote
///   slides there and bumps the door.
/// - Between the doors (beside a single door): the players who have not voted, the seconds the others have left
///   and a bar that runs out. A player still taking the room's reward shows a chest instead of the skull, and
///   until everyone is done the clock shows the reward wait in blue (votes made meanwhile already stand).
/// - The result: the chosen door grows and glows, the other one dims. A coin flip: a coin swings between the two
///   doors, each door shows its chance in percent, and the coin lands on the winner.
/// Drawn on the tags' layer, so it is hidden while a game menu is open. CoopDoors feeds it the host's state.
/// </summary>
internal sealed class DoorVotePanel
{
    /// <summary>How long the coin swings before it lands. The host waits Rules.DoorVoteSettings.CoinFlipSeconds
    /// (the same by default) before the room changes.</summary>
    public const double FlipSeconds = DoorVoteSettings.DefaultCoinFlipSeconds;

    private const float PanelTop = 226f, DoorSize = 150f, DoorY = 170f, FirstRowY = 72f, RowStep = 30f,
        TwoDoorsX = 230f, OneDoorX = 130f, BarWidth = 150f, CoinSize = 46f, NameWidth = 190f;
    private const double AppearSeconds = 0.18, MoveSeconds = 0.22, BumpSeconds = 0.25, WinSeconds = 0.3,
        FadeSeconds = 0.3, AgreedSeconds = 1.3, AfterFlipSeconds = 1.5;
    private const int Waiting = -1, Unset = -2, Tex = 64;

    private static readonly Color Gold = new Color32(0xFF, 0xD8, 0x6A, 0xFF), Amber = new Color32(0xFF, 0xC8, 0x5A, 0xFF),
        Alarm = new Color32(0xFF, 0x6A, 0x5A, 0xFF), Calm = new Color32(0x9C, 0xD4, 0xFF, 0xFF), Ink = new Color(0.07f, 0.05f, 0.09f, 0.92f);

    private sealed class DoorView
    {
        public int Node = Unset, Votes, Percent = int.MinValue;
        public SpriteRenderer Source;
        public RectTransform Root;
        public CanvasGroup Group;
        public Image Glow, Picture, BadgeIcon;
        public GameObject Badge;
        public TextMeshProUGUI Chance;
        public double BumpAt = double.NegativeInfinity;
        public float X, Scale;
        public Vector2 PivotAt;
        public bool Fitted;
    }

    private sealed class Chip
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public Image Skull, Back;
        public TextMeshProUGUI Name;
        public string ShownName;
        public bool Dots;
        public int Target = Unset;
        public Vector2 From, At;
        public double MovedAt = double.NegativeInfinity;
    }

    private static Sprite _soft, _disc;

    private readonly TeammateTags _tags;
    private readonly DoorView[] _doors = { new(), new() };
    private readonly Chip[] _chips = new Chip[PartyState.MaxPlayers];
    private readonly int[] _stack = new int[3];
    private RectTransform _root, _clockRect, _barFill, _coin;
    private CanvasGroup _group;
    private GameObject _clock;
    private TextMeshProUGUI _count;
    private Image _barFillImage, _coinFace;
    private int _doorCount, _second = -1;
    private bool _on, _built, _clockWait;
    private double _since, _secondAt;
    private float _total;

    public DoorVotePanel(TeammateTags tags) => _tags = tags;

    /// <summary>How long a result stays on screen (the coin's swing included).</summary>
    public static double ResultSeconds(in DoorVoteMessage state) =>
        state.CoinFlip ? FlipSeconds + AfterFlipSeconds : AgreedSeconds;

    /// <summary>The doors of the room the vote is about (the game's own door objects). An empty list changes
    /// nothing: the room is going away and the vote that just ended keeps its pictures.</summary>
    public void SetDoors(List<Gate> gates)
    {
        if (gates == null || gates.Count == 0)
            return;
        Build();
        gates.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        int count = Math.Min(_doors.Length, gates.Count);
        bool same = count == _doorCount;
        for (int i = 0; i < count && same; i++)
            same = _doors[i].Node == (int)gates[i]._nodeIndex && _doors[i].Source != null
                   && _doors[i].Source.gameObject == gates[i].gameObject;
        if (same)
            return;
        _doorCount = count;
        _total = 0f;
        for (int i = 0; i < _doors.Length; i++)
        {
            var view = _doors[i];
            bool used = i < count;
            view.Root.gameObject.SetActive(used);
            view.Chance.gameObject.SetActive(used);
            view.Votes = 0;
            view.BumpAt = double.NegativeInfinity;
            if (!used)
            {
                view.Node = Unset;
                view.Source = null;
                continue;
            }
            var gate = gates[i];
            view.Node = (int)gate._nodeIndex;
            view.Source = gate.GetComponent<SpriteRenderer>();
            view.Fitted = false;
            Fit(view, view.Source != null && view.Source.sprite != null ? view.Source.sprite : PixelIcons.Get("door"));
            // the door's kind sits with its graphics (the game never sets Gate._type)
            var kind = gate._gateProperty != null ? gate._gateProperty.type : Gate.Type.None;
            var badge = PixelIcons.Get(BadgeOf(kind));
            Plugin.Log.LogInfo($"[Coop] door vote: door {view.Node} is {kind}, picture x{view.Scale:0.00} of {view.Picture.sprite?.rect.size}");
            view.Badge.SetActive(badge != null);
            view.BadgeIcon.sprite = badge;
        }
    }

    /// <summary>
    /// Shows a door's sprite so that what is drawn of it fills the picture's box. A door's sprite is a wide,
    /// mostly empty sheet (in the first game run the door was a third of its box): the sprite's mesh hugs the
    /// drawn part, so its outline says where the door is. Scale and place come from the first frame and stay for
    /// the door's animation; every frame is placed by its own pivot.
    /// </summary>
    private static void Fit(DoorView view, Sprite sprite)
    {
        view.Picture.sprite = sprite;
        if (sprite == null)
            return;
        float ppu = sprite.pixelsPerUnit;
        if (!view.Fitted)
        {
            Vector2 min = -sprite.pivot / ppu, max = (sprite.rect.size - sprite.pivot) / ppu; // the whole sheet
            var points = sprite.vertices;
            if (points.Length > 0)
            {
                min = max = points[0];
                foreach (var point in points)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
            }
            var drawn = (max - min) * ppu;
            view.Scale = DoorSize / Mathf.Max(1f, Mathf.Max(drawn.x, drawn.y));
            view.PivotAt = -(min + max) * 0.5f * ppu * view.Scale; // the sprite's pivot, so the drawn part is centred
            view.Fitted = true;
        }
        // Drawn with the sprite's own mesh (Image.useSpriteMesh): the game packs some door sprites flipped in its
        // atlas, and the plain rectangle showed them upside down (Umut's picture, 2026-11-15). With the mesh, the
        // Image maps a vertex v (units from the sprite's pivot) to v / bounds.size * the rect's size from the
        // rect's pivot; so the rect's pivot is the sprite's and its size is bounds.size in pixels times the scale.
        var rect = view.Picture.rectTransform;
        rect.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        var bounds = (Vector2)sprite.bounds.size;
        rect.sizeDelta = (bounds.x > 0f && bounds.y > 0f ? bounds * ppu : sprite.rect.size) * view.Scale;
        rect.anchoredPosition = view.PivotAt;
    }

    /// <summary>The badge of a door: what waits behind it.</summary>
    private static string BadgeOf(Gate.Type type) => type switch
    {
        Gate.Type.Normal => "coins",
        Gate.Type.Grave => "skull",
        Gate.Type.Chest => "sword",
        Gate.Type.Npc => "star",
        Gate.Type.Terminal => "flag",
        Gate.Type.Adventurer => "trophy",
        Gate.Type.Boss => "crown",
        _ => null,
    };

    public void Hide()
    {
        if (!_on)
            return;
        _on = false;
        if (_root != null)
            _root.gameObject.SetActive(false);
        foreach (var chip in _chips)
            if (chip != null)
                chip.Target = Unset;
        foreach (var view in _doors)
        {
            view.Votes = 0;
            view.BumpAt = double.NegativeInfinity;
        }
        _total = 0f;
        _second = -1;
    }

    public void Destroy()
    {
        _on = _built = false;
        _doorCount = 0;
        if (_root != null)
            UnityEngine.Object.Destroy(_root.gameObject);
        _root = null;
    }

    // ------------------------------------------------------------------ every frame

    /// <summary>Draws the vote as the host told it at <paramref name="stateAt"/> (this game's clock).</summary>
    public void Draw(in DoorVoteMessage state, double stateAt, double now, IReadOnlyList<RosterEntry> roster)
    {
        if (_doorCount == 0 || _root == null || !_built)
        {
            Hide();
            return;
        }
        if (!_on)
        {
            _on = true;
            _since = now;
            _root.gameObject.SetActive(true);
        }
        float t = (float)(now - stateAt);
        bool resolved = state.Resolved, flip = resolved && state.CoinFlip && _doorCount == 2;
        float landAt = flip ? (float)FlipSeconds : 0f;
        bool decided = resolved && t >= landAt;

        float appear = Mathf.Clamp01((float)((now - _since) / AppearSeconds));
        float alpha = appear;
        if (resolved)
            alpha *= 1f - Mathf.Clamp01((t - (float)(ResultSeconds(state) - FadeSeconds)) / (float)FadeSeconds);
        _group.alpha = alpha;
        float size = Mathf.Lerp(0.92f, 1f, EaseOut(appear));
        _root.localScale = new Vector3(size, size, 1f);

        int total = 0;
        for (int i = 0; i < _doorCount; i++)
        {
            var view = _doors[i];
            int votes = 0;
            for (int p = 0; p < roster.Count; p++)
                if (state.VoteOf(roster[p].Slot) == view.Node)
                    votes++;
            if (votes > view.Votes)
                view.BumpAt = now;
            view.Votes = votes;
            total += votes;
        }
        float infoX = _doorCount == 2 ? 0f : OneDoorX;
        for (int i = 0; i < _doorCount; i++)
            DrawDoor(_doors[i], i, state, flip, decided, decided ? t - landAt : 0f, total, now);
        DrawChips(state, roster, infoX, resolved, now);
        DrawClock(state, t, infoX, resolved, now);
        DrawCoin(state, flip, t, landAt);
    }

    private void DrawDoor(DoorView view, int index, in DoorVoteMessage state, bool flip, bool decided, float since,
        int total, double now)
    {
        view.X = _doorCount == 2 ? (index == 0 ? -TwoDoorsX : TwoDoorsX) : -OneDoorX;
        if (view.Source != null && view.Source.sprite != null && view.Picture.sprite != view.Source.sprite)
            Fit(view, view.Source.sprite); // the door's own animation
        float bump = Mathf.Clamp01((float)((now - view.BumpAt) / BumpSeconds));
        float scale = 1f + 0.14f * Mathf.Sin(Mathf.PI * bump), shown = 1f;
        var glow = Color.clear;
        bool winner = view.Node == state.Door;
        if (decided)
        {
            float w = Mathf.Clamp01(since / (float)WinSeconds);
            if (winner)
            {
                scale = Mathf.LerpUnclamped(1f, 1.2f, EaseOutBack(w));
                glow = Gold;
                glow.a = w * (0.5f + 0.15f * Mathf.Sin(since * 9f));
            }
            else
            {
                scale = Mathf.Lerp(1f, 0.88f, w);
                shown = Mathf.Lerp(1f, 0.3f, w);
            }
        }
        view.Root.anchoredPosition = new Vector2(view.X, -DoorY);
        view.Root.localScale = new Vector3(scale, scale, 1f);
        view.Group.alpha = shown;
        view.Glow.color = glow;

        int percent = flip && total > 0 ? Mathf.RoundToInt(100f * view.Votes / total) : -1;
        if (percent != view.Percent)
        {
            view.Percent = percent;
            view.Chance.text = percent < 0 ? "" : percent.ToString(CultureInfo.InvariantCulture) + " %";
        }
        view.Chance.rectTransform.anchoredPosition = new Vector2(view.X, -(DoorY + DoorSize / 2f + 24f));
        view.Chance.color = decided && winner ? Gold : new Color(1f, 1f, 1f, decided ? 0.45f : 0.9f);
    }

    private void DrawChips(in DoorVoteMessage state, IReadOnlyList<RosterEntry> roster, float infoX, bool resolved, double now)
    {
        _stack[0] = _stack[1] = _stack[2] = 0; // rows taken above door 0, above door 1, in the waiting column
        for (int slot = 0; slot < _chips.Length; slot++)
        {
            var chip = _chips[slot];
            RosterEntry player = null;
            for (int p = 0; p < roster.Count; p++)
                if (roster[p].Slot == slot)
                {
                    player = roster[p];
                    break;
                }
            if (chip.Root.gameObject.activeSelf != (player != null))
                chip.Root.gameObject.SetActive(player != null);
            if (player == null)
            {
                chip.Target = Unset;
                continue;
            }
            int vote = state.VoteOf(slot), column = 2;
            for (int i = 0; i < _doorCount; i++)
                if (vote >= 0 && _doors[i].Node == vote)
                    column = i;
            bool waiting = column == 2;
            // the first row sits right over the door, later ones stack upwards
            var at = new Vector2(waiting ? infoX : _doors[column].X, -(FirstRowY - RowStep * _stack[column]++));
            int target = waiting ? Waiting : vote;
            if (chip.Target != target)
            {
                bool first = chip.Target == Unset; // the panel just came up: no slide
                chip.From = first ? at : chip.At;
                chip.MovedAt = first ? double.NegativeInfinity : now;
                chip.Target = target;
            }
            float u = Mathf.Clamp01((float)((now - chip.MovedAt) / MoveSeconds));
            chip.At = Vector2.Lerp(chip.From, at, EaseOut(u));
            chip.Root.anchoredPosition = chip.At;
            float pop = waiting ? 1f : 1f + 0.25f * Mathf.Sin(Mathf.PI * u);
            chip.Root.localScale = new Vector3(pop, pop, 1f);
            chip.Group.alpha = waiting ? (resolved ? 0.4f : 0.85f) : 1f; // 0.6 washed out over bright rooms (run 3)

            // still taking the room's reward (the host waits for them): a chest in place of the skull, no dots
            bool busy = waiting && !resolved && !state.IsReady(slot);
            var icon = PixelIcons.Get(busy ? "chest" : "skull");
            if (chip.Skull.sprite != icon)
            {
                chip.Skull.sprite = icon;
                chip.Skull.color = busy ? Color.white : TeammateTags.ColorOf(slot);
            }
            bool dots = waiting && !resolved && !busy;
            if (chip.ShownName != player.Name || chip.Dots != dots)
            {
                chip.ShownName = player.Name;
                chip.Dots = dots;
                chip.Name.text = dots ? player.Name + " ..." : player.Name;
                chip.Name.ForceMeshUpdate();
                float width = Mathf.Min(NameWidth, chip.Name.preferredWidth);
                chip.Skull.rectTransform.anchoredPosition = new Vector2(14f - width / 2f - 16f, 0f);
                chip.Back.rectTransform.anchoredPosition = new Vector2(2f, 0f);
                chip.Back.rectTransform.sizeDelta = new Vector2(width + 90f, 46f);
            }
        }
    }

    private void DrawClock(in DoorVoteMessage state, float t, float infoX, bool resolved, double now)
    {
        // the vote's countdown; before it, the reward wait's (calm blue: nobody is late yet)
        bool vote = state.SecondsLeft >= 0f, wait = !vote && state.GateSeconds >= 0f;
        bool runs = !resolved && (vote || wait);
        if (_clock.activeSelf != runs)
            _clock.SetActive(runs);
        if (!runs)
        {
            _second = -1;
            return;
        }
        if (wait != _clockWait)
        {
            _clockWait = wait;
            _total = 0f; // the bar runs for the new countdown
        }
        float from = vote ? state.SecondsLeft : state.GateSeconds;
        float left = Mathf.Max(0f, from - t);
        _total = Mathf.Max(_total, from);
        int second = Mathf.CeilToInt(left);
        if (second != _second)
        {
            _second = second;
            _secondAt = now;
            _count.text = second.ToString(CultureInfo.InvariantCulture);
        }
        // the number ticks, and turns from white over amber to red in the last three seconds
        float pulse = 1f + 0.3f * (1f - Mathf.Clamp01((float)((now - _secondAt) / 0.25)));
        var color = wait ? Calm : left > 3f ? Color.white : Color.Lerp(Alarm, Amber, Mathf.Clamp01(left / 3f));
        _clockRect.anchoredPosition = new Vector2(infoX, -DoorY);
        _count.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
        _count.color = color;
        _barFill.sizeDelta = new Vector2(BarWidth * (_total > 0f ? Mathf.Clamp01(left / _total) : 0f), 7f);
        _barFillImage.color = color;
    }

    /// <summary>The coin: from the middle to the winner's side, back over to the other door, and home to the
    /// winner, slower towards the end; it spins on the way and lands face up on the winner's inner upper corner.</summary>
    private void DrawCoin(in DoorVoteMessage state, bool flip, float t, float landAt)
    {
        if (_coin.gameObject.activeSelf != flip)
            _coin.gameObject.SetActive(flip);
        if (!flip)
            return;
        float side = _doors[1].Node == state.Door ? 1f : -1f;
        float u = Mathf.Clamp01(t / landAt), e = 1f - (1f - u) * (1f - u);
        float angle = 2.5f * Mathf.PI * e;
        float x = side * Mathf.Sin(angle) * (TwoDoorsX - DoorSize * 0.36f);
        float y = DoorY - DoorSize * 0.36f - 60f * Mathf.Abs(Mathf.Cos(angle)); // high between the doors, down on each of them
        float turn = Mathf.Cos(e * 12f * Mathf.PI);
        float pop = u >= 1f ? 1f + 0.3f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - landAt) / 0.25f)) : 1f;
        _coin.anchoredPosition = new Vector2(x, -y);
        _coin.localScale = new Vector3((Mathf.Abs(turn) < 0.08f ? 0.08f : turn) * pop, pop, 1f);
        _coinFace.enabled = turn > 0f;
    }

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float y = x - 1f;
        return 1f + c3 * y * y * y + c1 * y * y;
    }

    // ------------------------------------------------------------------ building

    private void Build()
    {
        if (_root != null && _built)
            return;
        if (_root != null)
            UnityEngine.Object.Destroy(_root.gameObject); // a build that broke half way
        _built = false;
        _soft ??= Disc(d =>
        {
            float a = Mathf.Max(0f, 1f - d / (Tex / 2f));
            return a * a;
        });
        _disc ??= Disc(d => Tex / 2f - 1f - d);
        _on = false;
        _doorCount = 0;
        _root = UiKit.Rect("DoorVote", _tags.Layer);
        Top(_root, 0f, PanelTop, 780f, 320f);
        _root.pivot = new Vector2(0.5f, 1f);
        _root.anchoredPosition = new Vector2(0f, -PanelTop);
        _group = _root.gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = _group.interactable = false;
        // The panel stays switched on while it is built: a text made under a switched-off parent has no material
        // yet, and setting its outline throws (the first game run, 2026-11-15).

        for (int i = 0; i < _doors.Length; i++)
        {
            var view = _doors[i] = new DoorView();
            view.Root = UiKit.Rect("Door" + i, _root);
            Top(view.Root, 0f, DoorY, DoorSize, DoorSize);
            view.Group = view.Root.gameObject.AddComponent<CanvasGroup>();
            view.Group.blocksRaycasts = view.Group.interactable = false;
            view.Glow = Part("Glow", view.Root, _soft, DoorSize * 1.9f, Color.clear);
            Part("Shade", view.Root, _soft, DoorSize * 1.7f, new Color(0f, 0f, 0f, 0.75f));
            view.Picture = Part("Picture", view.Root, PixelIcons.Get("door"), DoorSize, Color.white);
            view.Picture.useSpriteMesh = true; // see Fit
            var badge = Part("Badge", view.Root, _disc, 54f, Ink);
            badge.rectTransform.anchoredPosition = new Vector2(DoorSize * 0.36f, -DoorSize * 0.34f);
            view.Badge = badge.gameObject;
            view.BadgeIcon = Part("Icon", badge.transform, null, 34f, Color.white);
        }
        foreach (var view in _doors)
        {
            view.Chance = UiKit.Text("Chance", _root, "", 22f, TextAlignmentOptions.Center);
            view.Chance.richText = false;
            UiKit.Outline(view.Chance, 0.22f);
            view.Chance.raycastTarget = false;
            Top(view.Chance.rectTransform, 0f, DoorY + DoorSize / 2f + 24f, 140f, 30f);
        }

        for (int slot = 0; slot < _chips.Length; slot++)
        {
            var color = TeammateTags.ColorOf(slot);
            var chip = _chips[slot] = new Chip();
            chip.Root = UiKit.Rect("Voter" + (slot + 1), _root);
            Top(chip.Root, 0f, FirstRowY, NameWidth + 50f, 40f);
            chip.Group = chip.Root.gameObject.AddComponent<CanvasGroup>();
            chip.Group.blocksRaycasts = chip.Group.interactable = false;
            // a soft dark oval behind skull and name: a cyan name was hard to read over a light room (run 3)
            chip.Back = Part("Back", chip.Root, _soft, 46f, new Color(0f, 0f, 0f, 0.55f));
            chip.Skull = Part("Skull", chip.Root, PixelIcons.Get("skull"), 24f, color);
            chip.Name = UiKit.Text("Name", chip.Root, "", 21f, TextAlignmentOptions.Center, color);
            chip.Name.richText = false; // a player's name is shown as typed
            chip.Name.enableWordWrapping = false;
            chip.Name.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.Outline(chip.Name, 0.28f);
            chip.Name.raycastTarget = false;
            // 40 high for a 21 font: with Ellipsis, a line taller than its box is not drawn at all (the names were
            // missing at 28, Umut's picture 2026-11-15)
            Middle(chip.Name.rectTransform, 14f, 0f, NameWidth, 40f);
            chip.Root.gameObject.SetActive(false);
        }

        _clockRect = UiKit.Rect("Clock", _root);
        Top(_clockRect, 0f, DoorY, 200f, 120f);
        _clock = _clockRect.gameObject;
        _count = UiKit.Text("Seconds", _clockRect, "", 48f, TextAlignmentOptions.Center);
        _count.richText = false;
        UiKit.Outline(_count, 0.25f);
        _count.raycastTarget = false;
        Middle(_count.rectTransform, 0f, 8f, 160f, 64f);
        var back = UiKit.Image("Bar", _clockRect, new Color(0f, 0f, 0f, 0.55f));
        back.raycastTarget = false;
        Middle(back.rectTransform, 0f, -40f, BarWidth + 4f, 11f);
        _barFillImage = UiKit.Image("Fill", back.transform, Color.white);
        _barFillImage.raycastTarget = false;
        _barFill = _barFillImage.rectTransform;
        Middle(_barFill, 0f, 0f, BarWidth, 7f);
        _clock.SetActive(false);

        _coin = UiKit.Rect("Coin", _root);
        Top(_coin, 0f, DoorY, CoinSize, CoinSize);
        Part("Rim", _coin, _disc, CoinSize, new Color32(0xA8, 0x74, 0x24, 0xFF));
        Part("Gold", _coin, _disc, CoinSize * 0.8f, new Color32(0xFF, 0xD0, 0x4A, 0xFF));
        _coinFace = Part("Face", _coin, PixelIcons.Get("skull"), CoinSize * 0.5f, new Color(0.55f, 0.36f, 0.1f, 1f));
        _coin.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
        _built = true;
    }

    /// <summary>Placed from the panel's top centre: x to the right, y downwards, the rect's middle there.</summary>
    private static void Top(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>Placed from the parent's middle (y upwards).</summary>
    private static void Middle(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static Image Part(string name, Transform parent, Sprite sprite, float size, Color color)
    {
        var image = UiKit.Image(name, parent, color, sprite);
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        Middle(image.rectTransform, 0f, 0f, size, size);
        return image;
    }

    /// <summary>A white round picture whose alpha comes from the distance to its middle (in pixels).</summary>
    private static Sprite Disc(Func<float, float> alphaAt)
    {
        var texture = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[Tex * Tex];
        for (int y = 0; y < Tex; y++)
            for (int x = 0; x < Tex; x++)
            {
                float dx = x + 0.5f - Tex / 2f, dy = y + 0.5f - Tex / 2f;
                float a = Mathf.Clamp01(alphaAt(Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * Tex + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), 100f);
    }
}
