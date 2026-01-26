namespace towerdefensegame;

using Godot;
using System;

public partial class SimplexNoiseTileMap : Node
{
    [Export]
    public SimplexNoiseSettings SimplexNoiseSettings;
    
    [Export]
    public int TileSetIndex;
    
    [Export]
    public TileMapLayer TileMapLayer;
    
    [ExportGroup("Noise Slider Settings")]
    // Noise Sliders
    [Export]
    private HSlider _frequencySlider;
    
    [Export]
    private HSlider _fractalOctavesSlider;
    
    [Export]
    private HSlider _fractalLacunaritySlider;
    
    [Export]
    private HSlider _fractalGainSlider;
    
    // Noise Slider Labels
    [Export]
    private Label _frequencySliderLabel;
    
    [Export]
    private Label _fractalOctavesSliderLabel;
    
    [Export]
    private Label _fractalLacunaritySliderLabel;
    
    [Export]
    private Label _fractalGainSliderLabel;
    
    [ExportGroup("Jump Range Settings")]
    // Jump range sliders
    [Export]
    private HSlider _jumpRangeMinSlider;
    
    [Export]
    private HSlider _jumpRangeMaxSlider;
    
    [Export]
    private HSlider _jumpRangeJumpToSlider;
    
    // Jump range labels
    [Export]
    private Label _jumpRangeMinLabel;
    
    [Export]
    private Label _jumpRangeMaxLabel;
    
    [Export]
    private Label _jumpRangeJumpToLabel;
    
    private FastNoiseLite _noise;

    private float _jumpRangeMin;

    private float _jumpRangeMax;

    private float _jumpRangeJumpTo;
    
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

    public override void _Ready()
    {
        if (ProcessMode == ProcessModeEnum.Disabled)
        {
            QueueFree();
            return;
        }
        _noise = new FastNoiseLite();
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        
        // Enable fractal noise (required for octaves, lacunarity, gain)
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;

        // Period → Frequency (inverse relationship: lower frequency = larger features)
        float frequency = SimplexNoiseSettings.FrequencyConfig.InitialValue;
        _noise.Frequency = frequency;  // Smaller = larger "blobs"
        InitSlider(_frequencySlider,
            SimplexNoiseSettings.FrequencyConfig,
            _frequencySliderLabel,
            $"Frequency: {_noise.Frequency}");

        // Octaves
        int octaves = (int)SimplexNoiseSettings.OctavesConfig.InitialValue;
        _noise.FractalOctaves = octaves;  // More = more detail
        InitSlider(_fractalOctavesSlider,
            SimplexNoiseSettings.OctavesConfig,
            _fractalOctavesSliderLabel,
            $"Octaves: {_noise.FractalOctaves}");

        // Lacunarity (how frequency changes per octave)
        float lacunarity = SimplexNoiseSettings.LacunarityConfig.InitialValue;
        _noise.FractalLacunarity = lacunarity;  // Higher = more detail per octave
        InitSlider(_fractalLacunaritySlider,
            SimplexNoiseSettings.LacunarityConfig,
            _fractalLacunaritySliderLabel,
            $"Lacunarity: {_noise.FractalLacunarity}");

        // Persistence → Gain (how amplitude changes per octave)
        float gain = SimplexNoiseSettings.GainConfig.InitialValue;
        _noise.FractalGain = gain;  // Higher = rougher noise
        InitSlider(_fractalGainSlider,
            SimplexNoiseSettings.GainConfig,
            _fractalGainSliderLabel,
            $"Gain: {_noise.FractalGain}");
        
        // Connect slider signals
        _frequencySlider.ValueChanged += OnFrequencyChanged;
        _fractalOctavesSlider.ValueChanged += OnFractalOctavesChanged;
        _fractalLacunaritySlider.ValueChanged += OnFractalLacunarityChanged;
        _fractalGainSlider.ValueChanged += OnFractalGainChanged;
        
        if (SimplexNoiseSettings.JumpRangeSettings != null)
        {
            if (SimplexNoiseSettings.JumpRangeSettings.JumpRangeMinConfig == null ||
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeMaxConfig == null ||
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeJumpToConfig == null)
            {
                throw new Exception("JumpRangeSettings is non-null, but or more of its subfields is null");
            }
            
            if (_jumpRangeMinSlider == null ||
                _jumpRangeMaxSlider == null ||
                _jumpRangeJumpToSlider == null)
            {
                throw new Exception("JumpRangeSettings is non-null, but one or more jump slider is null");
            }
            
            if (_jumpRangeMinLabel == null ||
                _jumpRangeMaxLabel == null ||
                _jumpRangeJumpToLabel == null)
            {
                throw new Exception("JumpRangeSettings is non-null, but one or more jump label is null");
            }
            
            // JumpRangeMin → The min value to apply a jump
            _jumpRangeMin = SimplexNoiseSettings.JumpRangeSettings.JumpRangeMinConfig.InitialValue;
            InitSlider(_jumpRangeMinSlider,
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeMinConfig,
                _jumpRangeMinLabel,
                $"Min: {_jumpRangeMin}");
            
            // JumpRangeMax → The max value to apply a jump
            _jumpRangeMax = SimplexNoiseSettings.JumpRangeSettings.JumpRangeMaxConfig.InitialValue;
            InitSlider(_jumpRangeMaxSlider,
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeMaxConfig,
                _jumpRangeMaxLabel,
                $"Max: {_jumpRangeMax}");
            
            // JumpRangeJumpTo → The value to jump to when the 
            _jumpRangeJumpTo = SimplexNoiseSettings.JumpRangeSettings.JumpRangeJumpToConfig.InitialValue;
            InitSlider(_jumpRangeJumpToSlider,
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeJumpToConfig,
                _jumpRangeJumpToLabel,
                $"JumpTo: {_jumpRangeJumpTo}");
            
            // Connect slider signals
            _jumpRangeMinSlider.ValueChanged += OnJumpRangeMinChanged;
            _jumpRangeMaxSlider.ValueChanged += OnJumpRangeMaxChanged;
            _jumpRangeJumpToSlider.ValueChanged += OnJumpRangeJumpToChanged;
        }
        
        GenerateTerrain();
        Console.WriteLine("Ready!");
    }

