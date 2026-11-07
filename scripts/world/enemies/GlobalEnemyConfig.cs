using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Shared enemy configuration read by any system that needs to know about
/// enemy dimensions or stats — nav baking, tower targeting, wave balancing, etc.
/// Export this resource on any node that needs enemy parameters rather than
/// duplicating values across scripts.
/// </summary>
public static class GlobalEnemyConfig
{
    /// <summary>
    /// Collision radius used for navigation. The nav mesh is eroded by this
    /// amount during baking, and each enemy's hitbox circle is set from it at
    /// runtime so paths match the enemy's footprint.
    /// </summary>
    public static float AgentRadius => 10f;
}
