using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.Coop.Match;

/// <summary>Where a matchmaking search stands (docs/coop/MATCHMAKING.md).</summary>
public enum MatchState
{
    /// <summary>Not searching.</summary>
    Idle,
    /// <summary>The ticket is on its way to the server.</summary>
    Queueing,
    /// <summary>In the queue: position, players looking, estimated wait.</summary>
    Queued,
    /// <summary>A partner was found: Accept or Decline within 15 s (the Match found box).</summary>
    Proposed,
    /// <summary>This player accepted; waiting for the others.</summary>
    Accepted,
    /// <summary>Everyone accepted: the host and the Steam ids are known; the runtime connects.</summary>
    Confirmed,
    /// <summary>Connected (or given up connecting): the server was told "done".</summary>
    Done,
    Failed,
    /// <summary>Too many declines: a pause (409, <see cref="MatchClient.CooldownSecondsLeft"/>).</summary>
    Cooldown,
    /// <summary>The server has matchmaking off (404 coop_off).</summary>
    ServerOff,
    /// <summary>This player stopped the search (or declined a match).</summary>
    Cancelled,
}

/// <summary>Why a search failed or ended.</summary>
public enum MatchProblem
{
    None,
    Offline,
    ServerError,
    BadField,
    VersionRefused,
    QueueFull,
    RateLimited,
    /// <summary>The server does not know the ticket any more (not polled for 30 s).</summary>
    Lost,
    /// <summary>The proposal was not answered in time.</summary>
    Timeout,
    Expired,
    /// <summary>Another ticket of this player (another game) took its place.</summary>
    Replaced,
    Declined,
}

/// <summary>The HTTP calls of the matchmaking client: injected (UnityWebRequest in the game, a fake in the tests).</summary>
public interface IMatchHttp
{
    /// <summary>One request to <paramref name="path"/> below the server's address ("/api/coop/queue"). <paramref name="done"/>
    /// gets the status code (0 = no answer: no connection, a time-out) and the body; it runs on the main thread, later.</summary>
    void Send(string method, string path, string body, int timeoutSeconds, Action<long, string> done);
}

/// <summary>A matchmaking ticket: who, what they want to play, their Steam ping location (MATCHMAKING.md, POST /queue).</summary>
public sealed class MatchTicket
{
    public string Player = "", SteamId = "", Name = "", Mod = "", ModListHash = "";
    public int ProtocolVersion;
    /// <summary>"normal" | "darkmirror" | "any".</summary>
    public string Mode = "normal";
    public int DmMin, DmMax = 10, DmUnlocked = -1;
    public bool Dlc;
    public string DlcRun = "any";
    public int Size = 2;
    /// <summary>Optional (null or "" = any language).</summary>
    public string Language;
    public int MaxPing = 200;
    public bool CanHost = true;
    public string PingLocation = "";

    public string ToJson()
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        void Str(string key, string value) => sb.Append(sb.Length > 1 ? "," : "").Append('"').Append(key).Append("\":").Append(LogJson.Quote(value ?? ""));
        void Num(string key, int value) => sb.Append(sb.Length > 1 ? "," : "").Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        void Bool(string key, bool value) => sb.Append(sb.Length > 1 ? "," : "").Append('"').Append(key).Append("\":").Append(value ? "true" : "false");
        Str("player", Player);
        Str("steamId", SteamId);
        Str("name", Name);
        Str("mod", Mod);
        Num("protocolVersion", ProtocolVersion);
        Str("modListHash", ModListHash);
        Str("mode", Mode);
        Num("dmMin", Math.Max(0, Math.Min(10, DmMin)));
        Num("dmMax", Math.Max(0, Math.Min(10, DmMax)));
        Num("dmUnlocked", Math.Max(-1, Math.Min(10, DmUnlocked)));
        Bool("dlc", Dlc);
        Str("dlcRun", DlcRun);
        Num("size", Size);
        if (!string.IsNullOrEmpty(Language))
            Str("language", Language);
        Num("maxPing", Math.Max(30, Math.Min(1000, MaxPing)));
        Bool("canHost", CanHost);
        Str("pingLocation", PingLocation ?? "");
        sb.Append('}');
        return sb.ToString();
    }
}

