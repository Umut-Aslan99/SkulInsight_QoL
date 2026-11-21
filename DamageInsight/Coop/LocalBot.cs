using System.Linq;
using Characters.Controllers;
using Services;
using Singletons;
using UnityEngine;
using GameAction = Characters.Actions.Action;

namespace DamageInsight.Coop;

/// <summary>
/// Test copies started with `--skulinsight-bot` (docs/coop/GAME_FLOW.md §1.5): walks this game's own player left and
/// right, jumps, attacks, dashes and uses skills now and then, without keys (a copy's window has no focus). Waits while
/// the game blocks input (loading, cutscenes). It plays for the other copy to watch; it never opens or buys anything.
/// With an enemy in the room it walks to the nearest one that can be hit and attacks it (2026-11-10: the wandering
/// bot hit an enemy only by chance, so a test said little about hits, flinches and statuses); without one it wanders.
/// </summary>
internal sealed partial class LocalBot
{
    private readonly System.Random _random;
    private int _direction = 1;
    private float _nextTurn, _nextJump, _nextMove, _nextLook;
    private Characters.Character _prey;

    private readonly string _skulls;

    /// <param name="skulls">The bot's two skulls ("Name,Name" or "random"); null: the plain skull.</param>
    public LocalBot(int seed, string skulls = null)
    {
        // the skull pick differs from run to run (it is written to the trace), the walk stays the copy's own
        _random = new System.Random(seed);
        _kitRandom = new System.Random(seed * 7919 + System.Environment.TickCount);
        _skulls = skulls;
        _full = Testing.FullSweepPlan.Parse(skulls);
        // each copy's bot starts its status round elsewhere: the two gave the same status at the same moment, so a
        // status of one player alone was never seen
        _statusIndex = (seed & 1) * 3;
        Active = true;
    }

