using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace DamageInsight.Coop.Net;

/// <summary>
/// The only code that calls Steamworks for co-op: Steam Networking Sockets P2P through the game's Steamworks.NET 14.0
/// (Platforms.Steam.dll; the game starts Steam and runs its callbacks every frame). Every call is guarded: Steam
/// missing or not started gives "failed" answers instead of exceptions. Not testable without Steam (see the in-game
/// test list in the co-op docs): <see cref="SteamTransport"/>'s logic is tested with a fake instead.
/// Notes on this Steamworks.NET version: there is no InitRelayNetworkAccess yet (relay access starts on first use;
/// <see cref="WarmUp"/> nudges it), GetQuickConnectionStatus gives ping and quality, and SteamNetworkingMessage_t's own
/// Release() passes the wrong pointer (a bug of 14.0.0), so messages are released through the flat API directly.
/// </summary>
internal sealed class SteamworksSockets : ISteamSockets
{
    private Callback<SteamNetConnectionStatusChangedCallback_t> _callback;
    private readonly IntPtr[] _messages = new IntPtr[1];

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SteamAPI_SteamNetworkingMessage_t_Release")]
    private static extern void ReleaseMessage(IntPtr message);

    private SteamworksSockets()
    {
        _callback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
    }

    /// <summary>null when Steam isn't there (another platform's build, Steam not started).</summary>
    public static SteamworksSockets TryCreate()
    {
        try
        {
            if (!SteamAPI.IsSteamRunning())
                return null;
            return new SteamworksSockets();
        }
        catch (Exception e)
        {
            Warn("Steam networking is not available", e);
            return null;
        }
    }

    public static void WarmUp()
    {
        try
        {
            if (!SteamAPI.IsSteamRunning())
                return;
            SteamNetworkingSockets.InitAuthentication(); // P2P needs Steam's certificate; ask for it now
            SteamNetworkingUtils.CheckPingDataUpToDate(60f); // starts the relay network and ping measurement
        }
        catch (Exception e)
        {
            Warn("Steam networking warm-up failed", e);
        }
    }

    private static void Warn(string what, Exception e)
    {
        try
        {
            Plugin.Log?.LogWarning($"Co-op: {what} ({e.GetType().Name}: {e.Message})");
        }
        catch (Exception)
        {
            // No plugin (tests): nothing to log to.
        }
    }

    public Action<SteamStatusChange> StatusChanged { private get; set; }

    private void OnStatus(SteamNetConnectionStatusChangedCallback_t c)
    {
        try
        {
            var info = c.m_info;
            StatusChanged?.Invoke(new SteamStatusChange(c.m_hConn.m_HSteamNetConnection, (SteamConnState)(int)info.m_eState,
                info.m_identityRemote.GetSteamID64(), info.m_hListenSocket != HSteamListenSocket.Invalid,
                info.m_eEndReason, info.m_szEndDebug));
        }
        catch (Exception e)
        {
            Warn("a connection status could not be read", e);
        }
    }

