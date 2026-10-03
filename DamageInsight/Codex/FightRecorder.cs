using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Characters;
using DamageInsight.Recording;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DamageInsight.Codex;

/// <summary>
/// Films boss attacks as they really happen, effects, projectiles and platforms included, for the Codex.
/// During a boss fight a second camera (a copy of the game camera, without the HUD) renders a small picture about
/// 10 times a second; the GPU sends it back asynchronously. Frames are grouped by the attack the boss is performing:
/// for sprite bosses the attack coroutine of its AI, from start to finish (<see cref="AttackGraph"/>, followed by
/// <see cref="Patches.AttackTracker"/>); for Spine bosses its animation ("FistSlam_Intro" + "FistSlam" + "FistSlam_Outro"
/// = "Fist slam"). Every move is saved once, on a background thread, to Codex/Replays/&lt;key&gt;/ in the same format as
/// the posed animations. Several bosses at once (the Leiana sisters) are filmed with the same pictures.
/// </summary>
public sealed class FightRecorder : MonoBehaviour
{
    private const int Width = 384, Height = 216, MaxFrames = 180; // 180 = one full 4096 sheet (10 x 18)
    private const float Interval = 0.1f;
    private const float StepGap = 0.4f;     // a pause this short between two steps doesn't end a take
    private const float TailWindow = 2f;    // how long a finished attack waits for its tail (the sisters' escape)
    private const int IdleTailFrames = 12;  // idle kept at the end of a take (effects fading), 1.2 s

    /// <summary>One boss being filmed (the Leiana sisters are two at once).</summary>
    private sealed class Film
    {
        public Character Boss;
        public string Key;
        public HashSet<string> Recorded;
        public AttackGraph Graph;                   // null: moves are told apart by animation (Spine bosses)
        public string Segment;                      // label of the move being filmed
        public AttackGraph.Attack Attack;
        public object Root, Pattern;                // the attack's coroutine; the dispatcher pattern it runs in
        public bool Ended;
        public float EndedAt, LastMove;
        public readonly List<Color32[]> Frames = new();
        public readonly List<float> Times = new();
        public readonly List<bool> Busy = new();    // per frame: was the boss doing something (not idling)?
        public bool Pending;                        // its behaviour tree isn't loaded yet: wait before filming
        public float NextTry, GiveUpAt;
        public bool Dark;                           // a Dark Mirror fight
        public List<string> Moves = new();          // every move it has (for the move counter), once known
        public HashSet<string> Seen = new();        // moves seen in this mode, this fight and earlier ones
        public readonly HashSet<string> Done = new(); // moves started in this fight (Developer/PreferNewMoves)
        private readonly Dictionary<string, (int tries, float at)> _steered = new(); // PreferNewMoves tries per move

        /// <summary>
        /// Whether steering towards a move still missing (unseen or unfilmed) is worth it: 3 tries, then a 15 s pause.
        /// A move locked behind an HP range, or one only ever filmed inside a bigger move, must not pull forever.
        /// </summary>
        public bool Worth(string move) =>
            !_steered.TryGetValue(move, out var t) || t.tries < 3 || Time.unscaledTime - t.at > 15f;

        public void Steered(string move)
        {
            _steered.TryGetValue(move, out var t);
            if (Time.unscaledTime - t.at > 15f)
                t.tries = 0;
            _steered[move] = (t.tries + 1, Time.unscaledTime);
        }

        public bool Missing(string move) => !Seen.Contains(move) || !Recorded.Contains(move);
        private Dictionary<object, AttackGraph.Unit> _unitOf;
        private readonly Dictionary<AttackGraph.Unit, List<string>> _below = new();

        /// <summary>The moves a node of the boss's tree can lead to (null: not a node of this boss).</summary>
        public List<string> MovesBelow(object node)
        {
            if (Graph == null)
                return null;
            _unitOf ??= Graph.Units.Where(u => u.Owner.Value != null).GroupBy(u => u.Owner.Value).ToDictionary(g => g.Key, g => g.First());
            if (!_unitOf.TryGetValue(node, out var unit))
                return null;
            if (!_below.TryGetValue(unit, out var moves))
                _below[unit] = moves = Graph.Attacks
                    .Where(a => !a.IsTail && Moves.Contains(a.Label) && a.Units.Any(u => u == unit || AttackGraph.Reaches(unit, u)))
                    .Select(a => a.Label).Distinct().ToList();
            return moves;
        }

