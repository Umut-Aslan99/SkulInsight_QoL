using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Characters;
using Data;
using DamageInsight.Coop.Net;
using GameResources;
using HarmonyLib;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// The shared world's loot for every player (COOP_PLAN §4.2: "every player gets the full gold from enemies ... into
/// their own wallet"; Umut 2026-11-14: "My host has 692 gold, but the client has 7 only. He didnt get rewarded for
/// any kills or gold piles i destroyed on my host"). Why the client got nothing:
/// - An enemy's loot (gold, dark quartz, a healing orb: Characters.DropLootOnDie) drops only where the enemy really
///   dies. Enemies die on the host; a client's copy dies by Health.Kill(), and the game drops nothing for a Kill.
/// - A prop (barrel, crate, gold pile) breaks only in the game of the player who hit it (a teammate's copy hits
///   nothing here), and its gold drops there.
/// What this does:
/// - ENEMIES: the host tells every client what each synced enemy dropped and where; the client's game drops the
///   same coins and the same healing orb (the game's own coins: they fly out and go into the wallet by themselves,
///   with this player's own gold bonuses). A client that is not in that room any more gets the amount directly.
/// - PROPS: a plain prop that breaks in one game breaks in every game (the game's own hit, so sprites, sounds and
///   wreckage are the game's) and drops there what it dropped for the one who broke it. "Plain" = nothing listens
///   to it: cages, altars, spawner props and wave props do things when they break and stay each game's own for now.
/// - Not shared, on purpose (the plan: rewards are per player): room rewards, chests, currency bags, shop wares,
///   NPC gifts, what a player's own gear gives (thief gold, discarded gear, bones for a skull). Dark enemies' heart
///   quartz already drops on every game (another event of the game).
/// Every drop writes a trace line (`loot-sent`, `loot-got`, `loot-own` with where it came from, `prop-broken`,
/// `prop-followed`), so a test run shows which sources exist and what happened to each.
/// </summary>
internal sealed class SharedLoot : IDisposable
{
    internal static SharedLoot Current { get; private set; }

    private enum Source
    {
        /// <summary>Nothing of ours runs: a drop is this game's own (a reward, an item's effect, ...).</summary>
        None,
        /// <summary>An enemy the host runs is dropping its loot.</summary>
        Enemy,
        /// <summary>An enemy each game runs for itself is dropping its loot: this game's own.</summary>
        OwnEnemy,
        /// <summary>A plain prop is being hit in this game: what drops is collected for the other games.</summary>
        Prop,
        /// <summary>A drop another game told us about is made here.</summary>
        Told,
        /// <summary>A prop is broken here because it broke in another game: its own roll does not count.</summary>
        Quiet,
    }

    private const float PropReach = 0.4f;
    private const int MaxFollowHits = 64;

    private static readonly FieldInfo PropHitListeners = AccessTools.Field(typeof(Prop), "onDidHit");

    private readonly CoopSession _session;
    private readonly Func<int, Character> _playerOf;
    private readonly NetWriter _writer = new();
    private readonly List<LootDrop> _collected = new();
    private Source _source;
    private string _from = "";
    private double _nextWallet;
    private string _wallet = "";

    public SharedLoot(CoopSession session, Func<int, Character> playerOf)
    {
        _session = session;
        _playerOf = playerOf;
        session.RegisterHandler(MessageType.Loot, OnMessage);
        Current = this;
    }

    public void Dispose()
    {
        if (Current == this)
            Current = null;
    }

    private bool Active => _session.State == SessionState.Active && _session.Roster.Count > 1;

    private static LevelManager Levels => Singleton<Service>.Instance?.levelManager;

    /// <summary>Every frame: the wallet in the trace now and then (a test compares the games' wallets).</summary>
    public void Tick(double now)
    {
        if (!CoopTrace.On || now < _nextWallet)
            return;
        _nextWallet = now + 10.0;
        if (GameData.Currency.gold == null)
            return;
        string wallet = "gold " + GameData.Currency.gold.balance.ToString(CultureInfo.InvariantCulture)
            + ", dark quartz " + GameData.Currency.darkQuartz.balance.ToString(CultureInfo.InvariantCulture)
            + ", bones " + GameData.Currency.bone.balance.ToString(CultureInfo.InvariantCulture);
        if (wallet == _wallet)
            return;
        _wallet = wallet;
        CoopTrace.Write("wallet", CoopWorld.CurrentRoom(), what: wallet, amount: GameData.Currency.gold.totalIncome, slot: _session.LocalSlot);
    }

    // ------------------------------------------------------------------ this game drops something

