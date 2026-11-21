using System;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Crash;
using DamageInsight.Mods;
using DamageInsight.Tools;
using Xunit;

namespace DamageInsight.Tests;

public class BugReportTests
{
    [Fact]
    public void ZipWriter_RoundTripsThroughTheModsZipReader()
    {
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"t\":1.000,\"kind\":\"motion\"}\n", 2000)));
        var tiny = new byte[] { 1, 2, 3 };
        using var memory = new MemoryStream();
        using (var zip = new ZipWriter(memory))
        {
            zip.Add("report.txt", Encoding.UTF8.GetBytes("hello"), new DateTime(2026, 11, 21, 15, 30, 10));
            zip.Add("CoopTrace\\a.jsonl", text, DateTime.Now);
            zip.Add("Unity/tiny.bin", tiny, DateTime.Now);
        }
        var entries = ZipReader.Read(memory.ToArray());
        Assert.Equal(new[] { "report.txt", "CoopTrace/a.jsonl", "Unity/tiny.bin" }, entries.Select(e => e.Path));
        Assert.Equal("hello", Encoding.UTF8.GetString(entries[0].Data));
        Assert.Equal(text, entries[1].Data);
        Assert.Equal(tiny, entries[2].Data);
        Assert.True(memory.Length < text.Length / 5, "the trace text is deflated");
    }

    [Fact]
    public void Crc32_IsTheZipOne() => Assert.Equal(0xCBF43926u, ZipWriter.Crc32(Encoding.ASCII.GetBytes("123456789")));

    [Fact]
    public void SteamIds_AreMasked_OtherNumbersKept()
    {
        var data = Encoding.UTF8.GetBytes("joined 76561198131523257 and \"76561198365020686\"; frame 765611981315232570; 7656119813152 x");
        Assert.Equal(2, LogPrivacy.MaskSteamIds(data));
        Assert.Equal("joined 7656119xxxxxx3257 and \"7656119xxxxxx0686\"; frame 765611981315232570; 7656119813152 x",
            Encoding.UTF8.GetString(data));
    }
}
