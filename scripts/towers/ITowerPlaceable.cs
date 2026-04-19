namespace towerdefensegame.scripts.towers;

/// <summary>
/// Implemented by tower scene roots to receive configuration from TowerDef
/// before the node enters the scene tree.
/// </summary>
public interface ITowerPlaceable
{
    void Configure(TowerDef def);
}
