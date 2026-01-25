namespace towerdefensegame;

using Godot;

[GlobalClass]
public partial class SimplexNoiseSettings : Resource
{
    // saved grass settings
    // frequency = 0.93f;
    // octaves = 2;
    // lacunarity = 1.0f;
    // gain = 0.1f;
    [Export]
    public SliderConfig FrequencyConfig { get; set; }
    
    [Export]
    public SliderConfig OctavesConfig { get; set; }
    
    [Export]
    public SliderConfig LacunarityConfig { get; set; }
    
    [Export]
    public SliderConfig GainConfig { get; set; }
}