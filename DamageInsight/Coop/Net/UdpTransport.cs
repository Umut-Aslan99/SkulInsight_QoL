using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DamageInsight.Coop.Net;

/// <summary>Settings of a <see cref="UdpTransport"/>; Port and the lag options come from the start options.</summary>
public sealed class UdpTransportOptions
{
    /// <summary>The host's address: the one it binds, and the one clients send to.</summary>
    public string Address { get; set; } = "127.0.0.1";
    /// <summary>Host: the port it binds (0 = any free one, see <see cref="UdpTransport.BoundPort"/>); client: the host's.</summary>
    public int Port { get; set; } = UdpTransport.DefaultPort;
    public int MaxClients { get; set; } = 3;
    /// <summary>A client gives up connecting after this long (and hears OnDisconnected without OnConnected).</summary>
    public double ConnectTimeoutSeconds { get; set; } = 10;
    public double ConnectRetrySeconds { get; set; } = 0.25;
    /// <summary>A peer silent this long is dropped (§5.7: 20 s).</summary>
    public double TimeoutSeconds { get; set; } = 20;
    public double KeepAliveSeconds { get; set; } = 0.25;
    public int Mtu { get; set; } = 1200;
    /// <summary>true: every Send goes out at once (lowest delay); false: sends wait for Poll or <see cref="UdpTransport.Flush"/>
    /// and share datagrams.</summary>
    public bool FlushEachSend { get; set; } = true;
    /// <summary>Seconds; tests inject their own. Default: a stopwatch.</summary>
    public Func<double> Clock { get; set; }
}

public enum UdpState { Connecting, Connected, Closed }

/// <summary>
/// Co-op between game copies on one PC or a LAN (docs/COOP_PLAN.md §5.2, §9) over UDP: the host binds a port
/// (127.0.0.1:27960 by default), clients connect to it with a small handshake (a request with a random number, the
/// host's answer with the client's peer id and a connection token; packets without the right token are ignored), and
/// every connection runs a <see cref="ReliableChannel"/>. Peer ids: the host is 1, clients get 2, 3, 4 (the lowest free).
/// Non-blocking sockets read in <see cref="Poll"/> on the main thread: no threads.
///
/// Packets: A1 connect request ("SKCP", version, u32 nonce) | A2 accept (u32 nonce, u32 token, u8 peer id) |
/// A3 reject (u32 nonce, u8 reason) | A4 data (u32 token, channel datagram) | A5 disconnect (u32 token, UTF-8 reason).
/// </summary>
public sealed class UdpTransport : ITransport
{
    public const int DefaultPort = 27960;
    public static readonly PeerId HostPeer = new(1);

    private const byte Request = 0xA1, Accept = 0xA2, Reject = 0xA3, Data = 0xA4, Bye = 0xA5;
    private const byte Version = 1;
    private const int HeaderRoom = 5;

    private sealed class Peer
    {
        public PeerId Id;
        public EndPoint EndPoint;
        public uint Token, Nonce;
        public ReliableChannel Channel;
        public bool Connected;
    }

    private readonly UdpTransportOptions _options;
    private readonly Func<double> _clock;
    private readonly Socket _socket;
    private readonly bool _isHost;
    private readonly byte[] _receive = new byte[65536], _control = new byte[256];
    private readonly Random _random = new(Guid.NewGuid().GetHashCode());
    private readonly EndPoint _anyEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private readonly Dictionary<EndPoint, Peer> _byEndPoint = new();
    private readonly Dictionary<PeerId, Peer> _byId = new();
    private readonly List<Peer> _scratch = new();
    private readonly List<(PeerId peer, string reason)> _pendingDisconnects = new();
    private ITransportListener _listener;

    // Client only.
    private readonly EndPoint _hostEndPoint;
    private readonly uint _nonce;
    private readonly double _connectStarted;
    private double _nextRequest;
    private bool _disposed;

