using System;
using System.Collections.Generic;

namespace DamageInsight.Coop.Net;

/// <summary>Settings of a <see cref="ReliableChannel"/>. The defaults fit the internet and game copies on one PC.</summary>
public sealed class ReliableChannelOptions
{
    /// <summary>The biggest datagram, transport header included (1200 fits every internet path without IP splitting).</summary>
    public int Mtu { get; set; } = 1200;
    /// <summary>Bytes kept free at the start of every datagram for the transport's own header.</summary>
    public int HeaderRoom { get; set; }
    public double MinRtoSeconds { get; set; } = 0.05;
    public double MaxRtoSeconds { get; set; } = 2.0;
    /// <summary>The resend time before the first round trip was measured.</summary>
    public double InitialRtoSeconds { get; set; } = 0.25;
    /// <summary>After this long without sending anything, an empty datagram (with the ack) goes out.</summary>
    public double KeepAliveSeconds { get; set; } = 0.25;
    /// <summary>No datagram from the peer for this long = the connection is dead (§5.7: a player drops after 20 s).</summary>
    public double DeadAfterSeconds { get; set; } = 20;
    /// <summary>The biggest message; bigger ones are dropped (a reliable one would block the stream).</summary>
    public int MaxMessageBytes { get; set; } = 1 << 20;
}

/// <summary>
/// One peer's connection over a pipe that loses, doubles and reorders datagrams (UDP): it gives the
/// <see cref="ITransport"/> promises on top. Unreliable messages pass through (never twice); reliable ones get a
/// sequence number, are acknowledged (a running ack + a bitfield of the 32 after it, in every datagram), resent after
/// a timeout measured from the round trip (doubling on every resend), and handed over once and in order. Messages
/// bigger than a datagram are split and joined again; small ones share datagrams. Also a keep-alive and a "dead" check.
/// Plain logic: the clock and the datagram sender are injected, so tests drive it through a simulated lossy pipe.
///
/// Datagram (after HeaderRoom): u8 flags (1 = has ack), u16 packet number, [u16 ack, u32 ack bits], then messages:
/// u8 kind (0 unreliable, 1 unreliable fragment, 2 reliable; + 0x10 first piece, 0x20 last piece), then
/// unreliable: u16 length | fragment: u16 group, u8 index, u8 count, u16 stride, u16 length | reliable: u16 seq,
/// u16 length; then the bytes. Numbers little-endian.
/// </summary>
public sealed class ReliableChannel
{
    private const int Window = 256; // reliable pieces in flight; divides 65536, so slots survive the sequence wrap
    private const int DatagramHeaderMax = 9;
    private const int PieceHeaderMax = 9;
    private const byte KindUnreliable = 0, KindFragment = 1, KindReliable = 2, FlagFirst = 0x10, FlagLast = 0x20;
    private const int FragmentSlots = 4;

    private sealed class Piece
    {
        public byte[] Data;
        public int Length;
        public byte Flags;
        public ushort Seq;
        public double FirstSent, LastSent;
        public int Sends;
    }

    private struct Range
    {
        public int Offset, Length;
    }

    private sealed class FragmentSlot
    {
        public bool Used;
        public ushort Group;
        public int Count, Received, Total, Stride;
        public double Stamp;
        public readonly bool[] Have = new bool[256];
        public byte[] Buffer = new byte[4096];
    }

    private readonly Func<double> _clock;
    private readonly Action<ArraySegment<byte>> _sendDatagram;
    private readonly Action<ArraySegment<byte>, Delivery> _onMessage;
    private readonly ReliableChannelOptions _options;
    private readonly int _maxPayload;

    // Sending.
    private readonly byte[] _out;
    private int _outLength, _outMessages;
    private ushort _packetSeq;
    private readonly Piece[] _inFlight = new Piece[Window];
    private readonly Queue<Piece> _waiting = new();
    private readonly Stack<Piece> _pool = new();
    private ushort _nextSeq, _oldestUnacked;
    private byte[] _unreliable = new byte[2048];
    private int _unreliableLength;
    private readonly List<Range> _unreliableRanges = new();
    private ushort _fragmentGroup;
    private double _lastSent;
    private bool _ackOwed;

    // Round trip. _backoff doubles on every flush that had to resend and falls back to 1 with the next clean sample:
    // without it a round trip longer than the first timeout would never be measured (every piece resent = no sample).
    private double _srtt = -1, _rttVar;
    private int _backoff = 1;

