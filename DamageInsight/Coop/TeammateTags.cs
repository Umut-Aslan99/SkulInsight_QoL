using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.Coop;

/// <summary>
/// Teammates on screen (COOP_PLAN §4.8; Umut 2026-11-03: "a small name tag above each teammate and, if possible, a
/// health bar"): a name tag in the player's colour with a small health bar above each teammate's head, an arrow with
/// the name at the screen edge while they are off screen, and a short party list (name, health) above this player's
/// HUD at the bottom left. Names are shown as plain text (no rich-text tags from other players).
/// </summary>
internal sealed class TeammateTags
{
    private const float TagWidth = 160f, BarHeight = 6f, EdgeMargin = 46f;

    private static readonly Color[] SlotColors =
    {
        new Color32(0xF2, 0xC1, 0x4E, 0xFF), // P1 (the host): gold
        new Color32(0x4E, 0xC9, 0xF2, 0xFF), // P2: sky blue
        new Color32(0x9B, 0xE5, 0x64, 0xFF), // P3: green
        new Color32(0xF2, 0x7E, 0xC9, 0xFF), // P4: pink
    };

    public static Color ColorOf(int slot) => SlotColors[Math.Max(0, slot) % SlotColors.Length];

    private sealed class Tag
    {
        public RectTransform Root;
        public TextMeshProUGUI Name;
        public Image Bar;
        public RectTransform Row;
        public TextMeshProUGUI RowText;
        public Image RowBar;
        public string ShownName = "";
    }

    private Canvas _canvas;
    private RectTransform _world, _list;
    private TextMeshProUGUI _banner, _notice, _connection;
    private readonly Dictionary<int, Tag> _tags = new();
    private bool _covered;
    private int _coverFrame = -1;

    private void EnsureCanvas()
    {
        if (_canvas != null)
            return;
        _canvas = UiKit.OverlayCanvas("SkulInsightCoop_Tags", 40);
        _covered = false;
        // tags, rings and the party list: what a game menu covers (see Cover)
        _world = UiKit.Rect("World", _canvas.transform);
        _world.Stretch(0f, 0f, 0f, 0f);
        _list = UiKit.Rect("Party", _world);
        _list.anchorMin = _list.anchorMax = Vector2.zero;
        _list.pivot = Vector2.zero;
        _list.anchoredPosition = new Vector2(30f, 232f);
        _list.sizeDelta = new Vector2(260f, 120f);
    }

    private Tag TagOf(int slot)
    {
        if (_tags.TryGetValue(slot, out var tag))
            return tag;
        EnsureCanvas();
        var color = ColorOf(slot);
        tag = new Tag { Root = UiKit.Rect("P" + (slot + 1), _world) };
        tag.Root.anchorMin = tag.Root.anchorMax = Vector2.zero;
        tag.Root.pivot = new Vector2(0.5f, 0f);
        tag.Root.sizeDelta = new Vector2(TagWidth, 34f);
        tag.Name = UiKit.Text("Name", tag.Root, "", 17f, TextAlignmentOptions.Bottom, color);
        tag.Name.richText = false;
        tag.Name.enableWordWrapping = false;
        UiKit.Outline(tag.Name, 0.2f);
        tag.Name.rectTransform.Place(0f, 0f, TagWidth, 24f);
        var back = UiKit.Image("Bar", tag.Root, new Color(0f, 0f, 0f, 0.65f));
        back.rectTransform.Place(TagWidth / 2f - 30f, 26f, 60f, BarHeight);
        tag.Bar = UiKit.Image("Fill", back.transform, color);
        tag.Bar.rectTransform.Place(0f, 0f, 60f, BarHeight);

        tag.Row = UiKit.Rect("Row" + slot, _list);
        tag.Row.anchorMin = tag.Row.anchorMax = Vector2.zero;
        tag.Row.pivot = Vector2.zero;
        tag.Row.sizeDelta = new Vector2(260f, 24f);
        tag.RowText = UiKit.Text("Text", tag.Row, "", 16f, TextAlignmentOptions.Left, color);
        tag.RowText.richText = false;
        tag.RowText.enableWordWrapping = false;
        UiKit.Outline(tag.RowText, 0.2f);
        tag.RowText.rectTransform.Place(0f, 0f, 170f, 22f);
        var rowBack = UiKit.Image("Bar", tag.Row, new Color(0f, 0f, 0f, 0.65f));
        rowBack.rectTransform.Place(172f, 8f, 80f, BarHeight);
        tag.RowBar = UiKit.Image("Fill", rowBack.transform, color);
        tag.RowBar.rectTransform.Place(0f, 0f, 80f, BarHeight);
        _tags[slot] = tag;
        StackRows();
        return tag;
    }

