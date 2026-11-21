using System;

namespace DamageInsight.Coop.Net;

/// <summary>
/// The contract every co-op connection is built on (docs/COOP_PLAN.md §5.2). A transport moves byte messages between
/// this game and its peers and says who connected or left; it knows nothing about Skul. Implementations: the
/// in-memory <see cref="LoopbackTransport"/> (tests), UDP on this PC (several game copies), Steam Networking Sockets
/// (real play), and the lag simulator that wraps any of them. Plain C#, no Unity: everything above it is testable.
/// All calls happen on the game's main thread; <see cref="Poll"/> delivers what arrived since the last call.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>This game's own id on the transport (a Steam id, or a number for local copies).</summary>
    PeerId Self { get; }

    /// <summary>Sends one message. Reliable messages arrive once, in the order they were sent (per peer);
    /// unreliable ones may be lost or come late, never duplicated. The bytes are copied before the call returns.</summary>
    void Send(PeerId to, ArraySegment<byte> message, Delivery delivery);

    /// <summary>Delivers connects, disconnects and messages that arrived since the last call, in arrival order.</summary>
    void Poll(ITransportListener listener);

    /// <summary>Closes the connection to one peer (the listener hears OnDisconnected for it).</summary>
    void Disconnect(PeerId peer, string reason);

    /// <summary>The transport's own view of the connection (Steam measures it; the others leave it unknown).</summary>
    ConnectionQuality QualityOf(PeerId peer);
}

/// <summary>What a transport hands to the co-op session.</summary>
public interface ITransportListener
{
    void OnConnected(PeerId peer);
    void OnDisconnected(PeerId peer, string reason);
    /// <summary>The bytes are only valid during the call: copy what you keep.</summary>
    void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery);
}

public enum Delivery : byte
{
    /// <summary>May be lost; for state that is sent again soon anyway (positions, snapshots).</summary>
    Unreliable = 0,
    /// <summary>Arrives once and in order; for events (an enemy died, a door vote, gear changed).</summary>
    Reliable = 1,
}

/// <summary>A peer on a transport: a Steam id, or a small number for game copies on one PC.</summary>
public readonly struct PeerId : IEquatable<PeerId>
{
    public readonly ulong Value;
    public PeerId(ulong value) => Value = value;
    public static readonly PeerId None = new(0);
    public bool IsNone => Value == 0;
    public bool Equals(PeerId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is PeerId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(PeerId a, PeerId b) => a.Value == b.Value;
    public static bool operator !=(PeerId a, PeerId b) => a.Value != b.Value;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Ping and loss as a transport measures them; -1 = unknown (the session measures its own ping anyway).</summary>
public readonly struct ConnectionQuality
{
    public readonly float PingMs, LossPercent;
    public ConnectionQuality(float pingMs, float lossPercent)
    {
        PingMs = pingMs;
        LossPercent = lossPercent;
    }
    public static readonly ConnectionQuality Unknown = new(-1, -1);
}
