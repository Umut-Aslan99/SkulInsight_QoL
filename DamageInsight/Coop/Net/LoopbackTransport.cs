using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Net;

/// <summary>
/// Game copies connected in memory, for tests: every transport made from one <see cref="Hub"/> can reach the others.
/// Messages arrive at the receiver's next <see cref="Poll"/>; nothing is lost or reordered (the lag simulator adds
/// that on top).
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    /// <summary>The "network" the loopback transports share.</summary>
    public sealed class Hub
    {
        internal readonly Dictionary<PeerId, LoopbackTransport> Members = new();
        private ulong _next = 1;

        public LoopbackTransport Join() => new(this, new PeerId(_next++));

        /// <summary>Connects two members (both hear OnConnected at their next Poll).</summary>
        public void Connect(LoopbackTransport a, LoopbackTransport b)
        {
            a._events.Enqueue(new Event(EventKind.Connected, b.Self, default, Delivery.Reliable, null));
            b._events.Enqueue(new Event(EventKind.Connected, a.Self, default, Delivery.Reliable, null));
            a._peers.Add(b.Self);
            b._peers.Add(a.Self);
        }
    }

    private enum EventKind { Connected, Disconnected, Message }

    private readonly struct Event
    {
        public readonly EventKind Kind;
        public readonly PeerId Peer;
        public readonly byte[] Bytes;
        public readonly Delivery Delivery;
        public readonly string Reason;
        public Event(EventKind kind, PeerId peer, byte[] bytes, Delivery delivery, string reason)
        {
            Kind = kind;
            Peer = peer;
            Bytes = bytes;
            Delivery = delivery;
            Reason = reason;
        }
    }

    private readonly Hub _hub;
    private readonly Queue<Event> _events = new();
    private readonly HashSet<PeerId> _peers = new();
    private bool _disposed;

    private LoopbackTransport(Hub hub, PeerId self)
    {
        _hub = hub;
        Self = self;
        hub.Members[self] = this;
    }

    public PeerId Self { get; }

    public void Send(PeerId to, ArraySegment<byte> message, Delivery delivery)
    {
        if (_disposed || !_peers.Contains(to) || !_hub.Members.TryGetValue(to, out var target))
            return;
        var copy = new byte[message.Count];
        Buffer.BlockCopy(message.Array!, message.Offset, copy, 0, message.Count);
        target._events.Enqueue(new Event(EventKind.Message, Self, copy, delivery, null));
    }

    public void Poll(ITransportListener listener)
    {
        // Only what was queued before this call: a listener that answers at once is delivered at the next Poll.
        for (int n = _events.Count; n > 0; n--)
        {
            var e = _events.Dequeue();
            switch (e.Kind)
            {
                case EventKind.Connected: listener.OnConnected(e.Peer); break;
                case EventKind.Disconnected: listener.OnDisconnected(e.Peer, e.Reason); break;
                default: listener.OnMessage(e.Peer, new ArraySegment<byte>(e.Bytes), e.Delivery); break;
            }
        }
    }

    public void Disconnect(PeerId peer, string reason)
    {
        if (!_peers.Remove(peer))
            return;
        _events.Enqueue(new Event(EventKind.Disconnected, peer, null, Delivery.Reliable, reason));
        if (_hub.Members.TryGetValue(peer, out var other) && other._peers.Remove(Self))
            other._events.Enqueue(new Event(EventKind.Disconnected, Self, null, Delivery.Reliable, reason));
    }

    public ConnectionQuality QualityOf(PeerId peer) => ConnectionQuality.Unknown;

    public void Dispose()
    {
        if (_disposed)
            return;
        foreach (var peer in new List<PeerId>(_peers))
            Disconnect(peer, "closed");
        _hub.Members.Remove(Self);
        _disposed = true;
    }
}
