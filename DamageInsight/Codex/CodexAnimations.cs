using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Characters;
using Characters.Actions;
using DamageInsight.Describe;
using DamageInsight.Recording;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// One animation of an enemy for the Codex: where its sheet is and how its frames are laid out. The frames
/// themselves load on demand (CodexAnimations.EnsureFrames); until then Ready is false.
/// </summary>
public sealed class CodexClip
{
    public string Label = "";
    public string File = "";
    public int CellWidth, CellHeight, Columns, Count;
    public float[] Durations = new float[0];
    public Sprite[] Frames;          // null until loaded
    public bool Loading, Failed;
    public bool Ready => Frames != null;
}

/// <summary>
/// Captures an enemy's animations for the Codex, and loads them back. Two phases, so fights are never slowed down:
/// 1. On a kill (enemy without saved animations): its clips are sampled, one clip per game frame
///    (AnimationClip.SampleAnimation), noting only which sprite shows when. Effects, projectiles and platforms that
///    belong to the enemy's object are left out. Nothing heavy, nothing is saved yet.
/// 2. While the Codex is open (the game is paused): the noted frames are "developed" a few milliseconds per frame:
///    each frame is rebuilt from its sprite mesh (reading only that frame's area of the atlas), packed into sheets
///    aligned on the pivot, and saved to Codex/Animations/&lt;key&gt;/ (clip_N.png + animations.json).
/// Bosses also get their object structure written to Codex/Debug/&lt;key&gt;_structure.json (to build composed
/// pictures of cut-out bosses like Yggdrasil later).
/// </summary>
public static class CodexAnimations
{
    private const int MaxClips = 64, MaxFramesPerClip = 48, MaxSheetSize = 4096;

    private static readonly Queue<(Character enemy, string key)> ToSample = new();
    private static readonly HashSet<string> Tried = new();
    private static readonly Dictionary<string, List<CodexClip>> Cache = new();
    private static bool _sampling;

    public static string FolderOf(string key) => Path.Combine(CodexTracker.Folder, "Animations", key);

    public static bool Has(string key) => File.Exists(Path.Combine(FolderOf(key), "animations.json"));

    /// <summary>How many enemies wait to be developed (shown in the Codex).</summary>
    public static int Waiting => ToSample.Count + CodexDeveloper.Waiting;

    static CodexAnimations()
    {
        CodexDeveloper.Developed += key => Cache.Remove(key);
    }

    /// <summary>
    /// Queues a capture if this enemy has no animations saved yet for the current mode (at most one try per key and
    /// session). Dark Mirror captures go to "&lt;key&gt;@DM" (<see cref="CodexMode"/>).
    /// </summary>
    public static void RequestCapture(Character enemy, string key, MonoBehaviour runner)
    {
        key = CodexMode.Current(key);
        if (Tried.Contains(key) || Has(key))
            return;
        Tried.Add(key);
        ToSample.Enqueue((enemy, key));
        if (!_sampling && runner != null)
            runner.StartCoroutine(SampleQueue());
    }

    // ---------------------------------------------------------------- phase 1: sampling (during play, tiny)

