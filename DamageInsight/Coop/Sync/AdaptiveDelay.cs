using System;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// The render clock for one remote game: which moment of that game to draw now (docs/COOP_PLAN.md §5.3). It
/// watches the updates arriving (their send time and arrival time) and keeps the render time
/// <c>now - transit - Delay</c> behind, where transit = the measured arrival time minus send time (latency plus any
/// clock offset, so the clocks need not be synced for this) and
/// <c>Delay = clamp(IntervalFactor * interval + JitterFactor * jitter, MinDelay, MaxDelay)</c>: about two updates
/// (one may be lost) plus the arrival jitter. When the wanted delay changes, the render clock does not jump: it runs
/// at most <see cref="MaxWarp"/> faster or slower until it is there, so motion never jumps. All entities and events
/// of one remote game share one AdaptiveDelay and so one timeline.
/// </summary>
public sealed class AdaptiveDelay
{
    /// <summary>k1: updates to have in the buffer (2 = one update can go missing without a pause).</summary>
    public float IntervalFactor = 2f;
    /// <summary>k2: how many "jitters" of late arrival to cover.</summary>
    public float JitterFactor = 2f;
    /// <summary>The buffer delay's bounds in seconds (COOP_PLAN §5.3: 60-250 ms).</summary>
    public float MinDelay = 0.06f, MaxDelay = 0.25f;
    /// <summary>The render clock runs at 1 ± this while it catches up (0.08: 8 %, too little to see).</summary>
    public float MaxWarp = 0.08f;
    /// <summary>Warp = difference / this (clamped to MaxWarp): 0.5 s means a 40 ms difference asks for the full 8 %.
    /// Bigger = gentler.</summary>
    public float CatchUpSeconds = 0.5f;
    /// <summary>A render clock this far off (seconds) jumps instead of warping (the first update, a clock reset).</summary>
    public float ResyncSeconds = 1f;

    // How fast the measurements follow (per update). Jitter rises fast and falls slowly, like a jitter buffer
    // should: one late burst widens the buffer at once, and it narrows only after a calm while.
    private const double TransitFollow = 0.05, IntervalFollow = 0.1;
    /// <summary>How much of a later-than-usual arrival the jitter takes on at once / how slowly it calms down again.</summary>
    public float JitterRise = 0.2f, JitterFall = 0.02f;

    /// <summary>
    /// The settings for a real internet line (Steam relay, two homes). UBDuck's test (COOP_LOG 61 (4), 62): in calm
    /// minutes 99-100 % of frames were drawn between two known states; in the spiky ones (ping up to 310 ms, jitter
    /// 44-105 ms) only 86 % (up to 68 extrapolated or 56 waiting frames a minute: the visible jumps), with the buffer at
    /// 150-190 ms while single updates came 150-200 ms late. So: three jitters instead of two, a jitter that rises
    /// faster and stays up longer after a burst, up to 350 ms. A calm line (Kerkopi: 46 ms, jitter 7) costs ~10 ms more.
    /// </summary>
    public static AdaptiveDelay ForInternet(float expectedInterval) => new(expectedInterval)
    {
        JitterFactor = 3f, MaxDelay = 0.35f, JitterRise = 0.35f, JitterFall = 0.01f,
    };

    private bool _hasData, _hasClock;
    private double _transit, _lastSendTime, _lastNow;
    private float _interval, _jitter;

    /// <summary>The time to draw (sender timeline). Valid once <see cref="HasData"/>.</summary>
    public double RenderTime { get; private set; }
    /// <summary>The render clock's speed in the last <see cref="Advance"/> (1 = real time).</summary>
    public float Rate { get; private set; } = 1f;
    /// <summary>The last <see cref="Advance"/> jumped the render time (start, or a clock reset): clear the
    /// buffers if it jumped back.</summary>
    public bool Jumped { get; private set; }
    public bool HasData => _hasData;

    /// <summary>Average seconds between updates (lost ones make it a little longer).</summary>
    public float Interval => _interval;
    /// <summary>Seconds a typical update arrives later than the average (rises fast, falls slowly).</summary>
    public float Jitter => _jitter;
    /// <summary>Average arrival time minus send time (latency + clock offset).</summary>
    public double Transit => _transit;
    /// <summary>The buffer delay the render clock is moving toward (seconds).</summary>
    public float Delay => Clamp(IntervalFactor * _interval + JitterFactor * _jitter, MinDelay, MaxDelay);
    /// <summary>What the render time is moving toward at <paramref name="now"/>.</summary>
    public double TargetAt(double now) => now - _transit - Delay;

    public AdaptiveDelay(float expectedInterval = 0.05f) => _interval = expectedInterval;

    /// <summary>An update sent at <paramref name="sendTime"/> (the sender's clock, the same as its states' times)
    /// arrived at <paramref name="arrivalTime"/> (this game's clock). Call for every state update.</summary>
    public void OnReceive(double sendTime, double arrivalTime)
    {
        double transit = arrivalTime - sendTime;
        if (!_hasData)
        {
            _hasData = true;
            _transit = transit;
            _lastSendTime = sendTime;
            return;
        }
        double gap = sendTime - _lastSendTime;
        if (gap > 0)
        {
            if (gap < 1.0) // a longer gap is a stall, not the rate
                _interval += (float)((gap - _interval) * IntervalFollow);
            _lastSendTime = sendTime;
        }
        float deviation = (float)Math.Abs(transit - _transit);
        _jitter += (float)((deviation - _jitter) * (deviation > _jitter ? JitterRise : JitterFall));
        _transit += (transit - _transit) * TransitFollow;
    }

    /// <summary>Moves the render clock to <paramref name="now"/> (this game's clock; call once per frame) and
    /// returns the render time.</summary>
    public double Advance(double now)
    {
        Jumped = false;
        if (!_hasData)
            return RenderTime;
        double target = TargetAt(now);
        if (!_hasClock)
        {
            _hasClock = true;
            _lastNow = now;
            RenderTime = target;
            Jumped = true;
            Rate = 1f;
            return RenderTime;
        }
        double dt = Math.Max(0, now - _lastNow);
        _lastNow = now;
        // Steer by where the target will be at the end of this frame.
        double error = target - (RenderTime + dt);
        if (Math.Abs(error) > ResyncSeconds)
        {
            RenderTime = target;
            Jumped = true;
            Rate = 1f;
            return RenderTime;
        }
        Rate = 1f + Clamp((float)(error / CatchUpSeconds), -MaxWarp, MaxWarp);
        RenderTime += dt * Rate;
        return RenderTime;
    }

    /// <summary>Starts over (a new connection or session).</summary>
    public void Reset()
    {
        _hasData = _hasClock = false;
        _jitter = 0;
        RenderTime = 0;
        Rate = 1f;
    }

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