        /// <summary>Notes that the boss did this move (kept in the progress, per mode).</summary>
        public void Saw(string label)
        {
            if (label != null && Seen.Add(label))
                CodexTracker.MarkMoveSeen(Boss, Dark, label);
        }

        /// <summary>
        /// Whether a step of the attack being filmed still runs: an action (attacks that start an action and move on),
        /// or, for bosses that play animations directly (Yggdrasil, Chimera), one of its animations on the skeleton.
        /// </summary>
        public bool StepRunning()
        {
            if (Attack == null)
                return false;
            foreach (var step in Attack.Steps)
                if (step is Characters.Actions.Action action && action != null && action.running)
                    return true;
            // Animations the boss plays itself finish inside its attack coroutine, so they only count while that runs
            // (Chimera leaves its last clip set after the move; an action can outlast its coroutine, an animation not).
            return !Ended && AnimationRunning();
        }

        private Spine.Unity.IAnimationStateComponent[] _skeletons;
        private CharacterAnimation[] _bodies;
        private HashSet<string> _animationNames;
        private AttackGraph.Attack _namesOf;

        private bool AnimationRunning()
        {
            if (!ReferenceEquals(_namesOf, Attack))
            {
                _namesOf = Attack;
                _animationNames = new HashSet<string>(Attack.Steps.OfType<CharacterAnimationController.AnimationInfo>()
                    .Where(i => i?.values != null).SelectMany(i => i.values.Where(v => v?.clip != null).Select(v => v.clip.name)));
            }
            if (_animationNames.Count == 0 || Boss == null)
                return false;
            // Yggdrasil plays them through his animation controller: the clip his bodies were last told to play (his
            // idle clip once the move is over).
            _bodies ??= Boss.GetComponentsInChildren<CharacterAnimation>(true);
            foreach (var body in _bodies)
                if (body != null && body.isActiveAndEnabled && body._actionClip != null && _animationNames.Contains(body._actionClip.name))
                    return true;
            _skeletons ??= Boss.GetComponentsInChildren<Spine.Unity.IAnimationStateComponent>(true);
            foreach (var skeleton in _skeletons)
            {
                var state = (skeleton as UnityEngine.Object) != null ? skeleton.AnimationState : null;
                if (state == null)
                    continue;
                foreach (var track in state.Tracks)
                    if (track?.Animation != null && _animationNames.Contains(track.Animation.Name))
                        return true;
            }
            return false;
        }

        public bool Active => Segment != null && (!Ended || Time.unscaledTime - EndedAt < StepGap);
        public bool Wants => Active && !Recorded.Contains(Segment) && (Frames.Count < MaxFrames || Stride < 4);

        // Long moves (Pope's Super baptism): when the take is full, every second picture is dropped and only every
        // Stride-th picture is kept from then on (10 → 5 → 2.5 fps), so a take can last up to 72 s in one sheet.
        public int Stride = 1, Shot;

        public void Add(Color32[] pixels, float time, bool busy)
        {
            if (Frames.Count >= MaxFrames)
            {
                if (Stride >= 4)
                    return;
                for (int i = Frames.Count - 1; i >= 1; i -= 2)
                {
                    Frames.RemoveAt(i);
                    Times.RemoveAt(i);
                    Busy.RemoveAt(i);
                }
                Stride *= 2;
            }
            if (Shot++ % Stride != 0)
                return;
            Frames.Add(pixels);
            Times.Add(time);
            Busy.Add(busy);
        }
    }

    private static FightRecorder _instance;
    private readonly List<Film> _films = new();

    private Camera _camera;
    private RenderTexture _target;
    private float _nextShot;
    private bool _shooting;

