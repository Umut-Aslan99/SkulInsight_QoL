using DamageInsight.Codex;
using Xunit;

namespace DamageInsight.Tests;

public class FightLinkTests
{
    [Fact]
    public void Fight_pieces_and_summoners_survive_saving()
    {
        var progress = new CodexProgress();
        progress.Get("named:DarkCrystalLeft").PartOf = "enemy:Pope";
        var fanatic = progress.Get("enemy:Fanatic");
        fanatic.AddSummoner("enemy:Pope");
        fanatic.AddSummoner("enemy:Pope");
        fanatic.AddSummoner("named:VeteranThief");

        var loaded = CodexProgress.FromJson(progress.ToJson());
        Assert.Equal("enemy:Pope", loaded.Get("named:DarkCrystalLeft").PartOf);
        Assert.Equal("enemy:Pope,named:VeteranThief", loaded.Get("enemy:Fanatic").SummonedBy);
        Assert.True(loaded.Get("enemy:Fanatic").IsSummonedBy("enemy:Pope"));
        Assert.False(loaded.Get("enemy:Fanatic").IsSummonedBy("enemy:Po"));
    }
}
