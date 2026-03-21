namespace towerdefensegame;

public interface ISimplexGenConfigurable
{
    SimplexGenData Config { get; }
    void InitNoiseConfig(double frequency, double lacunarity, double octaves, double gain);
    void OnFrequencyChanged(double value);
    void OnFractalOctavesChanged(double value);
    void OnFractalLacunarityChanged(double value);
    void OnFractalGainChanged(double value);
}