    // Receiving.
    private bool _anyReliable, _anyPacket;
    private ushort _nextExpected, _highestPacket;
    private ulong _packetMask;
    private readonly Piece[] _received = new Piece[Window];
    private byte[] _assembly = new byte[2048];
    private int _assemblyLength;
    private bool _assembling, _assemblyTooBig;
    private readonly FragmentSlot[] _fragments = new FragmentSlot[FragmentSlots];
    private double _lastReceived;
    private double _lossLost, _lossTotal;
    private bool _died;

    public ReliableChannel(Func<double> clock, Action<ArraySegment<byte>> sendDatagram,
        Action<ArraySegment<byte>, Delivery> onMessage, ReliableChannelOptions options = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sendDatagram = sendDatagram ?? throw new ArgumentNullException(nameof(sendDatagram));
        _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
        _options = options ?? new ReliableChannelOptions();
        _maxPayload = _options.Mtu - _options.HeaderRoom - DatagramHeaderMax - PieceHeaderMax;
        if (_maxPayload < 64)
            throw new ArgumentException("MTU too small for the header room", nameof(options));
        _out = new byte[_options.Mtu];
        for (int i = 0; i < FragmentSlots; i++)
            _fragments[i] = new FragmentSlot();
        _lastReceived = _lastSent = _clock();
    }

    /// <summary>Called once from <see cref="Update"/> when the peer was silent for DeadAfterSeconds.</summary>
    public Action Died { get; set; }
    public bool IsDead => _died;
    public double SecondsSinceReceive => _clock() - _lastReceived;
    /// <summary>The smoothed round trip in ms; -1 before the first reliable message was acknowledged.</summary>
    public double SmoothedRttMs => _srtt < 0 ? -1 : _srtt * 1000;
    /// <summary>The current resend timeout (before the doubling of a piece that was already resent).</summary>
    public double RtoMs => Rto * 1000;
    /// <summary>Datagrams from the peer that never came, recent share in %.</summary>
    public float LossPercent => _lossTotal <= 0 ? 0 : (float)(_lossLost / _lossTotal * 100);
    public ConnectionQuality Quality => new((float)SmoothedRttMs, _anyPacket ? LossPercent : -1);
    /// <summary>Reliable pieces not yet acknowledged (waiting or in flight).</summary>
    public int PendingReliable => _waiting.Count + (ushort)(_nextSeq - _oldestUnacked);
    public long DatagramsSent { get; private set; }
    public long DatagramsReceived { get; private set; }
    public long Resends { get; private set; }
    public long DuplicatesDropped { get; private set; }
    public long BadDatagrams { get; private set; }
    public long OversizedDropped { get; private set; }

    private double Rto
    {
        get
        {
            double rto = _srtt < 0 ? _options.InitialRtoSeconds : _srtt + Math.Max(4 * _rttVar, 0.02);
            return Math.Min(_options.MaxRtoSeconds, Math.Max(_options.MinRtoSeconds, rto) * _backoff);
        }
    }

    /// <summary>Queues a message (copied now); it goes out at the next <see cref="Flush"/> or <see cref="Update"/>.</summary>
    public void Send(ArraySegment<byte> message, Delivery delivery)
    {
        int count = message.Count;
        if (count > _options.MaxMessageBytes)
        {
            OversizedDropped++;
            return;
        }
        if (delivery == Delivery.Reliable)
        {
            int at = 0;
            do
            {
                int n = Math.Min(_maxPayload, count - at);
                var piece = _pool.Count > 0 ? _pool.Pop() : new Piece { Data = new byte[_maxPayload] };
                Buffer.BlockCopy(message.Array!, message.Offset + at, piece.Data, 0, n);
                piece.Length = n;
                piece.Flags = (byte)((at == 0 ? FlagFirst : 0) | (at + n == count ? FlagLast : 0));
                piece.Sends = 0;
                _waiting.Enqueue(piece);
                at += n;
            } while (at < count);
            return;
        }
        if ((count + _maxPayload - 1) / _maxPayload > 255)
        {
            OversizedDropped++;
            return;
        }
        if (_unreliableLength + count > _unreliable.Length)
            Array.Resize(ref _unreliable, Math.Max(_unreliable.Length * 2, _unreliableLength + count));
        Buffer.BlockCopy(message.Array!, message.Offset, _unreliable, _unreliableLength, count);
        _unreliableRanges.Add(new Range { Offset = _unreliableLength, Length = count });
        _unreliableLength += count;
    }

