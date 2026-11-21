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
}
