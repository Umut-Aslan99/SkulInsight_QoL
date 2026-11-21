using System;
using System.Collections;
using System.Text;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace DamageInsight.Coop.Match;

/// <summary>
/// Steam's ping location and its own estimate (MATCHMAKING.md, "What the mod must do" 2 and 5), through the game's
/// Steamworks.NET 14.0 (Platforms.Steam.dll: GetLocalPingLocation(out), ConvertPingLocationToString(ref, out, int),
/// ParsePingLocationString(string, out), EstimatePingTimeFromLocalHost(ref) all exist there; checked 2026-11-16). Every
/// call is guarded: no Steam = "" / -1. The relay network is started by SteamTransport.WarmUp (CheckPingDataUpToDate):
/// this Steamworks.NET has no InitRelayNetworkAccess.
/// </summary>
// REVIEW(fable): these four Steam calls never ran (no Steam test yet): the string's size (1024), the age check.
internal static class SteamPing
{
    /// <summary>This PC's ping location as text, or "" while Steam has none yet (the age is negative) or no Steam.</summary>
    public static string LocalLocation()
    {
        try
        {
            if (!SteamAPI.IsSteamRunning())
                return "";
            float age = SteamNetworkingUtils.GetLocalPingLocation(out var location);
            if (age < 0)
            {
                SteamNetworkingUtils.CheckPingDataUpToDate(60f); // ask for it (again); the next search has it
                return "";
            }
            SteamNetworkingUtils.ConvertPingLocationToString(ref location, out string text, 1024);
            return text ?? "";
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning("[Coop] matchmaking: no Steam ping location (" + e.GetType().Name + ": " + e.Message + ")");
            return "";
        }
    }

    /// <summary>Asks Steam to measure its relays now (the ping location is ready a few seconds later): without it the
    /// first search after starting the game often had none, and the server counts an unknown ping as 250 ms.</summary>
    public static void WarmUp()
    {
        try
        {
            if (SteamAPI.IsSteamRunning())
                SteamNetworkingUtils.CheckPingDataUpToDate(60f);
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning("[Coop] matchmaking: Steam's ping measurement didn't start (" + e.GetType().Name + ")");
        }
    }

    /// <summary>Steam's estimate from this PC to that ping location in ms, or -1 (no Steam, a text it can't read).</summary>
    public static int Estimate(string pingLocation)
    {
        if (string.IsNullOrEmpty(pingLocation))
            return -1;
        try
        {
            if (!SteamAPI.IsSteamRunning() || !SteamNetworkingUtils.ParsePingLocationString(pingLocation, out var remote))
                return -1;
            int ms = SteamNetworkingUtils.EstimatePingTimeFromLocalHost(ref remote);
            return ms >= 0 ? ms : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}

/// <summary>The matchmaking client's HTTP over UnityWebRequest (like the daily run's DailyService): coroutines on the
/// co-op runtime; the answer is handed back on the main thread.</summary>
internal sealed class UnityMatchHttp : IMatchHttp
{
    private readonly MonoBehaviour _host;
    private readonly Func<string> _server;

    public UnityMatchHttp(MonoBehaviour host, Func<string> server)
    {
        _host = host;
        _server = server;
    }

    public void Send(string method, string path, string body, int timeoutSeconds, Action<long, string> done)
    {
        if (_host == null)
            return;
        _host.StartCoroutine(Run(method, (_server() ?? "").TrimEnd('/') + path, body, timeoutSeconds, done));
    }

    private static IEnumerator Run(string method, string url, string body, int timeout, Action<long, string> done)
    {
        using var request = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer() };
        if (body != null)
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");
        }
        request.timeout = timeout;
        request.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return request.SendWebRequest();
        long code = request.result is UnityWebRequest.Result.Success or UnityWebRequest.Result.ProtocolError ? request.responseCode : 0;
        string text = request.downloadHandler?.text ?? "";
        DamageInsight.Patches.Guard.Run("Co-op matchmaking (answer)", () => done(code, text));
    }
}
