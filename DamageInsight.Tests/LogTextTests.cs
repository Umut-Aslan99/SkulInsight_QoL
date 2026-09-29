using System.Linq;
using Characters;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

public class LogTextTests
{
    [Fact]
    public void Line_ReadsLikeASentence()
    {
        string line = LogText.Line(Hit.Make(amount: 45, source: DamageSource.Skill, attribute: Damage.Attribute.Magic, targetName: "Carleon ManAtArms"));

        string plain = StripTags(line);
        Assert.Contains("You dealt 45 Skill Magic damage to Carleon ManAtArms", plain);
        Assert.DoesNotContain("CRIT", plain);
    }

    [Fact]
    public void Line_MarksCritsAndDamageTaken()
    {
        string line = LogText.Line(Hit.Make(amount: 12, attacker: EntityKind.Boss, attackerName: "Leiana", target: EntityKind.Player, crit: true));

        string plain = StripTags(line);
        Assert.Contains("Leiana dealt 12 Basic Physical damage to You", plain);
        Assert.EndsWith("CRIT", plain);
    }

    [Fact]
    public void CompactLine_IsShortAndShowsDirection()
    {
        Assert.Equal("45 Skill » Ent", StripTags(LogText.CompactLine(Hit.Make(amount: 45, source: DamageSource.Skill))));
        Assert.Equal("-12 Basic CRIT « Leiana", StripTags(LogText.CompactLine(
            Hit.Make(amount: 12, attacker: EntityKind.Boss, attackerName: "Leiana", target: EntityKind.Player, crit: true))));
    }

    [Fact]
    public void CompactLine_AppliesAlphaToEveryColour()
    {
        string line = LogText.CompactLine(Hit.Make(), alpha: 0.5f);
        var colours = System.Text.RegularExpressions.Regex.Matches(line, "<color=(#[0-9A-F]+)>");

        Assert.NotEmpty(colours);
        foreach (System.Text.RegularExpressions.Match c in colours)
            Assert.EndsWith("80", c.Groups[1].Value); // 0.5 * 255 = 128 = 0x80
    }

    [Fact]
    public void Build_AddsARoomHeaderWhenTheRoomChanges()
    {
        var rooms = new[] { new RoomInfo(1, "Chapter1 · A", 0f), new RoomInfo(2, "Chapter1 · B", 0f) };
        var shown = new[] { Hit.Make(room: 1), Hit.Make(room: 1), Hit.Make(room: 2) };

        string text = StripTags(LogText.Build(shown, rooms, maxLines: 100));

        Assert.Equal(1, Count(text, "Room 1"));
        Assert.Equal(1, Count(text, "Room 2"));
        Assert.Contains("Chapter1 · B", text);
    }

    [Fact]
    public void Build_ShowsOnlyTheNewestLines()
    {
        var shown = Enumerable.Range(1, 10).Select(i => Hit.Make(amount: i)).ToArray();

        string text = StripTags(LogText.Build(shown, new RoomInfo[0], maxLines: 3));

        Assert.Contains("7 older entries hidden", text);
        Assert.Contains("dealt 10 ", text);
        Assert.DoesNotContain("dealt 7 ", text);
    }

    [Fact]
    public void Build_ExplainsAnEmptyLog()
    {
        Assert.Contains(LogText.EmptyMessage, LogText.Build(new DamageRecord[0], new RoomInfo[0], 100));
    }

    /// <summary>Removes rich-text tags like &lt;color=#fff&gt; and collapses spacing, leaving the readable text.</summary>
    private static string StripTags(string text)
    {
        string plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        return System.Text.RegularExpressions.Regex.Replace(plain, " {2,}", " ").Trim();
    }

    private static int Count(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;
}