    /// <summary>LevelManager.DropCurrency is about to run. False: the drop is not this game's to make.</summary>
    private bool OnCurrency(GameData.Currency.Type type, int amount, int count, Vector3 position, Vector2 force)
    {
        if (!Active || count == 0)
            return true;
        uint room = CoopWorld.CurrentRoom();
        switch (_source)
        {
            case Source.Told:
                return true;
            case Source.Quiet:
                return false;
            case Source.Enemy:
                if (!_session.IsHost)
                {
                    // a synced enemy that died here by itself: the host's word brings its loot
                    CoopTrace.Write("loot-skipped", room, what: type + " of " + _from, amount: amount);
                    return false;
                }
                var origin = MapOrigin();
                Send(LootMessage.OfDrop(room, position.x - origin.x, position.y - origin.y, new LootDrop((int)type, amount, count), force.x, force.y), -1);
                CoopTrace.Write("loot-sent", room, what: type + " of " + _from, amount: amount);
                return true;
            case Source.Prop:
                _collected.Add(new LootDrop((int)type, amount, count));
                return true;
            default:
                if (CoopTrace.On)
                    CoopTrace.Write("loot-own", room, what: type + " from " + (_source == Source.OwnEnemy ? _from + " (not synced)" : Caller()), amount: amount);
                return true;
        }
    }

    /// <summary>LevelManager.DropPotion is about to run.</summary>
    private void OnPotion(Potion potion, Vector3 position)
    {
        if (!Active || _source != Source.Enemy || potion == null)
            return;
        uint room = CoopWorld.CurrentRoom();
        if (!_session.IsHost)
        {
            // cannot be left out (the caller uses the orb it gets back); seen in a trace = one orb too many here
            CoopTrace.Write("loot-own", room, what: "a healing orb of " + _from + " (a synced enemy that died here by itself)");
            return;
        }
        var common = CommonResource.instance;
        int size = potion.name == common.largePotion?.name ? 2 : potion.name == common.mediumPotion?.name ? 1 : 0;
        var origin = MapOrigin();
        Send(LootMessage.OfPotion(room, position.x - origin.x, position.y - origin.y, size), -1);
        CoopTrace.Write("loot-sent", room, what: "healing orb " + size + " of " + _from);
    }

    private void EnemyBegins(Character enemy)
    {
        if (!Active || _source != Source.None || enemy == null)
            return;
        _source = EnemySync.Current?.IsBound(enemy) == true ? Source.Enemy : Source.OwnEnemy;
        _from = CoopTrace.NameOf(enemy);
    }

    private void EnemyEnds()
    {
        if (_source is Source.Enemy or Source.OwnEnemy)
            _source = Source.None;
    }

    /// <summary>A prop is about to be hit in this game. True: watch what it drops if this hit breaks it.</summary>
    private bool PropBegins(Prop prop)
    {
        if (!Active || _source != Source.None || prop == null || prop.destroyed || !Shared(prop))
            return false;
        _source = Source.Prop;
        _collected.Clear();
        return true;
    }

    private void PropEnds(Prop prop)
    {
        if (_source != Source.Prop)
            return;
        _source = Source.None;
        if (prop == null || !prop.destroyed)
            return;
        uint room = CoopWorld.CurrentRoom();
        var origin = MapOrigin();
        var at = prop.transform.position;
        int gold = 0;
        foreach (var drop in _collected)
            gold += drop.Amount;
        Send(LootMessage.OfBroken(room, at.x - origin.x, at.y - origin.y, _collected.ToArray()), -1);
        CoopTrace.Write("prop-broken", room, name: prop.name, what: _collected.Count + " drops", amount: gold, slot: _session.LocalSlot);
        _collected.Clear();
    }

    /// <summary>A prop whose break every game shares: a plain one, or a Dark Mirror shield "pumpkin" (Level.Objects.
    /// DivineShield: it shields an enemy until its prop breaks; Umut's finding 7, 2026-11-20 + his picture 11-21: broken
    /// on the client, still standing on the host, so the shielded enemy could not die).</summary>
    private static bool Shared(Prop prop) => Plain(prop) || ShieldProps.Contains(prop);

    /// <summary>The props of the room's DivineShields (noted when each starts).</summary>
    internal static readonly HashSet<Prop> ShieldProps = new();

    [HarmonyPatch(typeof(Level.Objects.DivineShield), "Start")]
    public static class ShieldProp
    {
        private static void Postfix(Level.Objects.DivineShield __instance)
        {
            if (ShieldProps.Count > 64)
                ShieldProps.RemoveWhere(p => p == null);
            if (__instance != null && __instance._prop != null)
                ShieldProps.Add(__instance._prop);
        }
    }

