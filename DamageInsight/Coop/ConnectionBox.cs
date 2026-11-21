using System.Collections.Generic;
using System.Globalization;
using DamageInsight.Coop.Net;
using DamageInsight.Lang;
using DamageInsight.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.Coop;

/// <summary>
/// The connection box (COOP_PLAN 4.8: "A small connection box (ping, packet loss, a warning colour) that can be
/// hidden"): at the top left below the game's clock (Umut 2026-11-16: at the top right it covered the
/// adventurers' health bars), one row per teammate this game talks to (the host talks to every client, a client to
/// the host): a dot and the name in the player's colour, and the ping, green below 120 ms, amber below 250 ms, red
/// above. When nothing has come from a teammate for 1.5 s the row says so ("no signal for 3 s") in red: the moment
/// a player sees a teammate freeze, they see why. On the tags' layer, so a game menu covers it. Setting
/// Coop/ConnectionBox (on).
/// </summary>
internal sealed class ConnectionBox
{
    private const float Width = 260f, RowHeight = 24f, Left = 8f, Top = 72f;
    private const double GoodMs = 120, OkMs = 250, SilentSeconds = 1.5, Every = 0.25;

    private static readonly Color Good = new Color32(0x7C, 0xE0, 0x8A, 0xFF), Ok = new Color32(0xFF, 0xC8, 0x5A, 0xFF),
        Bad = new Color32(0xFF, 0x6A, 0x5A, 0xFF);

    private sealed class Row
    {
        public RectTransform Root;
        public Image Dot;
        public TextMeshProUGUI Name, Value;
        public string ShownName, ShownValue;
    }

    private readonly CoopSession _session;
    private readonly TeammateTags _tags;
    private readonly Dictionary<int, Row> _rows = new();
    private readonly List<int> _gone = new();
    private RectTransform _root;
    private double _next;

    public ConnectionBox(CoopSession session, TeammateTags tags)
    {
        _session = session;
        _tags = tags;
    }

    public void Destroy()
    {
        if (_root != null)
            Object.Destroy(_root.gameObject);
        _root = null;
        _rows.Clear();
    }

    public void Tick(double now)
    {
        if (now < _next)
            return;
        _next = now + Every;
        bool on = Plugin.CoopConnectionBox?.Value ?? true;
        if (_root == null || _root.parent == null)
        {
            if (!on)
                return;
            Build();
        }
        if (_root.gameObject.activeSelf != on)
            _root.gameObject.SetActive(on);
        if (!on)
            return;

        int shown = 0;
        _gone.Clear();
        _gone.AddRange(_rows.Keys);
        foreach (var player in _session.Roster)
        {
            if (player.Slot == _session.LocalSlot || (!_session.IsHost && player.Slot != 0))
                continue; // a client only talks to the host
            _gone.Remove(player.Slot);
            if (!_rows.TryGetValue(player.Slot, out var row))
                _rows[player.Slot] = row = MakeRow(player.Slot);
            row.Root.anchoredPosition = new Vector2(0f, -shown * RowHeight);
            shown++;
            Fill(row, player, now);
        }
        foreach (int slot in _gone)
        {
            Object.Destroy(_rows[slot].Root.gameObject);
            _rows.Remove(slot);
        }
    }

    private void Fill(Row row, RosterEntry player, double now)
    {
        if (row.ShownName != player.Name)
        {
            row.ShownName = player.Name;
            row.Name.text = player.Name;
        }
        double silence = _session.SilenceSeconds(player.Slot), ping = _session.PingMs(player.Slot);
        string value;
        Color color;
        if (silence >= SilentSeconds)
        {
            value = Loc.F("no signal for {0} s", Mathf.FloorToInt((float)silence));
            color = Bad;
        }
        else if (ping < 0)
        {
            value = "...";
            color = Ok;
        }
        else
        {
            value = ping.ToString("0", CultureInfo.InvariantCulture) + " ms";
            color = ping < GoodMs ? Good : ping < OkMs ? Ok : Bad;
        }
        if (row.ShownValue != value)
        {
            row.ShownValue = value;
            row.Value.text = value;
        }
        row.Value.color = color;
        row.Dot.color = color;
    }

    private void Build()
    {
        _rows.Clear();
        _root = UiKit.Rect("ConnectionBox", _tags.Layer);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(Left, -Top);
        _root.sizeDelta = new Vector2(Width, RowHeight);
    }

    private Row MakeRow(int slot)
    {
        var row = new Row { Root = UiKit.Rect("P" + (slot + 1), _root) };
        row.Root.anchorMin = row.Root.anchorMax = row.Root.pivot = new Vector2(0f, 1f);
        row.Root.sizeDelta = new Vector2(Width, RowHeight);
        var back = UiKit.Image("Back", row.Root, new Color(0f, 0f, 0f, 0.45f));
        back.raycastTarget = false;
        back.rectTransform.anchorMin = Vector2.zero;
        back.rectTransform.anchorMax = Vector2.one;
        back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
        row.Dot = UiKit.Image("Dot", row.Root, Good);
        row.Dot.raycastTarget = false;
        Place(row.Dot.rectTransform, 10f, 8f, 8f);
        row.Name = UiKit.Text("Name", row.Root, "", 16f, TextAlignmentOptions.Left, TeammateTags.ColorOf(slot));
        row.Name.richText = false; // a player's name is shown as typed
        row.Name.enableWordWrapping = false;
        row.Name.overflowMode = TextOverflowModes.Ellipsis;
        row.Name.raycastTarget = false;
        Place(row.Name.rectTransform, 24f, 110f, RowHeight + 8f);
        row.Value = UiKit.Text("Value", row.Root, "", 16f, TextAlignmentOptions.Right);
        row.Value.richText = false;
        row.Value.enableWordWrapping = false;
        row.Value.raycastTarget = false;
        Place(row.Value.rectTransform, 136f, Width - 144f, RowHeight + 8f);
        return row;
    }

    /// <summary>From the row's left edge, centred on its middle line.</summary>
    private static void Place(RectTransform rect, float x, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, height);
    }
}
