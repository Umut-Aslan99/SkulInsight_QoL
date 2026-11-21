using System;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;

namespace DamageInsight.Coop;

/// <summary>
/// The host's numbers for the rules every game plays by (COOP_PLAN 4: "host settings, like the balance numbers"):
/// the enemy health table (CoopHealthScaling) and the revive numbers (CoopRevive: hold time, reach, health after a
/// revive and after the room's clear, revives per room). On the host they come from its settings (Coop/Balance,
/// Coop/ReviveHoldSeconds, ...); each client gets them when it joins and again whenever the host changes one
/// (MessageType.HostRules). Until then a client plays by the defaults (the same as an unchanged host). The settings
/// objects are shared: the rules that hold them see a change at once.
/// </summary>
// REVIEW(fable): a host -> client message (MessageType.HostRules, REVIEW_QUEUE entry 4); a client's own revive hold
// now runs on the host's hold time and reach.
internal sealed class CoopHostRules
{
    public static CoopHostRules Current { get; private set; }

    public BalanceSettings Balance { get; } = new();
    public ReviveSettings Revive { get; } = new();

    private readonly CoopSession _session;
    private readonly NetWriter _writer = new(96);

    public CoopHostRules(CoopSession session)
    {
        _session = session;
        Current = this;
        session.RegisterHandler(MessageType.HostRules, OnMessage);
        Balance.ScaleTo(BalanceSettings.DefaultTwoPlayers); // a client until the host's numbers come: an unchanged host's
        if (!session.IsHost)
            return;
        LoadOwn();
        session.PlayerJoined += OnJoined;
        Watch(true);
    }

    public void Dispose()
    {
        _session.PlayerJoined -= OnJoined;
        Watch(false);
        if (Current == this)
            Current = null;
    }

    private void Watch(bool on)
    {
        Toggle(Plugin.CoopBalanceText, on);
        Toggle(Plugin.CoopEnemyHealth, on);
        Toggle(Plugin.CoopReviveHoldSeconds, on);
        Toggle(Plugin.CoopRevivesPerRoom, on);
        Toggle(Plugin.CoopReviveHealth, on);
        Toggle(Plugin.CoopRoomClearHealth, on);
    }

    private void Toggle<T>(BepInEx.Configuration.ConfigEntry<T> entry, bool on)
    {
        if (entry == null)
            return;
        entry.SettingChanged -= OnSettingChanged;
        if (on)
            entry.SettingChanged += OnSettingChanged;
    }

    private void LoadOwn()
    {
        string text = Plugin.CoopBalanceText?.Value;
        if (string.IsNullOrWhiteSpace(text) || !Balance.TryParse(text))
            Balance.ResetToDefaults();
        Balance.ScaleTo(Plugin.CoopEnemyHealth?.Value ?? BalanceSettings.DefaultTwoPlayers);
        Revive.HoldSeconds = Plugin.CoopReviveHoldSeconds?.Value ?? ReviveSettings.DefaultHoldSeconds;
        Revive.RevivesPerRoom = Plugin.CoopRevivesPerRoom?.Value ?? ReviveSettings.DefaultRevivesPerRoom;
        Revive.ReviveHealth = Plugin.CoopReviveHealth?.Value ?? ReviveSettings.DefaultReviveHealth;
        Revive.RoomClearHealth = Plugin.CoopRoomClearHealth?.Value ?? ReviveSettings.DefaultRoomClearHealth;
        Revive.Clamp();
    }

    private void OnJoined(RosterEntry player) => Send(player.Slot);

    private void OnSettingChanged(object sender, EventArgs e)
    {
        LoadOwn();
        CoopHealthScaling.Current?.Rescale(); // live: the enemies already there too (and on every client when it arrives)
        foreach (var player in _session.Roster)
            if (player.Slot != _session.LocalSlot)
                Send(player.Slot);
    }

    private void Send(int slot)
    {
        _writer.Reset();
        HostRulesMessages.Write(_writer, HostRules.From(Balance, Revive));
        _session.Send(slot, MessageType.HostRules, _writer, Delivery.Reliable);
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (_session.IsHost || message.FromSlot != 0)
            return; // only the host's numbers count
        if (!HostRulesMessages.TryRead(payload, out var rules))
            return;
        if (!rules.ApplyTo(Balance, Revive))
            Plugin.Log.LogWarning("[Coop] the host's balance numbers could not be read: " + rules.Balance);
        CoopHealthScaling.Current?.Rescale();
        CoopTrace.Write("host-rules", what: rules.Balance + $" | hold {Revive.HoldSeconds:0.0} s, {Revive.RevivesPerRoom} per room");
    }
}
