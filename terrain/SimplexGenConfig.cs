using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

public partial class SimplexGenConfig : Container
{
    [Export]
    public SliderConfig FrequencyConfig { get; set; }
    
    [Export]
    public SliderConfig OctavesConfig { get; set; }
    
    [Export]
    public SliderConfig LacunarityConfig { get; set; }
    
    [Export]
    public SliderConfig GainConfig { get; set; }
    
    [Export]
    public Node SimplexGenNode;
    
    private readonly Dictionary<string, HSlider> _noiseSliders = new();
    
    private readonly Dictionary<string, Label> _noiseLabels = new();
    
    // this node must have a SimplexGen parent of type ISimplexGenConfigurable
    private ISimplexGenConfigurable _simplexGen;
    
    
    public override void _EnterTree()
    {
        if (ProcessMode == ProcessModeEnum.Disabled)
        {
            QueueFree();
        }
    }
    
    public override void _Ready()
    {
        if (ProcessMode == ProcessModeEnum.Disabled)
        {
            QueueFree();
            return;
        }
        
        if (SimplexGenNode is ISimplexGenConfigurable config)
        {
            _simplexGen = config;
        }
        else
        {
            throw new Exception("SimplexGenNode must be a ISimplexGenConfigurable");
        }
        // Initialize sliders
        CreateNoiseSliders();
        
        Console.WriteLine("SimplexGenConfig Ready!");
    }
    
    private void CreateNoiseSliders()
    {
        var sliderConfigsList = new List<(int row, int col, SliderConfig config, Godot.Range.ValueChangedEventHandler onValueChanged)>
        {
            // Period → Frequency (inverse relationship: lower frequency = larger features)
            // Smaller = larger "blobs"
            (0, 0, FrequencyConfig, OnFrequencyChanged),
            // Octaves → More = more detail
            (0, 1, OctavesConfig, OnFractalOctavesChanged),
            // Lacunarity (how frequency changes per octave)
            // Higher = more detail per octave
            (1, 0, LacunarityConfig, OnFractalLacunarityChanged),
            // Persistence → Gain (how amplitude changes per octave)
            // Higher = rougher noise
            (1, 1, GainConfig, OnFractalGainChanged),
        };
        
        foreach (var (row, col, config, onValueChanged) in sliderConfigsList)
        {
            if (config == null)
                throw new Exception("Slider config is null");
            
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
        }
        
        _simplexGen.InitNoiseConfig(FrequencyConfig.InitialValue,
            OctavesConfig.InitialValue,
            LacunarityConfig.InitialValue,
            GainConfig.InitialValue);
    }

    private string FormatLabelText(string name, float value)
    {
        return $"{name}: {value}";
    }
    
    public void OnFrequencyChanged(double value)
    {
        string name = FrequencyConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        _simplexGen.OnFrequencyChanged(value);
    }
    
    public void OnFractalOctavesChanged(double value)
    {
        string name = OctavesConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        _simplexGen.OnFractalOctavesChanged(value);
    }
    
    public void OnFractalLacunarityChanged(double value)
    {
        string name = LacunarityConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        _simplexGen.OnFractalLacunarityChanged(value);
    }
    
    public void OnFractalGainChanged(double value)
    {
        string name = GainConfig.Name;
        _noiseLabels[name].SetText(FormatLabelText(name, (float)value));
        _simplexGen.OnFractalGainChanged(value);
    }
}