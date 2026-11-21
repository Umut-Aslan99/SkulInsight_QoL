using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DamageInsight.Coop.Net;

/// <summary>How the random extra delay of the <see cref="LagSimulator"/> is spread.</summary>
public enum LagJitter
{
    /// <summary>A bell curve (most messages near the base delay), like a real connection.</summary>
    Bell,
    /// <summary>Every value between -JitterMs and +JitterMs equally often.</summary>
    Uniform,
}

/// <summary>What the <see cref="LagSimulator"/> does to messages; may be changed while it runs (read per message).
/// Units match the start options (--skulinsight-lag / -jitter / -loss).</summary>
public sealed class LagSettings
{
    /// <summary>One-way delay added to every message this game sends (both copies 100 = about 200 ms ping).</summary>
    public double DelayMs { get; set; }
    /// <summary>Random extra delay per message, at most this much earlier or later.</summary>
    public double JitterMs { get; set; }
    public LagJitter Shape { get; set; } = LagJitter.Bell;
    /// <summary>Share of unreliable messages dropped (0-100). Reliable ones are never lost: each "loss" delays them by
    /// a resend (when <see cref="SimulateResends"/>), as a real reliable channel would.</summary>
    public double LossPercent { get; set; }
    public bool SimulateResends { get; set; } = true;
    /// <summary>The delay one lost reliable message costs; 0 = about a round trip (2 x DelayMs + 50 ms).</summary>
    public double ResendDelayMs { get; set; }
    /// <summary>Unreliable messages get their delay on their own and may overtake each other (else they keep their order).</summary>
    public bool ReorderUnreliable { get; set; }
    /// <summary>Also delay and drop what this game receives, so one copy alone can simulate the whole round trip.</summary>
    public bool Incoming { get; set; }

    public bool IsOff => DelayMs <= 0 && JitterMs <= 0 && LossPercent <= 0;
}

/// <summary>
/// A broken line for the drop tests (package DC, `--skulinsight-drop=AFTER:FOR`): from <see cref="AfterSeconds"/>
/// after <see cref="Arm"/> on, for <see cref="ForSeconds"/> (or forever), nothing gets through in either direction.
/// Unreliable messages are lost; reliable ones come through when the line is back (a reliable channel sends them
/// again), or never when it stays broken. One object for every simulator of a game (a rejoin makes a new transport),
/// times on the simulators' clock.
/// </summary>
public sealed class Outage
{
    public Outage(double afterSeconds, double forSeconds)
    {
        AfterSeconds = Math.Max(0, afterSeconds);
        ForSeconds = Math.Max(0, forSeconds);
    }

    public double AfterSeconds { get; }

    /// <summary>How long it lasts; <see cref="double.PositiveInfinity"/> = for good.</summary>
    public double ForSeconds { get; }

    public bool Forever => double.IsPositiveInfinity(ForSeconds);

    /// <summary>When it starts (NaN until armed) and ends.</summary>
    public double From { get; private set; } = double.NaN;
    public double Until => From + ForSeconds;
    public bool Armed => !double.IsNaN(From);

    /// <summary>Starts the countdown (the first call only).</summary>
    public void Arm(double now)
    {
        if (!Armed)
            From = now + AfterSeconds;
    }

    public bool IsOn(double now) => Armed && now >= From && now < Until;

    /// <summary>"120:8" or "120:forever" (seconds); false when it isn't one.</summary>
    public static bool TryParse(string text, out Outage outage)
    {
        outage = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2
            || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double after)
            || after < 0 || after > 86400)
            return false;
        double length;
        if (parts[1].Trim().Equals("forever", StringComparison.OrdinalIgnoreCase))
            length = double.PositiveInfinity;
        else if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out length)
                 || length <= 0 || length > 86400)
            return false;
        outage = new Outage(after, length);
        return true;
    }

    public override string ToString() => FormattableString.Invariant($"line broken {AfterSeconds:0.#} s after the first teammate, ") +
                                         (Forever ? "for good" : FormattableString.Invariant($"for {ForSeconds:0.#} s"));
}

/// <summary>
/// Makes any transport behave like a worse connection, for testing co-op on one PC (docs/COOP_PLAN.md §9): delay,
/// jitter, loss and reordering. Reliable messages stay in order per peer and are never lost. Delayed messages go out
/// (and delayed incoming ones are handed over) during <see cref="Poll"/> and <see cref="Send"/>, in due order. Random
/// numbers are seeded and the clock injectable, so tests are exact. Plain C#.
/// </summary>
public sealed class LagSimulator : ITransport
{
    private sealed class Item
    {
        public double Due;
        public long Order;
        public PeerId Peer;
        public byte[] Bytes;
        public Delivery Delivery;
        public int Kind; // 0 message, 1 connected, 2 disconnected
        public string Reason;
    }

