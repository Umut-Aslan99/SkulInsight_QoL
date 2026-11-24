using System.Collections;
using System.Linq;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// `--skulinsight-autorun` (tools/coop/start-instances.ps1 -AutoRun): an unattended run test for copies on one PC.
/// Every copy's player takes no damage (nobody is at the keys); the host leaves the castle once all players are in
/// the session and the castle. Then one of two routes:
/// - every room (default): the host goes through a shown door every <see cref="CoopLaunch.AutoRunDoorSeconds"/> (also
///   before the room is cleared), so the others' following, the paths and the loading barrier get exercised;
/// - `--skulinsight-autorun-boss`: straight to the last room of each stage (the stage's adventurers, the chapter's
///   boss) the way the game continues a saved run (`Chapter.Enter(stage, path, node)`; clients follow by the catch-up).
/// In a boss room the boss stays alive until it showed no new move for a while, then its health goes down in steps
/// (next phase at 0) and it is killed at the end, each boss of the room in turn (BossStep). When the route is over the trace gets "autorun-done" (tools/coop/coop-test.py stops the copies then).
/// Never in a normal game.
/// </summary>
internal sealed class CoopAutoRun
{
    private static readonly object Key = new();
    private readonly MonoBehaviour _host;
    private readonly int _startChapter, _maxRooms, _fromRoom, _dmLevel, _players;
    private bool _waitLogged;
    private readonly bool _skipStages, _darkMirror, _bossRoute, _byHand;
    // the full skull sweep (FullSweepPlan): the route stays in the run's first room, the bots take nothing and vote
    // for no door (tools/coop/coop-test.py stops the copies when both bots are through their lists)
    private readonly bool _fullSweep;
    private readonly float _doorSeconds;
    private Characters.Character _guarded;
    // --skulinsight-bot-attack: the test players' attack (null: unchanged)
    private readonly Characters.Stat.Values _attack;
    private float _castleSince = -1, _nextDoor;
    private bool _starting, _done;
    private int _doorsTaken, _skips, _runs;
    public const float SecondRunSeconds = 15f;

    public CoopAutoRun(MonoBehaviour host, CoopLaunch launch)
    {
        _host = host;
        _darkMirror = launch.AutoRunDarkMirror;
        _dmLevel = launch.AutoRunDarkMirrorLevel;
        _startChapter = Mathf.Clamp(launch.AutoRunChapter, 1, _darkMirror ? 6 : 5);
        _skipStages = launch.AutoRunSkip;
        _bossRoute = launch.AutoRunBoss;
        _byHand = !launch.Bot; // a person plays this copy (play-revive-test.cmd: --no-host-bot)
        _doorSeconds = launch.AutoRunDoorSeconds;
        _maxRooms = launch.AutoRunRooms;
        _players = launch.AutoRunPlayers;
        _fromRoom = launch.AutoRunFrom;
        _fullSweep = launch.Bot && FullSweepPlan.Parse(launch.BotSkulls) != null;
        if (launch.BotAttackPercent > 100f)
        {
            // percent points on top of whatever the skull and items give: "at least" the asked percent
            double points = (launch.BotAttackPercent - 100f) / 100.0;
            _attack = new Characters.Stat.Values(
                new Characters.Stat.Value(Characters.Stat.Category.PercentPoint, Characters.Stat.Kind.PhysicalAttackDamage, points),
                new Characters.Stat.Value(Characters.Stat.Category.PercentPoint, Characters.Stat.Kind.MagicAttackDamage, points));
        }
    }