    /// <summary>Nothing listens to this prop's hits or its end (a cage, an altar, a spawner or a wave would).</summary>
    private static bool Plain(Prop prop) =>
        prop._onDestroy == null && (PropHitListeners == null || PropHitListeners.GetValue(prop) == null);

    private static Vector3 MapOrigin() => Map.Instance != null ? Map.Instance.transform.position : Vector3.zero;

    /// <summary>The game's method that asked for a drop (for the trace: which sources of gold exist).</summary>
    private static string Caller()
    {
        try
        {
            var frames = new StackTrace(1, false).GetFrames();
            if (frames == null)
                return "?";
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                if (type == null || type == typeof(LevelManager) || type == typeof(SharedLoot) || type.DeclaringType == typeof(SharedLoot)
                    || type.Namespace is { } ns && (ns.StartsWith("HarmonyLib", StringComparison.Ordinal) || ns.StartsWith("MonoMod", StringComparison.Ordinal)))
                    continue;
                return type.Name + "." + method.Name;
            }
        }
        catch
        {
            // a trace line without its source is still a trace line
        }
        return "?";
    }

    // ------------------------------------------------------------------ messages

    /// <summary>A client's message goes to the host; the host's (its own, or a client's passed on) to every
    /// client but <paramref name="except"/>.</summary>
    private void Send(in LootMessage message, int except)
    {
        _writer.Reset();
        LootMessages.Write(_writer, message);
        if (!_session.IsHost)
        {
            _session.Send(0, MessageType.Loot, _writer, Delivery.Reliable);
            return;
        }
        for (int slot = 0; slot < Protocol.MaxPlayers; slot++)
            if (slot != _session.LocalSlot && slot != except && _session.PlayerAt(slot) != null)
                _session.Send(slot, MessageType.Loot, _writer, Delivery.Reliable);
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (!LootMessages.TryRead(payload, out var m))
            return;
        if (m.Kind == LootMessageKind.Broken)
        {
            if (_session.IsHost)
            {
                if (message.FromSlot == _session.LocalSlot)
                    return;
                Send(m, message.FromSlot);
            }
            else if (message.FromSlot != 0)
                return;
            FollowProp(m, message.FromSlot);
            return;
        }
        // an enemy's loot: only the host says so
        if (_session.IsHost || message.FromSlot != 0)
            return;
        if (m.Kind == LootMessageKind.Drop)
            TakeDrop(m);
        else
            TakePotion(m);
    }

    private static bool InRoom(uint room) =>
        Levels != null && Map.Instance != null && CoopWorld.Current?.RoomLoaded == true && CoopWorld.CurrentRoom() == room;

    private void TakeDrop(in LootMessage m)
    {
        if (m.Drop.Currency > (int)GameData.Currency.Type.HeartQuartz || m.Drop.Amount <= 0)
            return;
        var type = (GameData.Currency.Type)m.Drop.Currency;
        bool here = InRoom(m.Room);
        var before = _source;
        _source = Source.Told;
        try
        {
            if (here)
                Levels.DropCurrency(type, m.Drop.Amount, m.Drop.Count, MapOrigin() + new Vector3(m.X, m.Y), new Vector2(m.ForceX, m.ForceY));
            else
                GameData.Currency.currencies[type].Earn(m.Drop.Amount); // the room is behind this player: the wallet still gets it
        }
        finally
        {
            _source = before;
        }
        CoopTrace.Write("loot-got", m.Room, what: type + (here ? "" : " (not in that room: straight into the wallet)"), amount: m.Drop.Amount);
    }

    private void TakePotion(in LootMessage m)
    {
        if (!InRoom(m.Room))
        {
            CoopTrace.Write("loot-missed", m.Room, what: "healing orb (not in that room)");
            return;
        }
        var common = CommonResource.instance;
        var potion = m.PotionSize == 2 ? common.largePotion : m.PotionSize == 1 ? common.mediumPotion : common.smallPotion;
        if (potion == null)
            return;
        var before = _source;
        _source = Source.Told;
        try
        {
            Levels.DropPotion(potion, MapOrigin() + new Vector3(m.X, m.Y));
        }
        finally
        {
            _source = before;
        }
        CoopTrace.Write("loot-got", m.Room, what: "healing orb " + m.PotionSize);
    }

    /// <summary>A plain prop broke in another game: this game's same prop breaks too and drops the same.</summary>
    private void FollowProp(in LootMessage m, int fromSlot)
    {
        if (!InRoom(m.Room))
        {
            CoopTrace.Write("prop-missed", m.Room, what: "not in that room", slot: fromSlot);
            return;
        }
        Vector2 at = (Vector2)MapOrigin() + new Vector2(m.X, m.Y);
        Prop prop = null;
        float nearest = PropReach;
        foreach (var candidate in Map.Instance.GetComponentsInChildren<Prop>())
        {
            if (candidate.destroyed || !Shared(candidate))
                continue;
            float distance = Vector2.Distance(candidate.transform.position, at);
            if (distance < nearest)
            {
                nearest = distance;
                prop = candidate;
            }
        }
        var owner = _playerOf(fromSlot) ?? Levels.player;
        if (prop == null || owner == null)
        {
            // broken here already (both players hit it: each got their own roll), or this map has no such prop
            CoopTrace.Write("prop-missed", m.Room, what: "no whole prop there", slot: fromSlot);
            return;
        }
        var before = _source;
        _source = Source.Quiet;
        try
        {
            var damage = new Damage(owner, 1e7, at, Damage.Attribute.Fixed, Damage.AttackType.Additional, Damage.MotionType.None, canCritical: false);
            for (int i = 0; i < MaxFollowHits && prop != null && !prop.destroyed; i++)
                prop.Hit(owner, ref damage, Vector2.zero);
            _source = Source.Told;
            int gold = 0;
            if (m.Drops != null)
                foreach (var drop in m.Drops)
                {
                    if (drop.Currency > (int)GameData.Currency.Type.HeartQuartz || drop.Amount <= 0)
                        continue;
                    Levels.DropCurrency((GameData.Currency.Type)drop.Currency, drop.Amount, drop.Count, at);
                    gold += drop.Amount;
                }
            CoopTrace.Write("prop-followed", m.Room, what: (m.Drops?.Length ?? 0) + " drops", amount: gold, slot: fromSlot);
        }
        finally
        {
            _source = before;
        }
    }

    // ------------------------------------------------------------------ patches

    private static void Report(string what, Exception e) =>
        Plugin.Log.LogError("[Coop] shared loot (" + what + "): " + e);

    /// <summary>An enemy's loot: gold, dark quartz, a healing orb (and gear, which stays the game's own).</summary>
    [HarmonyPatch(typeof(DropLootOnDie), "OnDie")]
    public static class EnemyLoot
    {
        private static void Prefix(DropLootOnDie __instance)
        {
            try
            {
                Current?.EnemyBegins(__instance._character);
            }
            catch (Exception e)
            {
                Report("an enemy's loot", e);
            }
        }

        private static void Finalizer()
        {
            try
            {
                Current?.EnemyEnds();
            }
            catch (Exception e)
            {
                Report("an enemy's loot", e);
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DropCurrency),
        typeof(GameData.Currency.Type), typeof(int), typeof(int), typeof(Vector3))]
    public static class CurrencyDropped
    {
        private static bool Prefix(GameData.Currency.Type type, int amount, int count, Vector3 position)
        {
            try
            {
                return Current == null || Current.OnCurrency(type, amount, count, position, Vector2.zero);
            }
            catch (Exception e)
            {
                Report("a drop", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DropCurrency),
        typeof(GameData.Currency.Type), typeof(int), typeof(int), typeof(Vector3), typeof(Vector2))]
    public static class CurrencyDroppedWithForce
    {
        private static bool Prefix(GameData.Currency.Type type, int amount, int count, Vector3 position, Vector2 force)
        {
            try
            {
                return Current == null || Current.OnCurrency(type, amount, count, position, force);
            }
            catch (Exception e)
            {
                Report("a drop", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DropPotion), typeof(Potion), typeof(Vector3))]
    public static class PotionDropped
    {
        private static void Prefix(Potion potion, Vector3 position)
        {
            try
            {
                Current?.OnPotion(potion, position);
            }
            catch (Exception e)
            {
                Report("a healing orb", e);
            }
        }
    }

    /// <summary>A prop is hit: when this hit breaks a plain one, the other games hear of it.</summary>
    [HarmonyPatch(typeof(Prop), nameof(Prop.Hit), new[] { typeof(Character), typeof(Damage), typeof(Vector2) },
        new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class PropHit
    {
        private static void Prefix(Prop __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = Current != null && Current.PropBegins(__instance);
            }
            catch (Exception e)
            {
                Report("a prop", e);
            }
        }

        private static void Finalizer(Prop __instance, bool __state)
        {
            if (!__state)
                return;
            try
            {
                Current?.PropEnds(__instance);
            }
            catch (Exception e)
            {
                Report("a prop", e);
            }
        }
    }
}