/// <summary>A member of a proposed or confirmed match, as the server shows them.</summary>
public sealed class MatchMember
{
    public string Name = "", PingLocation = "", SteamId = "";
    public bool You, Accepted, Host;
    /// <summary>The server's estimate in ms; -1 = none (this player).</summary>
    public int EstimatedPing = -1;
}

/// <summary>The "match" part of a proposed or confirmed status.</summary>
public sealed class MatchInfo
{
    public string Id = "", Mode = "normal", HostName = "", HostSteamId = "";
    public int Level = -1, Size = 2, ExpiresIn, EstimatedPing = -1;
    public bool Dlc, Accepted, YouHost;
    public readonly List<MatchMember> Members = new();

    public IEnumerable<MatchMember> Partners => Members.Where(m => !m.You);
}

/// <summary>One answer of GET /queue/{ticket} (and of POST /queue, accept, decline).</summary>
public sealed class MatchStatus
{
    public string State = "";
    public int Version = -1, WaitSeconds, MaxPing, Position, Looking;
    /// <summary>Seconds; -1 = not known yet (no matches this hour).</summary>
    public int EstimatedWait = -1;
    /// <summary>"" | partner_declined | partner_timeout | ping_check_failed.</summary>
    public string Notice = "";
    public string Ticket = "";
    public MatchInfo Match;

    public bool Ended => Version < 0;

    /// <summary>null when the text is no status.</summary>
    public static MatchStatus Parse(string json)
    {
        Node n;
        try { n = string.IsNullOrWhiteSpace(json) ? default : GearDoc.ParseNode(json); }
        catch (Exception) { return null; }
        if (n.IsNull || n.Str("state") is not { Length: > 0 } state)
            return null;
        var s = new MatchStatus
        {
            State = state,
            Version = (int)n.Num("version", -1),
            WaitSeconds = (int)n.Num("waitSeconds"),
            MaxPing = (int)n.Num("maxPing"),
            Position = (int)n.Num("position"),
            Looking = (int)n.Num("looking"),
            EstimatedWait = n.Raw("estimatedWait") is double wait ? (int)wait : -1,
            Notice = n.Str("notice") ?? "",
            Ticket = n.Str("ticket") ?? "",
        };
        var m = n.Child("match");
        if (!m.IsNull)
        {
            var info = new MatchInfo
            {
                Id = m.Str("id") ?? "",
                Mode = m.Str("mode") ?? "normal",
                Level = (int)m.Num("level", -1),
                Dlc = m.Bool("dlc"),
                Size = (int)m.Num("size", 2),
                ExpiresIn = (int)m.Num("expiresIn"),
                Accepted = m.Bool("accepted"),
                EstimatedPing = m.Raw("estimatedPing") is double ping ? (int)ping : -1,
                YouHost = m.Bool("youHost"),
            };
            var host = m.Child("host");
            if (!host.IsNull)
            {
                info.HostName = host.Str("name") ?? "";
                info.HostSteamId = host.Str("steamId") ?? "";
            }
            foreach (var member in m.List("members"))
                info.Members.Add(new MatchMember
                {
                    Name = member.Str("name") ?? "",
                    You = member.Bool("you"),
                    Accepted = member.Bool("accepted"),
                    Host = member.Bool("host"),
                    EstimatedPing = member.Raw("estimatedPing") is double p ? (int)p : -1,
                    PingLocation = member.Str("pingLocation") ?? "",
                    SteamId = member.Str("steamId") ?? "",
                });
            s.Match = info;
        }
        return s;
    }