    public void Tick(CoopWorld world)
    {
        var levels = Singleton<Service>.Instance?.levelManager;
        var player = levels?.player;
        if (player != null && player != _guarded)
        {
            _guarded = player;
            player.invulnerable.Attach(Key);
            if (_attack != null)
            {
                player.stat.AttachValues(_attack);
                Plugin.Log.LogInfo("[CoopTest] this copy's player attacks with the test's attack bonus");
            }
        }
        float now = Time.unscaledTime;
        if (world != null && player != null && world.RoomLoaded && !world.Busy && !_fullSweep)
            Loot(now, player);
        if (world != null && player != null && world.IsClient && !_bossRoute && world.RoomLoaded && !world.Busy && !_fullSweep)
            ClientVote(now, player);
        if (world != null && player != null && world.Active && !_byHand && !_bossRoute && world.RoomLoaded && !world.Busy && !_fullSweep)
            BotMenu(now);
        if (world == null || !world.IsHost || player == null || levels.currentChapter == null || _starting)
            return;
        var type = levels.currentChapter.type;
        if (type == Chapter.Type.Castle || type == Chapter.Type.HardmodeCastle)
        {
            if (_castleSince < 0)
                _castleSince = now;
            // 3-4 copies (COOP_LOG 70): wait until every copy joined (a client can join in the castle only), at most 120 s.
            int inSession = CoopRuntime.Instance?.Session?.Roster.Count ?? 1;
            if (inSession < _players && now - _castleSince < 120f)
                return;
            if (inSession < _players && !_waitLogged)
            {
                _waitLogged = true;
                Plugin.Log.LogWarning($"[CoopTest] autorun: only {inSession} of {_players} players after 120 s in the castle; the run starts anyway");
            }
            if (now - _castleSince > 6f && !_done)
                _host.StartCoroutine(CLeaveCastle(levels, (type == Chapter.Type.HardmodeCastle || _darkMirror ? Chapter.Type.HardmodeChapter1 : Chapter.Type.Chapter1) + (_startChapter - 1)));
            return;
        }
        _castleSince = -1;
        // The run is over (everyone fell, or a game-over scene ended it: package KA): the result screen waits for a
        // key the bot never presses, so the route ends here (2026-11-16 dm6 waited 7 min for the tool's timeout).
        if (!_done && CoopRevive.RunIsOver)
        {
            Done("the run is over (game over)");
            return;
        }
        TrackRoom(now, world);
        BossStep(now);
        if (_done || Map.Instance == null || world.Busy || !world.RoomLoaded)
            return;
        if (_runs > 1)
        {
            // Back from an ending (chapter 5's credits end in the castle) the host started a second run: the route
            // is over once that run's first room stood for a moment, so the report shows whether everyone came
            // along (2026-11-11: the client stayed in the castle; before that the route simply began again).
            if (_room != null && Map.Instance == _room && now - _roomSince > SecondRunSeconds)
                Done("a second run began after the ending");
            return;
        }
        if (_fullSweep)
            return; // the full skull sweep plays here: the route stays in this room
        if (LastRoomOver(now, levels))
        {
            // A boss room that never showed an enemy is a stuck room, not a finished one (2026-11-16: Leiana's
            // sisters never came, and the runs said "finished"): sync-report.py flags this event.
            if (!_sawEnemies && !NothingToFight(Map.Instance))
                CoopTrace.Write("boss-room-no-fight", CoopWorld.CurrentRoom(), what: type.ToString());
            Done("the last room of " + type + " is over");
            return;
        }
        if (_bossRoute)
        {
            BossRoute(now, levels);
            return;
        }
        if (_byHand && world.Active)
            return; // the person presses the doors: their press is their vote (CoopDoors), the bots vote too
        if (_nextDoor <= 0f)
            _nextDoor = now + _doorSeconds;
        // Played like a person (Umut 2026-11-13): through the door once the room is cleared and looted, not on the
        // 25 s timer any more; StuckSeconds is the cap for a room that never clears.
        var waves = Map.Instance.waveContainer;
        bool cleared = (waves == null || waves.state == EnemyWaveContainer.State.Empty) && RoomQuiet(now, 4f)
            && now - _lastLoot > 4f && now - _roomSince > 8f;
        if (now < _doorAfter || (!cleared && now - _roomSince < StuckSeconds))
            return;
        var gate =Map.Instance.GetComponentsInChildren<Gate>().FirstOrDefault(g => g.isActiveAndEnabled && g._collider != null && g._collider.enabled);
        if (gate == null)
            return; // a boss room before the boss falls: no door yet
        bool voted = _votedIn == Map.Instance;
        if (voted && now - _votedAt < VoteStuckSeconds)
            return; // the host's bot voted (below): the vote moves the party on
        if (voted)
        {
            // a finding for the report; the route goes on the old way
            Plugin.Log.LogWarning($"[CoopTest] autorun: the door vote did not move the party on in {VoteStuckSeconds:0} s; the host loads the door itself");
            CoopTrace.Write("vote-stuck", CoopWorld.CurrentRoom());
        }
        else
        {
            if (_maxRooms > 0 && _rooms >= _maxRooms)
            {
                Done(_rooms + " rooms");
                return;
            }
            if (world.Active && !gate.activated && now - _roomSince < StuckSeconds)
                return; // the host's doors open once its bot took the room's reward (Loot): no press before that
            _nextDoor = now + _doorSeconds;
            _doorAfter = now + 6f;
            _doorsTaken++;
            if (_skipStages && _skips < 2 && _doorsTaken % 2 == 0)
            {
                _skips++;
                Plugin.Log.LogInfo("[CoopTest] autorun: the host skips to the next stage (like the dev menu)");
                levels.LoadNextStage();
                return;
            }
            if (world.Active && gate.activated)
            {
                // A press on a door is a vote now (CoopDoors): the host's bot votes like a player. A room that never
                // cleared (StuckSeconds) has closed doors: the route leaves it the old way, below.
                _votedIn = Map.Instance;
                _votedAt = now;
                var pick = BotDoor(CoopWorld.CurrentRoom(), true) ?? gate;
                Plugin.Log.LogInfo($"[CoopTest] autorun: the host's bot votes for the door to {pick._nodeIndex}");
                pick.InteractWith(player);
                return;
            }
        }
        Plugin.Log.LogInfo($"[CoopTest] autorun: through the door to {gate._nodeIndex}");
        levels.LoadNextMap(gate._nodeIndex);
    }

    // ------------------------------------------------------------------ the bots open a menu in a fight

    private Map _menuMap;
    private float _menuSince;
    private bool _menuDone, _menuOpen, _unstuckDone;

    /// <summary>
    /// In every fourth room (by the room id, the same rooms on every game), 6 s into it, a bot opens a menu for 3 s:
    /// the host's the pause menu, a client's the inventory. In co-op the game must not stop time for a menu
    /// (COOP_PLAN 4.5, CoopNoPause): the trace's `bot-menu-time` holds the game's time scale while it is open (1 =
    /// running, 0 = stopped), and the room's enemies go on moving in the report.
    /// </summary>
    private void BotMenu(float now)
    {
        var map = Map.Instance;
        if (map == null || _menuOpen)
            return;
        if (map != _menuMap)
        {
            _menuMap = map;
            _menuSince = now;
            _menuDone = _unstuckDone = false;
        }
        if (!_unstuckDone && now - _menuSince >= 9f && CoopWorld.Current?.IsClient == true
            && ((CoopWorld.CurrentRoom() >> 6) & 3) == 2)
        {
            // every fourth room or so, the client's bot uses unstuck (CoopUnstuck, F6): the trace's `unstuck`, or why not
            _unstuckDone = true;
            string why = CoopUnstuck.ForTest();
            if (why != null)
                CoopTrace.Write("unstuck-refused", CoopWorld.CurrentRoom(), what: why);
        }
        if (_menuDone || now - _menuSince < 6f)
            return;
        _menuDone = true;
        uint room = CoopWorld.CurrentRoom();
        if (((room >> 4) & 3) != 1)
            return;
        _host.StartCoroutine(CMenu(room, CoopWorld.Current?.IsHost == true));
    }

