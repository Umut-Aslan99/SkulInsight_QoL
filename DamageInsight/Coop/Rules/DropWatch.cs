using System;
using System.Collections.Generic;
using DamageInsight.Coop.Net;

namespace DamageInsight.Coop.Rules;

/// <summary>The numbers of a broken line (COOP_PLAN §5.7 and §4.8; package DC). Every one is a setting.</summary>
public sealed class DropSettings
{
    public const double DefaultProblemSeconds = 3, DefaultDropSeconds = 20, DefaultRejoinWindowSeconds = 120, DefaultRetrySeconds = 3;

    /// <summary>Nothing heard this long: "Connection problem with ..." on every game.</summary>
    public double ProblemSeconds { get; set; } = DefaultProblemSeconds;

    /// <summary>Nothing heard this long: the player drops out of the party (the session's timeout).</summary>
    public double DropSeconds { get; set; } = DefaultDropSeconds;

    /// <summary>How long a dropped player may come back (the host keeps their slot; their game tries again).</summary>
    public double RejoinWindowSeconds { get; set; } = DefaultRejoinWindowSeconds;

    /// <summary>A game that lost its host tries to join again this long after each failed try.</summary>
    public double RetrySeconds { get; set; } = DefaultRetrySeconds;

    /// <summary>How long the one-off lines ("... is back", "You play on alone") stay on screen.</summary>
    public double MessageSeconds { get; set; } = 8;

    /// <summary>The values pulled into sensible ranges (a broken config file must not make a 0 s drop).</summary>
    public DropSettings Clamped()
    {
        double problem = Clamp(ProblemSeconds, 1, 30, DefaultProblemSeconds);
        return new DropSettings
        {
            ProblemSeconds = problem,
            DropSeconds = Math.Max(problem + 1, Clamp(DropSeconds, 5, 120, DefaultDropSeconds)),
            RejoinWindowSeconds = Clamp(RejoinWindowSeconds, 0, 600, DefaultRejoinWindowSeconds),
            RetrySeconds = Clamp(RetrySeconds, 1, 30, DefaultRetrySeconds),
            MessageSeconds = Clamp(MessageSeconds, 1, 30, 8),
        };
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsNaN(value) ? fallback : Math.Max(min, Math.Min(max, value));
}

/// <summary>How a teammate's line is, as this game sees it.</summary>
public enum LinkState : byte
{
    Ok = 0,
    /// <summary>Nothing heard for a few seconds; still in the party.</summary>
    Problem = 1,
    /// <summary>Out of the party; may come back within the rejoin window.</summary>
    Dropped = 2,
}

/// <summary>What happened (for the trace and the log; the caller drains <see cref="DropWatch.Notes"/>).</summary>
public enum DropNoteKind : byte
{
    /// <summary>A teammate went silent (drop-problem).</summary>
    Problem,
    /// <summary>They were heard again before dropping; Seconds = how long the silence was.</summary>
    ProblemOver,
    /// <summary>They dropped out of the party (drop-dropped); slot 0 on a client = this game lost the host.</summary>
    Dropped,
    /// <summary>A client: time for the next join try (drop-rejoin-try).</summary>
    RetryNow,
    /// <summary>They are back in their slot (drop-rejoined); slot 0 on a client = this game is back.</summary>
    Rejoined,
    /// <summary>The rejoin window of a dropped teammate ran out (drop-gone).</summary>
    Gone,
    /// <summary>This game plays on alone from here (drop-solo).</summary>
    Solo,
}

public readonly struct DropNote
{
    public readonly DropNoteKind Kind;
    public readonly int Slot;
    public readonly string Name;
    public readonly double Seconds;

    public DropNote(DropNoteKind kind, int slot, string name, double seconds)
    {
        Kind = kind;
        Slot = slot;
        Name = name ?? "";
        Seconds = seconds;
    }

    public override string ToString() => FormattableString.Invariant($"{Kind} {Slot} {Name} {Seconds:0.#}");
}

/// <summary>Which line the screen shows (the caller turns it into text in the player's language).</summary>
public enum DropLineKind : byte
{
    /// <summary>"Connection problem with {name}... {s} s" (s counts up).</summary>
    Problem,
    /// <summary>"{name} lost the connection. Waiting for them: {s} s" (s counts down).</summary>
    WaitingFor,
    /// <summary>A client: "Lost the connection to {name}. Trying again: {s} s, then you play on alone." (s counts down).</summary>
    Reconnecting,
    /// <summary>"{name} is back."</summary>
    Back,
    /// <summary>"{name} lost the connection. You play on alone."</summary>
    LostAlone,
    /// <summary>"{name} lost the connection." (others are still there)</summary>
    Lost,
    /// <summary>"The host left. You play on alone."</summary>
    HostLeftAlone,
}

public readonly struct DropLine
{
    public readonly DropLineKind Kind;
    public readonly string Name;
    public readonly int Seconds;

    public DropLine(DropLineKind kind, string name, int seconds)
    {
        Kind = kind;
        Name = name ?? "";
        Seconds = seconds;
    }

    public override string ToString() => FormattableString.Invariant($"{Kind} {Name} {Seconds}");
}

/// <summary>
/// Package DC, the disconnect manager's rules (plain C#, tested): per teammate Ok -> Problem (nothing heard for
/// <see cref="DropSettings.ProblemSeconds"/>) -> Dropped (out of the party: the session's timeout) -> back (rejoined)
/// or gone (the rejoin window ran out). A client that lost its host tries to join again every few seconds until the
/// window runs out, then plays on alone (<see cref="IsSolo"/>). A meant leave (goodbye, kick, the host ending the
/// session) is no drop: no waiting, no retries. Fed by the runtime once a frame; it says what to show and when to
/// try again, the runtime does it.
/// </summary>
public sealed class DropWatch
{
    private sealed class Mate
    {
        public int Slot;
        public string Name = "";
        public ulong PlayerId;
        public LinkState State;
        public double Since, Until;
    }

