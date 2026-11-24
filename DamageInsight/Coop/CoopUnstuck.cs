using Characters.Controllers;
using DamageInsight.Lang;
using DamageInsight.UI;
using HarmonyLib;
using Level;
using Scenes;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Unstuck (COOP_PLAN 4.8: "teleport to a teammate, once every 10 s"): the key (Coop/UnstuckKey, F6, like the room
/// restart's F5) puts this game's player where the nearest teammate stands in this room. The camera goes along:
/// it jumps to the new spot, and the room's camera zone becomes the one there (the game changes zones only when
/// the player walks through a zone's trigger, which a teleport skips: the camera would stay in the old zone). Not
/// while down, loading or in a dialogue. Since COOP_LOG 64 also a rescue (see <see cref="Go"/>): a hidden player is
/// made whole, and controls held without a dialogue can be freed with a second press.
/// </summary>
// REVIEW(fable): letting go of PlayerInput.blocked holders is a forced fix (trigger 6); only on two key presses.
internal static class CoopUnstuck
{
    private const float CooldownSeconds = 10f;
    private static float _next;

    /// <summary>Every frame from the co-op runtime.</summary>
    public static void Tick()
    {
        WatchTerrain();
        if (Plugin.CoopUnstuckKey == null || !Plugin.CoopUnstuckKey.Value.IsDown())
            return;
        if (TextInput.IsTyping() || SettingsPage.IsOpen || Codex.CodexWindow.IsOpen || CombatLogWindow.IsOpen)
            return;
        var world = CoopWorld.Current;
        if (world?.Active != true)
            return;
        string why = Go(world, key: true);
        if (why != null)
            Notice.Show("unstuck", why, 4f);
    }

    /// <summary>The test bots (CoopAutoRun): the key's effect without the key. Null: done, else why not.</summary>
    internal static string ForTest() => CoopWorld.Current is { Active: true } world ? Go(world, key: false) : "no session";

    /// <summary>Second press within this time frees the controls (the first one names what holds them).</summary>
    private const float FreeConfirmSeconds = 3f;
    private static float _freeArmedUntil;

    /// <summary>
    /// Gets this player going again (COOP_LOG 64, the chapter 4 cannon lock): first a hidden player is made whole
    /// (<see cref="PlayerWatch.Repair"/>); controls held by destroyed objects are let go; controls held by something
    /// else without a dialogue are named on the first press and let go on a second press (only from the key, never by
    /// the test bots); then the player goes to the nearest teammate. Returns a notice text, or null.
    /// </summary>
    private static string Go(CoopWorld world, bool key)
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        var revive = CoopRevive.Current;
        // down first: a player who is down counts as dead to the game too (the first bot runs: "wait until loaded")
        if (player != null && revive != null && revive.IsDown(player))
            return Loc.T("Not while you are down.");
        if (player == null || player.health == null || player.health.dead || world.Busy || !world.RoomLoaded || Map.Instance == null)
            return Loc.T("Please wait until the room has loaded.");
        if (global::UI.Dialogue.anyDialogueOpened)
            return null; // a dialogue has the controls: the key means nothing now
        uint room = CoopWorld.CurrentRoom();
        string repaired = PlayerWatch.Repair(player);
        if (repaired != null)
        {
            CoopTrace.Write("unstuck-repair", room, what: repaired);
            Plugin.Log.LogWarning("[Coop] unstuck: the player was hidden, fixed: " + repaired);
            Notice.Show("unstuck-repair", Loc.T("You are visible again."), 4f);
        }
        if (PlayerInput.blocked.value)
        {
            int stale = PlayerWatch.FreeDestroyedHolders();
            if (stale > 0)
            {
                CoopTrace.Write("unstuck-free", room, what: "destroyed holders", amount: stale);
                Plugin.Log.LogWarning("[Coop] unstuck: let go of " + stale + " destroyed control holder(s)");
            }
        }
        if (PlayerInput.blocked.value)
        {
            if (!key)
                return null; // a story scene has the controls; the bots wait for it
            float t = Time.unscaledTime;
            string holders = PlayerWatch.HolderNames();
            if (t < _freeArmedUntil)
            {
                _freeArmedUntil = 0;
                PlayerWatch.FreeAllHolders();
                CoopTrace.Write("unstuck-free", room, what: holders);
                Plugin.Log.LogWarning("[Coop] unstuck: the controls were held by " + holders + " (for " +
                                      PlayerWatchLogic.Span(PlayerWatch.BlockedFor) + "); let go");
                return Loc.T("The controls are free again.");
            }
            _freeArmedUntil = t + FreeConfirmSeconds;
            CoopTrace.Write("unstuck-blocked", room, what: holders);
            return Loc.T("Something holds the controls (a scene?). Press the key again within 3 s to free them.");
        }
        float now = Time.unscaledTime;
        if (now < _next)
            return repaired != null ? null : Loc.F("You can go to a teammate again in {0} s.", Mathf.CeilToInt(_next - now));
        var target = ToTeammate(player);
        if (target == null)
            return repaired != null ? null : Loc.T("No teammate in this room.");
        _next = now + CooldownSeconds;
        CoopTrace.Write("unstuck", CoopWorld.CurrentRoom(), what: CoopTrace.NameOf(target));
        return null;
    }

    /// <summary>Puts <paramref name="player"/> where the nearest teammate copy in this room stands, camera along.
    /// Returns that copy, or null when there is none.</summary>
    private static Characters.Character ToTeammate(Characters.Character player)
    {
        Characters.Character target = null;
        float best = float.MaxValue;
        foreach (var copy in CoopCopies.All)
        {
            if (copy == null || !copy.gameObject.activeInHierarchy)
                continue;
            float d = ((Vector2)(copy.transform.position - player.transform.position)).sqrMagnitude;
            if (d < best)
            {
                best = d;
                target = copy;
            }
        }
        if (target == null)
            return null;
        Vector3 to = target.transform.position;
        player.transform.position = new Vector3(to.x, to.y, player.transform.position.z);
        Physics2D.SyncTransforms();
        FollowCamera(to);
        return target;
    }

    // ------------------------------------------------------------------ stuck inside a wall

    /// <summary>A time inside the terrain counts from this long (the game's own StuckResolver waits 1 s too).</summary>
    private const float EpisodeSeconds = 0.8f;
    /// <summary>The game checks every 3 s and moves a stuck player after 1 s more: past this, its fix didn't come.</summary>
    private const float LongSeconds = 5f;
    /// <summary>Two times inside the terrain this close: the game's fix put the player back into the same wall.</summary>
    private const float AgainSeconds = 15f;
    private static float _insideSince = -1f;
    private static bool _counted;
    private static float _lastTime = -100f;
    private static int _times;
    private static Characters.Character _resolverOf;
    private static Characters.Movements.StuckResolver _resolver;

    /// <summary>
    /// Every frame: this game's player stuck inside the terrain, and the game's own fix (StuckResolver: back to the
    /// last spot outside, every 3 s) didn't get him out, twice in 15 s or for 5 s on end: he goes to the nearest
    /// teammate, like F6 (Coop/AutoUnstuck). US tester's run 2026-11-22 (COOP_LOG 65): a wall the room switched on
    /// around the host; the game put him back six times, into the same wall (its last spot outside was in it too).
    /// </summary>
    private static void WatchTerrain()
    {
        if (Plugin.CoopAutoUnstuck?.Value != true || CoopCopies.Count == 0)
            return;
        var world = CoopWorld.Current;
        var player = Singleton<Service>.Instance?.levelManager?.player;
        var controller = player != null && player.movement != null ? player.movement.controller : null;
        if (world?.Active != true || controller == null || world.Busy || !world.RoomLoaded || player.health == null ||
            player.health.dead || (CoopRevive.Current?.IsDown(player) ?? false) || PlayerInput.blocked.value ||
            global::UI.Dialogue.anyDialogueOpened || player.collider == null || !player.collider.enabled ||
            ResolverStopped(player) || !controller.IsInTerrain())
        {
            _insideSince = -1f;
            return;
        }
        float now = Time.unscaledTime;
        if (_insideSince < 0f)
        {
            _insideSince = now;
            _counted = false;
            return;
        }
        float lasted = now - _insideSince;
        if (!_counted && lasted >= EpisodeSeconds)
        {
            _counted = true;
            _times = now - _lastTime <= AgainSeconds ? _times + 1 : 1;
            _lastTime = now;
        }
        if (_times < 2 && lasted < LongSeconds)
            return;
        _times = 0;
        _insideSince = -1f;
        var target = ToTeammate(player);
        if (target == null)
            return;
        _next = now + CooldownSeconds;
        CoopTrace.Write("unstuck-auto", CoopWorld.CurrentRoom(), what: CoopTrace.NameOf(target), amount: lasted);
        Plugin.Log.LogWarning("[Coop] unstuck: stuck inside the terrain (" + lasted.ToString("0.0") +
                              " s, the game's fix didn't help): moved to the teammate");
        Notice.Show("unstuck", Loc.T("You were stuck inside a wall: you are with your teammate now."), 4f);
    }

    /// <summary>The game switches its stuck check off for some moves (StopPlayerStuckResolver): then so does this.</summary>
    private static bool ResolverStopped(Characters.Character player)
    {
        if (_resolverOf != player)
        {
            _resolverOf = player;
            _resolver = player.GetComponent<Characters.Movements.StuckResolver>();
        }
        return _resolver != null && _resolver.stop.value;
    }

    /// <summary>The camera zone around <paramref name="at"/> becomes the room's, and the cameras jump there.</summary>
    private static void FollowCamera(Vector3 at)
    {
        var map = Map.Instance;
        foreach (var changer in map.GetComponentsInChildren<CameraZoneChanger>())
        {
            var area = changer.GetComponent<Collider2D>();
            if (area != null && area.enabled && changer._cameraZone != null && area.OverlapPoint(at))
            {
                map.cameraZone = changer._cameraZone;
                break;
            }
        }
        map.SetCameraZoneOrDefault();
        var scene = Scene<GameBase>.instance;
        if (scene == null)
            return;
        scene.cameraController?.Move(at);
        scene.minimapCameraController?.Move(at);
    }

    /// <summary>
    /// A teammate's copy is a Player character on this game, and the camera zones answered every Player that walks
    /// into their trigger: the teammate walking into another zone moved THIS player's camera there. Only this
    /// game's own player changes its camera zone.
    /// </summary>
    [HarmonyPatch(typeof(CameraZoneChanger), "OnTriggerEnter2D")]
    internal static class OwnCameraZone
    {
        private static bool Prefix(Collider2D collision) =>
            collision == null || !CoopCopies.IsCopy(collision.GetComponent<Characters.Character>());
    }
}