    private UdpTransport(UdpTransportOptions options, bool host)
    {
        _options = options ?? new UdpTransportOptions();
        if (_options.Clock != null)
        {
            _clock = _options.Clock;
        }
        else
        {
            var watch = Stopwatch.StartNew();
            _clock = () => watch.Elapsed.TotalSeconds;
        }
        _isHost = host;
        var address = Resolve(_options.Address);
        _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        try
        {
            // Windows: an ICMP "port unreachable" (a peer that closed) would make the next ReceiveFrom throw.
            const int SioUdpConnReset = -1744830452;
            _socket.IOControl(SioUdpConnReset, new byte[4], null);
        }
        catch (Exception)
        {
            // Not Windows, or not supported: the receive loop skips those errors anyway.
        }
        try
        {
            _socket.ReceiveBufferSize = 1 << 20; // a frame hitch of a second must not overflow it
            _socket.SendBufferSize = 1 << 20;
        }
        catch (SocketException)
        {
        }
        if (host)
        {
            _socket.Bind(new IPEndPoint(address, _options.Port));
            State = UdpState.Connected;
            Self = HostPeer;
        }
        else
        {
            _socket.Bind(new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            _hostEndPoint = new IPEndPoint(address, _options.Port);
            do
                _nonce = (uint)_random.Next() ^ (uint)_random.Next() << 16;
            while (_nonce == 0);
            _connectStarted = _clock();
            State = UdpState.Connecting;
            Self = PeerId.None;
            SendRequest();
        }
    }

    /// <summary>Starts a host on options.Address:options.Port (throws SocketException when the port is taken).</summary>
    public static UdpTransport Host(UdpTransportOptions options) => new(options, true);

    /// <summary>Starts connecting to the host at options.Address:options.Port; OnConnected(HostPeer) once accepted.</summary>
    public static UdpTransport Join(UdpTransportOptions options) => new(options, false);

    public PeerId Self { get; private set; }
    public bool IsHost => _isHost;
    public UdpState State { get; private set; }
    public int BoundPort => ((IPEndPoint)_socket.LocalEndPoint).Port;
    public int PeerCount => _byId.Count;

    private static IPAddress Resolve(string address)
    {
        if (IPAddress.TryParse(address, out var ip))
            return ip;
        foreach (var a in Dns.GetHostAddresses(address))
            if (a.AddressFamily == AddressFamily.InterNetwork)
                return a;
        throw new ArgumentException("no IPv4 address for " + address);
    }

    public void Send(PeerId to, ArraySegment<byte> message, Delivery delivery)
    {
        if (_disposed || !_byId.TryGetValue(to, out var peer) || !peer.Connected)
            return;
        peer.Channel.Send(message, delivery);
        if (_options.FlushEachSend)
            peer.Channel.Flush();
    }

    /// <summary>Sends what waits in every connection (only needed with FlushEachSend = false; Poll does it too).</summary>
    public void Flush()
    {
        foreach (var peer in _byId.Values)
            peer.Channel.Flush();
    }

    public void Poll(ITransportListener listener)
    {
        if (_disposed)
            return;
        _listener = listener;
        try
        {
            if (_pendingDisconnects.Count > 0)
            {
                var pending = _pendingDisconnects.ToArray();
                _pendingDisconnects.Clear();
                foreach (var (peer, reason) in pending)
                    listener.OnDisconnected(peer, reason);
            }
            ReceiveAll();
            double now = _clock();
            if (State == UdpState.Connecting)
            {
                if (now - _connectStarted >= _options.ConnectTimeoutSeconds)
                {
                    State = UdpState.Closed;
                    listener.OnDisconnected(HostPeer, "could not connect to the host");
                }
                else if (now >= _nextRequest)
                {
                    SendRequest();
                }
            }
            _scratch.Clear();
            _scratch.AddRange(_byId.Values);
            foreach (var peer in _scratch)
            {
                peer.Channel.Update();
                if (peer.Channel.IsDead && peer.Connected)
                    Drop(peer, "timeout", true);
            }
        }
        finally
        {
            _listener = null;
        }
    }

    public void Disconnect(PeerId peer, string reason)
    {
        if (_byId.TryGetValue(peer, out var p))
        {
            Drop(p, reason, false);
        }
        else if (!_isHost && State == UdpState.Connecting && peer == HostPeer)
        {
            State = UdpState.Closed; // gives up connecting
            _pendingDisconnects.Add((HostPeer, reason));
        }
    }

    public ConnectionQuality QualityOf(PeerId peer) =>
        _byId.TryGetValue(peer, out var p) ? p.Channel.Quality : ConnectionQuality.Unknown;

    public void Dispose()
    {
        if (_disposed)
            return;
        foreach (var peer in new List<Peer>(_byId.Values))
            Drop(peer, "closed", false);
        _pendingDisconnects.Clear();
        _disposed = true;
        try
        {
            _socket.Close();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Ends a connection: tells the peer (twice, in case one is lost) and the listener (now inside Poll, else
    /// at the next Poll).</summary>
    private void Drop(Peer peer, string reason, bool insidePoll)
    {
        int n = 0;
        _control[n++] = Bye;
        TransportBytes.WriteU32(_control, n, peer.Token);
        n += 4;
        var text = Encoding.UTF8.GetBytes(reason ?? "");
        int len = Math.Min(text.Length, 200);
        Buffer.BlockCopy(text, 0, _control, n, len);
        n += len;
        SendRaw(_control, n, peer.EndPoint);
        SendRaw(_control, n, peer.EndPoint);
        Forget(peer);
        if (!_isHost)
            State = UdpState.Closed;
        if (insidePoll && _listener != null)
            _listener.OnDisconnected(peer.Id, reason);
        else
            _pendingDisconnects.Add((peer.Id, reason));
    }

    private void Forget(Peer peer)
    {
        peer.Connected = false;
        _byId.Remove(peer.Id);
        _byEndPoint.Remove(peer.EndPoint);
    }

    private void ReceiveAll()
    {
        for (int guard = 0; guard < 10000; guard++)
        {
            EndPoint from = _anyEndPoint;
            int n;
            try
            {
                if (_socket.Available <= 0)
                    return;
                n = _socket.ReceiveFrom(_receive, 0, _receive.Length, SocketFlags.None, ref from);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                return;
            }
            catch (SocketException)
            {
                continue; // a reset from a closed peer, a too-big datagram: skip it
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (n > 0)
                Handle(from, n);
        }
    }

    private void Handle(EndPoint from, int n)
    {
        byte type = _receive[0];
        _byEndPoint.TryGetValue(from, out var peer);
        if (type == Data || type == Bye)
        {
            if (n < HeaderRoom || peer == null || !peer.Connected || TransportBytes.ReadU32(_receive, 1) != peer.Token)
                return;
            if (type == Data)
            {
                peer.Channel.Receive(new ArraySegment<byte>(_receive, HeaderRoom, n - HeaderRoom));
                return;
            }
            Forget(peer);
            if (!_isHost)
                State = UdpState.Closed;
            _listener.OnDisconnected(peer.Id, Encoding.UTF8.GetString(_receive, HeaderRoom, n - HeaderRoom));
            return;
        }
        if (_isHost)
        {
            if (type != Request || n < 10 || _receive[1] != 'S' || _receive[2] != 'K' || _receive[3] != 'C' ||
                _receive[4] != 'P' || _receive[5] != Version)
                return;
            uint nonce = TransportBytes.ReadU32(_receive, 6);
            if (peer != null && peer.Nonce == nonce)
            {
                SendAccept(peer); // our answer was lost
                return;
            }
            if (peer != null)
            {
                Forget(peer); // the same address restarted
                _listener.OnDisconnected(peer.Id, "reconnected");
            }
            var id = FreeId();
            if (id.IsNone)
            {
                _control[0] = Reject;
                TransportBytes.WriteU32(_control, 1, nonce);
                _control[5] = 1; // full
                SendRaw(_control, 6, from);
                return;
            }
            uint token;
            do
                token = (uint)_random.Next() ^ (uint)_random.Next() << 16;
            while (token == 0);
            peer = NewPeer(id, from, token, nonce);
            SendAccept(peer);
            _listener.OnConnected(id);
            return;
        }
        if (State != UdpState.Connecting || n < 6 || !from.Equals(_hostEndPoint) || TransportBytes.ReadU32(_receive, 1) != _nonce)
            return;
        if (type == Accept && n >= 10)
        {
            Self = new PeerId(_receive[9]);
            State = UdpState.Connected;
            NewPeer(HostPeer, from, TransportBytes.ReadU32(_receive, 5), _nonce);
            _listener.OnConnected(HostPeer);
        }
        else if (type == Reject)
        {
            State = UdpState.Closed;
            _listener.OnDisconnected(HostPeer, _receive[5] == 1 ? "the host is full" : "the host refused");
        }
    }

    private PeerId FreeId()
    {
        for (ulong id = 2; id < 2 + (ulong)Math.Max(0, _options.MaxClients); id++)
            if (!_byId.ContainsKey(new PeerId(id)))
                return new PeerId(id);
        return PeerId.None;
    }

    private Peer NewPeer(PeerId id, EndPoint endPoint, uint token, uint nonce)
    {
        var peer = new Peer { Id = id, EndPoint = endPoint, Token = token, Nonce = nonce, Connected = true };
        peer.Channel = new ReliableChannel(_clock, d => SendData(peer, d), (m, d) =>
            {
                if (peer.Connected)
                    _listener?.OnMessage(peer.Id, m, d);
            },
            new ReliableChannelOptions
            {
                Mtu = _options.Mtu,
                HeaderRoom = HeaderRoom,
                KeepAliveSeconds = _options.KeepAliveSeconds,
                DeadAfterSeconds = _options.TimeoutSeconds,
            });
        _byId[id] = peer;
        _byEndPoint[endPoint] = peer;
        return peer;
    }

    private void SendRequest()
    {
        _nextRequest = _clock() + _options.ConnectRetrySeconds;
        _control[0] = Request;
        _control[1] = (byte)'S';
        _control[2] = (byte)'K';
        _control[3] = (byte)'C';
        _control[4] = (byte)'P';
        _control[5] = Version;
        TransportBytes.WriteU32(_control, 6, _nonce);
        SendRaw(_control, 10, _hostEndPoint);
    }

    private void SendAccept(Peer peer)
    {
        _control[0] = Accept;
        TransportBytes.WriteU32(_control, 1, peer.Nonce);
        TransportBytes.WriteU32(_control, 5, peer.Token);
        _control[9] = (byte)peer.Id.Value;
        SendRaw(_control, 10, peer.EndPoint);
    }

    private void SendData(Peer peer, ArraySegment<byte> datagram)
    {
        // The channel left HeaderRoom bytes free at the front of its datagram.
        var b = datagram.Array!;
        b[datagram.Offset] = Data;
        TransportBytes.WriteU32(b, datagram.Offset + 1, peer.Token);
        SendRaw(b, datagram.Offset, datagram.Count, peer.EndPoint);
    }

    private void SendRaw(byte[] bytes, int count, EndPoint to) => SendRaw(bytes, 0, count, to);

    private void SendRaw(byte[] bytes, int offset, int count, EndPoint to)
    {
        if (_disposed)
            return;
        try
        {
            _socket.SendTo(bytes, offset, count, SocketFlags.None, to);
        }
        catch (SocketException)
        {
            // A full send buffer or an unreachable peer: like a lost datagram (reliable messages are resent).
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