    private readonly Dictionary<int, Mate> _mates = new();   // in the party now (Ok / Problem), by slot
    private readonly List<Mate> _dropped = new();             // out of the party, may come back
    private readonly List<DropNote> _notes = new();
    private DropLine? _message;
    private double _messageUntil;
    // A client's line to its host.
    private string _hostName = "";
    private double _retryAt, _soloAt;

    public DropWatch(DropSettings settings = null) => Settings = (settings ?? new DropSettings()).Clamped();

    public DropSettings Settings { get; private set; }

    /// <summary>New numbers (the settings changed); running countdowns keep theirs.</summary>
    public void Apply(DropSettings settings) => Settings = (settings ?? new DropSettings()).Clamped();

    /// <summary>A client: the host is lost and this game tries to join again.</summary>
    public bool Reconnecting { get; private set; }

    /// <summary>This game plays on alone (the tries failed, or the host ended the session).</summary>
    public bool IsSolo { get; private set; }

    /// <summary>Join tries since the host was lost.</summary>
    public int Tries { get; private set; }

    /// <summary>When a client gives up and plays on alone (while <see cref="Reconnecting"/>).</summary>
    public double SoloAt => _soloAt;

    /// <summary>What happened since the caller last cleared it.</summary>
    public List<DropNote> Notes => _notes;

    public LinkState StateOf(int slot)
    {
        if (_mates.TryGetValue(slot, out var mate))
            return mate.State;
        foreach (var d in _dropped)
            if (d.Slot == slot)
                return LinkState.Dropped;
        return LinkState.Ok;
    }

    public bool IsDropped(int slot) => StateOf(slot) == LinkState.Dropped;

    // ------------------------------------------------------------------ in the party

    /// <summary>A teammate in the party, once a frame: how long nothing came from them (on a client, while the host
    /// is silent the others are not marked: they come through the host).</summary>
    public void Heard(int slot, string name, ulong playerId, double silence, double now)
    {
        if (!_mates.TryGetValue(slot, out var mate) || mate.PlayerId != playerId)
            _mates[slot] = mate = new Mate { Slot = slot, Name = name ?? "", PlayerId = playerId, State = LinkState.Ok, Since = now };
        mate.Name = name ?? mate.Name;
        bool silent = silence >= Settings.ProblemSeconds;
        if (silent && mate.State == LinkState.Ok)
        {
            mate.State = LinkState.Problem;
            mate.Since = now - silence;
            _notes.Add(new DropNote(DropNoteKind.Problem, slot, mate.Name, silence));
        }
        else if (!silent && mate.State == LinkState.Problem)
        {
            _notes.Add(new DropNote(DropNoteKind.ProblemOver, slot, mate.Name, now - mate.Since));
            mate.State = LinkState.Ok;
            mate.Since = now;
        }
    }

    /// <summary>A teammate joined: back from a drop when their player id was dropped.</summary>
    public void Joined(int slot, string name, ulong playerId, double now)
    {
        _mates[slot] = new Mate { Slot = slot, Name = name ?? "", PlayerId = playerId, State = LinkState.Ok, Since = now };
        for (int i = 0; i < _dropped.Count; i++)
            if (_dropped[i].PlayerId == playerId)
            {
                _notes.Add(new DropNote(DropNoteKind.Rejoined, slot, name, now - _dropped[i].Since));
                Show(new DropLine(DropLineKind.Back, name, 0), now);
                _dropped.RemoveAt(i);
                return;
            }
    }

    /// <summary>A teammate left the party: a drop keeps them for the rejoin window, a meant leave forgets them.</summary>
    public void Left(int slot, string name, ulong playerId, LeaveReason why, double now)
    {
        _mates.Remove(slot);
        _dropped.RemoveAll(d => d.PlayerId == playerId);
        if (!LeaveReasons.IsDrop(why) || Settings.RejoinWindowSeconds <= 0)
            return;
        _dropped.Add(new Mate
        {
            Slot = slot, Name = name ?? "", PlayerId = playerId, State = LinkState.Dropped, Since = now,
            Until = now + Settings.RejoinWindowSeconds,
        });
        _notes.Add(new DropNote(DropNoteKind.Dropped, slot, name, Settings.RejoinWindowSeconds));
    }

