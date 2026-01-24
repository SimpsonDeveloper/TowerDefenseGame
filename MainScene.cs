namespace towerdefensegame;

using System;
using Godot;

public partial class MainScene : Node2D
{
    [Export]
    private TileMapLayer _tileMapLayer;
    
    // Sliders
    [Export]
    private HSlider _frequencySlider;
    
    [Export]
    private HSlider _fractalOctavesSlider;
    
    [Export]
    private HSlider _fractalLacunaritySlider;
    
    [Export]
    private HSlider _fractalGainSlider;
    
    // Slider labels
    [Export]
    private Label _frequencySliderLabel;
    
    [Export]
    private Label _fractalOctavesSliderLabel;
    
    [Export]
    private Label _fractalLacunaritySliderLabel;
    
    [Export]
    private Label _fractalGainSliderLabel;
    
    private FastNoiseLite _noise;

    public override void _Ready()
    {
        _noise = new FastNoiseLite();
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        
        // Enable fractal noise (required for octaves, lacunarity, gain)
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;

        // Period → Frequency (inverse relationship: lower frequency = larger features)
        float frequency = 0.93f;
        _noise.Frequency = frequency;  // Smaller = larger "blobs"
        _frequencySlider.SetValue(frequency);

        // Octaves
        int octaves = 2;
        _noise.FractalOctaves = octaves;  // More = more detail
        _fractalOctavesSlider.SetValue(octaves);

        // Lacunarity (how frequency changes per octave)
        float lacunarity = 1.0f;
        _noise.FractalLacunarity = lacunarity;  // Higher = more detail per octave
        _fractalLacunaritySlider.SetValue(lacunarity);

        // Persistence → Gain (how amplitude changes per octave)
        float gain = 0.1f;
        _noise.FractalGain = gain;  // Higher = rougher noise
        _fractalGainSlider.SetValue(gain);
        
        _frequencySliderLabel.SetText($"Frequency: {frequency}");
        _fractalOctavesSliderLabel.SetText($"Octaves: {octaves}");
        _fractalLacunaritySliderLabel.SetText($"Lacunarity: {lacunarity}");
        _fractalGainSliderLabel.SetText($"Gain: {gain}");
        
        // Connect slider signals
        _frequencySlider.ValueChanged += OnFrequencyChanged;
        _fractalOctavesSlider.ValueChanged += OnFractalOctavesChanged;
        _fractalLacunaritySlider.ValueChanged += OnFractalLacunarityChanged;
        _fractalGainSlider.ValueChanged += OnFractalGainChanged;
        
        GenerateTerrain();
        Console.WriteLine("Ready!");
    }
    
    private void OnFrequencyChanged(double value)
    {
        _noise.Frequency = (float)value;
        _frequencySliderLabel.SetText($"Frequency: {_noise.Frequency}");
        GenerateTerrain();
    }
    
    private void OnFractalOctavesChanged(double value)
    {
        _noise.FractalOctaves = (int)value;
        _fractalOctavesSliderLabel.SetText($"Octaves: {_noise.FractalOctaves}");
        GenerateTerrain();
    }
    
    private void OnFractalLacunarityChanged(double value)
    {
        _noise.FractalLacunarity = (float)value;
        _fractalLacunaritySliderLabel.SetText($"Lacunarity: {_noise.FractalLacunarity}");
        GenerateTerrain();
    }
    
    private void OnFractalGainChanged(double value)
    {
        _noise.FractalGain = (float)value;
        _fractalGainSliderLabel.SetText($"Gain: {_noise.FractalGain}");
        GenerateTerrain();
    }
    
    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept"))
        {
            _noise.Seed = (int)GD.Randi();
            GenerateTerrain();
        }
    }

    private void GenerateTerrain()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2I tileSize = _tileMapLayer.TileSet.TileSize;

        int width = (int)(viewportSize.X / tileSize.X / _tileMapLayer.GetScale().X) + 1;
        int height = (int)(viewportSize.Y / tileSize.Y / _tileMapLayer.GetScale().Y) + 1;
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float noiseValue = _noise.GetNoise2D(x, y);
                
                // Map noise (-1 to 1) to a tile index (0 to 3)
                int tileIndex = (int)Math.Floor(Math.Abs(noiseValue) * 4);
                
                // set the Vector2I to the atlas coords. The available coords are (0,0), (1,0), (0,1), (1,1)
                // index 0 maps to (0,0). index 1 maps to (0,1). index 2 maps to (1,0). index 3 maps to (1,1)
                // convert the index to a 2d vector by checking the bits
                
                // x is the first bit
                int atlasX = tileIndex & 1;
                // y is the second bit
                int atlasY = (tileIndex >> 1) & 1;
                Vector2I atlasCoords = new Vector2I(atlasX, atlasY);
                
                // SetCell(coords, sourceId, atlasCoords)
                _tileMapLayer.SetCell(new Vector2I(x, y), 0, atlasCoords);
            }
        }
    }
}