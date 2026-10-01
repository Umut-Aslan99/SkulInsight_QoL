using DamageInsight.Codex;
using Xunit;

namespace DamageInsight.Tests;

public class AttackPatternTests
{
    [Theory]
    [InlineData("_goldenMeteorJump", "golden meteor")]
    [InlineData("_goldenMeteorLanding", "golden meteor")]
    [InlineData("_meteorInAirJumpAndReady", "meteor in air")]
    [InlineData("_meteorInAirLandingAndStanding", "meteor in air")]
    [InlineData("_meteorInGroundAttackOnHardmode", "meteor in ground")]
    [InlineData("_meteorInGround2Standing", "meteor in ground2")]
    [InlineData("_rushA", "rush")]
    [InlineData("_rushReady", "rush")]
    [InlineData("_rushStanding", "rush")]
    [InlineData("_twinMeteor", "twin meteor")]
    [InlineData("_twinMeteorPreparing", "twin meteor")]
    [InlineData("_risingPierceAttackAndEnd", "rising pierce")]
    [InlineData("_dimensionPierce", "dimension pierce")]
    [InlineData("_dimensionPierceCoolTimeAction", "dimension pierce")]
    [InlineData("_backStep", "back step")]
    [InlineData("_fristAttack", "")]
    [InlineData("_standing", "")]
    public void Field_names_give_the_attack(string field, string pattern) =>
        Assert.Equal(pattern, AttackPatterns.PatternOf(field));
}
