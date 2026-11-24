using DamageInsight.Coop;
using Xunit;

namespace DamageInsight.Tests.Coop;

/// <summary>Other co-op mods (COOP_LOG 73, ticket #6): which names count, and never SkulInsight's own files.</summary>
public class CoopConflictsTests
{
    [Theory]
    // OnlineCoopBeta: its preloader and runtime assemblies / files, its plugin GUID (the ticket's PC)
    [InlineData("SkulOnlineCoopV1.Preloader", CoopConflicts.OnlineCoopBeta)]
    [InlineData("SkulOnlineCoopV1.Runtime.dll", CoopConflicts.OnlineCoopBeta)]
    [InlineData("com.codex.skul.onlinecoop.v1", CoopConflicts.OnlineCoopBeta)]
    [InlineData("Skul4PlayerCoop.dll", CoopConflicts.FourPlayerCoop)]
    // anything else named co-op / multiplayer shows under its own name
    [InlineData("SkulMultiplayer.dll", "SkulMultiplayer")]
    [InlineData("Some Co-op Mod", "Some Co-op Mod")]
    public void Other_co_op_mods_are_named(string name, string expected) => Assert.Equal(expected, CoopConflicts.Classify(name));

    [Theory]
    [InlineData("DamageInsight.dll")]
    [InlineData("DamageInsight.Preloader")]
    [InlineData("docrun.skulinsight_qol")]
    [InlineData("SkulInsight QoL")]
    [InlineData("SkulInsight_CoopTest")]          // the friend test package's name
    [InlineData("DevMenuTurbo")]
    [InlineData("strongeritems")]
    [InlineData("Assembly-CSharp")]
    [InlineData("com.rlabrecque.steamworks.net")]
    [InlineData("")]
    [InlineData(null)]
    public void Our_files_and_other_mods_are_not(string name) => Assert.Null(CoopConflicts.Classify(name));
}
