using System.Collections.Generic;
using Godot;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Tracks which tile coordinates are occupied by placed towers inside the
/// pocket dimension. Designed to be queried before placement and updated
/// after a successful commit or a tower removal.
/// </summary>
public partial class TowerFootprintTracker : Node
{
    private readonly HashSet<Vector2I> _occupied = new();

    /// <summary>Returns true if every tile in the footprint is unoccupied.</summary>
    public bool CanPlace(IEnumerable<Vector2I> footprint)
    {
        foreach (var tile in footprint)
            if (_occupied.Contains(tile)) return false;
        return true;
    }

    /// <summary>Marks all tiles in the footprint as occupied.</summary>
    public void Register(IEnumerable<Vector2I> footprint)
    {
        foreach (var tile in footprint)
            _occupied.Add(tile);
    }

    /// <summary>Frees all tiles in the footprint (for future sell/remove support).</summary>
    public void Unregister(IEnumerable<Vector2I> footprint)
    {
        foreach (var tile in footprint)
            _occupied.Remove(tile);
    }
}
