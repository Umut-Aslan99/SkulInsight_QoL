using System;

namespace DamageInsight.Coop.Rules;

/// <summary>
/// Who is in the party right now, as the rules see it: per player slot (0-3), connected or not, down or up. One
/// object shared by the room's rules on the host (door vote, reward gate, revive). The connection flags come from the
/// session; the down flags are changed only by <see cref="ReviveRules"/> (it owns going down and coming back), so the
/// rules never disagree about who is down. Built for 4 players from the start (COOP_PLAN §12.2).
/// </summary>
public sealed class PartyState
{
    public const int MaxPlayers = 4;

    private readonly bool[] _connected = new bool[MaxPlayers];
    private readonly bool[] _down = new bool[MaxPlayers];

    public static bool IsSlot(int slot) => slot >= 0 && slot < MaxPlayers;

    /// <summary>A player is in the run (joined or came back). A player who left while down is still down.</summary>
    public void Join(int slot)
    {
        Check(slot);
        _connected[slot] = true;
    }

    /// <summary>A player left or lost the connection. They no longer vote, block or count as alive.</summary>
    public void Leave(int slot)
    {
        Check(slot);
        _connected[slot] = false;
    }

    /// <summary>A new person takes this slot: everything about the old one is forgotten.</summary>
    public void Reset(int slot)
    {
        Check(slot);
        _connected[slot] = false;
        _down[slot] = false;
    }

    public bool IsConnected(int slot) => IsSlot(slot) && _connected[slot];
    public bool IsDown(int slot) => IsSlot(slot) && _down[slot];

    /// <summary>Connected and not down: votes, blocks the reward gate, can be targeted, can revive.</summary>
    public bool IsActive(int slot) => IsSlot(slot) && _connected[slot] && !_down[slot];

    public int ConnectedCount => Count(false);
    public int ActiveCount => Count(true);

    internal void SetDown(int slot, bool down)
    {
        Check(slot);
        _down[slot] = down;
    }

    private int Count(bool activeOnly)
    {
        int n = 0;
        for (int i = 0; i < MaxPlayers; i++)
            if (_connected[i] && !(activeOnly && _down[i]))
                n++;
        return n;
    }

    private static void Check(int slot)
    {
        if (!IsSlot(slot))
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "player slots are 0-3");
    }
}