    /// <summary>
    /// A boss was hit or showed its health bar: film it (once per spawned boss). Dark Mirror fights are filmed into
    /// their own folder ("&lt;key&gt;@DM"), because the moves differ.
    /// </summary>
    public static void Watch(Character boss, string key)
    {
        key = CodexMode.Current(key);
        if (_instance == null || boss == null || !Plugin.CodexRecordFights.Value || _instance._films.Any(f => f.Boss == boss))
            return;
        if (!BossAttacks.HasAi(boss))
        {
            // A piece of the fight (Pope's dark crystal) is seen in the boss's films. Its bar opening means the fight
            // is on: film the boss, which may have no bar of its own yet (Pope behind his barrier).
            var owner = MainBoss(search: true);
            if (owner != null && owner != boss && CodexTracker.EntryOf(owner) is { } entry)
                Watch(owner, entry.key);
            return;
        }
        _instance.Begin(boss, key);
    }

    /// <summary>
    /// The boss or adventurer whose fight is going on (the latest one being filmed that is still alive), or null.
    /// With <paramref name="search"/> the scene is searched when nothing is filmed yet.
    /// </summary>
    public static Character MainBoss(bool search = false)
    {
        var film = _instance?._films.LastOrDefault(f => f.Boss != null && f.Boss.health != null && !f.Boss.health.dead);
        if (film != null || !search)
            return film?.Boss;
        foreach (var c in FindObjectsOfType<Character>())
            if (BossAttacks.Covers(c) && c.health != null && !c.health.dead && c.gameObject.activeInHierarchy && BossAttacks.HasAi(c))
                return c;
        return null;
    }

    private void Awake()
    {
        _instance = this;
        Patches.AttackTracker.Started += (run, coroutine) => Patches.Guard.Run("Codex filming", () => OnAttackStarted(run, coroutine));
        Patches.AttackTracker.Ended += (run, coroutine) => Patches.Guard.Run("Codex filming", () => OnAttackEnded(run, coroutine));
    }

    /// <summary>The player (un)marked a move for a new take in the book: applies to the fight in progress too.</summary>
    public static void Refilm(string key, string label, bool on)
    {
        if (_instance == null)
            return;
        foreach (var film in _instance._films.Where(f => f.Key == key))
        {
            if (on)
                film.Recorded.Remove(label);
            else if (CodexAnimations.Load(key, replays: true).Any(c => c.Label == label))
                film.Recorded.Add(label);
        }
    }

    /// <summary>
    /// Developer/PreferNewMoves: how much a node of a filmed boss's tree is wanted as the AI's next pick. 2: it leads to
    /// a move the Codex hasn't seen in this mode or has no film of (a few tries at a time, see Film.Worth); 1: to a move
    /// not done in this fight yet (once every seen move was done, a new round starts); 0: neither, or not a node of a
    /// boss being filmed.
    /// </summary>
    public static int Want(object node)
    {
        if (_instance == null || node == null)
            return 0;
        foreach (var film in _instance._films)
        {
            var moves = film.MovesBelow(node);
            if (moves == null)
                continue;
            if (moves.Any(m => film.Missing(m) && film.Worth(m)))
                return 2;
            // The round covers the moves seen so far: one never seen (a setup step, a locked phase) would keep it open.
            var seen = film.Moves.Where(film.Seen.Contains).ToList();
            if (seen.Count > 0 && seen.All(film.Done.Contains))
                film.Done.Clear();
            return moves.Any(m => film.Seen.Contains(m) && !film.Done.Contains(m)) ? 1 : 0;
        }
        return 0;
    }

    /// <summary>PreferNewMoves steered the AI to this node: counts a try for each missing move below it.</summary>
    public static void NoteSteered(object node)
    {
        foreach (var film in _instance != null ? _instance._films : new List<Film>())
            if (film.MovesBelow(node) is { } moves)
            {
                foreach (var move in moves.Where(film.Missing))
                    film.Steered(move);
                return;
            }
    }