    // ------------------------------------------------------------------ a client's line to the host

    /// <summary>A client's session ended. <paramref name="wasActive"/>: it had been in the party (else a join try
    /// that failed). A drop starts the tries; the host ending it (or this game leaving) means playing on alone.</summary>
    public void HostLost(string hostName, LeaveReason why, bool wasActive, double now)
    {
        if (IsSolo)
            return;
        if (Reconnecting)
        {
            if (!wasActive)
            {
                // a try that didn't make it (no answer, or the host still had the old line: retry)
                _retryAt = now + Settings.RetrySeconds;
                return;
            }
            // welcomed again and lost again within the window: the window goes on
        }
        if (!wasActive)
            return; // never in the party: not this rule's business (a copy started before its host)
        _mates.Clear();
        _dropped.Clear();
        _hostName = string.IsNullOrEmpty(hostName) ? _hostName : hostName;
        if (LeaveReasons.IsDrop(why) && Settings.RejoinWindowSeconds > 0)
        {
            if (!Reconnecting)
            {
                Reconnecting = true;
                Tries = 0;
                _soloAt = now + Settings.RejoinWindowSeconds;
                _notes.Add(new DropNote(DropNoteKind.Dropped, 0, _hostName, Settings.RejoinWindowSeconds));
            }
            _retryAt = now + Settings.RetrySeconds;
            return;
        }
        GoSolo(why == LeaveReason.HostLeft ? DropLineKind.HostLeftAlone : LeaveReasons.IsDrop(why) ? DropLineKind.LostAlone : (DropLineKind?)null, now);
    }

    /// <summary>A client while <see cref="Reconnecting"/> and no try is under way: true once when the next try is due.</summary>
    public bool RetryDue(double now)
    {
        if (!Reconnecting || now < _retryAt)
            return false;
        _retryAt = double.PositiveInfinity; // until this try ends (HostLost) or makes it (Reconnected)
        Tries++;
        _notes.Add(new DropNote(DropNoteKind.RetryNow, 0, _hostName, _soloAt - now));
        return true;
    }

    /// <summary>A client's try made it: the host welcomed this game again.</summary>
    public void Reconnected(double now)
    {
        if (!Reconnecting)
            return;
        Reconnecting = false;
        _notes.Add(new DropNote(DropNoteKind.Rejoined, 0, _hostName, now - (_soloAt - Settings.RejoinWindowSeconds)));
        Show(new DropLine(DropLineKind.Back, _hostName, 0), now);
    }

    // ------------------------------------------------------------------ every frame

    /// <summary>Windows that ran out. <paramref name="teammates"/>: how many others are still in the party.</summary>
    public void Tick(double now, int teammates)
    {
        for (int i = 0; i < _dropped.Count; i++)
        {
            var d = _dropped[i];
            if (now < d.Until)
                continue;
            _dropped.RemoveAt(i--);
            _notes.Add(new DropNote(DropNoteKind.Gone, d.Slot, d.Name, now - d.Since));
            Show(new DropLine(teammates > 0 ? DropLineKind.Lost : DropLineKind.LostAlone, d.Name, 0), now);
        }
        if (Reconnecting && now >= _soloAt)
            GoSolo(DropLineKind.LostAlone, now);
    }

    private void GoSolo(DropLineKind? message, double now)
    {
        Reconnecting = false;
        IsSolo = true;
        _mates.Clear();
        _dropped.Clear();
        _notes.Add(new DropNote(DropNoteKind.Solo, 0, _hostName, Tries));
        if (message.HasValue)
            Show(new DropLine(message.Value, _hostName, 0), now);
    }

    /// <summary>Back to the start (a new session of this game's own choosing: hosting or joining from the window).</summary>
    public void Reset()
    {
        _mates.Clear();
        _dropped.Clear();
        _notes.Clear();
        _message = null;
        Reconnecting = IsSolo = false;
        Tries = 0;
    }

    private void Show(DropLine line, double now)
    {
        _message = line;
        _messageUntil = now + Settings.MessageSeconds;
    }

    /// <summary>The lines to show now, most important first.</summary>
    public void Lines(List<DropLine> into, double now)
    {
        into.Clear();
        if (Reconnecting)
            into.Add(new DropLine(DropLineKind.Reconnecting, _hostName, Up(_soloAt - now)));
        else
        {
            bool hostSilent = _mates.TryGetValue(0, out var host) && host.State == LinkState.Problem;
            foreach (var mate in _mates.Values)
                if (mate.State == LinkState.Problem && (!hostSilent || mate.Slot == 0))
                    into.Add(new DropLine(DropLineKind.Problem, mate.Name, (int)Math.Floor(now - mate.Since)));
            foreach (var d in _dropped)
                into.Add(new DropLine(DropLineKind.WaitingFor, d.Name, Up(d.Until - now)));
        }
        if (_message.HasValue && now < _messageUntil)
            into.Add(_message.Value);
    }

    private static int Up(double seconds) => Math.Max(0, (int)Math.Ceiling(seconds - 1e-9));
}
