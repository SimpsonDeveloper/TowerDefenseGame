using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Tracks which tile coordinates are occupied by placed towers inside the
/// pocket dimension. Also serves per-tower perimeter points (world-space
/// corners of the outer tiles of each tower's footprint) for enemy targeting.
/// </summary>
public partial class TowerFootprintTracker : Node
{
    public static TowerFootprintTracker Instance { get; private set; }

    [Export] public CoordConfig Coords { get; set; }

    private readonly HashSet<Vector2I> _occupied = new();
    private readonly Dictionary<Node2D, Vector2I[]> _byTower = new();

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

    // ── Placement queries ───────────────────────────────────────────────────

    /// <summary>Returns true if every tile in the footprint is unoccupied.</summary>
    public bool CanPlace(IEnumerable<Vector2I> footprint)
    {
        foreach (var tile in footprint)
            if (_occupied.Contains(tile)) return false;
        return true;
    }

    // ── Per-tower register / unregister ─────────────────────────────────────

    /// <summary>Marks tiles as occupied and associates the footprint with the tower node.</summary>
    public void Register(Node2D tower, IReadOnlyList<Vector2I> footprint)
    {
        if (tower == null) return;
        var copy = new Vector2I[footprint.Count];
        for (int i = 0; i < footprint.Count; i++)
        {
            copy[i] = footprint[i];
            _occupied.Add(footprint[i]);
        }
        _byTower[tower] = copy;
    }

    /// <summary>Frees the tower's tiles and drops the per-tower record.</summary>
    public void Unregister(Node2D tower)
    {
        if (tower == null || !_byTower.TryGetValue(tower, out var tiles)) return;
        foreach (var t in tiles) _occupied.Remove(t);
        _byTower.Remove(tower);
    }

    // ── Perimeter points ────────────────────────────────────────────────────

    /// <summary>
    /// World-space corners of the outer tiles of <paramref name="tower"/>'s
    /// footprint. An "outer" tile is one that has at least one 4-neighbor not
    /// in the footprint — so concave holes and non-rectangular shapes produce
    /// corners around their actual outline, not just around a bounding box.
    ///
    /// Points are raw tile corners. Callers are expected to snap them onto
    /// the nav mesh and test reachability themselves.
    /// </summary>
    public Vector2[] GetPerimeterPoints(Node2D tower)
    {
        if (tower == null || Coords == null) return Array.Empty<Vector2>();
        if (!_byTower.TryGetValue(tower, out var tiles) || tiles.Length == 0)
            return Array.Empty<Vector2>();

        var set = new HashSet<Vector2I>(tiles);
        var corners = new HashSet<Vector2>();
        float tp = Coords.TilePixelSize;

        foreach (var t in tiles)
        {
            bool outer =
                !set.Contains(t + Vector2I.Up)    ||
                !set.Contains(t + Vector2I.Down)  ||
                !set.Contains(t + Vector2I.Left)  ||
                !set.Contains(t + Vector2I.Right);
            if (!outer) continue;

            Vector2 o = new(t.X * tp, t.Y * tp);
            corners.Add(o);
            corners.Add(o + new Vector2(tp, 0));
            corners.Add(o + new Vector2(0,  tp));
            corners.Add(o + new Vector2(tp, tp));
        }

        var arr = new Vector2[corners.Count];
        corners.CopyTo(arr);
        return arr;
    }

    /// <summary>
    /// True if <paramref name="worldPoint"/>'s tile is within
    /// <paramref name="maxTiles"/> Manhattan tile-steps of any tile in
    /// <paramref name="tower"/>'s footprint. Caller picks maxTiles based on
    /// agent radius: (int)(AgentRadius / TilePixelSize) + 1.
    /// </summary>
    public bool IsWithinTileReach(Node2D tower, Vector2 worldPoint, int maxTiles)
    {
        if (tower == null || Coords == null) return false;
        if (!_byTower.TryGetValue(tower, out var tiles) || tiles.Length == 0) return false;

        int tp = Coords.TilePixelSize;
        Vector2I pt = new(Mathf.FloorToInt(worldPoint.X / tp), Mathf.FloorToInt(worldPoint.Y / tp));

        foreach (var t in tiles)
        {
            int d = Mathf.Abs(pt.X - t.X) + Mathf.Abs(pt.Y - t.Y);
            if (d <= maxTiles) return true;
        }
        return false;
    }
}
