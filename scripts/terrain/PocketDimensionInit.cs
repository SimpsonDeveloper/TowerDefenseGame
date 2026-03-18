using Godot;

namespace towerdefensegame;

/// <summary>
/// Initializes the pocket dimension's terrain gen nodes before their _Ready() is called.
/// Must appear before TerrainGen in the scene tree so its _Ready() runs first.
/// </summary>
public partial class PocketDimensionInit : Node
{
    [Export] public TerrainGen TerrainGen { get; set; }
    [Export] public SimplexGen GrassGen { get; set; }

    // Grass config — matches the overworld grass defaults.
    // These can also be driven by SimplexGenData .tres resources if desired.
    [Export] public SimplexGenData GrassConfig { get; set; }

    // Master terrain noise config (all values will still produce grass
    // since GenRanges maps the full noise range to grass only).
    [Export] public SimplexGenData TerrainConfig { get; set; }

    public override void _Ready()
    {
        float gFreq = GrassConfig?.Frequency ?? 0.93f;
        float gOct  = GrassConfig?.Octaves   ?? 2f;
        float gLac  = GrassConfig?.Lacunarity ?? 1.0f;
        float gGain = GrassConfig?.Gain       ?? 0.1f;

        float tFreq = TerrainConfig?.Frequency ?? 0.006f;
        float tOct  = TerrainConfig?.Octaves   ?? 3f;
        float tLac  = TerrainConfig?.Lacunarity ?? 3.1f;
        float tGain = TerrainConfig?.Gain       ?? 0.32f;

        GrassGen.InitNoiseConfig(gFreq, gOct, gLac, gGain);
        TerrainGen.InitNoiseConfig(tFreq, tOct, tLac, tGain);
    }
}