    private IEnumerator CMenu(uint room, bool host)
    {
        var ui = Scenes.Scene<Scenes.GameBase>.instance?.uiManager;
        if (ui == null || global::UI.Dialogue.anyDialogueOpened)
            yield break;
        _menuOpen = true;
        string name = host ? "pause" : "inventory";
        global::UI.Dialogue menu = null;
        try
        {
            if (host)
            {
                ui.ShowPausePopup();
                menu = Object.FindObjectOfType<global::UI.Pause.Panel>();
            }
            else
            {
                ui.inventory.Open();
                menu = ui.inventory;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("[CoopTest] autorun: opening the " + name + " failed: " + e.Message);
        }
        CoopTrace.Write("bot-menu", room, what: name + " open");
        yield return new WaitForSecondsRealtime(1.5f);
        CoopTrace.Write("bot-menu-time", room, what: name, amount: Chronometer.global.timeScale);
        yield return new WaitForSecondsRealtime(1.5f);
        try
        {
            if (menu != null && menu.gameObject.activeSelf)
                menu.Close();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("[CoopTest] autorun: closing the " + name + " failed: " + e.Message);
        }
        CoopTrace.Write("bot-menu", room, what: name + " closed");
        _menuOpen = false;
    }

    // ------------------------------------------------------------------ the bots' door votes

    // the longest a vote can take: the reward wait, then the vote's countdown, and a margin
    private static float VoteStuckSeconds => (Plugin.CoopRewardWaitSeconds?.Value ?? 60f) + (Plugin.CoopDoorVoteSeconds?.Value ?? 10f) + 15f;
    private Map _votedIn, _clientRoom;
    private float _votedAt, _clientRoomSince;

    /// <summary>The door a test bot votes for, among the room's open doors: the host's bot reads bit 0 of the room's
    /// id, a client's bot bit 1. So in half of the rooms with two doors they agree, in the other half they differ
    /// and the coin flip decides. Null: no open door (the room is not cleared).</summary>
    private static Gate BotDoor(uint room, bool host)
    {
        var doors = Map.Instance.GetComponentsInChildren<Gate>()
            .Where(g => g.isActiveAndEnabled && g._collider != null && g._collider.enabled && g.activated)
            .OrderBy(g => (int)g._nodeIndex).ToList();
        return doors.Count == 0 ? null : doors[(int)((host ? room : room >> 1) & 1) % doors.Count];
    }

    /// <summary>A client's bot votes once per cleared room, like a player who is done looting. In every fourth room
    /// it does not vote at all: there the host's vote must decide alone when the vote's time is up.</summary>
    private void ClientVote(float now, Characters.Character player)
    {
        var map = Map.Instance;
        if (map != _clientRoom)
        {
            _clientRoom = map;
            _clientRoomSince = now;
        }
        // three seconds after the host's bot (its 8 s / 4 s): the standing vote and its countdown get on a picture
        if (map == null || _votedIn == map || now - _clientRoomSince < 11f || now - _lastLoot < 7f)
            return;
        uint room = CoopWorld.CurrentRoom();
        var pick = BotDoor(room, false);
        if (pick == null)
            return;
        _votedIn = map;
        if (((room >> 2) & 3) == 0)
        {
            Plugin.Log.LogInfo("[CoopTest] autorun: this copy's bot does not vote in this room (the vote's time must run out)");
            CoopTrace.Write("bot-no-vote", room);
            return;
        }
        Plugin.Log.LogInfo($"[CoopTest] autorun: this copy's bot votes for the door to {pick._nodeIndex}");
        pick.InteractWith(player);
    }

    private void Done(string why)
    {
        _done = true;
        Plugin.Log.LogInfo("[CoopTest] autorun: done (" + why + ")");
        CoopTrace.Write("autorun-done", CoopWorld.CurrentRoom(), what: why);
    }

    // ------------------------------------------------------------------ loot

    private const float StuckSeconds = 120f;
    private const int MaxGear = 8; // a full item inventory opens the game's "replace an item" panel: stay below it
    private readonly System.Collections.Generic.HashSet<int> _seen = new(), _tried = new();
    private Map _lootRoom;
    private float _nextLoot, _lastLoot = -100f, _gearAt = -100f, _doorAfter;
    private int _gearTaken;

    private static bool Enemies(Map map) => map.GetComponentsInChildren<Characters.Character>().Any(c =>
        c.health != null && !c.health.dead && (c.@base == null || c.@base.gameObject.activeInHierarchy)
        && c.type is Characters.Character.Type.Boss or Characters.Character.Type.Adventurer
            or Characters.Character.Type.Named or Characters.Character.Type.TrashMob or Characters.Character.Type.Summoned);

    private static bool Gear(InteractiveObject thing) => thing is DroppedGear or DroppedItem or DroppedWeapon;

    private static bool Lootable(InteractiveObject thing) => Gear(thing) || thing is Chest or HardmodeChest or Grave
        or GoldReward or BossChest or AdventurerGearReward or DroppedPurchasableReward or CurrencyBag;

    /// <summary>A first, simple "bot that plays" (Umut 2026-11-13): every copy's player takes what a cleared room
    /// offers, one thing a second and each thing once: chests, graves, gold, dropped gear, a shop's wares (the game
    /// refuses what the gold doesn't pay). From where it stands: InteractWith asks for no distance, so the bot does
    /// not walk there yet. Gear that shows up right after its own pick-up is what the pick-up pushed out (the old
    /// skull): left alone, or two things would be swapped for ever.</summary>
    private void Loot(float now, Characters.Character player)
    {
        var map = Map.Instance;
        if (map == null || now < _nextLoot)
            return;
        _nextLoot = now + 1f;
        if (map != _lootRoom)
        {
            _lootRoom = map;
            _seen.Clear();
            _tried.Clear();
            _fightOverAt = -1f;
        }
        // A room whose waves all still wait (Leiana's sisters before their intro) is not over: the bot's jump to the
        // reward range ended the throne room before the sisters came (COOP_LOG 59i/60).
        if ((map.waveContainer != null && map.waveContainer.state != EnemyWaveContainer.State.Empty) || Enemies(map)
            || !EnemySync.RoomBegun(map))
            return;
        if (_fightOverAt < 0f)
            _fightOverAt = now;
        // The room's reward appears only once a player stands in its range after the fight (Map.CCheckRewardActivating),
        // and the game opens a player's doors only after that reward is taken. A bot goes there once, like a player
        // walking to the chest (2026-11-15: without it most rooms never opened their doors and were no vote).
        if (!_byHand && _rewardRoom != map && map.mapReward != null && !map.mapReward.activated
            && map._mapRewardActivatingRange != null && map.waveContainer != null && map.waveContainer.enemyWaves.Length != 0)
        {
            _rewardRoom = map;
            var at = map._mapRewardActivatingRange.bounds.center;
            player.transform.position = new Vector3(at.x, at.y, player.transform.position.z);
            _lastLoot = now;
            CoopTrace.Write("bot-to-reward", CoopWorld.CurrentRoom(), what: map.mapReward.type.ToString());
            return;
        }
        var things = map.GetComponentsInChildren<InteractiveObject>().Where(t => t.isActiveAndEnabled && Lootable(t)).ToArray();
        foreach (var thing in things)
            if (_seen.Add(thing.GetInstanceID()) && Gear(thing) && now - _gearAt < 2f)
                _tried.Add(thing.GetInstanceID());
        foreach (var thing in things)
        {
            if (!thing.activated || (Gear(thing) && (_gearTaken >= MaxGear || BotItems.Active)) || !_tried.Add(thing.GetInstanceID()))
                continue; // the item sweep (BotItems): the sweep's item is the only one
            if (Gear(thing))
            {
                _gearAt = now;
                _gearTaken++;
            }
            _lastLoot = now;
            Plugin.Log.LogInfo($"[CoopTest] autorun: takes {thing.GetType().Name} '{thing.name}'");
            CoopTrace.Write("autorun-loot", CoopWorld.CurrentRoom(), what: thing.GetType().Name + " " + thing.name);
            try
            {
                thing.InteractWith(player);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[CoopTest] autorun: taking " + thing.name + " failed: " + e.Message);
            }
            return;
        }
    }

    // ------------------------------------------------------------------ rooms

    private Map _room, _leaving; // _leaving: the room a jump of the boss route is leaving (still there while it loads)
    private float _roomSince, _quietSince = -1;
    private Map _rewardRoom; // the room whose reward this copy's bot went to
    private float _fightOverAt = -1f;
    private int _rooms;
    private bool _sawEnemies;

    private void TrackRoom(float now, CoopWorld world)
    {
        var map = Map.Instance;
        if (map == _room || map == null || map == _leaving || !world.RoomLoaded || world.Busy)
            return;
        _leaving = null;
        _room = map;
        _roomSince = now;
        _quietSince = -1;
        _sawEnemies = false;
        _rooms++;
        _nextDoor = now + _doorSeconds;
    }

    /// <summary>Nothing left to fight in this room (no living boss, adventurer or wave enemy) for a while.</summary>
    private bool RoomQuiet(float now, float seconds)
    {
        var map = Map.Instance;
        if (map == null || map != _room)
            return false;
        // hidden ones don't count (Leiana's golden sister that awakened stays alive, hidden)
        bool fighting = map.GetComponentsInChildren<Characters.Character>().Any(c =>
            c.health != null && !c.health.dead && (c.@base == null || c.@base.gameObject.activeInHierarchy)
            && c.type is Characters.Character.Type.Boss or Characters.Character.Type.Adventurer
                or Characters.Character.Type.Named or Characters.Character.Type.TrashMob or Characters.Character.Type.Summoned);
        if (fighting)
        {
            _sawEnemies = true;
            _quietSince = -1;
            return false;
        }
        if (_quietSince < 0)
            _quietSince = now;
        return now - _quietSince >= seconds;
    }

    private static bool HasMap(PathNode node) => node?.reference?.reference != null && !node.reference.empty;

    /// <summary>The last room of a stage's path (the stage's adventurers or boss), or -1. A stage is a generated
    /// path (StageInfo, the Dark Mirror's too) or a fixed list of rooms (chapter 5 and the Dark Mirror's chapters 5
    /// and 6 are CustomStageInfo; 2026-11-11: only the first kind was read, so the boss route stood in the first
    /// room of n5, dm5 and dm6 for 25 minutes).</summary>
    private static int LastRoom(IStageInfo stage)
    {
        switch (stage)
        {
            case StageInfo generated when generated._path != null:
                for (int i = generated._path.Length - 1; i >= 0; i--)
                    if (HasMap(generated._path[i].type1) || HasMap(generated._path[i].type2))
                        return i;
                return -1;
            case CustomStageInfo custom when custom._maps?.values != null:
                for (int i = custom._maps.values.Length - 1; i >= 0; i--)
                    if (HasMap(custom._maps.values[i]))
                        return i;
                return -1;
            case SequentialStageInfo when stage.maps != null:
                return stage.maps.Length - 1;
            default:
                return -1;
        }
    }

    /// <summary>A fixed list of rooms: its bosses sit in rooms of their own anywhere in the list (the First Hero), so
    /// the boss route takes it room by room instead of jumping to the last one.</summary>
    private static bool FixedRooms(IStageInfo stage) => stage != null && stage is not StageInfo;

    /// <summary>The chapter's last room (its boss) is over: fought and quiet for 20 s.</summary>
    private bool LastRoomOver(float now, LevelManager levels)
    {
        var chapter = levels.currentChapter;
        var stage = chapter.currentStage;
        if (stage == null || _room == null || Map.Instance != _room || chapter.stages == null
            || chapter.stageIndex != chapter.stages.Length - 1 || stage.pathIndex != LastRoom(stage))
            return false;
        return (RoomQuiet(now, 20f) && (_sawEnemies || now - _roomSince > (NothingToFight(Map.Instance) ? 15f : 90f)))
            || now - CapSince > RoomCap;
    }

    // ------------------------------------------------------------------ the boss route

    public const float FirstRoomSeconds = 8f, StageEntrySeconds = 4f, KillAllSeconds = 75f, AfterFightSeconds = 12f, RoomCapSeconds = 300f;
    private bool _killedAll, _hasBoss, _reportTried;
    private Map _roomWithBoss;

    /// <summary>A room ends at the latest after this long; a boss room after its bosses' steps (BossStep).</summary>
    private float RoomCap => _hasBoss || _boss != null ? BossRoomSeconds + 60f : RoomCapSeconds;

    /// <summary>What the cap counts from: the room's start, or the start of the boss the steps are for now (each boss
    /// of a room has its own time; 2026-11-11 the First Hero's third form got 43 s of the room's 480).</summary>
    private float CapSince => Mathf.Max(_roomSince, _bossRoomSince);

    private Map _bosslessRoom;
    private float _bosslessSince;

    private void BossRoute(float now, LevelManager levels)
    {
        var chapter = levels.currentChapter;
        var stage = chapter.currentStage;
        int last = LastRoom(stage);
        if (stage == null || last < 0 || _room == null || Map.Instance != _room)
            return; // also while a jump's room is still loading
        float inRoom = now - _roomSince;
        // A boss stage of the Dark Mirror is [boss room, "LandsBetween"]: the boss sits in the stage's FIRST room
        // (2026-11-10: the route jumped past Leiana after 4 s). A room with a boss is fought wherever it is.
        if (_roomWithBoss != _room)
        {
            _roomWithBoss = _room;
            _hasBoss = Map.Instance.GetComponentsInChildren<Characters.Character>(true).Any(c => c.type == Characters.Character.Type.Boss);
        }
        // King Alexander's arenas are out-of-track maps the game's scenes chain by themselves (COOP_LOG 50i: after
        // the last one the route went "to room 3", the giant again, until the timeout). A bossless one that stays
        // for 75 s is the chain's end.
        // (Sixth `dm6r2`: the last arena holds a boss that is switched off, so "bossless" has to mean "no boss
        // shown": the route jumped back to the giant after 57 s.) No jump from such a map either: the game's own
        // scenes move on.
        if (Map.Instance.name.Contains("Out track")
            && !Map.Instance.GetComponentsInChildren<Characters.Character>().Any(c => c.type == Characters.Character.Type.Boss && c.health != null && !c.health.dead))
        {
            if (_bosslessRoom != _room)
            {
                _bosslessRoom = _room;
                _bosslessSince = now;
            }
            else if (now - _bosslessSince > 75f)
                Done("the out-of-track chain is over (" + Map.Instance.name + ")");
            return;
        }
        _bosslessRoom = null;
        // the room after this one: the stage's last, or simply the next of a fixed list
        int next = FixedRooms(stage) ? Mathf.Min(stage.pathIndex + 1, last) : last;
        if (_fromRoom > 0 && _rooms <= 1 && chapter.stageIndex == 0 && stage.pathIndex < _fromRoom)
            next = Mathf.Min(_fromRoom, last); // --skulinsight-autorun-from: straight to the room asked for
        if (stage.pathIndex != last && !_hasBoss)
        {
            // the stage's first room: a moment for the normal start (paths, barrier, first enemies), then the jump
            if (inRoom < (_rooms <= 1 ? FirstRoomSeconds : StageEntrySeconds))
                return;
            if (!_reportTried && System.Environment.GetEnvironmentVariable("SKULINSIGHT_TEST_REPORT") == "1")
            {
                // coop-test.py --report: the bug report zip once, in the game's own runtime (COOP_LOG 64)
                _reportTried = true;
                Crash.BugReport.MakeForTest();
            }
            Plugin.Log.LogInfo($"[CoopTest] autorun: to room {next} of {chapter.type} stage {chapter.stageIndex} (its last: {last})");
            CoopTrace.Write("autorun-room", CoopWorld.CurrentRoom(), what: $"to room {next} of stage {chapter.stageIndex} (last {last})");
            _killedAll = false;
            Leave();
            chapter.Enter(chapter.stageIndex, next, 0);
            return;
        }
        // a fight room: the stage's last one or a boss's (BossStep handles bosses; adventurers and the rest fall after a while)
        if (!_killedAll && !_hasBoss && inRoom > KillAllSeconds)
        {
            _killedAll = true;
            int killed = 0;
            foreach (var c in Map.Instance.GetComponentsInChildren<Characters.Character>())
                if (c.health != null && !c.health.dead && c.type is Characters.Character.Type.Adventurer or Characters.Character.Type.Named
                        or Characters.Character.Type.TrashMob or Characters.Character.Type.Summoned)
                {
                    c.health.Kill();
                    killed++;
                }
            if (killed > 0)
                Plugin.Log.LogInfo($"[CoopTest] autorun: {killed} enemies of the room killed");
        }
        // a boss room: 20 s of quiet (between two bosses of a room nobody may be shown for a while)
        bool over = RoomQuiet(now, _hasBoss ? 20f : AfterFightSeconds) && (_sawEnemies || inRoom > (NothingToFight(Map.Instance) ? 15f : 60f));
        if (!over && now - CapSince < RoomCap)
            return;
        if (stage.pathIndex != last)
        {
            Plugin.Log.LogInfo($"[CoopTest] autorun: the boss room is over, to room {next} of {chapter.type} stage {chapter.stageIndex} (its last: {last})");
            CoopTrace.Write("autorun-room", CoopWorld.CurrentRoom(), what: $"boss room over, to room {next} of stage {chapter.stageIndex} (last {last})");
            _killedAll = false;
            Leave();
            chapter.Enter(chapter.stageIndex, next, 0);
            return;
        }
        if (chapter.stageIndex + 1 >= chapter.stages.Length)
            return; // the chapter's last room: LastRoomOver ends the route
        Plugin.Log.LogInfo($"[CoopTest] autorun: on to {chapter.type} stage {chapter.stageIndex + 1}");
        CoopTrace.Write("autorun-room", CoopWorld.CurrentRoom(), what: $"on to stage {chapter.stageIndex + 1}");
        Leave();
        chapter.Enter(chapter.stageIndex + 1, 0, 0);
    }

    /// <summary>No enemy in the room at all, not even one that waits switched off for its wave or its scene (a stage's
    /// terminal room): no need to wait a minute for a fight that can't come.</summary>
    private static bool NothingToFight(Map map) =>
        map != null && !map.GetComponentsInChildren<Characters.Character>(true).Any(c =>
            c.type is Characters.Character.Type.Boss or Characters.Character.Type.Adventurer or Characters.Character.Type.Named
                or Characters.Character.Type.TrashMob);

    /// <summary>A jump starts: this room is on its way out (it stays loaded for a moment while the next one loads).</summary>
    private void Leave()
    {
        _leaving = Map.Instance;
        _room = null;
    }

    // ------------------------------------------------------------------ bosses

    /// <summary>A boss step is due after <see cref="BossMinSeconds"/> once the boss showed no NEW move for
    /// <see cref="BossQuietSeconds"/>, at the latest after <see cref="BossMaxSeconds"/>; a boss is killed
    /// <see cref="BossRoomSeconds"/> after it came (each boss of a room in turn has that long).</summary>
    public const float BossMinSeconds = 20f, BossQuietSeconds = 25f, BossMaxSeconds = 90f, BossRoomSeconds = 480f, BossWaitSeconds = 10f, BossGuardSeconds = 120f, BombSeconds = 5f;
    public const int MaxBossesPerRoom = 3;

    /// <summary>The boss's health after each step, as a share of its full health: down in steps (attacks that wait for
    /// a health mark), 0 = the next phase (or its end for a boss with one), then the same for a second phase.</summary>
    private static readonly float[] BossHealthSteps = { 0.7f, 0.4f, 0.15f, 0f, 0.5f, 0.15f, 0f };

    private Map _bossMap;
    private float _bossSince, _bossRoomSince, _bossLastNew, _bossBlockedSince;
    private int _bossStep, _bossesDone;
    private bool _bossBegan;
    private Characters.Character _boss;
    private readonly System.Collections.Generic.HashSet<string> _bossMoves = new();
    private Characters.Character.OnStartMotionDelegate _bossWatch;

    /// <summary>
    /// The bots barely hurt a boss, so a boss fight's later parts were never seen in a test; and a boss killed after
    /// a minute showed only a few of its attacks (Umut 2026-11-11: "not kill off every boss immediately, but check
    /// whether all or at least the majority of their attacks are synced"). In a boss room the host keeps the boss
    /// alive and watches its moves: whenever it showed no new move for a while, its health goes down a step
    /// (<see cref="BossHealthSteps"/>: 70 %, 40 %, 15 %, then 0 = the next phase as when a player gets it there, then
    /// the same for the second phase), and at the end it is killed. A boss that comes after the first one (Leiana's
    /// Dark sister after the awakening) gets the same steps, up to <see cref="MaxBossesPerRoom"/> per room (hidden
    /// ones, like the golden sister that awakened, don't count). The report lists each attack seen (sync-report.py).
    /// </summary>
    private void BossStep(float now)
    {
        var map = Map.Instance;
        if (map != _bossMap)
        {
            _bossMap = map;
            _bossSince = now;
            _bossRoomSince = -1f;
            _bossStep = 0;
            _bossesDone = 0;
            WatchBoss(null);
        }
        if (map == null || _bossesDone >= MaxBossesPerRoom)
            return;
        var boss = _boss != null && !_boss.health.dead && _boss.gameObject.activeInHierarchy ? _boss
            : map.GetComponentsInChildren<Characters.Character>().FirstOrDefault(c =>
                c.type == Characters.Character.Type.Boss && c.health != null && !c.health.dead
                && (c.@base == null || c.@base.gameObject.activeInHierarchy));
        if (boss == null)
        {
            _bossSince = now; // the boss isn't there yet (intro): count from when it is
            return;
        }
        if (boss != _boss)
        {
            if (_boss != null)
                _bossesDone++;
            WatchBoss(boss);
            _bossStep = 0;
            _bossBegan = false;
            _bossSince = _bossLastNew = now;
            _bossBlockedSince = -1f;
            _bossRoomSince = now; // each boss of the room has its own time
            if (_bossesDone >= MaxBossesPerRoom)
                return;
        }
        if (_bossRoomSince < 0f)
            _bossRoomSince = now;
        if (_bossStep > BossHealthSteps.Length)
            return; // killed already
        // A timed objective, not a boss to watch: King Alexander's bomb (DarkTech_Bomb) blows up when its time runs out,
        // and it can't be hit while its guards live. 2026-11-16 (dm6) the health steps and the long guard wait kept it
        // alive, it exploded, and the fight stood still on a white screen: now its guards fall and it breaks 5 s after
        // it appears.
        if (boss.name.IndexOf("Bomb", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // The game-over test (package KA): SKULINSIGHT_TEST_BOMB=explode (coop-test.py --bomb-explodes) leaves the
            // bomb alone, so its timer runs out and the run must end on every game.
            if (System.Environment.GetEnvironmentVariable("SKULINSIGHT_TEST_BOMB") == "explode")
                return;
            if (now - _bossRoomSince < BombSeconds)
                return;
            _bossStep = BossHealthSteps.Length + 1;
            KillGuards(map, boss, CoopWorld.CurrentRoom(), CoopTrace.NameOf(boss));
            CoopTrace.Write("autorun-boss", CoopWorld.CurrentRoom(), name: CoopTrace.NameOf(boss), what: "killed (a timed bomb)");
            boss.health.Kill();
            return;
        }
        bool roomOver = now - _bossRoomSince > BossRoomSeconds;
        if (!_bossBegan)
        {
            // The fight begins when somebody in the room can be hit: until then the boss waits in its scene for a
            // player to walk up, and its clock doesn't run (2026-11-11: the Pope's crystals were killed 110 s after
            // the room's start, before its first phase had begun; the second phase then ran with the first's parts).
            // Not during a story scene either (King Alexander's intro, dm6 2026-11-11: he isn't `cinematic`, the
            // clock ran through his intro and his health went down a step every 25 s before he had attacked).
            bool scene = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.letterBox is { } box && box.visible;
            _bossBegan = !scene && (!boss.cinematic.value || map.GetComponentsInChildren<Characters.Character>().Any(c =>
                c != boss && c.health != null && !c.health.dead && !c.cinematic.value
                && (c.type == Characters.Character.Type.TrashMob || c.type == Characters.Character.Type.Named
                    || c.type == Characters.Character.Type.Adventurer || c.type == Characters.Character.Type.Boss)));
            if (!_bossBegan)
            {
                _bossSince = _bossLastNew = now;
                if (!roomOver)
                    return;
            }
            else
                CoopTrace.Write("autorun-boss", CoopWorld.CurrentRoom(), name: CoopTrace.NameOf(boss), what: "fight began");
        }
        float step = now - _bossSince, quiet = now - Mathf.Max(_bossLastNew, _bossSince);
        if (!roomOver && (step < BossMinSeconds || (quiet < BossQuietSeconds && step < BossMaxSeconds)))
            return;
        uint room = CoopWorld.CurrentRoom();
        string name = CoopTrace.NameOf(boss);
        if (roomOver || _bossStep == BossHealthSteps.Length)
        {
            _bossStep = BossHealthSteps.Length + 1;
            Plugin.Log.LogInfo($"[CoopTest] autorun: boss {boss.name} killed ({_bossMoves.Count} different moves seen)");
            CoopTrace.Write("autorun-boss", room, name: name, what: roomOver ? "killed (the room's time is up)" : "killed", amount: _bossMoves.Count);
            boss.health.Kill();
            return;
        }
        float share = BossHealthSteps[_bossStep];
        bool blocked = boss.cinematic.value || boss.invulnerable.value;
        if (share > 0f)
        {
            if (blocked)
            {
                // A boss nobody can hit right now (the Pope behind its barrier) keeps its health AND its step:
                // 2026-11-11 the Pope's phase 1 used up the three health marks, so its phase 2 went from full
                // health straight to 0. Behind its guards for good (bots that can't kill them): they are killed.
                if (_bossBlockedSince < 0f)
                    _bossBlockedSince = now;
                _bossSince = now;
                if (now - _bossBlockedSince < BossGuardSeconds)
                    return;
                _bossBlockedSince = -1f;
                KillGuards(map, boss, room, name);
                return;
            }
            // a health mark
            _bossBlockedSince = -1f;
            _bossStep++;
            _bossSince = now;
            double to = boss.health.maximumHealth * share;
            if (boss.health.currentHealth > to)
            {
                boss.health.SetCurrentHealth(to);
                Plugin.Log.LogInfo($"[CoopTest] autorun: boss {boss.name} health to {share * 100f:0} % ({_bossMoves.Count} different moves seen)");
                CoopTrace.Write("autorun-boss", room, name: name, what: $"health to {share * 100f:0} %", amount: _bossMoves.Count);
            }
            return;
        }
        // the next phase: a boss nobody can hit right now (a move that can't be interrupted, its scene) gets a moment
        if (blocked)
        {
            if (_bossBlockedSince < 0f)
                _bossBlockedSince = now;
            if (now - _bossBlockedSince < BossWaitSeconds)
                return;
        }
        _bossBlockedSince = -1f;
        _bossStep++;
        _bossSince = now;
        if (blocked)
        {
            KillGuards(map, boss, room, name);
            return;
        }
        Plugin.Log.LogInfo($"[CoopTest] autorun: boss {boss.name} health to 0 (next phase; {_bossMoves.Count} different moves seen)");
        CoopTrace.Write("autorun-boss", room, name: name, what: "health to 0", amount: _bossMoves.Count);
        boss.health.SetCurrentHealth(0);
    }

    /// <summary>A boss behind guards (the Pope's phase 1 ends when its two dark crystals fall, not by its own health;
    /// 2026-11-10: its health went to 0 behind the barrier and nothing happened): the others of the room are killed.</summary>
    private void KillGuards(Map map, Characters.Character boss, uint room, string name)
    {
        int guards = 0;
        foreach (var c in map.GetComponentsInChildren<Characters.Character>())
            // not a boss's later parts that wait in the room (King Alexander's giant: 2026-11-13 the test killed its
            // body and hands while his first form couldn't be hit, and the fight's order broke)
            if (c != boss && c.health != null && !c.health.dead && c.type != Characters.Character.Type.Player
                && c.type != Characters.Character.Type.PlayerMinion && c.type != Characters.Character.Type.Trap
                && !EnemySync.IsBossPart(c))
            {
                c.health.Kill();
                guards++;
            }
        Plugin.Log.LogInfo($"[CoopTest] autorun: boss {boss.name} can't be hit, {guards} others of the room killed (next phase; {_bossMoves.Count} different moves seen)");
        CoopTrace.Write("autorun-boss", room, name: name, what: "guards killed", amount: guards);
    }

    private readonly System.Collections.Generic.List<(Characters.Character Part, Characters.Character.OnStartMotionDelegate Watch)> _bossParts = new();

    /// <summary>Counts the different moves of the boss the steps are for (a new one keeps the current step going),
    /// and those of the room's other boss characters: a boss in parts attacks with them (King Alexander's body and
    /// hands; `Emperor_Renewal` itself starts no move, so a step came every 25 s).</summary>
    private void WatchBoss(Characters.Character boss)
    {
        if (_boss != null && _bossWatch != null)
            _boss.onStartMotion -= _bossWatch;
        foreach (var (part, watch) in _bossParts)
            if (part != null)
                part.onStartMotion -= watch;
        _bossParts.Clear();
        _boss = boss;
        _bossWatch = null;
        _bossMoves.Clear();
        if (boss == null)
            return;
        _bossWatch = (motion, speed) =>
        {
            if (motion?.action != null && _bossMoves.Add(RemoteAvatar.PathOf(motion.action.transform, boss.transform)))
                _bossLastNew = Time.unscaledTime;
        };
        boss.onStartMotion += _bossWatch;
        var map = Map.Instance;
        if (map != null)
            foreach (var part in map.GetComponentsInChildren<Characters.Character>(true))
            {
                if (part == boss || part.type != Characters.Character.Type.Boss)
                    continue;
                var its = part;
                Characters.Character.OnStartMotionDelegate watch = (motion, speed) =>
                {
                    if (motion?.action != null && _bossMoves.Add(its.name + ":" + RemoteAvatar.PathOf(motion.action.transform, its.transform)))
                        _bossLastNew = Time.unscaledTime;
                };
                part.onStartMotion += watch;
                _bossParts.Add((part, watch));
            }
        WriteKnownAttacks(boss);
    }

    /// <summary>For the report: the attacks the Codex reads for this boss (the list a player sees in the book), each
    /// with the moves it is made of ("Label=path|path"), one trace line per attack. The report sets what the test saw
    /// against it ("9 of 14 attacks seen").</summary>
    private static void WriteKnownAttacks(Characters.Character boss)
    {
        if (!CoopTrace.On)
            return;
        try
        {
            var graph = DamageInsight.Codex.BossAttacks.Of(boss);
            uint room = CoopWorld.CurrentRoom();
            string name = CoopTrace.NameOf(boss);
            foreach (var attack in DamageInsight.Codex.BossAttacks.Shown(graph))
            {
                var paths = attack.Steps.OfType<Characters.Actions.Action>()
                    .Select(a => RemoteAvatar.PathOf(a.transform, boss.transform)).Distinct().ToList();
                if (paths.Count > 0)
                    CoopTrace.Write("boss-attack", room, name: name, what: attack.Label + "=" + string.Join("|", paths));
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[CoopTest] autorun: the attack list of {boss.name} could not be read: {e.Message}");
        }
    }

    private IEnumerator CLeaveCastle(LevelManager levels, Chapter.Type chapter)
    {
        _starting = true;
        _runs++;
        Plugin.Log.LogInfo("[CoopTest] autorun: the host leaves the castle" + (_runs > 1 ? " again (run " + _runs + ")" : ""));
        if (_runs > 1)
            CoopTrace.Write("autorun-room", CoopWorld.CurrentRoom(), what: "back in the castle, run " + _runs + " starts");
        yield return Singleton<Service>.Instance.fadeInOut.CFadeOut();
        if (_dmLevel >= 0 && chapter >= Chapter.Type.HardmodeChapter1 && chapter <= Chapter.Type.HardmodeChapter6)
        {
            // a test copy's isolated save: the level the test asked for (the clients borrow it at the run's start)
            Data.GameData.HardmodeProgress.hardmodeLevel = _dmLevel;
            CoopTrace.Write("autorun-dm-level", what: _dmLevel.ToString());
        }
        levels.Load(chapter);
        _starting = false;
        _castleSince = -1;
    }
}
