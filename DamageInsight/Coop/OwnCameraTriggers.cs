using Characters;
using HarmonyLib;
using Level.Adventurer;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// The camera triggers of a room that pin the camera on a fixed spot answer every Player character that walks in, and a
/// teammate's copy is one. US tester's run 2026-11-22 (COOP_LOG 65): the client walked into the Dark Mirror chapter 4
/// veteran adventurer's arena while the host was down in the secret dark quartz room below it; the host's camera
/// stayed on the arena (<see cref="AdventurerCameraZone"/> tracks its fixed target until the fight's wave is
/// cleared) and the host saw nothing of himself until F6 put him next to the client. Like the camera zones
/// (<see cref="CoopUnstuck.OwnCameraZone"/>), only this game's own player moves this game's camera here:
/// <list type="bullet">
/// <item><see cref="AdventurerCameraZone"/>: entered by a copy, nothing happens; entered by this player after the
/// fight's wave is cleared, nothing either (alone a player is inside when it starts; in co-op the player who stayed out
/// would come in afterwards, and the game's release at the clear has already run: the camera would stay pinned).</item>
/// <item><see cref="CameraTargetChanger"/> (a spot the camera follows while a player stands in its box): a copy's
/// walking in or out does nothing.</item>
/// </list>
/// </summary>
// REVIEW(fable): a Harmony patch on game camera triggers (trigger 3); built from the game code and the report's
// trace times, not yet proven by a game run.
internal static class OwnCameraTriggers
{
    private static bool IsCopy(Collider2D collision) =>
        collision != null && CoopCopies.Count > 0 && CoopCopies.IsCopy(collision.GetComponent<Character>());

    [HarmonyPatch(typeof(AdventurerCameraZone), "OnTriggerEnter2D")]
    internal static class AdventurerArena
    {
        private static bool Prefix(AdventurerCameraZone __instance, Collider2D collision)
        {
            if (CoopWorld.Current?.Active != true)
                return true;
            if (IsCopy(collision))
            {
                Trace(__instance, "copy");
                return false;
            }
            var wave = __instance._enemyWave;
            if (wave != null && wave.state == Wave.State.Cleared && collision != null &&
                collision.GetComponent<Character>()?.type == Character.Type.Player)
            {
                Trace(__instance, "fight over");
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(CameraTargetChanger), "OnTriggerEnter2D")]
    internal static class TargetEnter
    {
        private static bool Prefix(CameraTargetChanger __instance, Collider2D collision)
        {
            if (CoopWorld.Current?.Active != true || !IsCopy(collision))
                return true;
            Trace(__instance, "copy in");
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraTargetChanger), "OnTriggerExit2D")]
    internal static class TargetExit
    {
        private static bool Prefix(Collider2D collision) => CoopWorld.Current?.Active != true || !IsCopy(collision);
    }

    // ------------------------------------------------------------------ the arena camera stays on (COOP_LOG 68)

    /// <summary>
    /// Every frame from the co-op runtime (checks 4 times a second): while an adventurer arena's fight runs and this
    /// game's player stands inside the arena (the zone's box is the whole arena: 19 x 20 around the camera point,
    /// wall to wall), the camera is on the arena's point, as alone. Ratvibing's run 2026-11-23: Umut (host, 0.17.1)
    /// had the camera on himself in the adventurer fights ("not locked onto the arena anymore"); the game pins only
    /// in OnTriggerEnter2D of the FIRST player character in (see <see cref="Inside"/>), and since 65 a copy's entry is
    /// refused here, so a teammate walking in first left this player's camera free (reproduced by the bot start
    /// check). Not while a scene holds the controls (an intro moves the camera itself) or this player is down (the
    /// revive camera watches a teammate).
    /// </summary>
    // REVIEW(fable): drives the game camera from a poll (trigger 3); the trace's camera rows show what took it off.
    internal static void Tick()
    {
        if (CoopWorld.Current?.Active != true)
            return;
        float now = Time.unscaledTime;
        if (now < _nextCheck)
            return;
        _nextCheck = now + 0.25f;
        var map = Level.Map.Instance;
        if (map == null)
            return;
        if (map != _zonesOf)
        {
            _zonesOf = map;
            _zones = map.GetComponentsInChildren<AdventurerCameraZone>(true);
            _repins = 0;
        }
        if (_zones.Length == 0)
            return;
        var player = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player;
        if (player == null || !CoopTargeting.Up(player) || Characters.Controllers.PlayerInput.blocked.value)
            return;
        var camera = Scenes.Scene<Scenes.GameBase>.instance?.cameraController;
        if (camera == null)
            return;
        foreach (var zone in _zones)
        {
            if (zone == null || zone._cameraTarget == null || zone._enemyWave == null || zone._startTrigger == null
                || zone._enemyWave.state != Wave.State.Spawned || camera._targetToTrack == zone._cameraTarget
                || !zone.isActiveAndEnabled || !Inside(zone._startTrigger, player.transform.position))
                continue;
            string before = camera._targetToTrack != null ? camera._targetToTrack.name : "nothing";
            zone.EnableCameraZone();
            if (_repins++ < 20)
                CoopTrace.Write("camera-pinned", CoopWorld.CurrentRoom(), name: zone.name,
                    what: "this player in the arena, the camera was on " + before);
            break;
        }
    }

