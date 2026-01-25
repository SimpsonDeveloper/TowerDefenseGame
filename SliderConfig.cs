using Godot;

namespace towerdefensegame;

[GlobalClass]
public partial class SliderConfig : Resource
{
    [Export] public float InitialValue { get; set; }
    [Export] public float Min { get; set; }
    [Export] public float Max { get; set;}
    [Export] public float Step{ get; set;}
}