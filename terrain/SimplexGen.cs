using System;
using Godot;

namespace towerdefensegame;

[GlobalClass]
public partial class SimplexGen : Node, ISimplexGenConfigurable
{
    [Export]
    public TerrainGen TerrainGen;
    
    [Export]
    public TileMapLayer TileMapLayer;
    
    [Export]
    public int TileSetIndex;

    /// <summary>
    /// Index of this SimplexGen in the TerrainGen.SimplexGens array.
    /// Set during initialization.
    /// </summary>
    public int SimplexGenIndex { get; set; }
    
    private FastNoiseLite _noise;

    private bool _initialized;
    
    public override void _EnterTree()
    {
        if (!CanProcess())
        {
            QueueFree();
        }
    }
    
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
        if (!CanProcess())
        {
            QueueFree();
            return;
        }

        if (!_initialized)
        {
            throw new Exception("SimplexGen not initialized yet!");
        }
        
        Console.WriteLine($"SimplexGen {GetName()} Ready!");
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
    
    public void GenerateTerrain(int x, int y)
    {
        float noiseValue = _noise.GetNoise2D(x, y);
        float absNoiseValue = Math.Abs(noiseValue);
        
        // Map noise (-1 to 1) to a tile index (0 to 3) for 4 color variants
        int tileIndex = (int)Math.Floor(absNoiseValue * 4);
        tileIndex = Math.Clamp(tileIndex, 0, 3);
        
        // Convert the index to a 2d vector for 2x2 atlas
        int atlasWidth = 2;
        int atlasX = tileIndex % atlasWidth;
        int atlasY = tileIndex / atlasWidth;
        Vector2I atlasCoords = new Vector2I(atlasX, atlasY);
        
        TileMapLayer.SetCell(new Vector2I(x, y), TileSetIndex, atlasCoords);
    }

    /// <summary>
    /// Generates tile information without calling SetCell. Thread-safe for background generation.
    /// </summary>
    /// <param name="x">World tile X coordinate</param>
    /// <param name="y">World tile Y coordinate</param>
    /// <returns>TileInfo containing all data needed to place the tile</returns>
    public TileInfo GenerateTileInfo(int x, int y)
    {
        float noiseValue = _noise.GetNoise2D(x, y);
        float absNoiseValue = Math.Abs(noiseValue);
        
        // Map noise (-1 to 1) to a tile index (0 to 3) for 4 color variants
        int tileIndex = (int)Math.Floor(absNoiseValue * 4);
        
        // Clamp to valid range
        tileIndex = Math.Clamp(tileIndex, 0, 3);
        
        // Convert the index to a 2d vector for 2x2 atlas
        int atlasWidth = 2;
        int atlasX = tileIndex % atlasWidth;
        int atlasY = tileIndex / atlasWidth;
        Vector2I atlasCoords = new Vector2I(atlasX, atlasY);

        return new TileInfo(SimplexGenIndex, TileSetIndex, atlasCoords, tileIndex);
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