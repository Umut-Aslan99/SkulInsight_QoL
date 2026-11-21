using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters.Controllers;
using DamageInsight.Coop.Net;
using DamageInsight.Lang;
using GameResources;
using HarmonyLib;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// One world for the whole party (COOP_PLAN §4.6, §5.1; GAME_FLOW §2.5, hooks 6-9): the host decides the run and the
/// rooms, the others follow.
/// - Run start: only the host leaves the castle; its `Load(Chapter1)` sends RunStart (seed, chapter), clients load the
///   same chapter with the same seed once the host's first path arrived. A client at the portal gets a notice.
/// - Same rooms: the host sends every stage's generated path (StagePath); a client's GeneratePath builds its path
///   from it (rooms by their Addressables GUID), so saves that differ (special rooms seen, NPCs rescued) don't matter.
/// - Same doors: where the game picks one of two same-type doors at random (Map.SetExits), co-op picks from the seed.
/// - Room changes: the host's LoadNextMap sends NextMap; clients go through the same door when they are in that room.
///   A client's own door does nothing for now (the door vote comes with the rules, step 4).
/// - Loading barrier: each game holds its player and time still in a new room until everyone is there (10 s at most).
/// </summary>
internal sealed class CoopWorld
{
    public static CoopWorld Current { get; private set; }

    private static readonly object BarrierKey = new();
    private readonly CoopSession _session;
    private readonly MonoBehaviour _host;
    private readonly TeammateTags _tags;
    private readonly NetWriter _writer = new(256);
    private readonly Dictionary<string, List<(PathNodeData, PathNodeData)>> _paths = new();
    private readonly Queue<(int seed, string stage, int pathIndex, int node)> _maps = new();
    private readonly RoomBarrier _barrier = new();
    private readonly CoopDoors _doors;
    private (int seed, int chapter)? _runStart;
    private int _runStartLevel, _hostDarkMirrorLevel; // the host's Dark Mirror level (DarkMirrorLevel)
    private double _runStartSince, _mapWaitSince = -1, _nextNotice;
    private bool _following, _loading, _frozen;
    private LevelManager _subscribed;
    private Map _roomIn; // the map the last "loaded and faded in" was for
    // Catch-up (a client out of step goes to the host's room): the host's last reported room, the decision, timing.
    private readonly CatchUp _catchUp = new();
    private RunSpot? _hostSpot;
    private double _nextHostRoom;
    private bool _gaveUpLogged;
    // Scene watchdog: the host's last "in a story scene", and since when this game's scene look outlasts it.
    private bool _hostInScene, _sentInScene;
    private double _sceneSince = -1;

    public CoopWorld(CoopSession session, MonoBehaviour host, TeammateTags tags, bool rejoin = false)
    {
        _session = session;
        _host = host;
        _tags = tags;
        Current = this;
        // REVIEW(fable): package DC. A client joining again after a broken line stands in a loaded room: it counts as
        // loaded (its enemies bind again, the host's snapshots drive them) instead of waiting for the next room.
        // REVIEW(fable): COOP_LOG 60u. The same for ANY session that starts in a loaded map: a Steam lobby or a
        // matchmaking session opens in the castle, and no "map in" event comes for the castle it already stands in.
        // Without this a joining client never counted its castle as loaded, so it never followed the host's run start
        // (`n1 --matchmaking` 2026-11-16: the host played 9 rooms alone; the friend test over Steam would have too).
        // Sessions of the test copies start on the title screen (no map yet): unchanged.
        if (Map.Instance != null)
        {
            _roomIn = Map.Instance;
            CoopTrace.Write("world-in-room", what: rejoin ? "rejoin" : "started in a loaded map");
        }
        session.RegisterHandler(MessageType.RunStart, OnRunStart);
        session.RegisterHandler(MessageType.StagePath, OnStagePath);
        session.RegisterHandler(MessageType.NextMap, OnNextMap);
        session.RegisterHandler(MessageType.HostRoom, (m, r) => _hostSpot = WorldMessages.ReadHostRoom(r, out _hostInScene, out _hostDarkMirrorLevel));
        session.RegisterHandler(MessageType.RoomReady, (m, r) => _barrier.Reached(m.FromSlot, WorldMessages.ReadRoomReady(r)));
        if (session.IsHost)
            session.SetRelay(MessageType.RoomReady);
        session.PlayerLeft += (p, _) => _barrier.Forget(p.Slot);
        _doors = new CoopDoors(session, this, tags);
    }

