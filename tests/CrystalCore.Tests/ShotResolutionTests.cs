using System.Collections.Generic;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// The pipeline's far end: spending a compiled shot on an enemy
/// (docs/tower-design/impl-planning/combat/primitives.md).
///
/// The point being locked is that an unwritten op costs nothing — 1 of 21 ops exists and a shot
/// still resolves — plus the ordering guarantee interactives will be built on.
/// </summary>
public class ShotResolutionTests
{
    /// <summary>Records that it ran and what it was handed, without doing anything to the enemy.</summary>
    private sealed class SpyOp : IOp
    {
        private readonly List<int> _log;

        public SpyOp(OpId id, List<int> log)
        {
            Id = id;
            _log = log;
        }

        public OpId Id { get; }

        public void Apply(ShotContext context, double quantity, EnemyState target) => _log.Add(context.Index);
    }

    [Fact]
    public void UnwrittenOps_AreSkipped_SoAShotResolvesBeforeAllOpsExist()
    {
        EnemyState enemy = new EnemyState();

        // Everything except Burn is unimplemented today. None of it may throw.
        List<ShotOp> shot = new List<ShotOp>
        {
            new ShotOp(OpId.Frostburn, 5),
            new ShotOp(OpId.Burn, 3),
            new ShotOp(OpId.Shatter, 9),
        };

        ShotResolver.Resolve(shot, enemy);

        Assert.True(enemy.Stacks(StateId.Burn) > 0);   // the one op that IS written still ran
    }

    [Fact]
    public void OpsRunInListOrder_WhichIsWhatInteractivesReadState()
    {
        List<int> ran = new List<int>();
        CombatRules rules = new CombatRules()
            .Add(new SpyOp(OpId.Burn, ran))
            .Add(new SpyOp(OpId.Frostburn, ran));

        List<ShotOp> shot = new List<ShotOp>
        {
            new ShotOp(OpId.Burn, 1),
            new ShotOp(OpId.Frostburn, 1),
        };

        ShotResolver.Resolve(shot, new EnemyState(), rules);

        Assert.Equal(new[] { 0, 1 }, ran);
    }

    [Fact]
    public void VitalsAreWhatAnOpMayReadAboutTheEnemy()
    {
        // Chill scales its freeze threshold off max HP and Corrode ticks a percentage of it, so
        // the stat has to reach an op. Current HP deliberately does not — damage only goes out.
        EnemyState enemy = new EnemyState(new EnemyVitals(MaxHp: 750, MaxR: 40));

        Assert.Equal(750, enemy.Vitals.MaxHp);
        Assert.Equal(40, enemy.R);   // the meter starts full, from the stat
    }

    [Fact]
    public void ACompiledLatticeLandsOnAnEnemy()
    {
        // The whole path, end to end: two Rubies compile to a Burn, and that Burn is stacks on
        // an enemy. op-flow.md's worked example, cashed in.
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);
        lat.Place(0, 1, CrystalKind.Ruby);

        // Shipping costs, and enough core to clear them — an edge in debt produces no op at all.
        CompileResult result = Compiler.Compile(lat, 200);
        EnemyState enemy = new EnemyState();

        ShotResolver.Resolve(result.Shot, enemy);

        Assert.Equal(OpId.Burn, Assert.Single(result.Shot).Op);
        Assert.True(enemy.Stacks(StateId.Burn) > 0);
    }
}
