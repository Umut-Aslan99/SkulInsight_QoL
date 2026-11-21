using System;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;

namespace DamageInsight.Coop;

/// <summary>
/// The host's numbers for the rules every game needs (MessageType.HostRules, host → a client, reliable, on joining and
/// when the host changes a setting). COOP_PLAN 4: "host settings, like the balance numbers": the host decides, every
/// game plays by the same numbers.
/// [balance str: Rules.BalanceSettings' short text][hold tenths u8][reach tenths u8][revive health percent u8]
/// [revives per room u8][back at the clear percent u8]
/// </summary>
public struct HostRules
{
    public string Balance;
    public float HoldSeconds, Reach, ReviveHealth, RoomClearHealth;
    public int RevivesPerRoom;

    public static HostRules From(BalanceSettings balance, ReviveSettings revive) => new()
    {
        Balance = balance.ToText(), HoldSeconds = revive.HoldSeconds, Reach = revive.Reach,
        ReviveHealth = revive.ReviveHealth, RoomClearHealth = revive.RoomClearHealth, RevivesPerRoom = revive.RevivesPerRoom,
    };

    /// <summary>Puts these numbers into the rules' settings (clamped there). False: the balance text was not readable
    /// (the revive numbers are taken anyway).</summary>
    public bool ApplyTo(BalanceSettings balance, ReviveSettings revive)
    {
        revive.HoldSeconds = HoldSeconds;
        revive.Reach = Reach;
        revive.ReviveHealth = ReviveHealth;
        revive.RoomClearHealth = RoomClearHealth;
        revive.RevivesPerRoom = RevivesPerRoom;
        revive.Clamp();
        return balance.TryParse(Balance ?? "");
    }
}

public static class HostRulesMessages
{
    public static void Write(NetWriter w, in HostRules m)
    {
        w.WriteString(m.Balance ?? "");
        w.WriteByte(Tenths(m.HoldSeconds));
        w.WriteByte(Tenths(m.Reach));
        w.WriteByte(Percent(m.ReviveHealth));
        w.WriteByte((byte)Math.Max(0, Math.Min(255, m.RevivesPerRoom)));
        w.WriteByte(Percent(m.RoomClearHealth));
    }

    public static bool TryRead(NetReader r, out HostRules m)
    {
        m = default;
        try
        {
            m.Balance = r.ReadString();
            if (r.Remaining < 5)
                return false;
            m.HoldSeconds = r.ReadByte() / 10f;
            m.Reach = r.ReadByte() / 10f;
            m.ReviveHealth = r.ReadByte() / 100f;
            m.RevivesPerRoom = r.ReadByte();
            m.RoomClearHealth = r.ReadByte() / 100f;
            return true;
        }
        catch (Exception)
        {
            return false; // a broken string
        }
    }

    private static byte Tenths(float value) =>
        float.IsNaN(value) ? (byte)0 : (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 10f)));

    private static byte Percent(float share) =>
        float.IsNaN(share) ? (byte)0 : (byte)Math.Max(0, Math.Min(100, (int)Math.Round(share * 100f)));
}