    private static IEnumerator SampleQueue()
    {
        _sampling = true;
        while (ToSample.Count > 0)
        {
            var (enemy, key) = ToSample.Dequeue();
            if (enemy == null)
                continue;
            // Adventurers load their behaviour tree a moment after they appear: wait for it (up to ~10 s), so their
            // moves are grouped into whole attacks.
            for (int wait = 0; wait < 20 && enemy != null && BossAttacks.Covers(enemy) && SafeIncomplete(enemy); wait++)
                yield return new WaitForSecondsRealtime(0.5f);
            if (enemy == null)
                continue;
            List<Part> parts = null;
            List<SpineCapture.Part> spine = null;
            var attackLabels = new HashSet<string>();
            try
            {
                if (BossAttacks.Covers(enemy))
                {
                    WriteStructure(enemy, key);
                    BossAttacks.WriteReport(enemy, key, BossAttacks.Of(enemy));
                    attackLabels.UnionWith(BossAttacks.Shown(BossAttacks.Of(enemy)).Select(a => a.Label));
                }
                var root = enemy.transform;
                spine = SpineCapture.PartsOf(enemy, t => IsEffect(t, root));
                // Spine figures (big bosses) are posed from their skeleton data later; their loose sprite objects
                // (hand props, freeze heads) are not the figure.
                parts = spine.Count > 0 ? new List<Part>() : PartsOf(enemy);
                // A boss whose attacks are known shows them whole on its body; its effect sprites (a meteor) are
                // pieces of those attacks and are seen in the films.
                if (parts.Count > 1 && BossAttacks.Covers(enemy) && BossAttacks.Of(enemy).Attacks.Count > 0)
                {
                    int bodies = enemy.GetComponentsInChildren<CharacterAnimation>(true).Count(b => b.spriteRenderer != null && !IsEffect(b.transform, root));
                    parts = parts.Take(Math.Max(1, bodies)).ToList();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Codex: looking at {key} failed: {e.Message}");
            }
            if (spine != null && spine.Count > 0)
            {
                CodexDeveloper.Enqueue(new CodexDeveloper.Request { Key = key, Spine = spine, SpineGroups = SpineMoves(enemy, spine) });
                Plugin.Log.LogInfo($"Codex: {key} is a Spine figure ({spine.Count} skeletons, animations: " +
                                   $"{string.Join(", ", SpineCapture.AnimationNames(spine))}); it will be developed in the Codex.");
                continue;
            }
            if (parts == null || parts.Count == 0)
            {
                Plugin.Log.LogInfo($"Codex: {key} has no animated sprite to capture.");
                continue;
            }

            var item = new CodexDeveloper.Request { Key = key };
            var labels = new Dictionary<string, int>();
            foreach (var part in parts)
            {
                string prefix = part == parts[0] ? "" : Short(OwnerNames.Humanize(part.Name), 14) + ": ";
                foreach (var (label, clips) in part.Clips)
                {
                    yield return null; // one sequence per frame: sampling is quick, but a boss has many
                    if (part.Renderer == null || item.SpriteClips.Count >= MaxClips * 2)
                        break; // the enemy is gone: keep what we have
                    var original = part.Renderer.sprite;
                    var frames = new List<(Sprite sprite, float duration)>();
                    try
                    {
                        foreach (var clip in clips)
                            frames.AddRange(Sample(clip, part.Target, part.Renderer));
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Codex: sampling {key} {label} failed: {e.Message}");
                        continue;
                    }
                    finally
                    {
                        if (part.Renderer != null)
                            part.Renderer.sprite = original; // the Animator re-applies its own pose anyway
                    }
                    // A single frame is a pose (hit, stun), not an animation; only the first picture may be still, and
                    // a boss's move done in one pose (Leiana's backstep: one frame sliding back).
                    if (frames.Count == 0 || (frames.Count == 1 && item.SpriteClips.Count > 0 && !attackLabels.Contains(label)))
                        continue;
                    string name = prefix + label;
                    int n = labels.TryGetValue(name, out int seen) ? seen + 1 : 1;
                    labels[name] = n;
                    item.SpriteClips.Add((n > 1 ? $"{name} {n}" : name, frames));
                }
            }
            if (item.SpriteClips.Count > 0)
                CodexDeveloper.Enqueue(item);
            else
                Plugin.Log.LogInfo($"Codex: {key}: no sprite frames found.");
        }
        _sampling = false;
    }

    /// <summary>
    /// A Spine boss's pictures grouped like its AI's moves: each move with the Spine animations its steps play
    /// ("Wreck destroy" = the big stomp; Yggdrasil's "Energy bomb" includes its laser animations), after the
    /// non-attacks (idle, sleep, appearance, death) grouped by name. Null (group by name) if the AI gives none.
    /// </summary>
    private static List<(string label, List<string> parts)> SpineMoves(Character enemy, List<SpineCapture.Part> spine)
    {
        try
        {
            if (!BossAttacks.Covers(enemy))
                return null;
            var graph = BossAttacks.Of(enemy);
            if (graph.Attacks.Count == 0)
                return null;
            var names = SpineCapture.AnimationNames(spine);
            var known = new HashSet<string>(names);
            var moves = new List<(string label, List<string> parts)>();
            var used = new HashSet<string>();
            foreach (var attack in BossAttacks.Shown(graph))
            {
                var animations = attack.Steps.OfType<CharacterAnimationController.AnimationInfo>()
                    .Where(i => i?.values != null)
                    .SelectMany(i => i.values.Where(v => v?.clip != null).Select(v => v.clip.name))
                    .Where(known.Contains).Distinct().ToList();
                if (animations.Count == 0)
                    continue;
                moves.Add((attack.Label, animations));
                used.UnionWith(animations);
            }
            if (moves.Count == 0)
                return null;
            // Non-attacks first, unless a move already shows them (Yggdrasil's "Appearance" is a move).
            var groups = AnimationGrouping.Group(names)
                .Where(g => System.Text.RegularExpressions.Regex.IsMatch(g.label, @"(?i)\b(idle|sleep|appear\w*|dead|die|intro)\b") &&
                            !g.parts.All(used.Contains))
                .ToList();
            groups.AddRange(moves);
            return groups;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: grouping the Spine moves of {enemy.name} failed, grouping by animation: {e.Message}");
            return null;
        }
    }

    private static string Short(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 3).TrimEnd() + "...";

    private static bool SafeIncomplete(Character enemy)
    {
        try { return BossAttacks.Of(enemy).Incomplete; }
        catch (Exception) { return false; }
    }

    /// <summary>An animated part of an enemy: its sprite renderer and the clips to sample on its object.</summary>
    private sealed class Part
    {
        public GameObject Target;
        public SpriteRenderer Renderer;
        public string Name = "";
        public List<(string label, List<AnimationClip> clips)> Clips = new();
        public float Area => Renderer.sprite != null ? Renderer.sprite.rect.width * Renderer.sprite.rect.height : 0;
    }

    private static readonly System.Text.RegularExpressions.Regex EffectName = new(
        @"effect|fx|particle|projectile|spark|explosion|platform|bullet|laser|sweep|emerge|smoke|dust|shadow|trail|flash|aura|fire ?position|spawn",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>True for objects that are effects, projectiles or props of the enemy rather than its figure.</summary>
    internal static bool IsEffect(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (EffectName.IsMatch(t.name))
                return true;
        return false;
    }

    /// <summary>
    /// Every animated part of the figure: CharacterAnimation parts (normal enemies), plus animators with a sprite
    /// renderer that aren't one (some bosses). Effects, projectiles and platforms are left out. Biggest part first.
    /// </summary>
    private static List<Part> PartsOf(Character enemy)
    {
        var parts = new List<Part>();
        var covered = new HashSet<GameObject>();
        var root = enemy.transform;
        foreach (var body in enemy.GetComponentsInChildren<CharacterAnimation>(true))
        {
            if (body.spriteRenderer == null || IsEffect(body.transform, root))
                continue;
            covered.Add(body.gameObject);
            parts.Add(new Part { Target = body.gameObject, Renderer = body.spriteRenderer, Name = body.name, Clips = ClipsOf(enemy, body) });
        }
        foreach (var animator in enemy.GetComponentsInChildren<Animator>(true))
        {
            if (covered.Contains(animator.gameObject) || animator.runtimeAnimatorController == null || IsEffect(animator.transform, root))
                continue;
            var renderer = animator.GetComponent<SpriteRenderer>() ?? animator.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer == null)
                continue;
            var part = new Part { Target = animator.gameObject, Renderer = renderer, Name = animator.name };
            var seen = new HashSet<AnimationClip>();
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null && !clip.name.StartsWith("Empty") && !EffectName.IsMatch(clip.name) && part.Clips.Count < MaxClips && seen.Add(clip))
                    part.Clips.Add((OwnerNames.Humanize(clip.name), new List<AnimationClip> { clip }));
            if (part.Clips.Count > 0)
                parts.Add(part);
        }
        // The character's own bodies (CharacterAnimation) first, biggest first; then other animated parts. Pope's
        // barrier effect can be bigger than Pope when first seen.
        return parts.Take(covered.Count).OrderByDescending(p => p.Area)
            .Concat(parts.Skip(covered.Count).OrderByDescending(p => p.Area)).ToList();
    }

