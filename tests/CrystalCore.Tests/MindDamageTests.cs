using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Mind-damage to docs/tower-design/effect-vocab/ops/primitives/mind-damage.md — damage
/// routed to the Mind meter instead of HP, with the op's quantity as its magnitude.
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

        Assert.Equal(100 - 6.5, enemy.Mind, Eps);
    }

    [Fact]
    public void ItTouchesNothingButR()
    {
        // Not a state and not HP: nothing to stack, nothing to tick, nothing queued for the bar.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 130, rules);

        Assert.Equal(0, enemy.TakeHpDamage(), Eps);
        Assert.Empty(enemy.ActiveStates);
    }

    [Fact]
    public void DrainsFloorAtZero_AndNothingRefillsThem()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 10000, rules);
        Assert.Equal(0, enemy.Mind, Eps);

        enemy.Tick(60.0, rules);
        Assert.Equal(0, enemy.Mind, Eps);   // the drain is permanent for the enemy's life
    }

    [Fact]
    public void DrainsAccumulateAcrossShots()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        for (int i = 0; i < 4; i++) Hit(enemy, 100, rules);

        Assert.Equal(80, enemy.Mind, Eps);
    }

    [Fact]
    public void VitalsArrivingLateStillStartTheMeterFull()
    {
        // The owner builds the state on construction and learns the real numbers in _Ready, so a
        // meter stored as a running total would be left holding the default's worth. Mind is
        // derived from damage taken instead.
        EnemyState enemy = new EnemyState();

        enemy.Vitals = new EnemyVitals(MaxHp: 200, MaxMind: 400);

        Assert.Equal(400, enemy.Mind, Eps);
    }

    [Fact]
    public void ChangesRaiseMindChanged_SoBarsNeverPoll()
    {
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();
        int raised = 0;
        enemy.MindChanged += () => raised++;

        enemy.Vitals = new EnemyVitals(MaxMind: 200);   // the meter is measured against this
        Assert.Equal(1, raised);

        Hit(enemy, 100, rules);
        Assert.Equal(2, raised);

        Hit(enemy, 0, rules);                            // nothing happened, nothing announced
        Assert.Equal(2, raised);
    }

    [Fact]
    public void OverkillIsNotBankedAgainstALaterBiggerPool()
    {
        // The meter is emptied, never put into debt: a huge drain cannot pre-pay for a mind that
        // grows afterwards.
        CombatRules rules = Rules();
        EnemyState enemy = Enemy();

        Hit(enemy, 100000, rules);
        Assert.Equal(0, enemy.Mind, Eps);

        enemy.Vitals = new EnemyVitals(MaxMind: 500);
        Assert.Equal(400, enemy.Mind, Eps);   // the 100 it actually had, gone; the rest intact
    }

    [Fact]
    public void ATougherMindTakesLongerToBreak()
    {
        // Mind is innate and per-enemy, so the same shot is worth proportionally less against a
        // bigger meter. Magnitude is absolute, unlike Corrode's percentage.
        CombatRules rules = Rules();
        EnemyState weak = Enemy(maxMind: 50);
        EnemyState tough = Enemy(maxMind: 400);

        Hit(weak, 200, rules);
        Hit(tough, 200, rules);

        Assert.Equal(40, weak.Mind, Eps);
        Assert.Equal(390, tough.Mind, Eps);
    }
}
