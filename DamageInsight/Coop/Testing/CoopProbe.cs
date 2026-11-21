using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Characters;
using Characters.Controllers;
using HarmonyLib;
using Level;
using Scenes;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// What each game of a co-op test SHOWS, sampled four times a second (Umut 2026-11-10: "find new ways to verify
/// synchronisation ... so I as a human have to do as little input as possible"). The trace (CoopTrace) says which
/// events each game sent and played; this says what came of it on screen, so tools/coop/sync-report.py can measure
/// how far apart the two games are without anyone watching:
/// - every bound enemy: where it is, facing, health, its running move and the move's time, flags (walking, grounded,
///   stunned, frozen, hidden ...), its target's slot, and a fingerprint of the things switched on below it (attack
///   signs, auras: the Dark sister's rush lines that stayed on a client would have shown here);
/// - the players (this game's own and the teammates' copies), the camera;
/// - the screen's state: HUD, letterbox, input block, time scale, boss and dark-enemy health bars, dialogues;
/// - the room: open doors, waves left, living enemies WITHOUT a net id (each game runs those by itself);
/// - a census of every effect and projectile that appeared (by name), every 2 s.
/// Samples sit on a grid of the host's clock: the host samples at grid time T, a client when the moment it DRAWS
/// reaches T (its enemies are drawn a little in the past), so sample k shows the same moment on both games.
/// One JSON line per sample in `<copy data>/CoopTrace/<start>_<side>.probe.jsonl`. Test copies only.
/// </summary>
internal static class CoopProbe
{
    public const double Interval = 0.25, CensusSeconds = 2;

    private static StreamWriter _file;
    private static Func<double, double> _toSession = t => t;
    private static readonly StringBuilder Line = new(4096);
    private static long _lastGrid = long.MinValue;
    private static double _nextFlush, _nextCensus;
    private static uint _room;
    private static bool _patched;

    private static readonly List<(ushort id, Character c, bool lifeOnly)> Bound = new();
    private static readonly List<Renderer> Renderers = new();
    private static readonly Dictionary<Characters.Actions.Motion, string> MotionNames = new();
    private static readonly Dictionary<ushort, (int count, uint hash)> Fingerprints = new();
    private static readonly Dictionary<string, int> Effects = new(), Projectiles = new();
    private static readonly SortedDictionary<string, int> Names = new(StringComparer.Ordinal);

    public static bool On => _file != null;

