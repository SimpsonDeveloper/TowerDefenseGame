using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

public partial class SimplexGenConfig : Container
{
    [Export] public bool ShowUI;
    [Export] public SliderConfig FrequencyConfig { get; set; }
    [Export] public SliderConfig OctavesConfig { get; set; }
    [Export] public SliderConfig LacunarityConfig { get; set; }
    [Export] public SliderConfig GainConfig { get; set; }
    [Export] public Node SimplexGenNode;

    private readonly Dictionary<string, Label> _noiseLabels = new();

    // this node must have a SimplexGen parent of type ISimplexGenConfigurable
    private ISimplexGenConfigurable _simplexGen;


    public override void _Ready()
    {
        if (SimplexGenNode is ISimplexGenConfigurable config)
        {
            _simplexGen = config;
        }
        else
        {
            throw new Exception("SimplexGenNode must be a ISimplexGenConfigurable");
        }
        CreateNoiseSliders();
    }

    private void CreateNoiseSliders()
    {
        if (ShowUI)
        {
            // Period → Frequency (inverse relationship: lower frequency = larger features)
            // Smaller = larger "blobs"
            _noiseLabels[FrequencyConfig.Name]  = SliderBuilder.AddSlider(this, FrequencyConfig,  0, 0, OnFrequencyChanged);
            // Octaves → More = more detail
            _noiseLabels[OctavesConfig.Name]    = SliderBuilder.AddSlider(this, OctavesConfig,    0, 1, OnFractalOctavesChanged);
            // Lacunarity (how frequency changes per octave) — Higher = more detail per octave
            _noiseLabels[LacunarityConfig.Name] = SliderBuilder.AddSlider(this, LacunarityConfig, 1, 0, OnFractalLacunarityChanged);
            // Persistence → Gain (how amplitude changes per octave) — Higher = rougher noise
            _noiseLabels[GainConfig.Name]       = SliderBuilder.AddSlider(this, GainConfig,       1, 1, OnFractalGainChanged);
        }

        _simplexGen.InitNoiseConfig(
            FrequencyConfig.InitialValue,
            OctavesConfig.InitialValue,
            LacunarityConfig.InitialValue,
            GainConfig.InitialValue);
    }

    public void OnFrequencyChanged(double value)
    {
        string name = FrequencyConfig.Name;
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFrequencyChanged(value);
    }

    public void OnFractalOctavesChanged(double value)
    {
        string name = OctavesConfig.Name;
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFractalOctavesChanged(value);
    }

    public void OnFractalLacunarityChanged(double value)
    {
        string name = LacunarityConfig.Name;
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFractalLacunarityChanged(value);
    }

    public void OnFractalGainChanged(double value)
    {
        string name = GainConfig.Name;
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFractalGainChanged(value);
    }
}
