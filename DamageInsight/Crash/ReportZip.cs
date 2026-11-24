using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Tools;

namespace DamageInsight.Crash;

/// <summary>One file of a sent zip: a file on disk (from <see cref="From"/> on) or bytes made in the game.</summary>
public sealed class ZipPart
{
    public string Name = "";
    public string Path;
    public byte[] Data;
    /// <summary>A file read from this byte on (a co-op run's part of the session's trace).</summary>
    public long From;
    /// <summary>Left out first when the zip is too big (an older trace, the previous Unity log).</summary>
    public bool Optional;
    /// <summary>Cut to its last part when the zip is still too big (logs and traces: the end shows the problem).</summary>
    public bool Cuttable;
}

/// <summary>
/// Packs the zip a report or a co-op run sends (REPORTS.md), without Unity calls so it runs on a background thread:
/// files read while the game still writes them, Steam ids and the account name masked (LogPrivacy), and kept under
/// <paramref name="maxBytes"/>: first the optional files go, then the cuttable ones keep only their last half, quarter,
/// ... (a line saying so starts them). Tested (ReportTests).
/// </summary>
public static class ReportZip
{
    public const string CutNote = "[SkulInsight: the start of this file was left out to keep the report small]\n";

    public static byte[] Build(IList<ZipPart> parts, long maxBytes, string userName, DateTime now)
    {
        var data = new Dictionary<ZipPart, byte[]>();
        foreach (var part in parts)
        {
            byte[] bytes = part.Data ?? ReadShared(part.Path, part.From);
            if (bytes == null)
                continue;
            LogPrivacy.MaskSteamIds(bytes);
            data[part] = LogPrivacy.MaskUserName(bytes, userName);
        }

        byte[] zip = null;
        for (int step = 0; step <= 6; step++)
        {
            zip = Pack(parts, data, now, dropOptional: step >= 1, keepShare: step <= 1 ? 1.0 : Math.Pow(0.5, step - 1));
            if (zip.Length <= maxBytes)
                break;
        }
        return zip;
    }

    private static byte[] Pack(IList<ZipPart> parts, Dictionary<ZipPart, byte[]> data, DateTime now, bool dropOptional, double keepShare)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipWriter(memory))
        {
            foreach (var part in parts)
            {
                if (!data.TryGetValue(part, out var bytes) || (dropOptional && part.Optional))
                    continue;
                if (part.Cuttable && keepShare < 1.0)
                    bytes = Tail(bytes, (long)(bytes.Length * keepShare));
                zip.Add(part.Name, bytes, now);
            }
        }
        return memory.ToArray();
    }

    /// <summary>The last <paramref name="keep"/> bytes from a line start on, after <see cref="CutNote"/>.</summary>
    public static byte[] Tail(byte[] bytes, long keep)
    {
        if (keep >= bytes.Length)
            return bytes;
        int start = bytes.Length - (int)Math.Max(0, keep);
        while (start < bytes.Length && bytes[start - 1] != (byte)'\n')
            start++;
        var note = Encoding.UTF8.GetBytes(CutNote);
        var cut = new byte[note.Length + bytes.Length - start];
        Buffer.BlockCopy(note, 0, cut, 0, note.Length);
        Buffer.BlockCopy(bytes, start, cut, note.Length, bytes.Length - start);
        return cut;
    }

    /// <summary>A file another part of the game still writes, from <paramref name="from"/> on; null when unreadable.</summary>
    public static byte[] ReadShared(string file, long from = 0)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            return null;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (from > 0)
                stream.Seek(Math.Min(from, stream.Length), SeekOrigin.Begin);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The newest <paramref name="count"/> files of a folder that match.</summary>
    public static List<string> Newest(string folder, string pattern, int count)
    {
        if (!Directory.Exists(folder))
            return new List<string>();
        return Directory.GetFiles(folder, pattern).OrderByDescending(File.GetLastWriteTimeUtc).Take(count).ToList();
    }
}
