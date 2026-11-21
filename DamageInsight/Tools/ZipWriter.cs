using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DamageInsight.Tools;

/// <summary>
/// Writes a plain zip (the game has no System.IO.Compression.dll, so no ZipArchive): each entry deflated with the
/// runtime's DeflateStream, or stored when that fails (a runtime without its native zlib helper). Names use "/".
/// For the bug report zip (Coop/BugReport); read back by Mods/ZipReader in the tests.
/// </summary>
public sealed class ZipWriter : IDisposable
{
    private readonly Stream _out;
    private readonly List<(string name, uint crc, uint packed, uint size, ushort method, uint offset, ushort time, ushort date)> _entries = new();
    private bool _done;

    public ZipWriter(Stream output) => _out = output;

    /// <summary>Adds one file under <paramref name="name"/> ("folder/file.txt").</summary>
    public void Add(string name, byte[] data, DateTime when)
    {
        name = name.Replace('\\', '/');
        uint crc = Crc32(data);
        byte[] packed = Deflate(data);
        ushort method = 8;
        if (packed == null || packed.Length >= data.Length)
        {
            packed = data;
            method = 0;
        }
        var (time, date) = DosTime(when);
        uint offset = (uint)_out.Position;
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        var w = new BinaryWriter(_out, Encoding.UTF8, leaveOpen: true);
        w.Write(0x04034b50u);
        w.Write((ushort)20); // version needed
        w.Write((ushort)0x0800); // UTF-8 names
        w.Write(method);
        w.Write(time);
        w.Write(date);
        w.Write(crc);
        w.Write((uint)packed.Length);
        w.Write((uint)data.Length);
        w.Write((ushort)nameBytes.Length);
        w.Write((ushort)0);
        w.Write(nameBytes);
        w.Write(packed);
        w.Flush();
        _entries.Add((name, crc, (uint)packed.Length, (uint)data.Length, method, offset, time, date));
    }

    /// <summary>Writes the central directory (also on Dispose).</summary>
    public void Finish()
    {
        if (_done)
            return;
        _done = true;
        uint start = (uint)_out.Position;
        var w = new BinaryWriter(_out, Encoding.UTF8, leaveOpen: true);
        foreach (var e in _entries)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(e.name);
            w.Write(0x02014b50u);
            w.Write((ushort)20); // made by
            w.Write((ushort)20); // needed
            w.Write((ushort)0x0800);
            w.Write(e.method);
            w.Write(e.time);
            w.Write(e.date);
            w.Write(e.crc);
            w.Write(e.packed);
            w.Write(e.size);
            w.Write((ushort)nameBytes.Length);
            w.Write((ushort)0); // extra
            w.Write((ushort)0); // comment
            w.Write((ushort)0); // disk
            w.Write((ushort)0); // internal attributes
            w.Write(0u); // external attributes
            w.Write(e.offset);
            w.Write(nameBytes);
        }
        uint size = (uint)_out.Position - start;
        w.Write(0x06054b50u);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)_entries.Count);
        w.Write((ushort)_entries.Count);
        w.Write(size);
        w.Write(start);
        w.Write((ushort)0);
        w.Flush();
    }

    public void Dispose() => Finish();

    private static byte[] Deflate(byte[] data)
    {
        try
        {
            using var memory = new MemoryStream();
            using (var deflate = new DeflateStream(memory, CompressionMode.Compress, leaveOpen: true))
                deflate.Write(data, 0, data.Length);
            return memory.ToArray();
        }
        catch (Exception)
        {
            return null; // no zlib in this runtime: stored
        }
    }

    private static (ushort time, ushort date) DosTime(DateTime t)
    {
        if (t.Year < 1980)
            t = new DateTime(1980, 1, 1);
        return ((ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2)),
            (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day));
    }

    private static uint[] _table;

    public static uint Crc32(byte[] data)
    {
        if (_table == null)
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            _table = table;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
            crc = _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
