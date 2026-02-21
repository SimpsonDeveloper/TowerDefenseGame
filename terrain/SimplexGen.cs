using System;
using Godot;

namespace towerdefensegame;

[GlobalClass]
public partial class SimplexGen : Node, ISimplexGenConfigurable
{
    [Export]
    public TerrainGen TerrainGen;
    
    /// <summary>
    /// Index of this SimplexGen in the TerrainGen.SimplexGens array.
    /// Set during initialization.
    /// </summary>
    public int SimplexGenIndex { get; set; }
    
    private FastNoiseLite _noise;

    private bool _initialized;
    
    public override void _ExitTree()
    {
        // Proper cleanup when node is removed from the scene tree
        if (_noise != null)
        {
            _noise.Dispose();
            _noise = null;
        }
    }

    public override void _Ready()
    {
        if (!_initialized)
        {
            throw new Exception("SimplexGen not initialized yet!");
        }
    }
    
    public void InitNoiseConfig(double frequency, double lacunarity, double octaves, double gain)
    {
        _noise = new FastNoiseLite();
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        
        // Enable fractal noise (required for octaves, lacunarity, gain)
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        
        _noise.Frequency = (float)frequency;
        _noise.FractalLacunarity = (float)lacunarity;
        _noise.FractalOctaves = (int)octaves;
        _noise.FractalGain = (float)gain;
        
        _initialized = true;
    }
    
    public void OnFrequencyChanged(double value)
    {
        _noise.Frequency = (float)value;
        TerrainGen.InvalidateChunks();
    }
    
    public void OnFractalOctavesChanged(double value)
    {
        _noise.FractalOctaves = (int)value;
        TerrainGen.InvalidateChunks();
    }
    
    public void OnFractalLacunarityChanged(double value)
    {
        _noise.FractalLacunarity = (float)value;
        TerrainGen.InvalidateChunks();
    }
    
    public void OnFractalGainChanged(double value)
    {
        _noise.FractalGain = (float)value;
        TerrainGen.InvalidateChunks();
    }
    
    /// <summary>
    /// Generates tile information without calling SetCell. Thread-safe for background generation.
    /// </summary>
    /// <param name="x">World tile X coordinate</param>
    /// <param name="y">World tile Y coordinate</param>
    /// <returns>TileInfo containing all data needed to place the tile</returns>
    public TileInfo GenerateTileInfo(int x, int y)
    {
        return new TileInfo(SimplexGenIndex);
    }

    /// <summary>
    /// Gets just the variant index (0-3) for a given position based on noise.
    /// Used by ChunkRenderer for sub-tile color variation.
    /// </summary>
    /// <param name="x">World X coordinate (can be sub-tile position)</param>
    /// <param name="y">World Y coordinate (can be sub-tile position)</param>
    /// <returns>Variant index 0-3</returns>
    public int GetVariantIndex(float x, float y)
    {
        float noiseValue = _noise.GetNoise2D(x, y);
        float absNoiseValue = Math.Abs(noiseValue);
        
        // Map noise to variant index (0 to 3)
        int variantIndex = (int)Math.Floor(absNoiseValue * 4);
        return Math.Clamp(variantIndex, 0, 3);
    }
}