    /// <summary>Resends what timed out, sends the keep-alive, checks for death, and flushes. Call every frame.</summary>
    public void Update()
    {
        double now = _clock();
        if (!_died && now - _lastReceived >= _options.DeadAfterSeconds)
        {
            _died = true;
            Died?.Invoke();
        }
        Flush(now - _lastSent >= _options.KeepAliveSeconds);
    }

    /// <summary>Sends everything queued (coalesced into as few datagrams as fit), due resends and an owed ack.</summary>
    public void Flush() => Flush(false);

    private void Flush(bool keepAlive)
    {
        double now = _clock();
        Begin();
        double rto = Rto;
        bool resent = false;
        for (ushort s = _oldestUnacked; s != _nextSeq; s++)
        {
            var p = _inFlight[s % Window];
            if (p == null)
                continue;
            double timeout = Math.Min(_options.MaxRtoSeconds, rto * (1 << Math.Min(p.Sends - 1, 10)));
            if (now - p.LastSent < timeout)
                continue;
            AppendReliable(p);
            p.LastSent = now;
            p.Sends++;
            Resends++;
            resent = true;
        }
        if (resent && _backoff < 64)
            _backoff *= 2;
        while (_waiting.Count > 0 && (ushort)(_nextSeq - _oldestUnacked) < Window)
        {
            var p = _waiting.Dequeue();
            p.Seq = _nextSeq++;
            _inFlight[p.Seq % Window] = p;
            AppendReliable(p);
            p.FirstSent = p.LastSent = now;
            p.Sends = 1;
        }
        for (int i = 0; i < _unreliableRanges.Count; i++)
            AppendUnreliable(_unreliableRanges[i]);
        _unreliableRanges.Clear();
        _unreliableLength = 0;
        if (_outMessages > 0 || _ackOwed || keepAlive)
            SendOut();
    }

    private void Begin()
    {
        int at = _options.HeaderRoom;
        _out[at] = (byte)(_anyReliable ? 1 : 0);
        TransportBytes.WriteU16(_out, at + 1, _packetSeq);
        at += 3;
        if (_anyReliable)
        {
            uint bits = 0;
            for (int i = 0; i < 32; i++)
                if (_received[(ushort)(_nextExpected + 1 + i) % Window] != null)
                    bits |= 1u << i;
            TransportBytes.WriteU16(_out, at, (ushort)(_nextExpected - 1));
            TransportBytes.WriteU32(_out, at + 2, bits);
            at += 6;
        }
        _outLength = at;
        _outMessages = 0;
    }

    private void SendOut()
    {
        _sendDatagram(new ArraySegment<byte>(_out, 0, _outLength));
        _packetSeq++;
        _lastSent = _clock();
        _ackOwed = false;
        DatagramsSent++;
        Begin();
    }

    private void Room(int bytes)
    {
        if (_outLength + bytes > _out.Length)
            SendOut();
    }

    private void AppendReliable(Piece p)
    {
        Room(5 + p.Length);
        _out[_outLength] = (byte)(KindReliable | p.Flags);
        TransportBytes.WriteU16(_out, _outLength + 1, p.Seq);
        TransportBytes.WriteU16(_out, _outLength + 3, (ushort)p.Length);
        Buffer.BlockCopy(p.Data, 0, _out, _outLength + 5, p.Length);
        _outLength += 5 + p.Length;
        _outMessages++;
    }

    private void AppendUnreliable(Range r)
    {
        if (r.Length <= _maxPayload)
        {
            Room(3 + r.Length);
            _out[_outLength] = KindUnreliable;
            TransportBytes.WriteU16(_out, _outLength + 1, (ushort)r.Length);
            Buffer.BlockCopy(_unreliable, r.Offset, _out, _outLength + 3, r.Length);
            _outLength += 3 + r.Length;
            _outMessages++;
            return;
        }
        int count = (r.Length + _maxPayload - 1) / _maxPayload;
        ushort group = _fragmentGroup++;
        for (int i = 0; i < count; i++)
        {
            int n = Math.Min(_maxPayload, r.Length - i * _maxPayload);
            Room(9 + n);
            int at = _outLength;
            _out[at] = KindFragment;
            TransportBytes.WriteU16(_out, at + 1, group);
            _out[at + 3] = (byte)i;
            _out[at + 4] = (byte)count;
            TransportBytes.WriteU16(_out, at + 5, (ushort)_maxPayload);
            TransportBytes.WriteU16(_out, at + 7, (ushort)n);
            Buffer.BlockCopy(_unreliable, r.Offset + i * _maxPayload, _out, at + 9, n);
            _outLength += 9 + n;
            _outMessages++;
        }
    }

