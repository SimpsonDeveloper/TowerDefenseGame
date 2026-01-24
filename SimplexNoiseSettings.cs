namespace towerdefensegame;

using Godot;

public enum NoisePreset
{
    Default,
    Smooth,
    Rough,
    Detailed
}

public partial class SimplexNoiseSettings : Resource
{
    [Export]
    public NoisePreset Preset { get; set; }

    [Export(PropertyHint.Range, "0.01,2.0")] 
    public float Frequency { get; set;}

    [Export(PropertyHint.Range, "1,8")] 
    public float Octaves { get; set;}

    [Export(PropertyHint.Range, "0.1,4.0")] 
    public float Lacunarity { get; set;}

    [Export(PropertyHint.Range, "0.1,1.0")] 
    public float Gain { get; set;}

    public SimplexNoiseSettings()
    {
        Preset = NoisePreset.Default;
        Frequency = 0.93f;
        Octaves = 2;
        Lacunarity = 1.0f;
        Gain = 0.1f;
    }
}