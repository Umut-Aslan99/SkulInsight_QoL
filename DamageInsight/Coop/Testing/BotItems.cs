using System.Collections.Generic;
using System.Linq;
using Characters.Gear.Items;
using GameResources;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// The item sweep (Umut's sweep spec, 2026-11-13, point 7: "The same for every item's effect that shows"), first
/// version: `--skulinsight-bot-items=sweep:<half>:<first>`. The game's obtainable items in name order are split in
/// two (the host's bot takes every second one from the first, the client's bot the others); each bot carries ONE
/// item at a time for <see cref="ItemSeconds"/> while it fights, then the next. The trace's `bot-item` marks which;
/// the report (sync-report.py, "Items") compares, per item, its shots and the effects that came with it on this game
/// against what the other game showed. While it runs the bot takes no other gear (CoopAutoRun.Loot), so the item
/// is the only one. `bot-items-done` when its half is through.
/// </summary>
internal sealed class BotItems
{
    public const float ItemSeconds = 25f;

    /// <summary>An item sweep runs on this game (the bot's looting leaves gear alone).</summary>
    public static bool Active { get; internal set; }

    private readonly int _half;
    private int _index;
    private List<ItemReference> _list;
    private ItemRequest _request;
    private Item _current;
    private float _since;
    private bool _done;

    private BotItems(int half, int first)
    {
        _half = half;
        _index = first;
        Active = true;
    }

    /// <summary>From the launch option ("sweep:<half>:<first>"); null: no item sweep.</summary>
    public static BotItems From(string option)
    {
        if (string.IsNullOrEmpty(option) || !option.StartsWith("sweep"))
            return null;
        var part = option.Split(':');
        int half = part.Length > 1 && int.TryParse(part[1], out int h) ? h & 1 : 0;
        int first = part.Length > 2 && int.TryParse(part[2], out int f) ? f : 0;
        return new BotItems(half, first);
    }

    public void Tick(Characters.Character player)
    {
        if (_done || player == null)
            return;
        var levels = Singleton<Service>.Instance?.levelManager;
        var chapter = levels?.currentChapter;
        if (chapter == null || chapter.type == Chapter.Type.Castle || chapter.type == Chapter.Type.HardmodeCastle
            || Map.Instance == null || GearResource.instance?.items == null)
            return;
        _list ??= GearResource.instance.items.Where(r => r != null && r.obtainable).OrderBy(r => r.name, System.StringComparer.Ordinal)
            .Where((r, i) => (i & 1) == _half).ToList();
        float now = Time.unscaledTime;
        if (_request != null)
        {
            if (!_request.isDone)
                return;
            Take(levels, player, _request);
            _request = null;
            _since = now;
            return;
        }
        if (_current != null && now - _since < ItemSeconds)
            return;
        Drop(player);
        if (_index >= _list.Count)
        {
            _done = true;
            Plugin.Log.LogInfo($"[CoopTest] item sweep: all {_list.Count} items of this half done");
            CoopTrace.Write("bot-items-done", EnemySync.TraceRoom, amount: _list.Count);
            return;
        }
        _request = _list[_index++].LoadAsync();
    }

    private void Take(LevelManager levels, Characters.Character player, ItemRequest request)
    {
        var name = _list[_index - 1].name;
        try
        {
            var item = levels.DropItem(request, player.transform.position);
            if (item == null || item.dropped == null)
            {
                CoopTrace.Write("bot-item-failed", EnemySync.TraceRoom, what: name);
                return;
            }
            var before = player.playerComponents.inventory.item.items.ToList();
            item.dropped.InteractWith(player);
            // an item that turns into another one when it is picked up: the new one is what the bot carries
            _current = player.playerComponents.inventory.item.items.FirstOrDefault(i => i != null && !before.Contains(i)) ?? item;
            CoopTrace.Write("bot-item", EnemySync.TraceRoom, what: Loadout.GearName(name), amount: _index - 1);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[CoopTest] item sweep: {name} failed: {e.Message}");
            CoopTrace.Write("bot-item-failed", EnemySync.TraceRoom, what: name);
        }
    }

    /// <summary>The item before goes (discarded, as the QA tool does).</summary>
    private void Drop(Characters.Character player)
    {
        var inventory = player.playerComponents?.inventory?.item;
        if (inventory == null)
            return;
        for (int i = inventory.items.Count - 1; i >= 0; i--)
            if (inventory.items[i] != null)
                inventory.Discard(i);
        inventory.Trim();
        _current = null;
    }
}
