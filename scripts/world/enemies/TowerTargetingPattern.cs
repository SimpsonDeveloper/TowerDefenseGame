using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Targeting pattern that walks the enemy toward the nearest reachable tower.
/// Builds an <see cref="EnemyTowerTargeter"/>. The exports here are the single
/// source of truth for this pattern's tuning; they are copied onto the targeter
/// at build time.
/// </summary>
[GlobalClass]
public partial class TowerTargetingPattern : TargetingPattern
{
    [Export] public string TargetGroup { get; set; } = "Towers";

    /// <summary>Reach of the enemy's attack, added to agent radius for standoff.</summary>
    [Export] public float AttackRange { get; set; }

    /// <summary>Seconds between retargets. Keep above 0.1s.</summary>
    [Export] public float TargetUpdateInterval { get; set; } = 0.25f;

    /// <summary>Max age (ms) of a path-resolve result before it's discarded.</summary>
    [Export] public int MaxResultAgeMs { get; set; } = 500;

    public override EnemyTargeter Build(EnemyConfig enemyConfig) => new EnemyTowerTargeter
    {
        TargetGroup = TargetGroup,
        EnemyConfig = enemyConfig,
        AttackRange = AttackRange,
        TargetUpdateInterval = TargetUpdateInterval,
        MaxResultAgeMs = MaxResultAgeMs,
    };
}