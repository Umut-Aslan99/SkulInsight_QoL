using DamageInsight.Coop.Net;
using DamageInsight.Coop.Sync;

namespace DamageInsight.Coop;

/// <summary>
/// The PlayerState message (MessageType.PlayerState, unreliable, about 30 a second, relayed by the host): the
/// sender's own clock when it was taken, which map the player is in, and the player (plain logic, tested).
/// Layout: [time f64][map key u32][PlayerState, 20 bytes]. The time is the sender's raw clock (package C: one
/// timeline per sender, AdaptiveDelay absorbs the clock offset). The map key tells the receiver whether the player is
/// in the same map; positions are relative to that map's origin.
/// </summary>
public static class PlayerStateMessage
{
    public static void Write(NetWriter w, double senderTime, uint mapKey, in PlayerState state)
    {
        w.WriteDouble(senderTime);
        w.WriteUInt32(mapKey);
        state.Write(w);
    }

    public static PlayerState Read(NetReader r, out double senderTime, out uint mapKey)
    {
        senderTime = r.ReadDouble();
        mapKey = r.ReadUInt32();
        return PlayerState.Read(r);
    }

    /// <summary>A map's key from its object name: FNV-1a 32 of the name without Unity's "(Clone)"; 0 = no map
    /// (loading, title screen). Both games load the same map prefab, so the names match.</summary>
    public static uint MapKey(string mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return 0;
        string name = mapName.EndsWith("(Clone)", System.StringComparison.Ordinal)
            ? mapName.Substring(0, mapName.Length - "(Clone)".Length).TrimEnd()
            : mapName;
        uint hash = 2166136261;
        foreach (char c in name)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash == 0 ? 1 : hash;
    }
}
