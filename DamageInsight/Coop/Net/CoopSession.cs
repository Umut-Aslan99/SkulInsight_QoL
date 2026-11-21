using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace DamageInsight.Coop.Net;

/// <summary>
/// A co-op session of up to four games over one <see cref="ITransport"/> (docs/COOP_PLAN.md §5): the host and up to
/// three clients, each client connected to the host only. It does the handshake (a client's Hello with its versions,
/// mod list, name and player id; the host's Welcome with a slot and the roster, or Reject with a reason), keeps the
/// roster (slot 0 is the host), pings every peer twice a second for ping and clock offset (<see cref="NetClock"/>),
/// drops a peer that stays silent, says goodbye, and passes game messages (ids from <see cref="Protocol.FirstGameMessage"/>)
/// to the handler registered for their type. A client reaches the other clients through the host: the host relays
/// the types marked with <see cref="SetRelay"/>. Driven by <see cref="Update"/> once a frame with the game's time in
/// seconds; all events are raised from inside Update (or Leave/Kick). Plain C#, no Unity.
/// </summary>
public sealed class CoopSession : ITransportListener
{
    /// <summary>One connection: on the host every client (and a game that hasn't said hello yet), on a client the host.</summary>
    private sealed class Peer
    {
        public readonly PeerId Id;
        public readonly NetClock Clock;
        public double LastHeard, NextPing, CloseAt, WelcomedAt;
        public RosterEntry Player; // the host's side: set once welcomed
        public bool Closing;       // rejected or kicked: kept a moment so the reason arrives, then disconnected
        public string CloseReason;

        public Peer(PeerId id, int window, double now)
        {
            Id = id;
            Clock = new NetClock(window);
            LastHeard = now;
        }
    }

    private readonly ITransport _transport;
    private readonly PeerId _hostId;
    private readonly int _maxPlayers;
    private readonly Dictionary<PeerId, Peer> _peers = new();
    private readonly Peer[] _slotPeers = new Peer[Protocol.MaxPlayers]; // the host: joined clients; a client: [0] = the host
    private readonly RosterEntry[] _slots = new RosterEntry[Protocol.MaxPlayers];
    private readonly RosterEntry[] _incoming = new RosterEntry[Protocol.MaxPlayers];
    private readonly List<RosterEntry> _roster = new(Protocol.MaxPlayers);
    private readonly List<RosterEntry> _left = new(Protocol.MaxPlayers), _joined = new(Protocol.MaxPlayers);
    private readonly List<Peer> _scratch = new();
    private readonly GameMessageHandler[] _handlers = new GameMessageHandler[256];
    private readonly bool[] _relay = new bool[256];
    private readonly HashSet<ulong> _banned = new();
    // The host's side (package DC): a slot whose player dropped is kept for them until then (their id, the time).
    private readonly ulong[] _reservedId = new ulong[Protocol.MaxPlayers];
    private readonly double[] _reservedUntil = new double[Protocol.MaxPlayers];
    private readonly NetWriter _send = new(256);
    private readonly NetReader _reader = new();
    private readonly Peer _host; // a client's connection to the host
    private double _now;