    private sealed class PeerTimes
    {
        public double Reliable, Unreliable, Any;
    }

    /// <summary>Puts what the inner transport delivers into the incoming queue (or passes it on).</summary>
    private sealed class Capture : ITransportListener
    {
        private readonly LagSimulator _owner;
        public ITransportListener Outer;
        public Capture(LagSimulator owner) => _owner = owner;
        public void OnConnected(PeerId peer) => _owner.Incoming(peer, default, Delivery.Reliable, 1, null);
        public void OnDisconnected(PeerId peer, string reason) => _owner.Incoming(peer, default, Delivery.Reliable, 2, reason);
        public void OnMessage(PeerId from, ArraySegment<byte> message, Delivery delivery) =>
            _owner.Incoming(from, message, delivery, 0, null);
    }

    private readonly ITransport _inner;
    private readonly Func<double> _clock;
    private readonly Random _random;
    private readonly Capture _capture;
    private readonly List<Item> _outgoing = new(), _incoming = new();
    private readonly Dictionary<PeerId, PeerTimes> _outTimes = new(), _inTimes = new();
    private long _order;

    public LagSimulator(ITransport inner, LagSettings settings, int seed = 1, Func<double> clock = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Settings = settings ?? new LagSettings();
        _random = new Random(seed);
        if (clock == null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }
        _clock = clock;
        _capture = new Capture(this);
    }

    public LagSettings Settings { get; set; }
    public ITransport Inner => _inner;
    public PeerId Self => _inner.Self;
    public long Dropped { get; private set; }
    public long SimulatedResends { get; private set; }
    public int Waiting => _outgoing.Count + _incoming.Count;

    /// <summary>A broken line (drop tests), or null.</summary>
    public Outage Outage { get; set; }

    /// <summary>Messages the broken line lost (both directions).</summary>
    public long LostToOutage { get; private set; }

    /// <summary>During an outage: false = the message is lost; true with <paramref name="notBefore"/> = it waits.</summary>
    private bool ThroughOutage(double now, Delivery delivery, out double notBefore)
    {
        notBefore = 0;
        var outage = Outage;
        if (outage == null || !outage.IsOn(now))
            return true;
        if (delivery != Delivery.Reliable || outage.Forever)
        {
            LostToOutage++;
            return false;
        }
        notBefore = outage.Until;
        return true;
    }

    public void Send(PeerId to, ArraySegment<byte> message, Delivery delivery)
    {
        double now = _clock();
        var settings = Settings;
        if (!ThroughOutage(now, delivery, out double notBefore))
            return;
        if (notBefore > 0)
        {
            Schedule(_outgoing, Times(_outTimes, to), Off, now, to, message, delivery, 0, null, notBefore);
            return;
        }
        if (settings.IsOff && _outgoing.Count == 0)
        {
            _inner.Send(to, message, delivery);
            return;
        }
        if (Schedule(_outgoing, Times(_outTimes, to), settings, now, to, message, delivery, 0, null))
            Release(_outgoing, now, null);
    }

    public void Poll(ITransportListener listener)
    {
        double now = _clock();
        Release(_outgoing, now, null);
        _capture.Outer = listener;
        _inner.Poll(_capture);
        Release(_incoming, _clock(), listener);
    }

    public void Disconnect(PeerId peer, string reason)
    {
        // What was sent before goes out first (a real connection lingers for it); a broken line loses it.
        bool broken = Outage?.IsOn(_clock()) == true;
        for (int i = 0; i < _outgoing.Count; i++)
        {
            var item = _outgoing[i];
            if (item.Peer != peer)
                continue;
            _outgoing.RemoveAt(i--);
            if (broken)
                LostToOutage++;
            else
                _inner.Send(peer, new ArraySegment<byte>(item.Bytes), item.Delivery);
        }
        _inner.Disconnect(peer, reason);
    }

    /// <summary>The inner transport's view plus the delay added here (unknown stays unknown).</summary>
    public ConnectionQuality QualityOf(PeerId peer)
    {
        var q = _inner.QualityOf(peer);
        if (q.PingMs < 0)
            return q;
        var s = Settings;
        return new ConnectionQuality((float)(q.PingMs + s.DelayMs * (s.Incoming ? 2 : 1)), q.LossPercent);
    }

    public void Dispose()
    {
        _outgoing.Clear();
        _incoming.Clear();
        _inner.Dispose();
    }