    /// <summary>The "detail" (and "seconds") of a refusal: {"detail": "cooldown", "seconds": 287}.</summary>
    public static string DetailOf(string json, out int seconds)
    {
        seconds = 0;
        try
        {
            var n = string.IsNullOrWhiteSpace(json) ? default : GearDoc.ParseNode(json);
            seconds = (int)n.Num("seconds");
            return n.Str("detail") ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }
}

/// <summary>
/// The matchmaking client (co-op package MM; the server is package I, docs/coop/MATCHMAKING.md): Start queues a ticket,
/// then a long poll (GET /queue/{ticket}?since=&amp;wait=20) follows the ticket: queued (position, looking) -> proposed
/// (the Match found box; Steam's own ping estimate first: over the limit = decline with reason "ping") -> accepted ->
/// confirmed (the host; <see cref="Confirmed"/> hands it to the runtime, which connects and then calls
/// <see cref="Finish"/> = DELETE "done"). Plain logic: HTTP, the clock and Steam's estimate are injected. Every answer
/// of an older search (after Cancel or a new Start) is dropped.
/// </summary>
public sealed class MatchClient
{
    public const string Api = "/api/coop";
    public const int PollWait = 20, PollTimeout = 35, CallTimeout = 10;
    /// <summary>A failed poll is tried again after this; a ticket not polled for 30 s is gone on the server.</summary>
    public const double RetrySeconds = 3, LostAfterSeconds = 25;
    /// <summary>The server is told "done" this long after the confirmation at the latest (no session came up).</summary>
    public const double FinishAfterSeconds = 30;

    private readonly IMatchHttp _http;
    private readonly Func<double> _now;
    private readonly Func<string, int> _estimate;
    private int _generation;
    private bool _polling;
    private double _retryAt = -1, _lastAnswer, _confirmedAt;
    private string _declinedMatch = "";

    /// <param name="estimatePing">Steam's own estimate to a partner's ping location in ms, -1 = unknown.</param>
    public MatchClient(IMatchHttp http, Func<double> now, Func<string, int> estimatePing = null)
    {
        _http = http;
        _now = now;
        _estimate = estimatePing ?? (_ => -1);
    }

    public MatchState State { get; private set; } = MatchState.Idle;
    public MatchProblem Problem { get; private set; }
    /// <summary>The server's word for a problem (a field name, a status code), for the log.</summary>
    public string Detail { get; private set; } = "";
    public MatchTicket Ticket { get; private set; }
    /// <summary>The server's ticket id: the bearer secret of every later call. "" before the answer.</summary>
    public string TicketId { get; private set; } = "";
    /// <summary>The newest status, or null.</summary>
    public MatchStatus Last { get; private set; }
    /// <summary>The proposal, then the confirmed match.</summary>
    public MatchInfo Match { get; private set; }
    public double SearchingSince { get; private set; }
    public double ProposalDeadline { get; private set; }
    /// <summary>Steam's estimate to the worst partner of the proposal (-1 = unknown).</summary>
    public int CheckedPing { get; private set; } = -1;
    /// <summary>Test bots: accept every proposal at once.</summary>
    public bool AutoAccept { get; set; }

    private double _cooldownUntil;

    public int CooldownSecondsLeft => State == MatchState.Cooldown ? (int)Math.Ceiling(Math.Max(0, _cooldownUntil - _now())) : 0;
    public int ProposalSecondsLeft => State is MatchState.Proposed or MatchState.Accepted ? (int)Math.Ceiling(Math.Max(0, ProposalDeadline - _now())) : 0;
    public int SearchSeconds => Searching ? (int)Math.Max(0, _now() - SearchingSince) : 0;

    public bool Searching => State is MatchState.Queueing or MatchState.Queued or MatchState.Proposed or MatchState.Accepted;
    public bool CanStart => !Searching && State != MatchState.Confirmed && State != MatchState.Cooldown;

    /// <summary>The state changed (the window and the box redraw).</summary>
    public event Action Changed;
    /// <summary>A line for the log (English).</summary>
    public event Action<string> Log;
    /// <summary>A trace event: kind ("match-queued", ...), detail.</summary>
    public event Action<string, string> Trace;
    /// <summary>Everyone accepted: the runtime connects (host or client) and calls <see cref="Finish"/>.</summary>
    public event Action<MatchInfo> Confirmed;

    // ---------------------------------------------------------------- the player's actions

