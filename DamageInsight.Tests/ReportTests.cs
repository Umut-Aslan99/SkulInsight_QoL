using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DamageInsight.Crash;
using DamageInsight.Describe;
using DamageInsight.Mods;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The report form's payload, the zip it sends and the account name masking (docs/coop/REPORTS.md).</summary>
public class ReportTests
{
    [Fact]
    public void Json_CarriesTheFormCleanedAndTheServersFields()
    {
        var form = new ReportForm { Kind = ReportKind.Idea, Title = "  A second\tloadout  ", Text = "line one\r\nline \"two\"\u202e\n\n\n\n\nend", Contact = "Me#1" };
        var json = GearDoc.ParseNode(ReportPayload.Json(form, "0.17.1", "1.9.1", "Debug", "pt-BR", true));
        Assert.Equal("idea", json.Str("kind"));
        Assert.Equal("A second loadout", json.Str("title"));
        Assert.Equal("line one\nline \"two\"\n\n\nend", json.Str("text"));
        Assert.Equal("Me#1", json.Str("contact"));
        Assert.Equal("pt-BR", json.Str("language"));
        Assert.True(json.Bool("coop"));
    }

    [Theory]
    [InlineData("ab", ReportPayload.Problem.TitleShort)]
    [InlineData("   x  ", ReportPayload.Problem.TitleShort)]
    [InlineData("Fine title", ReportPayload.Problem.None)]
    public void Check_NeedsATitle(string title, ReportPayload.Problem expected) =>
        Assert.Equal(expected, ReportPayload.Check(new ReportForm { Title = title }));

    [Fact]
    public void Short_KeepsOnlyWhatTheServerTakes() =>
        Assert.Equal("1.9.1 (b)", ReportPayload.Short("1.9.1 <b>(b)</b>".Replace("<b>", "").Replace("</b>", "") + "\u00e9", 32));

    [Fact]
    public void ParseAnswer_ReadsTicketAndIssueAndRefusesOtherLinks()
    {
        var a = ReportPayload.ParseAnswer("{\"ok\":true,\"ticket\":\"R-20261124-3\",\"issue\":\"https://github.com/Umut-Aslan99/SkulInsight_QoL/issues/12\",\"issueNumber\":12,\"logs\":true}");
        Assert.Equal("R-20261124-3", a.Ticket);
        Assert.Equal(12, a.IssueNumber);
        Assert.True(a.Logs);
        var b = ReportPayload.ParseAnswer("{\"ticket\":\"R-1\",\"issue\":\"https://evil.example/x\",\"issueNumber\":1,\"logs\":false,\"logsRefused\":\"full\"}");
        Assert.Equal("", b.Issue);
        Assert.Equal("full", b.LogsRefused);
        Assert.Null(ReportPayload.ParseAnswer("{\"detail\":\"title\"}"));
        Assert.Null(ReportPayload.ParseAnswer("not json"));
        Assert.Equal("title", ReportPayload.RefusedField("{\"detail\":\"title\"}"));
    }

    [Fact]
    public void RunKey_IsTheSameOnBothGamesAndFitsTheServer()
    {
        string a = ReportPayload.RunKey(-207714531, 3051234567u, "Umut");
        Assert.Equal(a, ReportPayload.RunKey(-207714531, 3051234567u, "Umut"));
        Assert.NotEqual(a, ReportPayload.RunKey(-207714531, 3051234568u, "Umut"));
        Assert.Matches("^[A-Za-z0-9_-]{8,64}$", a);
        var run = GearDoc.ParseNode(ReportPayload.RunJson(a, false, 0, "0.17.1", "1.9.1", "darkmirror", 12, 5, 900, 2, "castle"));
        Assert.Equal("client", run.Str("side"));
        Assert.Equal(1, (int)run.Num("slot"));
        Assert.Equal(10, (int)run.Num("level"));
    }

    [Theory]
    [InlineData("C:\\Users\\Max\\AppData\\LocalLow\\x", "C:\\Users\\%USERNAME%\\AppData\\LocalLow\\x")]
    [InlineData("path: C:/users/MAX/AppData", "path: C:/users/%USERNAME%/AppData")]
    [InlineData("Z:\\home\\max\\.steam", "Z:\\home\\%USERNAME%\\.steam")]
    [InlineData("C:\\Users\\Maxine\\x and Max wins", "C:\\Users\\Maxine\\x and Max wins")]
    [InlineData("\"C:\\Users\\Max\" end", "\"C:\\Users\\%USERNAME%\" end")]
    public void MaskUserName_OnlyInHomeFolderPaths(string text, string expected) =>
        Assert.Equal(expected, Encoding.UTF8.GetString(LogPrivacy.MaskUserName(Encoding.UTF8.GetBytes(text), "Max")));

    [Fact]
    public void MaskUserName_NonAsciiNames()
    {
        var masked = LogPrivacy.MaskUserName(Encoding.UTF8.GetBytes("C:\\Users\\Jürgen\\log"), "Jürgen");
        Assert.Equal("C:\\Users\\%USERNAME%\\log", Encoding.UTF8.GetString(masked));
    }

    [Fact]
    public void Build_DropsOptionalThenCutsLogsToStayUnderTheCap()
    {
        var random = new Random(7);
        string Noise(int lines) => string.Concat(Enumerable.Range(0, lines).Select(i => $"{i} {random.Next()} {random.Next()} C:\\Users\\Max\\x\n"));
        var log = Encoding.UTF8.GetBytes(Noise(20000));
        var parts = new List<ZipPart>
        {
            new() { Name = "report.json", Data = Encoding.UTF8.GetBytes("{\"kind\":\"bug\"}") },
            new() { Name = "BepInEx/LogOutput.log", Data = log, Cuttable = true },
            new() { Name = "CoopTrace/old.jsonl", Data = Encoding.UTF8.GetBytes(Noise(20000)), Optional = true, Cuttable = true },
        };
        byte[] full = ReportZip.Build(parts, long.MaxValue, "Max", DateTime.Now);
        byte[] small = ReportZip.Build(parts, full.Length / 3, "Max", DateTime.Now);
        Assert.True(small.Length <= full.Length / 3, $"{small.Length} > {full.Length / 3}");
        var entries = ZipReader.Read(small);
        Assert.Equal(new[] { "report.json", "BepInEx/LogOutput.log" }, entries.Select(e => e.Path));
        string kept = Encoding.UTF8.GetString(entries[1].Data);
        Assert.StartsWith(ReportZip.CutNote, kept);
        Assert.EndsWith("19999 ", kept.Substring(0, kept.LastIndexOf('\n')).Split('\n').Last().Substring(0, 6));
        Assert.DoesNotContain("\\Max\\", kept);
    }
}
