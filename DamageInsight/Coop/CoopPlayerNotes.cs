using System;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;

namespace DamageInsight.Coop;

/// <summary>
/// The players' notes for the Co-op window (<see cref="PlayerNote"/>): this game sends its own (Dark Mirror progress,
/// its request about the host's enemy health, OK) to everyone through the host's relay when it changes, when a
/// player joins and every <see cref="ResendSeconds"/>; the others' come in the same way. The host's value is the one
/// every game plays by (<see cref="CoopHostRules"/>): its two-player number. On the host the window's arrows change
/// the setting itself (Coop/EnemyHealth); on a client they only ask.
/// </summary>
// REVIEW(fable): a new relayed message (MessageType.PlayerNote, REVIEW_QUEUE entry 23); only the Co-op window reads it.
internal sealed class CoopPlayerNotes
{
    public const double CheckSeconds = 1.0, ResendSeconds = 15.0;

    public static CoopPlayerNotes Current { get; private set; }

    public PartyNotes Notes { get; } = new();

    private readonly CoopSession _session;
    private readonly CoopHostRules _rules;
    private readonly NetWriter _writer = new(16);
    private bool _dirty = true;
    private double _nextCheck, _nextSend;

    public CoopPlayerNotes(CoopSession session, CoopHostRules rules)
    {
        _session = session;
        _rules = rules;
        Current = this;
        session.RegisterHandler(MessageType.PlayerNote, OnMessage);
        session.SetRelay(MessageType.PlayerNote);
        session.PlayerJoined += OnJoined;
        session.PlayerLeft += OnLeft;
        Check();
    }

    public void Dispose()
    {
        _session.PlayerJoined -= OnJoined;
        _session.PlayerLeft -= OnLeft;
        if (Current == this)
            Current = null;
    }

    /// <summary>The two-player enemy health every game plays by now (the host's slider value).</summary>
    public float HostValue => _rules.Balance.Get(2, EnemyCategory.Normal);

    public CoopSession Session => _session;

    /// <summary>A client's arrow: asks the host for more or less (the same arrow again takes it back).</summary>
    public void Ask(HealthRequest request)
    {
        Check();
        Notes.Ask(request);
        SendSoon();
        CoopTrace.Write("health-request", what: request + " at " + Notes.HostValue);
    }

    /// <summary>A client's OK box.</summary>
    public void ToggleOk()
    {
        Check();
        Notes.ToggleOk();
        SendSoon();
        CoopTrace.Write("health-ok", what: (Notes.Own.Ok ? "on" : "off") + " at " + Notes.HostValue);
    }

    private void SendSoon()
    {
        _dirty = true;
        _nextCheck = 0;
    }

    private void OnJoined(RosterEntry player) => _dirty = true; // the newcomer hears everyone's note

    private void OnLeft(RosterEntry player, LeaveReason why) => Notes.Forget(player.Slot);

    /// <summary>Once a frame while the session runs (CoopRuntime).</summary>
    public void Tick(double now)
    {
        if (now >= _nextCheck)
        {
            _nextCheck = now + CheckSeconds;
            Check();
        }
        if (!_dirty && now < _nextSend)
            return;
        _dirty = false;
        _nextSend = now + ResendSeconds;
        _writer.Reset();
        Notes.Own.Write(_writer);
        _session.Broadcast(MessageType.PlayerNote, _writer, Delivery.Reliable);
    }

    private void Check()
    {
        if (Notes.SetProgress(OwnClearedLevel()))
            _dirty = true;
        if (Notes.SetHostValue(BalanceSettings.Hundredths(HostValue)))
            _dirty = true;
    }

    /// <summary>This game's own Dark Mirror progress (co-op never lends it: DarkMirrorLevel keeps the cleared level
    /// this game's own).</summary>
    private static int OwnClearedLevel()
    {
        try { return Math.Max(-1, Math.Min(10, Data.GameData.HardmodeProgress.clearedLevel)); }
        catch (Exception) { return PlayerNote.UnknownProgress; }
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (!PlayerNote.TryRead(payload, out var note))
            return;
        Notes.Heard(message.FromSlot, note);
    }
}
