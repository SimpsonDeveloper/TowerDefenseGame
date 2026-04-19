using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.terrain;
using Range = Godot.Range;

namespace towerdefensegame.scripts.ui;

public partial class SimplexGenUI : Container
{
    [Export] public bool ShowUi;
    [Export] public Node SimplexGenNode { get; set; }

    private readonly Dictionary<string, Label> _noiseLabels = new();

    private ISimplexGenConfigurable _simplexGen;

    public override void _Ready()
    {
        if (SimplexGenNode is not ISimplexGenConfigurable simplexGen)
            throw new Exception("SimplexGenUI.SimplexGenNode must implement ISimplexGenConfigurable.");
        _simplexGen = simplexGen;

        if (_simplexGen.Config == null)
            throw new Exception("SimplexGenUI.SimplexGenNode.Config must not be null.");

        if (!ShowUi) return;

        SimplexGenData cfg = _simplexGen.Config;
        AddSlider(cfg.Name + " Frequency",  cfg.Frequency,  0, 0, OnFrequencyChanged);
        AddSlider(cfg.Name + " Octaves",    cfg.Octaves,    0, 1, OnFractalOctavesChanged);
        AddSlider(cfg.Name + " Lacunarity", cfg.Lacunarity, 1, 0, OnFractalLacunarityChanged);
        AddSlider(cfg.Name + " Gain",       cfg.Gain,       1, 1, OnFractalGainChanged);
    }

    private void AddSlider(string name, float initial, int row, int col, Range.ValueChangedEventHandler cb)
    {
        bool isInt = Mathf.IsEqualApprox(initial, Mathf.Round(initial));
        (float min, float max) = isInt ? ComputeIntRange(initial) : ComputeRange(initial);
        float step = isInt ? 1f : (max - min) / 1000f;
        _noiseLabels[name] = SliderBuilder.AddSlider(this, name, min, max, step, initial, row, col, cb);
    }

    /// <summary>
    /// Maps the initial value to the tightest power-of-10 interval that contains it:
    /// (0,1] → [0,1], (1,10] → [1,10], (10,100] → [10,100], etc.
    /// </summary>
    private static (float min, float max) ComputeRange(float initial)
    {
        // find the power of 10 closest to, and greater than initial
        // try both +/- directions at each magnitude, stop when no candidate improves on the best
        int powerMagnitude = 0;
        int[] directions = [-1, 1];
        float bestMax = float.MaxValue;
        float prevBestMax = float.MaxValue;
        while (true)
        {
            prevBestMax = bestMax;
            foreach (int dir in directions)
            {
                float candidate = Mathf.Pow(10f, powerMagnitude * dir);
                if (candidate >= initial && candidate < bestMax)
                    bestMax = candidate;
            }
            if (bestMax < float.MaxValue && Mathf.IsEqualApprox(bestMax, prevBestMax)) break;
            powerMagnitude++;
        }
        return (bestMax / 10f, bestMax);
    }

    /// <summary>Integer variant: min=1, max=next power of 10 above initial.</summary>
    private static (float min, float max) ComputeIntRange(float initial)
    {
        if (initial <= 1f) return (1f, 10f);
        float max = Mathf.Pow(10f, Mathf.Ceil((float)Math.Log10(initial + 1)));
        return (1f, max);
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void OnFrequencyChanged(double value)
    {
        string name = _simplexGen.Config.Name + " Frequency";
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFrequencyChanged(value);
    }

    private void OnFractalOctavesChanged(double value)
    {
        string name = _simplexGen.Config.Name + " Octaves";
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFractalOctavesChanged(value);
    }

    private void OnFractalLacunarityChanged(double value)
    {
        string name = _simplexGen.Config.Name + " Lacunarity";
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFractalLacunarityChanged(value);
    }

    private void OnFractalGainChanged(double value)
    {
        string name = _simplexGen.Config.Name + " Gain";
        _noiseLabels[name].SetText(SliderBuilder.FormatLabel(name, (float)value));
        _simplexGen.OnFractalGainChanged(value);
    }
}
