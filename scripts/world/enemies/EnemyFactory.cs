using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Single place that instantiates the shared enemy scene, applies an
/// <see cref="EnemyType"/>, and parents it. Centralized so the wave spawner and
/// the debug spawner agree on the critical ordering: configure BEFORE entering
/// the tree (see <see cref="EnemyNavController.ApplyType"/>).
/// </summary>
public static class EnemyFactory
{
    public static Node2D Spawn(PackedScene scene, EnemyType type, Vector2 globalPosition, Node parent)
    {
        var enemy = scene.Instantiate<Node2D>();
        if (enemy is EnemyNavController controller)
            controller.ApplyType(type);
        enemy.GlobalPosition = globalPosition;
        parent.AddChild(enemy);
        return enemy;
    }
}
