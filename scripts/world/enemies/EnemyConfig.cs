using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Shared enemy configuration read by any system that needs to know about
/// enemy dimensions or stats — nav baking, tower targeting, wave balancing, etc.
/// Export this resource on any node that needs enemy parameters rather than
/// duplicating values across scripts.
/// </summary>
[GlobalClass]
public partial class EnemyConfig : Resource
{
    /// <summary>
    /// Collision radius used for navigation. The nav mesh is eroded by this
    /// amount during baking, and each enemy's NavigationAgent2D.Radius is set
    /// from it at runtime so paths match the agent's footprint.
    /// </summary>
    [Export] public float AgentRadius { get; set; } = 5f;
}