    /// <summary>Queues the ticket (a search already under way is left first).</summary>
    public void Start(MatchTicket ticket)
    {
        if (Searching || State == MatchState.Confirmed)
            Cancel();
        int generation = ++_generation;
        Ticket = ticket;
        TicketId = "";
        Last = null;
        Match = null;
        Problem = MatchProblem.None;
        Detail = "";
        CheckedPing = -1;
        _polling = false;
        _retryAt = -1;
        _declinedMatch = "";
        SearchingSince = _lastAnswer = _now();
        SetState(MatchState.Queueing);
        // what the server matches by, for the log (11-20: no match came, the trace could not say why)
        Write("match-queue", ticket.Mode + ", max ping " + ticket.MaxPing + (ticket.CanHost ? "" : ", no host")
                             + ", language " + (string.IsNullOrEmpty(ticket.Language) ? "any" : ticket.Language)
                             + ", ping location " + (string.IsNullOrEmpty(ticket.PingLocation) ? "NONE (the server guesses 250 ms)" : "sent")
                             + ", mods " + (ticket.ModListHash ?? "").Substring(0, Math.Min(8, (ticket.ModListHash ?? "").Length)));
        _http.Send("POST", Api + "/queue", ticket.ToJson(), CallTimeout, (code, text) =>
        {
            var status = code == 200 ? MatchStatus.Parse(text) : null;
            if (generation != _generation)
            {
                // Cancelled while the ticket was on its way: take it off the queue again.
                if (status != null && status.Ticket.Length > 0)
                    _http.Send("DELETE", Api + "/queue/" + status.Ticket, null, CallTimeout, (_, _) => { });
                return;
            }
            if (status != null && status.Ticket.Length > 0)
            {
                TicketId = status.Ticket;
                _lastAnswer = _now();
                Apply(status, generation);
                return;
            }
            Refused(code, text, queueing: true);
        });
    }

    /// <summary>Stops the search (DELETE: a pending proposal fails for the others, no cooldown). Also leaves a
    /// confirmed match that was not connected.</summary>
    public void Cancel()
    {
        if (!Searching && State != MatchState.Confirmed)
            return;
        _generation++;
        _polling = false;
        if (TicketId.Length > 0)
            _http.Send("DELETE", Api + "/queue/" + TicketId, null, CallTimeout, (_, _) => { });
        Write("match-cancel", State.ToString());
        Problem = MatchProblem.None;
        SetState(MatchState.Cancelled);
    }

    public void Accept()
    {
        if (State != MatchState.Proposed || Match == null)
            return;
        int generation = _generation;
        Write("match-accept", Match.Id);
        SetState(MatchState.Accepted);
        Answer(generation, "accept", "{\"ticket\":" + LogJson.Quote(TicketId) + "}");
    }

    /// <summary>Declines the proposal: "player" counts for the cooldown, "ping" (Steam's estimate said no) is free and
    /// keeps the search going.</summary>
    public void Decline(string reason = "player")
    {
        if (State is not (MatchState.Proposed or MatchState.Accepted) || Match == null)
            return;
        int generation = _generation;
        _declinedMatch = Match.Id;
        Write("match-decline", reason);
        Answer(generation, "decline", "{\"ticket\":" + LogJson.Quote(TicketId) + ",\"reason\":" + LogJson.Quote(reason) + "}");
        if (reason == "ping")
            SetState(MatchState.Queued); // free: back in the queue, the search goes on
        else
        {
            // The server takes this player off the queue ("declined"): the search is over here.
            _generation++;
            _polling = false;
            Problem = MatchProblem.Declined;
            SetState(MatchState.Cancelled);
        }
    }

    /// <summary>After the confirmation: the session is up (or connecting was given up). DELETE = "done".</summary>
    public void Finish()
    {
        if (State != MatchState.Confirmed)
            return;
        _generation++;
        if (TicketId.Length > 0)
            _http.Send("DELETE", Api + "/queue/" + TicketId, null, CallTimeout, (_, _) => { });
        Write("match-done", Match?.YouHost == true ? "host" : "client");
        SetState(MatchState.Done);
    }

