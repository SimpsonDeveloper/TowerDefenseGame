using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

public partial class TerrainGen : Node, ISimplexGenConfigurable
{
    [Export]
    public SimplexGen[] SimplexGens;
    
    /**
     * Remap the indices from SimplexGens to custom indices.
     * This is so you can specify which gen to use at a certain noise range
    */
    [Export]
    public Godot.Collections.Dictionary<GenRange, int> GenRanges;

    // Mainly using this for transform parenting of other tile map layers at the moment
    [Export]
    public TileMapLayer TileMapLayer;
    
    [Export]
    public ChunkManager ChunkManager;
        
    private FastNoiseLite _noise;
    
    private Godot.Collections.Dictionary<int, SimplexGen> _simplexGenIndices;

    private int _maxGenIndex;

    private bool _initialized;

    /// <summary>
    /// Whether the TerrainGen has been initialized and is ready for chunk generation.
    /// </summary>
    public bool IsInitialized => _initialized;
    
    public override void _ExitTree()
    {
        // Proper cleanup when node is removed from the scene tree
        if (_noise != null)
        {
            _noise.Dispose();
            _noise = null;
        }
    }
    
    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept"))
        {
            _noise.Seed = (int)GD.Randi();
            InvalidateChunks();
        }
    }
    
    public override void _Ready()
    {
        if (!_initialized)
        {
            throw new Exception("TerrainGen not initialized yet!");
        }
    }

    private void ValidateConfig()
    {
        if (SimplexGens == null || SimplexGens.Length == 0)
        {
            throw new Exception("SimplexGens must not be null or empty!");
        }

        foreach (SimplexGen simplexGen in SimplexGens)
        {
            if (simplexGen == null)
            {
                throw new Exception("SimplexGens contents must not be null!");
            }
        }

        if (GenRanges == null || GenRanges.Count == 0)
        {
            throw new Exception("GenRanges must not be null or empty!");
        }
        
        // Validate index ranges are sorted, don't overlap, and min and max are equidistant from 0
        KeyValuePair<GenRange, int>? previous = null;
        int min = 0;
        int max = 0;
        foreach (KeyValuePair<GenRange, int> entry in GenRanges)
        {
            if (entry.Key == null)
            {
                throw new Exception("GenRanges contents must not be null or empty!");
            }
            if (entry.Value < 0)
            {
                throw new Exception("GenRanges contents must be greater than or equal to zero!");
            }
            if (entry.Value >= SimplexGens.Length)
            {
                throw new Exception("GenRanges indices must be less than or equal to SimplexGens.Length!");
            }
            
            int firstIndex = entry.Key.FirstIndex;
            int lastIndex = entry.Key.LastIndex;
            if (firstIndex < min)
            {
                min = firstIndex;
            }
            if (lastIndex > max)
            {
                max = lastIndex;
            }
            
            if (!previous.HasValue)
            {
                previous = entry;
                continue;
            }
            
            if (firstIndex > lastIndex)
            {
                throw new Exception("SimplexGen FirstIndex must be less than or equal to LastIndex!");
            }
            if (previous.Value.Key.LastIndex >= firstIndex)
            {
                throw new Exception("SimplexGen IndexRanges must not overlap!");
            }
        }

        if (min == 0 || max == 0)
        {
            throw new Exception("SimplexGen IndexRanges min/max must not be zero!");
        }
        if (min != -max)
        {
            throw new Exception("SimplexGen IndexRanges min must be -max!");
        }
    }
    
    public void InvalidateChunks()
    {
        ChunkManager.ClearAllChunks();
    }

    /// <summary>
    /// Generates chunk data without calling SetCell. Thread-safe for background generation.
    /// </summary>
    /// <param name="chunkCoord">The chunk coordinate</param>
    /// <param name="startX">Starting tile X coordinate</param>
    /// <param name="startY">Starting tile Y coordinate</param>
    /// <param name="width">Width in tiles</param>
    /// <param name="height">Height in tiles</param>
    /// <returns>ChunkData containing all tile information</returns>
    public ChunkData GenerateChunkData(Vector2I chunkCoord, int startX, int startY, int width, int height)
    {
        ChunkData chunkData = new ChunkData(chunkCoord, startX, startY, width, height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int worldX = startX + x;
                int worldY = startY + y;

                // Get terrain type from main noise
                float noiseValue = _noise.GetNoise2D(worldX, worldY);
                int genIndex = (int)Math.Round(noiseValue * _maxGenIndex);

                // Get the SimplexGen for this terrain type
                SimplexGen simplexGen = _simplexGenIndices[genIndex];

                // Get tile info from SimplexGen (thread-safe, no SetCell)
                TileInfo tileInfo = simplexGen.GenerateTileInfo(worldX, worldY);

                chunkData.Tiles[x, y] = tileInfo;
            }
        }

        return chunkData;
    }

    public void InitNoiseConfig(double frequency, double lacunarity, double octaves, double gain)
    {
        if (ProcessMode == ProcessModeEnum.Disabled)
        {
            QueueFree();
            return;
        }
        
        ValidateConfig();
        _noise = new FastNoiseLite();
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        
        // Enable fractal noise (required for octaves, lacunarity, gain)
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        
        _noise.Frequency = (float)frequency;
        _noise.FractalLacunarity = (float)lacunarity;
        _noise.FractalOctaves = (int)octaves;
        _noise.FractalGain = (float)gain;
        
        // Set SimplexGenIndex on each SimplexGen for async tile generation
        for (int i = 0; i < SimplexGens.Length; i++)
        {
            SimplexGens[i].SimplexGenIndex = i;
        }

        // set up Gen index ranges
        _simplexGenIndices = new Godot.Collections.Dictionary<int, SimplexGen>();
        foreach (KeyValuePair<GenRange, int> entry in GenRanges)
        {
            // assume validation passed. Validated index ranges are sorted, don't overlap, and min and max are equidistant from 0
            int firstIndex = entry.Key.FirstIndex;
            int lastIndex = entry.Key.LastIndex;
            // last range should have the largest index
            _maxGenIndex = lastIndex;
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                _simplexGenIndices.Add(i, SimplexGens[entry.Value]);
            }
        }
        
        // Don't generate terrain here - ChunkManager handles generation
        _initialized = true;
    }
    
    public void OnFrequencyChanged(double value)
    {
        _noise.Frequency = (float)value;
        InvalidateChunks();
    }
    
    public void OnFractalOctavesChanged(double value)
    {
        _noise.FractalOctaves = (int)value;
        InvalidateChunks();
    }
    
    public void OnFractalLacunarityChanged(double value)
    {
        _noise.FractalLacunarity = (float)value;
        InvalidateChunks();
    }
    
    public void OnFractalGainChanged(double value)
    {
        _noise.FractalGain = (float)value;
        InvalidateChunks();
    }
}