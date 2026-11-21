using System;
using System.Collections;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Pictures of what each game of a co-op test shows, in pairs (`--skulinsight-shots=SECONDS`): every game takes one
/// when the moment it draws crosses the same grid of the host's clock (like the probe's samples), so picture N of the
/// host and picture N of a client show the same moment and tools/coop/sync-report.py can put them side by side. The
/// game draws them itself (the frame it just rendered), so it works with the windows covered or moved off screen.
/// `--skulinsight-cam-host`: every game's camera stays on the HOST's player (a client: on the host's copy), without
/// the camera's smoothing and shake, so both pictures show the same part of the room. Test copies only.
/// Files: `<copy data>/CoopTrace/<start>_<side>.shots/<N>_<room>.jpg`.
/// </summary>
internal static class CoopShots
{
    private static double _interval;
    private static long _lastGrid = long.MinValue;
    private static bool _busy, _failed;
    private static MonoBehaviour _runner;
    private static string _folder;

    public static bool SharedCamera { get; private set; }

    public static void Start(MonoBehaviour runner, CoopLaunch launch)
    {
        _runner = runner;
        _interval = launch.ShotSeconds;
        if (launch.CameraOnHost && !SharedCamera)
        {
            SharedCamera = true;
            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".coop.shots");
            var result = Patches.PatchRegistry.Apply(harmony, typeof(CameraOnHost));
            Plugin.Log.LogInfo(result.Ok ? "[CoopTest] camera: on the host's player in every copy"
                : "[CoopTest] camera patch not applied: " + (result.Error?.Message ?? "no method"));
            result = Patches.PatchRegistry.Apply(harmony, typeof(SameBackground));
            if (!result.Ok)
                Plugin.Log.LogWarning("[CoopTest] background patch not applied: " + (result.Error?.Message ?? "no method"));
        }
    }

    /// <summary>Every frame of an active session: a picture when the drawn moment crosses the grid.</summary>
    public static void Tick(double shown)
    {
        if (_interval <= 0 || _runner == null || CoopTrace.BasePath == null || _busy || _failed)
            return;
        long grid = (long)Math.Floor(shown / _interval);
        if (grid == _lastGrid)
            return;
        _lastGrid = grid;
        if (CoopWorld.Current?.RoomLoaded != true)
            return; // loading screens: nothing to compare
        try
        {
            if (_folder == null)
            {
                _folder = CoopTrace.BasePath + ".shots";
                Directory.CreateDirectory(_folder);
            }
            string name = grid.ToString("000000", CultureInfo.InvariantCulture) + "_" + CoopWorld.CurrentRoom().ToString("x8") + ".jpg";
            _busy = true;
            _runner.StartCoroutine(Capture(Path.Combine(_folder, name)));
        }
        catch (Exception e)
        {
            _busy = false;
            _failed = true;
            Plugin.Log.LogWarning("[CoopTest] pictures off: " + e.Message);
        }
    }

    private static int _extras;

    /// <summary>One more picture right now, outside the grid: something short worth seeing (the revive ring half
    /// full, its flash). Goes to `&lt;stem&gt;.extra/`, so the report's pairs stay as they are.</summary>
    public static void Extra(string tag)
    {
        if (_interval <= 0 || _runner == null || CoopTrace.BasePath == null || _busy || _failed || _extras >= 400)
            return;
        try
        {
            string folder = CoopTrace.BasePath + ".extra";
            Directory.CreateDirectory(folder);
            string name = (++_extras).ToString("0000", CultureInfo.InvariantCulture) + "_" + tag + ".jpg";
            _busy = true;
            _runner.StartCoroutine(Capture(Path.Combine(folder, name)));
        }
        catch (Exception e)
        {
            _busy = false;
            Plugin.Log.LogWarning("[CoopTest] no extra picture: " + e.Message);
        }
    }

    private static IEnumerator Capture(string path)
    {
        yield return new WaitForEndOfFrame();
        Texture2D picture = null;
        try
        {
            picture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
            File.WriteAllBytes(path, picture.EncodeToJPG(80));
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("[CoopTest] pictures off: " + e.Message);
        }
        finally
        {
            if (picture != null)
                UnityEngine.Object.Destroy(picture);
            _busy = false;
        }
    }

    /// <summary>The host's player as this game has it: its own player (host) or the host's copy (client); null: not here.</summary>
    private static Characters.Character HostPlayer()
    {
        var world = CoopWorld.Current;
        if (world == null || !world.Active)
            return null;
        if (world.IsHost)
            return Singleton<Service>.Instance?.levelManager?.player;
        var copy = CoopRuntime.Instance != null ? CoopRuntime.Instance.CopyOf(0) : null;
        return copy != null && copy.gameObject.activeInHierarchy ? copy : null;
    }

    /// <summary>While the game's camera follows this game's player, it is put on the host's player instead: the same
    /// spot on every game (clamped to the room like the game does), no smoothing, no shake. The camera's step of the
    /// frame (`delta`) is set to the way it really went: the backgrounds scroll by it (ParallaxBackground), and
    /// without it the sky sat at another height on each game (the first paired pictures, 2026-11-10).</summary>
    [HarmonyPatch(typeof(CameraController), nameof(CameraController.Update))]
    public static class CameraOnHost
    {
        private static void Prefix(CameraController __instance, out Vector3 __state) => __state = __instance._position;

        private static void Postfix(CameraController __instance, Vector3 __state)
        {
            try
            {
                if (!SharedCamera || __instance.pause)
                    return;
                var own = Singleton<Service>.Instance?.levelManager?.player;
                if (own == null || __instance._targetToTrack != own.transform)
                    return; // a scene moves the camera, or it follows something else
                var host = HostPlayer();
                if (host == null)
                    return;
                Vector3 target = host.transform.position + __instance._offset;
                target.z = __instance._position.z;
                Vector3 position = __instance.zone != null ? __instance.zone.GetClampedPosition(__instance._camera, target) : target;
                __instance._position = position;
                __instance._delta = position - __state;
                __instance.transform.position = position;
            }
            catch (Exception)
            {
                // a test camera never breaks the game
            }
        }
    }

    /// <summary>A room's background layers start at a random sideways offset on every game (ParallaxBackground's
    /// `_randomize`): with the shared test camera they start at the same one, so paired pictures can be compared.</summary>
    [HarmonyPatch(typeof(Level.ParallaxBackground), nameof(Level.ParallaxBackground.Awake))]
    public static class SameBackground
    {
        private static void Prefix(out UnityEngine.Random.State __state)
        {
            __state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(20261110);
        }

        private static void Postfix(UnityEngine.Random.State __state) => UnityEngine.Random.state = __state;
    }
}
