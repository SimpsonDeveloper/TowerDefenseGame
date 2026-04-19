using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// One entry in a tower's build cost: a resource type and the quantity required.
/// </summary>
[GlobalClass]
public partial class TowerCost : Resource
{
    [Export] public ResourceData Data { get; set; }
    [Export] public int Amount { get; set; } = 1;
}