    private void InitSlider(HSlider slider, SliderConfig sliderConfig, Label sliderLabel, string labelText)
    {
        slider.SetMin(sliderConfig.Min);
        slider.SetMax(sliderConfig.Max);
        slider.SetStep(sliderConfig.Step);
        slider.SetValue(sliderConfig.InitialValue);
        sliderLabel.SetText(labelText);
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

    private void OnJumpRangeMinChanged(double value)
    {
        _jumpRangeMin = (float)value;
        _jumpRangeMinLabel.SetText($"Min: {_jumpRangeMin}");
        GenerateTerrain();
    }
    
    private void OnJumpRangeMaxChanged(double value)
    {
        _jumpRangeMax = (float)value;
        _jumpRangeMaxLabel.SetText($"Max: {_jumpRangeMax}");
        GenerateTerrain();
    }

    private void OnJumpRangeJumpToChanged(double value)
    {
        _jumpRangeJumpTo = (float)value;
        _jumpRangeJumpToLabel.SetText($"JumpTo: {_jumpRangeJumpTo}");
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
        Vector2I tileSize = TileMapLayer.TileSet.TileSize;

        int width = (int)(viewportSize.X / tileSize.X / TileMapLayer.GetScale().X) + 1;
        int height = (int)(viewportSize.Y / tileSize.Y / TileMapLayer.GetScale().Y) + 1;
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float noiseValue = _noise.GetNoise2D(x, y);
                float absNoiseValue = Math.Abs(noiseValue);
                if (SimplexNoiseSettings.JumpRangeSettings != null)
                {
                    if (absNoiseValue >= _jumpRangeMin && absNoiseValue <= _jumpRangeMax)
                    {
                        absNoiseValue = _jumpRangeJumpTo;
                    }
                }
                
                // Map noise (-1 to 1) to a tile index (0 to 3)
                int tileIndex = (int)Math.Floor(absNoiseValue * 4);
                
                // convert the index to a 2d vector, based on vector dimensions
                int atlasWidth = 2;
                int atlasX = tileIndex % atlasWidth;
                int atlasY = tileIndex / atlasWidth;
                Vector2I atlasCoords = new Vector2I(atlasX, atlasY);
                
                // SetCell(coords, sourceId, atlasCoords)
                TileMapLayer.SetCell(new Vector2I(x, y), TileSetIndex, atlasCoords);
            }
        }
    }
}