    public ulong OwnId
    {
        get
        {
            try
            {
                return SteamUser.GetSteamID().m_SteamID;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Steam closes a connected line that hears nothing for 10 s (its default). The disconnect manager (package DC)
    /// shows the problem banner after 3 s and drops at DropSeconds (20 s), then tries to rejoin: with Steam's own
    /// 10 s, every blip of 10-20 s became a drop + rejoin instead of the banner alone (DC.md risk 3). So Steam waits
    /// 10 s longer than our drop and our session's timeout decides. Global, set before each new socket (the game
    /// itself uses no Steam networking). Connecting keeps Steam's 10 s (a join try that gets no answer retries).
    /// </summary>
    // REVIEW(fable): Steam call never run with a real connection, REVIEW_QUEUE 12.
    private static void KeepLinesOpen()
    {
        try
        {
            float drop = Plugin.CoopDropSeconds?.Value ?? (float)Rules.DropSettings.DefaultDropSeconds;
            int milliseconds = (int)(Math.Max(30f, Math.Min(130f, drop + 10f)) * 1000f);
            var value = GCHandle.Alloc(milliseconds, GCHandleType.Pinned);
            try
            {
                if (!SteamNetworkingUtils.SetConfigValue(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected,
                        ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero,
                        ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, value.AddrOfPinnedObject()))
                    Plugin.Log?.LogWarning("Co-op: Steam did not take the connection timeout");
            }
            finally
            {
                value.Free();
            }
        }
        catch (Exception e)
        {
            Warn("Steam connection timeout not set", e);
        }
    }

    public uint Listen(int virtualPort)
    {
        KeepLinesOpen();
        try
        {
            return SteamNetworkingSockets.CreateListenSocketP2P(virtualPort, 0, null).m_HSteamListenSocket;
        }
        catch (Exception e)
        {
            Warn("CreateListenSocketP2P failed", e);
            return 0;
        }
    }

    public uint Connect(ulong remote, int virtualPort)
    {
        KeepLinesOpen();
        try
        {
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(remote);
            return SteamNetworkingSockets.ConnectP2P(ref identity, virtualPort, 0, null).m_HSteamNetConnection;
        }
        catch (Exception e)
        {
            Warn("ConnectP2P failed", e);
            return 0;
        }
    }

    public bool Accept(uint connection)
    {
        try
        {
            return SteamNetworkingSockets.AcceptConnection(new HSteamNetConnection(connection)) == EResult.k_EResultOK;
        }
        catch (Exception e)
        {
            Warn("AcceptConnection failed", e);
            return false;
        }
    }

    public void Close(uint connection, int reason, string debug, bool linger)
    {
        try
        {
            SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(connection), reason, debug, linger);
        }
        catch (Exception)
        {
        }
    }

    public void CloseListen(uint listenSocket)
    {
        try
        {
            SteamNetworkingSockets.CloseListenSocket(new HSteamListenSocket(listenSocket));
        }
        catch (Exception)
        {
        }
    }

    public unsafe bool Send(uint connection, byte[] data, int count, bool reliable)
    {
        try
        {
            int flags = reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
            fixed (byte* p = data)
            {
                // Steam copies the bytes during the call.
                return SteamNetworkingSockets.SendMessageToConnection(new HSteamNetConnection(connection), (IntPtr)p,
                    (uint)count, flags, out _) == EResult.k_EResultOK;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public int Receive(uint connection, ref byte[] buffer)
    {
        IntPtr message;
        try
        {
            if (SteamNetworkingSockets.ReceiveMessagesOnConnection(new HSteamNetConnection(connection), _messages, 1) <= 0)
                return -1;
            message = _messages[0];
        }
        catch (Exception)
        {
            return -1;
        }
        try
        {
            // SteamNetworkingMessage_t starts with: void* m_pData; int m_cbSize. Read them directly (no marshalling).
            IntPtr data = Marshal.ReadIntPtr(message, 0);
            int size = Marshal.ReadInt32(message, IntPtr.Size);
            if (size < 0)
                return 0;
            if (buffer.Length < size)
                buffer = new byte[Math.Max(size, buffer.Length * 2)];
            if (size > 0)
                Marshal.Copy(data, buffer, 0, size);
            return size;
        }
        catch (Exception)
        {
            return 0;
        }
        finally
        {
            try
            {
                ReleaseMessage(message);
            }
            catch (Exception)
            {
            }
        }
    }

    public bool Quality(uint connection, out int pingMs, out float lossPercent)
    {
        pingMs = -1;
        lossPercent = -1;
        try
        {
            if (!SteamNetworkingSockets.GetQuickConnectionStatus(new HSteamNetConnection(connection), out var status))
                return false;
            pingMs = status.m_nPing;
            // Quality = the share of packets that arrived (0..1); negative = not known yet.
            lossPercent = status.m_flConnectionQualityLocal < 0 ? -1 : (1 - status.m_flConnectionQualityLocal) * 100;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _callback?.Dispose();
        }
        catch (Exception)
        {
        }
        _callback = null;
    }
}

/// <summary>
/// Steam's relay network (SDR) in one line, for the log and the trace (COOP_LOG 70): players in mainland China got
/// "Steam found no route to the other game" (2026-11-24); this shows whether their Steam reached the relays at all.
/// Null without Steam. Never throws.
/// </summary>
internal static class SteamRelay
{
    public static string Describe()
    {
        try
        {
            if (!SteamAPI.IsSteamRunning())
                return null;
            var overall = SteamNetworkingUtils.GetRelayNetworkStatus(out var details);
            string debug = (details.m_debugMsg ?? "").Trim();
            return "relay " + Name(overall)
                + ", network config " + Name(details.m_eAvailNetworkConfig)
                + ", any relay " + Name(details.m_eAvailAnyRelay)
                + (details.m_bPingMeasurementInProgress != 0 ? ", measuring pings" : "")
                + (debug.Length > 0 ? " (" + debug + ")" : "");
        }
        catch (Exception e)
        {
            return "relay status unknown (" + e.GetType().Name + ")";
        }
    }

    private static string Name(ESteamNetworkingAvailability value) => value switch
    {
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current => "ok",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Attempting => "connecting",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Waiting => "waiting",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_NeverTried => "not tried",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Retrying => "retrying",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Previously => "lost (worked before)",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Failed => "failed",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_CannotTry => "cannot try",
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown => "unknown",
        _ => ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
