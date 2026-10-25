using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DamageInsight.Mods;

/// <summary>
/// Reads the files of a zip (a Thunderstore package). The game has no ZipArchive (System.IO.Compression.dll), only
/// System.dll's DeflateStream, so this reads the zip's central directory itself: stored and deflated entries, UTF-8
/// or old DOS names, Windows backslashes turned into "/" (DuelManTeam-OnlineCoopBeta's zip has them). No zip64 (no
/// Skul package comes near 4 GB). Plain logic, no Unity.
/// </summary>
public static class ZipReader
{
    public sealed class Entry
    {
        /// <summary>The path inside the zip with "/" ("plugins/Mod.dll"); folders end with "/".</summary>
        public string Path = "";
        public byte[] Data = Array.Empty<byte>();
        public bool IsFolder => Path.EndsWith("/", StringComparison.Ordinal);
    }

    private const uint EndOfDirectory = 0x06054b50, DirectoryEntry = 0x02014b50, LocalHeader = 0x04034b50;

    public static List<Entry> Read(byte[] zip)
    {
        if (zip == null || zip.Length < 22)
            throw new InvalidDataException("not a zip");
        int end = -1;
        for (int i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
            if (U32(zip, i) == EndOfDirectory)
            {
                end = i;
                break;
            }
        if (end < 0)
            throw new InvalidDataException("not a zip (no directory)");
        int count = U16(zip, end + 10);
        int at = (int)U32(zip, end + 16);
        var entries = new List<Entry>(count);
        for (int n = 0; n < count; n++)
        {
            if (at + 46 > zip.Length || U32(zip, at) != DirectoryEntry)
                throw new InvalidDataException("broken zip directory");
            int method = U16(zip, at + 10);
            int packed = (int)U32(zip, at + 20), size = (int)U32(zip, at + 24);
            int nameLength = U16(zip, at + 28), extraLength = U16(zip, at + 30), commentLength = U16(zip, at + 32);
            int local = (int)U32(zip, at + 42);
            // UTF-8 for both kinds of names: the game's Mono may lack the DOS code page (I18N), and package file
            // names are ASCII, which reads the same either way.
            string name = Encoding.UTF8.GetString(zip, at + 46, nameLength).Replace('\\', '/');
            at += 46 + nameLength + extraLength + commentLength;

            if (local + 30 > zip.Length || U32(zip, local) != LocalHeader)
                throw new InvalidDataException("broken zip entry " + name);
            int data = local + 30 + U16(zip, local + 26) + U16(zip, local + 28);
            if (data + packed > zip.Length)
                throw new InvalidDataException("cut zip entry " + name);
            var entry = new Entry { Path = name };
            if (!entry.IsFolder)
                entry.Data = method switch
                {
                    0 => Slice(zip, data, packed),
                    8 => Inflate(zip, data, packed, size),
                    _ => throw new InvalidDataException($"{name}: zip method {method} not supported"),
                };
            entries.Add(entry);
        }
        return entries;
    }

    private static byte[] Inflate(byte[] zip, int offset, int packed, int size)
    {
        using var input = new MemoryStream(zip, offset, packed, writable: false);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        var output = new byte[size];
        int read = 0;
        while (read < size)
        {
            int got = inflate.Read(output, read, size - read);
            if (got <= 0)
                throw new InvalidDataException("cut deflate data");
            read += got;
        }
        return output;
    }

    private static byte[] Slice(byte[] zip, int offset, int length)
    {
        var copy = new byte[length];
        Buffer.BlockCopy(zip, offset, copy, 0, length);
        return copy;
    }

    private static int U16(byte[] b, int i) => b[i] | (b[i + 1] << 8);

    private static uint U32(byte[] b, int i) => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
}
