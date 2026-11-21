using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop;

/// <summary>One room of a stage's path, as the host generated it (GAME_FLOW §2.5 option b): the room's Addressables
/// asset (its GUID loads it on any game, also a room this player's save would never offer), its kind, whether dark
/// enemies live there (Dark Mirror), the reward and the door type.</summary>
public sealed class PathNodeData : IEquatable<PathNodeData>
{
    public string Guid = "", AssetPath = "";
    public byte MapType, SpecialType, Reward, Gate;
    public bool DarkEnemy;

    public bool Empty => string.IsNullOrEmpty(Guid);

    public void Write(NetWriter w)
    {
        w.WriteString(Guid ?? "", 64);
        w.WriteString(AssetPath ?? "", 256);
        w.WriteByte(MapType);
        w.WriteByte(SpecialType);
        w.WriteBool(DarkEnemy);
        w.WriteByte(Reward);
        w.WriteByte(Gate);
    }

    public static PathNodeData Read(NetReader r) => new()
    {
        Guid = r.ReadString(64),
        AssetPath = r.ReadString(256),
        MapType = r.ReadByte(),
        SpecialType = r.ReadByte(),
        DarkEnemy = r.ReadBool(),
        Reward = r.ReadByte(),
        Gate = r.ReadByte(),
    };

    public bool Equals(PathNodeData o) => o != null && Guid == o.Guid && AssetPath == o.AssetPath && MapType == o.MapType
        && SpecialType == o.SpecialType && DarkEnemy == o.DarkEnemy && Reward == o.Reward && Gate == o.Gate;

    public override bool Equals(object obj) => Equals(obj as PathNodeData);
    public override int GetHashCode() => (Guid ?? "").GetHashCode() ^ Reward ^ (Gate << 8);
}

/// <summary>The messages of the shared world (plain logic, tested). Times aren't needed: they are reliable and ordered.</summary>
public static class WorldMessages
{
    public const int MaxPathLength = 64;

    // RunStart: [seed i32][chapter u8][Dark Mirror level u8] (the host's; a client plays a DM run at it: DarkMirrorLevel)
    public static void WriteRunStart(NetWriter w, int seed, int chapter, int darkMirrorLevel = 0)
    {
        w.WriteInt32(seed);
        w.WriteByte((byte)chapter);
        w.WriteByte((byte)Math.Max(0, Math.Min(255, darkMirrorLevel)));
    }

    public static (int seed, int chapter) ReadRunStart(NetReader r) => ReadRunStart(r, out _);

    public static (int seed, int chapter) ReadRunStart(NetReader r, out int darkMirrorLevel)
    {
        var start = (r.ReadInt32(), (int)r.ReadByte());
        darkMirrorLevel = r.ReadByte();
        return start;
    }

    // StagePath: [seed i32][stage str][count u8] + count x (node1, node2)
    public static void WriteStagePath(NetWriter w, int seed, string stage, IReadOnlyList<(PathNodeData, PathNodeData)> path)
    {
        w.WriteInt32(seed);
        w.WriteString(stage ?? "", 128);
        int count = Math.Min(path.Count, MaxPathLength);
        w.WriteByte((byte)count);
        for (int i = 0; i < count; i++)
        {
            (path[i].Item1 ?? new PathNodeData()).Write(w);
            (path[i].Item2 ?? new PathNodeData()).Write(w);
        }
    }

    public static (int seed, string stage, List<(PathNodeData, PathNodeData)> path) ReadStagePath(NetReader r)
    {
        int seed = r.ReadInt32();
        string stage = r.ReadString(128);
        int count = Math.Min((int)r.ReadByte(), MaxPathLength);
        var path = new List<(PathNodeData, PathNodeData)>(count);
        for (int i = 0; i < count; i++)
            path.Add((PathNodeData.Read(r), PathNodeData.Read(r)));
        return (seed, stage, path);
    }

    // NextMap: [seed i32][stage str][path index i16][node u8]
    public static void WriteNextMap(NetWriter w, int seed, string stage, int pathIndex, int node)
    {
        w.WriteInt32(seed);
        w.WriteString(stage ?? "", 128);
        w.WriteInt16((short)pathIndex);
        w.WriteByte((byte)node);
    }

    public static (int seed, string stage, int pathIndex, int node) ReadNextMap(NetReader r) =>
        (r.ReadInt32(), r.ReadString(128), r.ReadInt16(), r.ReadByte());

