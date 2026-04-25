using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Tracks tile occupancy for placed towers and serves per-tower
/// <see cref="TowerFootprint"/> handles for geometry queries.
/// </summary>
public partial class TowerFootprintTracker : Node
{
    public static TowerFootprintTracker Instance { get; private set; }

    [Export] public CoordConfig Coords { get; set; }

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly Dictionary<Node2D, TowerFootprint> _byTower = new();

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushWarning($"{Name}: another TowerFootprintTracker instance already present.");
            return;
        }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Returns true if every tile in the footprint is unoccupied.</summary>
    public bool CanPlace(IEnumerable<Vector2I> footprint)
    {
        foreach (var tile in footprint)
            if (_occupied.Contains(tile)) return false;
        return true;
    }

    /// <summary>Marks tiles as occupied and stores a footprint handle for the tower.</summary>
    public void Register(Node2D tower, IReadOnlyList<Vector2I> footprint)
    {
        if (tower == null || Coords == null) return;
        for (int i = 0; i < footprint.Count; i++) _occupied.Add(footprint[i]);
        _byTower[tower] = new TowerFootprint(footprint, Coords);
    }

    /// <summary>Frees the tower's tiles and drops its footprint handle.</summary>
    public void Unregister(Node2D tower)
    {
        if (tower == null || !_byTower.TryGetValue(tower, out var fp)) return;
        foreach (var t in fp.Tiles) _occupied.Remove(t);
        _byTower.Remove(tower);
    }

    /// <summary>Retrieves the footprint geometry for <paramref name="tower"/>.</summary>
    public bool TryGetFootprint(Node2D tower, out TowerFootprint footprint)
    {
        if (tower != null && _byTower.TryGetValue(tower, out footprint)) return true;
        footprint = null;
        return false;
    }
}
