using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Scramble to docs/tower-design/effect-vocab/ops/primitives/scramble.md — an EMP that
/// switches a shield off without spending it.
///
/// The shield itself is a Godot <c>ShieldComponent</c>, so what is testable here is the core's
/// half: energy becomes a shutdown in seconds, queued out. Absorbing damage, counting the timer
/// down and deciding a unit has no shield to disable all belong to the component.
/// </summary>
public class ScrambleTests
{
    private const double Eps = 1e-9;

    private static readonly ScrambleTuning Tuning = new ScrambleTuning(
        SecondsPerEnergy: 1.0 / 40.0,
        MaxSeconds: 6.0);

    private static CombatRules Rules() => new CombatRules().Add(new Scramble(Tuning));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.Scramble, quantity) }, enemy, rules);

    [Fact]
    public void EnergyBecomesSecondsOfShutdown()
    {
        // Ci-Ci off the 150-core starter turret pays one Citrine's toll of 12, so ~138 crosses.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 138, rules);

        Assert.Equal(3.45, enemy.TakeShieldDisable(), Eps);
    }

    [Fact]
    public void ShutdownIsCapped()
    {
        // A huge lattice should not switch a shield off for the whole wave.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 100000, rules);

        Assert.Equal(6.0, enemy.TakeShieldDisable(), Eps);
    }

    [Fact]
    public void TheLongestShutdownWins_TheyDoNotAddUp()
    {
        // A timed flat merges by max (merge.md). Two scrambles in one frame are not eight seconds.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 160, rules);   // 4s
        Hit(enemy, 80, rules);    // 2s

        Assert.Equal(4.0, enemy.TakeShieldDisable(), Eps);
        Assert.Equal(0, enemy.TakeShieldDisable(), Eps);   // and the queue empties
    }

    [Fact]
    public void ItDealsNoDamageAndWritesNoState()
    {
        // Shield-down is mirrored back from the shield itself, not written here: the shield goes
        // down two ways and comes back up on its own timer.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 138, rules);

        Assert.Equal(0, enemy.TakeHpDamage(), Eps);
        Assert.Equal(0, enemy.TakeMindDamage(), Eps);
        Assert.Empty(enemy.ActiveStates);
    }
}
