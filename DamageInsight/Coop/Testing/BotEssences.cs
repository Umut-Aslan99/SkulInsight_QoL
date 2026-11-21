using System.Collections.Generic;
using System.Linq;
using Characters.Gear.Quintessences;
using GameResources;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// The quintessence sweep (Umut 2026-11-21: "prove it with a quintessence sweep in bot runs"), through the item sweep's
/// launch option: `--skulinsight-bot-items=essences:<half>:<first>`. The game's quintessences in name order are split
/// in two (the host's bot takes every second one from the first, the client's bot the others); each bot carries ONE at
/// a time for <see cref="EssenceSeconds"/> while it fights and uses it up to <see cref="Uses"/> times (its cooldown is
/// made ready before each try; a try fails while the quintessence's own conditions don't hold, e.g. no enemy near).
/// The trace's `bot-item` (what = "Q:<name>") marks the window, so the report's item table counts its shots and
/// effects; `essence-sent` (owner) against `essence-shown` / `essence-missing` (other game) count the uses.
/// `bot-items-done` when its half is through.
/// </summary>
internal sealed class BotEssences
{
    public const float EssenceSeconds = 14f, TrySeconds = 2.5f;
    public const int Uses = 3;

    private readonly int _half;
    private int _index;
    private List<EssenceReference> _list;
    private EssenceRequest _request;
    private Quintessence _current;
    private float _since, _nextTry;
    private int _used;
    private bool _done;

    private BotEssences(int half, int first)
    {
        _half = half;
        _index = first;
    }

    /// <summary>From the launch option ("essences:<half>:<first>"); null: no quintessence sweep.</summary>
    public static BotEssences From(string option)
    {
        if (string.IsNullOrEmpty(option) || !option.StartsWith("essences"))
            return null;
        var part = option.Split(':');
        int half = part.Length > 1 && int.TryParse(part[1], out int h) ? h & 1 : 0;
        int first = part.Length > 2 && int.TryParse(part[2], out int f) ? f : 0;
        BotItems.Active = true; // the bot's looting leaves gear alone
        return new BotEssences(half, first);
    }

    public void Tick(Characters.Character player)
    {
        if (_done || player == null)
            return;
        var levels = Singleton<Service>.Instance?.levelManager;
        var chapter = levels?.currentChapter;
        if (chapter == null || chapter.type == Chapter.Type.Castle || chapter.type == Chapter.Type.HardmodeCastle
            || Map.Instance == null || GearResource.instance?.essences == null)
            return;
        _list ??= GearResource.instance.essences.Where(r => r != null && r.obtainable).OrderBy(r => r.name, System.StringComparer.Ordinal)
            .Where((r, i) => (i & 1) == _half).ToList();
        float now = Time.unscaledTime;
        if (_request != null)
        {
            if (!_request.isDone)
                return;
            Take(levels, player, _request);
            _request = null;
            _since = now;
            _nextTry = now + 1f;
            _used = 0;
            return;
        }
        if (_current != null && now - _since < EssenceSeconds)
        {
            if (_used < Uses && now >= _nextTry)
            {
                _nextTry = now + TrySeconds;
                TryUse(player);
            }
            return;
        }
        Drop(player);
        if (_index >= _list.Count)
        {
            _done = true;
            Plugin.Log.LogInfo($"[CoopTest] quintessence sweep: all {_list.Count} of this half done");
            CoopTrace.Write("bot-items-done", EnemySync.TraceRoom, amount: _list.Count);
            return;
        }
        _request = _list[_index++].LoadAsync();
    }

    private void Take(LevelManager levels, Characters.Character player, EssenceRequest request)
    {
        var name = _list[_index - 1].name;
        try
        {
            var essence = levels.DropQuintessence(request, player.transform.position);
            var inventory = player.playerComponents?.inventory?.quintessence;
            if (essence == null || inventory == null)
            {
                CoopTrace.Write("bot-item-failed", EnemySync.TraceRoom, what: "Q:" + name);
                return;
            }
            inventory.EquipAt(essence, 0);
            _current = essence;
            CoopTrace.Write("bot-item", EnemySync.TraceRoom, what: "Q:" + Loadout.GearName(name), amount: _index - 1);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[CoopTest] quintessence sweep: {name} failed: {e.Message}");
            CoopTrace.Write("bot-item-failed", EnemySync.TraceRoom, what: "Q:" + name);
        }
    }

    private void TryUse(Characters.Character player)
    {
        if (_current == null || _current.owner != player)
            return;
        try
        {
            // ready now (the sweep has no time for a 76 s cooldown)
            var cooldown = _current.cooldown;
            if (cooldown.time != null)
                cooldown.time.remainTime = 0f;
            if (cooldown.stacks < 1)
                cooldown.stacks = 1;
            int before = Sent;
            player.playerComponents.inventory.quintessence.UseAt(0);
            if (Sent > before)
                _used++;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[CoopTest] quintessence sweep: using {Loadout.GearName(_current.name)} failed: {e.Message}");
        }
    }

    /// <summary>Uses that reached the active (TeammateEssences counts them for the sweep).</summary>
    internal static int Sent;

    /// <summary>The quintessence before goes (removed, never "destroyed" for currency).</summary>
    private void Drop(Characters.Character player)
    {
        var inventory = player.playerComponents?.inventory?.quintessence;
        if (inventory != null)
            for (int i = 0; i < inventory.items.Count; i++)
                if (inventory.items[i] != null)
                    inventory.Remove(i);
        _current = null;
    }
}
