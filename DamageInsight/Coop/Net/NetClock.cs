using System;

namespace DamageInsight.Coop.Net;

/// <summary>
/// One peer's ping and clock, from ping/pong pairs (docs/COOP_PLAN.md §5.3, §5.7). Each pair gives three times: when
/// this game sent the ping, the peer's own time when it answered, and when the answer came back here. Like NTP, the
/// round trip is "back - sent" and the peer's clock is off by "its time - the middle of the round trip". The offset is
/// taken from the fastest recent pairs (a pair that waited in a queue on one way is lopsided), the shown ping is
/// smoothed, and the jitter says how much the round trip varies (the time buffer for remote things uses it).
/// A one-way delay that differs from the other way can't be seen by anyone: it shifts the offset by half the
/// difference. Times are seconds on this game's clock; nothing here reads a clock itself, so tests are exact.
/// </summary>
public sealed class NetClock
{
    public const int DefaultWindow = 16;

    /// <summary>How much of a new round trip goes into the smoothed one (TCP's 1/8).</summary>
    public const double Smoothing = 0.125;

    private readonly double[] _rtt, _offset;
    private readonly int[] _order; // scratch: sample indices by round trip
    private int _count, _next;

    public NetClock(int window = DefaultWindow)
    {
        if (window < 1)
            throw new ArgumentOutOfRangeException(nameof(window), window, "at least one sample");
        _rtt = new double[window];
        _offset = new double[window];
        _order = new int[window];
    }

    /// <summary>Samples kept (up to the window).</summary>
    public int SampleCount => _count;

    public bool HasSamples => _count > 0;

    /// <summary>The smoothed round trip in seconds (0 before the first sample).</summary>
    public double Rtt { get; private set; }

    public double RttMs => Rtt * 1000.0;

    /// <summary>The newest sample's round trip.</summary>
    public double LastRtt { get; private set; }

    /// <summary>The fastest round trip in the window.</summary>
    public double MinRtt { get; private set; }

    /// <summary>The mean absolute deviation of the window's round trips from their mean, in seconds.</summary>
    public double Jitter { get; private set; }

    public double JitterMs => Jitter * 1000.0;

    /// <summary>The peer's clock minus this game's clock (0 before the first sample).</summary>
    public double Offset { get; private set; }

    /// <summary>A time on the peer's clock as a time on this game's clock.</summary>
    public double RemoteToLocal(double remoteTime) => remoteTime - Offset;

    /// <summary>A time on this game's clock as a time on the peer's clock.</summary>
    public double LocalToRemote(double localTime) => localTime + Offset;

    /// <summary>
    /// Adds one ping/pong pair: <paramref name="localSend"/> and <paramref name="localReceive"/> on this game's clock,
    /// <paramref name="remoteTime"/> on the peer's. A pair that came back before it left (or with a broken time) is
    /// ignored (false).
    /// </summary>
    public bool AddSample(double localSend, double remoteTime, double localReceive)
    {
        double rtt = localReceive - localSend;
        if (double.IsNaN(rtt) || double.IsInfinity(rtt) || rtt < 0 || double.IsNaN(remoteTime) || double.IsInfinity(remoteTime))
            return false;
        _rtt[_next] = rtt;
        _offset[_next] = remoteTime - (localSend + localReceive) * 0.5;
        _next = (_next + 1) % _rtt.Length;
        if (_count < _rtt.Length)
            _count++;

        LastRtt = rtt;
        Rtt = _count == 1 ? rtt : Rtt + (rtt - Rtt) * Smoothing;
        Recompute();
        return true;
    }

    public void Reset()
    {
        _count = _next = 0;
        Rtt = LastRtt = MinRtt = Jitter = Offset = 0;
    }

    private void Recompute()
    {
        // Insertion sort of at most a window's indices by round trip (16 by default, twice a second: no allocations).
        double sum = 0;
        for (int i = 0; i < _count; i++)
        {
            sum += _rtt[i];
            int j = i;
            while (j > 0 && _rtt[_order[j - 1]] > _rtt[i])
            {
                _order[j] = _order[j - 1];
                j--;
            }
            _order[j] = i;
        }
        MinRtt = _rtt[_order[0]];

        double mean = sum / _count, deviation = 0;
        for (int i = 0; i < _count; i++)
            deviation += Math.Abs(_rtt[i] - mean);
        Jitter = deviation / _count;

        // The fastest quarter (at least one) decides the offset: they waited least, so they are the least lopsided.
        int best = Math.Max(1, (_count + 3) / 4);
        double offset = 0;
        for (int i = 0; i < best; i++)
            offset += _offset[_order[i]];
        Offset = offset / best;
    }
}
