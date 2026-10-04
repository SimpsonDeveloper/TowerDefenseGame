using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Chill to docs/tower-design/effect-vocab/ops/primitives/chill-freeze.md — stacks that slow
/// while held, and freeze outright past a threshold that scales with the enemy's health.
///
/// Tunables are pinned here rather than taken from the shipped defaults, so retuning the feel
/// never turns these red.
/// </summary>
public class ChillTests
{
    private const double Eps = 1e-9;

    private static readonly ChillTuning Tuning = new ChillTuning(
        StacksPerEnergy: 1.0 / 20.0,
        SlowPerStack: 0.03,
        MinSpeedScale: 0.25,
        FreezeThreshold: 20,
        ReferenceMaxHp: 200,
        FreezeDuration: 2.0,
        DecayPerTick: 1,
        TickInterval: 1.0);

    private static CombatRules Rules() => new CombatRules().Add(new Chill(Tuning)).Add(new Frozen());

    private static EnemyState Enemy(double maxHp = 200) =>
        new EnemyState(new EnemyVitals(maxHp, MaxMind: 100, MoveSpeed: 220));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.ChillFreeze, quantity) }, enemy, rules);

    [Fact]
    public void StacksSlowWhileHeld()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Assert.Equal(1.0, enemy.SpeedScale(rules), Eps);

        Hit(enemy, 134, rules);   // 7 stacks

        Assert.Equal(7, enemy.Stacks(StateId.Chill));
        Assert.Equal(1 - 7 * 0.03, enemy.SpeedScale(rules), Eps);
    }

    [Fact]
    public void ChillAloneNeverStopsAnEnemy()
    {
        // Stopping is Freeze's job and has to be earned; a deep chill bottoms out instead.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 100000);   // threshold far out of reach, so no freeze
        enemy.AddStacks(StateId.Chill, 500);

        Assert.Equal(0.25, enemy.SpeedScale(rules), Eps);
    }

    [Fact]
    public void CrossingTheThresholdFreezes_AndSpendsTheThreshold()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);   // threshold 20
        enemy.AddStacks(StateId.Chill, 5);

        Hit(enemy, 400, rules);   // +20 → 25 banked, freeze costs 20

        Assert.True(enemy.IsActive(StateId.Freeze));
        Assert.Equal(5, enemy.Stacks(StateId.Chill));    // overstack survives as credit
        Assert.Equal(0, enemy.SpeedScale(rules), Eps);   // stopped dead
    }

    [Fact]
    public void ABiggerEnemyIsHarderToFreeze()
    {
        // Chill's defining asymmetry: the cost is proportional to the health pool.
        Chill chill = new Chill(Tuning);

        Assert.Equal(20, chill.ThresholdFor(Enemy(maxHp: 200)));
        Assert.Equal(100, chill.ThresholdFor(Enemy(maxHp: 1000)));
        Assert.Equal(1, chill.ThresholdFor(Enemy(maxHp: 1)));   // never free, never zero
    }

    [Fact]
    public void FreezingDoesNotLockTheEnemyForever()
    {
        // The threshold is SPENT, so an expired freeze cannot immediately refreeze on the same
        // stacks that bought the first one.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        enemy.AddStacks(StateId.Chill, 20);
        Hit(enemy, 0.1, rules);   // +1, crosses, freeze spends 20 → 1 left

        enemy.Tick(3.0, rules);   // freeze (2s) expires, chill decays

        Assert.False(enemy.IsActive(StateId.Freeze));
        Assert.Equal(1.0, enemy.SpeedScale(rules), Eps);   // and the slow is gone with the stacks
    }

    [Fact]
    public void EnoughOverstackRefreezesWithoutAnotherShot()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        enemy.AddStacks(StateId.Chill, 45);
        Hit(enemy, 0.1, rules);   // 46 → freeze one, 26 left

        enemy.Tick(3.0, rules);   // freeze expires; decay has taken a few stacks

        Assert.True(enemy.IsActive(StateId.Freeze));   // froze again off the overstack
    }

    [Fact]
    public void StacksDecayWhenNothingRefreshesThem()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 100, rules);   // 5 stacks
        enemy.Tick(5.0, rules);   // one a second

        Assert.False(enemy.IsActive(StateId.Chill));
        Assert.Equal(1.0, enemy.SpeedScale(rules), Eps);
    }

    [Fact]
    public void SlowsCompound_TheyDoNotOverwriteEachOther()
    {
        // SpeedScale multiplies every active modifier, so a second source of slow stacks with the
        // first rather than replacing it. Freeze on top of chill is still a full stop.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        enemy.AddStacks(StateId.Chill, 25);
        enemy.SetFlag(StateId.Freeze, 1.0);

        Assert.Equal(0, enemy.SpeedScale(rules), Eps);
    }

    [Fact]
    public void AnUnregisteredModifierLeavesSpeedAlone()
    {
        EnemyState enemy = Enemy();
        enemy.AddStacks(StateId.Chill, 10);

        Assert.Equal(1.0, enemy.SpeedScale(new CombatRules()), Eps);
    }
}
