using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Net;

/// <summary>Steam's connection states (the values of ESteamNetworkingConnectionState), without Steamworks.</summary>
internal enum SteamConnState
{
    None = 0,
    Connecting = 1,
    FindingRoute = 2,
    Connected = 3,
    ClosedByPeer = 4,
    ProblemDetectedLocally = 5,
}

/// <summary>One SteamNetConnectionStatusChangedCallback_t, copied out of Steamworks.</summary>
internal readonly struct SteamStatusChange
{
    public readonly uint Connection;
    public readonly SteamConnState State;
    public readonly ulong Remote;
    /// <summary>true when the connection came in on our listen socket (someone connects to the host).</summary>
    public readonly bool Incoming;
    public readonly int EndReason;
    public readonly string EndDebug;

    public SteamStatusChange(uint connection, SteamConnState state, ulong remote, bool incoming, int endReason, string endDebug)
    {
        Connection = connection;
        State = state;
        Remote = remote;
        Incoming = incoming;
        EndReason = endReason;
        EndDebug = endDebug;
    }
}

/// <summary>The few Steam Networking Sockets calls the transport needs. The real one (<see cref="SteamworksSockets"/>,
/// SteamNet.cs) is the only code that touches Steamworks; tests use a fake.</summary>
internal interface ISteamSockets : IDisposable
{
    ulong OwnId { get; }
    /// <summary>Called from Steam's callbacks (the game runs them on the main thread in its Update).</summary>
    Action<SteamStatusChange> StatusChanged { set; }
    /// <summary>0 = failed.</summary>
    uint Listen(int virtualPort);
    /// <summary>0 = failed.</summary>
    uint Connect(ulong remote, int virtualPort);
    bool Accept(uint connection);
    void Close(uint connection, int reason, string debug, bool linger);
    void CloseListen(uint listenSocket);
    bool Send(uint connection, byte[] data, int count, bool reliable);
    /// <summary>The next waiting message, copied into buffer (grown when too small): its length, or -1 when none.</summary>
    int Receive(uint connection, ref byte[] buffer);
    bool Quality(uint connection, out int pingMs, out float lossPercent);
}

/// <summary>
/// Real co-op over the internet (docs/COOP_PLAN.md §5.2): Steam Networking Sockets P2P through Steam's relays, as
/// shipped with the game (Steamworks.NET 14.0 in Platforms.Steam.dll). The host opens a P2P listen socket on a virtual
/// port and accepts up to MaxClients; a client connects to the host's Steam id. Peer ids are Steam ids. Steam does
/// reliability, ordering, splitting and the connection quality itself; every message carries one byte in front (its
/// <see cref="Delivery"/>), so the receiver knows how it was sent without reading Steam's message flags.
/// Steam's status callbacks only queue events; <see cref="Poll"/> hands them over with the messages. Every Steam call
/// is guarded: without Steam, the transport just never connects (and says so through OnDisconnected).
/// </summary>
public sealed class SteamTransport : ITransport
{
    /// <summary>Close reasons (Steam's application range 1000-1999).</summary>
    private const int ReasonGeneric = 1000, ReasonFull = 1001;

    private sealed class Peer
    {
        public PeerId Id;
        public uint Connection;
        public bool Connected;
    }

    private readonly ISteamSockets _sockets;
    private readonly bool _isHost;
    private readonly int _maxClients;
    private readonly uint _listenSocket;
    private readonly PeerId _hostPeer;
    private readonly Dictionary<uint, Peer> _byConnection = new();
    private readonly Dictionary<PeerId, Peer> _byId = new();
    private readonly List<(bool connected, PeerId peer, string reason)> _events = new();
    private readonly List<Peer> _scratch = new();
    private byte[] _receive = new byte[4096], _send = new byte[4096];
    private bool _disposed;

    /// <summary>Opens a P2P listen socket; others connect with <see cref="Join"/> and this game's Steam id.</summary>
    public static SteamTransport Host(int virtualPort = 0, int maxClients = 3) =>
        new(SteamworksSockets.TryCreate(), true, 0, virtualPort, maxClients);

    /// <summary>Connects to the host's Steam id; OnConnected(host's Steam id) once Steam made the route.</summary>
    public static SteamTransport Join(ulong hostSteamId, int virtualPort = 0) =>
        new(SteamworksSockets.TryCreate(), false, hostSteamId, virtualPort, 1);

    /// <summary>Starts Steam's relay access and ping measurement early, so the first connection (and the ping
    /// estimates for matchmaking) come faster. Harmless without Steam.</summary>
    public static void WarmUp() => SteamworksSockets.WarmUp();

    internal SteamTransport(ISteamSockets sockets, bool host, ulong hostSteamId, int virtualPort, int maxClients)
    {
        _sockets = sockets;
        _isHost = host;
        _maxClients = maxClients;
        if (sockets == null)
        {
            Self = PeerId.None;
            _events.Add((false, new PeerId(hostSteamId), "Steam is not running"));
            return;
        }
        sockets.StatusChanged = OnStatus;
        Self = new PeerId(sockets.OwnId);
        if (host)
        {
            _hostPeer = Self;
            _listenSocket = sockets.Listen(virtualPort);
            if (_listenSocket == 0)
                Failed = "Steam could not open a listen socket";
            return;
        }
        _hostPeer = new PeerId(hostSteamId);
        uint connection = sockets.Connect(hostSteamId, virtualPort);
        if (connection == 0)
        {
            _events.Add((false, _hostPeer, "Steam could not start the connection"));
            return;
        }
        Track(new Peer { Id = _hostPeer, Connection = connection });
    }