    private CoopSession(ITransport transport, SessionOptions options, bool isHost, PeerId host, double now)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        CheckLength(options.ModVersion, Protocol.MaxVersionBytes, nameof(options.ModVersion));
        CheckLength(options.ModListHash, Protocol.MaxVersionBytes, nameof(options.ModListHash));
        IsHost = isHost;
        _hostId = host;
        _maxPlayers = Math.Max(1, Math.Min(Protocol.MaxPlayers, options.MaxPlayers));
        _now = now;
        Roster = new ReadOnlyCollection<RosterEntry>(_roster);
        if (!isHost)
        {
            _host = new Peer(host, options.ClockWindow, now);
            _peers[host] = _host;
            _slotPeers[0] = _host;
        }
    }

    /// <summary>Opens a session as the host (slot 0): clients that connect to the transport can join at once.</summary>
    public static CoopSession Host(ITransport transport, SessionOptions options, double now)
    {
        var session = new CoopSession(transport, options, true, PeerId.None, now);
        session.State = SessionState.Active;
        session.LocalSlot = 0;
        session._slots[0] = new RosterEntry(0, options.PlayerId, Protocol.CleanName(options.PlayerName, 0), transport.Self);
        session.RebuildRoster();
        return session;
    }

    /// <summary>Joins the host at <paramref name="host"/>: Hello goes out now (if the transport is connected already)
    /// and again when the transport says it connected; <see cref="Started"/> comes with the Welcome.</summary>
    public static CoopSession Join(ITransport transport, PeerId host, SessionOptions options, double now)
    {
        var session = new CoopSession(transport, options, false, host, now);
        session.SendHello();
        return session;
    }

    public SessionOptions Options { get; }
    public bool IsHost { get; }
    public SessionState State { get; private set; } = SessionState.Connecting;

    /// <summary>This game's slot (0 = host, 1-3 = clients), -1 until a client is welcomed.</summary>
    public int LocalSlot { get; private set; } = -1;

    public RosterEntry LocalPlayer => LocalSlot >= 0 ? _slots[LocalSlot] : null;

    /// <summary>The players by slot, this game included (kept as it was when the session ended).</summary>
    public IReadOnlyList<RosterEntry> Roster { get; }

    /// <summary>The player in a slot, or null.</summary>
    public RosterEntry PlayerAt(int slot) => (uint)slot < Protocol.MaxPlayers ? _slots[slot] : null;

    /// <summary>The host's side: whether new players may join (false = Reject NotAccepting).</summary>
    public bool AcceptingPlayers { get; set; } = true;

    /// <summary>The host's side: who may join (the peer, the player id from its Hello); null = anyone. A matchmaking
    /// session lets in only the matched players (their Steam ids), everyone else gets Reject NotAccepting.</summary>
    public Func<PeerId, ulong, bool> Admit { get; set; }

    public LeaveReason EndReason { get; private set; }

    /// <summary>A client's side after <see cref="LeaveReason.Rejected"/>: why, and the host's versions.</summary>
    public RejectReason Rejection { get; private set; }
    public ushort HostProtocolVersion { get; private set; }
    public string HostModVersion { get; private set; } = "";

    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }

    /// <summary>Messages dropped because they were broken (or claimed another sender).</summary>
    public int MalformedMessages { get; private set; }

    /// <summary>Game messages whose handler threw (the session carries on).</summary>
    public int HandlerErrors { get; private set; }

    /// <summary>A client was welcomed (the host's session is active from the start and never raises it).</summary>
    public event Action Started;

    /// <summary>Another player is in the session: on the host when one is welcomed, on a client for everyone already
    /// there at the Welcome and everyone who joins later. Never for this game's own player.</summary>
    public event Action<RosterEntry> PlayerJoined;

    /// <summary>Another player left (not raised when the whole session ends: see <see cref="SessionEnded"/>).</summary>
    public event Action<RosterEntry, LeaveReason> PlayerLeft;

    public event Action<LeaveReason> SessionEnded;

    /// <summary>Plain English lines for the log (joins, rejects, timeouts, broken messages).</summary>
    public event Action<string> Log;

    // ---- the frame ----------------------------------------------------------------------------------------------

    /// <summary>Takes what the transport received, pings, and drops peers that were silent too long.</summary>
    public void Update(double now)
    {
        if (State == SessionState.Ended)
            return;
        _now = now;
        _transport.Poll(this);
        if (State == SessionState.Ended)
            return;

        _scratch.Clear();
        foreach (var p in _peers.Values)
        {
            if (p.Closing)
            {
                if (now >= p.CloseAt)
                    _scratch.Add(p);
                continue;
            }
            bool handshake = IsHost ? p.Player == null : State == SessionState.Connecting;
            if (now - p.LastHeard > (handshake ? HandshakeTimeout : Options.TimeoutSeconds))
            {
                _scratch.Add(p);
                continue;
            }
            bool joined = IsHost ? p.Player != null : State == SessionState.Active;
            if (joined && now >= p.NextPing)
            {
                SendPing(p);
                p.NextPing = now + Options.PingIntervalSeconds;
            }
        }
        for (int i = 0; i < _scratch.Count && State != SessionState.Ended; i++)
        {
            var p = _scratch[i];
            if (p.Closing)
            {
                _peers.Remove(p.Id);
                _transport.Disconnect(p.Id, p.CloseReason);
            }
            else
                Drop(p, LeaveReason.Timeout, FormattableString.Invariant($"nothing heard for {now - p.LastHeard:0.#} s"));
        }
    }

    private double HandshakeTimeout => Options.HandshakeTimeoutSeconds > 0 ? Options.HandshakeTimeoutSeconds : Options.TimeoutSeconds;

    // ---- rejoin (package DC) ------------------------------------------------------------------------------------

    /// <summary>The host's side: the slot is kept for its player who dropped (until <see cref="ReservedUntil"/>).</summary>
    public bool IsReserved(int slot) => (uint)slot < Protocol.MaxPlayers && _reservedId[slot] != 0 && _now < _reservedUntil[slot];

    public double ReservedUntil(int slot) => IsReserved(slot) ? _reservedUntil[slot] : 0;

    /// <summary>The host's side: the slot kept for this player id, or -1.</summary>
    private int ReservedSlotOf(ulong playerId)
    {
        for (int s = 1; s < Protocol.MaxPlayers; s++)
            if (_reservedId[s] == playerId && IsReserved(s))
                return s;
        return -1;
    }

    /// <summary>The host's side: the slot is no longer kept (the player came back, or gave up: a new game may take it).</summary>
    public void Release(int slot)
    {
        if ((uint)slot >= Protocol.MaxPlayers)
            return;
        _reservedId[slot] = 0;
        _reservedUntil[slot] = 0;
    }

    /// <summary>Ends the session on purpose: a client says goodbye to the host; the host says goodbye to every client
    /// (and turns away games still in the handshake). Reliable messages sent before a disconnect must be delivered by
    /// the transport (Steam: linger).</summary>
    public void Leave()
    {
        if (State == SessionState.Ended)
            return;
        _scratch.Clear();
        _scratch.AddRange(_peers.Values);
        foreach (var p in _scratch)
        {
            if (p.Closing)
                continue;
            if (!IsHost)
                WriteGoodbye(LeaveReason.Goodbye);
            else if (p.Player != null)
                WriteGoodbye(LeaveReason.HostLeft);
            else
                WriteReject(RejectReason.HostLeaving);
            SendRaw(p, _send.ToSegment(), Delivery.Reliable);
        }
        End(LeaveReason.LocalLeave, "left");
    }

    /// <summary>The host's side: removes a client (it hears Kicked); with <paramref name="ban"/> its player id can't
    /// join again (Reject Kicked).</summary>
    public bool Kick(int slot, bool ban = false)
    {
        if (!IsHost || State != SessionState.Active || slot <= 0 || slot >= Protocol.MaxPlayers || _slotPeers[slot] == null)
            return false;
        var p = _slotPeers[slot];
        if (ban)
            _banned.Add(p.Player.PlayerId);
        WriteGoodbye(LeaveReason.Kicked);
        SendRaw(p, _send.ToSegment(), Delivery.Reliable);
        Close(p, ban ? "kicked and banned" : "kicked");
        RemovePlayer(p, LeaveReason.Kicked);
        return true;
    }

    // ---- game messages ------------------------------------------------------------------------------------------

    /// <summary>Who handles a game message type (one handler per type; null removes it). The reader is only valid
    /// during the call.</summary>
    public void RegisterHandler(MessageType type, GameMessageHandler handler)
    {
        CheckGameType(type);
        _handlers[(byte)type] = handler;
    }

    public void UnregisterHandler(MessageType type) => RegisterHandler(type, null);

    /// <summary>The host's side: messages of this type from a client also go to the other clients (unchanged, same
    /// delivery), e.g. player states. Set it the same on every game; it only matters on the host.</summary>
    public void SetRelay(MessageType type, bool relay = true)
    {
        CheckGameType(type);
        _relay[(byte)type] = relay;
    }

    /// <summary>Sends a game message to one slot: the host to any client, a client only to the host (slot 0).
    /// False when there is nobody to send to.</summary>
    public bool Send(int toSlot, MessageType type, NetWriter payload, Delivery delivery) =>
        Send(toSlot, type, payload == null ? default : payload.ToSegment(), delivery);

    public bool Send(int toSlot, MessageType type, ArraySegment<byte> payload, Delivery delivery)
    {
        CheckGameType(type);
        if (State != SessionState.Active || toSlot == LocalSlot || (uint)toSlot >= Protocol.MaxPlayers)
            return false;
        var p = IsHost ? _slotPeers[toSlot] : toSlot == 0 ? _host : null;
        if (p == null)
            return false;
        SendRaw(p, Frame(type, payload), delivery);
        return true;
    }

    /// <summary>The host to every client, a client to the host (which relays it if the type is marked). Returns
    /// how many peers it went to.</summary>
    public int Broadcast(MessageType type, NetWriter payload, Delivery delivery) =>
        Broadcast(type, payload == null ? default : payload.ToSegment(), delivery);

    public int Broadcast(MessageType type, ArraySegment<byte> payload, Delivery delivery)
    {
        CheckGameType(type);
        if (State != SessionState.Active)
            return 0;
        var frame = Frame(type, payload);
        if (!IsHost)
        {
            SendRaw(_host, frame, delivery);
            return 1;
        }
        int sent = 0;
        for (int s = 1; s < Protocol.MaxPlayers; s++)
            if (_slotPeers[s] != null)
            {
                SendRaw(_slotPeers[s], frame, delivery);
                sent++;
            }
        return sent;
    }

    // ---- ping and time ------------------------------------------------------------------------------------------

    /// <summary>The clock of a directly connected slot (the host: any client; a client: only slot 0), or null.</summary>
    public NetClock ClockOf(int slot)
    {
        if ((uint)slot >= Protocol.MaxPlayers || slot == LocalSlot)
            return null;
        return IsHost ? _slotPeers[slot]?.Clock : slot == 0 ? _host.Clock : null;
    }

    /// <summary>The smoothed ping to a slot in milliseconds, -1 when not measured (yet).</summary>
    public double PingMs(int slot)
    {
        var clock = ClockOf(slot);
        return clock != null && clock.HasSamples ? clock.RttMs : -1;
    }

    public double JitterMs(int slot)
    {
        var clock = ClockOf(slot);
        return clock != null && clock.HasSamples ? clock.JitterMs : -1;
    }

    /// <summary>Seconds since anything came from a slot (for a "connection problem" mark), -1 when not connected.</summary>
    public double SilenceSeconds(int slot)
    {
        var p = PeerAt(slot);
        return p == null ? -1 : _now - p.LastHeard;
    }

    /// <summary>The transport's own view of the connection to a slot.</summary>
    public ConnectionQuality QualityOf(int slot)
    {
        var p = PeerAt(slot);
        return p == null ? ConnectionQuality.Unknown : _transport.QualityOf(p.Id);
    }

    /// <summary>Whether <see cref="ToSessionTime"/> knows the host's clock (always on the host; a client after its
    /// first pong).</summary>
    public bool ClockSynced => IsHost || _host.Clock.HasSamples;

    /// <summary>The session's shared timeline is the host's clock: a time on this game's clock on it. Game messages
    /// that carry times should carry session times, so a message relayed from another client fits too.</summary>
    public double ToSessionTime(double localTime) => ClockSynced && !IsHost ? _host.Clock.LocalToRemote(localTime) : localTime;

    public double ToLocalTime(double sessionTime) => ClockSynced && !IsHost ? _host.Clock.RemoteToLocal(sessionTime) : sessionTime;

    // ---- the transport's calls ----------------------------------------------------------------------------------

    void ITransportListener.OnConnected(PeerId peer)
    {
        if (State == SessionState.Ended)
            return;
        if (IsHost)
        {
            if (!_peers.ContainsKey(peer))
                _peers[peer] = new Peer(peer, Options.ClockWindow, _now);
        }
        else if (peer == _hostId)
        {
            _host.LastHeard = _now;
            if (State == SessionState.Connecting)
                SendHello();
        }
    }

    void ITransportListener.OnDisconnected(PeerId peer, string reason)
    {
        if (State == SessionState.Ended || !_peers.TryGetValue(peer, out var p))
            return;
        if (!IsHost)
        {
            End(LeaveReason.ConnectionLost, reason ?? "connection lost");
            return;
        }
        _peers.Remove(peer);
        if (p.Player != null)
            RemovePlayer(p, LeaveReason.ConnectionLost);
    }

    void ITransportListener.OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery)
    {
        if (State == SessionState.Ended || message.Count == 0 || message.Array == null)
            return;
        BytesReceived += message.Count;
        if (!_peers.TryGetValue(from, out var p))
        {
            if (!IsHost)
                return; // a client only listens to its host
            p = new Peer(from, Options.ClockWindow, _now);
            _peers[from] = p;
        }
        if (p.Closing)
            return;
        p.LastHeard = _now;

        var type = (MessageType)message.Array[message.Offset];
        _reader.Reset(new ArraySegment<byte>(message.Array, message.Offset + 1, message.Count - 1));
        try
        {
            if (Protocol.IsGameMessage(type))
                OnGameMessage(p, type, message, delivery);
            else if (IsHost)
                OnHostMessage(p, type);
            else
                OnClientMessage(type);
        }
        catch (NetReadException e)
        {
            MalformedMessages++;
            Note(FormattableString.Invariant($"broken {type} from {from}: {e.Message}"));
            if (type is MessageType.Hello or MessageType.Welcome or MessageType.Roster or MessageType.Reject)
                ProtocolError(p, e.Message);
        }
    }

    private void OnHostMessage(Peer p, MessageType type)
    {
        switch (type)
        {
            case MessageType.Hello:
                OnHello(p);
                break;
            case MessageType.Ping:
                if (p.Player != null)
                    SendPong(p);
                break;
            case MessageType.Pong:
                OnPong(p);
                break;
            case MessageType.Goodbye:
                _peers.Remove(p.Id);
                _transport.Disconnect(p.Id, "goodbye");
                if (p.Player != null)
                    RemovePlayer(p, LeaveReason.Goodbye);
                break;
        }
    }

    private void OnClientMessage(MessageType type)
    {
        switch (type)
        {
            case MessageType.Welcome:
                OnWelcome();
                break;
            case MessageType.Reject:
                if (State == SessionState.Connecting)
                    OnReject();
                break;
            case MessageType.Roster:
                if (State == SessionState.Active)
                {
                    var reason = (LeaveReason)_reader.ReadByte();
                    ReadRoster();
                    ApplyRoster(reason, false);
                }
                break;
            case MessageType.Ping:
                SendPong(_host);
                break;
            case MessageType.Pong:
                OnPong(_host);
                break;
            case MessageType.Goodbye:
                var why = (LeaveReason)_reader.ReadByte();
                End(why == LeaveReason.Kicked ? LeaveReason.Kicked : LeaveReason.HostLeft, "the host said goodbye");
                break;
        }
    }

    // ---- handshake ----------------------------------------------------------------------------------------------

    private void SendHello()
    {
        string name = string.IsNullOrWhiteSpace(Options.PlayerName) ? "" : Protocol.CleanName(Options.PlayerName, 0);
        _send.Reset();
        _send.WriteByte((byte)MessageType.Hello);
        _send.WriteUInt16(Protocol.Version); // always first: a game of another version can still read it
        _send.WriteString(Options.ModVersion, Protocol.MaxVersionBytes);
        _send.WriteString(Options.ModListHash, Protocol.MaxVersionBytes);
        _send.WriteString(name, Protocol.MaxNameBytes);
        _send.WriteUInt64(Options.PlayerId);
        SendRaw(_host, _send.ToSegment(), Delivery.Reliable);
    }

    private void OnHello(Peer p)
    {
        ushort protocol = _reader.ReadUInt16();
        if (protocol != Protocol.Version)
        {
            Reject(p, RejectReason.VersionMismatch, FormattableString.Invariant($"protocol {protocol}, ours {Protocol.Version}"));
            return;
        }
        string modVersion = _reader.ReadString(Protocol.MaxVersionBytes);
        string modList = _reader.ReadString(Protocol.MaxVersionBytes);
        string name = _reader.ReadString(Protocol.MaxNameBytes);
        ulong playerId = _reader.ReadUInt64();

        if (p.Player != null)
        {
            if (p.Player.PlayerId != playerId)
                return;
            // The same Hello again (sent at the start and on connect): answer it the same way.
            if (_now - p.WelcomedAt <= Options.RepeatedHelloSeconds)
            {
                SendWelcome(p);
                return;
            }
            // REVIEW(fable): package DC, never run in the game. A Hello long after the Welcome on the same connection
            // id is a new session of that game (a Steam rejoin before this host noticed the drop: Steam ids stay the
            // same): the player leaves and comes back, so every part sends them what a newcomer needs.
            Note(FormattableString.Invariant($"{p.Player.Name} said hello again on the same connection: a new session of theirs"));
            RemovePlayer(p, LeaveReason.ConnectionLost);
        }

        int reserved = ReservedSlotOf(playerId);
        int slot = -1;
        RejectReason reason = RejectReason.None;
        string detail = "";
        if (Options.RequireSameModVersion && modVersion != (Options.ModVersion ?? ""))
        {
            reason = RejectReason.VersionMismatch;
            detail = $"mod {modVersion}, ours {Options.ModVersion}";
        }
        else if (Options.RequireSameModList && modList != (Options.ModListHash ?? ""))
        {
            reason = RejectReason.ModListMismatch;
            detail = $"mod list {modList}, ours {Options.ModListHash}";
        }
        else if (_banned.Contains(playerId))
            reason = RejectReason.Kicked;
        else if (PlayerIdTaken(playerId))
            reason = RejectReason.DuplicatePlayer;
        // A player who dropped comes back to their kept slot also during a run (package DC).
        else if (reserved < 0 && !AcceptingPlayers)
            reason = RejectReason.NotAccepting;
        // REVIEW(fable): matchmaking (package MM): only the matched players may join; others get NotAccepting.
        else if (Admit != null && !Admit(p.Id, playerId))
            reason = RejectReason.NotAccepting;
        else if ((slot = reserved >= 0 ? reserved : FreeSlot()) < 0)
            reason = RejectReason.Full;
        if (reason != RejectReason.None)
        {
            Reject(p, reason, detail);
            return;
        }

        if (reserved >= 0)
            Release(reserved);
        var entry = new RosterEntry(slot, playerId, Protocol.CleanName(name, slot), p.Id);
        p.Player = entry;
        p.NextPing = _now;
        p.WelcomedAt = _now;
        _slots[slot] = entry;
        _slotPeers[slot] = p;
        RebuildRoster();
        SendWelcome(p);
        SendRoster(LeaveReason.Goodbye, p); // the others hear of the new player
        Note(FormattableString.Invariant($"{entry.Name} {(reserved >= 0 ? "came back to" : "joined in")} slot {slot} (peer {p.Id})"));
        PlayerJoined?.Invoke(entry);
    }

    private void Reject(Peer p, RejectReason reason, string detail)
    {
        WriteReject(reason);
        SendRaw(p, _send.ToSegment(), Delivery.Reliable);
        Close(p, "rejected: " + reason);
        Note(FormattableString.Invariant($"turned away peer {p.Id}: {reason}") + (detail.Length > 0 ? " (" + detail + ")" : ""));
    }

    private void WriteReject(RejectReason reason)
    {
        _send.Reset();
        _send.WriteByte((byte)MessageType.Reject);
        _send.WriteByte((byte)reason);
        _send.WriteUInt16(Protocol.Version);
        _send.WriteString(Options.ModVersion, Protocol.MaxVersionBytes);
    }

    private void OnReject()
    {
        Rejection = (RejectReason)_reader.ReadByte();
        HostProtocolVersion = _reader.ReadUInt16();
        HostModVersion = _reader.ReadString(Protocol.MaxVersionBytes);
        End(LeaveReason.Rejected, FormattableString.Invariant($"the host said no: {Rejection} (host: protocol {HostProtocolVersion}, mod {HostModVersion})"));
    }

    private void SendWelcome(Peer p)
    {
        _send.Reset();
        _send.WriteByte((byte)MessageType.Welcome);
        _send.WriteByte((byte)p.Player.Slot);
        WriteRoster();
        SendRaw(p, _send.ToSegment(), Delivery.Reliable);
    }

    private void OnWelcome()
    {
        int slot = _reader.ReadByte();
        if (slot == 0 || slot >= Protocol.MaxPlayers)
            throw new NetReadException(FormattableString.Invariant($"welcome to slot {slot}"));
        ReadRoster();
        if (_incoming[slot] == null || _incoming[slot].PlayerId != Options.PlayerId)
            throw new NetReadException(FormattableString.Invariant($"welcome to slot {slot} without this player in it"));
        if (State == SessionState.Active)
        {
            if (slot == LocalSlot)
                ApplyRoster(LeaveReason.Goodbye, false); // a repeated Welcome
            return;
        }
        LocalSlot = slot;
        State = SessionState.Active;
        _host.NextPing = _now;
        Note(FormattableString.Invariant($"joined the host {_hostId} in slot {slot}"));
        ApplyRoster(LeaveReason.Goodbye, true);
    }

    // ---- roster -------------------------------------------------------------------------------------------------

    private void WriteRoster()
    {
        int count = 0;
        for (int s = 0; s < Protocol.MaxPlayers; s++)
            if (_slots[s] != null)
                count++;
        _send.WriteByte((byte)count);
        for (int s = 0; s < Protocol.MaxPlayers; s++)
        {
            var e = _slots[s];
            if (e == null)
                continue;
            _send.WriteByte((byte)e.Slot);
            _send.WriteUInt64(e.PlayerId);
            _send.WriteUInt64(e.Peer.Value);
            _send.WriteString(e.Name, Protocol.MaxNameBytes);
        }
    }

    /// <summary>The host to every client except <paramref name="except"/>: the roster after a change.</summary>
    private void SendRoster(LeaveReason reason, Peer except)
    {
        _send.Reset();
        _send.WriteByte((byte)MessageType.Roster);
        _send.WriteByte((byte)reason);
        WriteRoster();
        var bytes = _send.ToSegment();
        for (int s = 1; s < Protocol.MaxPlayers; s++)
            if (_slotPeers[s] != null && _slotPeers[s] != except)
                SendRaw(_slotPeers[s], bytes, Delivery.Reliable);
    }

    private void ReadRoster()
    {
        Array.Clear(_incoming, 0, _incoming.Length);
        int count = _reader.ReadByte();
        if (count > Protocol.MaxPlayers)
            throw new NetReadException(FormattableString.Invariant($"roster of {count} players"));
        for (int i = 0; i < count; i++)
        {
            int slot = _reader.ReadByte();
            if (slot >= Protocol.MaxPlayers || _incoming[slot] != null)
                throw new NetReadException(FormattableString.Invariant($"roster with slot {slot} twice or out of range"));
            ulong id = _reader.ReadUInt64();
            ulong peer = _reader.ReadUInt64();
            string name = _reader.ReadString(Protocol.MaxNameBytes);
            _incoming[slot] = new RosterEntry(slot, id, name, new PeerId(peer));
        }
        if (_incoming[0] == null)
            throw new NetReadException("roster without the host");
    }

    /// <summary>A client takes the host's roster: players gone leave with <paramref name="reason"/>, new ones join.</summary>
    private void ApplyRoster(LeaveReason reason, bool started)
    {
        _left.Clear();
        _joined.Clear();
        for (int s = 0; s < Protocol.MaxPlayers; s++)
        {
            var old = _slots[s];
            var now = _incoming[s];
            if (old != null && now != null && old.PlayerId == now.PlayerId && old.Name == now.Name && old.Peer == now.Peer)
                continue; // unchanged: keep the same object
            if (old != null && (now == null || old.PlayerId != now.PlayerId) && s != LocalSlot)
                _left.Add(old);
            if (now != null && (old == null || old.PlayerId != now.PlayerId) && s != LocalSlot)
                _joined.Add(now);
            _slots[s] = now;
        }
        RebuildRoster();
        if (started)
            Started?.Invoke();
        foreach (var e in _left)
            if (State != SessionState.Ended)
                PlayerLeft?.Invoke(e, reason);
        foreach (var e in _joined)
            if (State != SessionState.Ended)
                PlayerJoined?.Invoke(e);
    }

    private void RemovePlayer(Peer p, LeaveReason reason)
    {
        var entry = p.Player;
        p.Player = null;
        _slots[entry.Slot] = null;
        _slotPeers[entry.Slot] = null;
        bool keep = LeaveReasons.IsDrop(reason) && Options.RejoinWindowSeconds > 0;
        if (keep)
        {
            // The line broke: the slot is kept for them a while (package DC); a goodbye or a kick frees it at once.
            _reservedId[entry.Slot] = entry.PlayerId;
            _reservedUntil[entry.Slot] = _now + Options.RejoinWindowSeconds;
        }
        RebuildRoster();
        SendRoster(reason, null);
        Note(FormattableString.Invariant($"{entry.Name} (slot {entry.Slot}) left: {reason}") +
             (keep ? FormattableString.Invariant($"; the slot is kept {Options.RejoinWindowSeconds:0} s for a rejoin") : ""));
        PlayerLeft?.Invoke(entry, reason);
    }

    private void RebuildRoster()
    {
        _roster.Clear();
        foreach (var e in _slots)
            if (e != null)
                _roster.Add(e);
    }

    private int FreeSlot()
    {
        for (int s = 1; s < _maxPlayers; s++)
            if (_slots[s] == null && !IsReserved(s))
                return s;
        return -1;
    }

    private bool PlayerIdTaken(ulong playerId)
    {
        foreach (var e in _slots)
            if (e != null && e.PlayerId == playerId)
                return true;
        return false;
    }

    // ---- ping ---------------------------------------------------------------------------------------------------

    private void SendPing(Peer p)
    {
        _send.Reset();
        _send.WriteByte((byte)MessageType.Ping);
        _send.WriteDouble(_now);
        SendRaw(p, _send.ToSegment(), Delivery.Unreliable);
    }

    private void SendPong(Peer p)
    {
        double sent = _reader.ReadDouble();
        _send.Reset();
        _send.WriteByte((byte)MessageType.Pong);
        _send.WriteDouble(sent);
        _send.WriteDouble(_now);
        SendRaw(p, _send.ToSegment(), Delivery.Unreliable);
    }

    private void OnPong(Peer p)
    {
        double sent = _reader.ReadDouble();
        double remote = _reader.ReadDouble();
        p.Clock.AddSample(sent, remote, _now);
    }

    // ---- game messages ------------------------------------------------------------------------------------------

    private void OnGameMessage(Peer p, MessageType type, ArraySegment<byte> message, Delivery delivery)
    {
        if (State != SessionState.Active)
            return;
        int origin = _reader.ReadByte();
        if (IsHost)
        {
            if (p.Player == null)
                return;
            if (origin != p.Player.Slot)
            {
                MalformedMessages++;
                Note(FormattableString.Invariant($"{type} from slot {p.Player.Slot} claimed to be from slot {origin}: dropped"));
                return;
            }
            if (_relay[(byte)type])
                for (int s = 1; s < Protocol.MaxPlayers; s++)
                    if (s != origin && _slotPeers[s] != null)
                        SendRaw(_slotPeers[s], message, delivery);
        }
        else if (origin >= Protocol.MaxPlayers || origin == LocalSlot)
        {
            MalformedMessages++;
            return;
        }

        var handler = _handlers[(byte)type];
        if (handler == null)
            return;
        try
        {
            handler(new GameMessage(type, origin, delivery, !IsHost && origin != 0), _reader);
        }
        catch (Exception e)
        {
            if (e is NetReadException)
                MalformedMessages++;
            else
                HandlerErrors++;
            Note(FormattableString.Invariant($"the handler for {type} from slot {origin} failed: {e.GetType().Name}: {e.Message}"));
        }
    }

    private ArraySegment<byte> Frame(MessageType type, ArraySegment<byte> payload)
    {
        _send.Reset();
        _send.WriteByte((byte)type);
        _send.WriteByte((byte)LocalSlot);
        _send.WriteBytes(payload);
        return _send.ToSegment();
    }

    // ---- endings ------------------------------------------------------------------------------------------------

    private void WriteGoodbye(LeaveReason reason)
    {
        _send.Reset();
        _send.WriteByte((byte)MessageType.Goodbye);
        _send.WriteByte((byte)reason);
    }

    private void Close(Peer p, string reason)
    {
        p.Closing = true;
        p.CloseAt = _now + Options.RejectLingerSeconds;
        p.CloseReason = reason;
    }

    /// <summary>Gives up on a peer: on a client the host (the session ends), on the host a client or a stranger.</summary>
    private void Drop(Peer p, LeaveReason reason, string detail)
    {
        if (!IsHost)
        {
            End(reason, detail);
            return;
        }
        _peers.Remove(p.Id);
        _transport.Disconnect(p.Id, detail);
        if (p.Player != null)
            RemovePlayer(p, reason);
        else
            Note(FormattableString.Invariant($"dropped peer {p.Id} before its hello: {detail}"));
    }

    private void ProtocolError(Peer p, string detail)
    {
        if (!p.Closing)
            Drop(p, LeaveReason.ProtocolError, "protocol error: " + detail);
    }

    private void End(LeaveReason reason, string detail)
    {
        if (State == SessionState.Ended)
            return;
        State = SessionState.Ended;
        EndReason = reason;
        _scratch.Clear();
        _scratch.AddRange(_peers.Values);
        foreach (var p in _scratch)
            _transport.Disconnect(p.Id, detail);
        _peers.Clear();
        Array.Clear(_slotPeers, 0, _slotPeers.Length);
        Note(FormattableString.Invariant($"session ended: {reason} ({detail})"));
        SessionEnded?.Invoke(reason);
    }

    // ---- small helpers ------------------------------------------------------------------------------------------

    private Peer PeerAt(int slot)
    {
        if ((uint)slot >= Protocol.MaxPlayers || slot == LocalSlot)
            return null;
        return IsHost ? _slotPeers[slot] : slot == 0 && State != SessionState.Ended ? _host : null;
    }

    private void SendRaw(Peer p, ArraySegment<byte> bytes, Delivery delivery)
    {
        _transport.Send(p.Id, bytes, delivery);
        BytesSent += bytes.Count;
    }

    private void Note(string line) => Log?.Invoke(line);

    private static void CheckGameType(MessageType type)
    {
        if (!Protocol.IsGameMessage(type))
            throw new ArgumentOutOfRangeException(nameof(type), type, "game messages start at " + Protocol.FirstGameMessage);
    }

    private static void CheckLength(string value, int maxBytes, string name)
    {
        if (value != null && Encoding.UTF8.GetByteCount(value) > maxBytes)
            throw new ArgumentException(FormattableString.Invariant($"{name} is over {maxBytes} bytes"), name);
    }
}