    /// <summary>For the PreferNewMoves log: the boss and the moves a tree node leads to ("FirstHero1@DM: Rush, Dash").</summary>
    public static string MovesText(object node)
    {
        foreach (var film in _instance != null ? _instance._films : new List<Film>())
            if (film.MovesBelow(node) is { } moves)
                return $"{film.Key}: {string.Join(", ", moves.Take(4))}{(moves.Count > 4 ? $" (+{moves.Count - 4})" : "")}";
        return "?";
    }

    private void Begin(Character boss, string key)
    {
        // Moves already on film are skipped, unless the player marked them for a new take ("Refilm").
        var refilm = CodexAnimations.Refilm(CodexAnimations.ReplayFolderOf(key));
        var film = new Film
        {
            Boss = boss,
            Key = key,
            Recorded = new HashSet<string>(CodexAnimations.Load(key, replays: true).Select(c => c.Label).Where(l => !refilm.Contains(l))),
            Dark = CodexMode.DarkMirrorNow,
        };
        film.Seen = CodexTracker.MovesSeen(boss, film.Dark);
        film.GiveUpAt = Time.unscaledTime + 15f;
        TryGraph(film);
        _films.Add(film);
        Plugin.Log.LogInfo($"Codex: filming {key} ({film.Recorded.Count} moves already on film, " +
                           (film.Graph != null ? $"{film.Graph.Attacks.Count} attacks known)." : "moves by animation)."));
    }