    /// <summary>Takes one datagram from the peer (the part after the transport's header) and hands over the
    /// messages in it that are due, through the onMessage callback.</summary>
    public void Receive(ArraySegment<byte> datagram)
    {
        var buf = datagram.Array!;
        int at = datagram.Offset, end = datagram.Offset + datagram.Count;
        if (datagram.Count < 3)
        {
            BadDatagrams++;
            return;
        }
        _lastReceived = _clock();
        DatagramsReceived++;
        byte flags = buf[at];
        if (!AcceptPacket(TransportBytes.ReadU16(buf, at + 1)))
        {
            DuplicatesDropped++;
            return;
        }
        at += 3;
        if ((flags & 1) != 0)
        {
            if (end - at < 6)
            {
                BadDatagrams++;
                return;
            }
            OnAck(TransportBytes.ReadU16(buf, at), TransportBytes.ReadU32(buf, at + 2));
            at += 6;
        }
        while (at < end)
        {
            byte kind = buf[at];
            switch (kind & 0x0F)
            {
                case KindUnreliable:
                {
                    if (end - at < 3)
                        goto bad;
                    int n = TransportBytes.ReadU16(buf, at + 1);
                    if (end - at - 3 < n)
                        goto bad;
                    _onMessage(new ArraySegment<byte>(buf, at + 3, n), Delivery.Unreliable);
                    at += 3 + n;
                    break;
                }
                case KindFragment:
                {
                    if (end - at < 9)
                        goto bad;
                    int n = TransportBytes.ReadU16(buf, at + 7);
                    if (end - at - 9 < n)
                        goto bad;
                    OnFragment(TransportBytes.ReadU16(buf, at + 1), buf[at + 3], buf[at + 4],
                        TransportBytes.ReadU16(buf, at + 5), buf, at + 9, n);
                    at += 9 + n;
                    break;
                }
                case KindReliable:
                {
                    if (end - at < 5)
                        goto bad;
                    int n = TransportBytes.ReadU16(buf, at + 3);
                    if (end - at - 5 < n)
                        goto bad;
                    OnReliable(TransportBytes.ReadU16(buf, at + 1), (byte)(kind & 0x30), buf, at + 5, n);
                    at += 5 + n;
                    break;
                }
                default:
                    goto bad;
            }
        }
        return;
        bad:
        BadDatagrams++;
    }

    /// <summary>Drops datagrams seen before (the pipe may double them) or too old to tell; counts the missing ones.</summary>
    private bool AcceptPacket(ushort seq)
    {
        if (!_anyPacket)
        {
            _anyPacket = true;
            _highestPacket = seq;
            _packetMask = 1;
            _lossTotal = 1;
            return true;
        }
        int diff = (short)(seq - _highestPacket);
        if (diff > 0)
        {
            _packetMask = diff >= 64 ? 1 : (_packetMask << diff) | 1;
            _highestPacket = seq;
            _lossLost += diff - 1;
            _lossTotal += diff;
            if (_lossTotal > 400)
            {
                _lossLost /= 2;
                _lossTotal /= 2;
            }
            return true;
        }
        int back = -diff;
        if (back >= 64 || (_packetMask & (1UL << back)) != 0)
            return false;
        _packetMask |= 1UL << back;
        _lossLost = Math.Max(0, _lossLost - 1); // it came late, not lost
        return true;
    }

    private void OnAck(ushort ack, uint bits)
    {
        double now = _clock();
        for (ushort s = _oldestUnacked; s != _nextSeq && (short)(ack - s) >= 0; s++)
            Acked(s, now);
        for (int i = 0; i < 32; i++)
        {
            if ((bits & (1u << i)) == 0)
                continue;
            ushort s = (ushort)(ack + 2 + i);
            if ((ushort)(s - _oldestUnacked) < (ushort)(_nextSeq - _oldestUnacked))
                Acked(s, now);
        }
        while (_oldestUnacked != _nextSeq && _inFlight[_oldestUnacked % Window] == null)
            _oldestUnacked++;
    }

    private void Acked(ushort seq, double now)
    {
        var p = _inFlight[seq % Window];
        if (p == null || p.Seq != seq)
            return;
        _inFlight[seq % Window] = null;
        if (p.Sends == 1)
        {
            // Karn: only pieces sent once tell the round trip.
            double sample = now - p.FirstSent;
            _backoff = 1;
            if (_srtt < 0)
            {
                _srtt = sample;
                _rttVar = sample / 2;
            }
            else
            {
                _rttVar = 0.75 * _rttVar + 0.25 * Math.Abs(_srtt - sample);
                _srtt = 0.875 * _srtt + 0.125 * sample;
            }
        }
        _pool.Push(p);
    }