    /// <summary>
    /// The point inside the box, from its size and place (not the physics query: the arena's other script,
    /// AdventurerCombatArea, switches this same box off at the FIRST player character that walks in, closing the side
    /// walls; OverlapPoint on a switched-off collider always says no). That is also why the game's camera zone hears
    /// only the first one: when the teammate's copy came first (refused here), this player's own entry never came.
    /// Bot start check 2026-11-24 (COOP_LOG 68): the host's camera followed him through the whole ch1 adventurer fight.
    /// </summary>
    private static bool Inside(BoxCollider2D box, Vector2 point)
    {
        var t = box.transform;
        Vector2 center = t.TransformPoint(box.offset);
        Vector2 scale = t.lossyScale;
        float halfX = Mathf.Abs(box.size.x * scale.x) * 0.5f, halfY = Mathf.Abs(box.size.y * scale.y) * 0.5f;
        return Mathf.Abs(point.x - center.x) <= halfX && Mathf.Abs(point.y - center.y) <= halfY;
    }

    private static float _nextCheck;
    private static Level.Map _zonesOf;
    private static AdventurerCameraZone[] _zones = new AdventurerCameraZone[0];
    private static int _repins;

    /// <summary>
    /// The recorder's camera rows (COOP_LOG 65e named the gap, 68 needed it): every change of what the game camera
    /// follows, with the code that asked for it ("camera-track", 40 a room at most).
    /// </summary>
    [HarmonyPatch(typeof(CameraController), nameof(CameraController.StartTrack))]
    internal static class TrackTrace
    {
        private static uint _room;
        private static int _rows;

        private static void Prefix(CameraController __instance, Transform target)
        {
            try
            {
                if (CoopWorld.Current?.Active != true || target == null || __instance._targetToTrack == target
                    || __instance != Scenes.Scene<Scenes.GameBase>.instance?.cameraController)
                    return;
                uint room = CoopWorld.CurrentRoom();
                if (room != _room)
                {
                    _room = room;
                    _rows = 0;
                }
                if (_rows++ < 40)
                    CoopTrace.Write("camera-track", room, name: target.name, what: Caller());
            }
            catch (System.Exception)
            {
                // the trace must never stop the camera
            }
        }

        private static string Caller()
        {
            var frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
            if (frames == null)
                return "?";
            for (int i = 0; i < frames.Length && i < 10; i++)
            {
                var method = frames[i].GetMethod();
                var type = method?.DeclaringType;
                if (type == null || type == typeof(CameraController) || type == typeof(TrackTrace)
                    || (type.Namespace ?? "").StartsWith("HarmonyLib", System.StringComparison.Ordinal))
                    continue;
                var outer = type.DeclaringType != null ? type.DeclaringType.Name + "/" : "";
                return outer + type.Name + "." + method.Name;
            }
            return "?";
        }
    }

    private static int _traced;

    private static void Trace(Component trigger, string why)
    {
        if (_traced++ < 40)
            CoopTrace.Write("camera-refused", CoopWorld.CurrentRoom(), name: trigger != null ? trigger.name : "?", what: why);
    }
}
