using System.Collections.Generic;

namespace DamageInsight.Coop;

/// <summary>A few room ids remembered (oldest forgotten first): the rooms the host has cleared, for a client whose
/// room may have finished loading before or after the host's word came (plain logic, tested).</summary>
public sealed class RecentRooms
{
    private readonly int _capacity;
    private readonly Queue<uint> _order = new();
    private readonly HashSet<uint> _rooms = new();

    public RecentRooms(int capacity = 8) => _capacity = capacity < 1 ? 1 : capacity;

    public void Add(uint room)
    {
        if (room == 0 || !_rooms.Add(room))
            return;
        _order.Enqueue(room);
        while (_order.Count > _capacity)
            _rooms.Remove(_order.Dequeue());
    }

    public bool Contains(uint room) => room != 0 && _rooms.Contains(room);

    public void Clear()
    {
        _order.Clear();
        _rooms.Clear();
    }
}
