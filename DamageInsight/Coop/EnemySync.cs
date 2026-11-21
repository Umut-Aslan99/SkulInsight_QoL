using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Sync;
using HarmonyLib;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Enemies run on the host only (COOP_PLAN §5.1; GAME_FLOW §3 and hooks 10-11). Every game builds a room's enemies
/// itself, in the same order, so each gets the same net id (wave index, then order in the wave).
/// - Host: about 20 snapshots a second (position, facing, health) and reliable events: each move an enemy starts
///   (its motion), each death, each wave that appears.
/// - Clients: their enemies are puppets: AI and behaviour trees off, physics off, waves only on the host's word,
///   drawn a little in the past (snapshot buffer); the host's moves are replayed on them, so their attacks and
///   projectiles hit this player on this game (§5.5: you're hit by what you see). They die when the host says so
///   (`undying` until then, so a hit never kills one early).
/// - A client's hits on a puppet show at once (number, flash, health bar; §5.4) and go to the host, which applies
///   them to the real enemy through the game's damage code with the client's teammate copy as the attacker (so the
///   enemy turns to them). The puppet's health follows the host's, never back up from an old snapshot.
/// Not yet: enemy summons on clients (a client's puppet doesn't summon; the host's summons only show on the host),
/// lag compensation of hits (the host trusts the hit), balance and targeting rules (step 4).
/// </summary>
internal sealed partial class EnemySync
{
    public const float SnapshotRate = 20f;
    public static EnemySync Current { get; private set; }

    private static readonly object Key = new();
    private readonly CoopSession _session;
    private readonly Func<int, Character> _avatarOf;
    private readonly Func<Character, int> _slotOf;   // a player or a teammate copy -> its slot (-1: none)
    private readonly Func<int, Character> _playerOf; // a slot -> this game's player or that teammate's copy
    private readonly NetWriter _writer = new(1024);
    private readonly NetWriter _hitWriter = new(32); // the host's own hits, for the clients' damage numbers
    private readonly List<EnemyState> _states = new();

    private Map _map;
    private uint _room;
    private readonly List<Character> _byId = new();           // net id - 1
    private readonly Dictionary<Character, ushort> _ids = new();
    private readonly List<Action> _unsubscribe = new();
    private double _nextSnapshot;
    // Client side.
    private readonly AdaptiveDelay _delay = AdaptiveDelay.ForInternet(1f / SnapshotRate);
    private readonly Dictionary<ushort, SnapshotBuffer<EnemyState>> _buffers = new();
    private readonly TimedEvents<EnemyEvent> _events = new();
    private readonly Dictionary<ushort, double> _higherSince = new();
    private readonly HashSet<Character> _puppets = new();
    private readonly Dictionary<Character, EnemyWave> _waveOf = new(); // a wave enemy's wave (activation rule)
    private readonly Dictionary<ushort, double> _shownSince = new();    // host shows it, off here, wave waits: since
    // Map characters outside the waves (traps, dummies): only their life is the host's (health, hits, death); their
    // own trap code runs on every game, their moves aren't sent.
    private readonly HashSet<Character> _lifeOnly = new();
    private readonly HostTargeting _targeting; // host: the co-op target rule for normal enemies and elites
    // Client: host moves held back until the puppet's previous move finished its last moments (its end steps clean up
    // effects; a cut-off Dimension Pierce left its lines on screen for good).
    private readonly List<(double due, EnemyEvent e)> _held = new();
    private readonly HashSet<EnemyEvent> _wasHeld = new();
    public const float FinishWindow = 0.25f;
    // Enemies summoned during the room (ids SummonFlag | n, in the host's order), and the prefabs found per summoner.
    public const ushort SummonFlag = 0x4000;
    private readonly List<Character> _summons = new();
    private readonly Dictionary<Transform, Dictionary<string, Character>> _prefabsOf = new();
    private readonly Dictionary<ushort, Character> _lifeById = new(); // ids from LifeIds (top bit set)
    // Client: the AIs of puppets (their own main loop is blocked: BossSync) and the host's boss behaviours running here.
    private readonly HashSet<Characters.AI.AIController> _puppetAis = new();
    private readonly Dictionary<Character, List<Characters.AI.AIController>> _aisOf = new();
    private readonly Dictionary<Character, Dictionary<string, Characters.AI.Behaviours.Behaviour>> _behavioursOf = new();
    private readonly Dictionary<Character, (MonoBehaviour runner, Coroutine routine)> _replays = new();
    // Client: the moves of a helper call running here (BossSync helper calls): the host's copies are skipped until
    // shortly after it ends (the host's last ones may come a little later than this game's).
    private readonly Dictionary<Character, (HashSet<Characters.Actions.Action> moves, double until)> _ownMoves = new();
    public const double OwnMovesGrace = 0.5, OwnMovesLongest = 15;
    // Host: the target slot clients last heard for each enemy (with a move, a boss call or a Target event).
    private readonly Dictionary<ushort, byte> _sentTarget = new();
    private readonly HashSet<BehaviorDesigner.Runtime.Behavior> _puppetTrees = new();
    // Events for a room this game hasn't finished loading yet (a wave often appears on the host at once): kept until
    // the room is bound, for the last few rooms only.
    private readonly Dictionary<uint, List<(double time, EnemyEvent e)>> _early = new();
    private double _lastRender = double.NegativeInfinity;
    private bool _applying, _replaying;
    // Client: rooms whose waves the host has stopped (its reward came out); a client's room finishes only after that.
    private static readonly RecentRooms HostCleared = new();
    private static uint _sentCleared;
    // Counted for the report line.
    public int SnapshotsSent, SnapshotsReceived, EventsSent, EventsPlayed, HitsSent, HitsApplied, Deaths;

    public EnemySync(CoopSession session, Func<int, Character> avatarOf, Func<Character, int> slotOf, Func<int, Character> playerOf)
    {
        _session = session;
        _avatarOf = avatarOf;
        _slotOf = slotOf;
        _playerOf = playerOf;
        _targeting = new HostTargeting(playerOf, slotOf);
        Current = this;
        DamageInsight.Codex.CodexTracker.KillCounted = OnKillCounted;
        BossSync.EnsureApplied();
        session.RegisterHandler(MessageType.EnemySnapshot, OnSnapshot);
        session.RegisterHandler(MessageType.EnemyEvent, OnEvent);
        session.RegisterHandler(MessageType.EnemyHit, OnHit);
        session.RegisterHandler(MessageType.EnemyStatus, OnStatus);
        session.RegisterHandler(MessageType.EnemyPush, OnPush);
        session.PlayerJoined += OnPlayerJoined;
    }

    private static CoopWorld World => CoopWorld.Current;
    public bool IsHost => World?.IsHost == true;
    public bool IsClient => World?.IsClient == true;
    private static double Now => Time.realtimeSinceStartupAsDouble;

    public void Dispose()
    {
        _session.PlayerJoined -= OnPlayerJoined;
        Unbind();
        HostCleared.Clear();
        _sentCleared = 0;
        if (Current == this)
        {
            Current = null;
            DamageInsight.Codex.CodexTracker.KillCounted = null;
        }
    }

    public void Tick(double now)
    {
        if (_unsyncedLater.Count > 0)
            CheckUnsyncedLater();
        var map = Map.Instance;
        bool active = World?.Active == true;
        if (!active)
        {
            if (_map != null)
                Unbind();
            return;
        }
        // Bound once the room finished loading (its waves are built then), the same moment on every game.
        if (map != _map || (map != null && _room != CoopWorld.CurrentRoom()))
        {
            if (map != null && World?.RoomLoaded != true)
            {
                if (_map != null)
                    Unbind();
                return;
            }
            Bind(map);
        }
        if (_map == null)
            return;
        Census(now);
        if (IsHost)
        {
            SendSnapshot(now);
            if (_targeting.Tick(_byId, now))
            {
                _targeting.Tick(_summons, now, again: true);
                SendTargetChanges(_byId);
                SendTargetChanges(_summons);
            }
        }
        else
        {
            Draw(now);
            PuppetAim.Update();
            StrayTrees(now);
        }
    }

    private double _nextTreeLook;

    /// <summary>
    /// Client: a behaviour tree that drives a puppet but sits where <see cref="MakePuppet"/> doesn't look, or came
    /// after it, kept running: the Dark First Hero's dark form made its own moves on the client (dm5, 2026-11-11:
    /// DimensionSlash while the host's did TripleThorn, 13 of 30 moves in step, its own projectiles). Twice a second
    /// every running tree is asked for its owner; a puppet's tree is switched off like the others.
    /// </summary>
    private void StrayTrees(double now)
    {
        if (now < _nextTreeLook || _puppets.Count == 0)
            return;
        _nextTreeLook = now + 0.5;
        foreach (var tree in UnityEngine.Object.FindObjectsOfType<BehaviorDesigner.Runtime.Behavior>())
        {
            if (tree == null || !tree.enabled)
                continue;
            // a tree switched off before that something switched on again (the component's `enabled`) counts too
            bool again = _puppetTrees.Contains(tree);
            var owner = TreeOwner(tree);
            if (!again && (owner == null || !IsRunByHost(owner)))
                continue;
            _puppetTrees.Add(tree);
            LedgerTrees.Add(tree);
            try { tree.DisableBehavior(true); }
            catch (Exception) { /* not started yet */ }
            tree.enabled = false;
            string where = _map != null && tree.transform.IsChildOf(_map.transform) ? RemoteAvatar.PathOf(tree.transform, _map.transform) : tree.name + " (outside the map)";
            CoopTrace.Write(again ? "puppet-tree-again" : "puppet-tree-late", _room, owner != null ? TraceIdOf(owner) : 0, owner != null ? CoopTrace.NameOf(owner) : tree.name, where);
            Plugin.Log.LogInfo($"[Coop] a behaviour tree of the host's enemy {(owner != null ? owner.name : tree.name)} was {(again ? "running again" : "still running")} here ({where}): switched off");
        }
    }

    /// <summary>Whose tree it is: the character it sits below; else the one its variables name (not its target: a
    /// player's minion aims at puppets too); else the one below it.</summary>
    private static Character TreeOwner(BehaviorDesigner.Runtime.Behavior tree)
    {
        var above = tree.GetComponentInParent<Character>();
        if (above != null)
            return above;
        try
        {
            var variables = tree.GetAllVariables();
            if (variables != null)
                foreach (var variable in variables)
                    if (variable is BehaviorDesigner.Runtime.SharedCharacter shared && shared.Name != "Target"
                        && shared.Value != null && IsRunByHost(shared.Value))
                        return shared.Value;
        }
        catch (Exception) { /* a tree that isn't loaded yet */ }
        return tree.GetComponentInChildren<Character>(true);
    }

    // ------------------------------------------------------------------ a room's enemies and their ids

    // ------------------------------------------------------------------ playing on alone (package DC)

    // What this game switched off on the room's puppets, kept past this object (a rejoin try makes a new EnemySync
    // before the player plays on alone): the room's map and its puppets, their AIs and behaviour trees.
    private static Map _ledgerMap;
    private static readonly HashSet<Character> LedgerPuppets = new();
    private static readonly HashSet<Characters.AI.AIController> LedgerAis = new();
    private static readonly HashSet<BehaviorDesigner.Runtime.Behavior> LedgerTrees = new();

    private static void Ledger(Map map)
    {
        if (_ledgerMap == map)
            return;
        _ledgerMap = map;
        LedgerPuppets.Clear();
        LedgerAis.Clear();
        LedgerTrees.Clear();
    }

