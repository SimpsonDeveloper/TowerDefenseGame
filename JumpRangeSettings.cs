using Godot;

namespace towerdefensegame;

[GlobalClass]
public partial class JumpRangeSettings : Resource
{
    [Export]
    public SliderConfig JumpRangeMinConfig { get; set; }
    
    [Export]
    public SliderConfig JumpRangeMaxConfig { get; set; }
    
    [Export]
    public SliderConfig JumpRangeJumpToConfig { get; set; }
}