public enum SessionState : byte
{
    /// <summary>A client waiting for the host's Welcome.</summary>
    Connecting = 0,
    Active = 1,
    Ended = 2,
}

/// <summary>What a session needs to know about this game; set before the session starts.</summary>
public sealed class SessionOptions
{
    public string PlayerName { get; set; } = "";

    /// <summary>This player's own id (made by the client, kept between sessions: run history, rejoin).</summary>
    public ulong PlayerId { get; set; }

    public string ModVersion { get; set; } = "";

    /// <summary><see cref="Protocol.HashModList"/> of the mods that must match.</summary>
    public string ModListHash { get; set; } = "";

    /// <summary>The host's side: players allowed, host included (1-4).</summary>
    public int MaxPlayers { get; set; } = Protocol.MaxPlayers;

    public double PingIntervalSeconds { get; set; } = 0.5;

    /// <summary>A peer silent this long is dropped (also a handshake that gets no answer, unless
    /// <see cref="HandshakeTimeoutSeconds"/> says otherwise).</summary>
    public double TimeoutSeconds { get; set; } = 10;

    /// <summary>A handshake without an answer ends after this long; 0 = <see cref="TimeoutSeconds"/>.</summary>
    public double HandshakeTimeoutSeconds { get; set; }

    /// <summary>The host's side: how long the slot of a player whose line broke is kept for their rejoin (0 = not;
    /// package DC). Such a player may come back also while <see cref="CoopSession.AcceptingPlayers"/> is off.</summary>
    public double RejoinWindowSeconds { get; set; }

