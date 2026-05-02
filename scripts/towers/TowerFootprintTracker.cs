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
    /// <summary>Per-viewport registry. Each tracker registers itself on
    /// <see cref="_EnterTree"/> against the viewport it lives in; consumers
    /// (towers, enemies) resolve their tracker by their own viewport.</summary>
    private static readonly Dictionary<Viewport, TowerFootprintTracker> ByViewport = new();

    /// <summary>Returns the tracker registered for <paramref name="viewport"/>,
    /// or null if none. Cache the result; do not call per-frame.</summary>
    public static TowerFootprintTracker ForViewport(Viewport viewport)
        => viewport != null && ByViewport.TryGetValue(viewport, out var t) ? t : null;

    [Export] public CoordConfig Coords { get; set; }

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly Dictionary<Node2D, TowerFootprint> _byTower = new();
    private readonly Dictionary<Vector2I, TowerFootprint> _tileToFootprint = new();

    public override void _EnterTree()
    {
        Viewport vp = GetViewport();
        if (vp == null) return;
        if (ByViewport.TryGetValue(vp, out var existing) && existing != this)
        {
            GD.PushWarning($"{Name}: another TowerFootprintTracker already registered for this viewport.");
            return;
        }
        ByViewport[vp] = this;
    }

    public override void _ExitTree()
    {
        Viewport vp = GetViewport();
        if (vp != null && ByViewport.TryGetValue(vp, out var t) && t == this)
            ByViewport.Remove(vp);
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
        var fp = new TowerFootprint(footprint, Coords);
        for (int i = 0; i < footprint.Count; i++)
        {
            _occupied.Add(footprint[i]);
            _tileToFootprint[footprint[i]] = fp;
        }
        _byTower[tower] = fp;
    }

    /// <summary>Frees the tower's tiles and drops its footprint handle.</summary>
    public void Unregister(Node2D tower)
    {
        if (tower == null || !_byTower.TryGetValue(tower, out var fp)) return;
        foreach (var t in fp.Tiles)
        {
            _occupied.Remove(t);
            _tileToFootprint.Remove(t);
        }
        _byTower.Remove(tower);
    }

    /// <summary>Retrieves the footprint geometry for <paramref name="tower"/>.</summary>
    public bool TryGetFootprint(Node2D tower, out TowerFootprint footprint)
    {
        if (tower != null && _byTower.TryGetValue(tower, out footprint)) return true;
        footprint = null;
        return false;
    }

    /// <summary>True iff any tower's footprint covers <paramref name="tile"/>.</summary>
    public bool IsOccupied(Vector2I tile) => _occupied.Contains(tile);

    /// <summary>Returns the footprint that owns <paramref name="tile"/>, if any.</summary>
    public bool TryGetFootprintAt(Vector2I tile, out TowerFootprint footprint)
        => _tileToFootprint.TryGetValue(tile, out footprint);
}
