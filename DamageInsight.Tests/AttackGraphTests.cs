using System;
using System.Linq;
using DamageInsight.Codex;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

/// <summary>The attack analysis on the real boss AIs (types only: every action is named by its field path).</summary>
public class AttackGraphTests
{
    private readonly ITestOutputHelper _out;
    public AttackGraphTests(ITestOutputHelper output) => _out = output;

    private AttackGraph Graph(Type ai)
    {
        var graph = new AttackGraph(typeof(Characters.AI.AIController), BossAttacks.StepTypes)
            .Build(new[] { new AttackGraph.Node(ai, null, ai.Name) });
        _out.WriteLine(graph.Report(s => s.ToString().Substring(ai.Name.Length + 1)));
        return graph;
    }

    private static string Steps(AttackGraph g, string label)
    {
        var attack = g.Attacks.SingleOrDefault(a => a.Label == label);
        Assert.True(attack != null, $"no attack \"{label}\" among: {string.Join(", ", g.Attacks.Select(a => a.Label))}");
        return string.Join(" ", attack.Steps.Select(s => s.ToString().Substring(s.ToString().IndexOf('/') + 1)));
    }

    [Fact]
    public void Leiana_sisters_attacks_are_whole()
    {
        var g = Graph(typeof(Characters.AI.TwinSister.GoldenAideAI));
        Assert.Equal("_dashOfRush/_motion _rushReady _rushA _rushB _rushC _rushFinish _rushStanding", Steps(g, "Rush"));
        Assert.Equal("_goldenMeteorJump _goldenMeteorReady _goldenMeteorAttack _goldenMeteorLanding", Steps(g, "Golden meteor"));
        Assert.Equal("_meteorInAirJump _meteorInAirReady _meteorInAirAttack _meteorInAirLanding _meteorInAirStanding", Steps(g, "Meteor in air"));
        Assert.Equal("_twinAppear _meteorInGround2Ready _meteorInGround2Attack _meteorInGround2Landing _meteorInGround2Standing _twinMeteorEscape",
            Steps(g, "Twin meteor ground"));
        Assert.Equal("_twinAppear _rangeAttackHoming/attack _twinMeteorEscape", Steps(g, "Twin meteor pierce"));
        Assert.Equal("_twinAppear _awakening", Steps(g, "Awakening"));
        // Twin meteor and its "predict" twin are the same moves: one attack.
        Assert.Equal("_twinMeteorPreparing _twinMeteor _twinMeteorEnding", Steps(g, "Twin meteor"));
        Assert.DoesNotContain(g.Attacks, a => a.Label == "Predict twin meteor");
        // The escape only ends other attacks: it is a tail, not a move of its own.
        Assert.True(g.Attacks.Single(a => a.Label == "Escape for twin meteor").IsTail);
        Assert.False(g.Attacks.Single(a => a.Label == "Range attack homing").IsTail);
        // Helpers are no attacks of their own.
        Assert.DoesNotContain(g.Attacks, a => a.Label is "Twin appear" or "Escape for twin" or "Awakening appear");
    }

    [Fact]
    public void Awakened_Leiana_combines_what_a_pattern_runs_back_to_back()
    {
        var g = Graph(typeof(Characters.AI.TwinSister.DarkAideAI));
        // RunPattern(MeteorGround) = CastBackstep + CastMeteorInGround2.
        Assert.Equal("_backStepTeleport/_teleportStart _backStepTeleport/_teleportEnd _meteorInGround2Ready _meteorInGround2Attack _meteorInGround2Landing _meteorInGround2Standing",
            Steps(g, "Meteor in ground 2"));
        Assert.DoesNotContain(g.Attacks, a => a.Label == "Backstep");
        Assert.Equal("_darkRush/_teleportBehind/_teleport/_teleportStart _darkRush/_teleportBehind/_teleport/_teleportEnd _darkRush/_fristAttack _darkRush/_secondAttack _darkRush/_finishAttack _darkRush/_standing",
            Steps(g, "Rush"));
        Assert.Equal(6, g.Attacks.Count);
        Assert.Contains(g.Units, u => u.Dispatcher && u.Method.Name == "RunPattern");
    }

    [Fact]
    public void Chimera_attacks_are_its_animations()
    {
        // Chimera plays animation infos (ChimeraAnimation._phase1.bite) instead of starting actions.
        var g = Graph(typeof(Characters.AI.Chimera.Chimera));
        Assert.Contains("bite", Steps(g, "Bite"));
        Assert.Contains("venomBreath", Steps(g, "Venom breath"));
        Assert.DoesNotContain(g.Attacks, a => a.Label is "Idle" or "Skippable idle" or "Combat");
        Assert.Contains(g.Units, u => u.Dispatcher && u.Method?.Name == "RunPattern");
    }
}