    /// <summary>The health rows bottom left, one above the other by slot without gaps (3-4 players, COOP_LOG 70: a
    /// row's height per slot left an empty line where this game's own slot is).</summary>
    private void StackRows()
    {
        int index = 0;
        foreach (int slot in _tags.Keys.OrderBy(s => s))
            if (_tags[slot].Row != null)
                _tags[slot].Row.anchoredPosition = new Vector2(0f, 28f * index++);
    }

    /// <summary>One teammate, every frame. <paramref name="avatar"/> null: not in this map (only the party row).</summary>
    public void Update(int slot, string name, Characters.Character avatar, float health, float maxHealth)
    {
        var tag = TagOf(slot);
        Cover();
        float fraction = maxHealth > 0 ? Mathf.Clamp01(health / maxHealth) : 0f;
        string row = name + "  " + (maxHealth > 0
            ? Mathf.CeilToInt(health).ToString(CultureInfo.InvariantCulture) + "/" + Mathf.CeilToInt(maxHealth).ToString(CultureInfo.InvariantCulture)
            : "");
        if (tag.RowText.text != row)
            tag.RowText.text = row;
        tag.RowBar.rectTransform.sizeDelta = new Vector2(80f * fraction, BarHeight);
        tag.Bar.rectTransform.sizeDelta = new Vector2(60f * fraction, BarHeight);

        if (!Spot(avatar, out var at, out bool off, out bool left))
        {
            tag.Root.gameObject.SetActive(false);
            return;
        }
        tag.Root.gameObject.SetActive(true);
        // Off screen: pinned to the edge on the side the teammate is, with an arrow.
        string label = !off ? name : left ? "< " + name : name + " >";
        if (tag.ShownName != label)
        {
            tag.ShownName = label;
            tag.Name.text = label;
        }
        tag.Root.anchoredPosition = at;
    }

    /// <summary>The layer of the tags (the rings over downed players are drawn on it too).</summary>
    internal Transform Layer
    {
        get
        {
            EnsureCanvas();
            return _world;
        }
    }

    /// <summary>
    /// Once a frame: while one of the game's menus is open (pause, inventory, a shop, an NPC's talk, a choice), the
    /// tags, the rings, the party list and the revive line are switched off. An overlay canvas is always drawn over
    /// the game's whole UI, so its menus could not cover them: the ring sat in the middle of the pause menu and of
    /// the inventory (Umut 2026-11-15). The banner ("Waiting for ...") stays: it is about the session, not the room.
    /// </summary>
    private void Cover()
    {
        if (_canvas == null || _coverFrame == Time.frameCount)
            return;
        _coverFrame = Time.frameCount;
        // the run's result screen too (2026-11-16 dm6: the connection box, the party line and a teammate's tag stood
        // over "Shattered!" on both games)
        bool covered = global::UI.Dialogue.anyDialogueOpened || ResultShown();
        if (covered == _covered)
            return;
        _covered = covered;
        _world.gameObject.SetActive(!covered);
        if (_notice != null)
            _notice.gameObject.SetActive(!covered && !string.IsNullOrEmpty(_notice.text));
    }

