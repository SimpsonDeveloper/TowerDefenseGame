using Godot;

namespace towerdefensegame;

/// <summary>
/// Resource that stores simplex noise configuration values.
/// Save as a .tres file to share terrain configs between worlds.
/// </summary>
[GlobalClass]
public partial class SimplexGenData : Resource
{
    [Export] public string Name { get; set; }
    [Export] public float Frequency { get; set; } = 0.93f;
    [Export] public float Octaves { get; set; } = 2f;
    [Export] public float Lacunarity { get; set; } = 1.0f;
    [Export] public float Gain { get; set; } = 0.1f;
}
