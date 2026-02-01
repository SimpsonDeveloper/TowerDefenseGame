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
        
    private FastNoiseLite _noise;
    
    private Godot.Collections.Dictionary<int, SimplexGen> _simplexGenIndices;

    private int _maxGenIndex;

    private Vector2I _tileSize;
    
    private bool _initialized;
    
    public override void _EnterTree()
    {
        if (ProcessMode == ProcessModeEnum.Disabled)
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
    
    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept"))
        {
            _noise.Seed = (int)GD.Randi();
            GenerateTerrain();
        }
    }
    
    public override void _Ready()
    {
        if (ProcessMode == ProcessModeEnum.Disabled)
        {
            QueueFree();
            return;
        }
        
        if (!_initialized)
        {
            throw new Exception("TerrainGen not initialized yet!");
        }
        
        GenerateTerrain();
        Console.WriteLine("TerrainGen Ready!");
    }

    private void ValidateConfig()
    {
        if (SimplexGens == null || SimplexGens.Length == 0)
        {
            throw new Exception("SimplexGens must not be null or empty!");
        }

        // Validate tile sizes are equal
        Vector2I? tileSize = null;
        foreach (SimplexGen simplexGen in SimplexGens)
        {
            if (simplexGen == null)
            {
                throw new Exception("SimplexGens contents must not be null!");
            }
            if (simplexGen.TileMapLayer.TileSet.TileSize.Equals(Vector2I.Zero))
            {
                throw new Exception("SimplexGen.TileMapLayer.TileSet.TileSize must not be zero!");
            } 
            if (tileSize == null)
            {
                // first entry. set tileSize
                tileSize = simplexGen.TileMapLayer.TileSet.TileSize;
            } else if (!tileSize.Value.Equals(simplexGen.TileMapLayer.TileSet.TileSize))
            {
                // second or nth entry. compare tileSize
                throw new Exception($"All SimplexGen.TileMapLayer.TileSet.TileSize must be equal! Got: {tileSize.Value} and {simplexGen.TileMapLayer.TileSet.TileSize}");
            }
        }
        
        if (GenRanges == null || GenRanges.Count == 0)
        {
            throw new Exception("GenRanges must not be null or empty!");
        }
        
        // Validate index ranges are sorted, don't overlap, and are greater than or equal to 0
        KeyValuePair<GenRange, int>? previous = null;
        foreach (KeyValuePair<GenRange, int> entry in GenRanges)
        {
            if (entry.Key == null)
            {
                throw new Exception("GenRanges contents must not be null or empty!");
            }

            if (entry.Value >= SimplexGens.Length)
            {
                throw new Exception("GenRanges indices must be less than or equal to SimplexGens.Length!");
            }
            if (!previous.HasValue)
            {
                previous = entry;
                continue;
            }
            
            int firstIndex = entry.Key.FirstIndex;
            int lastIndex = entry.Key.LastIndex;
            if (firstIndex < 0 || lastIndex < 0)
            {
                throw new Exception("All SimplexGen indices must be greater than or equal to zero!");
            }
            if (firstIndex > lastIndex)
            {
                throw new Exception("SimplexGen FirstIndex must be less than or equal to LastIndex!");
            }
            if (previous.Value.Key.LastIndex > firstIndex)
            {
                throw new Exception("SimplexGen IndexRanges must not overlap!");
            }
        }
    }
    
    public void GenerateTerrain()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2I tileSize = TileMapLayer.TileSet.TileSize;

        int width = (int)(viewportSize.X / tileSize.X / TileMapLayer.GetScale().X) + 1;
        int height = (int)(viewportSize.Y / tileSize.Y / TileMapLayer.GetScale().Y) + 1;

        foreach (var gen in SimplexGens)
        {
            gen.TileMapLayer.Clear();
        }
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float absNoiseValue = Math.Abs(_noise.GetNoise2D(x, y));
                
                // Map noise (-1 to 1) to a tile index (0 to [multiplicand - 1])
                int multiplicand = _maxGenIndex + 1;
                int genIndex = (int)Math.Floor(absNoiseValue * multiplicand);

                // Tell the generator to set its tile
                _simplexGenIndices[genIndex].GenerateTerrain(x, y);
            }
        }
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
        
        // set up Gen index ranges
        _simplexGenIndices = new Godot.Collections.Dictionary<int, SimplexGen>();
        foreach (KeyValuePair<GenRange, int> entry in GenRanges)
        {
            // assume validation passed. Validated index ranges are sorted, don't overlap, and are greater than or equal to 0
            int firstIndex = entry.Key.FirstIndex;
            int lastIndex = entry.Key.LastIndex;
            // last range should have the largest index
            _maxGenIndex = lastIndex;
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                _simplexGenIndices.Add(i, SimplexGens[entry.Value]);
            }
        }
        
        // init tile size. assume validation passed so index 0 should contain something
        _tileSize = SimplexGens[0].TileMapLayer.TileSet.TileSize;
        GenerateTerrain();
        _initialized = true;
    }
    
    public void OnFrequencyChanged(double value)
    {
        _noise.Frequency = (float)value;
        GenerateTerrain();
    }
    
    public void OnFractalOctavesChanged(double value)
    {
        _noise.FractalOctaves = (int)value;
        GenerateTerrain();
    }
    
    public void OnFractalLacunarityChanged(double value)
    {
        _noise.FractalLacunarity = (float)value;
        GenerateTerrain();
    }
    
    public void OnFractalGainChanged(double value)
    {
        _noise.FractalGain = (float)value;
        GenerateTerrain();
    }
}