    /// <summary>Writes a boss's object structure (parts, sprites, sorting, animators and their clips) for analysis.</summary>
    private static void WriteStructure(Character enemy, string key)
    {
        string path = Path.Combine(CodexTracker.Folder, "Debug", key + "_structure.json");
        if (File.Exists(path))
            return;
        var sb = new StringBuilder("{\"key\":").Append(LogJson.Quote(key)).Append(",\"objects\":[");
        bool first = true;
        foreach (var t in enemy.GetComponentsInChildren<Transform>(true))
        {
            var renderer = t.GetComponent<SpriteRenderer>();
            var animator = t.GetComponent<Animator>();
            if (renderer == null && animator == null)
                continue;
            if (!first) sb.Append(',');
            first = false;
            string relative = t == enemy.transform ? "" : RelativePath(t, enemy.transform);
            sb.Append("{\"path\":").Append(LogJson.Quote(relative))
              .Append(",\"active\":").Append(t.gameObject.activeInHierarchy ? "true" : "false")
              .Append(",\"components\":").Append(LogJson.Quote(string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "?" : c.GetType().Name))));
            if (renderer != null)
                sb.Append(",\"sprite\":").Append(LogJson.Quote(renderer.sprite != null ? $"{renderer.sprite.name} {renderer.sprite.rect.width}x{renderer.sprite.rect.height} tex {renderer.sprite.texture?.name}" : "(none)"))
                  .Append(",\"sorting\":").Append(LogJson.Quote($"{renderer.sortingLayerName} {renderer.sortingOrder}"))
                  .Append(",\"enabled\":").Append(renderer.enabled ? "true" : "false");
            if (animator != null && animator.runtimeAnimatorController != null)
                sb.Append(",\"controller\":").Append(LogJson.Quote(animator.runtimeAnimatorController.name))
                  .Append(",\"clips\":[").Append(string.Join(",", animator.runtimeAnimatorController.animationClips
                      .Where(c => c != null).Select(c => LogJson.Quote($"{c.name} {c.length:0.00}s")))).Append(']');
            sb.Append('}');
        }
        sb.Append("]}");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Plugin.Log.LogInfo($"Codex: wrote the structure of {key} to {path}");
    }

    private static string RelativePath(Transform t, Transform root)
    {
        var names = new List<string>();
        for (; t != null && t != root; t = t.parent)
            names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }

    // Phase 2 (drawing and saving) lives in CodexDeveloper: game-thread slices + a background thread.

    /// <summary>
    /// Idle, walk, jump and fall first; then every attack as ONE sequence of the clips of all actions it starts, read
    /// from the AI's code ("Rush" = dash, ready, rush a, rush b, rush c, finish, standing; see <see cref="AttackGraph"/>).
    /// Enemies whose AI gives no attacks are grouped per action (by field names, <see cref="AttackPatterns"/>). Then
    /// motions outside any action, then the animator's other clips, as long as they show something new.
    /// </summary>
    private static List<(string label, List<AnimationClip> clips)> ClipsOf(Character enemy, CharacterAnimation body)
    {
        var list = new List<(string, List<AnimationClip>)>();
        var seen = new HashSet<AnimationClip>();
        // A sequence keeps its pieces even if one is shown elsewhere too (the jump of "Golden meteor"); loose pieces
        // are only added when they show something new.
        void Add(string label, List<AnimationClip> clips, bool onlyIfNew = false)
        {
            clips = clips.Where(c => c != null && !c.name.StartsWith("Empty")).ToList();
            clips = clips.Where((c, i) => i == 0 || clips[i - 1] != c).ToList();
            if (clips.Count == 0 || list.Count >= MaxClips ||
                (onlyIfNew ? clips.Any(seen.Contains) : list.Any(l => l.Item1 == label && l.Item2.SequenceEqual(clips))))
                return;
            foreach (var c in clips)
                seen.Add(c);
            list.Add((label, clips));
        }
        AnimationClip ClipOfInfo(CharacterAnimationController.AnimationInfo info)
        {
            var values = info?.values;
            if (values == null)
                return null;
            // The clip for this body part; without one for it, the animation's first clip (Leiana's backstep and dash
            // name another part).
            return values.FirstOrDefault(k => k != null && k.clip != null && (string.IsNullOrEmpty(k.key) || k.key == body._key))?.clip
                   ?? values.FirstOrDefault(k => k?.clip != null)?.clip;
        }
        AnimationClip ClipOf(Characters.Actions.Motion motion) => ClipOfInfo(motion?.animationInfo);
        Characters.Actions.Motion[] MotionsOf(Characters.Actions.Action action)
        {
            try { return action.motions ?? Array.Empty<Characters.Actions.Motion>(); }
            catch (Exception) { return Array.Empty<Characters.Actions.Motion>(); }
        }

        Add("Idle", new List<AnimationClip> { body._idleClip });
        Add("Walk", new List<AnimationClip> { body._walkClip });
        Add("Jump", new List<AnimationClip> { body._jumpClip });
        Add("Fall", new List<AnimationClip> { body._fallClip });

        var inActions = new HashSet<Characters.Actions.Motion>();
        var actions = enemy.GetComponentsInChildren<Characters.Actions.Action>(true);
        AttackGraph graph = null;
        try { graph = BossAttacks.Covers(enemy) ? BossAttacks.Of(enemy) : null; }
        catch (Exception e) { Plugin.Log.LogWarning($"Codex: reading the attacks of {enemy.name} failed: {e.Message}"); }

        if (graph != null && graph.Attacks.Count > 0)
        {
            // Tails (an adventurer's potion, the sisters' escape) follow many moves: in the films, not in every picture.
            var tailSteps = new HashSet<object>(graph.Attacks.Where(a => a.IsTail).SelectMany(a => a.Steps));
            foreach (var attack in BossAttacks.Shown(graph))
                Add(attack.Label, attack.Steps
                    .Where(step => !BossAttacks.HardmodeOnly(graph, step) && !tailSteps.Contains(step))
                    .SelectMany(step => step is Characters.Actions.Action action
                        ? MotionsOf(action).Select(ClipOf)
                        : new[] { ClipOfInfo(step as CharacterAnimationController.AnimationInfo) })
                    .ToList());
            foreach (var action in actions)
            {
                var motions = MotionsOf(action);
                foreach (var m in motions.Where(m => m != null))
                    inActions.Add(m);
                // Actions no attack uses are left out (unused leftovers like "(legacy) action"), except dying.
                if (!graph.Uses(action) && DeathName.IsMatch(action.name))
                    Add(ActionLabel(action), motions.Select(ClipOf).ToList(), onlyIfNew: true);
            }
        }
        else
        {
            // Actions the AI chains into one attack by field name ("_goldenMeteorJump", "_goldenMeteorReady") form
            // one sequence, in the order the AI declares them; every other action is a sequence of its own.
            var patterns = AttackPatterns.Map(enemy);
            var steps = new List<(string label, int order, List<AnimationClip> clips)>();
            int loose = 100000;
            foreach (var action in actions)
            {
                var motions = MotionsOf(action);
                foreach (var m in motions.Where(m => m != null))
                    inActions.Add(m);
                var clips = motions.Select(ClipOf).ToList();
                steps.Add(patterns.TryGetValue(action, out var group) ? (group.label, group.order, clips) : (ActionLabel(action), loose++, clips));
            }
            foreach (var attack in steps.GroupBy(s => s.label).OrderBy(g => g.Min(s => s.order)))
                Add(attack.Key, attack.OrderBy(s => s.order).SelectMany(s => s.clips).ToList());
        }
        if (graph != null && graph.Attacks.Count > 0)
            return list; // everything else would be loose pieces of the attacks above
        foreach (var motion in enemy.GetComponentsInChildren<Characters.Actions.Motion>(true))
            if (!inActions.Contains(motion) && ClipOf(motion) is { } clip)
                Add(MotionLabel(motion, clip), new List<AnimationClip> { clip }, onlyIfNew: true);
        // Some bosses play attacks straight from the animator (not through motions): take those clips too.
        var controller = body._animator != null ? body._animator.runtimeAnimatorController : null;
        if (controller != null)
            foreach (var clip in controller.animationClips)
                if (clip != null && !EffectName.IsMatch(clip.name))
                    Add(OwnerNames.Humanize(clip.name), new List<AnimationClip> { clip }, onlyIfNew: true);
        return list;
    }

    private static readonly System.Text.RegularExpressions.Regex DeathName =
        new(@"^\s*(die|died|dead|death)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>An action's readable name: its object, or the first non-container object above it ("Rush").</summary>
    public static string ActionLabel(Characters.Actions.Action action)
    {
        for (var t = action.transform; t != null && t.GetComponent<Character>() == null; t = t.parent)
            if (!IsContainerName(t.name))
                return OwnerNames.Humanize(t.name);
        return OwnerNames.Humanize(action.name);
    }

    /// <summary>
    /// A readable name for a motion: the first object above it whose name isn't a container ("[0]", "Motion",
    /// "Motions", "Actions"...), e.g. "Attack" or "Jump attack"; the clip name as a last resort.
    /// </summary>
    private static string MotionLabel(Characters.Actions.Motion motion, AnimationClip clip)
    {
        for (var t = motion.transform; t != null && t.GetComponent<Character>() == null; t = t.parent)
            if (!IsContainerName(t.name))
                return OwnerNames.Humanize(t.name);
        return OwnerNames.Humanize(clip.name);
    }

    private static bool IsContainerName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^\s*(\[\d+\]|motions?|actions?|operations?|\d+)\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Samples a clip and returns each new sprite with how long it shows.</summary>
    private static List<(Sprite, float)> Sample(AnimationClip clip, GameObject target, SpriteRenderer renderer)
    {
        var frames = new List<(Sprite sprite, float duration)>();
        float step = 1f / Mathf.Clamp(clip.frameRate > 0 ? clip.frameRate : 12f, 6f, 30f);
        float length = Mathf.Max(clip.length, step);
        Sprite last = null;
        float lastStart = 0f;
        for (float t = 0f; t < length + 1e-4f && frames.Count < MaxFramesPerClip; t += step)
        {
            clip.SampleAnimation(target, Mathf.Min(t, length));
            var sprite = renderer.sprite;
            if (sprite == null || sprite == last)
                continue;
            if (last != null)
                frames[frames.Count - 1] = (last, t - lastStart);
            frames.Add((sprite, step));
            last = sprite;
            lastStart = t;
        }
        if (frames.Count > 0)
            frames[frames.Count - 1] = (last, Mathf.Max(step, length - lastStart));
        return frames;
    }

    // ---------------------------------------------------------------- loading

    public static string ReplayFolderOf(string key) => Path.Combine(CodexTracker.Folder, "Replays", key);

    private static readonly object RefilmLock = new();

    /// <summary>Moves marked for a new take (refilm.txt in the replay folder, one label per line).</summary>
    public static HashSet<string> Refilm(string replayFolder)
    {
        lock (RefilmLock)
        {
            string path = Path.Combine(replayFolder, "refilm.txt");
            return File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path, Encoding.UTF8).Where(l => l.Trim().Length > 0))
                : new HashSet<string>();
        }
    }

    /// <summary>Marks or unmarks a move for a new take. Called from the book and from the recorder's save thread.</summary>
    public static void SetRefilm(string replayFolder, string label, bool on)
    {
        lock (RefilmLock)
        {
            var labels = Refilm(replayFolder);
            if (on ? !labels.Add(label) : !labels.Remove(label))
                return;
            Directory.CreateDirectory(replayFolder);
            File.WriteAllLines(Path.Combine(replayFolder, "refilm.txt"), labels, new UTF8Encoding(false));
        }
    }

    private static readonly Dictionary<string, (DateTime written, List<CodexClip> clips)> ReplayCache = new();
    private static readonly Dictionary<string, (Sprite still, Sprite outline)> Portraits = new();

    /// <summary>
    /// The saved animations of an enemy, or with <paramref name="replays"/> its filmed moves (re-read when the fight
    /// recorder added one). Only the list is read here; the pictures of a clip load when it is shown (EnsureFrames).
    /// </summary>
    public static List<CodexClip> Load(string key, bool replays)
    {
        if (!replays)
            return Load(key);
        string folder = ReplayFolderOf(key);
        string json = Path.Combine(folder, "animations.json");
        var written = File.Exists(json) ? File.GetLastWriteTimeUtc(json) : DateTime.MinValue;
        if (ReplayCache.TryGetValue(key, out var cached) && cached.written == written)
            return cached.clips;
        var clips = written == DateTime.MinValue ? new List<CodexClip>() : ReadIndex(folder, key);
        ReplayCache[key] = (written, clips);
        return clips;
    }

    /// <summary>The saved animations of an enemy (cached list; pictures load on demand), or an empty list.</summary>
    public static List<CodexClip> Load(string key)
    {
        if (Cache.TryGetValue(key, out var clips))
            return clips;
        clips = ReadIndex(FolderOf(key), key);
        Cache[key] = clips;
        Portraits.Remove(key);
        return clips;
    }

    private static List<CodexClip> ReadIndex(string folder, string key)
    {
        var clips = new List<CodexClip>();
        try
        {
            string path = Path.Combine(folder, "animations.json");
            if (!File.Exists(path))
                return clips;
            foreach (var node in GearDoc.ParseNode(File.ReadAllText(path, Encoding.UTF8)).List("clips"))
            {
                string file = Path.Combine(folder, node.Str("file") ?? "");
                if (!File.Exists(file))
                    continue;
                int count = (int)node.Num("count");
                var durations = node.Numbers("durations").Select(d => (float)d).ToArray();
                clips.Add(new CodexClip
                {
                    Label = node.Str("label") ?? "",
                    File = file,
                    CellWidth = (int)node.Num("cellW"),
                    CellHeight = (int)node.Num("cellH"),
                    Columns = Math.Max(1, (int)node.Num("cols")),
                    Count = count,
                    Durations = durations.Length == count ? durations : Enumerable.Repeat(1f / 12f, count).ToArray(),
                });
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: animations of {key} could not be read: {e.Message}");
        }
        return clips;
    }

    /// <summary>
    /// Starts loading a clip's sheet if needed: Unity decodes the PNG on a background thread
    /// (UnityWebRequestTexture), so the game never waits. Until then clip.Ready is false.
    /// </summary>
    public static void EnsureFrames(CodexClip clip)
    {
        if (clip == null || clip.Ready || clip.Loading || clip.Failed || CodexWindow.Instance == null)
            return;
        clip.Loading = true;
        CodexWindow.Instance.StartCoroutine(LoadFrames(clip));
    }

    private static IEnumerator LoadFrames(CodexClip clip)
    {
        using var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(new Uri(clip.File).AbsoluteUri, true);
        yield return request.SendWebRequest();
        clip.Loading = false;
        try
        {
            var texture = request.result == UnityEngine.Networking.UnityWebRequest.Result.Success
                ? UnityEngine.Networking.DownloadHandlerTexture.GetContent(request)
                : null;
            if (texture == null)
            {
                clip.Failed = true;
                Plugin.Log.LogWarning($"Codex: {clip.File} could not be loaded: {request.error}");
                yield break;
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var frames = new Sprite[clip.Count];
            for (int i = 0; i < clip.Count; i++)
            {
                int row = i / clip.Columns, col = i % clip.Columns;
                // Row 0 is the top of the sheet; texture coordinates start at the bottom.
                var rect = new Rect(col * clip.CellWidth, texture.height - (row + 1) * clip.CellHeight, clip.CellWidth, clip.CellHeight);
                frames[i] = Sprite.Create(texture, rect, new Vector2(0.5f, 0f), 100f);
            }
            clip.Frames = frames;
        }
        catch (Exception e)
        {
            clip.Failed = true;
            Plugin.Log.LogWarning($"Codex: {clip.File} could not be cut into frames: {e.Message}");
        }
    }

    /// <summary>
    /// The small still picture of an enemy (first frame of its idle) and its outline, from portrait.png / outline.png
    /// written by CodexDeveloper. Small files, loaded directly. (null, null) if there are none.
    /// </summary>
    public static (Sprite still, Sprite outline) Portrait(string key)
    {
        if (Portraits.TryGetValue(key, out var cached))
            return cached;
        string folder = FolderOf(key);
        var result = (LoadSmall(Path.Combine(folder, "portrait.png")), LoadSmall(Path.Combine(folder, "outline.png")));
        Portraits[key] = result;
        // Captured before portraits existed: make them once from the first sheet (loaded in the background).
        if (result.Item1 == null && CodexWindow.Instance != null)
        {
            var clips = Load(key);
            if (clips.Count > 0)
                CodexWindow.Instance.StartCoroutine(MakePortrait(key, folder, clips[0]));
        }
        return result;
    }

    private static IEnumerator MakePortrait(string key, string folder, CodexClip clip)
    {
        using var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(new Uri(clip.File).AbsoluteUri, false);
        yield return request.SendWebRequest();
        if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            yield break;
        var texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
        if (texture == null)
            yield break;
        try
        {
            int w = clip.CellWidth, h = clip.CellHeight;
            var still = texture.GetPixels32();
            var cell = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(still, (texture.height - h + y) * texture.width, cell, y * w, w);
            var png = new Texture2D(w, h, TextureFormat.RGBA32, false);
            png.SetPixels32(cell);
            File.WriteAllBytes(Path.Combine(folder, "portrait.png"), png.EncodeToPNG());
            png.SetPixels32(CodexDeveloper.Outline(cell, w, h));
            File.WriteAllBytes(Path.Combine(folder, "outline.png"), png.EncodeToPNG());
            UnityEngine.Object.Destroy(png);
            Portraits.Remove(key);
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    public static void ForgetPortrait(string key) => Portraits.Remove(key);

    private static Sprite LoadSmall(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!texture.LoadImage(File.ReadAllBytes(path), true))
                return null;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0f), 100f);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