    /// <summary>A session with teammates: co-op rules apply (alone in a session the game plays as usual).</summary>
    public bool Active => _session.State == SessionState.Active && _session.Roster.Count > 1;
    public bool IsHost => Active && _session.IsHost;
    public bool IsClient => Active && !_session.IsHost;
    /// <summary>The room is loaded (its enemies are built), also while the barrier waits.</summary>
    public bool RoomLoaded => !_loading && Map.Instance != null && _roomIn == Map.Instance;

    /// <summary>Loading a room, waiting at the barrier or about to start the run.</summary>
    public bool Busy => _loading || _barrier.Waiting || _runStart.HasValue;

    private static LevelManager Levels => Singleton<Service>.Instance?.levelManager;
    private static int Seed => Data.GameData.Save.instance.randomSeed;

    public void Dispose()
    {
        _doors.Dispose();
        Unfreeze();
        DarkMirrorLevel.Restore();
        if (_subscribed != null)
            _subscribed.onMapChangedAndFadedIn -= OnMapIn;
        if (Current == this)
            Current = null;
    }

    private void Notice(string text)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextNotice)
            return;
        _nextNotice = now + 3;
        var player = Levels?.player;
        if (player != null)
            Singleton<Service>.Instance.floatingTextSpawner.SpawnBuff(text, player.transform.position + Vector3.up * 2.2f);
    }

    // ------------------------------------------------------------------ every frame

    public void Tick(double now)
    {
        var levels = Levels;
        if (levels != _subscribed)
        {
            if (_subscribed != null)
                _subscribed.onMapChangedAndFadedIn -= OnMapIn;
            _subscribed = levels;
            if (levels != null)
                levels.onMapChangedAndFadedIn += OnMapIn;
        }
        if (_outTrackFrom.HasValue && Map.Instance != null && Map.Instance.GetInstanceID() != _outTrackFrom.Value)
        {
            // the out-of-track map is there (EnterOutTrackPatch): a room like any other from here on
            _outTrackFrom = null;
            Plugin.Log.LogInfo($"[Coop] out-of-track map {Map.Instance.name}: room {CurrentRoom():x8}");
            CoopTrace.Write("out-track", CurrentRoom(), name: Map.Instance.name);
            OnMapIn(_roomIn, Map.Instance);
        }
        if (_runStart.HasValue && IsClient)
            TryStartRun(now);
        if (_maps.Count > 0 && IsClient)
            TryNextMap(now);
        if (IsHost && (now >= _nextHostRoom || InScene() != _sentInScene))
            SendHostRoom(now);
        if (IsClient)
        {
            CheckInStep(now);
            CheckScene(now);
        }
        if (_barrier.Waiting)
        {
            var slots = _session.Roster.Select(p => p.Slot).ToList();
            if (_barrier.Release(slots, now, out bool timedOut))
            {
                Unfreeze();
                _tags.Banner("");
                CoopDiagnostics.Current?.Barrier(_barrier.LastWaitSeconds, timedOut);
                CoopTrace.Write(timedOut ? "barrier-timeout" : "barrier-released", CurrentRoom(), what: timedOut ? Names(_barrier.Missing(slots)) : null);
                if (timedOut)
                    Plugin.Log.LogWarning("[Coop] loading barrier: went on without " + Names(_barrier.Missing(slots)));
            }
            else
                _tags.Banner(Loc.F("Waiting for {0}...", Names(_barrier.Missing(slots))));
        }
        _doors.Tick(now);
    }

    private string Names(IEnumerable<int> slots) =>
        string.Join(", ", slots.Select(s => _session.PlayerAt(s)?.Name ?? "Player " + (s + 1)));

    // ------------------------------------------------------------------ run start

    /// <summary>The host leaves the castle: everyone starts this run with its seed.</summary>
    private void SendRunStart(Chapter.Type chapter)
    {
        _writer.Reset();
        int level = Data.GameData.HardmodeProgress.hardmodeLevel;
        WorldMessages.WriteRunStart(_writer, Seed, (int)chapter, level);
        _session.Broadcast(MessageType.RunStart, _writer, Delivery.Reliable);
        Plugin.Log.LogInfo($"[Coop] run start sent: {chapter}, seed {Seed}" + (DarkMirrorLevel.IsRunChapter((int)chapter) ? $", Dark Mirror level {level}" : ""));
        CoopTrace.Write("run-start", what: chapter + " seed " + Seed);
    }

    private void OnRunStart(GameMessage message, NetReader payload)
    {
        _runStart = WorldMessages.ReadRunStart(payload, out _runStartLevel);
        _runStartSince = Time.realtimeSinceStartupAsDouble;
        Plugin.Log.LogInfo($"[Coop] the host starts the run: {(Chapter.Type)_runStart.Value.chapter}, seed {_runStart.Value.seed}");
    }

    /// <summary>A client starts the host's run once the host's first path is here (3 s at most) and it stands in a castle.</summary>
    private void TryStartRun(double now)
    {
        var (seed, chapter) = _runStart.Value;
        bool havePath = _paths.Keys.Any(k => k.StartsWith(seed + ":", StringComparison.Ordinal));
        var levels = Levels;
        // Never while this game is still loading (title screen, its castle): two loads at once mix both maps.
        if (levels?.currentChapter == null || levels.player == null || !RoomLoaded || (!havePath && now - _runStartSince < 3))
            return;
        _runStart = null;
        if (!havePath)
            Plugin.Log.LogWarning("[Coop] run start: the host's first path didn't come in time; this game makes its own");
        _host.StartCoroutine(CStartRun(seed, (Chapter.Type)chapter));
    }

    private IEnumerator CStartRun(int seed, Chapter.Type chapter)
    {
        _loading = true;
        yield return Singleton<Service>.Instance.fadeInOut.CFadeOut();
        Data.GameData.Save.instance._randomSeed.value = seed; // what the daily run does (DailyRun.Arm)
        if (DarkMirrorLevel.IsRunChapter((int)chapter))
            DarkMirrorLevel.Borrow(_runStartLevel);
        HostWorld.Reapply(); // the run's rooms are built from the host's flags
        _following = true;
        try
        {
            Levels.Load(chapter);
        }
        finally
        {
            _following = false;
        }
    }

    // ------------------------------------------------------------------ the same rooms

    /// <summary>A stage's name: its chapter and asset name (the assets are only called "0", "1", ... per chapter).</summary>
    private static string StageName(IStageInfo stage) =>
        (Levels?.currentChapter?.type.ToString() ?? "") + "/" + (stage != null ? stage.name : "");

    private static string PathKey(int seed, string stage) => seed + ":" + stage;

    /// <summary>Host: a stage's path was generated; every game gets it.</summary>
    private void SendPath(StageInfo stage)
    {
        var path = stage._path;
        if (path == null || InCastle())
            return;
        var data = path.Select(p => (ToData(p.type1), ToData(p.type2))).ToList();
        _writer.Reset();
        WorldMessages.WriteStagePath(_writer, Seed, StageName(stage), data);
        _session.Broadcast(MessageType.StagePath, _writer, Delivery.Reliable);
        Plugin.Log.LogInfo($"[Coop] path of {StageName(stage)} sent ({data.Count} rooms)");
    }

    private static PathNodeData ToData(PathNode node)
    {
        var data = new PathNodeData();
        if (node == null)
            return data;
        data.Reward = (byte)node.reward;
        data.Gate = (byte)node.gate;
        var map = node.reference;
        if (map?.reference != null && !map.empty)
        {
            data.Guid = map.reference.AssetGUID ?? "";
            data.AssetPath = map.path ?? "";
            data.MapType = (byte)map.type;
            data.SpecialType = (byte)map.specialMapType;
            data.DarkEnemy = map.darkEnemy;
        }
        return data;
    }

    private static PathNode FromData(PathNodeData data)
    {
        MapReference reference = null;
        if (!data.Empty)
        {
            reference = new MapReference(new UnityEngine.AddressableAssets.AssetReference(data.Guid))
            {
                path = data.AssetPath,
                type = (Map.Type)data.MapType,
                specialMapType = (SpecialMap.Type)data.SpecialType,
                darkEnemy = data.DarkEnemy,
            };
        }
        return new PathNode(reference, (MapReward.Type)data.Reward, (Gate.Type)data.Gate);
    }

    private void OnStagePath(GameMessage message, NetReader payload)
    {
        var (seed, stage, path) = WorldMessages.ReadStagePath(payload);
        _paths[PathKey(seed, stage)] = path;
        // A path that came after this game made its own (it entered the stage first): take it while still at the start.
        var current = Levels?.currentChapter?.currentStage as StageInfo;
        if (current != null && StageName(current) == stage && Seed == seed && current.pathIndex <= 0)
        {
            current._path = path.Select(p => (FromData(p.Item1), FromData(p.Item2))).ToArray();
            Plugin.Log.LogInfo($"[Coop] path of {stage} replaced by the host's");
        }
    }

    /// <summary>Client: the host's path for this stage, if it came.</summary>
    private static bool InCastle()
    {
        var type = Levels?.currentChapter?.type;
        return type == Chapter.Type.Castle || type == Chapter.Type.HardmodeCastle || type == Chapter.Type.Tutorial;
    }

    private bool TryHostPath(StageInfo stage)
    {
        if (InCastle())
            return false; // every game's own castle
        string name = StageName(stage);
        if (!_paths.TryGetValue(PathKey(Seed, name), out var path))
        {
            Plugin.Log.LogWarning($"[Coop] no host path for {name} yet: this game makes its own for now");
            return false;
        }
        stage._path = path.Select(p => (FromData(p.Item1), FromData(p.Item2))).ToArray();
        Plugin.Log.LogInfo($"[Coop] {name}: the host's path ({path.Count} rooms)");
        return true;
    }

    /// <summary>The room this game is in, as every game of the run names it (0 = no room).</summary>
    internal static uint CurrentRoom()
    {
        var stage = Levels?.currentChapter?.currentStage;
        var map = Map.Instance;
        return map == null ? 0 : WorldMessages.RoomId(Seed, StageName(stage), stage?.pathIndex ?? 0, map.name);
    }

    /// <summary>Which of two same-type doors shows: from the seed and the room, the same on every game.</summary>
    internal static bool ExitChoice()
    {
        var stage = Levels?.currentChapter?.currentStage;
        uint id = WorldMessages.RoomId(Seed, StageName(stage), stage?.pathIndex ?? 0, "exits");
        return (id & 1) == 0;
    }

    // ------------------------------------------------------------------ room changes

    private void SendNextMap(NodeIndex node)
    {
        var stage = Levels?.currentChapter?.currentStage;
        _writer.Reset();
        WorldMessages.WriteNextMap(_writer, Seed, StageName(stage), stage?.pathIndex ?? 0, (int)node);
        _session.Broadcast(MessageType.NextMap, _writer, Delivery.Reliable);
        CoopTrace.Write("door", CurrentRoom(), (int)node, what: StageName(stage) + " room " + (stage?.pathIndex ?? 0));
    }

    private void OnNextMap(GameMessage message, NetReader payload)
    {
        _maps.Enqueue(WorldMessages.ReadNextMap(payload));
        if (_mapWaitSince < 0)
            _mapWaitSince = Time.realtimeSinceStartupAsDouble;
    }

    /// <summary>A client goes through the host's door once it stands in the same room (not loading).</summary>
    private void TryNextMap(double now)
    {
        var levels = Levels;
        var stage = levels?.currentChapter?.currentStage;
        if (!RoomLoaded || stage == null || _runStart.HasValue)
            return;
        var (seed, name, pathIndex, node) = _maps.Peek();
        if (seed != Seed || name != StageName(stage) || pathIndex != stage.pathIndex)
        {
            if (now - _mapWaitSince > 15)
            {
                Plugin.Log.LogError($"[Coop] out of step with the host: it left {name} room {pathIndex}, this game is in " +
                                    $"{StageName(stage)} room {stage.pathIndex} (seed {Seed} vs {seed})");
                _mapWaitSince = now; // logged every 15 s until it catches up (desync repair: phase 2)
            }
            return;
        }
        _maps.Dequeue();
        _mapWaitSince = _maps.Count > 0 ? now : -1;
        _loading = true;
        _following = true;
        CoopTrace.Write("door-followed", CurrentRoom(), node, what: name + " room " + pathIndex);
        try
        {
            levels.LoadNextMap((NodeIndex)node);
        }
        finally
        {
            _following = false;
        }
    }

    // ------------------------------------------------------------------ catch-up

    /// <summary>Where this game is (null: no chapter yet).</summary>
    private static RunSpot? OwnSpot()
    {
        var chapter = Levels?.currentChapter;
        if (chapter == null)
            return null;
        if (InCastle())
            return RunSpot.InCastle((int)chapter.type);
        var stage = chapter.currentStage;
        return new RunSpot(false, Seed, (int)chapter.type, chapter.stageIndex, stage?.pathIndex ?? 0, (int)(stage?.nodeIndex ?? 0));
    }

    /// <summary>Host: its room, on each new room and every 3 s (a client that missed something sees it again).</summary>
    private void SendHostRoom(double now)
    {
        _nextHostRoom = now + 3;
        var spot = OwnSpot();
        if (spot == null)
            return;
        _sentInScene = InScene();
        _writer.Reset();
        WorldMessages.WriteHostRoom(_writer, spot.Value, _sentInScene, Data.GameData.HardmodeProgress.hardmodeLevel);
        _session.Broadcast(MessageType.HostRoom, _writer, Delivery.Reliable);
    }

    /// <summary>A story scene shows (its letterbox: only scenes use it, menus don't).</summary>
    private static bool InScene()
    {
        try
        {
            var letterBox = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.letterBox;
            return letterBox != null && letterBox.visible;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Client: a story scene that ran here but not on the host can get stuck: the scene waits for something only the
    /// host's game does (Dark Mirror's Awakened Leiana: letterbox, HUD hidden and input blocked for the rest of the
    /// fight). After 2 s in a scene look while the host shows none: letterbox off, HUD on, the scene's input blocks
    /// released (never the loading barrier's). A stopgap until scenes play in sync (step 4). Also missing then: the
    /// boss health bar that the scene's end would open.
    /// </summary>
    private void CheckScene(double now)
    {
        // Never this player's own talk (Umut 2026-11-20, findings 2 and 3: after 1 s the stopgap took the letterbox
        // and the input block of a client's NPC talk or Arachne's awakening, so the player walked about during it;
        // 18x in Kerkopi's test): only a shared scene that outlived the host's.
        if (_frozen || _hostInScene || !InScene() || SharedScenes.LastTalkPersonal || OwnTalkOpen())
        {
            _sceneSince = -1;
            return;
        }
        if (_sceneSince < 0)
        {
            _sceneSince = now;
            return;
        }
        if (now - _sceneSince < StuckSceneSeconds)
            return;
        _sceneSince = -1;
        var ui = Scenes.Scene<Scenes.GameBase>.instance.uiManager;
        ui.letterBox.Disappear();
        ui.headupDisplay.visible = true;
        int released = ReleaseInputBlocks();
        Plugin.Log.LogWarning($"[Coop] a scene stayed on here but not on the host: letterbox off, HUD on, {released} input block(s) released");
        CoopTrace.Write("scene-released", CurrentRoom(), amount: released);
    }

    /// <summary>How long a shared scene may stay on here after the host's ended before the stopgap ends it (a client
    /// that is a little behind finishes its last line meanwhile; was 1 s).</summary>
    public const double StuckSceneSeconds = 3.0;

    /// <summary>An NPC's talk box is open on this game (its own conversation, never the stopgap's).</summary>
    private static bool OwnTalkOpen()
    {
        try
        {
            var ui = Scenes.Scene<Scenes.GameBase>.instance?.uiManager;
            var talk = ui != null ? ui.npcConversation : null;
            return talk != null && talk.gameObject.activeInHierarchy && talk.visible;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static System.Reflection.FieldInfo _blockList;

    private static int ReleaseInputBlocks()
    {
        var blocked = PlayerInput.blocked;
        _blockList ??= blocked.GetType().GetField("_list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (_blockList?.GetValue(blocked) is not System.Collections.IList keys)
            return 0;
        var release = new List<object>();
        foreach (object key in keys)
            if (key != BarrierKey)
                release.Add(key);
        foreach (object key in release)
            blocked.Detach(key);
        return release.Count;
    }

    /// <summary>Client: out of step with the host for 3 s with nothing on the way: go to the host's room.</summary>
    private void CheckInStep(double now)
    {
        var own = OwnSpot();
        if (own == null)
            return;
        bool doorFromHere = false;
        if (_maps.Count > 0)
        {
            var stage = Levels?.currentChapter?.currentStage;
            var (seed, name, pathIndex, _) = _maps.Peek();
            doorFromHere = stage != null && seed == Seed && name == StageName(stage) && pathIndex == stage.pathIndex;
        }
        switch (_catchUp.Decide(_hostSpot, own.Value, Busy || !RoomLoaded, doorFromHere, now))
        {
            case CatchUpStep.InStep:
                _gaveUpLogged = false;
                break;
            case CatchUpStep.Jump:
                _catchUp.Jumped(_hostSpot.Value);
                _host.StartCoroutine(CJump(_hostSpot.Value, own.Value));
                break;
            case CatchUpStep.GiveUp:
                if (!_gaveUpLogged)
                {
                    _gaveUpLogged = true;
                    Plugin.Log.LogError($"[Coop] out of step: three tries to reach the host's room ({_hostSpot}) failed; staying in {own}");
                }
                break;
        }
    }

    /// <summary>
    /// Client: load the host's room. Another chapter or run (or the castle): that chapter with the host's seed, like a
    /// run start (it begins at its first room; the next check goes on from there). The same chapter: the exact room,
    /// the way the game continues a saved run (`Chapter.Enter(stage, path, node)`).
    /// </summary>
    private IEnumerator CJump(RunSpot to, RunSpot from)
    {
        _loading = true;
        _maps.Clear();
        _mapWaitSince = -1;
        Plugin.Log.LogWarning($"[Coop] out of step: this game is in {from}, the host in {to}: going there");
        CoopTrace.Write("catch-up", CurrentRoom(), what: $"{from} -> {to}");
        bool inChapter = to.SameChapter(from);
        if (!inChapter)
            yield return Singleton<Service>.Instance.fadeInOut.CFadeOut();
        _following = true;
        try
        {
            var levels = Levels;
            HostWorld.Reapply();
            if (to.Castle)
                levels.Load((Chapter.Type)to.Chapter);
            else if (!inChapter)
            {
                Data.GameData.Save.instance._randomSeed.value = to.Seed;
                if (DarkMirrorLevel.IsRunChapter(to.Chapter))
                    DarkMirrorLevel.Borrow(_hostDarkMirrorLevel);
                levels.Load((Chapter.Type)to.Chapter);
            }
            else
                levels.currentChapter.Enter(to.Stage, to.Path, to.Node);
        }
        catch (Exception e)
        {
            _loading = false;
            Plugin.Log.LogError("[Coop] catch-up failed: " + e.Message);
        }
        finally
        {
            _following = false;
        }
    }

    // ------------------------------------------------------------------ loading barrier

    private void OnMapIn(Map old, Map @new)
    {
        _loading = false;
        _roomIn = @new;
        if (IsHost)
            SendHostRoom(Time.realtimeSinceStartupAsDouble);
        var chapter = Levels?.currentChapter;
        if (chapter != null && (chapter.type == Chapter.Type.Castle || chapter.type == Chapter.Type.HardmodeCastle))
        {
            DarkMirrorLevel.Restore(); // a run is over: this game's own Dark Mirror level again
            HostWorld.Reapply(); // the castle may have read the save anew (a game loaded from the title screen)
        }
        if (!Active || chapter == null || @new == null || chapter.type == Chapter.Type.Castle || chapter.type == Chapter.Type.HardmodeCastle)
            return;
        var stage = chapter.currentStage;
        uint room = CurrentRoom();
        _writer.Reset();
        WorldMessages.WriteRoomReady(_writer, room);
        _session.Broadcast(MessageType.RoomReady, _writer, Delivery.Reliable);
        _barrier.Enter(_session.LocalSlot, room, Time.realtimeSinceStartupAsDouble);
        Freeze();
    }

    private void Freeze()
    {
        if (_frozen)
            return;
        _frozen = true;
        PlayerInput.blocked.Attach(BarrierKey);
        Chronometer.global.AttachTimeScale(BarrierKey, 0f);
    }

    private void Unfreeze()
    {
        if (!_frozen)
            return;
        _frozen = false;
        PlayerInput.blocked.Detach(BarrierKey);
        Chronometer.global.DetachTimeScale(BarrierKey);
    }

    // ------------------------------------------------------------------ patches

    [HarmonyPatch(typeof(StageInfo), "GeneratePath")]
    public static class StagePathPatch
    {
        private static bool Prefix(StageInfo __instance) => !(Current?.IsClient == true && Current.TryHostPath(__instance));

        private static void Postfix(StageInfo __instance)
        {
            if (Current?.IsHost == true)
                Current.SendPath(__instance);
        }
    }

    [HarmonyPatch(typeof(HardmodeStageInfo), "GeneratePath")]
    public static class HardmodeStagePathPatch
    {
        private static bool Prefix(HardmodeStageInfo __instance) => !(Current?.IsClient == true && Current.TryHostPath(__instance));

        private static void Postfix(HardmodeStageInfo __instance)
        {
            if (Current?.IsHost == true)
                Current.SendPath(__instance);
        }
    }

    /// <summary>Map.SetExits with the random choice between two same-type doors taken from the seed (co-op only).</summary>
    [HarmonyPatch(typeof(Map), nameof(Map.SetExits))]
    public static class SameExits
    {
        private static bool Prefix(Map __instance, PathNode node1, PathNode node2)
        {
            if (Current?.Active != true || __instance._gate1 == null || __instance._gate2 == null
                || node1.gate != node2.gate || node1.gate == Gate.Type.Boss)
                return true;
            __instance._gate1.GetComponent<SpriteRenderer>().sortingOrder = -1;
            __instance._gate2.GetComponent<SpriteRenderer>().sortingOrder = -2;
            if (ExitChoice())
            {
                __instance._gate1.Set(node1.gate, NodeIndex.Node1);
                __instance._gate2.ShowDestroyed(node1.gate == Gate.Type.Terminal);
            }
            else
            {
                __instance._gate1.ShowDestroyed(node1.gate == Gate.Type.Terminal);
                __instance._gate2.Set(node2.gate, NodeIndex.Node2);
            }
            return false;
        }
    }

    /// <summary>The host's chapter loads that start a run tell everyone: chapter 1, or any chapter loaded from the
    /// castle (the autorun's test start at a later chapter, `-AutoRun -AutoRunChapter N`).</summary>
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.Load), new[] { typeof(Chapter.Type) })]
    public static class RunStartPatch
    {
        private static void Prefix(LevelManager __instance, Chapter.Type chapter)
        {
            if (Current?.IsHost != true || IsCastleOrTutorial(chapter))
                return;
            var from = __instance.currentChapter?.type;
            if (chapter == Chapter.Type.Chapter1 || chapter == Chapter.Type.HardmodeChapter1
                || from == Chapter.Type.Castle || from == Chapter.Type.HardmodeCastle)
                Current.SendRunStart(chapter);
        }

        private static bool IsCastleOrTutorial(Chapter.Type type) =>
            type is Chapter.Type.Castle or Chapter.Type.HardmodeCastle or Chapter.Type.Tutorial or Chapter.Type.Test;
    }

    /// <summary>A client in the castle portal waits for the host (the castle shows the outside, nothing loads).</summary>
    [HarmonyPatch(typeof(CastleCameraController), "CTransitionToOutside")]
    public static class ClientCastleExit
    {
        private static bool Prefix(CastleCameraController __instance, Vector3 playerPosition, ref IEnumerator __result)
        {
            if (Current?.IsClient != true)
                return true;
            __result = Outside(__instance, playerPosition);
            return false;
        }

        private static IEnumerator Outside(CastleCameraController castle, Vector3 playerPosition)
        {
            castle._state = CastleCameraController.State.Outside;
            Map.Instance?.ChangeLight(castle._outsideLightColor, castle._outsideLightIntensity, castle._lightColorTransitionTime);
            castle._cameraController.zone = castle._outsideCameraZone;
            if (castle._portal.bounds.Contains(playerPosition))
            {
                // Out of the portal again: past it the camera can't follow (only the host's walk-in loads the run).
                var player = Levels?.player;
                if (player != null)
                {
                    var p = player.transform.position;
                    p.x = castle._portal.bounds.min.x - 1.5f;
                    player.transform.position = p;
                    player.movement?.controller?.ResetBounds();
                    Physics2D.SyncTransforms();
                }
                Current?.Notice(Loc.T("The host starts the run."));
            }
            yield break;
        }
    }

    /// <summary>
    /// The castle's magic mirror (normal castle <-> Dark Mirror castle) is the host's (Umut 2026-11-16: "the host leads;
    /// when the host goes through the dark mirror, every other player follows at once"): a client's mirror only says
    /// so; the client follows the host's castle by itself (CatchUp: two castles of different dimensions are not the
    /// same place; CastleGrace).
    /// </summary>
    [HarmonyPatch(typeof(HardmodeMagicMirror), nameof(HardmodeMagicMirror.InteractWith))]
    public static class ClientMirror
    {
        private static bool Prefix()
        {
            var world = Current;
            if (world == null || !world.IsClient || !world.Active)
                return true;
            world.Notice(Loc.T("The host chooses the castle."));
            CoopTrace.Write("mirror-refused", CurrentRoom());
            return false;
        }
    }

    /// <summary>
    /// The game wipes the level manager's events when it resets its scene (Service.ResetGameScene -> ClearEvents:
    /// after the ending credits). The level manager stays the same object, so this world never noticed that its
    /// "map is in" handler was gone: no room counted as loaded any more, the host sent no rooms and a client never
    /// started the host's next run (n5, 2026-11-11: after the First Hero and the credits the host stood in chapter 5
    /// again, the client in the castle; Umut saw it in the test windows). Tick subscribes again.
    /// </summary>
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ClearEvents))]
    public static class EventsCleared
    {
        private static void Postfix()
        {
            var world = Current;
            if (world == null)
                return;
            world._subscribed = null;
            Plugin.Log.LogInfo("[Coop] the game cleared its level events (scene reset): subscribing again");
        }
    }

    private int? _outTrackFrom; // the map's instance id when the game began to swap in an out-of-track map

    /// <summary>
    /// An "out of track" map: the game swaps the map inside a scene (`Runnables.EnterOutTrackMap` ->
    /// `LevelManager.EnterOutTrack` -> `Chapter.CEnterOutTack`: destroy the map, instantiate the new one) and raises
    /// none of its map events. King Alexander's giant and his later phases are such maps (dm6): this world kept
    /// the destroyed map as its room, so no room counted as loaded from the giant on: no enemy was bound on either
    /// game, and when the host went on to the next arena its player was gone for the client (Umut watching,
    /// 2026-11-13). Every game runs the scene itself (story scenes start together), so each one swaps its own map;
    /// here the swap is noticed and <see cref="Tick"/> treats the new map as a room that came in (ready message,
    /// loading barrier, enemies bound), under the room id every game makes from the map's name.
    /// </summary>
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.EnterOutTrack))]
    public static class EnterOutTrackPatch
    {
        private static void Prefix()
        {
            var world = Current;
            if (world == null)
                return;
            world._loading = true;
            world._outTrackFrom = Map.Instance != null ? Map.Instance.GetInstanceID() : 0;
        }
    }

    /// <summary>The host's room changes go to everyone; a client's own (not following the host) don't happen.</summary>
    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.LoadNextMap))]
    public static class NextMapPatch
    {
        private static bool Prefix(NodeIndex nodeIndex)
        {
            var world = Current;
            if (world?.Active != true)
                return true;
            if (world.IsHost)
            {
                world.SendNextMap(nodeIndex);
                world._loading = true;
                return true;
            }
            return world._following;
        }
    }

    /// <summary>A press on a door is that player's vote, on every game (CoopDoors): the room changes when the
    /// host's vote resolves, through LoadNextMap on the host. Alone in a session the game's own door stays.</summary>
    [HarmonyPatch(typeof(Gate), nameof(Gate.InteractWith))]
    public static class DoorPress
    {
        private static bool Prefix(Gate __instance)
        {
            var world = Current;
            if (world?.Active != true)
                return true;
            world._doors.Press(__instance);
            return false;
        }
    }
}
