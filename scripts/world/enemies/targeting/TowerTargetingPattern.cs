using Godot;

namespace towerdefensegame.scripts.world.enemies.targeting;

/// <summary>
/// Targeting pattern that walks the enemy toward the nearest reachable tower.
/// Builds an <see cref="EnemyStructureTargeter"/> pointed at the <c>Towers</c> group. The exports
/// here are the single source of truth for this pattern's tuning; they are copied onto the
/// targeter at build time.
/// </summary>
[GlobalClass]
public partial class TowerTargetingPattern : TargetingPattern
{
    public override EnemyTargeter Build(EnemyType enemyType) => new EnemyStructureTargeter
    {
        TargetGroup = "Towers",
        AttackRange = enemyType.AttackRange,
    };
}
