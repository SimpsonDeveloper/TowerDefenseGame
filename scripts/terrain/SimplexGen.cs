using System;
using Godot;

namespace towerdefensegame;

[GlobalClass]
public partial class SimplexGen : Node, ISimplexGenConfigurable
{
    [Export]
    public ChunkManager ChunkManager;

    [Export]
    public TerrainType TerrainType { get; set; }
    
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
         ChunkManager.ClearAllChunks();
    }
    
    public void OnFractalOctavesChanged(double value)
    {
        _noise.FractalOctaves = (int)value;
         ChunkManager.ClearAllChunks();
    }
    
    public void OnFractalLacunarityChanged(double value)
    {
        _noise.FractalLacunarity = (float)value;
         ChunkManager.ClearAllChunks();
    }
    
    public void OnFractalGainChanged(double value)
    {
        _noise.FractalGain = (float)value;
         ChunkManager.ClearAllChunks();
    }
    
    /// <summary>
    /// Generates tile information without calling SetCell. Thread-safe for background generation.
    /// </summary>
    /// <param name="x">World tile X coordinate</param>
    /// <param name="y">World tile Y coordinate</param>
    /// <returns>TileInfo containing all data needed to place the tile</returns>
    public TileInfo GenerateTileInfo(int x, int y)
    {
        return new TileInfo(TerrainType);
    }

    /// <summary>
    /// Gets a variant index for a given position based on noise.
    /// Used by ChunkRenderer for sub-tile color variation.
    /// </summary>
    /// <param name="x">World X coordinate (can be sub-tile position)</param>
    /// <param name="y">World Y coordinate (can be sub-tile position)</param>
    /// <param name="variantCount">Number of variants to map noise into — should match the terrain's color array length</param>
    /// <returns>Variant index in range [0, variantCount - 1]</returns>
    public int GetVariantIndex(float x, float y, int variantCount)
    {
        float noiseValue = _noise.GetNoise2D(x, y);
        float absNoiseValue = Math.Abs(noiseValue);
        int variantIndex = (int)Math.Floor(absNoiseValue * variantCount);
        return Math.Clamp(variantIndex, 0, variantCount - 1);
    }
}