    private void Incoming(PeerId peer, ArraySegment<byte> message, Delivery delivery, int kind, string reason)
    {
        var settings = Settings;
        if (kind == 0)
        {
            double now = _clock();
            if (!ThroughOutage(now, delivery, out double notBefore))
                return;
            if (notBefore > 0)
            {
                Schedule(_incoming, Times(_inTimes, peer), Off, now, peer, message, delivery, kind, reason, notBefore);
                return;
            }
        }
        if (!settings.Incoming && _incoming.Count == 0)
        {
            Hand(_capture.Outer, peer, message, delivery, kind, reason);
            return;
        }
        Schedule(_incoming, Times(_inTimes, peer), settings.Incoming ? settings : Off, _clock(), peer, message,
            delivery, kind, reason);
    }

    private static readonly LagSettings Off = new();

    private static PeerTimes Times(Dictionary<PeerId, PeerTimes> all, PeerId peer)
    {
        if (!all.TryGetValue(peer, out var t))
            all[peer] = t = new PeerTimes();
        return t;
    }

    /// <summary>Gives a message its due time and puts it in the queue; false when it was dropped.</summary>
    private bool Schedule(List<Item> queue, PeerTimes times, LagSettings s, double now, PeerId peer,
        ArraySegment<byte> message, Delivery delivery, int kind, string reason, double notBefore = 0)
    {
        double due;
        if (kind != 0)
        {
            due = Math.Max(now, times.Any); // connects and disconnects come after the peer's earlier messages
        }
        else
        {
            bool reliable = delivery == Delivery.Reliable;
            if (!reliable && s.LossPercent > 0 && _random.NextDouble() * 100 < s.LossPercent)
            {
                Dropped++;
                return false;
            }
            due = Math.Max(notBefore, now + Math.Max(0, s.DelayMs + Jitter(s)) / 1000);
            if (reliable)
            {
                if (s.SimulateResends && s.LossPercent > 0)
                {
                    double resend = s.ResendDelayMs > 0 ? s.ResendDelayMs : 2 * s.DelayMs + 50;
                    for (int tries = 0; tries < 20 && _random.NextDouble() * 100 < s.LossPercent; tries++)
                    {
                        due += resend / 1000;
                        SimulatedResends++;
                    }
                }
                due = Math.Max(due, times.Reliable);
                times.Reliable = due;
            }
            else if (!s.ReorderUnreliable)
            {
                due = Math.Max(due, times.Unreliable);
                times.Unreliable = due;
            }
        }
        times.Any = Math.Max(times.Any, due);
        byte[] bytes = null;
        if (kind == 0)
        {
            bytes = new byte[message.Count];
            Buffer.BlockCopy(message.Array!, message.Offset, bytes, 0, message.Count);
        }
        var item = new Item { Due = due, Order = _order++, Peer = peer, Bytes = bytes, Delivery = delivery, Kind = kind, Reason = reason };
        // Sorted by due time; equal times keep their order.
        int lo = 0, hi = queue.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (queue[mid].Due <= due)
                lo = mid + 1;
            else
                hi = mid;
        }
        queue.Insert(lo, item);
        return true;
    }

    private double Jitter(LagSettings s)
    {
        if (s.JitterMs <= 0)
            return 0;
        if (s.Shape == LagJitter.Uniform)
            return (_random.NextDouble() * 2 - 1) * s.JitterMs;
        double sum = _random.NextDouble() + _random.NextDouble() + _random.NextDouble(); // 0..3, bell-shaped
        return (sum / 1.5 - 1) * s.JitterMs;
    }

    /// <summary>Sends (listener null) or hands over what is due, in due order.</summary>
    private void Release(List<Item> queue, double now, ITransportListener listener)
    {
        int n = 0;
        while (n < queue.Count && queue[n].Due <= now)
            n++;
        if (n == 0)
            return;
        var due = queue.GetRange(0, n);
        queue.RemoveRange(0, n);
        foreach (var item in due)
        {
            if (listener == null)
                _inner.Send(item.Peer, new ArraySegment<byte>(item.Bytes), item.Delivery);
            else
                Hand(listener, item.Peer, item.Bytes == null ? default : new ArraySegment<byte>(item.Bytes),
                    item.Delivery, item.Kind, item.Reason);
        }
    }

    private static void Hand(ITransportListener listener, PeerId peer, ArraySegment<byte> message, Delivery delivery,
        int kind, string reason)
    {
        switch (kind)
        {
            case 1: listener.OnConnected(peer); break;
            case 2: listener.OnDisconnected(peer, reason); break;
            default: listener.OnMessage(peer, message, delivery); break;
        }
    }
}
