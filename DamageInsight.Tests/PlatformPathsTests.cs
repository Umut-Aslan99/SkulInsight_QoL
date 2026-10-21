using System.Linq;
using DamageInsight.Lang;
using DamageInsight.Tools;
using Xunit;

namespace DamageInsight.Tests;

public class PlatformPathsTests
{
    [Fact]
    public void Windows_folder_url_keeps_its_old_form()
    {
        Assert.Equal("file:///C:/Program Files (x86)/Steam/steamapps/common/Skul/BepInEx/DamageInsight/CrashReports",
            PlatformPaths.FolderUrl(@"C:\Program Files (x86)\Steam\steamapps\common\Skul\BepInEx\DamageInsight\CrashReports", windows: true));
    }

    [Fact]
    public void Mac_folder_url_is_escaped()
    {
        Assert.Equal("file:///Users/doc%20run/Library/Application%20Support/Steam/steamapps/common/Skul/BepInEx/DamageInsight/CrashReports",
            PlatformPaths.FolderUrl("/Users/doc run/Library/Application Support/Steam/steamapps/common/Skul/BepInEx/DamageInsight/CrashReports", windows: false));
    }

    [Fact]
    public void Mac_font_folders_start_with_the_system_ones()
    {
        var folders = PlatformPaths.FontFolders(windows: false);
        Assert.Equal("/System/Library/Fonts", folders[0]);
        Assert.Contains("/Library/Fonts", folders);
        Assert.EndsWith("Fonts", PlatformPaths.FontFolders(windows: true).Single());
    }

    [Fact]
    public void Every_cjk_language_has_a_mac_font_after_the_windows_ones()
    {
        string[] mac = { "AppleSDGothicNeo.ttc", "ヒラギノ角ゴシック W6.ttc", "STHeiti Medium.ttc", "Arial Unicode.ttf" };
        foreach (var (code, names) in FontFallback.Candidates.Select(p => (p.Key, p.Value)))
        {
            Assert.Contains(names, n => mac.Contains(n));
            Assert.Equal("Arial Unicode.ttf", names.Last());
            Assert.True(names[0].StartsWith("m") || names[0].StartsWith("Y"), $"{code}: a Windows font stays first ({names[0]})");
        }
    }
}
