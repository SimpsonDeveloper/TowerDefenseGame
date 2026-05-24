using Godot;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Debug convenience: drop one of these in a scene where you'd previously have
/// hand-placed an enemy. On <see cref="_Ready"/> it instantiates the shared
/// enemy scene, applies <see cref="EnemyType"/>, and spawns it at this node's
/// position — so a placed enemy is now a placed-spawner-plus-a-type, going
/// through the exact same configure-then-spawn path as a real wave.
/// </summary>
[GlobalClass]
public partial class DebugEnemySpawner : Node2D
{
    [Export] public PackedScene EnemyScene { get; set; }
    [Export] public EnemyType EnemyType { get; set; }

    /// <summary>Where spawned enemies are parented. Falls back to this node's parent.</summary>
    [Export] public Node2D EnemiesContainer { get; set; }

    public override void _Ready()
    {
        if (EnemyScene == null || EnemyType == null)
        {
            GD.PushWarning($"{Name}: EnemyScene or EnemyType not assigned — nothing spawned.");
            return;
        }

        Node parent = EnemiesContainer ?? GetParent();
        EnemyFactory.Spawn(EnemyScene, EnemyType, GlobalPosition, parent);
    }
}