    /// <summary>
    /// Package DC: this game lost the host for good and plays on alone. The current room's puppets become its own
    /// enemies again: life no longer the host's (`undying` off), movement, AIs and behaviour trees back on (an AI
    /// whose main loop starts when it is switched on starts it so; one that starts it elsewhere gets it started), aimed
    /// at this game's player. Then the room ends by the game itself (enemies fight and die, waves, reward, doors).
    /// Call it after <see cref="Dispose"/> (the patches let a puppet's AI and trees run only with no EnemySync).
    /// Returns how many enemies came back to life.
    /// </summary>
    // REVIEW(fable): package DC, never run in the game. [DECISION] AIs back on rather than removing the puppets: the
    // room is finished normally. Risks: an AI whose OnEnable override doesn't start its main loop stays idle; bosses
    // whose fight is driven by a host-only script (phases, Chapter3Script) may not restart cleanly.
    internal static int ReleasePuppets(Character player)
    {
        if (Current != null)
            return 0;
        var map = Map.Instance;
        int released = 0;
        try
        {
            if (map == null || map != _ledgerMap)
                return 0;
            foreach (var enemy in LedgerPuppets)
            {
                if (enemy == null || enemy.health == null)
                    continue;
                enemy.health.undying.Detach(Key);
                if (enemy.movement != null)
                    enemy.movement.enabled = true;
                if (!enemy.health.dead)
                    released++;
            }
            var checkStun = AccessTools.Method(typeof(Characters.AI.AIController), "CCheckStun");
            foreach (var ai in LedgerAis)
            {
                if (ai == null || ai.enabled)
                    continue;
                try
                {
                    ai.enabled = true; // OnEnable: most AIs start their main loop there
                    if (!ai.isActiveAndEnabled || ai.character == null || ai.character.health == null || ai.character.health.dead)
                        continue;
                    var onEnable = ai.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (onEnable == null || onEnable.DeclaringType == typeof(Characters.AI.AIController))
                        ai.RunProcess(); // its loop started in Awake or Start, long gone (StopAllCoroutines)
                    if (checkStun != null && checkStun.Invoke(ai, null) is IEnumerator stun)
                        ai.StartCoroutine(stun);
                    if (player != null && ai.target == null)
                        ai.target = player;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Coop] playing on alone: the AI of {ai.name} did not start: {e.Message}");
                }
            }
            foreach (var tree in LedgerTrees)
            {
                if (tree == null)
                    continue;
                try
                {
                    tree.enabled = true;
                    if (tree.isActiveAndEnabled)
                        tree.EnableBehavior();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Coop] playing on alone: a behaviour tree of {tree.name} did not start: {e.Message}");
                }
            }
            if (player != null)
                foreach (var option in UnityEngine.Object.FindObjectsOfType<EnemyCharacterBehaviorOption>())
                {
                    var owner = CoopTargeting.CharacterOf(option);
                    if (owner != null && LedgerPuppets.Contains(owner) && option.isActiveAndEnabled)
                    {
                        try { option.SetTargetToPlayer(); }
                        catch (Exception) { /* a tree without the variable */ }
                    }
                }
            Plugin.Log.LogInfo($"[Coop] playing on alone: {released} enemies of {map.name} are this game's own again " +
                               $"({LedgerAis.Count} AIs, {LedgerTrees.Count} behaviour trees switched back on)");
        }
        finally
        {
            Ledger(null);
        }
        return released;
    }

    /// <summary>The bound room's wave enemies in id order (see <see cref="Bind"/>): kept over a rejoin.</summary>
    private static Map _rosterMap;
    private static readonly List<(Character enemy, EnemyWave wave)> _roster = new();

    private void Bind(Map map)
    {
        Unbind();
        Ledger(map);
        _map = map;
        if (map == null || map.waveContainer == null || map.waveContainer.enemyWaves == null)
            return;
        _room = CoopWorld.CurrentRoom();
        if (ReferenceEquals(_rosterMap, map) && _roster.Count > 0)
        {
            // Bound again in the same room (a rejoin, package DC): the first binding's order, dead ones included. A
            // wave's own list drops its dead, so counting again shifted every id after the room's first death.
            foreach (var (character, wave) in _roster)
            {
                if (_byId.Count >= LifeIds.Flag - 1)
                    break;
                _byId.Add(character);
                if (character != null && !_ids.ContainsKey(character))
                {
                    _ids[character] = (ushort)_byId.Count;
                    _waveOf[character] = wave;
                }
            }
            CoopTrace.Write("room-rebound", _room, amount: _roster.Count);
        }
        else
        {
            _rosterMap = map;
            _roster.Clear();
            foreach (var wave in map.waveContainer.enemyWaves)
                foreach (var character in wave?.characters ?? Enumerable.Empty<Character>())
                    if (character != null && !_ids.ContainsKey(character) && _byId.Count < LifeIds.Flag - 1)
                    {
                        _byId.Add(character);
                        _ids[character] = (ushort)_byId.Count;
                        _waveOf[character] = wave;
                        _roster.Add((character, wave));
                    }
        }
        // Then the map's own traps and dummies, in hierarchy order (the same on every game): a vine wall that blocks
        // the way broke only on the game whose player broke it (Umut 2026-11-07; trace: "unsynced ... Trap").
        // Their ids come from their path in the map (LifeIds): a random decoration only one game has can't shift them.
        var lifeIds = new LifeIds();
        foreach (var character in map.GetComponentsInChildren<Character>(true))
            if (character != null && !_ids.ContainsKey(character) && (SharesLifeOnly(character) || RunsOutsideWaves(character)))
            {
                ushort? lifeId = lifeIds.Next(RemoteAvatar.PathOf(character.transform, map.transform));
                if (lifeId == null)
                {
                    CoopTrace.Write("life-skipped", _room, name: CoopTrace.NameOf(character), what: "id taken");
                    continue;
                }
                _ids[character] = lifeId.Value;
                _lifeById[lifeId.Value] = character;
                if (SharesLifeOnly(character))
                    _lifeOnly.Add(character);
                CoopTrace.Write("life", _room, lifeId.Value, CoopTrace.NameOf(character));
            }
        foreach (var pair in _ids)
        {
            var enemy = pair.Key;
            ushort id = pair.Value;
            if (enemy == null || (enemy.health != null && enemy.health.dead))
                continue; // bound again after a rejoin: the room's dead stay as they are
            if (!_lifeOnly.Contains(enemy))
                CoopHealthScaling.Current?.Scale(enemy); // health by party size, the same on every game
            if (IsHost)
                WatchOnHost(enemy, id, !_lifeOnly.Contains(enemy));
            else
                MakePuppet(enemy, id);
        }
        Plugin.Log.LogInfo($"[Coop] room {_room:x8}: {_byId.Count} enemies + {_lifeById.Count} traps/dummies ({(IsHost ? "host: sent from here" : "client: puppets of the host's")})");
        CoopTrace.Write("room", _room, name: Loadout.GearName(map.name), amount: _byId.Count);
        TraceUnsynced(map);
        if (IsHost && _catchUpWaiting.Count > 0)
        {
            foreach (int slot in _catchUpWaiting)
                CatchUp(slot);
            _catchUpWaiting.Clear();
        }
        if (!IsHost && _early.TryGetValue(_room, out var early))
        {
            _early.Remove(_room);
            foreach (var (time, e) in early)
            {
                if (e.Kind == EnemyEventKind.WaveSpawn)
                    Apply(e);
                else
                    _events.Add(time, e);
            }
            Plugin.Log.LogInfo($"[Coop] room {_room:x8}: {early.Count} host event(s) from before this game was in");
        }
    }

    /// <summary>Host: an enemy's moves, hits and death are sent (moves = false: traps and dummies, life only).</summary>
    private void WatchOnHost(Character enemy, ushort id, bool moves)
    {
        Character.OnStartMotionDelegate onMotion = (motion, speed) => OnHostMotion(enemy, id, motion, speed);
        Action onDied = () => SendDied(enemy, id);
        TookDamageDelegate onHurt = (in Damage original, in Damage took, double dealt) =>
        {
            if (dealt > 0)
            {
                _targeting.OnDamaged(enemy, original.attacker.character, Now);
                SendTargetChange(enemy); // the game's own switch to the attacker: the next tick sends it at the latest
                SendHostHit(id, original.attacker.character, took);
            }
        };
        if (moves)
        {
            enemy.onStartMotion += onMotion;
            enemy.health.onTookDamage += onHurt;
        }
        enemy.health.onDiedTryCatch += onDied;
        WatchBlows(enemy, id);
        _unsubscribe.Add(() =>
        {
            if (enemy == null)
                return;
            if (moves)
            {
                enemy.onStartMotion -= onMotion;
                enemy.health.onTookDamage -= onHurt;
            }
            enemy.health.onDiedTryCatch -= onDied;
        });
    }

    private readonly HashSet<ushort> _diedSent = new();

    /// <summary>Host: an enemy's death, sent once (by its death event, or before the first move a death handler
    /// started: <see cref="OnHostMotion"/>).</summary>
    private void SendDied(Character enemy, ushort id)
    {
        if (!_diedSent.Add(id))
            return;
        byte killer = KillerOf(id); // whose hit it was: a client counts the kill and runs its own on-kill effects (KillCredit)
        CoopTrace.Write("died", _room, id, CoopTrace.NameOf(enemy), slot: killer == NobodysKill ? -1 : killer);
        SendEvent(new EnemyEvent { Kind = EnemyEventKind.Died, NetId = id, TargetSlot = killer });
    }

    // ------------------------------------------------------------------ summons

    internal bool Bound => _map != null;

    /// <summary>Everything this game bound in its room (wave enemies, the map's bosses / parts, summons, traps and
    /// dummies; some may be destroyed by now). Read only: CoopTimeEffects slows them.</summary>
    internal IEnumerable<Character> BoundCharacters => _ids.Keys;

    /// <summary>Host: a summon operation made <paramref name="summoned"/>: it gets the next summon id and goes to clients.</summary>
    internal void OnHostSummon(Character summoner, Character summoned)
    {
        if (!IsHost || _map == null || summoned == null || _ids.ContainsKey(summoned) || _summons.Count >= SummonFlag - 1)
            return;
        _summons.Add(summoned);
        ushort id = (ushort)(SummonFlag | _summons.Count);
        _ids[summoned] = id;
        CoopHealthScaling.Current?.Scale(summoned);
        WatchOnHost(summoned, id, true);
        int summonerId = summoner != null && _ids.TryGetValue(summoner, out var s) ? s : 0;
        Vector2 at = (Vector2)summoned.transform.position - (Vector2)_map.transform.position;
        bool inWave = _map.waveContainer?.summonWave != null && summoned.transform.IsChildOf(_map.waveContainer.summonWave.transform);
        // "|monster": made from a monster pool (two different prefabs are both called "Tentacle" in the Pope's room:
        // the platforms' pooled one and the fanatics' sacrifice one)
        string prefab = Loadout.GearName(summoned.name) + (summoned.GetComponent<Characters.Monsters.Monster>() != null ? MonsterHint : "");
        CoopTrace.Write("summon", _room, id, CoopTrace.NameOf(summoned), $"{prefab} by #{summonerId}" + (inWave ? " (wave)" : ""));
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.Summon, NetId = id, Wave = summonerId, ActionPath = prefab, X = at.x, Y = at.y,
            Extra = inWave ? (byte)1 : (byte)0,
        });
    }

    /// <summary>Client: the host's summon, made here from the same prefab (found among its summoner's or the room's
    /// components) at the host's spot, as a puppet with the host's id.</summary>
    private void PlaySummon(EnemyEvent e)
    {
        if (_map == null || EnemyOf(e.NetId) != null)
            return;
        var summoner = e.Wave != 0 ? EnemyOf((ushort)e.Wave) : null;
        string path = e.ActionPath ?? "";
        bool pooled = path.EndsWith(MonsterHint, StringComparison.Ordinal);
        string name = pooled ? path.Substring(0, path.Length - MonsterHint.Length) : path;
        var prefab = PrefabNamed(summoner != null ? RootOf(summoner) : null, name, pooled) ?? PrefabNamed(_map.transform, name, pooled);
        if (prefab == null && !_spawnPrefabs.TryGetValue(name, out prefab))
        {
            // A prefab that only a projectile's spawn operation knows (ProjectileSpawns): this game's own copy of that
            // projectile lands about now and names it; wait a moment for it.
            if (!_summonWaits.TryGetValue(e, out double since))
                _summonWaits[e] = since = Now;
            if (Now - since < SummonWait)
            {
                _held.Add((Now + 0.05, e));
                return;
            }
            _summonWaits.Remove(e);
            CoopTrace.Write("summon-not-found", _room, e.NetId, what: e.ActionPath + " by #" + e.Wave);
            return;
        }
        _summonWaits.Remove(e);
        var spot = (Vector2)_map.transform.position + new Vector2(e.X, e.Y);
        var monster = prefab.GetComponent<Characters.Monsters.Monster>();
        Character summoned;
        if (monster != null && monster._poolObject != null)
            summoned = monster.Summon(spot).character; // the game's own pool, as the host's was made
        else
            summoned = UnityEngine.Object.Instantiate(prefab, spot, Quaternion.identity, _map.transform);
        summoned.gameObject.SetActive(true);
        if (e.Extra == 1)
            _map.waveContainer?.Attach(summoned);
        int index = e.NetId & (SummonFlag - 1);
        while (_summons.Count < index)
            _summons.Add(null);
        _summons[index - 1] = summoned;
        _ids[summoned] = e.NetId;
        CoopHealthScaling.Current?.Scale(summoned);
        MakePuppet(summoned, e.NetId);
        // a dark elite's Recharge "shrine" beams at the elite it shields (DivineShieldEffect.Activate, done by the
        // ability on the host)
        var beam = summoner != null ? summoned.GetComponentInChildren<Level.Objects.DivineShieldEffect>(true) : null;
        if (beam != null)
            beam.Activate(summoner);
        CoopTrace.Write("summon-applied", _room, e.NetId, CoopTrace.NameOf(summoned), e.ActionPath + (beam != null ? " (beam)" : ""));
    }

    /// <summary>How long a host's summon waits for its prefab (see <see cref="PlaySummon"/>).</summary>
    public const double SummonWait = 1.5;
    private readonly Dictionary<EnemyEvent, double> _summonWaits = new();
    /// <summary>Client: character prefabs named by this game's own (blocked) projectile spawns, by name.</summary>
    private readonly Dictionary<string, Character> _spawnPrefabs = new();
    /// <summary>The suffix of a summon's prefab name for one made from a monster pool (OnHostSummon).</summary>
    internal const string MonsterHint = "|monster";
    private readonly Dictionary<Transform, Dictionary<string, Character>> _pooledPrefabsOf = new();
    private readonly Dictionary<(Transform, string), double> _prefabsRebuilt = new();

    /// <summary>
    /// Client: a summon operation that must not run here because the host makes its characters: in a bound room every
    /// summon whose owner isn't on the players' side (an enemy, a puppet, a trap like the flask bottles, or none). A
    /// player's own (this game's, a minion's, its quintessence's; a teammate copy's for the show) still runs, and so
    /// does an operation with no known owner that makes only player minions.
    /// </summary>
    internal bool LeavesSummonToHost(Character owner, Component operation)
    {
        if (_map == null || operation == null)
            return false;
        if (owner != null && CoopCopies.IsPlayerSide(owner))
            return false;
        if (owner == null && MakesOnlyMinions(operation))
            return false;
        CoopTrace.Write("summon-left", _room, TraceIdOf(owner), CoopTrace.NameOf(owner), operation.GetType().Name);
        return true;
    }

    /// <summary>All the characters an operation could make are player minions (and there is at least one).</summary>
    private static bool MakesOnlyMinions(Component operation)
    {
        bool any = false;
        foreach (var field in PrefabFields(operation.GetType()))
        {
            object value = field.GetValue(operation);
            if (value is Character one && one != null)
            {
                if (one.type != Character.Type.PlayerMinion)
                    return false;
                any = true;
            }
            else if (value is Character[] many)
                foreach (var c in many)
                    if (c != null)
                    {
                        if (c.type != Character.Type.PlayerMinion)
                            return false;
                        any = true;
                    }
        }
        return any;
    }

    /// <summary>Client: the characters a spawn operation could make (its Character and Character[] fields).</summary>
    private void NoteSpawnPrefabs(Component operation)
    {
        foreach (var field in PrefabFields(operation.GetType()))
            foreach (var c in CharactersIn(field.GetValue(operation)))
                _spawnPrefabs[Loadout.GearName(c.name)] = c;
    }

    /// <summary>Client: the characters among the objects a projectile's SpawnObject could make (false: none).</summary>
    private bool NoteSpawnObjects(Characters.Projectiles.Operations.SpawnObject spawn)
    {
        bool any = false;
        var hard = Singletons.Singleton<Hardmode.HardmodeManager>.Instance;
        var list = hard != null && hard.hardmode ? spawn._objectsInHardmode : spawn._objects;
        if (list != null && list.Length > 0)
            foreach (var o in list)
                Add(o);
        else
            Add(spawn._object);
        return any;

        void Add(GameObject o)
        {
            var c = o != null ? o.GetComponent<Character>() : null;
            if (c == null)
                return;
            _spawnPrefabs[Loadout.GearName(c.name)] = c;
            any = true;
        }
    }

    /// <summary>
    /// Enemies made by a PROJECTILE when it lands (the Dark Mirror Chimera's subject drop: Escaped / Strange Subjects,
    /// Flask's Spirits; `Characters.Projectiles.Operations`, not the character operations of <see cref="Summons"/>).
    /// Found 2026-11-11: each game made its own random ones, and the host's (sent as summons) were never made on the
    /// client, because their prefab is only named inside the projectile. A client makes none of its own for an enemy
    /// the host runs and remembers the prefabs for the host's summon; the host's are sent by
    /// <see cref="AttachedOnHost"/> (in a wave) or here (outside one).
    /// </summary>
    [HarmonyPatch]
    public static class ProjectileSpawns
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var type in new[]
                     {
                         typeof(Characters.Projectiles.Operations.SpawnBDRandomCharacter),
                         typeof(Characters.Projectiles.Operations.SpawnRandomEnemy),
                     })
            {
                var run = AccessTools.Method(type, "Run", new[] { typeof(Characters.Projectiles.IProjectile) });
                if (run != null && run.DeclaringType == type)
                    yield return run;
            }
            // the one the DM Chimera's drop really uses: any object, a random one of a list, a character among them
            var spawn = AccessTools.Method(typeof(Characters.Projectiles.Operations.SpawnObject), "Run",
                new[] { typeof(Characters.Projectiles.IProjectile), typeof(RaycastHit2D) });
            if (spawn != null)
                yield return spawn;
        }

        private static bool Prefix(Component __instance, Characters.Projectiles.IProjectile projectile, out HashSet<Character> __state)
        {
            __state = null;
            var sync = Current;
            if (sync == null || !sync.Bound)
                return true;
            var owner = projectile?.owner;
            if (sync.IsClient)
            {
                // a player's projectile (this one's or a copy's) stays as it is; an enemy's (its owner is often not
                // the puppet itself) spawns nothing here
                if (CoopCopies.IsPlayerSide(owner))
                    return true;
                if (__instance is Characters.Projectiles.Operations.SpawnObject spawn)
                    return !sync.NoteSpawnObjects(spawn); // an effect or a prop (no character among them) runs here
                sync.NoteSpawnPrefabs(__instance);
                return false;
            }
            if (sync.IsHost)
                __state = new HashSet<Character>(UnityEngine.Object.FindObjectsOfType<Character>());
            return true;
        }

        private static void Postfix(Characters.Projectiles.IProjectile projectile, HashSet<Character> __state)
        {
            var sync = Current;
            if (__state == null || sync == null)
                return;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Character>())
                if (c != null && !__state.Contains(c) && c.type != Character.Type.Player && c.type != Character.Type.PlayerMinion)
                    sync.OnHostSummon(projectile?.owner, c);
        }
    }

    /// <summary>A character prefab called <paramref name="name"/> in a Character or Character[] field of a component
    /// below <paramref name="root"/> (summon operations, boss scripts); found once per root.</summary>
    private Character PrefabNamed(Transform root, string name, bool pooled = false)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;
        if (pooled && _pooledPrefabsOf.TryGetValue(root, out var pooledByName) && pooledByName.TryGetValue(name, out var pooledOne)
            && pooledOne != null)
            return pooledOne;
        // the list of a root is made at its first lookup: things that appeared later (the fanatics, with their
        // sacrifice tentacle) are missing. A lookup that finds nothing or only the other kind makes it again (at
        // most every 2 s per root and name).
        if (_prefabsOf.TryGetValue(root, out var known) && (!known.TryGetValue(name, out var knownOne) || knownOne == null
                || knownOne.GetComponent<Characters.Monsters.Monster>() != null != pooled))
        {
            var key = (root, name);
            if (!_prefabsRebuilt.TryGetValue(key, out double at) || Now - at > 2.0)
            {
                _prefabsRebuilt[key] = Now;
                _prefabsOf.Remove(root);
                _pooledPrefabsOf.Remove(root);
            }
        }
        if (!_prefabsOf.TryGetValue(root, out var byName))
        {
            _prefabsOf[root] = byName = new Dictionary<string, Character>();
            _pooledPrefabsOf[root] = pooledByName = new Dictionary<string, Character>();
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is Characters.AI.Pope.FanaticFactory factory && factory._fanatics != null)
                    foreach (var fanatic in factory._fanatics.Values)
                        Add(fanatic);
                if (component != null)
                    foreach (var field in PrefabFields(component.GetType()))
                        foreach (var c in CharactersIn(field.GetValue(component)))
                            Add(c);
            }

            void Add(Character c)
            {
                if (c == null)
                    return;
                string key = Loadout.GearName(c.name);
                bool isPooled = c.GetComponent<Characters.Monsters.Monster>() != null;
                if (isPooled && !pooledByName.ContainsKey(key))
                    pooledByName[key] = c;
                // a name without the pool hint means a prefab made without a pool: a plain one wins over a pooled one of
                // the same name (n4, 2026-11-21: the fanatics' sacrifice tentacles were made from the platforms'
                // pooled "Tentacle" on the client: other moves, motion-not-found, alive there for minutes)
                if (!byName.TryGetValue(key, out var had) || had == null
                    || !isPooled && had.GetComponent<Characters.Monsters.Monster>() != null)
                    byName[key] = c;
            }
            if (pooled && pooledByName.TryGetValue(name, out pooledOne) && pooledOne != null)
                return pooledOne;
        }
        return byName.TryGetValue(name, out var found) ? found : null;
    }

    private static readonly Dictionary<Type, System.Reflection.FieldInfo[]> PrefabFieldCache = new();

    private static System.Reflection.FieldInfo[] PrefabFields(Type type)
    {
        if (PrefabFieldCache.TryGetValue(type, out var fields))
            return fields;
        var list = new List<System.Reflection.FieldInfo>();
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                          | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                if (f.FieldType == typeof(Character) || f.FieldType == typeof(Character[])
                    || f.FieldType == typeof(Characters.Monsters.Monster) || f.FieldType == typeof(Characters.Monsters.Monster[])
                    || f.FieldType == typeof(Level.Traps.TentacleAI))
                    list.Add(f);
        return PrefabFieldCache[type] = list.ToArray();
    }

    /// <summary>The character prefabs a prefab field holds: a character, a monster (pooled enemy: the Pope room's
    /// platform tentacles, SummonMonster's), a fanatic's sacrifice tentacle, or a list of those.</summary>
    private static IEnumerable<Character> CharactersIn(object value)
    {
        switch (value)
        {
            case Character one when one != null:
                yield return one;
                break;
            case Character[] many:
                foreach (var c in many)
                    if (c != null)
                        yield return c;
                break;
            case Characters.Monsters.Monster monster when monster != null && monster.character != null:
                yield return monster.character;
                break;
            case Characters.Monsters.Monster[] monsters:
                foreach (var m in monsters)
                    if (m != null && m.character != null)
                        yield return m.character;
                break;
            case Level.Traps.TentacleAI tentacle when tentacle != null && tentacle.character != null:
                yield return tentacle.character;
                break;
        }
    }

    /// <summary>Trace: characters of the room that aren't synced (not in a wave: bosses, their parts, props), and
    /// every move they make on this game (on a client that is a move the host didn't ask for).</summary>
    private void TraceUnsynced(Map map)
    {
        if (!CoopTrace.On)
            return;
        foreach (var c in map.GetComponentsInChildren<Character>(true))
        {
            if (c == null || _ids.ContainsKey(c) || c.type == Character.Type.Player || c.type == Character.Type.PlayerMinion)
                continue;
            var character = c;
            uint room = _room;
            CoopTrace.Write("unsynced", room, name: CoopTrace.NameOf(character), what: character.type.ToString());
            Character.OnStartMotionDelegate onMotion = (motion, speed) =>
                CoopTrace.Write("unsynced-motion", room, name: CoopTrace.NameOf(character), what: MotionName(character, motion));
            character.onStartMotion += onMotion;
            _unsubscribe.Add(() =>
            {
                if (character != null)
                    character.onStartMotion -= onMotion;
            });
        }
    }

    /// <summary>A map character outside the waves whose life is shared: traps (vine walls, powder kegs, ...) and
    /// dummies, if they can be hurt at all.</summary>
    private static bool SharesLifeOnly(Character c) =>
        (c.type == Character.Type.Trap || c.type == Character.Type.Dummy) && c.health != null;

    /// <summary>An enemy placed in the map outside the waves that the host runs like a wave enemy: adventurers and
    /// mini-bosses (Dark Mirror's SupportingMagician ran on each game by itself). Not trash mobs outside waves (castle
    /// cats, background soldiers that differ per game) and not bosses (their scripts are handled by BossSync).</summary>
    private static bool RunsOutsideWaves(Character c) =>
        (c.type == Character.Type.Adventurer || c.type == Character.Type.Named || IsBossPart(c)) && c.health != null;

    /// <summary>King Alexander's giant (dm6) is three boss characters placed outside the waves: `EmperorBody`,
    /// `EmperorHand(Left)`, `EmperorHand(Right)` (and `EmperorBody_Phase3`). Without an id each game ran its own:
    /// two full health bars on the client while the host's were hurt, other hand attacks (Umut's pictures,
    /// 2026-11-11). With one they are the host's like an adventurer: health, statuses, place, and the hands'
    /// attacks, which are character actions (`EmperorAnimationEventRecevier.RunHandBehavior`).</summary>
    internal static bool IsBossPart(Character c) =>
        c.type == Character.Type.Boss && c.name.StartsWith("Emperor", StringComparison.Ordinal);

    private static string MotionName(Character c, Characters.Actions.Motion motion)
    {
        if (motion?.action == null)
            return motion != null ? "(no action) " + motion.name : "?";
        return RemoteAvatar.PathOf(motion.action.transform, c.transform) + "#" + Array.IndexOf(motion.action.motions, motion);
    }

    private void Unbind()
    {
        foreach (var undo in _unsubscribe)
        {
            try { undo(); }
            catch (Exception) { /* the enemy is gone with its room */ }
        }
        _unsubscribe.Clear();
        _byId.Clear();
        _held.Clear();
        _wasHeld.Clear();
        _summons.Clear();
        _prefabsOf.Clear();
        _pooledPrefabsOf.Clear();
        _prefabsRebuilt.Clear();
        _targeting.Clear();
        _lifeById.Clear();
        _waveOf.Clear();
        _shownSince.Clear();
        _ids.Clear();
        _buffers.Clear();
        _events.Clear();
        _higherSince.Clear();
        foreach (var pair in _replays)
            if (pair.Value.runner != null && pair.Value.routine != null)
            {
                pair.Value.runner.StopCoroutine(pair.Value.routine);
                ReleaseHolds(pair.Key, pair.Value.runner);
            }
        _replays.Clear();
        _lockOwnSince.Clear();
        _lockTriedAt.Clear();
        _ownStatusAt.Clear();
        _shownTriedAt.Clear();
        _diedSent.Clear();
        _blows.Clear();
        _ownHits.Clear();
        _summonWaits.Clear();
        _spawnPrefabs.Clear();
        _ownMoves.Clear();
        _sentTarget.Clear();
        BossSync.ForgetCalls();
        _puppetAis.Clear();
        _aisOf.Clear();
        _behavioursOf.Clear();
        _puppets.Clear();
        _lifeOnly.Clear();
        _puppetTrees.Clear();
        PuppetAim.Clear();
        _map = null;
        _room = 0;
    }

    /// <summary>For the trace: why an id has no enemy (never bound, the room unbound, or its object destroyed).</summary>
    private string WhyMissing(ushort id)
    {
        if (LifeIds.IsLifeId(id) || (id & SummonFlag) != 0)
            return "no such enemy";
        if (_byId.Count == 0)
            return "no such enemy (the room is not bound)";
        if (id < 1 || id > _byId.Count)
            return "no such enemy (id out of range)";
        return ReferenceEquals(_byId[id - 1], null) ? "no such enemy" : "no such enemy (its object was destroyed)";
    }

    private Character EnemyOf(ushort id)
    {
        if (LifeIds.IsLifeId(id))
            return _lifeById.TryGetValue(id, out var c) ? c : null;
        if ((id & SummonFlag) != 0)
        {
            int n = id & (SummonFlag - 1);
            return n >= 1 && n <= _summons.Count ? _summons[n - 1] : null;
        }
        return id >= 1 && id <= _byId.Count ? _byId[id - 1] : null;
    }

    // ------------------------------------------------------------------ host

    /// <summary>
    /// Host: a player who joins during a room (in practice one who comes back after a drop, package DC: a new player
    /// joins only in the castle) gets what the events told the others while they were away: the waves that appeared,
    /// the enemies that died (nobody's kill), and whether the room is cleared. Only to that player. Before, enemies
    /// the host killed during the outage stood frozen on the client and its room never finished (DC.md risk 2).
    /// </summary>
    // REVIEW(fable): host -> rejoiner catch-up, REVIEW_QUEUE 13.
    private void OnPlayerJoined(RosterEntry player)
    {
        if (!IsHost || player == null || player.Slot == _session.LocalSlot || _session.State != SessionState.Active)
            return;
        if (_map == null)
        {
            // the host's parts were made anew for the rejoin and this room isn't bound yet (drop run b, COOP_LOG
            // 60q: no catch-up was sent): it goes out right after the binding
            if (!_catchUpWaiting.Contains(player.Slot))
                _catchUpWaiting.Add(player.Slot);
            return;
        }
        CatchUp(player.Slot);
    }

    private readonly List<int> _catchUpWaiting = new();

    private void CatchUp(int slot)
    {
        if (_map == null || _session.State != SessionState.Active || _session.PlayerAt(slot) == null)
            return;
        var player = _session.PlayerAt(slot);
        int waves = 0, dead = 0;
        var list = _map.waveContainer != null ? _map.waveContainer.enemyWaves : null;
        if (list != null)
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null && list[i].state is Wave.State.Spawned or Wave.State.Cleared)
                {
                    SendEventTo(player.Slot, new EnemyEvent { Kind = EnemyEventKind.WaveSpawn, Wave = i });
                    waves++;
                }
        for (int i = 0; i < _byId.Count; i++)
        {
            var enemy = _byId[i];
            if (enemy == null || (enemy.health != null && enemy.health.dead))
            {
                SendEventTo(player.Slot, new EnemyEvent { Kind = EnemyEventKind.Died, NetId = (ushort)(i + 1), TargetSlot = NobodysKill });
                dead++;
            }
        }
        bool cleared = _room != 0 && _sentCleared == _room;
        if (cleared)
            SendEventTo(player.Slot, new EnemyEvent { Kind = EnemyEventKind.RoomCleared });
        CoopTrace.Write("catch-up-sent", _room, slot: player.Slot, what: $"{waves} waves, {dead} dead" + (cleared ? ", cleared" : ""));
    }

    private void SendEventTo(int slot, EnemyEvent e)
    {
        _writer.Reset();
        EnemyMessages.WriteEvent(_writer, _room, Now, e);
        _session.Send(slot, MessageType.EnemyEvent, _writer, Delivery.Reliable);
        EventsSent++;
    }

    private void SendEvent(EnemyEvent e, uint room = 0)
    {
        if (_session.State != SessionState.Active)
            return;
        _writer.Reset();
        EnemyMessages.WriteEvent(_writer, room != 0 ? room : _room, Now, e);
        _session.Broadcast(MessageType.EnemyEvent, _writer, Delivery.Reliable);
        EventsSent++;
    }

    private void OnHostMotion(Character enemy, ushort id, Characters.Actions.Motion motion, float speed)
    {
        if (motion?.action == null || enemy == null)
            return;
        int index = Array.IndexOf(motion.action.motions, motion);
        if (index < 0)
            return;
        // A move started by a death handler that runs before this class's own: the death goes out first. A client
        // then kills its puppet, whose own handlers start the same move (and skips this one as "dead"); the other way
        // round the move ran twice there and the Pope's death scene ended 2.8 s early (the probe, 2026-11-11).
        if (enemy.health != null && enemy.health.dead)
            SendDied(enemy, id);
        var target = CoopTargeting.TargetOf(enemy);
        int slot = target != null ? _slotOf(target) : -1;
        CoopTrace.Write("motion", _room, id, CoopTrace.NameOf(enemy), MotionName(enemy, motion), slot: slot);
        NoteTarget(id, slot);
        Vector2 at = (Vector2)enemy.transform.position - (Vector2)_map.transform.position;
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.Motion, NetId = id, MotionIndex = index, Speed = speed, X = at.x, Y = at.y,
            ActionPath = RemoteAvatar.PathOf(motion.action.transform, enemy.transform),
            TargetSlot = slot is >= 0 and < 255 ? (byte)slot : (byte)255,
        });
    }

    private void NoteTarget(ushort id, int slot)
    {
        if (slot is >= 0 and < 255)
            _sentTarget[id] = (byte)slot;
    }

    /// <summary>Host: enemies whose target changed since clients last heard it (the co-op rule, the game's own switch
    /// on a hit): sent now, so a move that is still running (a spell cast "at the player" later) goes to the same
    /// player on every game.</summary>
    private void SendTargetChanges(IReadOnlyList<Character> enemies)
    {
        for (int i = 0; i < enemies.Count; i++)
            SendTargetChange(enemies[i]);
    }

    private void SendTargetChange(Character enemy)
    {
        if (enemy == null || enemy.health == null || enemy.health.dead || !enemy.gameObject.activeInHierarchy
            || _lifeOnly.Contains(enemy) || !_ids.TryGetValue(enemy, out var id))
            return;
        var target = CoopTargeting.TargetOf(enemy);
        int slot = target != null ? _slotOf(target) : -1;
        if (slot is < 0 or >= 255 || (_sentTarget.TryGetValue(id, out byte last) && last == slot))
            return;
        _sentTarget[id] = (byte)slot;
        CoopTrace.Write("target", _room, id, CoopTrace.NameOf(enemy), slot: slot);
        SendEvent(new EnemyEvent { Kind = EnemyEventKind.Target, NetId = id, TargetSlot = (byte)slot });
    }

    /// <summary>Where an enemy's behaviours are looked up (the Enemy container holds the AI, else the character).</summary>
    private static Transform RootOf(Character enemy)
    {
        var container = enemy.GetComponentInParent<Enemy>();
        return container != null ? container.transform : enemy.transform;
    }

    /// <summary>Host: a boss behaviour (BossSync.Replayed) starts: clients run the same one.</summary>
    internal void OnHostBehaviour(Characters.AI.Behaviours.Behaviour behaviour, Characters.AI.AIController controller)
    {
        if (!IsHost || _map == null || behaviour == null)
            return;
        var enemy = controller != null ? controller.character : null;
        if (enemy == null || !_ids.TryGetValue(enemy, out var id))
        {
            CoopTrace.Write("behaviour-unbound", _room, name: enemy != null ? CoopTrace.NameOf(enemy) : null, what: behaviour.GetType().Name);
            return;
        }
        string path = BossSync.PathOf(behaviour, RootOf(enemy));
        byte extra = BossSync.PickOnHost(behaviour);
        var target = CoopTargeting.TargetOf(enemy);
        int slot = target != null ? _slotOf(target) : -1;
        CoopTrace.Write("behaviour", _room, id, CoopTrace.NameOf(enemy), $"{behaviour.GetType().Name} {path}" + (extra != 0 ? " pick " + extra : ""), slot: slot);
        NoteTarget(id, slot);
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.Behaviour, NetId = id, ActionPath = path, Extra = extra,
            TargetSlot = slot is >= 0 and < 255 ? (byte)slot : (byte)255,
        });
    }

    /// <summary>Host: a listed boss method runs (BossSync.Calls): clients run the same call.</summary>
    internal void OnHostCall(Characters.AI.AIController ai, string key, byte arg, float value)
    {
        if (!IsHost || _map == null || ai == null)
            return;
        var enemy = ai.character;
        if (enemy == null || !_ids.TryGetValue(enemy, out var id))
        {
            CoopTrace.Write("call-unbound", _room, name: enemy != null ? CoopTrace.NameOf(enemy) : null, what: key);
            return;
        }
        ushort seq = BossSync.Calls.TryGetValue(key, out var info) && info.Routine != null ? BossSync.NextSeq(enemy) : (ushort)0;
        var target = CoopTargeting.TargetOf(enemy);
        int slot = target != null ? _slotOf(target) : -1;
        CoopTrace.Write("call", _room, id, CoopTrace.NameOf(enemy), $"{key} {arg} {value:0.##} #{seq}", slot: slot);
        NoteTarget(id, slot);
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.Call, NetId = id, ActionPath = key, Extra = arg, Speed = value, Seq = seq,
            TargetSlot = slot is >= 0 and < 255 ? (byte)slot : (byte)255,
        });
    }

    /// <summary>Host: a helper behaviour starts (BossSync.HelperBehaviours: the Pope's attacks): clients run the same
    /// behaviour with the call's seed.</summary>
    internal void OnHostHelperBehaviour(Characters.AI.Behaviours.Behaviour behaviour, Characters.AI.AIController controller)
    {
        if (!IsHost || _map == null || behaviour == null)
            return;
        var enemy = controller != null ? controller.character : null;
        if (enemy == null || !_ids.TryGetValue(enemy, out var id))
        {
            CoopTrace.Write("call-unbound", _room, name: enemy != null ? CoopTrace.NameOf(enemy) : null, what: behaviour.GetType().Name);
            return;
        }
        string key = BossSync.BehaviourKey + BossSync.PathOf(behaviour, RootOf(enemy));
        ushort seq = BossSync.NextSeq(enemy);
        var target = CoopTargeting.TargetOf(enemy);
        int slot = target != null ? _slotOf(target) : -1;
        CoopTrace.Write("call", _room, id, CoopTrace.NameOf(enemy), $"{behaviour.GetType().Name} {key} #{seq}", slot: slot);
        NoteTarget(id, slot);
        SendEvent(new EnemyEvent
        {
            Kind = EnemyEventKind.Call, NetId = id, ActionPath = key, Seq = seq,
            TargetSlot = slot is >= 0 and < 255 ? (byte)slot : (byte)255,
        });
    }

    /// <summary>Client: the host's helper behaviour, run here on the puppet (its moves are its own while it runs).</summary>
    private void PlayHelperBehaviour(EnemyEvent e)
    {
        var enemy = EnemyOf(e.NetId);
        if (enemy != null && !enemy.health.dead && !enemy.gameObject.activeInHierarchy)
            ActivateLikeHost(e.NetId, enemy, true);
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health.dead)
        {
            CoopTrace.Write("call-skipped", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + (enemy == null ? " no such enemy" : ""));
            return;
        }
        var behaviour = BehaviourOf(enemy, e.ActionPath.Substring(BossSync.BehaviourKey.Length));
        var ai = behaviour != null ? AiFor(enemy, behaviour) : null;
        if (behaviour == null || ai == null || !behaviour.gameObject.activeInHierarchy)
        {
            CoopTrace.Write("call-not-found", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + (behaviour == null ? "" : ai == null ? " (no AI)" : " (inactive)"));
            return;
        }
        if (e.TargetSlot != 255)
            CoopTargeting.SetTarget(enemy, _playerOf(e.TargetSlot));
        BossSync.SetSeq(enemy, e.Seq);
        RunHelper(enemy, behaviour, behaviour.CRun(ai), behaviour);
        CoopTrace.Write("call-played", _room, e.NetId, CoopTrace.NameOf(enemy), $"{behaviour.GetType().Name} {e.ActionPath} #{e.Seq}",
            slot: e.TargetSlot == 255 ? -1 : e.TargetSlot);
    }

    /// <summary>Client: runs a helper (one at a time per enemy, like the host): it starts its moves itself here, so the
    /// host's copies of them are skipped while it runs (and shortly after).</summary>
    private void RunHelper(Character enemy, MonoBehaviour runner, IEnumerator routine, Component helper)
    {
        StopReplay(enemy);
        var moves = helper != null ? BossSync.ActionsOf(helper) : new HashSet<Characters.Actions.Action>();
        _ownMoves[enemy] = (moves, Now + OwnMovesLongest); // a helper that stops with an error never ends
        routine = new BossSync.Steps(routine, new BossSync.Steps.Shared { Replaying = true }, () =>
        {
            if (_ownMoves.TryGetValue(enemy, out var own) && own.moves == moves)
                _ownMoves[enemy] = (moves, Now + OwnMovesGrace);
        });
        _replays[enemy] = (runner, runner.StartCoroutine(routine));
    }

    private void PlayCall(EnemyEvent e)
    {
        if (e.ActionPath.StartsWith(BossSync.BehaviourKey, StringComparison.Ordinal))
        {
            PlayHelperBehaviour(e);
            return;
        }
        if (BossParts.Handles(e.ActionPath))
        {
            PlayPartCall(e);
            return;
        }
        var enemy = EnemyOf(e.NetId);
        // Like a move: a call proves the host shows it (the Dark sister's first attack may be a helper call).
        if (enemy != null && !enemy.health.dead && !enemy.gameObject.activeInHierarchy && BossSync.Calls.ContainsKey(e.ActionPath))
            ActivateLikeHost(e.NetId, enemy, true);
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health.dead || !BossSync.Calls.TryGetValue(e.ActionPath, out var info))
        {
            CoopTrace.Write("call-skipped", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + (enemy == null ? " no such enemy" : ""));
            return;
        }
        Characters.AI.AIController ai = null;
        if (_aisOf.TryGetValue(enemy, out var ais))
            ai = ais.FirstOrDefault(a => a != null && info.Ai.IsInstanceOfType(a));
        if (ai == null)
        {
            CoopTrace.Write("call-not-found", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + " (no " + info.Ai.Name + ")");
            return;
        }
        if (e.TargetSlot != 255)
            CoopTargeting.SetTarget(enemy, _playerOf(e.TargetSlot));
        if (info.Instant != null)
            info.Instant(ai, e.Extra, e.Speed);
        else
        {
            BossSync.SetSeq(enemy, e.Seq);
            var routine = info.Routine(ai, e.Extra, e.Speed);
            if (info.Helper != null)
                RunHelper(enemy, ai, routine, info.Helper(ai));
            else
            {
                StopReplay(enemy);
                _ownMoves.Remove(enemy);
                _replays[enemy] = (ai, ai.StartCoroutine(routine));
            }
        }
        CoopTrace.Write("call-played", _room, e.NetId, CoopTrace.NameOf(enemy), $"{e.ActionPath} {e.Extra} {e.Speed:0.##} #{e.Seq}",
            slot: e.TargetSlot == 255 ? -1 : e.TargetSlot);
    }

    /// <summary>Host: a boss changed phase.</summary>
    internal void OnHostPhase(Character enemy, byte phase)
    {
        if (!IsHost || enemy == null || !_ids.TryGetValue(enemy, out var id))
            return;
        CoopTrace.Write("phase", _room, id, CoopTrace.NameOf(enemy), amount: phase);
        SendEvent(new EnemyEvent { Kind = EnemyEventKind.Phase, NetId = id, Extra = phase });
    }

    /// <summary>Client: a puppet's behaviour by the host's path.</summary>
    private Characters.AI.Behaviours.Behaviour BehaviourOf(Character enemy, string path)
    {
        if (!_behavioursOf.TryGetValue(enemy, out var byPath))
        {
            byPath = new Dictionary<string, Characters.AI.Behaviours.Behaviour>();
            var root = RootOf(enemy);
            foreach (var b in root.GetComponentsInChildren<Characters.AI.Behaviours.Behaviour>(true))
                if (b != null)
                    byPath[BossSync.PathOf(b, root)] = b;
            _behavioursOf[enemy] = byPath;
        }
        return byPath.TryGetValue(path, out var found) ? found : null;
    }

    /// <summary>Client: the puppet's AI that runs <paramref name="behaviour"/> (the one above it, else its first).</summary>
    private Characters.AI.AIController AiFor(Character enemy, Component behaviour)
    {
        if (!_aisOf.TryGetValue(enemy, out var ais) || ais.Count == 0)
            return null;
        foreach (var ai in ais)
            if (ai != null && behaviour.transform.IsChildOf(ai.transform))
                return ai;
        return ais[0];
    }

    /// <summary>How long a drawn snapshot waits for a wave event that may still be on its way (a resend) before it
    /// switches the enemy on by itself. Was 1 s: Leiana's Dark sister stood on the host a second before she appeared
    /// on a client (the probe, 2026-11-11); a snapshot is drawn the buffer's delay after it came, an event at once.</summary>
    public const double ShownWait = 0.5;

    /// <summary>
    /// Client: an enemy the host moves or shows (a snapshot) but that is switched off here: a boss script switched it
    /// on at the host only (Leiana's sisters at the fight's start, her phase 2 "Dark" sister: its moves were skipped as
    /// "not active"). Switched on here too, with its switched-off parents below the map. A host move proves it (moves
    /// come after any wave event); a snapshot only after <see cref="ShownWait"/> while its wave still waits here (the
    /// unreliable snapshot may come before the wave's own spawn; the Dark sister's wave waits on the host too: the
    /// script skips it). True when it is active now.
    /// </summary>
    private bool ActivateLikeHost(ushort id, Character enemy, bool fromMove, double now = 0)
    {
        if (_map == null)
            return false;
        if (!fromMove && _waveOf.TryGetValue(enemy, out var wave) && wave != null && wave.state == Wave.State.Waiting)
        {
            if (!_shownSince.TryGetValue(id, out double since))
                _shownSince[id] = since = now;
            if (now - since < ShownWait)
                return false;
        }
        var stop = _map.transform;
        for (var t = enemy.transform; t != null && t != stop; t = t.parent)
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
        bool active = enemy.gameObject.activeInHierarchy;
        CoopTrace.Write(active ? "activated" : "activate-failed", _room, id, CoopTrace.NameOf(enemy));
        return active;
    }

    /// <summary>
    /// Client: stops the puppet's running replay (one at a time per enemy, like the host's tree) and takes back what it
    /// held. A boss behaviour holds states for its own length with itself as the key and lets go on its last line
    /// (Yggdrasil's PlayAnimations / Sweeping / Awakening: `cinematic`, `unstoppable`); the host's next behaviour often
    /// arrives a moment before this game's copy of the last one ends, so that last line never ran: the puppet stayed
    /// `cinematic` for the rest of the fight and this player's attacks went through it (found by the test probe,
    /// 2026-11-10: 'cinematic' on in 200 of 221 samples on the client, 61 on the host; one hit sent in 45 s).
    /// </summary>
    private void StopReplay(Character enemy)
    {
        if (!_replays.TryGetValue(enemy, out var running) || running.runner == null || running.routine == null)
            return;
        running.runner.StopCoroutine(running.routine);
        _replays.Remove(enemy);
        ReleaseHolds(enemy, running.runner);
    }

    private static readonly List<Characters.AI.Behaviours.Behaviour> HoldKeys = new();

    /// <summary>Lets go of the states the behaviours at and below <paramref name="runner"/> hold on the enemy.</summary>
    private static void ReleaseHolds(Character enemy, MonoBehaviour runner)
    {
        if (enemy == null || runner == null)
            return;
        HoldKeys.Clear();
        runner.GetComponentsInChildren(true, HoldKeys);
        int released = 0;
        foreach (var key in HoldKeys)
        {
            if (enemy.cinematic.Detach(key)) released++;
            if (enemy.invulnerable.Detach(key)) released++;
            if (enemy.health != null && enemy.health.undying.Detach(key)) released++;
            if (enemy.status != null && enemy.status.unstoppable.Detach(key)) released++;
        }
        HoldKeys.Clear();
        if (released > 0)
            CoopTrace.Write("holds-released", TraceRoom, TraceIdOf(enemy), CoopTrace.NameOf(enemy), runner.GetType().Name, released);
    }

    private void PlayBehaviour(EnemyEvent e)
    {
        var enemy = EnemyOf(e.NetId);
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health.dead)
        {
            CoopTrace.Write("behaviour-skipped", _room, e.NetId, CoopTrace.NameOf(enemy), enemy == null ? "no such enemy" : enemy.health.dead ? "dead" : "not active");
            return;
        }
        var behaviour = BehaviourOf(enemy, e.ActionPath);
        var ai = behaviour != null ? AiFor(enemy, behaviour) : null;
        if (behaviour == null || ai == null || !behaviour.gameObject.activeInHierarchy)
        {
            CoopTrace.Write("behaviour-not-found", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + (behaviour == null ? "" : ai == null ? " (no AI)" : " (inactive)"));
            return;
        }
        if (e.TargetSlot != 255)
            CoopTargeting.SetTarget(enemy, _playerOf(e.TargetSlot));
        BossSync.ForceOnClient(behaviour, e.Extra);
        // One at a time per boss, like the host's tree runs them (a late one must not overlap the next).
        StopReplay(enemy);
        _ownMoves.Remove(enemy);
        _replays[enemy] = (behaviour, behaviour.StartCoroutine(behaviour.CRun(ai)));
        CoopTrace.Write("behaviour-played", _room, e.NetId, CoopTrace.NameOf(enemy), behaviour.GetType().Name + " " + e.ActionPath,
            slot: e.TargetSlot == 255 ? -1 : e.TargetSlot);
    }

    private void EnterPhase(EnemyEvent e)
    {
        var enemy = EnemyOf(e.NetId);
        bool done = false;
        if (enemy != null && _aisOf.TryGetValue(enemy, out var ais))
            foreach (var ai in ais)
                done |= BossSync.EnterPhase(ai, e.Extra);
        CoopTrace.Write(done ? "phase-applied" : "phase-missing", _room, e.NetId, CoopTrace.NameOf(enemy), amount: e.Extra);
    }

    /// <summary>Host: a wave of this room appeared (EnemyWave.Spawn). A room's first waves often appear while it is
    /// still loading, before its enemies are bound here: the room and the wave's index come from the room as it is.</summary>
    internal void OnHostWave(EnemyWave wave)
    {
        var waves = Map.Instance?.waveContainer?.enemyWaves;
        int index = waves != null ? Array.IndexOf(waves, wave) : -1;
        uint room = CoopWorld.CurrentRoom();
        if (index >= 0 && room != 0)
        {
            CoopTrace.Write("wave", room, index);
            SendEvent(new EnemyEvent { Kind = EnemyEventKind.WaveSpawn, Wave = index }, room);
        }
        else
            Plugin.Log.LogWarning($"[Coop] a wave appeared outside a known room (index {index}, room {room:x8}): not shared");
    }

    private void SendSnapshot(double now)
    {
        if (now < _nextSnapshot)
            return;
        _nextSnapshot = Math.Max(_nextSnapshot + 1.0 / SnapshotRate, now);
        _states.Clear();
        Vector2 origin = _map.transform.position;
        for (int i = 0; i < _byId.Count; i++)
            AddState((ushort)(i + 1), _byId[i], origin);
        foreach (var pair in _lifeById)
            AddState(pair.Key, pair.Value, origin);
        for (int i = 0; i < _summons.Count; i++)
            AddState((ushort)(SummonFlag | (i + 1)), _summons[i], origin);
        _writer.Reset();
        EnemyMessages.WriteSnapshot(_writer, _room, now, _states);
        _session.Broadcast(MessageType.EnemySnapshot, _writer, Delivery.Unreliable);
        SnapshotsSent++;
    }

    private void AddState(ushort id, Character enemy, Vector2 origin)
    {
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health == null || enemy.health.dead)
            return;
        if (_diedSent.Count > 0)
            _diedSent.Remove(id); // alive again (revived): its next death is sent too
        Vector2 p = enemy.transform.position;
        var velocity = enemy.movement != null ? enemy.movement.velocity : Vector2.zero;
        var animation = enemy.animationController != null ? enemy.animationController.parameter : null;
        var flags = EnemyFlags.None;
        if (animation != null && animation.grounded)
            flags |= EnemyFlags.Grounded;
        if (animation != null && animation.walk)
            flags |= EnemyFlags.Walking;
        if (enemy.cinematic.value)
            flags |= EnemyFlags.Cinematic;
        if (enemy.invulnerable.value)
            flags |= EnemyFlags.Invulnerable;
        if (enemy.status != null && enemy.status.stuned)
            flags |= EnemyFlags.Stunned;
        if (enemy.status != null && enemy.status.freezed)
            flags |= EnemyFlags.Frozen;
        if (enemy.status != null && enemy.status.burning)
            flags |= EnemyFlags.Burning;
        if (enemy.status != null && enemy.status.poisoned)
            flags |= EnemyFlags.Poisoned;
        if (enemy.status != null && enemy.status.wounded)
            flags |= EnemyFlags.Wounded;
        _states.Add(new EnemyState
        {
            NetId = id,
            Flags = flags,
            Position = new Vec2(p.x - origin.x, p.y - origin.y),
            Velocity = new Vec2(velocity.x, velocity.y),
            FacingRight = enemy.lookingDirection == Character.LookingDirection.Right,
            Health = (float)enemy.health.currentHealth,
        });
    }

    /// <summary>
    /// Host: its own player's (or summons') hit on an enemy goes to the clients as a damage number only (COOP_PLAN
    /// 4.10: "teammates' hits in their colour"). A client's enemy loses the health by the host's snapshots, where the
    /// game shows no number. Unreliable: a lost number costs nothing. The same message as a client's hit, the other
    /// way round (a client never applies it).
    /// </summary>
    // REVIEW(fable): EnemyHit host -> clients now (unreliable, numbers only), REVIEW_QUEUE entry 6.
    private void SendHostHit(ushort id, Character attacker, in Damage took)
    {
        if (!IsHost || attacker == null || took.amount < 1.0 || _session.Roster.Count < 2 || CoopCopies.BelongsToCopy(attacker)
            || !CoopCopies.IsPlayerSide(attacker))
            return;
        _hitWriter.Reset();
        EnemyMessages.WriteHit(_hitWriter, _room, new EnemyHit
        {
            NetId = id, Amount = took.amount, Critical = took.critical, AttackType = (byte)took.attackType,
            MotionType = (byte)took.motionType, Attribute = (byte)took.attribute,
        });
        _session.Broadcast(MessageType.EnemyHit, _hitWriter, Delivery.Unreliable);
    }

    /// <summary>A client: the host's hit (<see cref="SendHostHit"/>) as a damage number in the host's colour.</summary>
    private void ShowHostHit(in EnemyHit hit)
    {
        if (Plugin.DamageNumbersTeammates?.Value == false)
            return;
        var enemy = EnemyOf(hit.NetId);
        if (enemy == null || !enemy.gameObject.activeInHierarchy)
            return;
        Vector2 at = enemy.collider != null ? (Vector2)enemy.collider.bounds.center : (Vector2)enemy.transform.position;
        var damage = new Damage(_avatarOf(0), hit.Amount, at, (Damage.Attribute)hit.Attribute, (Damage.AttackType)hit.AttackType,
            (Damage.MotionType)hit.MotionType, canCritical: false);
        damage.critical = hit.Critical;
        Patches.DamageNumberPatch.SpawnTeammate(damage, TeammateTags.ColorOf(0));
    }

    /// <summary>Host: a client's hit, applied to the real enemy as if the client's teammate copy struck it.</summary>
    private void OnHit(GameMessage message, NetReader payload)
    {
        var hit = EnemyMessages.ReadHit(payload, out uint room);
        if (!IsHost)
        {
            if (message.FromSlot == 0 && room == _room)
                ShowHostHit(hit);
            return;
        }
        if (room != _room)
            return;
        var enemy = EnemyOf(hit.NetId);
        if (enemy == null || enemy.health == null || enemy.health.dead || !enemy.gameObject.activeInHierarchy)
        {
            CoopTrace.Write("hit-refused", room, hit.NetId, CoopTrace.NameOf(enemy), enemy == null ? "no such enemy" :
                enemy.health != null && enemy.health.dead ? "already dead" : "not active", hit.Amount, slot: message.FromSlot);
            return;
        }
        double before = enemy.health.currentHealth;
        var attacker = _avatarOf(message.FromSlot);
        Vector2 at = enemy.collider != null ? (Vector2)enemy.collider.bounds.center : (Vector2)enemy.transform.position;
        var damage = new Damage(attacker, hit.Amount, at, (Damage.Attribute)hit.Attribute, (Damage.AttackType)hit.AttackType,
            (Damage.MotionType)hit.MotionType, canCritical: false);
        // Multipliers 1: the amount stays the client's (its crit included), the attribute brings the resistances.
        damage.critical = hit.Critical;
        // The flinch that interrupts the enemy's move, as the game's own attack path does before the damage
        // (Character.TryAttackCharacter): without it a client's hits never stopped an enemy's attack on the host.
        if (hit.StoppingPower > 0f && enemy.hit != null)
        {
            damage.stoppingPower = hit.StoppingPower;
            enemy.hit.Stop(hit.StoppingPower);
        }
        bool blocked = enemy.health.TakeDamage(ref damage);
        HitsApplied++;
        if (!blocked && damage.amount >= 1.0 && Plugin.DamageNumbersTeammates?.Value != false)
            Patches.DamageNumberPatch.SpawnTeammate(damage, TeammateTags.ColorOf(message.FromSlot));
        CoopTrace.Write("hit-applied", room, hit.NetId, CoopTrace.NameOf(enemy), hit.Critical ? "crit" : null, hit.Amount,
            before, enemy.health.currentHealth, message.FromSlot);
    }

    // ------------------------------------------------------------------ statuses that lock an enemy (stun, freeze)

    /// <summary>A puppet may stay locked on its own for this long (the round trip of this player's own stun or freeze).</summary>
    public const double LockGrace = 1.0, LockRetry = 0.5;
    private readonly Dictionary<(ushort, CharacterStatus.Kind), double> _lockOwnSince = new(), _lockTriedAt = new();

    /// <summary>
    /// Client: this player (or its minion) gave a puppet a status. The game shows it here at once; the host gives it
    /// to the real enemy too. A stun or freeze really stops the enemy for everyone (before, the puppet looked frozen
    /// here while the host's enemy went on attacking). Burn, poison and wound only SHOW there (and through the host's
    /// snapshot on the other clients): their damage stays this game's, it already reaches the host as this player's
    /// hits.
    /// </summary>
    internal void OnLocalStatus(Character target, Character attacker, CharacterStatus.Kind kind)
    {
        if (!IsClient || target == null || attacker == null || _lifeOnly.Contains(target) || !_ids.TryGetValue(target, out var id))
            return;
        if (kind > CharacterStatus.Kind.Poison)
            return;
        if (CoopCopies.BelongsToCopy(attacker) || !CoopCopies.IsPlayerSide(attacker))
            return;
        if (!IsLock(kind))
            _ownStatusAt[(id, kind)] = Now;
        _writer.Reset();
        EnemyMessages.WriteStatus(_writer, _room, id, (byte)kind);
        _session.Send(0, MessageType.EnemyStatus, _writer, Delivery.Reliable);
        CoopTrace.Write("status-sent", _room, id, CoopTrace.NameOf(target), kind.ToString());
    }

    private static bool IsLock(CharacterStatus.Kind kind) => kind == CharacterStatus.Kind.Stun || kind == CharacterStatus.Kind.Freeze;

    /// <summary>A status's running instance on a character (who gave it: <c>attacker</c>).</summary>
    private static Characters.Abilities.Statuses.CharacterStatusAbility Running(CharacterStatus status, CharacterStatus.Kind kind) => kind switch
    {
        CharacterStatus.Kind.Stun => status.stun,
        CharacterStatus.Kind.Freeze => status.freeze,
        CharacterStatus.Kind.Burn => status.burn,
        CharacterStatus.Kind.Wound => status.wound,
        CharacterStatus.Kind.Poison => status.poison,
        _ => null,
    };

    private static bool Has(CharacterStatus status, CharacterStatus.Kind kind) => kind switch
    {
        CharacterStatus.Kind.Stun => status.stuned,
        CharacterStatus.Kind.Freeze => status.freezed,
        CharacterStatus.Kind.Burn => status.burning,
        CharacterStatus.Kind.Wound => status.wounded,
        CharacterStatus.Kind.Poison => status.poisoned,
        _ => false,
    };

    /// <summary>Given by this game's own player or its minion (not shown for a teammate through its copy).</summary>
    private static bool IsOwn(Characters.Abilities.Statuses.CharacterStatusAbility running) =>
        running != null && running.attacker != null && !CoopCopies.BelongsToCopy(running.attacker);

    /// <summary>
    /// Host: a client's status on the real enemy, given by that client's copy (the enemy's own immunities decide, as
    /// for the host's own player). Stun and freeze are real. Burn, poison and wound only show: a copy's attacks do
    /// nothing in this game (CoopCopies), so their ticks deal no damage here; the client's own ticks arrive as its
    /// hits. The host's own burn or poison is never taken over by a copy (its ticks would stop); a wound is: a second
    /// wound sets off the bleeding, whose damage comes from the game of the player who set it off.
    /// </summary>
    private void OnStatus(GameMessage message, NetReader payload)
    {
        EnemyMessages.ReadStatus(payload, out uint room, out ushort id, out byte kindByte);
        if (!IsHost || room != _room)
            return;
        var enemy = EnemyOf(id);
        var attacker = _avatarOf(message.FromSlot);
        var kind = (CharacterStatus.Kind)kindByte;
        string result = "status-refused";
        if (enemy != null && enemy.status != null && enemy.health != null && !enemy.health.dead && enemy.gameObject.activeInHierarchy
            && attacker != null && kind <= CharacterStatus.Kind.Poison)
        {
            if ((kind == CharacterStatus.Kind.Burn || kind == CharacterStatus.Kind.Poison)
                && Has(enemy.status, kind) && IsOwn(Running(enemy.status, kind)))
                result = "status-kept";
            else
                try
                {
                    if (enemy.status.Apply(attacker, new CharacterStatus.ApplyInfo(kind)))
                        result = "status-applied";
                }
                catch (Exception e)
                {
                    StatusFailed("giving a client's " + kind + " to the host's enemy", e);
                }
        }
        CoopTrace.Write(result, room, id, CoopTrace.NameOf(enemy), kind.ToString(), slot: message.FromSlot);
    }

    private static readonly HashSet<string> StatusFailures = new();

    /// <summary>A status given through a copy ran into game code that needs a real player (logged once per kind).</summary>
    private static void StatusFailed(string what, Exception e)
    {
        if (StatusFailures.Add(what))
            Plugin.Log.LogWarning($"[Coop] {what} failed (logged once): {e}");
    }

    private Character AnyCopy()
    {
        for (int slot = 0; slot < 4; slot++)
        {
            var copy = _avatarOf(slot);
            if (copy != null)
                return copy;
        }
        return null;
    }

    // ------------------------------------------------------------------ statuses that only show (burn, poison, wound)

    /// <summary>After this player's own burn, poison or wound on a puppet the host's word waits this long (the round
    /// trip: the host's enemy shows it a moment later, and lets go of a set-off wound a moment later).</summary>
    public const double ShownGrace = 1.0;
    private readonly Dictionary<(ushort, CharacterStatus.Kind), double> _ownStatusAt = new(), _shownTriedAt = new();

    /// <summary>
    /// Client: a puppet shows burn, poison and wound exactly while the host's enemy has them, whoever gave them. Their
    /// damage stays with the game of the player who gave them (a client's ticks reach the host as its hits, the host's
    /// own are in the enemy's health), so what is added here only shows: a teammate's copy gives it, and a copy's
    /// attacks do nothing in this game (CoopCopies). On the host only: shown here. Here only: one that was only shown
    /// goes at once; this player's own burn or poison ends by itself (its ticks are this player's damage); its own
    /// wound, which has no end, goes with the host's (a teammate set off the bleeding there, or the host's enemy
    /// refused it).
    /// </summary>
    private void SyncShown(ushort id, Character enemy, CharacterStatus.Kind kind, bool onHost, double now)
    {
        var status = enemy.status;
        if (status == null || onHost == Has(status, kind))
            return;
        var key = (id, kind);
        if (_ownStatusAt.TryGetValue(key, out double own))
        {
            if (now - own < ShownGrace)
                return;
            _ownStatusAt.Remove(key);
        }
        if (onHost)
        {
            if (enemy.invulnerable.value || (_shownTriedAt.TryGetValue(key, out double tried) && now - tried < LockRetry))
                return;
            _shownTriedAt[key] = now;
            var giver = AnyCopy();
            if (giver == null)
                return;
            bool shown = false;
            try
            {
                shown = status.Apply(giver, new CharacterStatus.ApplyInfo(kind));
            }
            catch (Exception e)
            {
                StatusFailed("showing the host's " + kind + " on a puppet", e);
            }
            CoopTrace.Write(shown ? "status-shown" : "status-not-shown", _room, id, CoopTrace.NameOf(enemy), kind.ToString());
            return;
        }
        var running = Running(status, kind);
        if (kind != CharacterStatus.Kind.Wound && IsOwn(running))
            return;
        status.Remove(kind);
        // Burn and poison keep their time when they are taken off (only their own end sets it to 0), and "burning" /
        // "poisoned" read that time: without this the puppet counted as burning for good and was "ended" every frame
        // (2026-11-11: 2150 times in one room).
        if (running is Characters.Abilities.IAbilityInstance instance && instance.remainTime > 0f)
            instance.remainTime = 0f;
        CoopTrace.Write("status-ended", _room, id, CoopTrace.NameOf(enemy), kind.ToString());
    }

    /// <summary>
    /// Client: a puppet is stunned or frozen exactly while the host's enemy is (the snapshot's bits). Locked on the
    /// host only: given here by a teammate's copy (never by this game's player: its items would react to a status it
    /// didn't give). Locked here only: this player's own stun or freeze gets <see cref="LockGrace"/> for the host's
    /// answer, then the host's word counts (its enemy was immune, or its lock ended).
    /// </summary>
    private void SyncLock(ushort id, Character enemy, CharacterStatus.Kind kind, bool onHost, double now)
    {
        var status = enemy.status;
        if (status == null)
            return;
        bool here = kind == CharacterStatus.Kind.Stun ? status.stuned : status.freezed;
        var key = (id, kind);
        if (onHost == here)
        {
            _lockOwnSince.Remove(key);
            return;
        }
        if (onHost)
        {
            _lockOwnSince.Remove(key);
            if (status.unstoppable.value || enemy.invulnerable.value
                || (_lockTriedAt.TryGetValue(key, out double tried) && now - tried < LockRetry))
                return;
            _lockTriedAt[key] = now;
            var giver = AnyCopy();
            if (giver == null)
                return;
            bool shown = status.Apply(giver, new CharacterStatus.ApplyInfo(kind));
            CoopTrace.Write(shown ? "status-shown" : "status-not-shown", _room, id, CoopTrace.NameOf(enemy), kind.ToString());
            return;
        }
        // One that was only shown here (given by a copy) ends with the host's at once (a freeze broken by hits there);
        // this player's own waits for the host's answer.
        if (IsOwn(Running(status, kind)))
        {
            if (!_lockOwnSince.TryGetValue(key, out double since))
            {
                _lockOwnSince[key] = now;
                return;
            }
            if (now - since < LockGrace)
                return;
        }
        _lockOwnSince.Remove(key);
        status.Remove(kind);
        CoopTrace.Write("status-ended", _room, id, CoopTrace.NameOf(enemy), kind.ToString());
    }

    /// <summary>The key of the host's states on a puppet (cinematic, invulnerable).</summary>
    private static readonly object HostHold = new();

    private static void Hold(TrueOnlyLogicalSumList list, bool on)
    {
        if (on)
            list.Attach(HostHold);
        else
            list.Detach(HostHold);
    }

    /// <summary>Client: a stun or freeze this player gave a puppet goes to the host (<see cref="OnLocalStatus"/>).</summary>
    [HarmonyPatch(typeof(CharacterStatus), nameof(CharacterStatus.Apply))]
    public static class StatusGiven
    {
        private static void Postfix(CharacterStatus __instance, Character attacker, CharacterStatus.ApplyInfo applyInfo, bool __result)
        {
            if (__result && applyInfo != null)
                Current?.OnLocalStatus(__instance._character, attacker, applyInfo.kind);
        }
    }

    // ------------------------------------------------------------------ clients

    private void MakePuppet(Character enemy, ushort id)
    {
        _puppets.Add(enemy);
        LedgerPuppets.Add(enemy);
        if (_lifeOnly.Contains(enemy))
        {
            ShareLife(enemy, id);
            return;
        }
        // The AI often sits above the character (the Enemy container holds the characters and their behaviour trees;
        // seen in a trace: puppets kept attacking on their own when only the character's children were searched).
        var container = enemy.GetComponentInParent<Enemy>();
        var root = container != null ? container.gameObject : enemy.gameObject;
        var ais = new HashSet<Characters.AI.AIController>(root.GetComponentsInChildren<Characters.AI.AIController>(true));
        foreach (var ai in enemy.GetComponentsInParent<Characters.AI.AIController>(true))
            ais.Add(ai);
        foreach (var ai in ais)
        {
            ai.enabled = false;
            // A main loop already started (in Awake) keeps running on a switched-off component; later ones are
            // blocked (BossSync.NoPuppetMainLoop).
            ai.StopAllCoroutines();
            _puppetAis.Add(ai);
            LedgerAis.Add(ai);
        }
        _aisOf[enemy] = ais.ToList();
        var trees = new HashSet<BehaviorDesigner.Runtime.Behavior>(root.GetComponentsInChildren<BehaviorDesigner.Runtime.Behavior>(true));
        if (container?.behaviors != null)
            trees.UnionWith(container.behaviors);
        foreach (var tree in trees)
        {
            _puppetTrees.Add(tree);
            LedgerTrees.Add(tree);
            try { tree.DisableBehavior(true); }
            catch (Exception) { /* not started yet */ }
            // Paused alone isn't enough: a trace showed puppets' trees waking up when their wave appeared (the
            // component's OnEnable resumes it) and attacking ~0.2 s before the host's same move. Off, it stays off.
            tree.enabled = false;
        }
        if (enemy.movement != null)
            enemy.movement.enabled = false;
        ShareLife(enemy, id);
    }

    /// <summary>Client: the enemy lives as long as the host's (dies on its word) and this player's hits go there.</summary>
    private void ShareLife(Character enemy, ushort id)
    {
        enemy.health.undying.Attach(Key);
        if (CoopTrace.On)
        {
            Character.OnStartMotionDelegate onOwn = (motion, speed) =>
            {
                if (!_replaying)
                    CoopTrace.Write("puppet-own-motion", _room, id, CoopTrace.NameOf(enemy), MotionName(enemy, motion));
            };
            enemy.onStartMotion += onOwn;
            _unsubscribe.Add(() =>
            {
                if (enemy != null)
                    enemy.onStartMotion -= onOwn;
            });
        }
        TookDamageDelegate onHit = (in Damage original, in Damage took, double dealt) => OnPuppetHit(id, original, took);
        enemy.health.onTookDamage += onHit;
        _unsubscribe.Add(() =>
        {
            if (enemy != null)
                enemy.health.onTookDamage -= onHit;
        });
    }

    public static bool IsPuppet(Character character) => Current != null && character != null && Current._puppets.Contains(character);

    /// <summary>Client: an AI of a puppet (its own main loop and phase changes don't run here).</summary>
    public static bool IsPuppetAi(Characters.AI.AIController ai) => Current != null && ai != null && Current._puppetAis.Contains(ai);

    /// <summary>A puppet whose moves come from the host (not a trap or dummy, whose own code runs here too).</summary>
    public static bool IsRunByHost(Character character) => IsPuppet(character) && !Current._lifeOnly.Contains(character);

    /// <summary>For the trace: an enemy's net id in this room (-1: none) and a player's or copy's slot (-1: none).</summary>
    internal static int TraceIdOf(Character enemy) =>
        Current != null && enemy != null && Current._ids.TryGetValue(enemy, out var id) ? id : -1;

    internal static int TraceSlotOf(Character character) => Current?._slotOf(character) ?? -1;

    internal static uint TraceRoom => Current?._room ?? 0;

    // ------------------------------------------------------------------ the test probe (Testing/CoopProbe)

    /// <summary>Client: the moment of the host's clock its enemies are drawn at (NaN: host, or nothing drawn yet).</summary>
    internal double RenderTime => IsClient && _map != null && _delay.HasData && !double.IsInfinity(_lastRender) ? _lastRender : double.NaN;

    internal bool IsBound(Character character) => _ids.ContainsKey(character);

    /// <summary>The room's characters with a net id (enemies, summons, traps and dummies).</summary>
    internal void ForProbe(List<(ushort id, Character c, bool lifeOnly)> into)
    {
        foreach (var pair in _ids)
            into.Add((pair.Value, pair.Key, _lifeOnly.Contains(pair.Key)));
    }

    /// <summary>Client: this player hit a puppet: shown already by the game, now sent to the host.</summary>
    private void OnPuppetHit(ushort id, in Damage original, in Damage took)
    {
        var attacker = took.attacker.character;
        if (attacker == null || CoopCopies.BelongsToCopy(attacker) || !CoopCopies.IsPlayerSide(attacker))
            return;
        var hit = new EnemyHit
        {
            NetId = id,
            Amount = original.amount,
            Critical = original.critical,
            AttackType = (byte)original.attackType,
            MotionType = (byte)original.motionType,
            Attribute = (byte)original.attribute, // physical or magic: the host applies the enemy's resistances once
            StoppingPower = original.stoppingPower,
        };
        _writer.Reset();
        EnemyMessages.WriteHit(_writer, _room, hit);
        _session.Send(0, MessageType.EnemyHit, _writer, Delivery.Reliable);
        HitsSent++;
        NoteOwnHit(id, attacker, took);
        var puppet = EnemyOf(id);
        CoopTrace.Write("hit-sent", _room, id, CoopTrace.NameOf(puppet), original.critical ? "crit" : null, original.amount,
            after: puppet != null ? puppet.health.currentHealth : double.NaN);
    }

    private void OnSnapshot(GameMessage message, NetReader payload)
    {
        EnemyMessages.ReadSnapshot(payload, out uint room, out double time, _states);
        if (!IsClient || room != _room || _map == null)
            return;
        _delay.OnReceive(time, Now);
        foreach (var state in _states)
        {
            if (!_buffers.TryGetValue(state.NetId, out var buffer))
                _buffers[state.NetId] = buffer = new SnapshotBuffer<EnemyState>(16);
            buffer.Add(time, state);
        }
        SnapshotsReceived++;
    }

    private void OnEvent(GameMessage message, NetReader payload)
    {
        var e = EnemyMessages.ReadEvent(payload, out uint room, out double time);
        if (e == null || !IsClient)
            return;
        if (e.Kind == EnemyEventKind.RoomCleared)
        {
            HostCleared.Add(room);
            CoopTrace.Write("room-cleared-received", room);
            return;
        }
        if (room != _room)
        {
            if (room == 0)
                return;
            if (!_early.TryGetValue(room, out var list))
            {
                if (_early.Count >= 3)
                    _early.Clear(); // old rooms' leftovers
                _early[room] = list = new List<(double, EnemyEvent)>();
            }
            if (list.Count < 1024)
                list.Add((time, e));
            CoopTrace.Write("event-early", room, e.NetId, what: e.Kind.ToString());
            return;
        }
        if (e.Kind == EnemyEventKind.WaveSpawn)
            Apply(e); // at once: the enemies must exist before their first state
        else
            _events.Add(time, e);
    }

    private void Draw(double now)
    {
        if (!_delay.HasData)
            return;
        double render = _delay.Advance(now);
        if (render < _lastRender - 0.001)
            foreach (var buffer in _buffers.Values)
                buffer.Clear();
        _lastRender = render;
        Vector2 origin = _map.transform.position;
        foreach (var pair in _buffers)
        {
            var enemy = EnemyOf(pair.Key);
            if (enemy == null || enemy.health == null || enemy.health.dead)
                continue;
            var kind = pair.Value.Sample(render, out var s);
            if (!enemy.gameObject.activeInHierarchy && (kind == SampleKind.Empty || kind == SampleKind.Waiting || !ActivateLikeHost(pair.Key, enemy, false, now)))
                continue;
            if (kind == SampleKind.Empty || kind == SampleKind.Waiting)
                continue;
            if (_lifeOnly.Contains(enemy))
            {
                SyncHealth(pair.Key, enemy, s.Health, now);
                continue;
            }
            var t = enemy.transform;
            var position = t.position;
            position.x = origin.x + s.Position.X;
            position.y = origin.y + s.Position.Y;
            t.position = position;
            var facing = s.FacingRight ? Character.LookingDirection.Right : Character.LookingDirection.Left;
            if (enemy.lookingDirection != facing)
                enemy.lookingDirection = facing;
            bool grounded = (s.Flags & EnemyFlags.Grounded) != 0;
            StandingGround.Update(enemy, grounded); // its platform, for the host's boss helpers run here (BossSync)
            var p = enemy.animationController?.parameter;
            if (p != null)
            {
                // The host's own animator flags: a puppet's movement code is off, so nothing here sets "grounded",
                // and without it the walk animation never plays (Umut 2026-11-07: enemies "moonwalk" / slide).
                float speedX = Math.Abs(s.Velocity.X);
                p.grounded = grounded;
                p.walk = (s.Flags & EnemyFlags.Walking) != 0;
                p.movementSpeed = Math.Max(speedX, p.walk ? 1f : 0f) * 0.25f;
                p.ySpeed = grounded ? 0f : s.Velocity.Y;
            }
            SyncHealth(pair.Key, enemy, s.Health, now);
            // what the host's player can't hit, nobody can (an adventurer's intro: the host's was cinematic for 9 s,
            // the client's puppet never, so the client's hits landed during the scene; the probe, 2026-11-10)
            Hold(enemy.cinematic, (s.Flags & EnemyFlags.Cinematic) != 0);
            Hold(enemy.invulnerable, (s.Flags & EnemyFlags.Invulnerable) != 0);
            SyncLock(pair.Key, enemy, CharacterStatus.Kind.Stun, (s.Flags & EnemyFlags.Stunned) != 0, now);
            SyncLock(pair.Key, enemy, CharacterStatus.Kind.Freeze, (s.Flags & EnemyFlags.Frozen) != 0, now);
            SyncShown(pair.Key, enemy, CharacterStatus.Kind.Burn, (s.Flags & EnemyFlags.Burning) != 0, now);
            SyncShown(pair.Key, enemy, CharacterStatus.Kind.Poison, (s.Flags & EnemyFlags.Poisoned) != 0, now);
            SyncShown(pair.Key, enemy, CharacterStatus.Kind.Wound, (s.Flags & EnemyFlags.Wounded) != 0, now);
        }
        while (_events.TryTakeDue(render, out var due))
            Apply(due.Item);
        for (int i = 0; i < _held.Count; i++)
        {
            if (_held[i].due > Now)
                continue;
            var held = _held[i].e;
            _held.RemoveAt(i--);
            Apply(held);
            _wasHeld.Remove(held);
        }
    }

    /// <summary>The host's health, except an older (higher) number right after this player's own hit; a heal is
    /// taken once it stayed higher for a second.</summary>
    private void SyncHealth(ushort id, Character enemy, float hostHealth, double now)
    {
        double current = enemy.health.currentHealth;
        if (hostHealth < current - 0.5)
        {
            enemy.health.SetCurrentHealth(Math.Max(1, hostHealth));
            _higherSince.Remove(id);
        }
        else if (hostHealth > current + 0.5)
        {
            if (!_higherSince.TryGetValue(id, out double since))
                _higherSince[id] = now;
            else if (now - since > 1.0)
            {
                enemy.health.SetCurrentHealth(hostHealth);
                _higherSince.Remove(id);
            }
        }
        else
            _higherSince.Remove(id);
    }

    private void Apply(EnemyEvent e)
    {
        _applying = true;
        try
        {
            switch (e.Kind)
            {
                case EnemyEventKind.WaveSpawn:
                    var waves = _map?.waveContainer?.enemyWaves;
                    if (waves != null && e.Wave < waves.Length && waves[e.Wave] != null)
                    {
                        CoopTrace.Write("wave-applied", _room, e.Wave, what: waves[e.Wave].state.ToString());
                        waves[e.Wave].Spawn();
                    }
                    else
                        CoopTrace.Write("wave-missing", _room, e.Wave);
                    break;
                case EnemyEventKind.Motion:
                    var enemy = EnemyOf(e.NetId);
                    if (enemy != null && !enemy.health.dead && !enemy.gameObject.activeInHierarchy)
                        ActivateLikeHost(e.NetId, enemy, true);
                    if (enemy != null && enemy.gameObject.activeInHierarchy && !enemy.health.dead)
                    {
                        // The host's target: this player, or a teammate's copy (its attack aims there here too).
                        if (e.TargetSlot != 255)
                            CoopTargeting.SetTarget(enemy, _playerOf(e.TargetSlot));
                        var action = RemoteAvatar.FindAction(enemy, e.ActionPath);
                        // A move of a helper call that runs here (it starts its moves itself).
                        if (action != null && _ownMoves.TryGetValue(enemy, out var own) && Now < own.until && own.moves.Contains(action))
                        {
                            CoopTrace.Write("motion-own", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + "#" + e.MotionIndex);
                            break;
                        }
                        // A later part of a move (#1, #2...) that the puppet already went on to by itself when the
                        // host's earlier part ended (the game chains them): replaying it would restart it (a trace
                        // showed the host's part arriving 15-85 ms after the puppet's own; a fireball could fire twice).
                        var running = enemy.runningMotion;
                        if (action != null && running != null && e.MotionIndex > 0 && running.action == action
                            && e.MotionIndex < action.motions.Length && action.motions[e.MotionIndex] == running)
                        {
                            CoopTrace.Write("motion-chained", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + "#" + e.MotionIndex);
                            break;
                        }
                        // The previous move (another one) is in its last moments: let it end, then play this one.
                        if (running != null && !_wasHeld.Contains(e) && action != null
                            && (e.MotionIndex >= action.motions.Length || action.motions[e.MotionIndex] != running))
                        {
                            float left = running.length - running.time;
                            if (left > 0f && left < FinishWindow)
                            {
                                _held.Add((Now + left + 0.02, e));
                                _wasHeld.Add(e);
                                CoopTrace.Write("motion-held", _room, e.NetId, CoopTrace.NameOf(enemy), e.ActionPath + "#" + e.MotionIndex, amount: left);
                                break;
                            }
                        }
                        // Where the host's enemy stood when the move began: a move's effects start from there (a
                        // puppet drawn a little in the past was still gliding through the air after a teleport, and
                        // the Dark sister's ground wave started in the air).
                        if (_map != null && !_lifeOnly.Contains(enemy))
                        {
                            var t = enemy.transform;
                            t.position = new Vector3(_map.transform.position.x + e.X, _map.transform.position.y + e.Y, t.position.z);
                        }
                        PuppetAim.Before(enemy, action, CoopTargeting.TargetOf(enemy));
                        _replaying = true;
                        bool found;
                        try
                        {
                            found = RemoteAvatar.Play(enemy, e.ActionPath, e.MotionIndex, e.Speed);
                        }
                        finally
                        {
                            _replaying = false;
                        }
                        CoopTrace.Write(found ? "motion-played" : "motion-not-found", _room, e.NetId, CoopTrace.NameOf(enemy),
                            e.ActionPath + "#" + e.MotionIndex, slot: e.TargetSlot == 255 ? -1 : e.TargetSlot);
                    }
                    else
                        CoopTrace.Write("motion-skipped", _room, e.NetId, CoopTrace.NameOf(enemy),
                            enemy == null ? WhyMissing(e.NetId) : enemy.health.dead ? "dead" : "not active");
                    break;
                case EnemyEventKind.Behaviour:
                    PlayBehaviour(e);
                    break;
                case EnemyEventKind.Phase:
                    EnterPhase(e);
                    break;
                case EnemyEventKind.Call:
                    PlayCall(e);
                    break;
                case EnemyEventKind.Target:
                    var aiming = EnemyOf(e.NetId);
                    if (aiming != null && !aiming.health.dead && e.TargetSlot != 255)
                    {
                        CoopTargeting.SetTarget(aiming, _playerOf(e.TargetSlot));
                        CoopTrace.Write("target-applied", _room, e.NetId, CoopTrace.NameOf(aiming), slot: e.TargetSlot);
                    }
                    break;
                case EnemyEventKind.Summon:
                    PlaySummon(e);
                    break;
                case EnemyEventKind.DarkElite:
                    PlayDarkElite(e);
                    break;
                case EnemyEventKind.Died:
                    var dying = EnemyOf(e.NetId);
                    if (dying != null && !dying.health.dead)
                    {
                        dying.health.Kill();
                        Deaths++;
                        CoopTrace.Write("died-applied", _room, e.NetId, CoopTrace.NameOf(dying),
                            dying.gameObject.activeInHierarchy ? "active" : "inactive");
                    }
                    else
                    {
                        // a summon still waiting for its prefab (PlaySummon) that died meanwhile: never made
                        if (dying == null)
                            foreach (var waiting in _summonWaits.Keys.Where(w => w.NetId == e.NetId).ToList())
                            {
                                _summonWaits.Remove(waiting);
                                _held.RemoveAll(h => h.e == waiting);
                            }
                        CoopTrace.Write("died-skipped", _room, e.NetId, CoopTrace.NameOf(dying), dying == null ? "no such enemy" : "already dead");
                    }
                    if (dying != null)
                        CreditKill(dying, e.NetId, e.TargetSlot);
                    break;
            }
            EventsPlayed++;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Coop] enemy event {e.Kind} #{e.NetId}: {ex.Message}");
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>A client applying the host's word (a wave, a death): the patches below let it through.</summary>
    public static bool ApplyingHost => Current?._applying == true;

    /// <summary>A helper call's step runs (BossSync.Steps): its moves are the host's, not the puppet's own. Returns
    /// the mark before, for <see cref="ExitReplay"/>.</summary>
    internal static bool EnterReplay()
    {
        var sync = Current;
        if (sync == null)
            return false;
        bool before = sync._replaying;
        sync._replaying = true;
        return before;
    }

    internal static void ExitReplay(bool before)
    {
        if (Current != null)
            Current._replaying = before;
    }

    // ------------------------------------------------------------------ patches

    /// <summary>
    /// Host: its room is done (Map.CCheckRewardActivating stops the room's waves right before the reward comes out):
    /// clients are told, so their room may finish too.
    /// </summary>
    [HarmonyPatch(typeof(EnemyWaveContainer), nameof(EnemyWaveContainer.Stop))]
    public static class RoomStopPatch
    {
        private static void Postfix(EnemyWaveContainer __instance)
        {
            var sync = Current;
            if (sync == null || !sync.IsHost || __instance != Map.Instance?.waveContainer)
                return;
            uint room = CoopWorld.CurrentRoom();
            if (room == 0 || room == _sentCleared)
                return;
            _sentCleared = room;
            CoopTrace.Write("room-cleared", room);
            sync.SendEvent(new EnemyEvent { Kind = EnemyEventKind.RoomCleared }, room);
        }
    }

    /// <summary>
    /// Client: the room may finish (waves stopped, reward out, `Map.CCheckRewardActivating`) only once the host's room
    /// did. A client's first wave waits for the host's word (~0.5 s), and in that gap the game saw a room without
    /// enemies: with the player already near the reward spot it stopped every wave and put out the reward, and the
    /// host's wave could no longer appear (Umut 2026-11-07: "not all enemies are shown for both"; trace: wave-applied
    /// "Stopped"). After the host's word the game's own check runs as before (enemies gone, player near the reward).
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.CCheckRewardActivating))]
    public static class RewardCheckPatch
    {
        private static bool _passThrough;

        private static bool Prefix(Map __instance, ref IEnumerator __result)
        {
            if (_passThrough || Current == null)
                return true;
            __result = Current.IsClient ? AfterHost(__instance) : HostAfterStart(__instance);
            return false;
        }

        /// <summary>
        /// Host: a room whose fight has not begun yet (every enemy wave still waits, e.g. Leiana's sisters until
        /// their intro) is not finished by a TEAMMATE standing in the reward range, only by this game's own player
        /// (as alone). The game's check stops the room's waves once its waves are empty and anyone is in that range,
        /// and a stopped wave never spawns: on 2026-11-16 the client's test bot jumped to the reward spot of the
        /// sisters' throne room before their intro, its copy stood there on the host, and the sisters never came
        /// (Umut's hand test, COOP_LOG 59i/60). Once a wave has spawned, the game's own check runs as before.
        /// </summary>
        // REVIEW(fable): host authority over a room's end, REVIEW_QUEUE 11.
        private static IEnumerator HostAfterStart(Map map)
        {
            double since = Now;
            while (map != null && Current?.IsHost == true && !RoomBegun(map) && !OwnPlayerInRewardRange(map))
                yield return null;
            if (map == null)
                yield break;
            if (Now - since > 1)
                CoopTrace.Write("reward-check-held", CoopWorld.CurrentRoom(), amount: Now - since);
            foreach (var step in RunOwn(map))
                yield return step;
        }

        private static IEnumerable<object> RunOwn(Map map)
        {
            IEnumerator own;
            _passThrough = true;
            try
            {
                own = map.CCheckRewardActivating();
            }
            finally
            {
                _passThrough = false;
            }
            while (own.MoveNext())
                yield return own.Current;
        }

        private static bool OwnPlayerInRewardRange(Map map)
        {
            var range = map._mapRewardActivatingRange;
            var player = Singleton<Service>.Instance?.levelManager?.player;
            if (range == null || player == null)
                return false;
            var body = player.collider;
            return body != null ? range.bounds.Intersects(body.bounds) : range.OverlapPoint(player.transform.position);
        }

        private static IEnumerator AfterHost(Map map)
        {
            double since = Now;
            uint room = 0;
            while (map != null)
            {
                var sync = Current;
                if (sync == null || !sync.IsClient || World?.Active != true)
                    break;
                room = CoopWorld.CurrentRoom();
                if (HostCleared.Contains(room))
                    break;
                yield return null;
            }
            if (map == null)
                yield break;
            CoopTrace.Write("room-finish-allowed", room, amount: Now - since);
            foreach (var step in RunOwn(map))
                yield return step;
        }
    }

    /// <summary>The map in which an enemy wave last appeared (any game: the host's own, a client's on the host's
    /// word). See <see cref="RoomBegun"/>.</summary>
    private static Map _begunMap;

    /// <summary>
    /// The room's fight has begun or can't begin any more: it has no enemy waves, one of them appeared here, or none
    /// still waits. False in a room whose waves all still wait for their start (a boss intro, a trigger): its
    /// reward range must not end it yet (<see cref="RewardCheckPatch"/>, the test bot's walk to the reward).
    /// </summary>
    internal static bool RoomBegun(Map map)
    {
        var waves = map != null && map.waveContainer != null ? map.waveContainer.enemyWaves : null;
        if (waves == null || waves.Length == 0 || ReferenceEquals(_begunMap, map))
            return true;
        foreach (var wave in waves)
            if (wave != null && wave.state == Wave.State.Waiting)
                return false;
        return true;
    }

    /// <summary>Host: waves that appear are sent. Clients: waves appear only on the host's word.</summary>
    [HarmonyPatch(typeof(EnemyWave), nameof(EnemyWave.Spawn))]
    public static class WaveSpawnPatch
    {
        private static bool Prefix(EnemyWave __instance, out bool __state)
        {
            __state = __instance.state == Wave.State.Waiting;
            var sync = Current;
            return sync == null || !sync.IsClient || ApplyingHost;
        }

        private static void Postfix(EnemyWave __instance, bool __state)
        {
            if (__instance.state == Wave.State.Spawned && Map.Instance != null && __instance.transform.IsChildOf(Map.Instance.transform))
                _begunMap = Map.Instance;
            var sync = Current;
            if (__state && sync != null && sync.IsHost && __instance.state == Wave.State.Spawned)
                sync.OnHostWave(__instance);
        }
    }

    /// <summary>
    /// Enemy summon operations. Clients: nothing makes an enemy here in a bound room (the host's summons come as events).
    /// Host: the characters that are new after the operation are summons: each gets a summon id and goes to clients.
    /// </summary>
    [HarmonyPatch]
    public static class Summons
    {
        /// <summary>The operations' types; <see cref="RunOf"/> picks the Run each one declares.</summary>
        internal static readonly Type[] Types =
        {
            typeof(Characters.Operations.SpawnCharacter), typeof(Characters.Operations.SpawnRandomCharacter),
            typeof(Characters.Operations.Summon.SummonCharacter), typeof(Characters.Operations.Summon.SummonCharacterInPool),
            typeof(Characters.Operations.Summon.SummonCharactersInRange), typeof(Characters.Operations.Summon.SummonBDCharacter),
            typeof(Characters.Operations.Summon.SummonBDRandomCharacter),
            // the Pope room's "(Delay) Fanatic Brothers" (a fanatic's sacrifice): both games made their own (n4, COOP_LOG 62k)
            typeof(Characters.Operations.Summon.SummonMonster),
        };

        /// <summary>
        /// The Run a summon operation itself declares: Run(Character) or Run(). Before (COOP_LOG 62), the lookup asked
        /// for Run(Character) first, which also finds the base Operation's, so a type that declares only Run()
        /// (SummonBDCharacter, SummonBDRandomCharacter) was never patched: on a client the HighFanatic's call, the flask
        /// bottles, the Dark Golem and the Pope's tentacles made enemies of their own beside the host's summons
        /// (Kerkopi's test: 124 "unsynced-enemy"; nobody could hit them and they didn't count for the room).
        /// </summary>
        internal static System.Reflection.MethodInfo RunOf(Type type) =>
            AccessTools.DeclaredMethod(type, "Run", new[] { typeof(Character) }) ?? AccessTools.DeclaredMethod(type, "Run", Type.EmptyTypes);

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var type in Types)
            {
                var run = RunOf(type);
                if (run != null)
                    yield return run;
                else
                    Plugin.Log.LogWarning($"[Coop] summon operation {type.Name}: no Run of its own (not watched)");
            }
        }

        private static bool Prefix(Component __instance, object[] __args, out HashSet<Character> __state)
        {
            __state = null;
            var sync = Current;
            if (sync == null)
                return true;
            var owner = (__args != null && __args.Length > 0 ? __args[0] as Character : null) ?? CoopTargeting.CharacterOf(__instance);
            if (sync.IsClient && (IsRunByHost(owner) || sync.LeavesSummonToHost(owner, __instance)))
            {
                sync.NoteSpawnPrefabs(__instance); // the host's summon may name a prefab only this operation knows
                return false;
            }
            if (sync.IsHost && sync.Bound)
                __state = new HashSet<Character>(UnityEngine.Object.FindObjectsOfType<Character>());
            return true;
        }

        private static void Postfix(Component __instance, object[] __args, HashSet<Character> __state)
        {
            var sync = Current;
            if (__state == null || sync == null)
                return;
            var owner = (__args != null && __args.Length > 0 ? __args[0] as Character : null) ?? CoopTargeting.CharacterOf(__instance);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Character>())
                if (c != null && !__state.Contains(c) && c.type != Character.Type.Player && c.type != Character.Type.PlayerMinion)
                    sync.OnHostSummon(owner, c);
        }
    }

    /// <summary>Host: a character put into the room's summon wave outside a summon operation (the Pope's fanatic
    /// factory instantiates them itself): a summon for clients like any other.</summary>
    [HarmonyPatch(typeof(EnemyWaveContainer), nameof(EnemyWaveContainer.Attach))]
    public static class AttachedOnHost
    {
        private static void Postfix(Character character)
        {
            var sync = Current;
            if (sync != null && sync.IsHost && sync.Bound && character != null
                && character.type != Character.Type.Player && character.type != Character.Type.PlayerMinion)
                sync.OnHostSummon(HostDarkSummoner, character); // known ones (a summon operation's) are skipped there; a dark
                                                               // ability's summon names its elite (DarkAbilitySummons)
        }
    }

    /// <summary>Host: a character put straight into the summon wave (`waveContainer.summonWave.Attach`, not through
    /// EnemyWaveContainer.Attach): the Pope room's platform tentacles (Level.Chapter4.Platform) and a fanatic's
    /// sacrifice tentacle (Characters.AI.Behaviours.Sacrifice) were never shared: "unsynced Tentacle x2" on the host,
    /// hits nobody else saw (n4, COOP_LOG 62k). Known ones are skipped in OnHostSummon.</summary>
    [HarmonyPatch(typeof(global::SummonWave), nameof(global::SummonWave.Attach))]
    public static class AttachedToSummonWaveOnHost
    {
        private static void Postfix(Character character)
        {
            var sync = Current;
            if (sync != null && sync.IsHost && sync.Bound && character != null
                && character.type != Character.Type.Player && character.type != Character.Type.PlayerMinion
                && !CoopCopies.IsPlayerSide(character))
                sync.OnHostSummon(SummonerHint ?? HostDarkSummoner, character);
        }
    }

    /// <summary>Host: the character a summon wave attach is made for, while the game's code that attaches is running
    /// (a fanatic's sacrifice: its tentacle; a Pope room platform: the Pope). The client then looks for the prefab
    /// below that summoner first.</summary>
    [ThreadStatic] private static Character SummonerHint;

    [HarmonyPatch(typeof(Characters.AI.Behaviours.Sacrifice), "SummonTentacle")]
    public static class SacrificeSummoner
    {
        private static void Prefix(Character owner) => SummonerHint = owner;
        private static void Finalizer() => SummonerHint = null;
    }

    /// <summary>Clients: a Pope room platform's purification makes no tentacle here (the host's comes as a summon);
    /// its look (the platform's operations) still runs.</summary>
    [HarmonyPatch(typeof(Level.Chapter4.Platform), "CRunPurifiy")]
    public static class NoPlatformTentacleOnClients
    {
        private static bool Prefix(Level.Chapter4.Platform __instance, Character owner, ref IEnumerator __result)
        {
            var sync = Current;
            if (sync == null || !sync.IsClient || !sync.Bound || ApplyingHost)
                return true;
            __result = LookOnly(__instance, owner);
            return false;
        }

        private static IEnumerator LookOnly(Level.Chapter4.Platform platform, Character owner)
        {
            platform._operations.gameObject.SetActive(true);
            platform._operations.Run(owner);
            CoopTrace.Write("summon-left", Current?._room ?? 0, name: "Tentacle", what: "Platform");
            yield return owner.chronometer.master.WaitForSeconds(platform._duration);
        }
    }

    /// <summary>Clients: the Pope's fanatic factory makes no fanatics of its own (the host's come as summons). Its
    /// ladder picks still run (the phase-1 end drops them), only the climb and the spawn are the host's.</summary>
    [HarmonyPatch(typeof(Characters.AI.Pope.FanaticFactory), "CSummon")]
    public static class NoFanaticsOnClients
    {
        private static bool Prefix(ref IEnumerator __result)
        {
            if (Current?.IsClient != true)
                return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }

    /// <summary>Clients: a puppet's first target isn't "the player" of this game (EnemyCharacterBehaviorOption, read
    /// #89, when it appears): its target comes with the host's moves. Trace: it pointed the puppet's tree at the
    /// client's own player until the next move.</summary>
    [HarmonyPatch(typeof(EnemyCharacterBehaviorOption), nameof(EnemyCharacterBehaviorOption.SetTargetToPlayer))]
    public static class NoPuppetPlayerTarget
    {
        private static bool Prefix(EnemyCharacterBehaviorOption __instance)
        {
            var sync = Current;
            if (sync == null)
                return true;
            var character = CoopTargeting.CharacterOf(__instance);
            sync.NoteUnsynced(character);
            return sync._puppets.Count == 0 || !IsRunByHost(character);
        }
    }

    private static readonly HashSet<string> UnsyncedLogged = new();

    /// <summary>
    /// Diagnosis (COOP_LOG 39): an enemy that appears in a bound room without a net id runs on each game by itself
    /// (DM's Dark Quartz Golem: ~20 s into some rooms, on both games, each going for its own player; its spawner
    /// comes from asset data, not found in the code). Logged once per name with the call stack, to find the spawner.
    /// </summary>
    private void NoteUnsynced(Character character)
    {
        if (_map == null || character == null || _ids.ContainsKey(character) || _lifeOnly.Contains(character)
            || character.type is Character.Type.Player or Character.Type.PlayerMinion or Character.Type.Trap or Character.Type.Dummy)
            return;
        if (IsHost)
        {
            // a summon's own settings aim it before the summon operation ends and gives it its id (a false alarm
            // for every summon of 11-20's bot run): looked at again next frame
            if (_unsyncedLater.Count < 64)
                _unsyncedLater.Add((character, Time.frameCount));
            return;
        }
        ReportUnsynced(character);
    }

    private readonly List<(Character c, int frame)> _unsyncedLater = new();

    /// <summary>Host: the characters still without an id a frame after they were aimed (Tick).</summary>
    private void CheckUnsyncedLater()
    {
        for (int i = _unsyncedLater.Count - 1; i >= 0; i--)
        {
            var (c, frame) = _unsyncedLater[i];
            if (Time.frameCount <= frame)
                continue;
            _unsyncedLater.RemoveAt(i);
            if (c != null && _map != null && !_ids.ContainsKey(c) && !_lifeOnly.Contains(c))
                ReportUnsynced(c);
        }
    }

    private void ReportUnsynced(Character character)
    {
        string name = Loadout.GearName(character.name);
        CoopTrace.Write("unsynced-enemy", _room, name: CoopTrace.NameOf(character), what: character.type.ToString());
        if (UnsyncedLogged.Add(name))
            Plugin.Log.LogWarning($"[Coop] an enemy appeared that isn't synced: {name} ({character.type}); spawned by:\n{Environment.StackTrace}");
    }

    /// <summary>Clients: a puppet's behaviour tree stays off (something may try to start it at spawn).</summary>
    [HarmonyPatch(typeof(BehaviorDesigner.Runtime.Behavior), nameof(BehaviorDesigner.Runtime.Behavior.EnableBehavior))]
    public static class NoPuppetBehaviour
    {
        private static bool Prefix(BehaviorDesigner.Runtime.Behavior __instance) =>
            Current == null || Current._puppets.Count == 0
            || !(Current._puppetTrees.Contains(__instance) || IsRunByHost(__instance.GetComponentInParent<Character>()));
    }
}