    public PeerId Self { get; }
    public bool IsHost => _isHost;
    /// <summary>The host's Steam id (this game's own on the host).</summary>
    public PeerId HostPeer => _hostPeer;
    /// <summary>Why the host could not start (null = fine).</summary>
    public string Failed { get; }
    public int PeerCount
    {
        get
        {
            int n = 0;
            foreach (var p in _byId.Values)
                if (p.Connected)
                    n++;
            return n;
        }
    }

    private void Track(Peer peer)
    {
        _byConnection[peer.Connection] = peer;
        _byId[peer.Id] = peer;
    }

    private void Forget(Peer peer)
    {
        _byConnection.Remove(peer.Connection);
        if (_byId.TryGetValue(peer.Id, out var same) && same == peer)
            _byId.Remove(peer.Id);
    }

    private void OnStatus(SteamStatusChange change)
    {
        if (_disposed)
            return;
        _byConnection.TryGetValue(change.Connection, out var peer);
        switch (change.State)
        {
            case SteamConnState.Connecting:
                if (!_isHost || !change.Incoming || peer != null)
                    return;
                var id = new PeerId(change.Remote);
                if (_byId.ContainsKey(id) || _byId.Count >= _maxClients)
                {
                    _sockets.Close(change.Connection, ReasonFull, "the host is full", false);
                    return;
                }
                if (!_sockets.Accept(change.Connection))
                {
                    _sockets.Close(change.Connection, ReasonGeneric, "accept failed", false);
                    return;
                }
                Track(new Peer { Id = id, Connection = change.Connection });
                return;
            case SteamConnState.Connected:
                if (peer == null || peer.Connected)
                    return;
                peer.Connected = true;
                _events.Add((true, peer.Id, null));
                return;
            case SteamConnState.ClosedByPeer:
            case SteamConnState.ProblemDetectedLocally:
                // The handle must be closed by us too, or Steam keeps it.
                _sockets.Close(change.Connection, 0, null, false);
                if (peer == null)
                    return;
                Forget(peer);
                string why = string.IsNullOrEmpty(change.EndDebug) ? "end reason " + change.EndReason : change.EndDebug;
                if (peer.Connected)
                    _events.Add((false, peer.Id, change.State == SteamConnState.ClosedByPeer ? why : "connection problem: " + why));
                else if (!_isHost)
                    _events.Add((false, peer.Id, "could not connect: " + why));
                return;
        }
    }

    public void Send(PeerId to, ArraySegment<byte> message, Delivery delivery)
    {
        if (_disposed || !_byId.TryGetValue(to, out var peer) || !peer.Connected)
            return;
        int n = message.Count + 1;
        if (_send.Length < n)
            _send = new byte[Math.Max(n, _send.Length * 2)];
        _send[0] = (byte)delivery;
        Buffer.BlockCopy(message.Array!, message.Offset, _send, 1, message.Count);
        _sockets.Send(peer.Connection, _send, n, delivery == Delivery.Reliable);
    }

    public void Poll(ITransportListener listener)
    {
        if (_events.Count > 0)
        {
            var events = _events.ToArray();
            _events.Clear();
            foreach (var (connected, peer, reason) in events)
            {
                if (connected)
                    listener.OnConnected(peer);
                else
                    listener.OnDisconnected(peer, reason);
            }
        }
        if (_disposed || _sockets == null)
            return;
        _scratch.Clear();
        _scratch.AddRange(_byId.Values);
        foreach (var peer in _scratch)
        {
            for (int guard = 0; guard < 4096 && peer.Connected; guard++)
            {
                int n = _sockets.Receive(peer.Connection, ref _receive);
                if (n < 0)
                    break;
                if (n == 0)
                    continue;
                var delivery = _receive[0] == (byte)Delivery.Reliable ? Delivery.Reliable : Delivery.Unreliable;
                listener.OnMessage(peer.Id, new ArraySegment<byte>(_receive, 1, n - 1), delivery);
            }
        }
    }

    public void Disconnect(PeerId peer, string reason)
    {
        if (!_byId.TryGetValue(peer, out var p))
            return;
        // Linger: reliable messages still on the way are delivered before the connection closes.
        _sockets.Close(p.Connection, ReasonGeneric, reason, true);
        Forget(p);
        _events.Add((false, peer, reason));
    }

    public ConnectionQuality QualityOf(PeerId peer)
    {
        if (!_byId.TryGetValue(peer, out var p) || !p.Connected || !_sockets.Quality(p.Connection, out int ping, out float loss))
            return ConnectionQuality.Unknown;
        return new ConnectionQuality(ping, loss);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_sockets != null)
        {
            foreach (var p in new List<Peer>(_byId.Values))
                _sockets.Close(p.Connection, ReasonGeneric, "closed", true);
            if (_listenSocket != 0)
                _sockets.CloseListen(_listenSocket);
            _sockets.Dispose();
        }
        _byId.Clear();
        _byConnection.Clear();
        _disposed = true;
    }
}
