using System;
using System.Diagnostics;
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

    // Timing accumulators for profiling
    private Stopwatch _noiseSw = new Stopwatch();
    private Stopwatch _setCellSw = new Stopwatch();

    /// <summary>
    /// Accumulated time spent on noise calculations (in ms).
    /// </summary>
    public long NoiseTimeMs => _noiseSw.ElapsedMilliseconds;

    /// <summary>
    /// Accumulated time spent on SetCell calls (in ms).
    /// </summary>
    public long SetCellTimeMs => _setCellSw.ElapsedMilliseconds;

    /// <summary>
    /// Resets the timing accumulators.
    /// </summary>
    public void ResetTimers()
    {
        _noiseSw.Reset();
        _setCellSw.Reset();
    }
    
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
        // Time noise calculation
        _noiseSw.Start();
        float noiseValue = _noise.GetNoise2D(x, y);
        float absNoiseValue = Math.Abs(noiseValue);
        
        // Map noise (-1 to 1) to a tile index (0 to 3)
        //TODO: need to update this hard coded range and atlas
        int tileIndex = (int)Math.Floor(absNoiseValue * 4);
        
        // convert the index to a 2d vector, based on vector dimensions
        int atlasWidth = 2;
        int atlasX = tileIndex % atlasWidth;
        int atlasY = tileIndex / atlasWidth;
        Vector2I atlasCoords = new Vector2I(atlasX, atlasY);
        _noiseSw.Stop();
        
        // Time SetCell call
        _setCellSw.Start();
        TileMapLayer.SetCell(new Vector2I(x, y), TileSetIndex, atlasCoords);
        _setCellSw.Stop();
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
        
        // Map noise (-1 to 1) to a tile index (0 to 3)
        //TODO: need to update this hard coded range and atlas
        int tileIndex = (int)Math.Floor(absNoiseValue * 4);
        
        // convert the index to a 2d vector, based on vector dimensions
        int atlasWidth = 2;
        int atlasX = tileIndex % atlasWidth;
        int atlasY = tileIndex / atlasWidth;
        Vector2I atlasCoords = new Vector2I(atlasX, atlasY);

        return new TileInfo(SimplexGenIndex, TileSetIndex, atlasCoords);
    }
}