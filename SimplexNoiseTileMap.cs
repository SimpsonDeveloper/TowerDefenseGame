using System.Collections.Generic;

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
    
    private readonly Dictionary<string, HSlider> _noiseSliders = new();
    
    private readonly Dictionary<string, Label> _noiseLabels = new();
    
    private FastNoiseLite _noise;

    private float _jumpRangeMin;

    private float _jumpRangeMax;

    private float _jumpRangeJumpTo;
    
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

        _noise = new FastNoiseLite();
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        
        // Enable fractal noise (required for octaves, lacunarity, gain)
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        
        // Initialize sliders
        CreateNoiseSliders();
        
        GenerateTerrain();
        Console.WriteLine("Ready!");
    }
    
    private void CreateNoiseSliders()
    {
        if (SimplexNoiseSettings == null)
            return;
        
        var sliderConfigsList = new List<(int row, int col, SliderConfig config, Godot.Range.ValueChangedEventHandler onValueChanged)>
        {
            // Period → Frequency (inverse relationship: lower frequency = larger features)
            // Smaller = larger "blobs"
            (0, 0, SimplexNoiseSettings.FrequencyConfig, OnFrequencyChanged),
            // Octaves → More = more detail
            (0, 1, SimplexNoiseSettings.OctavesConfig, OnFractalOctavesChanged),
            // Lacunarity (how frequency changes per octave)
            // Higher = more detail per octave
            (1, 0, SimplexNoiseSettings.LacunarityConfig, OnFractalLacunarityChanged),
            // Persistence → Gain (how amplitude changes per octave)
            // Higher = rougher noise
            (1, 1, SimplexNoiseSettings.GainConfig, OnFractalGainChanged),
        };
        
        if (SimplexNoiseSettings.JumpRangeSettings != null)
        {
            if (SimplexNoiseSettings.JumpRangeSettings.JumpRangeMinConfig == null ||
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeMaxConfig == null ||
                SimplexNoiseSettings.JumpRangeSettings.JumpRangeJumpToConfig == null)
            {
                throw new Exception("JumpRangeSettings is non-null, but or more of its subfields is null");
            }
            
            // JumpRangeMin → The min value to apply a jump
            sliderConfigsList.Add((0, 2, SimplexNoiseSettings.JumpRangeSettings.JumpRangeMinConfig, OnJumpRangeMinChanged));
            // JumpRangeMax → The max value to apply a jump
            sliderConfigsList.Add((1, 2, SimplexNoiseSettings.JumpRangeSettings.JumpRangeMaxConfig, OnJumpRangeMaxChanged));
            // JumpRangeJumpTo → The value to jump to when applying a jump
            sliderConfigsList.Add((2, 2, SimplexNoiseSettings.JumpRangeSettings.JumpRangeJumpToConfig, OnJumpRangeJumpToChanged));
        }
        
        foreach (var (row, col, config, onValueChanged) in sliderConfigsList)
        {
            if (config == null)
                continue;
            
            // Create slider
            var slider = new HSlider();
            // Add slider to current node
            AddChild(slider);
            slider.Size = new Vector2(200, 16);
            slider.Position = new Vector2(8 + 247 * col, 24 + 53 * row);
            slider.MinValue = config.Min;
            slider.MaxValue = config.Max;
            slider.Step = config.Step;
            slider.Value = config.InitialValue;
            slider.ValueChanged += onValueChanged;
            
            // Create label
            var label = new Label();
            // Add label as child to slider
            slider.AddChild(label);
            label.Size = new Vector2(90, 23);
            label.Position = new Vector2(0, -24);
            label.Text = FormatLabelText(config.Name, config.InitialValue);
            LabelSettings labelSettings = new LabelSettings();
            Color color = new Color();
            color.R = 0;
            color.G = 0;
            color.B = 0;
            color.A = 1;
            labelSettings.SetFontColor(color);
            label.LabelSettings = labelSettings;
            
            // Store reference for later use
            _noiseSliders[config.Name] = slider;
            _noiseLabels[config.Name] = label;
            
            onValueChanged.Invoke(config.InitialValue);
        }
    }

    private string FormatLabelText(string name, float value)
    {
        return $"{name}: {value}";
    }
    
    private void OnFrequencyChanged(double value)
    {
        _noise.Frequency = (float)value;
        string name = SimplexNoiseSettings.FrequencyConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        GenerateTerrain();
    }
    
    private void OnFractalOctavesChanged(double value)
    {
        _noise.FractalOctaves = (int)value;
        string name = SimplexNoiseSettings.OctavesConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        GenerateTerrain();
    }
    
    private void OnFractalLacunarityChanged(double value)
    {
        _noise.FractalLacunarity = (float)value;
        string name = SimplexNoiseSettings.LacunarityConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        GenerateTerrain();
    }
    
    private void OnFractalGainChanged(double value)
    {
        _noise.FractalGain = (float)value;
        string name = SimplexNoiseSettings.GainConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        GenerateTerrain();
    }

    private void OnJumpRangeMinChanged(double value)
    {
        _jumpRangeMin = (float)value;
        string name = SimplexNoiseSettings.JumpRangeSettings.JumpRangeMinConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        GenerateTerrain();
    }
    
    private void OnJumpRangeMaxChanged(double value)
    {
        _jumpRangeMax = (float)value;
        string name = SimplexNoiseSettings.JumpRangeSettings.JumpRangeMaxConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        GenerateTerrain();
    }

    private void OnJumpRangeJumpToChanged(double value)
    {
        _jumpRangeJumpTo = (float)value;
        string name = SimplexNoiseSettings.JumpRangeSettings.JumpRangeJumpToConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
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
