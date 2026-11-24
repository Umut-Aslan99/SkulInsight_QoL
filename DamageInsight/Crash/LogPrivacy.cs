using System.IO;
using System.Linq;
using System.Text;

namespace DamageInsight.Crash;

/// <summary>
/// The bug report zip goes into a public GitHub issue: the co-op logs name both players' Steam ids (the teammate never
/// agreed to that). Masked in place (same length, so nothing shifts): "76561198131523257" -> "7656119xxxxxx3257"; the
/// last 4 digits still tell two players apart in one report.
/// </summary>
public static class LogPrivacy
{
    private static readonly byte[] Prefix = { (byte)'7', (byte)'6', (byte)'5', (byte)'6', (byte)'1', (byte)'1', (byte)'9' };
    public const int IdLength = 17;

    /// <summary>Masks every 17-digit Steam id (7656119 + 10 digits, not inside a longer number). Count.</summary>
    public static int MaskSteamIds(byte[] data)
    {
        if (data == null)
            return 0;
        int count = 0;
        for (int i = 0; i + IdLength <= data.Length; i++)
        {
            if (data[i] != Prefix[0] || (i > 0 && IsDigit(data[i - 1])))
                continue;
            bool match = true;
            for (int k = 1; k < Prefix.Length && match; k++)
                match = data[i + k] == Prefix[k];
            for (int k = Prefix.Length; k < IdLength && match; k++)
                match = IsDigit(data[i + k]);
            if (!match || (i + IdLength < data.Length && IsDigit(data[i + IdLength])))
                continue;
            for (int k = Prefix.Length; k < IdLength - 4; k++)
                data[i + k] = (byte)'x';
            count++;
            i += IdLength - 1;
        }
        return count;
    }

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

    /// <summary>The placeholder that stands for the player's account name in sent logs (REPORTS.md).</summary>
    public const string UserPlaceholder = "%USERNAME%";

    private static readonly string[] HomeFolders = { "Users\\", "Users/", "home/", "home\\" };

    /// <summary>
    /// Logs sent to the server (a report's zip, a co-op run's logs): the account name inside home folder paths
    /// ("C:\Users\Name\AppData...", "C:/Users/Name/...", Wine's "Z:\home\name\...") becomes %USERNAME%. Only in those
    /// paths: a short name like "Max" stays everywhere else, where it may be a word. Case-insensitive (Windows paths);
    /// a name in a path only when the next character ends that folder. Returns the data unchanged when there is none.
    /// </summary>
    public static byte[] MaskUserName(byte[] data, string userName)
    {
        if (data == null || string.IsNullOrEmpty(userName) || userName.Length < 2 || userName.IndexOfAny(new[] { '/', '\\', '"' }) >= 0)
            return data;
        var name = Encoding.UTF8.GetBytes(userName);
        var placeholder = Encoding.ASCII.GetBytes(UserPlaceholder);
        var heads = HomeFolders.Select(Encoding.ASCII.GetBytes).ToArray();
        MemoryStream output = null;
        int copied = 0;
        for (int i = 0; i < data.Length; i++)
        {
            foreach (var head in heads)
            {
                int at = i + head.Length;
                if (at + name.Length > data.Length || !SameIgnoringCase(data, i, head) || !SameIgnoringCase(data, at, name))
                    continue;
                int end = at + name.Length;
                if (end < data.Length && !(data[end] is (byte)'/' or (byte)'\\' or (byte)'"' or (byte)'\'' or (byte)' '
                        or (byte)'\r' or (byte)'\n' or (byte)')' or (byte)',' or (byte)';'))
                    continue; // "Users\Maxine" is not "Users\Max"
                output ??= new MemoryStream(data.Length);
                output.Write(data, copied, at - copied);
                output.Write(placeholder, 0, placeholder.Length);
                copied = end;
                i = end - 1;
                break;
            }
        }
        if (output == null)
            return data;
        output.Write(data, copied, data.Length - copied);
        return output.ToArray();
    }

    private static bool SameIgnoringCase(byte[] data, int at, byte[] what)
    {
        for (int k = 0; k < what.Length; k++)
        {
            byte a = data[at + k], b = what[k];
            if (a == b)
                continue;
            if (a is >= (byte)'A' and <= (byte)'Z')
                a = (byte)(a + 32);
            if (b is >= (byte)'A' and <= (byte)'Z')
                b = (byte)(b + 32);
            if (a != b)
                return false;
        }
        return true;
    }
}
