using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks Purify to docs/tower-design/effect-vocab/ops/primitives/purify.md — a cleanse that
/// switches an enemy's buffs off for a time.
///
/// The buff itself is a Godot <c>HuddleRegenComponent</c>, so what is testable here is the core's
/// half: energy becomes a shutdown in seconds, queued out. Counting neighbours, running the timer
/// down and actually withholding the healing all belong to the component — the same split
/// Scramble has against the shield.
/// </summary>
public class PurifyTests
{
    private const double Eps = 1e-9;

    private static readonly PurifyTuning Tuning = new PurifyTuning(
        SecondsPerEnergy: 1.0 / 24.0,
        MaxSeconds: 12.0);

    private static CombatRules Rules() => new CombatRules().Add(new Purify(Tuning));

    private static void Hit(EnemyState enemy, double quantity, CombatRules rules) =>
        ShotResolver.Resolve(new List<ShotOp> { new ShotOp(OpId.Purify, quantity) }, enemy, rules);

    [Fact]
    public void EnergyBecomesSecondsOfShutdown()
    {
        // Qz-Qz off the 150-core starter turret pays one Quartz's toll of 6 — the cheapest crystal
        // in the set — so 144 crosses. The catalyst keeping nearly all of it is the point.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 144, rules);

        Assert.Equal(6.0, enemy.TakeBuffSuppress(), Eps);
    }

    [Fact]
    public void ShutdownIsCapped()
    {
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 100000, rules);

        Assert.Equal(12.0, enemy.TakeBuffSuppress(), Eps);
    }

    [Fact]
    public void TheLongestShutdownWins_TheyDoNotAddUp()
    {
        // A timed flat merges by max (merge.md). Two purifies in one frame are not their sum, and
        // a cheap one cannot cut a long one short.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        Hit(enemy, 240, rules);   // 10s
        Hit(enemy, 48, rules);    // 2s

        Assert.Equal(10.0, enemy.TakeBuffSuppress(), Eps);
        Assert.Equal(0, enemy.TakeBuffSuppress(), Eps);   // and the queue empties
    }

    [Fact]
    public void ItStripsNothingAndDealsNoDamage()
    {
        // The design decision, pinned: Purify does not clear states. Clearing a derived buff would
        // be undone by the next recount, so the op's entire output is the queued shutdown.
        CombatRules rules = Rules();
        EnemyState enemy = new EnemyState();

        enemy.AddStacks(StateId.Burn, 5);
        enemy.SetFlag(StateId.Mark, 4);
        enemy.SetFlag(StateId.Regenerating, 0);

        Hit(enemy, 144, rules);

        Assert.Equal(5, enemy.Stacks(StateId.Burn));
        Assert.Equal(4, enemy.FlagTimeLeft(StateId.Mark), Eps);
        Assert.True(enemy.IsActive(StateId.Regenerating));   // the component decides, not the op

        Assert.Equal(0, enemy.TakeHpDamage(), Eps);
        Assert.Equal(0, enemy.TakeMindDamage(), Eps);
        Assert.Equal(0, enemy.TakeShieldDisable(), Eps);
    }

    [Fact]
    public void EveryPrimitiveIsWired()
    {
        // Purify was the last of the seven. Registration is the whole of "built" for an op, so
        // this is the honest end-of-item-4 check.
        CombatRules rules = CombatRules.Default;

        foreach (OpId op in new[]
                 {
                     OpId.Burn, OpId.ChillFreeze, OpId.Corrode, OpId.Mark,
                     OpId.Scramble, OpId.MindDamage, OpId.Purify,
                 })
            Assert.NotNull(rules.Op(op));
    }
}
