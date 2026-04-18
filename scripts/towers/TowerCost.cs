using Godot;

namespace towerdefensegame;

/// <summary>
/// One entry in a tower's build cost: a resource type and the quantity required.
/// </summary>
[GlobalClass]
public partial class TowerCost : Resource
{
    [Export] public ResourceVariant Variant { get; set; }
    [Export] public int Amount { get; set; } = 1;
}