    // HostRoom: [castle bool][seed i32][chapter u8][stage u8][path i16][node u8][in a scene bool][Dark Mirror level u8]
    public static void WriteHostRoom(NetWriter w, RunSpot spot, bool inScene = false, int darkMirrorLevel = 0)
    {
        w.WriteBool(spot.Castle);
        w.WriteInt32(spot.Seed);
        w.WriteByte((byte)spot.Chapter);
        w.WriteByte((byte)spot.Stage);
        w.WriteInt16((short)spot.Path);
        w.WriteByte((byte)spot.Node);
        w.WriteBool(inScene);
        w.WriteByte((byte)Math.Max(0, Math.Min(255, darkMirrorLevel)));
    }

    public static RunSpot ReadHostRoom(NetReader r) => ReadHostRoom(r, out _, out _);

    public static RunSpot ReadHostRoom(NetReader r, out bool inScene) => ReadHostRoom(r, out inScene, out _);

    /// <param name="inScene">The host shows a story scene (its letterbox is on).</param>
    /// <param name="darkMirrorLevel">The host's Dark Mirror level (a catch-up into a DM run plays at it).</param>
    public static RunSpot ReadHostRoom(NetReader r, out bool inScene, out int darkMirrorLevel)
    {
        var spot = new RunSpot(r.ReadBool(), r.ReadInt32(), r.ReadByte(), r.ReadByte(), r.ReadInt16(), r.ReadByte());
        inScene = r.ReadBool();
        darkMirrorLevel = r.ReadByte();
        return spot;
    }

    // RoomReady: [room u32]
    public static void WriteRoomReady(NetWriter w, uint room) => w.WriteUInt32(room);
    public static uint ReadRoomReady(NetReader r) => r.ReadUInt32();

    /// <summary>A room's id for the loading barrier: the same on every game in the same room of the same run
    /// (FNV-1a 32 of seed, stage, path index and map name; never 0).</summary>
    public static uint RoomId(int seed, string stage, int pathIndex, string mapName)
    {
        uint hash = 2166136261;
        void Add(string text)
        {
            foreach (char c in text ?? "")
            {
                hash ^= c;
                hash *= 16777619;
            }
            hash ^= '|';
            hash *= 16777619;
        }
        Add(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(stage);
        Add(pathIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(Loadout.GearName(mapName));
        return hash == 0 ? 1 : hash;
    }
}

/// <summary>
/// The loading barrier (COOP_PLAN §4.8: "rooms change together: a short loading barrier until everyone is in, then
/// everyone starts at the same moment"): which players reached which room. Each game holds its own player still until
/// every player of the session is in the room, or <see cref="TimeoutSeconds"/> passed (plain logic, tested).
/// </summary>
public sealed class RoomBarrier
{
    public double TimeoutSeconds = 10;
    private readonly Dictionary<int, uint> _roomOf = new();
    private uint _room;
    private double _since = -1;

    /// <summary>The room this game waits in (0 = not waiting).</summary>
    public uint Room => _room;
    public bool Waiting => _room != 0;

    /// <summary>How long the last wait took, from Enter to the Release that ended it (for the session report).</summary>
    public double LastWaitSeconds { get; private set; }

    public void Reached(int slot, uint room) => _roomOf[slot] = room;

    public void Forget(int slot) => _roomOf.Remove(slot);

    /// <summary>This game entered <paramref name="room"/>: start waiting for the others.</summary>
    public void Enter(int localSlot, uint room, double now)
    {
        _roomOf[localSlot] = room;
        _room = room;
        _since = now;
    }

    /// <summary>The players still missing in the room this game waits in.</summary>
    public IEnumerable<int> Missing(IEnumerable<int> slots) =>
        _room == 0 ? Enumerable.Empty<int>() : slots.Where(s => !_roomOf.TryGetValue(s, out var r) || r != _room);

    /// <summary>True once everyone is in (or the wait timed out): the wait ends, call once per frame.</summary>
    public bool Release(IEnumerable<int> slots, double now, out bool timedOut)
    {
        timedOut = false;
        if (_room == 0)
            return false;
        bool all = !Missing(slots).Any();
        timedOut = !all && now - _since >= TimeoutSeconds;
        if (!all && !timedOut)
            return false;
        LastWaitSeconds = Math.Max(0, now - _since);
        _room = 0;
        _since = -1;
        return true;
    }
}