    /// <summary>
    /// Sprite bosses and adventurers: attacks are the AI's attack coroutines or tree blocks, followed as they start and
    /// finish. Spine bosses (Yggdrasil) name their moves by animation, like their posed pictures. An adventurer's tree
    /// loads a moment after it appears: until then nothing is filmed (so no take gets a wrong name).
    /// </summary>
    private static void TryGraph(Film film)
    {
        film.Pending = false;
        try
        {
            // Spine bosses too (Chimera, Yggdrasil): the moves come from their AI; only without one are they named by
            // animation groups, like before.
            var graph = BossAttacks.Of(film.Boss);
            if (graph.Attacks.Count == 0 && !graph.Incomplete && film.Boss.GetComponentInChildren<Spine.Unity.SkeletonRenderer>() != null)
            {
                var parts = SpineCapture.PartsOf(film.Boss, t => CodexAnimations.IsEffect(t, film.Boss.transform));
                film.Moves = AnimationGrouping.Group(SpineCapture.AnimationNames(parts))
                    .Select(g => g.label).Where(IsMove).Distinct().ToList();
                return;
            }
            if (graph.Incomplete && Time.unscaledTime < film.GiveUpAt)
            {
                film.Pending = true;
                film.NextTry = Time.unscaledTime + 0.5f;
                return;
            }
            if (graph.Attacks.Count > 0)
            {
                film.Graph = graph;
                Patches.AttackTracker.Track(graph);
                BossAttacks.WriteReport(film.Boss, film.Key, graph); // fresh every fight, with the conditions' values
                // Moves the counter waits for: the shown attacks, without Dark Mirror-only ones in a normal fight.
                film.Moves = BossAttacks.Shown(graph)
                    .Where(a => film.Dark || !a.Steps.All(step => BossAttacks.HardmodeOnly(graph, step)))
                    .Select(a => a.Label).Where(IsMove).Distinct().ToList();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: following the attacks of {film.Key} failed, filming by animation: {e.Message}");
        }
    }

    private void OnAttackStarted(Patches.AttackTracker.Run run, object coroutine)
    {
        var film = _films.FirstOrDefault(f => f.Graph == run.Graph);
        if (film == null)
            return;
        var (nested, pattern) = Patches.AttackTracker.Context(run);
        if (nested)
            return; // a smaller attack or helper inside the one being filmed
        var attack = run.Graph.AttackOf(run.Unit);
        if (film.Segment != null)
        {
            bool inside = !film.Ended && film.Attack != null && film.Attack.Units.Any(u => AttackGraph.Reaches(u, run.Unit));
            bool samePattern = pattern != null && pattern == film.Pattern;
            bool tail = attack != null && attack.IsTail && Time.unscaledTime - film.EndedAt < TailWindow;
            if (inside)
                return;
            if (samePattern || tail)
            {
                // The next part of the same attack: keep filming it into the same take.
                if (samePattern && attack != null && film.Attack != null && attack != film.Attack)
                    film.Segment = LabelInPattern(film, attack);
                film.Root = coroutine;
                film.Ended = false;
                film.LastMove = Time.unscaledTime;
                return;
            }
            Flush(film);
        }
        if (attack == null || attack.IsTail)
            return; // a helper or a tail on its own (an escape at the start of a phase): nothing to film
        film.Attack = attack;
        film.Segment = attack.Label;
        film.Done.Add(attack.Label);
        // Seen once one of its actions actually plays (Update): an AI that only checks whether it could do the move
        // ("can Phoenix landing happen?") hasn't shown it. Moves made of animations only count at once.
        if (!attack.Steps.Any(s => s is Characters.Actions.Action))
            film.Saw(attack.Label);
        film.Root = coroutine;
        film.Pattern = pattern;
        film.Ended = false;
        film.LastMove = Time.unscaledTime;
    }

    /// <summary>A dispatcher pattern went on with its next attack: the take gets the combined attack's label.</summary>
    private static string LabelInPattern(Film film, AttackGraph.Attack next)
    {
        var combined = film.Graph.Attacks.FirstOrDefault(a => a.Units.Count > 1 &&
            film.Attack.Units.Any(a.Units.Contains) && next.Units.Any(a.Units.Contains));
        film.Attack = combined ?? (next.Steps.Count > film.Attack.Steps.Count ? next : film.Attack);
        return film.Attack.Label;
    }

    private void OnAttackEnded(Patches.AttackTracker.Run run, object coroutine)
    {
        var film = _films.FirstOrDefault(f => f.Graph == run.Graph);
        if (film != null && film.Segment != null && (coroutine == film.Root || coroutine == film.Pattern))
        {
            film.Ended = true;
            film.EndedAt = Time.unscaledTime;
        }
    }

    private void Update()
    {
        foreach (var film in _films.ToList())
        {
            var boss = film.Boss;
            if (boss == null || boss.health == null || boss.health.dead || !boss.gameObject.activeInHierarchy)
            {
                Flush(film);
                if (film.Graph != null)
                    Patches.AttackTracker.Forget(film.Graph);
                _films.Remove(film);
                continue;
            }
            if (film.Pending)
            {
                if (Time.unscaledTime >= film.NextTry)
                    TryGraph(film);
                continue;
            }
            if (film.Graph != null)
            {
                bool stepRunning = film.Segment != null && film.StepRunning();
                if (stepRunning)
                    film.Saw(film.Segment);
                // Only the attack's own steps count: an adventurer is always doing something (walking, the next move),
                // and a move whose end isn't reported (a special skill cut short) would otherwise be filmed forever.
                bool moving = stepRunning;
                if (moving)
                    film.LastMove = Time.unscaledTime;
                if (stepRunning && film.Ended)
                    film.EndedAt = Time.unscaledTime; // the coroutine is done, its action still plays
                // Finished (and no tail came), or the coroutine was stopped from outside (a phase change).
                if (film.Segment != null && ((film.Ended && Time.unscaledTime - film.EndedAt > TailWindow) ||
                                             (!film.Ended && !moving && Time.unscaledTime - film.LastMove > 3f)))
                    Flush(film);
                continue;
            }
            string move = CurrentMove(boss);
            if (move != null)
                film.LastMove = Time.unscaledTime;
            else if (film.Segment != null && (Time.unscaledTime - film.LastMove < StepGap || boss.runningMotion != null))
                move = film.Segment; // a short pause, or the body looks idle while its action still runs (Yggdrasil's laser)
            if (move != film.Segment)
            {
                Flush(film);
                film.Segment = move;
                if (move != null)
                    film.Saw(move);
            }
        }
        if (_films.Count == 0)
        {
            SetCamera(false);
            return;
        }
        if (!_shooting && Time.unscaledTime >= _nextShot && _films.Any(f => f.Wants))
        {
            _nextShot = Time.unscaledTime + Interval;
            StartCoroutine(Shoot(_films.Where(f => f.Wants)
                .Select(f => (f, f.Segment, f.Graph == null || f.Boss.runningMotion != null || f.StepRunning())).ToList()));
        }
    }

    // ------------------------------------------------------------------ move counter (under the boss bar)

    /// <summary>What isn't a move to wait for: idling, sleeping, entrances and deaths (they play before or after).</summary>
    private static readonly Regex NotAMove = new(
        @"(?i)^(phase \d+ · )?(idle|intro|appear(ance)?|sleep|dead|die|died|death|former in|idle cut scene|cut scene)\b|\b(test|temp)\b");

    internal static bool IsMove(string label) => !string.IsNullOrWhiteSpace(label) && !NotAMove.IsMatch(label);

    /// <summary>The move count of a boss being filmed, for the counter under its health bar; null if not filmed.</summary>
    public static MoveCount? CountFor(Character boss)
    {
        var film = _instance?._films.FirstOrDefault(f => f.Boss == boss);
        if (film == null)
            return null;
        var seen = film.Seen.Where(IsMove).ToList();
        var missing = film.Moves.Where(m => !film.Seen.Contains(m)).ToList();
        // A move that only happens inside a bigger one (Leiana's "Meteor in ground 2" inside "Twin meteor ground")
        // counts as seen once a seen move contains all of its steps.
        int inside = 0;
        if (film.Graph != null)
            foreach (var m in missing.ToList())
            {
                var attack = film.Graph.Attacks.FirstOrDefault(a => a.Label == m);
                if (attack != null && attack.Steps.Count > 0 && film.Graph.Attacks.Any(s =>
                        s != attack && film.Seen.Contains(s.Label) && attack.Steps.All(s.Steps.Contains)))
                {
                    missing.Remove(m);
                    inside++;
                }
            }
        return new MoveCount
        {
            Pending = film.Pending,
            Total = film.Moves.Count,
            Seen = film.Moves.Count > 0 ? film.Moves.Count - missing.Count : seen.Count,
            Inside = inside,
            Filmed = film.Moves.Count > 0 ? film.Moves.Count(film.Recorded.Contains) : film.Recorded.Count(IsMove),
            Missing = missing,
            NotFilmed = film.Moves.Where(m => film.Seen.Contains(m) && !film.Recorded.Contains(m)).ToList(),
            Dark = film.Dark,
        };
    }

    public struct MoveCount
    {
        public bool Pending, Dark;
        public int Total, Seen, Filmed, Inside;      // Inside: counted as seen because a bigger move contained it
        public List<string> Missing, NotFilmed;
    }

    private bool _manualBroken, _checkedFirstFrame;

    /// <summary>
    /// One picture. After the game has drawn its frame, the player and its projectiles are hidden, the film camera
    /// renders once by hand, and everything is shown again before the next frame: nothing flickers on screen.
    /// If rendering by hand doesn't work here, the camera renders with the game (the player is then in the film).
    /// </summary>
    private IEnumerator Shoot(List<(Film film, string segment, bool busy)> takes)
    {
        _shooting = true;
        yield return new WaitForEndOfFrame();
        if (!SetCamera(true))
        {
            _shooting = false;
            yield break;
        }
        if (_manualBroken)
        {
            _camera.enabled = true;              // renders with the next frame
            yield return new WaitForEndOfFrame();
            _camera.enabled = false;
        }
        else
        {
            _camera.enabled = false;
            var hidden = HidePlayer();
            try
            {
                _camera.Render();
            }
            catch (Exception e)
            {
                _manualBroken = true;
                Plugin.Log.LogInfo($"Codex: filming by hand is not possible ({e.Message}); the player will be in the film.");
            }
            finally
            {
                foreach (var r in hidden)
                    if (r != null)
                        r.enabled = true;
            }
        }
        float time = Time.unscaledTime;
        AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32, request =>
        {
            if (request.hasError)
                return;
            var pixels = request.GetData<Color32>().ToArray();
            if (!_checkedFirstFrame && !_manualBroken)
            {
                _checkedFirstFrame = true;
                if (pixels.All(p => p.r == 0 && p.g == 0 && p.b == 0))
                {
                    _manualBroken = true; // rendering by hand produced nothing: fall back
                    Plugin.Log.LogInfo("Codex: filming by hand gave an empty picture; the player will be in the film.");
                    return;
                }
            }
            foreach (var (film, segment, busy) in takes)
                if (film.Segment == segment)
                    film.Add(pixels, time, busy);
        });
        _shooting = false;
    }

