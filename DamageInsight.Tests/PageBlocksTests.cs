using System.Linq;
using DamageInsight.Codex;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The Codex page layout: text, framed sections with icons, skill tiles.</summary>
public class PageBlocksTests
{
    [Fact]
    public void A_page_splits_into_intro_sections_and_tiles()
    {
        string page = "Intro text\n" + PageBlocks.Section("Attacks", "inscription:Arms") + "\nBasic attack: 5–7\n"
                      + PageBlocks.Section("Skills", "inscription:Wisdom") + "You get 2 of these.\n"
                      + PageBlocks.Tile("skillkey:Fireball", "<b>Fireball</b>\nBoom") + PageBlocks.Tile("", "<b>???</b>");
        var blocks = PageBlocks.Parse(page);
        Assert.Equal(3, blocks.Count);
        Assert.Null(blocks[0].Title);
        Assert.Equal("Intro text", blocks[0].Items.Single().Text);
        Assert.Equal("Attacks", blocks[1].Title);
        Assert.Equal("inscription:Arms", blocks[1].Icon);
        Assert.Equal("Basic attack: 5–7", blocks[1].Items.Single().Text);
        var skills = blocks[2];
        Assert.Equal(3, skills.Items.Count);
        Assert.Equal(PageBlocks.Kind.Text, skills.Items[0].Kind);
        Assert.Equal(PageBlocks.Kind.Tile, skills.Items[1].Kind);
        Assert.Equal("skillkey:Fireball", skills.Items[1].Icon);
        Assert.Equal("", skills.Items[2].Icon);
    }

    [Fact]
    public void Close_ends_a_section_so_the_story_stays_outside()
    {
        string page = PageBlocks.Section("Price per level", "inscription:Omen") + "1 / 3 / 5" + PageBlocks.Close() + "\n\n<i>A story</i>";
        var blocks = PageBlocks.Parse(page);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("1 / 3 / 5", blocks[0].Items.Single().Text);
        Assert.Null(blocks[1].Title);
        Assert.Equal("<i>A story</i>", blocks[1].Items.Single().Text);
    }

    [Fact]
    public void Plain_text_stays_one_block()
    {
        var blocks = PageBlocks.Parse("Just a creature page\nwith lines");
        Assert.Single(blocks);
        Assert.Equal("Just a creature page\nwith lines", blocks[0].Items.Single().Text);
        Assert.Empty(PageBlocks.Parse(""));
    }

    [Fact]
    public void Won_skills_are_saved_and_loaded()
    {
        var progress = new CodexProgress();
        var page = progress.Get("weapon:Hunter");
        Assert.True(page.AddWonSkill("PiercingArrow"));
        Assert.False(page.AddWonSkill("PiercingArrow"));
        page.AddWonSkill("ArrowRain");
        var loaded = CodexProgress.FromJson(progress.ToJson());
        Assert.True(loaded.Get("weapon:Hunter").WonSkill("ArrowRain"));
        Assert.True(loaded.Get("weapon:Hunter").WonSkill("PiercingArrow"));
        Assert.False(loaded.Get("weapon:Hunter").WonSkill("Snipe"));
    }
}