    public void Tick()
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        if (player == null || player.movement == null || PlayerInput.blocked.value)
            return;
        float now = Time.unscaledTime;
        Kit(player, now);
        if (Fly(player, now))
            return;
        if (Cannon(now) || Holding(now))
            return;
        if (_full != null)
        {
            // the full skull sweep: one game acts at a time (LocalBotFullSweep.cs)
            FullSweep(player, now);
            return;
        }
        var prey = Prey(player, now);
        Reach(player, prey, now);
        bool near = false, walk = true;
        if (prey != null)
        {
            float dx = prey.transform.position.x - player.transform.position.x;
            near = Mathf.Abs(dx) < 3f;
            if (Mathf.Abs(dx) > 1.2f)
                _direction = dx > 0 ? 1 : -1;
            else
            {
                // next to it: stand and face it
                walk = false;
                player.ForceToLookAt(dx >= 0 ? Characters.Character.LookingDirection.Right : Characters.Character.LookingDirection.Left);
            }
        }
        else if (now >= _nextTurn)
        {
            _direction = -_direction;
            _nextTurn = now + Between(1.5f, 3.5f);
        }
        if (walk)
            player.movement.MoveHorizontal(_direction > 0 ? Vector2.right : Vector2.left);
        if (now >= _nextJump)
        {
            _nextJump = now + Between(1f, 2.5f);
            // towards an enemy above; else now and then
            if (prey == null || prey.transform.position.y > player.transform.position.y + 1.5f || _random.NextDouble() < 0.3)
                Try(player, GameAction.Type.Jump);
        }
        // Moves for the other copy to replay (phase 1): attacks often, a dash or a skill now and then.
        if (now >= _nextMove)
        {
            _nextMove = now + (near ? Between(0.25f, 0.6f) : Between(0.6f, 1.4f));
            double roll = _random.NextDouble();
            if (near)
                Try(player, roll < 0.8 ? GameAction.Type.BasicAttack : GameAction.Type.Skill);
            else
                Try(player, roll < 0.6 ? GameAction.Type.BasicAttack : roll < 0.8 ? GameAction.Type.Dash : GameAction.Type.Skill);
        }
        if (near && now >= _nextStatus)
        {
            _nextStatus = now + Between(2.5f, 4f);
            GiveStatus(player, prey);
        }
    }

    private static readonly Characters.CharacterStatus.Kind[] StatusRound =
    {
        Characters.CharacterStatus.Kind.Burn, Characters.CharacterStatus.Kind.Poison, Characters.CharacterStatus.Kind.Wound,
        Characters.CharacterStatus.Kind.Wound, // the second wound sets off the bleeding
        Characters.CharacterStatus.Kind.Stun, Characters.CharacterStatus.Kind.Freeze,
    };

    private float _nextStatus;
    private int _statusIndex;

    /// <summary>The plain skull gives no statuses, so a test said nothing about them: next to its enemy the bot gives it
    /// one every few seconds as its player would by a hit (burn, poison, wound twice, stun, freeze in turn).</summary>
    private void GiveStatus(Characters.Character player, Characters.Character prey)
    {
        if (prey == null || prey.status == null || prey.cinematic.value || prey.invulnerable.value)
            return;
        var kind = StatusRound[_statusIndex++ % StatusRound.Length];
        bool given = player.GiveStatus(prey, new Characters.CharacterStatus.ApplyInfo(kind));
        CoopTrace.Write(given ? "bot-status" : "bot-status-refused", EnemySync.TraceRoom, EnemySync.TraceIdOf(prey), CoopTrace.NameOf(prey), kind.ToString());
    }

    /// <summary>The nearest enemy that can be hit, else the nearest one that can't yet (a boss before its scene: fights
    /// start when a player walks up to it); looked up twice a second. Null: nobody.</summary>
    private Characters.Character Prey(Characters.Character player, float now)
    {
        bool alive = _prey != null && _prey.health != null && !_prey.health.dead
            && (_preyHidden || _prey.gameObject.activeInHierarchy);
        if (alive && now < _nextLook)
            return _prey;
        _preyHidden = false;
        _nextLook = now + 0.5f;
        _prey = null;
        var map = Level.Map.Instance;
        if (map == null)
            return null;
        Vector2 at = player.transform.position;
        float best = float.MaxValue;
        Characters.Character waiting = null;
        double waitingRank = -1;
        bool waitingHidden = false;
        foreach (var c in map.GetComponentsInChildren<Characters.Character>(true))
        {
            if (c.health == null || c.health.dead)
                continue;
            if (c.type != Characters.Character.Type.TrashMob && c.type != Characters.Character.Type.Named
                && c.type != Characters.Character.Type.Adventurer && c.type != Characters.Character.Type.Boss)
                continue;
            Vector2 p = c.transform.position;
            float distance = Mathf.Abs(p.x - at.x) + 2f * Mathf.Abs(p.y - at.y);
            bool boss = c.type == Characters.Character.Type.Boss;
            if (!c.gameObject.activeInHierarchy)
            {
                // A boss that isn't there yet (the First Hero in chapter 5's first room appears when a player
                // walks up: 2026-11-11 the bot paced at the entrance for a minute and the route went on without
                // the fight): its spot is the goal when nobody else is.
                if (boss && waitingRank < 0)
                {
                    waiting = c;
                    waitingHidden = true;
                }
                continue;
            }
            if (c.cinematic.value)
            {
                // Can't be hit yet: only when nobody can be, and then the boss itself (the one with the most health),
                // however far. 2026-11-11: the bot went to the nearest of the Pope's dark crystals, never walked to
                // the Pope in the middle, phase 1 never began and the fight ran out of order.
                double rank = (boss ? 1e9 : 0) + c.health.maximumHealth;
                if ((boss || distance <= 60f) && rank > waitingRank)
                {
                    waitingRank = rank;
                    waiting = c;
                    waitingHidden = false;
                }
                continue;
            }
            if (distance > 60f)
                continue;
            if (distance < best)
            {
                best = distance;
                _prey = c;
            }
        }
        if (_prey == null)
        {
            _prey = waiting;
            _preyHidden = waiting != null && waitingHidden;
        }
        return _prey;
    }

    // ------------------------------------------------------------------ the bot's skulls (swap, gauge, transformations)

    private readonly System.Random _kitRandom;
    private int _kitState; // 0 not yet, 1 loading, 2 equipped (or nothing to equip)
    private readonly System.Collections.Generic.List<GameResources.WeaponRequest> _kitRequests = new();
    private float _nextSwap, _nextGauge;

    /// <summary>
    /// With `--skulinsight-bot-skulls` the bot plays two skulls instead of the plain one (Umut 2026-11-11: "different
    /// skulls and at times use the swap abilities / different abilities or transforms"): equipped once the run has
    /// started, swapped every 9-16 s (the swap move of each), and the skull's gauge is filled every 12-20 s, which
    /// sets off what a full gauge does (a transformation, a stronger skill) without minutes of fighting for it.
    /// </summary>
    private void Kit(Characters.Character player, float now)
    {
        var inventory = player.playerComponents?.inventory?.weapon;
        if (_skulls == null || inventory == null)
            return;
        if (_kitState == 0)
        {
            var levels = Singleton<Service>.Instance?.levelManager;
            var resource = GameResources.GearResource.instance;
            if (levels?.currentChapter == null || levels.currentChapter.type == Level.Chapter.Type.Castle
                || Level.Map.Instance == null || resource?.weapons == null)
                return;
            _kitState = 1;
            foreach (string name in PickSkulls(resource))
            {
                var reference = resource.weapons.FirstOrDefault(r => r != null && r.name == name);
                if (reference == null)
                    Plugin.Log.LogWarning($"[CoopTest] bot: no skull named {name} in this game");
                else
                    _kitRequests.Add(reference.LoadAsync());
            }
            return;
        }
        if (_kitState == 1)
        {
            if (_kitRequests.Any(r => !r.isDone))
                return;
            _kitState = 2;
            var names = new System.Collections.Generic.List<string>();
            for (int slot = 0; slot < _kitRequests.Count && slot < inventory.weapons.Length; slot++)
            {
                var weapon = Singleton<Service>.Instance.levelManager.DropWeapon(_kitRequests[slot], player.transform.position);
                if (weapon == null)
                    continue;
                inventory.ForceEquipAt(weapon, slot);
                names.Add(Loadout.GearName(weapon.name));
            }
            _kitRequests.Clear();
            _nextSwap = now + Between(6f, 10f);
            _nextGauge = now + Between(8f, 12f);
            _pairTime = 0f;
            _nextSkills = now + 2f;
            _skillRounds.Clear();
            _skillsToUse.Clear();
            Plugin.Log.LogInfo("[CoopTest] bot skulls: " + string.Join(", ", names)
                + (_playlist != null ? $" (sweep: pair {_playIndex / 2 + 1} of {(_playlist.Count + 1) / 2})" : ""));
            CoopTrace.Write("bot-skulls", EnemySync.TraceRoom, what: string.Join(",", names), amount: _playlist != null ? _playIndex / 2 : 0);
            return;
        }
        if (_full != null)
            return; // the full sweep swaps and fills the gauge in its turns (FullSweep), not on these timers
        if (_playlist != null)
        {
            Skills(player, inventory, now);
            // the next pair of the sweep (a pair's time counts only while the bot plays: not in loading or scenes)
            _pairTime += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_pairTime >= PairSeconds && _playIndex + 2 < _playlist.Count
                && Level.Map.Instance != null && Singleton<Service>.Instance?.levelManager?.currentChapter?.type != Level.Chapter.Type.Castle)
            {
                _playIndex += 2;
                _pairTime = 0f;
                if (inventory.polymorphWeapon != null)
                    inventory.Unpolymorph();
                player.CancelAction();
                _kitState = 0;
                return;
            }
            if (_pairTime >= PairSeconds && !_sweepDone)
            {
                _sweepDone = true;
                Plugin.Log.LogInfo("[CoopTest] bot: the skull sweep is through its list");
                CoopTrace.Write("bot-sweep-done", EnemySync.TraceRoom, amount: _playlist.Count);
            }
        }
        if (now >= _nextSwap)
        {
            _nextSwap = now + Between(9f, 16f);
            if (inventory.weapons.Count(w => w != null) > 1 && inventory.NextWeapon())
                CoopTrace.Write("bot-swap", EnemySync.TraceRoom, what: Loadout.GearName(inventory.current.name));
        }
        if (now >= _nextGauge)
        {
            _nextGauge = now + Between(12f, 20f);
            var weapon = inventory.polymorphOrCurrent;
            if (weapon != null && weapon.gauge is Characters.Gear.Weapons.Gauges.ValueGauge gauge && !gauge.isMax())
            {
                gauge.FillUp();
                CoopTrace.Write("bot-gauge", EnemySync.TraceRoom, what: Loadout.GearName(weapon.name));
            }
        }
    }

    // ------------------------------------------------------------------ the skull sweep (every skull, every skill)

    /// <summary>The sweep's list (null: no sweep), where it stands, and how long the current pair was played.</summary>
    private System.Collections.Generic.List<string> _playlist;
    private int _playIndex;
    private float _pairTime, _nextSkills;
    private bool _sweepDone;
    /// <summary>A pair of skulls is played this long (seconds of play, not of loading).</summary>
    public const float PairSeconds = 70f;
    private readonly System.Collections.Generic.Dictionary<Characters.Gear.Weapons.Weapon, int> _skillRounds = new();
    private readonly System.Collections.Generic.List<Characters.Gear.Weapons.SkillInfo> _skillsToUse = new();

    /// <summary>
    /// Umut 2026-11-11: "test each ability + transformation". A skull rolls one or two of its skills; in a sweep the
    /// skull on stage gets the next ones of its list every few seconds (Weapon.SetSkills), and the bot uses each
    /// once before the round goes on (trace "bot-skill"). The gauge is filled as always (transformations).
    /// </summary>
    private void Skills(Characters.Character player, Characters.Player.WeaponInventory inventory, float now)
    {
        var weapon = inventory.current;
        if (weapon == null || inventory.polymorphWeapon != null || weapon.skills == null || weapon.currentSkills == null)
            return;
        for (int i = _skillsToUse.Count - 1; i >= 0; i--)
        {
            var skill = _skillsToUse[i];
            if (skill == null || !weapon.currentSkills.Contains(skill))
                _skillsToUse.RemoveAt(i);
            else if (skill.action != null && skill.action.gameObject.activeInHierarchy && skill.action.TryStart())
            {
                _skillsToUse.RemoveAt(i);
                CoopTrace.Write("bot-skill", EnemySync.TraceRoom, name: Loadout.GearName(weapon.name), what: skill.key);
                return; // one move at a time
            }
        }
        // the next skills: when these were used (and a moment passed), or they can't be used here at all
        if (now < _nextSkills || (_skillsToUse.Count > 0 && now < _nextSkills + 6f))
            return;
        _nextSkills = now + 5f;
        var pool = weapon.skills.Where(k => k != null && !k.hasAlways).ToList();
        int slots = weapon.currentSkills.Count(k => k != null && !k.hasAlways);
        if (pool.Count == 0 || slots == 0)
            return;
        _skillRounds.TryGetValue(weapon, out int round);
        _skillRounds[weapon] = round + 1;
        var keys = new string[slots];
        for (int i = 0; i < slots; i++)
            keys[i] = pool[(round * slots + i) % pool.Count].key;
        if (pool.Count > slots)
        {
            weapon.UnapplyAllSkillChanges();
            weapon.SetSkills(keys, false);
            weapon.ApplyAllSkillChanges();
        }
        _skillsToUse.Clear();
        _skillsToUse.AddRange(weapon.currentSkills.Where(k => k != null));
    }

    private System.Collections.Generic.List<string> PickSkulls(GameResources.GearResource resource)
    {
        if (_full != null)
        {
            if (_playlist == null)
            {
                // the full sweep: sweepall's list (every droppable skull and its awakened forms) or a retry's names
                var all = SweepNames(resource, true);
                _playlist = _full.Playlist(all);
                _playIndex = System.Math.Min(_full.FirstPair * 2, System.Math.Max(0, _playlist.Count - 2));
                Plugin.Log.LogInfo($"[CoopTest] bot: full skull sweep, half {_full.Half}: {_playlist.Count} of {all.Count} skulls, "
                                   + $"from pair {_playIndex / 2 + 1}, turns of {_full.SlotSeconds:0} s");
                CoopTrace.Write("bot-fullsweep", EnemySync.TraceRoom, what: string.Join(",", _playlist), amount: _playIndex / 2);
            }
            return _playlist.Skip(_playIndex).Take(2).ToList();
        }
        var names = _skulls.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        if (names.Count > 0 && names[0].StartsWith("sweep", System.StringComparison.OrdinalIgnoreCase))
        {
            if (_playlist == null)
            {
                // "sweep:<half>:<first pair>": every skull a player can get, in name order; this bot plays every
                // second one (half 0 or 1: the two games' lists don't overlap), two at a time
                var parts = names[0].Split(':');
                int half = parts.Length > 1 && int.TryParse(parts[1], out int h) ? h & 1 : 0;
                int first = parts.Length > 2 && int.TryParse(parts[2], out int f) ? System.Math.Max(0, f) : 0;
                var all = SweepNames(resource, parts[0].Equals("sweepall", System.StringComparison.OrdinalIgnoreCase));
                _playlist = all.Where((n, i) => i % 2 == half).ToList();
                _playIndex = System.Math.Min(first * 2, System.Math.Max(0, _playlist.Count - 2));
                Plugin.Log.LogInfo($"[CoopTest] bot: skull sweep, half {half}: {_playlist.Count} of {all.Count} skulls, from pair {_playIndex / 2 + 1}");
                CoopTrace.Write("bot-sweep", EnemySync.TraceRoom, what: string.Join(",", _playlist), amount: _playIndex / 2);
            }
            return _playlist.Skip(_playIndex).Take(2).ToList();
        }
        if (names.Count > 0 && !names[0].Equals("random", System.StringComparison.OrdinalIgnoreCase))
            return names.Take(2).ToList();
        // any two skulls a player can get (their upgraded forms too)
        var pool = resource.weapons.Where(r => r != null && r.obtainable && !string.IsNullOrEmpty(r.name)).Select(r => r.name).ToList();
        names.Clear();
        while (names.Count < 2 && pool.Count > 0)
        {
            int pick = _kitRandom.Next(pool.Count);
            names.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
        return names;
    }

    /// <summary>Every skull a player can get, in name order; with <paramref name="forms"/> their awakened forms too
    /// ("sweepall", Umut's sweep spec point 4, "every skull in EVERY rarity": the game names them after the droppable
    /// skull with _2, _3, _4, not droppable themselves).</summary>
    private static System.Collections.Generic.List<string> SweepNames(GameResources.GearResource resource, bool forms)
    {
        var droppable = resource.weapons.Where(r => r != null && r.obtainable && !string.IsNullOrEmpty(r.name))
            .Select(r => r.name).Distinct().ToList();
        var all = droppable.ToList();
        if (forms)
        {
            var bases = new System.Collections.Generic.HashSet<string>(droppable, System.StringComparer.Ordinal);
            all.AddRange(resource.weapons.Where(r => r != null && !string.IsNullOrEmpty(r.name) && r.name.Length > 2
                    && r.name[r.name.Length - 2] == '_' && r.name[r.name.Length - 1] is >= '2' and <= '4'
                    && bases.Contains(r.name.Substring(0, r.name.Length - 2)))
                .Select(r => r.name));
        }
        return all.Distinct().OrderBy(n => n, System.StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ an enemy the bot can't walk to

    private float _farSince = -1f;
    /// <summary>The goal is a boss that isn't shown yet (its spot).</summary>
    private bool _preyHidden;

    /// <summary>The bot only walks and jumps: an enemy on a platform far above (the Pope sits 26 units over the room's
    /// entrance, so its fight never began: 2026-11-11) stayed out of reach. After 6 s of that the bot's player flies
    /// next to it (<see cref="Fly"/>) and lands on the ground there.</summary>
    private void Reach(Characters.Character player, Characters.Character prey, float now)
    {
        if (prey == null)
        {
            _farSince = -1f;
            return;
        }
        Vector2 to = prey.transform.position - player.transform.position;
        if (Mathf.Abs(to.y) < 4f && Mathf.Abs(to.x) < 30f)
        {
            _farSince = -1f;
            return;
        }
        if (_farSince < 0f)
        {
            _farSince = now;
            return;
        }
        if (now - _farSince < 6f)
            return;
        _farSince = -1f;
        if (player.movement?.controller == null)
            return;
        _flyTo = (Vector2)prey.transform.position + new Vector2(_direction > 0 ? -1.5f : 1.5f, 1f);
        _flyPrey = prey;
        _flySince = now;
    }

    // ------------------------------------------------------------------ the chapter 4 cannon (coop-test.py --cannon)

    private static readonly bool CannonTest = System.Environment.GetEnvironmentVariable("SKULINSIGHT_TEST_CANNON") == "1";
    private readonly System.Collections.Generic.HashSet<Level.Traps.PlatformInvoker> _cannonsUsed = new();
    private Level.Map _cannonMap;
    private float _cannonMapSince, _nextCannonLook;

    /// <summary>
    /// The Norman test's lock (COOP_LOG 64): the chapter 4 cannon grabbed this player and the teammate's copy together.
    /// With SKULINSIGHT_TEST_CANNON=1 both bots fly onto each cannon's platform 4 s after their room began (so the
    /// player and the copy stand in its Range on both games), once per cannon. True when a flight began.
    /// </summary>
    private bool Cannon(float now)
    {
        if (!CannonTest || now < _nextCannonLook || Level.Map.Instance == null)
            return false;
        _nextCannonLook = now + 0.5f;
        if (Level.Map.Instance != _cannonMap)
        {
            _cannonMap = Level.Map.Instance;
            _cannonMapSince = now;
        }
        if (now - _cannonMapSince < 4f)
            return false;
        foreach (var invoker in _cannonMap.GetComponentsInChildren<Level.Traps.PlatformInvoker>())
        {
            var holder = invoker != null ? invoker.transform.parent : null;
            if (holder == null || !holder.name.StartsWith("CannonJump") || _cannonsUsed.Contains(invoker) || invoker._platformCollider == null)
                continue;
            _cannonsUsed.Add(invoker);
            var platform = invoker._platformCollider.bounds;
            // The host's bot lands ON the platform (its cannon fires); the client's bot stands next to it inside the
            // cannon's Range (its copy is in the Range on the host's game when the cannon grabs: the Norman case).
            float x = platform.center.x;
            var range = holder.Find("Range")?.GetComponent<Collider2D>();
            bool beside = CoopWorld.Current?.IsHost == false && range != null;
            if (beside)
            {
                var r = range.bounds;
                if (platform.min.x - 0.4f > r.min.x + 0.3f)
                    x = Mathf.Max(r.min.x + 0.3f, platform.min.x - 1f);
                else if (platform.max.x + 0.4f < r.max.x - 0.3f)
                    x = Mathf.Min(r.max.x - 0.3f, platform.max.x + 1f);
                else
                    beside = false; // the Range is no wider than the platform: on it as well
            }
            _flyTo = new Vector2(x, platform.max.y + 0.5f);
            _flyPrey = null;
            _flySince = now;
            _holdUntil = now + FlySeconds + 4f; // after landing: stand still, so the grab finds this player there
            CoopTrace.Write("bot-cannon", CoopWorld.CurrentRoom(), name: holder.name,
                what: _cannonMap.name + (beside ? ", beside the platform" : ", on the platform"));
            return true;
        }
        return false;
    }

    private float _holdUntil;

    /// <summary>A cannon visit: the bot stands still once it landed (until <see cref="_holdUntil"/>).</summary>
    private bool Holding(float now) => CannonTest && now < _holdUntil && _flyTo == null;

    private Vector2? _flyTo;
    private Characters.Character _flyPrey;
    private float _flySince;
    private const float FlySpeed = 18f, FlySeconds = 10f;

    /// <summary>The way to an enemy out of reach, in small steps: up (or down) first, then across, as a player climbs
    /// there. One jump skipped the zones a walk passes (2026-11-11, the Pope's room: the camera stayed at the room's
    /// entrance and the fight's first phase never began). True while the bot flies (it does nothing else then).</summary>
    private bool Fly(Characters.Character player, float now)
    {
        if (_flyTo == null)
            return false;
        var controller = player.movement?.controller;
        Vector2 at = player.transform.position, to = _flyTo.Value;
        bool arrived = (to - at).sqrMagnitude < 0.01f;
        if (controller == null || arrived || now - _flySince > FlySeconds)
        {
            _flyTo = null;
            bool moved = controller != null && (controller.TeleportUponGround(to, 8f) || controller.Teleport(to) || arrived);
            player.movement.verticalVelocity = 0f;
            CoopTrace.Write(moved ? "bot-reach" : "bot-reach-failed", EnemySync.TraceRoom, EnemySync.TraceIdOf(_flyPrey), CoopTrace.NameOf(_flyPrey));
            _flyPrey = null;
            return false;
        }
        float step = FlySpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        Vector2 next = Mathf.Abs(to.y - at.y) > 0.01f
            ? new Vector2(at.x, Mathf.MoveTowards(at.y, to.y, step))
            : new Vector2(Mathf.MoveTowards(at.x, to.x, step), to.y);
        // straight through walls and floors (the controller's own teleport refuses a spot inside terrain)
        player.transform.position = new Vector3(next.x, next.y, player.transform.position.z);
        player.movement.verticalVelocity = 0f;
        return true;
    }

    /// <summary>A bot plays this copy (it presses no keys).</summary>
    internal static bool Active { get; private set; }

    /// <summary>
    /// A conversation box waits for a key (attack, jump, submit or cancel). A bot presses none: the host's copy sat in
    /// a veteran adventurer's intro talk for 72 s (2026-11-11, chapter 4). In a bot's copy each line goes on by itself
    /// after 1.5 s. (That the other game doesn't see the talk at all is the real finding: T.md.)
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(global::UI.NpcConversation), nameof(global::UI.NpcConversation.CWaitInput))]
    public static class BotTalks
    {
        private static void Postfix(ref System.Collections.IEnumerator __result)
        {
            if (Active)
                __result = Wait();
        }

        private static System.Collections.IEnumerator Wait()
        {
            float until = Time.unscaledTime + 1.5f;
            while (Time.unscaledTime < until)
                yield return null;
        }
    }

    private static void Try(Characters.Character player, GameAction.Type type)
    {
        // A skill: only the skull's own two skills. Skul's hidden "DownButNotOut" (the knocked-down pose of a story
        // scene) is typed Skill with no cooldown, so the bot played it whenever the real skills were cooling down
        // (Umut 2026-11-21: "the basic skul keeps falling into its death animation"; 82 x in one run).
        var skills = type == GameAction.Type.Skill
            ? player.playerComponents?.inventory?.weapon?.polymorphOrCurrent?.currentSkills
            : null;
        foreach (var action in player.actions)
        {
            if (action == null || action.type != type || !action.gameObject.activeInHierarchy)
                continue;
            if (skills != null && !skills.Exists(s => s != null && s.action == action))
                continue;
            if (action.TryStart())
                return;
        }
    }

    private float Between(float low, float high) => low + (float)_random.NextDouble() * (high - low);
}