    private void OnReliable(ushort seq, byte flags, byte[] buf, int at, int n)
    {
        _anyReliable = true;
        _ackOwed = true;
        int diff = (short)(seq - _nextExpected);
        if (diff < 0 || diff >= Window || (diff > 0 && _received[seq % Window] != null))
        {
            DuplicatesDropped++;
            return;
        }
        if (diff > 0)
        {
            var keep = _pool.Count > 0 ? _pool.Pop() : new Piece { Data = new byte[_maxPayload] };
            if (keep.Data.Length < n)
                keep.Data = new byte[n];
            Buffer.BlockCopy(buf, at, keep.Data, 0, n);
            keep.Length = n;
            keep.Flags = flags;
            keep.Seq = seq;
            _received[seq % Window] = keep;
            return;
        }
        _nextExpected++;
        Deliver(flags, buf, at, n);
        Piece next;
        while ((next = _received[_nextExpected % Window]) != null)
        {
            _received[_nextExpected % Window] = null;
            _nextExpected++;
            Deliver(next.Flags, next.Data, 0, next.Length);
            _pool.Push(next);
        }
    }

    private void Deliver(byte flags, byte[] buf, int at, int n)
    {
        bool first = (flags & FlagFirst) != 0, last = (flags & FlagLast) != 0;
        if (first && last)
        {
            _assembling = false;
            _onMessage(new ArraySegment<byte>(buf, at, n), Delivery.Reliable);
            return;
        }
        if (first)
        {
            _assembling = true;
            _assemblyTooBig = false;
            _assemblyLength = 0;
        }
        else if (!_assembling)
        {
            BadDatagrams++;
            return;
        }
        if (_assemblyLength + n > _options.MaxMessageBytes)
            _assemblyTooBig = true;
        if (!_assemblyTooBig)
        {
            if (_assemblyLength + n > _assembly.Length)
                Array.Resize(ref _assembly, Math.Max(_assembly.Length * 2, _assemblyLength + n));
            Buffer.BlockCopy(buf, at, _assembly, _assemblyLength, n);
            _assemblyLength += n;
        }
        if (!last)
            return;
        _assembling = false;
        if (_assemblyTooBig)
            OversizedDropped++;
        else
            _onMessage(new ArraySegment<byte>(_assembly, 0, _assemblyLength), Delivery.Reliable);
    }

    private void OnFragment(ushort group, int index, int count, int stride, byte[] buf, int at, int n)
    {
        if (count == 0 || index >= count || n > stride || (index < count - 1 && n != stride))
        {
            BadDatagrams++;
            return;
        }
        FragmentSlot slot = null, free = null, oldest = null;
        foreach (var f in _fragments)
        {
            if (f.Used && f.Group == group)
                slot = f;
            else if (!f.Used)
                free ??= f;
            else if (oldest == null || f.Stamp < oldest.Stamp)
                oldest = f;
        }
        if (slot == null)
        {
            slot = free ?? oldest!; // a message that never completed gives its place to a newer one
            slot.Used = true;
            slot.Group = group;
            slot.Count = count;
            slot.Stride = stride;
            slot.Received = 0;
            slot.Total = -1;
            Array.Clear(slot.Have, 0, 256);
            slot.Stamp = _clock();
            if (slot.Buffer.Length < count * stride)
                slot.Buffer = new byte[count * stride];
        }
        if (slot.Count != count || slot.Stride != stride || slot.Have[index])
            return;
        slot.Have[index] = true;
        slot.Received++;
        Buffer.BlockCopy(buf, at, slot.Buffer, index * stride, n);
        if (index == count - 1)
            slot.Total = index * stride + n;
        if (slot.Received < count)
            return;
        slot.Used = false;
        _onMessage(new ArraySegment<byte>(slot.Buffer, 0, slot.Total), Delivery.Unreliable);
    }
}

/// <summary>Little-endian numbers in byte arrays, for the transports' packet headers (no BitConverter: it follows
/// the machine's byte order and allocates).</summary>
internal static class TransportBytes
{
    public static void WriteU16(byte[] b, int at, ushort v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
    }

    public static void WriteU32(byte[] b, int at, uint v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
        b[at + 2] = (byte)(v >> 16);
        b[at + 3] = (byte)(v >> 24);
    }

    public static ushort ReadU16(byte[] b, int at) => (ushort)(b[at] | b[at + 1] << 8);

    public static uint ReadU32(byte[] b, int at) =>
        (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24);
}
