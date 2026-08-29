using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Burn to docs/tower-design/effect-vocab/ops/primitives/burn.md — an uncapped pile of
/// stacks that damages by its current size and burns itself down.
///
/// Tunables are pinned here rather than taken from the shipped defaults, so retuning the feel
/// never turns these red. The shape being locked is the curve, not the numbers.
/// </summary>
public class BurnTests
{
    private const double Eps = 1e-9;

    private static readonly BurnTuning Tuning = new BurnTuning(
        StacksPerEnergy: 1.0 / 20.0,
        DamagePerStack: 1.0,
        DecayFraction: 0.2,
        DecayBase: 1,
        TickInterval: 1.0);

    private static CombatRules Rules() => new CombatRules().Add(new Burn(Tuning));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.Burn, quantity) }, enemy, rules);

    [Fact]
    public void EnergyBecomesStacks_RoundedUp()
    {
        // A bare Ruby pair off a 600-energy core delivers ~572 across the edge.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 572, rules);

        Assert.Equal(29, enemy.Stacks(StateId.Burn));
    }

    [Fact]
    public void AnyEnergyAtAllIsWorthOneStack()
    {
        // Truncating 0.3 energy to 0 stacks would read as a broken tower, not a weak one.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 0.3, rules);

        Assert.Equal(1, enemy.Stacks(StateId.Burn));
    }

    [Fact]
    public void StacksSumAcrossHits_WithNoCap()
    {
        // merge.md: stacks state-merge by SUM. Nothing clamps them — the decay curve is what
        // keeps a big pile from lasting forever.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 200, rules);    // 10
        Hit(enemy, 200, rules);    // 10
        Hit(enemy, 20000, rules);  // 1000

        Assert.Equal(1020, enemy.Stacks(StateId.Burn));
    }

    [Fact]
    public void DamagePerTickFollowsTheStandingStacks_SoItFadesAsItBurns()
    {
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();
        Hit(enemy, 580, rules);   // 29 stacks

        // 29 damage, then 1 + floor(29 × 0.2) = 6 stacks burnt off.
        enemy.Tick(1.0, rules);
        Assert.Equal(29, enemy.TakeHpDamage(), Eps);
        Assert.Equal(23, enemy.Stacks(StateId.Burn));

        // Smaller pile, smaller tick: 23 damage, 1 + 4 off.
        enemy.Tick(1.0, rules);
        Assert.Equal(23, enemy.TakeHpDamage(), Eps);
        Assert.Equal(18, enemy.Stacks(StateId.Burn));
    }

    [Fact]
    public void TheBurnEndsWhenItHasEatenItself()
    {
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();
        Hit(enemy, 580, rules);   // 29 stacks

        // 29 → 23 → 18 → 14 → 11 → 8 → 6 → 4 → 3 → 2 → 1 → 0. The flat base is what closes the
        // tail out; a fraction of 3 truncates to nothing on its own.
        for (int i = 0; i < 11; i++) enemy.Tick(1.0, rules);

        Assert.False(enemy.IsActive(StateId.Burn));
        Assert.Equal(119, enemy.TakeHpDamage(), Eps);
    }

    [Fact]
    public void ABiggerPileBurnsHarder_NotProportionallyLonger()
    {
        // The point of proportional decay: 10× the stacks costs only a handful of extra ticks.
        Assert.Equal(6, TicksToBurnOut(10));
        Assert.Equal(11, TicksToBurnOut(29));
        Assert.Equal(16, TicksToBurnOut(100));
    }

    [Fact]
    public void FractionalDamageSurvives_ItIsNotRounded()
    {
        // Half a point a tick has to actually land — HealthComponent takes a double for exactly
        // this reason.
        CombatRules rules = new CombatRules().Add(new Burn(Tuning with { DamagePerStack = 0.5 }));
        EnemyState enemy = new EnemyState();
        enemy.AddStacks(StateId.Burn, 1);

        enemy.Tick(1.0, rules);

        Assert.Equal(0.5, enemy.TakeHpDamage(), Eps);
    }

    [Fact]
    public void OneLongFrameOwesEveryTickItPassed()
    {
        // Three intervals in one frame is three ticks, not one — otherwise lag is free healing.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();
        Hit(enemy, 580, rules);

        enemy.Tick(3.0, rules);

        Assert.Equal(29 + 23 + 18, enemy.TakeHpDamage(), Eps);
    }

    [Fact]
    public void TheFirstTickWaitsAFullInterval()
    {
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();
        Hit(enemy, 580, rules);

        enemy.Tick(0.5, rules);
        Assert.Equal(0, enemy.TakeHpDamage(), Eps);

        enemy.Tick(0.5, rules);
        Assert.Equal(29, enemy.TakeHpDamage(), Eps);
    }

    [Fact]
    public void AStateWithNoRegisteredOpIsCarriedButNeverTicks()
    {
        // The missing-handler rule covers ticking too: Chill is written but unimplemented, so it
        // sits on the enemy waiting for a consumer instead of quietly doing nothing forever.
        EnemyState enemy = new EnemyState();
        enemy.AddStacks(StateId.Chill, 5);

        enemy.Tick(10.0, new CombatRules());

        Assert.Equal(5, enemy.Stacks(StateId.Chill));
        Assert.Equal(0, enemy.TakeHpDamage(), Eps);
    }

    private static int TicksToBurnOut(int stacks)
    {
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();
        enemy.AddStacks(StateId.Burn, stacks);

        int ticks = 0;
        while (enemy.IsActive(StateId.Burn) && ticks < 1000)
        {
            enemy.Tick(1.0, rules);
            ticks++;
        }

        return ticks;
    }
}
