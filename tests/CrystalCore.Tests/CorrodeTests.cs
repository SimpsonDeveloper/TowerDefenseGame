using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Corrode to docs/tower-design/effect-vocab/ops/primitives/corrode.md — stacks that bank
/// until a threshold, then spend themselves on a fixed bout eating a percentage of max HP.
///
/// Tunables are pinned here rather than taken from the shipped defaults, so retuning the feel
/// never turns these red. The shape being locked is charge-then-discharge, not the numbers.
/// </summary>
public class CorrodeTests
{
    private const double Eps = 1e-9;

    private static readonly CorrodeTuning Tuning = new CorrodeTuning(
        StacksPerEnergy: 1.0 / 20.0,
        BoutThreshold: 10,
        BoutTicks: 10,
        DamageFraction: 0.02,
        TickInterval: 0.5);

    private static CombatRules Rules() => new CombatRules().Add(new Corrode(Tuning));

    private static EnemyState Enemy(double maxHp = 200) => new EnemyState(new EnemyVitals(maxHp));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.Corrode, quantity) }, enemy, rules);

    [Fact]
    public void StacksBankHarmlesslyBelowTheThreshold()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 128, rules);   // 7 stacks — under 10

        Assert.Equal(7, enemy.Stacks(StateId.Corrode));
        Assert.False(enemy.IsActive(StateId.Corroding));

        enemy.Tick(10.0, rules);

        Assert.Equal(0, enemy.TakeHpDamage(), Eps);   // banked acid does nothing on its own
        Assert.Equal(7, enemy.Stacks(StateId.Corrode));
    }

    [Fact]
    public void CrossingTheThresholdOpensABout_AndSpendsExactlyTheThreshold()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 128, rules);   // 7
        Hit(enemy, 128, rules);   // 14 banked → bout opens, costing 10

        Assert.Equal(10, enemy.Stacks(StateId.Corroding));   // ticks of acid left
        Assert.Equal(4, enemy.Stacks(StateId.Corrode));      // overflow stays as credit
    }

    [Fact]
    public void EachBoutTickEatsAPercentageOfMaxHp()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        Hit(enemy, 200, rules);   // exactly 10 stacks: the bout opens and the bank empties

        enemy.Tick(0.5, rules);

        Assert.Equal(4, enemy.TakeHpDamage(), Eps);         // 2% of 200
        Assert.Equal(9, enemy.Stacks(StateId.Corroding));
    }

    [Fact]
    public void ABoutEatsTheSameShareOfABiggerPool()
    {
        // The reason Corrode exists: flat damage scales badly against a big health pool and a
        // percentage does not.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 5000);
        Hit(enemy, 200, rules);   // exactly 10 stacks: the bout opens and the bank empties

        enemy.Tick(0.5, rules);

        Assert.Equal(100, enemy.TakeHpDamage(), Eps);
    }

    [Fact]
    public void ABoutRunsItsTicksAndEnds()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        Hit(enemy, 200, rules);   // exactly 10 stacks: the bout opens and the bank empties

        enemy.Tick(5.0, rules);   // ten ticks at 0.5s

        Assert.False(enemy.IsActive(StateId.Corroding));
        Assert.Equal(40, enemy.TakeHpDamage(), Eps);   // 10 × 2% of 200 = a fifth of the enemy
    }

    [Fact]
    public void ABoutEndingWithEnoughBanked_ChainsStraightIntoTheNext()
    {
        // Sustained fire should not waste overflow waiting for another shot to land.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        Hit(enemy, 500, rules);   // 25 stacks: bout one opens, 15 left banked

        enemy.Tick(5.0, rules);   // bout one finishes

        Assert.True(enemy.IsActive(StateId.Corroding));   // bout two opened on its own
        Assert.Equal(5, enemy.Stacks(StateId.Corrode));
    }

    [Fact]
    public void AnExhaustedBankLeavesNothingBehind()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy(maxHp: 200);
        Hit(enemy, 200, rules);   // exactly 10 stacks: the bout opens and the bank empties

        enemy.Tick(20.0, rules);

        Assert.False(enemy.IsActive(StateId.Corroding));
        Assert.False(enemy.IsActive(StateId.Corrode));
        Assert.Equal(40, enemy.TakeHpDamage(), Eps);   // one bout only, not a loop
    }

    [Fact]
    public void BurnAndCorrodeRunOnTheSameEnemyWithoutTouching()
    {
        // Two ticking ops, two clocks, two piles. The registry walks them independently.
        CombatRules rules = new CombatRules().Add(new Burn()).Add(new Corrode(Tuning));
        EnemyState enemy = Enemy(maxHp: 200);

        ShotResolver.Resolve(
            new List<ShotOp> { new ShotOp(OpId.Burn, 122), new ShotOp(OpId.Corrode, 300) },
            enemy, rules);

        Assert.Equal(7, enemy.Stacks(StateId.Burn));
        Assert.Equal(10, enemy.Stacks(StateId.Corroding));   // 15 banked, 10 spent
        Assert.Equal(5, enemy.Stacks(StateId.Corrode));
    }
}
