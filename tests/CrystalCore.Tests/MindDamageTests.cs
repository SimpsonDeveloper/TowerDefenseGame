using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Mind-damage to docs/tower-design/effect-vocab/ops/primitives/mind-damage.md — damage
/// routed to the Mind meter instead of HP, with the op's quantity as its magnitude.
///
/// The meter itself is a Godot <c>MindComponent</c>, so what is testable here is what the core
/// does: turn energy into an amount and queue it. Flooring at zero and firing Broken belong to the
/// component, out of reach of this project, exactly as HP's do.
/// </summary>
public class MindDamageTests
{
    private const double Eps = 1e-9;

    private static readonly MindDamageTuning Tuning = new MindDamageTuning(MindPerEnergy: 1.0 / 20.0);

    private static CombatRules Rules() => new CombatRules().Add(new MindDamage(Tuning));

    private static EnemyState Enemy(double maxMind = 100) =>
        new EnemyState(new EnemyVitals(MaxHp: 200, MaxMind: maxMind));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.MindDamage, quantity) }, enemy, rules);

    [Fact]
    public void QuantityIsTheMagnitude()
    {
        // Am-Am off the 150-core starter turret pays one Amethyst's toll of 20, so ~130 crosses.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 130, rules);

        Assert.Equal(6.5, enemy.TakeMindDamage(), Eps);
    }

    [Fact]
    public void ItTouchesNothingButMind()
    {
        // Not a state and not HP: nothing to stack, nothing to tick, nothing owed to the HP bar.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 130, rules);

        Assert.Equal(0, enemy.TakeHpDamage(), Eps);
        Assert.Empty(enemy.ActiveStates);
    }

    [Fact]
    public void NothingIsQueuedForAnEmptyShot()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 0, rules);

        Assert.Equal(0, enemy.TakeMindDamage(), Eps);
    }

    [Fact]
    public void DrainsAccumulateUntilTheMeterTakesThem()
    {
        // Queued like HP damage and for the same reason: four shots inside one frame reach the
        // meter as a single drain, not four.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        for (int i = 0; i < 4; i++) Hit(enemy, 100, rules);

        Assert.Equal(20, enemy.TakeMindDamage(), Eps);
        Assert.Equal(0, enemy.TakeMindDamage(), Eps);   // and the queue empties
    }

    [Fact]
    public void MagnitudeIsAbsolute_NotAShareOfTheMeter()
    {
        // Unlike Corrode's percentage: the same shot is worth proportionally less against a bigger
        // mind, which is what makes a tough one a real investment to break.
        CombatRules rules = Rules();
        EnemyState weak = Enemy(maxMind: 50);
        EnemyState tough = Enemy(maxMind: 400);

        Hit(weak, 200, rules);
        Hit(tough, 200, rules);

        Assert.Equal(10, weak.TakeMindDamage(), Eps);
        Assert.Equal(10, tough.TakeMindDamage(), Eps);
    }
}