    private static bool ResultShown()
    {
        try
        {
            var result = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.gameResult;
            return result != null && result.gameObject.activeInHierarchy;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>A tag's height above its spot: what stands over a head starts above this.</summary>
    internal const float TagHeight = 34f;

    /// <summary>Where a character's head is on the canvas (canvas units from the bottom left). Off screen
    /// (<paramref name="off"/>) the spot is pinned to the edge on the character's side (<paramref name="left"/>).
    /// False: not in this map or no camera.</summary>
    internal static bool Spot(Characters.Character avatar, out Vector2 at, out bool off, out bool left)
    {
        at = default;
        off = left = false;
        var camera = Camera.main;
        if (avatar == null || camera == null || !avatar.gameObject.activeInHierarchy)
            return false;
        var head = avatar.transform.position;
        if (avatar.collider != null)
            head.y = avatar.collider.bounds.max.y;
        Vector3 screen = camera.WorldToScreenPoint(head + Vector3.up * 0.3f);
        float scale = UiKit.CanvasScale;
        var size = UiKit.CanvasSize;
        at = new Vector2(screen.x / scale, screen.y / scale);
        bool behind = screen.z < 0f;
        off = behind || at.x < 0f || at.x > size.x || at.y < 0f || at.y > size.y;
        if (!off)
            return true;
        if (behind)
            at = -at;
        left = at.x < size.x / 2f;
        at.x = Mathf.Clamp(at.x, EdgeMargin + TagWidth / 2f, size.x - EdgeMargin - TagWidth / 2f);
        at.y = Mathf.Clamp(at.y, EdgeMargin, size.y - EdgeMargin - 40f);
        return true;
    }

    /// <summary>A line at the top of the screen ("Waiting for ..."); "" hides it.</summary>
    public void Banner(string text)
    {
        if (string.IsNullOrEmpty(text) && _banner == null)
            return;
        EnsureCanvas();
        if (_banner == null)
        {
            _banner = UiKit.Text("Banner", _canvas.transform, "", 26f, TextAlignmentOptions.Center);
            _banner.richText = false;
            _banner.outlineWidth = 0.25f;
            _banner.rectTransform.anchorMin = _banner.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _banner.rectTransform.pivot = new Vector2(0.5f, 1f);
            _banner.rectTransform.anchoredPosition = new Vector2(0f, -140f);
            _banner.rectTransform.sizeDelta = new Vector2(1200f, 40f);
        }
        if (_banner.text != text)
            _banner.text = text ?? "";
        _banner.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>A second line below the banner (going down and revive); "" hides it.</summary>
    public void Notice(string text)
    {
        if (string.IsNullOrEmpty(text) && _notice == null)
            return;
        EnsureCanvas();
        if (_notice == null)
        {
            _notice = UiKit.Text("Notice", _canvas.transform, "", 24f, TextAlignmentOptions.Center);
            _notice.richText = false;
            _notice.outlineWidth = 0.25f;
            _notice.rectTransform.anchorMin = _notice.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _notice.rectTransform.pivot = new Vector2(0.5f, 1f);
            _notice.rectTransform.anchoredPosition = new Vector2(0f, -182f);
            _notice.rectTransform.sizeDelta = new Vector2(1200f, 40f);
        }
        if (_notice.text != text)
            _notice.text = text ?? "";
        _notice.gameObject.SetActive(!_covered && !string.IsNullOrEmpty(text));
    }

    /// <summary>The connection lines below the notice ("Connection problem with ...", package DC); "" hides them.
    /// Shown also without a session (a client trying to join again).</summary>
    public void Connection(string text)
    {
        if (string.IsNullOrEmpty(text) && _connection == null)
            return;
        EnsureCanvas();
        if (_connection == null)
        {
            _connection = UiKit.Text("Connection", _canvas.transform, "", 24f, TextAlignmentOptions.Top);
            _connection.richText = false;
            _connection.outlineWidth = 0.25f;
            _connection.color = new Color(1f, 0.82f, 0.35f);
            _connection.rectTransform.anchorMin = _connection.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _connection.rectTransform.pivot = new Vector2(0.5f, 1f);
            _connection.rectTransform.anchoredPosition = new Vector2(0f, -224f);
            _connection.rectTransform.sizeDelta = new Vector2(1400f, 140f);
        }
        if (_connection.text != text)
            _connection.text = text ?? "";
        _connection.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void Remove(int slot)
    {
        if (!_tags.TryGetValue(slot, out var tag))
            return;
        if (tag.Root != null)
            UnityEngine.Object.Destroy(tag.Root.gameObject);
        if (tag.Row != null)
            UnityEngine.Object.Destroy(tag.Row.gameObject);
        _tags.Remove(slot);
        StackRows();
    }

    public void Destroy()
    {
        foreach (int slot in new List<int>(_tags.Keys))
            Remove(slot);
        if (_canvas != null)
            UnityEngine.Object.Destroy(_canvas.gameObject);
        _canvas = null;
    }
}