    /// <summary>Starts next to the trace (same file name stem). <paramref name="toSession"/>: local time -> host clock.</summary>
    public static void Start(Func<double, double> toSession)
    {
        if (_file != null || CoopTrace.BasePath == null || !CoopLaunch.Current.FromLauncher)
            return; // a test tool: a player's detailed log (Coop/DetailedLog) is the trace alone
        try
        {
            _file = new StreamWriter(CoopTrace.BasePath + ".probe.jsonl", false, new UTF8Encoding(false));
            _toSession = toSession;
            if (!_patched)
            {
                _patched = true;
                var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".coop.probe");
                foreach (var type in new[] { typeof(EffectCensus), typeof(ProjectileCensus) })
                {
                    var result = Patches.PatchRegistry.Apply(harmony, type);
                    if (!result.Ok)
                        Plugin.Log.LogWarning($"[CoopTest] probe: {type.Name} not applied: {result.Error?.Message ?? "no method"}");
                }
            }
            Plugin.Log.LogInfo("[CoopTest] probe: " + CoopTrace.BasePath + ".probe.jsonl");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[CoopTest] probe not started: " + e.Message);
        }
    }

    public static void Stop()
    {
        try
        {
            _file?.Flush();
            _file?.Dispose();
        }
        catch (Exception)
        {
            // closing at quit
        }
        _file = null;
    }

    /// <summary>Every frame of an active session; writes a sample when the drawn moment crosses the grid.</summary>
    public static void Tick(double now, CoopRuntime runtime)
    {
        if (_file == null)
            return;
        try
        {
            double session = _toSession(now);
            var enemies = EnemySync.Current;
            double shown = ShownMoment(now);
            long grid = (long)Math.Floor(shown / Interval);
            if (grid != _lastGrid)
            {
                _lastGrid = grid;
                Sample(now, session, shown, grid, runtime, enemies);
            }
            if (now >= _nextCensus)
            {
                _nextCensus = now + CensusSeconds;
                Census(session);
            }
            if (now >= _nextFlush)
            {
                _nextFlush = now + 1;
                _file.Flush();
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    /// <summary>The moment of the host's clock this game shows: the host's own "now"; a client's drawn moment of the
    /// host's world (its enemies are drawn a little in the past; without any yet: about 0.15 s back).</summary>
    public static double ShownMoment(double now)
    {
        double session = _toSession(now);
        var enemies = EnemySync.Current;
        double render = enemies != null ? enemies.RenderTime : double.NaN;
        return double.IsNaN(render) ? session - (CoopWorld.Current?.IsClient == true ? 0.15 : 0) : render;
    }

    private static int _failures;

    private static void Fail(Exception e)
    {
        if (_failures++ < 3)
            Plugin.Log.LogWarning("[CoopTest] probe: " + e);
    }

    // ------------------------------------------------------------------ one sample

    private static void Sample(double now, double session, double shown, long grid, CoopRuntime runtime, EnemySync enemies)
    {
        var map = Map.Instance;
        var levels = Singleton<Service>.Instance?.levelManager;
        uint room = CoopWorld.CurrentRoom();
        if (room != _room)
        {
            _room = room;
            MotionNames.Clear();
            Fingerprints.Clear();
        }
        Line.Clear();
        Line.Append("{\"k\":").Append(grid);
        Num(",\"t\":", session, "0.000");
        Num(",\"lt\":", now, "0.000");
        Num(",\"rt\":", shown, "0.000");
        Line.Append(",\"room\":\"").Append(room.ToString("x8")).Append('"');
        if (CoopWorld.Current?.RoomLoaded != true)
            Line.Append(",\"loading\":1");
        Vector2 origin = map != null ? (Vector2)map.transform.position : Vector2.zero;

        // players: this game's own, then the teammates' copies (with the moment of their owner's clock that is drawn)
        Line.Append(",\"pl\":[");
        bool first = true;
        var player = levels?.player;
        if (player != null && map != null)
        {
            PlayerRecord(runtime.LocalSlot, player, origin, double.NaN, ref first);
            runtime.ForProbe((slot, copy, drawn) => PlayerRecord(slot, copy, origin, drawn, ref first));
        }
        Line.Append(']');

        // enemies with a net id
        Bound.Clear();
        enemies?.ForProbe(Bound);
        Line.Append(",\"en\":[");
        first = true;
        foreach (var (id, c, lifeOnly) in Bound)
        {
            if (c == null || c.health == null || c.health.dead || !c.gameObject.activeInHierarchy)
                continue;
            if (!first)
                Line.Append(',');
            first = false;
            EnemyRecord(id, c, lifeOnly, origin);
        }
        Line.Append(']');

        // the room: open doors, waves left, living enemies without a net id (run by each game alone)
        if (map != null)
        {
            int gates = 0;
            foreach (var gate in map.GetComponentsInChildren<Gate>())
                if (gate.isActiveAndEnabled && gate._collider != null && gate._collider.enabled)
                    gates++;
            Line.Append(",\"gates\":").Append(gates);
            if (map.waveContainer != null)
                Line.Append(",\"waves\":\"").Append(map.waveContainer.state).Append('"');
            Names.Clear();
            foreach (var c in map.GetComponentsInChildren<Character>())
            {
                if (c == null || c.health == null || c.health.dead || (enemies != null && enemies.IsBound(c)))
                    continue;
                if (c.type is Character.Type.TrashMob or Character.Type.Named or Character.Type.Adventurer or Character.Type.Boss
                    or Character.Type.Summoned)
                    Count(Names, Loadout.GearName(c.name));
            }
            if (Names.Count > 0)
            {
                Line.Append(",\"unbound\":");
                WriteCounts(Names);
            }
        }

        // the screen
        Line.Append(",\"ui\":\"");
        Ui();
        Line.Append('"');
        var camera = Scene<GameBase>.instance?.cameraController;
        if (camera != null && map != null)
        {
            Vector2 at = (Vector2)camera.transform.position - origin;
            Num(",\"cam\":[", at.x, "0.##");
            Num(",", at.y, "0.##");
            Line.Append(']');
        }
        Line.Append('}');
        _file.WriteLine(Line.ToString());
    }

    private static void PlayerRecord(int slot, Character c, Vector2 origin, double drawn, ref bool first)
    {
        if (c == null)
            return;
        if (!first)
            Line.Append(',');
        first = false;
        Vector2 p = (Vector2)c.transform.position - origin;
        Line.Append('[').Append(slot);
        Num(",", p.x, "0.##");
        Num(",", p.y, "0.##");
        Line.Append(',').Append(c.lookingDirection == Character.LookingDirection.Right ? 1 : 0);
        Num(",", c.health != null ? c.health.currentHealth : 0, "0");
        Line.Append(",\"").Append(MotionOf(c)).Append('"');
        if (!double.IsNaN(drawn))
            Num(",", drawn, "0.000");
        Line.Append(']');
    }

    [Flags]
    private enum F
    {
        Grounded = 1, Walk = 2, Stunned = 4, Frozen = 8, Burning = 16, Poisoned = 32, Wounded = 64, Invulnerable = 128,
        Cinematic = 256, BodyShown = 512, LifeOnly = 1024,
    }

    private static void EnemyRecord(ushort id, Character c, bool lifeOnly, Vector2 origin)
    {
        Vector2 p = (Vector2)c.transform.position - origin;
        var flags = lifeOnly ? F.LifeOnly : 0;
        var animation = c.animationController != null ? c.animationController.parameter : null;
        if (animation != null && animation.grounded) flags |= F.Grounded;
        if (animation != null && animation.walk) flags |= F.Walk;
        var status = c.status;
        if (status != null)
        {
            if (status.stuned) flags |= F.Stunned;
            if (status.freezed) flags |= F.Frozen;
            if (status.burning) flags |= F.Burning;
            if (status.poisoned) flags |= F.Poisoned;
            if (status.wounded) flags |= F.Wounded;
        }
        if (c.invulnerable.value) flags |= F.Invulnerable;
        if (c.cinematic.value) flags |= F.Cinematic;
        if (c.@base == null || c.@base.gameObject.activeInHierarchy) flags |= F.BodyShown;
        var motion = c.runningMotion;
        var target = lifeOnly ? null : CoopTargeting.TargetOf(c);

        Line.Append('[').Append(id);
        Num(",", p.x, "0.##");
        Num(",", p.y, "0.##");
        Line.Append(',').Append(c.lookingDirection == Character.LookingDirection.Right ? 1 : 0);
        Num(",", c.health.currentHealth, "0");
        Line.Append(',').Append((int)flags);
        Line.Append(",\"").Append(MotionOf(c)).Append('"');
        Num(",", motion != null ? motion.time : 0, "0.##");
        Line.Append(',').Append(target != null ? EnemySync.TraceSlotOf(target) : -1);

        // what is switched on below it (its body's parts, attack signs, auras): count + a hash of the names; the names
        // themselves once whenever they change
        var container = c.GetComponentInParent<Enemy>();
        var root = container != null ? container.gameObject : c.gameObject;
        Renderers.Clear();
        root.GetComponentsInChildren(false, Renderers);
        int count = 0;
        uint hash = 0;
        foreach (var r in Renderers)
        {
            if (r == null || !r.enabled || (r is SpriteRenderer s && s.sprite == null))
                continue;
            count++;
            hash += Fnv(r.gameObject.name);
        }
        Line.Append(',').Append(count).Append(',').Append(hash);
        if (!Fingerprints.TryGetValue(id, out var last) || last.count != count || last.hash != hash)
        {
            Fingerprints[id] = (count, hash);
            Names.Clear();
            foreach (var r in Renderers)
                if (r != null && r.enabled && !(r is SpriteRenderer s && s.sprite == null))
                    Count(Names, Loadout.GearName(r.gameObject.name));
            Line.Append(',');
            WriteCounts(Names, 60);
        }
        Line.Append(']');
    }

    private static string MotionOf(Character c)
    {
        var motion = c.runningMotion;
        if (motion == null)
            return "";
        if (MotionNames.TryGetValue(motion, out string name))
            return name;
        if (MotionNames.Count > 4000)
            MotionNames.Clear();
        name = motion.action == null ? "(no action) " + motion.name
            : RemoteAvatar.PathOf(motion.action.transform, c.transform) + "#" + Array.IndexOf(motion.action.motions, motion);
        return MotionNames[motion] = Escape(name);
    }

    /// <summary>The screen's state as a list of what is on: "hud,letterbox,input-blocked,time:0,boss:5,dark:2,...".</summary>
    private static void Ui()
    {
        var ui = Scene<GameBase>.instance?.uiManager;
        if (ui == null)
        {
            Line.Append("no-ui");
            return;
        }
        var hud = ui.headupDisplay;
        Flag(() => hud != null && hud.visible, "hud");
        Flag(() => ui.letterBox != null && ui.letterBox.visible, "letterbox");
        if (PlayerInput.blocked.value) Line.Append("input-blocked,");
        float scale = Chronometer.global.timeScale;
        if (Math.Abs(scale - 1f) > 0.01f)
            Num("time:", scale, "0.##").Append(',');
        // these two objects are always active; what shows is a container inside them
        Flag(() => ui.npcConversation != null && ui.npcConversation.gameObject.activeInHierarchy && ui.npcConversation.visible, "talk");
        Flag(() => ui.narration != null && ui.narration.gameObject.activeInHierarchy && ui.narration.textVisible, "narration");
        Flag(() => ui.narration != null && ui.narration.gameObject.activeInHierarchy && ui.narration.sceneVisible, "narration-scene");
        Shown(ui.systemDialogue, "dialogue");
        Shown(ui.gameResult, "result");
        Shown(ui.inventory, "inventory");
        Shown(ui.itemSelect, "item-select");
        Shown(ui.upgradeShop, "upgrades");
        Shown(ui.unlockNotice, "unlock");
        Shown(ui.endingCredit, "credits");
        Shown(ui.confirm, "confirm");
        try
        {
            var bars = hud != null ? hud.bossHealthBar : null;
            if (bars != null && bars._healthbars != null)
            {
                var array = bars._healthbars.Array;
                for (int i = 0; i < array.Length; i++)
                    if (array[i] != null && array[i].gameObject.activeInHierarchy)
                        Line.Append("boss:").Append(((global::UI.BossHealthbarController.Type)i).ToString()).Append(',');
            }
            var dark = hud != null ? hud.darkEnemyHealthBar : null;
            if (dark != null && dark.attached != null && dark.attached.Count > 0)
                Line.Append("dark:").Append(dark.attached.Count).Append(',');
        }
        catch (Exception)
        {
            // health bars that aren't set up yet (a client while the first scene loads)
        }
        Flag(() =>
        {
            var fade = Singleton<Service>.Instance?.fadeInOut;
            return fade != null && fade._fadeBackground != null && fade._fadeBackground.isActiveAndEnabled && fade._fadeBackground.color.a > 0.5f;
        }, "faded");
    }

    private static void Shown(Component c, string name)
    {
        try
        {
            if (c == null)
                return;
            // a component whose object isn't there yet (a client while the first scene loads, 2026-11-10)
            var owner = c.gameObject;
            if (owner != null && owner.activeInHierarchy)
                Line.Append(name).Append(',');
        }
        catch (Exception)
        {
            // a UI part that isn't there: the sample goes on without it
        }
    }

    private static void Flag(Func<bool> shown, string name)
    {
        try
        {
            if (shown())
                Line.Append(name).Append(',');
        }
        catch (Exception)
        {
            // a UI part that isn't set up yet: the sample goes on without it
        }
    }

    // ------------------------------------------------------------------ census of effects and projectiles

    private static void Census(double session)
    {
        if (Effects.Count == 0 && Projectiles.Count == 0 && TeammateProjectiles.Sent.Count == 0
            && TeammateProjectiles.ShownFromCopy.Count == 0 && TeammateProjectiles.Lost.Count == 0)
            return;
        Line.Clear();
        Line.Append("{\"census\":1");
        Num(",\"t\":", session, "0.000");
        Line.Append(",\"room\":\"").Append(_room.ToString("x8")).Append('"');
        Line.Append(",\"fx\":");
        Names.Clear();
        foreach (var pair in Effects) Names[pair.Key] = pair.Value;
        WriteCounts(Names, 400);
        Line.Append(",\"shots\":");
        Names.Clear();
        foreach (var pair in Projectiles) Names[pair.Key] = pair.Value;
        WriteCounts(Names, 400);
        // this game's player's shots as sent, the teammate's as shown from its copy, and those not shown (no prefab)
        Tallies(",\"sent\":", TeammateProjectiles.Sent);
        Tallies(",\"shown\":", TeammateProjectiles.ShownFromCopy);
        Tallies(",\"lost\":", TeammateProjectiles.Lost);
        Line.Append('}');
        Effects.Clear();
        Projectiles.Clear();
        _file.WriteLine(Line.ToString());
    }

    private static void Tallies(string key, Dictionary<string, int> counts)
    {
        if (counts.Count == 0)
            return;
        Line.Append(key);
        Names.Clear();
        foreach (var pair in counts) Names[pair.Key] = pair.Value;
        WriteCounts(Names, 400);
        counts.Clear();
    }

    /// <summary>Every effect the game shows goes through the effect pool: counted by its animation's name.</summary>
    [HarmonyPatch(typeof(FX.EffectPool), nameof(FX.EffectPool.Play))]
    public static class EffectCensus
    {
        private static void Postfix(RuntimeAnimatorController animation)
        {
            if (_file != null && animation != null && Effects.Count < 2000)
                Count(Effects, animation.name);
        }
    }

    /// <summary>Every projectile fired: counted by its name and whose it is (e = an enemy's, p = a player's or a
    /// teammate copy's, o = other).</summary>
    [HarmonyPatch(typeof(Characters.Projectiles.Projectile), nameof(Characters.Projectiles.Projectile.Fire))]
    public static class ProjectileCensus
    {
        private static void Prefix(Characters.Projectiles.Projectile __instance, Character owner)
        {
            if (_file == null || __instance == null || Projectiles.Count >= 2000 || !TeammateProjectiles.Shown(owner))
                return; // a copy's own shot isn't shown (its owner's game sends the real one)
            char whose = owner == null ? 'o'
                : owner.type is Character.Type.Player or Character.Type.PlayerMinion ? 'p'
                : owner.type is Character.Type.Trap or Character.Type.Dummy ? 'o' : 'e';
            Count(Projectiles, whose + ":" + Loadout.GearName(__instance.name));
        }
    }

    // ------------------------------------------------------------------ writing

    private static void Count<TDictionary>(TDictionary counts, string name) where TDictionary : IDictionary<string, int>
    {
        counts.TryGetValue(name, out int n);
        counts[name] = n + 1;
    }

    private static void WriteCounts(SortedDictionary<string, int> counts, int most = 40)
    {
        Line.Append('{');
        int written = 0;
        foreach (var pair in counts)
        {
            if (written++ >= most)
                break;
            if (written > 1)
                Line.Append(',');
            Line.Append('"').Append(Escape(pair.Key)).Append("\":").Append(pair.Value);
        }
        Line.Append('}');
    }

    private static StringBuilder Num(string prefix, double value, string format)
    {
        Line.Append(prefix);
        if (double.IsNaN(value) || double.IsInfinity(value))
            return Line.Append("null");
        return Line.Append(value.ToString(format, CultureInfo.InvariantCulture));
    }

    private static string Escape(string text)
    {
        if (text.All(c => c >= ' ' && c != '"' && c != '\\'))
            return text;
        var clean = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c >= ' ' && c != '"' && c != '\\')
                clean.Append(c);
        return clean.ToString();
    }

    private static uint Fnv(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }
}