    /// <summary>The host's side: a Hello this soon after the Welcome on the same connection is the same one again
    /// (sent at the start and on connect); later it is a new session of that game (a rejoin).</summary>
    public double RepeatedHelloSeconds { get; set; } = 10;

    /// <summary>How long a rejected or kicked game stays connected so that the reason reaches it.</summary>
    public double RejectLingerSeconds { get; set; } = 1;

    public bool RequireSameModVersion { get; set; } = true;
    public bool RequireSameModList { get; set; } = true;

    /// <summary>Ping/pong pairs each <see cref="NetClock"/> keeps.</summary>
    public int ClockWindow { get; set; } = NetClock.DefaultWindow;
}

/// <summary>A player in the session's roster (slot 0 is the host). Immutable; a new one comes with a change.</summary>
public sealed class RosterEntry
{
    public RosterEntry(int slot, ulong playerId, string name, PeerId peer)
    {
        Slot = slot;
        PlayerId = playerId;
        Name = name ?? "";
        Peer = peer;
    }

    public int Slot { get; }
    public ulong PlayerId { get; }
    public string Name { get; }

    /// <summary>The player's id on the transport (as the host sees it: Steam ids are the same everywhere).</summary>
    public PeerId Peer { get; }

    public bool IsHost => Slot == 0;

    public override string ToString() => FormattableString.Invariant($"{Slot}:{Name}");
}

/// <summary>Where a game message came from: the slot that wrote it (for a relayed message the original client).</summary>
public readonly struct GameMessage
{
    public readonly MessageType Type;
    public readonly int FromSlot;
    public readonly Delivery Delivery;

    /// <summary>A client's message that the host passed on.</summary>
    public readonly bool Relayed;

    public GameMessage(MessageType type, int fromSlot, Delivery delivery, bool relayed)
    {
        Type = type;
        FromSlot = fromSlot;
        Delivery = delivery;
        Relayed = relayed;
    }
}

/// <summary>Handles one game message; <paramref name="payload"/> starts after the header and is only valid during the call.</summary>
public delegate void GameMessageHandler(GameMessage message, NetReader payload);
