using System;
using System.Linq;
using DamageInsight.Codex;
using Xunit;
using Xunit.Abstractions;

namespace DamageInsight.Tests;

/// <summary>
/// Runs the attack analysis over every AI class of the game (types only) and prints a table: which AIs the IL
/// reading understands (Cast* coroutines) and which need another way (behaviour trees, BehaviorDesigner, Spine).
/// Run with: dotnet test --filter AttackGraphSurvey --logger "console;verbosity=detailed"
/// </summary>
public class AttackGraphSurveyTests
{
    private readonly ITestOutputHelper _out;
    public AttackGraphSurveyTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Survey_every_AI()
    {
        var ai = typeof(Characters.AI.AIController);
        var types = ai.Assembly.GetTypes().Where(t => ai.IsAssignableFrom(t) && !t.IsAbstract).OrderBy(t => t.FullName).ToList();
        foreach (var type in types)
        {
            var graph = new AttackGraph(ai, BossAttacks.StepTypes).Build(new[] { new AttackGraph.Node(type, null, type.Name) });
            var dispatchers = graph.Units.Where(u => u.Dispatcher && u.Owner.Type == type).Select(u => u.Method.Name);
            _out.WriteLine($"{type.FullName}: {graph.Attacks.Count} attacks [{string.Join(", ", graph.Attacks.Select(a => a.Label + (a.Units.Count > 1 ? "*" : "") + (a.IsTail ? "(tail)" : "")))}]" +
                           $" dispatchers [{string.Join(", ", dispatchers)}]");
        }
        Assert.NotEmpty(types);
    }
}