    /// <summary>Back to Idle after a search ended (the window's state line forgets the old result).</summary>
    public void Clear()
    {
        if (Searching || State == MatchState.Confirmed)
            return;
        Problem = MatchProblem.None;
        SetState(MatchState.Idle);
    }

    /// <summary>Every frame: a poll after a failed one, the end of a cooldown, "done" when no session came up.</summary>
    public void Tick()
    {
        double now = _now();
        if (State == MatchState.Cooldown && now >= _cooldownUntil)
            SetState(MatchState.Idle);
        if (Searching && TicketId.Length > 0 && !_polling && _retryAt >= 0 && now >= _retryAt)
        {
            if (now - _lastAnswer > LostAfterSeconds)
            {
                Fail(MatchProblem.Offline, "no answer for " + (int)(now - _lastAnswer) + " s");
                return;
            }
            _retryAt = -1;
            Poll(_generation);
        }
        if (State == MatchState.Confirmed && now - _confirmedAt > FinishAfterSeconds)
        {
            Say("no session " + (int)FinishAfterSeconds + " s after the match: the server is told done");
            Finish();
        }
    }

    // ---------------------------------------------------------------- answers

    private void Answer(int generation, string what, string body)
    {
        string id = Match?.Id ?? "";
        _http.Send("POST", Api + "/match/" + id + "/" + what, body, CallTimeout, (code, text) =>
        {
            if (generation != _generation)
                return;
            if (code == 200 && MatchStatus.Parse(text) is { } status)
            {
                _lastAnswer = _now();
                Apply(status, generation);
            }
            else
                Say($"{what} answered {code}: {Short(text)}"); // the poll tells what happened
        });
    }

    private void Poll(int generation)
    {
        if (_polling || TicketId.Length == 0)
            return;
        _polling = true;
        int since = Last?.Version ?? 0;
        _http.Send("GET", Api + "/queue/" + TicketId + "?since=" + since.ToString(CultureInfo.InvariantCulture) + "&wait=" + PollWait,
            null, PollTimeout, (code, text) =>
            {
                if (generation != _generation)
                    return;
                _polling = false;
                var status = code == 200 ? MatchStatus.Parse(text) : null;
                if (status != null)
                {
                    _lastAnswer = _now();
                    Apply(status, generation);
                    return;
                }
                string detail = MatchStatus.DetailOf(text, out _);
                if (code == 404 && detail == "coop_off")
                    Off();
                else if (code == 404)
                    Fail(MatchProblem.Lost, detail);
                else if (code is >= 400 and < 500 and not 429 and not 408)
                    Fail(MatchProblem.ServerError, code + " " + detail);
                else
                    _retryAt = _now() + RetrySeconds; // no connection, a time-out, 429, 5xx: again in a moment
            });
    }

    private void Apply(MatchStatus s, int generation)
    {
        if (generation != _generation)
            return;
        if (s.Ended)
        {
            Ended(s.State);
            return;
        }
        if (Last != null && s.Version < Last.Version)
        {
            Poll(generation); // an older answer (a poll and an accept crossed)
            return;
        }
        Last = s;
        switch (s.State)
        {
            case "queued":
                if (State is MatchState.Proposed or MatchState.Accepted)
                    Write("match-requeued", s.Notice);
                else if (State == MatchState.Queueing)
                    Write("match-queued", $"position {s.Position}, looking {s.Looking}");
                Match = null;
                SetState(MatchState.Queued);
                break;
            case "proposed":
                if (s.Match == null)
                    break;
                bool fresh = Match == null || Match.Id != s.Match.Id || State is not (MatchState.Proposed or MatchState.Accepted);
                Match = s.Match;
                if (fresh && s.Match.Id != _declinedMatch)
                    Proposed(s, generation);
                else if (s.Match.Accepted && State == MatchState.Proposed)
                    SetState(MatchState.Accepted);
                else
                    Changed?.Invoke(); // a partner accepted
                break;
            case "confirmed":
                if (s.Match == null)
                    break;
                Match = s.Match;
                _confirmedAt = _now();
                _polling = false;
                Write("match-confirmed", (s.Match.YouHost ? "host" : "client") + ", " + s.Match.Mode +
                                         (s.Match.Level >= 0 && s.Match.Mode == "darkmirror" ? " " + s.Match.Level : ""));
                SetState(MatchState.Confirmed);
                Confirmed?.Invoke(s.Match);
                return; // no more polls: the match is the runtime's now
        }
        if (Searching && State != MatchState.Queueing && generation == _generation)
            Poll(generation);
    }

