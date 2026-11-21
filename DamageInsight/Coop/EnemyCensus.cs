using System.Collections.Generic;
using System.Linq;
using Characters;

namespace DamageInsight.Coop;

/// <summary>
/// The room census (flight recorder lite, COOP_LOG 64): every <see cref="CensusSeconds"/> s each game counts the
/// enemy-side characters alive in its room, bound (a net id: the host decides them) and unbound (this game's own),
/// with the unbound ones' names. The trace gets a "census" line when the count changed (or once a minute), so the
/// report can line both games up ("alive on one game only": the Norman test's maids, lab subjects, maze cats). Always
/// on: one "[Coop]" warning (-> session report) per room that is cleared while unbound ones still live on this game.
/// </summary>
internal sealed partial class EnemySync
{
    private const double CensusSeconds = 10, CensusRepeatSeconds = 60;
    private double _nextCensus, _lastCensusWrite;
    private string _lastCensus = "";
    private uint _censusWarnedRoom;
    private static readonly List<Character> CensusBuffer = new();

    private void Census(double now)
    {
        if (_map == null || _room == 0 || now < _nextCensus)
            return;
        _nextCensus = now + CensusSeconds;
        bool cleared = IsHost ? _sentCleared == _room : HostCleared.Contains(_room);
        bool warn = cleared && _censusWarnedRoom != _room;
        if (!CoopTrace.On && !warn)
            return;
        int bound = 0, unboundCount = 0;
        var unbound = new SortedDictionary<string, int>();
        _map.GetComponentsInChildren(false, CensusBuffer);
        foreach (var c in CensusBuffer)
        {
            if (c == null || !c.liveAndActive || c.type is Character.Type.Player or Character.Type.PlayerMinion
                    or Character.Type.Trap or Character.Type.Dummy || CoopCopies.IsPlayerSide(c) || CoopCopies.BelongsToCopy(c))
                continue;
            if (_ids.ContainsKey(c))
            {
                bound++;
                continue;
            }
            unboundCount++;
            string name = CoopTrace.NameOf(c) + (c.type == Character.Type.TrashMob ? "" : " [" + c.type + "]");
            unbound[name] = unbound.TryGetValue(name, out int n) ? n + 1 : 1;
        }
        CensusBuffer.Clear();
        string names = string.Join(", ", unbound.OrderByDescending(p => p.Value).Take(8)
            .Select(p => p.Value > 1 ? p.Key + " x" + p.Value : p.Key));
        string text = "bound " + bound + ", unbound " + unboundCount + (unboundCount > 0 ? ": " + names : "");
        if (CoopTrace.On && (text != _lastCensus || now - _lastCensusWrite >= CensusRepeatSeconds))
        {
            CoopTrace.Write("census", _room, what: text, amount: bound, after: unboundCount);
            _lastCensus = text;
            _lastCensusWrite = now;
        }
        if (warn && unboundCount > 0 && unbound.Keys.Any(k => !k.EndsWith("[Boss]")))
        {
            _censusWarnedRoom = _room;
            Plugin.Log.LogWarning($"[Coop] room cleared, but {unboundCount} enemy-side character(s) only this game " +
                                  $"knows still live here: {names}");
        }
        else if (warn)
            _censusWarnedRoom = _room;
    }
}
