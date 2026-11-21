using System;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop;

public enum LootMessageKind : byte
{
    /// <summary>The host to every client: an enemy the host runs dropped this much (coins fly out on every game,
    /// every player's own wallet gets the full amount).</summary>
    Drop = 1,
    /// <summary>The host to every client: that enemy dropped a healing orb of this size (everyone gets their own).</summary>
    Potion = 2,
    /// <summary>A player to the host, the host to everyone else: a plain prop (a barrel, a gold pile) broke in my
    /// game, with what it dropped. It breaks on the other games too and drops the same there.</summary>
    Broken = 3,
}

/// <summary>One fall of coins: which currency (the game's Currency.Type as a number), how much in all, in how many
/// pieces.</summary>
public readonly struct LootDrop
{
    public const int MaxAmount = 1_000_000, MaxCount = 100;

    public readonly byte Currency;
    public readonly int Amount, Count;

    public LootDrop(int currency, int amount, int count)
    {
        Currency = (byte)Math.Min(255, Math.Max(0, currency));
        Amount = Math.Min(MaxAmount, Math.Max(0, amount));
        Count = Math.Min(MaxCount, Math.Max(1, count));
    }
}

/// <summary>
/// Loot of the shared world on the wire (COOP_PLAN §4.2 "every player gets the full gold from enemies"; plain logic,
/// tested; MessageType.Loot, reliable). Places are relative to the map's origin.
/// [kind u8][room u32][x f32][y f32] + Drop [currency u8][amount varuint][count u8][force x f32][force y f32]
/// | Potion [size u8] | Broken [n u8] n x ([currency u8][amount varuint][count u8]).
/// </summary>
public struct LootMessage
{
    public const int MaxDrops = 8;

    public LootMessageKind Kind;
    public uint Room;
    public float X, Y;
    /// <summary>Drop: what fell.</summary>
    public LootDrop Drop;
    /// <summary>Drop: the push the coins fly with (the hit that killed the enemy).</summary>
    public float ForceX, ForceY;
    /// <summary>Potion: 0 small, 1 medium, 2 large.</summary>
    public byte PotionSize;
    /// <summary>Broken: what the prop dropped in the game it broke in (often nothing).</summary>
    public LootDrop[] Drops;

    public static LootMessage OfDrop(uint room, float x, float y, LootDrop drop, float forceX, float forceY) => new()
    {
        Kind = LootMessageKind.Drop, Room = room, X = x, Y = y, Drop = drop, ForceX = forceX, ForceY = forceY,
    };

    public static LootMessage OfPotion(uint room, float x, float y, int size) => new()
    {
        Kind = LootMessageKind.Potion, Room = room, X = x, Y = y, PotionSize = (byte)Math.Min(2, Math.Max(0, size)),
    };

    public static LootMessage OfBroken(uint room, float x, float y, LootDrop[] drops) => new()
    {
        Kind = LootMessageKind.Broken, Room = room, X = x, Y = y, Drops = drops ?? Array.Empty<LootDrop>(),
    };
}

public static class LootMessages
{
    public static void Write(NetWriter w, in LootMessage m)
    {
        w.WriteByte((byte)m.Kind);
        w.WriteUInt32(m.Room);
        w.WriteFloat(m.X);
        w.WriteFloat(m.Y);
        switch (m.Kind)
        {
            case LootMessageKind.Drop:
                WriteDrop(w, m.Drop);
                w.WriteFloat(m.ForceX);
                w.WriteFloat(m.ForceY);
                break;
            case LootMessageKind.Potion:
                w.WriteByte(m.PotionSize);
                break;
            case LootMessageKind.Broken:
                int n = Math.Min(LootMessage.MaxDrops, m.Drops?.Length ?? 0);
                w.WriteByte((byte)n);
                for (int i = 0; i < n; i++)
                    WriteDrop(w, m.Drops[i]);
                break;
        }
    }

    /// <summary>False for a kind this version doesn't know or numbers that make no sense (the message is ignored).</summary>
    public static bool TryRead(NetReader r, out LootMessage m)
    {
        m = new LootMessage { Kind = (LootMessageKind)r.ReadByte() };
        if (m.Kind < LootMessageKind.Drop || m.Kind > LootMessageKind.Broken)
            return false;
        m.Room = r.ReadUInt32();
        m.X = r.ReadFloat();
        m.Y = r.ReadFloat();
        if (!Finite(m.X) || !Finite(m.Y))
            return false;
        switch (m.Kind)
        {
            case LootMessageKind.Drop:
                m.Drop = ReadDrop(r);
                m.ForceX = r.ReadFloat();
                m.ForceY = r.ReadFloat();
                if (!Finite(m.ForceX) || !Finite(m.ForceY))
                    m.ForceX = m.ForceY = 0f;
                return true;
            case LootMessageKind.Potion:
                m.PotionSize = r.ReadByte();
                return m.PotionSize <= 2;
            default:
                int n = r.ReadByte();
                if (n > LootMessage.MaxDrops)
                    return false;
                m.Drops = new LootDrop[n];
                for (int i = 0; i < n; i++)
                    m.Drops[i] = ReadDrop(r);
                return true;
        }
    }

    private static void WriteDrop(NetWriter w, in LootDrop drop)
    {
        w.WriteByte(drop.Currency);
        w.WriteVarUInt((ulong)drop.Amount);
        w.WriteByte((byte)drop.Count);
    }

    private static LootDrop ReadDrop(NetReader r)
    {
        int currency = r.ReadByte();
        uint amount = r.ReadVarUInt32();
        int count = r.ReadByte();
        return new LootDrop(currency, (int)Math.Min(amount, (uint)LootDrop.MaxAmount), count);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
