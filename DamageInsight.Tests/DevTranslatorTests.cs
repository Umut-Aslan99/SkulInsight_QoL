using DamageInsight.Tools;
using Xunit;

namespace DamageInsight.Tests;

public class DevTranslatorTests
{
    [FixtureTheory]
    [InlineData("장비 목록", "Gear list")]
    [InlineData("  테스트 맵 ", "Test map")]
    [InlineData("검은적", "Dark enemies")]
    [InlineData("500 마석", "500 Dark Quartz")]     // not a known label: word replacement
    public void Translates(string korean, string english) => Assert.Equal(english, DevTranslator.Translate(korean));

    [FixtureFact]
    public void DetectsHangul()
    {
        Assert.True(DevTranslator.HasHangul("Ch.1 다음"));
        Assert.False(DevTranslator.HasHangul("Time Scale"));
    }
}

public class DevMenuCoverageTests
{
    /// <summary>Every Korean text collected from the developer menu and test map must translate fully.</summary>
    [FixtureFact]
    public void AllCollectedTextsTranslate()
    {
        string file = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "dev_menu_texts.txt");
        var missing = new System.Collections.Generic.List<string>();
        foreach (var line in System.IO.File.ReadAllLines(file))
        {
            string text = line.Replace("\\n", "\n"); // the file stores line breaks as a literal \n
            if (DevTranslator.HasHangul(DevTranslator.Translate(text)))
                missing.Add(line);
        }
        Assert.True(missing.Count == 0, "Not translated: " + string.Join(" | ", missing));
    }
}
