using System;
using System.Globalization;

namespace DamageInsight.Coop;

/// <summary>
/// The anomaly watcher's plain part (flight recorder lite, T.md Open 0b ORDER 2; COOP_LOG 64): this game's player state
/// as short texts, sampled every frame by <see cref="PlayerWatch"/>: which parts are hidden ("" = visible), who holds
/// the controls without a dialogue ("" = free) and the player's parent ("" = none). A change is one "player-state"
/// event (the detailed trace); a hidden player or blocked controls that last past their limit become one anomaly
/// line (trace + the always-on session report), and its end one more. Bounded: at most
/// <see cref="MaxChangesPerMinute"/> change events a minute, the rest counted in one line. Times are seconds.
/// </summary>
public sealed class PlayerWatchLogic
{
    public const double HiddenLimit = 3, BlockedLimit = 15;
    public const int MaxChangesPerMinute = 40;

    public enum Part { Hidden, Controls, Parent }

    private sealed class Track
    {
        public string Value = "";
        public double Since = double.NaN;
        public bool Reported;
    }

    private readonly Track[] _tracks = { new(), new(), new() };
    private readonly Action<Part, string> _change;
    private readonly Action<string> _anomaly;
    private double _minuteStart = double.NegativeInfinity;
    private int _changes, _dropped;

    /// <param name="change">A state change: the part and its new text ("" = back to normal, with how long it lasted
    /// in the text).</param>
    /// <param name="anomaly">One plain-English line for the session report.</param>
    public PlayerWatchLogic(Action<Part, string> change, Action<string> anomaly)
    {
        _change = change ?? ((_, _) => { });
        _anomaly = anomaly ?? (_ => { });
    }

    /// <summary>Anomaly lines written so far.</summary>
    public int Anomalies { get; private set; }

    public void Sample(double now, string hidden, string controls, string parent)
    {
        if (now - _minuteStart >= 60)
        {
            if (_dropped > 0)
                _change(Part.Hidden, Inv($"({_dropped} more player state changes in the last minute)"));
            _minuteStart = now;
            _changes = 0;
            _dropped = 0;
        }
        Step(now, Part.Hidden, hidden, HiddenLimit);
        Step(now, Part.Controls, controls, BlockedLimit);
        Step(now, Part.Parent, parent, double.PositiveInfinity);
    }

    /// <summary>Seconds the part has been out of normal (0 when normal).</summary>
    public double For(Part part, double now)
    {
        var t = _tracks[(int)part];
        return t.Value.Length == 0 || double.IsNaN(t.Since) ? 0 : now - t.Since;
    }

    /// <summary>The part's current text ("" = normal).</summary>
    public string Current(Part part) => _tracks[(int)part].Value;

    private void Step(double now, Part part, string value, double limit)
    {
        value ??= "";
        var t = _tracks[(int)part];
        if (!string.Equals(value, t.Value, StringComparison.Ordinal))
        {
            bool wasOff = t.Value.Length > 0;
            double lasted = wasOff && !double.IsNaN(t.Since) ? now - t.Since : 0;
            t.Value = value;
            if (value.Length == 0)
            {
                Change(part, Inv($"normal again after {Span(lasted)}"));
                if (t.Reported)
                    Anomaly(Inv($"{Label(part)}: over after {Span(lasted)}"));
                t.Since = double.NaN;
                t.Reported = false;
            }
            else
            {
                if (!wasOff)
                    t.Since = now;
                Change(part, value);
            }
        }
        if (value.Length > 0 && !t.Reported && now - t.Since >= limit)
        {
            t.Reported = true;
            Anomaly(Inv($"{Label(part)} for {Span(now - t.Since)}: {value}"));
        }
    }

    private void Change(Part part, string text)
    {
        if (_changes >= MaxChangesPerMinute)
        {
            _dropped++;
            return;
        }
        _changes++;
        _change(part, text);
    }

    private void Anomaly(string text)
    {
        Anomalies++;
        _anomaly(text);
    }

    private static string Label(Part part) => part switch
    {
        Part.Hidden => "this player hidden",
        Part.Controls => "controls blocked without a dialogue",
        _ => "this player parented",
    };

    public static string Span(double seconds) =>
        seconds < 90 ? Inv($"{seconds:0.#} s") : Inv($"{seconds / 60:0.#} min");

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
