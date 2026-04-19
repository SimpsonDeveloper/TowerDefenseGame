using Godot;

namespace towerdefensegame.scripts.world.drops;

/// <summary>
/// Marker component. A Node2D whose children include a ResourceDrop is considered a
/// valid drop that can be spawned by a Breakable on destruction or through any
/// other drop-spawning means
/// </summary>
public partial class ResourceDrop : Node2D
{
}