    private void Proposed(MatchStatus s, int generation)
    {
        // Steam's own estimate to every partner first (the server's is only for the order).
        int worst = -1;
        foreach (var partner in s.Match.Partners)
            if (partner.PingLocation.Length > 0)
            {
                int ping = _estimate(partner.PingLocation);
                if (ping >= 0)
                    worst = Math.Max(worst, ping);
            }
        CheckedPing = worst;
        int limit = s.MaxPing > 0 ? s.MaxPing : Ticket?.MaxPing ?? 200;
        if (worst > limit)
        {
            Write("match-ping-decline", $"Steam estimate {worst} ms, limit {limit} ms");
            Match = s.Match;
            State = MatchState.Proposed; // (no box: Decline puts it back to Queued at once)
            Decline("ping");
            return;
        }
        ProposalDeadline = _now() + Math.Max(1, s.Match.ExpiresIn);
        Write("match-proposed", $"{string.Join("+", s.Match.Partners.Select(p => p.Name))}, server {s.Match.EstimatedPing} ms, steam {worst} ms");
        SetState(s.Match.Accepted ? MatchState.Accepted : MatchState.Proposed);
        if (AutoAccept && State == MatchState.Proposed && generation == _generation)
            Accept();
    }

    private void Ended(string why)
    {
        _polling = false;
        switch (why)
        {
            case "done":
                SetState(MatchState.Done);
                break;
            case "declined":
                Problem = MatchProblem.Declined;
                SetState(MatchState.Cancelled);
                break;
            case "cancelled":
                SetState(MatchState.Cancelled);
                break;
            case "timeout":
                Fail(MatchProblem.Timeout, why);
                break;
            case "replaced":
                Fail(MatchProblem.Replaced, why);
                break;
            default:
                Fail(MatchProblem.Expired, why);
                break;
        }
        _generation++;
    }

    private void Refused(long code, string text, bool queueing)
    {
        string detail = MatchStatus.DetailOf(text, out int seconds);
        if (code == 409 && detail == "cooldown")
        {
            _cooldownUntil = _now() + Math.Max(1, seconds);
            Detail = seconds + " s";
            Write("match-cooldown", seconds + " s");
            SetState(MatchState.Cooldown);
        }
        else if (code == 404 && detail == "coop_off")
            Off();
        else if (code == 0)
            Fail(MatchProblem.Offline, "no connection");
        else if (code == 400 && detail is "protocolVersion" or "modListHash")
            Fail(MatchProblem.VersionRefused, detail);
        else if (code is 400 or 422)
            Fail(MatchProblem.BadField, detail.Length > 0 ? detail : Short(text));
        else if (code == 503)
            Fail(MatchProblem.QueueFull, detail);
        else if (code == 429)
            Fail(MatchProblem.RateLimited, detail);
        else
            Fail(MatchProblem.ServerError, code + " " + detail);
    }

    private void Off()
    {
        _generation++;
        _polling = false;
        Write("match-server-off", "");
        SetState(MatchState.ServerOff);
    }

    private void Fail(MatchProblem problem, string detail)
    {
        _generation++;
        _polling = false;
        Problem = problem;
        Detail = detail ?? "";
        Write("match-failed", problem + (Detail.Length > 0 ? ": " + Detail : ""));
        SetState(MatchState.Failed);
    }

    private void SetState(MatchState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private void Write(string kind, string detail)
    {
        Trace?.Invoke(kind, detail);
        Say(kind + (detail.Length > 0 ? ": " + detail : ""));
    }

    private void Say(string text) => Log?.Invoke("matchmaking: " + text);

    private static string Short(string text) => text == null ? "" : text.Length > 120 ? text.Substring(0, 120) : text;
}
