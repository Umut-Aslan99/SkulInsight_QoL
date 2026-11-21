using System;
using System.Runtime.InteropServices;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Skul is built with Unity's "force single instance": its boot.config has <c>single-instance=</c> (none of the other 8
/// Unity games on Umut's PC has it), and UnityPlayer.dll then makes a named mutex "...-SingleInstanceMutex-..." at
/// start; a second Skul.exe finds it, logs "Another instance is already running", brings the first window forward and
/// quits. For co-op tests a copy started by the launcher (any --skulinsight- option) closes its own handle of that
/// mutex, so the next copy can start; the launcher starts copies one after another. Only our own process's handles
/// are touched, no game file. Normal players never get here.
/// </summary>
internal static class SingleInstance
{
    private const int ObjectNameInformation = 1, ObjectTypeInformation = 2, BufferSize = 2048;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr info, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Unity's single instance mutex (by name, without the session prefix).</summary>
    public static bool IsSingleInstanceMutex(string name) =>
        name != null && name.IndexOf("SingleInstanceMutex", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Closes this process's handles of Unity's single instance mutex; returns how many (0 = none found).</summary>
    public static int Release()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return 0;
        int closed = 0;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(BufferSize);
            // Handle values are multiples of 4; a game has a few thousand. Only the type is asked first: asking the
            // name of some file handles can hang, a mutex's never does.
            for (int value = 4; value < 0x20000; value += 4)
            {
                var handle = new IntPtr(value);
                if (NtQueryObject(handle, ObjectTypeInformation, buffer, BufferSize, out _) != 0 || Text(buffer) != "Mutant")
                    continue;
                if (NtQueryObject(handle, ObjectNameInformation, buffer, BufferSize, out _) != 0)
                    continue;
                string name = Text(buffer);
                if (IsSingleInstanceMutex(name) && CloseHandle(handle))
                {
                    closed++;
                    Plugin.Log?.LogInfo($"[CoopTest] Released Unity's single instance lock ({name}): the next copy can start.");
                }
            }
            if (closed == 0)
                Plugin.Log?.LogWarning("[CoopTest] Unity's single instance lock wasn't found: a second copy may not start.");
        }
        catch (Exception e)
        {
            Plugin.Log?.LogWarning("[CoopTest] Releasing the single instance lock failed: " + e.Message);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
        return closed;
    }

    /// <summary>The UNICODE_STRING at the start of NtQueryObject's answer (type and name infos both begin with one).</summary>
    private static string Text(IntPtr info)
    {
        int length = (ushort)Marshal.ReadInt16(info);
        IntPtr chars = Marshal.ReadIntPtr(info, IntPtr.Size);
        return length == 0 || chars == IntPtr.Zero ? "" : Marshal.PtrToStringUni(chars, length / 2);
    }
}
