using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Mark to docs/tower-design/effect-vocab/ops/primitives/mark.md — a timed tag that does
/// nothing by itself.
///
/// With no consumer written, "does nothing" is most of the specification, so most of what is
/// pinned here is absence: no damage, no second state, no stacking. The one real behaviour is the
/// clock, and the one real rule is that a re-mark can only extend it.
/// </summary>
public class MarkTests
{
    private const double Eps = 1e-9;

    private static readonly MarkTuning Tuning = new MarkTuning(
        SecondsPerEnergy: 1.0 / 30.0,
        MaxSeconds: 8.0);

    private static CombatRules Rules() => new CombatRules().Add(new Mark(Tuning));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.Mark, quantity) }, enemy, rules);

    [Fact]
    public void EnergyBuysNothingButTime()
    {
        // Am-Ru off the 150-core starter turret pays one Amethyst's toll of 20, so 130 crosses.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 130, rules);

        Assert.True(enemy.IsActive(StateId.Mark));
        Assert.Equal(130.0 / 30.0, enemy.FlagTimeLeft(StateId.Mark), Eps);
        Assert.Equal(0, enemy.Stacks(StateId.Mark));   // a flat, not a ladder
    }

    [Fact]
    public void TheWindowIsCapped()
    {
        // Uncapped, a big lattice would make the mark a property of the enemy rather than a window.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 100000, rules);

        Assert.Equal(8.0, enemy.FlagTimeLeft(StateId.Mark), Eps);
    }

    [Fact]
    public void ARemarkExtendsAndNeverShortens()
    {
        // Timed flats merge by max (merge.md): two marks are not a longer window than the longer
        // of them, and a cheap one landing second cannot cut an expensive one short.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 180, rules);   // 6s
        Assert.Equal(6.0, enemy.FlagTimeLeft(StateId.Mark), Eps);

        Hit(enemy, 60, rules);    // 2s — ignored, the standing mark is longer
        Assert.Equal(6.0, enemy.FlagTimeLeft(StateId.Mark), Eps);

        Hit(enemy, 210, rules);   // 7s — longer, so it wins outright rather than adding
        Assert.Equal(7.0, enemy.FlagTimeLeft(StateId.Mark), Eps);
    }

    [Fact]
    public void ItRunsOutOnItsOwn()
    {
        // No ticking face: the flag's clock is EnemyState's, and nothing has to remember to
        // clear it.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 90, rules);   // 3s

        enemy.Tick(2.5, rules);
        Assert.True(enemy.IsActive(StateId.Mark));

        enemy.Tick(0.6, rules);
        Assert.False(enemy.IsActive(StateId.Mark));
        Assert.Empty(enemy.ActiveStates);
    }

    [Fact]
    public void ItDoesNothingElseAtAll()
    {
        // The point of the op. Anything else showing up here means Mark grew an effect it should
        // not have — its payoff belongs to Focus and Detonate.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 130, rules);
        enemy.Tick(1.0, rules);

        Assert.Equal(0, enemy.TakeHpDamage(), Eps);
        Assert.Equal(0, enemy.TakeMindDamage(), Eps);
        Assert.Equal(0, enemy.TakeShieldDisable(), Eps);
        Assert.Equal(1.0, enemy.SpeedScale(rules), Eps);
        Assert.Equal(new[] { StateId.Mark }, enemy.ActiveStates);
    }
}
