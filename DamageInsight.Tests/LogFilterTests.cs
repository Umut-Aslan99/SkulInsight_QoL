using Characters;
using DamageInsight.Recording;
using Xunit;

namespace DamageInsight.Tests;

public class LogFilterTests
{
    private const int CurrentRoom = 2;

    [Fact]
    public void Default_ShowsOnlyYourHitsInTheCurrentRoom()
    {
        var filter = new LogFilter();

        Assert.True(filter.Passes(Hit.Make(room: CurrentRoom), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(room: 1), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(room: CurrentRoom, attacker: EntityKind.TrashMob, target: EntityKind.Player), CurrentRoom));
    }

    [Fact]
    public void Dealt_IncludesYourMinions()
    {
        var filter = new LogFilter { Scope = LogScope.All };
        Assert.True(filter.Passes(Hit.Make(attacker: EntityKind.PlayerMinion), CurrentRoom));
    }

    [Fact]
    public void Taken_ShowsOnlyHitsOnYou()
    {
        var filter = new LogFilter { Direction = LogDirection.Taken, Scope = LogScope.All };

        Assert.True(filter.Passes(Hit.Make(attacker: EntityKind.Boss, target: EntityKind.Player), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(), CurrentRoom));
    }

    [Fact]
    public void EnemyFilter_UsesTheOtherParty()
    {
        var filter = new LogFilter { Direction = LogDirection.All, Scope = LogScope.All };
        filter.EnemyKinds.Clear();
        filter.EnemyKinds.Add(EntityKind.Elite);

        // You hit an elite: the target counts.
        Assert.True(filter.Passes(Hit.Make(target: EntityKind.Elite), CurrentRoom));
        // An elite hits you: the attacker counts.
        Assert.True(filter.Passes(Hit.Make(attacker: EntityKind.Elite, target: EntityKind.Player), CurrentRoom));
        // Anything involving a regular enemy is hidden.
        Assert.False(filter.Passes(Hit.Make(target: EntityKind.TrashMob), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(attacker: EntityKind.TrashMob, target: EntityKind.Player), CurrentRoom));
    }

    [Fact]
    public void SourceAndTypeFilters()
    {
        var filter = new LogFilter { Scope = LogScope.All };
        filter.Sources.Remove(DamageSource.Poison);
        filter.Attributes.Remove(Damage.Attribute.Magic);

        Assert.False(filter.Passes(Hit.Make(source: DamageSource.Poison), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(attribute: Damage.Attribute.Magic), CurrentRoom));
        Assert.True(filter.Passes(Hit.Make(source: DamageSource.Burn, attribute: Damage.Attribute.Physical), CurrentRoom));
    }

    [Fact]
    public void CritsOnlyAndMinimumDamage()
    {
        var filter = new LogFilter { Scope = LogScope.All, CritsOnly = true, MinDamage = 50 };

        Assert.True(filter.Passes(Hit.Make(amount: 50, crit: true), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(amount: 49, crit: true), CurrentRoom));
        Assert.False(filter.Passes(Hit.Make(amount: 500, crit: false), CurrentRoom));
    }

    [Fact]
    public void Apply_KeepsOrder()
    {
        var filter = new LogFilter { Scope = LogScope.All };
        var shown = filter.Apply(new[] { Hit.Make(amount: 1), Hit.Make(amount: 2, target: EntityKind.Player, attacker: EntityKind.Boss), Hit.Make(amount: 3) }, CurrentRoom);

        Assert.Equal(new[] { 1.0, 3.0 }, shown.ConvertAll(r => r.Amount));
    }
}