    /// <summary>Hides the player's renderers and its projectiles for one render; returns what to show again.</summary>
    private static List<Renderer> HidePlayer()
    {
        var hidden = new List<Renderer>();
        var player = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player;
        if (player == null)
            return hidden;
        foreach (var r in player.GetComponentsInChildren<Renderer>())
            if (r.enabled)
            {
                r.enabled = false;
                hidden.Add(r);
            }
        foreach (var projectile in FindObjectsOfType<Characters.Projectiles.Projectile>())
        {
            if (projectile.owner != player)
                continue;
            foreach (var r in projectile.GetComponentsInChildren<Renderer>())
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
        }
        return hidden;
    }

    /// <summary>Turns the filming camera on (a fresh copy of the game camera's view) or off.</summary>
    private bool SetCamera(bool on)
    {
        try
        {
            if (!on)
            {
                if (_camera != null)
                    _camera.enabled = false;
                return false;
            }
            var main = Scenes.Scene<Scenes.GameBase>.instance?.camera;
            if (main == null)
                return false;
            if (_camera == null)
            {
                _target = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32);
                var go = new GameObject("DamageInsight_FightCamera");
                DontDestroyOnLoad(go);
                _camera = go.AddComponent<Camera>();
            }
            _camera.CopyFrom(main);
            _camera.targetTexture = _target;
            _camera.depth = main.depth - 1;
            _camera.enabled = false;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: the filming camera failed, filming is off for this session: {e.Message}");
            Plugin.CodexRecordFights.Value = false;
            _films.Clear();
            return false;
        }
    }

    /// <summary>The move the boss is performing, as a Codex label ("Fist slam"), or null while it idles.</summary>
    private static string CurrentMove(Character boss)
    {
        // Sprite bosses: the running action's attack is the move ("Rush" = ready, a, b, c, finish, standing).
        // Spine bosses (Yggdrasil) name their moves by animation, grouped like the posed pictures.
        try
        {
            if (boss.GetComponentInChildren<Spine.Unity.SkeletonRenderer>() == null)
            {
                var action = boss.runningMotion != null ? boss.runningMotion.action : null;
                return action != null ? AttackPatterns.LabelOf(boss, action) : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
        string name = null;
        try
        {
            var animation = boss.GetComponentsInChildren<CharacterAnimation>()
                .FirstOrDefault(a => a._animator != null && a._animator.isActiveAndEnabled);
            if (animation != null)
            {
                var clips = animation._animator.GetCurrentAnimatorClipInfo(0);
                if (clips.Length > 0 && clips[0].clip != null)
                    name = clips[0].clip.name;
            }
            if ((name == null || name.StartsWith("Empty")) && boss.runningMotion != null)
                name = boss.runningMotion.name;
        }
        catch (Exception)
        {
            return null;
        }
        if (string.IsNullOrEmpty(name) || Regex.IsMatch(name, @"idle|empty|walk", RegexOptions.IgnoreCase))
            return null;
        var group = AnimationGrouping.Group(new[] { name });
        return group.Count > 0 ? group[0].label : null;
    }

    /// <summary>Saves the filmed move (if long enough) on a background thread.</summary>
    private static void Flush(Film film)
    {
        // Idle at the end (Pope's "long idle" after Divine cross) is cut, keeping a moment for effects to fade.
        int last = film.Busy.FindLastIndex(b => b);
        int keep = last < 0 ? film.Frames.Count : Math.Min(film.Frames.Count, last + 1 + IdleTailFrames);
        if (film.Segment != null && keep >= 4 && !film.Recorded.Contains(film.Segment))
        {
            film.Recorded.Add(film.Segment);
            var frames = film.Frames.Take(keep).ToList();
            var durations = new List<float>();
            for (int i = 0; i < keep; i++)
                durations.Add(i + 1 < keep ? Mathf.Clamp(film.Times[i + 1] - film.Times[i], 0.05f, 0.5f) : Interval);
            string folder = CodexAnimations.ReplayFolderOf(film.Key), label = film.Segment, key = film.Key;
            Task.Run(() => Save(folder, label, frames, durations))
                .ContinueWith(t => Plugin.Log.LogInfo(t.IsFaulted
                    ? $"Codex: saving the film of {key} {label} failed: {t.Exception?.GetBaseException().Message}"
                    : $"Codex: filmed {key}: {label} ({frames.Count} frames)."));
        }
        film.Frames.Clear();
        film.Times.Clear();
        film.Busy.Clear();
        film.Stride = 1;
        film.Shot = 0;
        film.Segment = null;
        film.Attack = null;
        film.Root = film.Pattern = null;
        film.Ended = false;
    }

    private static readonly object SaveLock = new();

    /// <summary>Background thread: appends one move as a sheet to the boss's replay folder.</summary>
    private static void Save(string folder, string label, List<Color32[]> frames, List<float> durations)
    {
        int columns = Math.Max(1, Math.Min(frames.Count, 4096 / Width));
        int count = Math.Min(frames.Count, columns * (4096 / Height));
        int rows = (count + columns - 1) / columns;
        int sheetW = columns * Width, sheetH = rows * Height;
        var sheet = new Color32[sheetW * sheetH];
        for (int i = 0; i < count; i++)
        {
            int row = i / columns, col = i % columns;
            int baseX = col * Width, baseY = sheetH - (row + 1) * Height;
            for (int y = 0; y < Height; y++)
                Array.Copy(frames[i], y * Width, sheet, (baseY + y) * sheetW + baseX, Width);
        }
        var png = ImageConversion.EncodeArrayToPNG(sheet, GraphicsFormat.R8G8B8A8_UNorm, (uint)sheetW, (uint)sheetH);

        lock (SaveLock)
        {
            Directory.CreateDirectory(folder);
            string jsonPath = Path.Combine(folder, "animations.json");
            // Keep every other move's entry as it is; an older take of this move is replaced (refilm).
            var entries = new List<string>();
            if (File.Exists(jsonPath))
            {
                var doc = Describe.GearDoc.ParseNode(File.ReadAllText(jsonPath, Encoding.UTF8));
                foreach (var node in doc.List("clips"))
                {
                    if (node.Str("label") == label)
                    {
                        string old = Path.Combine(folder, node.Str("file") ?? "");
                        if (File.Exists(old))
                            File.Delete(old);
                        continue;
                    }
                    entries.Add(Entry(node.Str("label") ?? "", node.Str("file") ?? "", (int)node.Num("cellW"), (int)node.Num("cellH"),
                        (int)node.Num("cols"), (int)node.Num("count"), node.Numbers("durations").Select(d => (float)d)));
                }
            }
            string file = $"clip_{DateTime.Now.Ticks}.png";
            File.WriteAllBytes(Path.Combine(folder, file), png);
            entries.Add(Entry(label, file, Width, Height, columns, count, durations.Take(count)));
            File.WriteAllText(jsonPath, "{\"clips\":[" + string.Join(",", entries) + "]}", new UTF8Encoding(false));
            CodexAnimations.SetRefilm(folder, label, false);
        }
    }

    private static string Entry(string label, string file, int w, int h, int cols, int count, IEnumerable<float> durations) =>
        "{\"label\":" + LogJson.Quote(label) + ",\"file\":" + LogJson.Quote(file) +
        $",\"cellW\":{w},\"cellH\":{h},\"cols\":{cols},\"count\":{count},\"durations\":[" +
        string.Join(",", durations.Select(d => d.ToString("0.###", CultureInfo.InvariantCulture))) + "]}";
}
