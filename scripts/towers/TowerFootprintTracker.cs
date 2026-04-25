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

            Vector2 o = CoordHelper.TileToWorld(t, Coords);
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
    /// Closest point on any outward-facing footprint edge of <paramref name="tower"/>
    /// to <paramref name="fromWorld"/>. "Outward-facing" = side of a tile whose
    /// 4-neighbor is not in the footprint, so concave shapes work.
    /// </summary>
    public bool TryGetNearestApproachPoint(Node2D tower, Vector2 fromWorld, out Vector2 point)
    {
        point = default;
        if (!TryGetTiles(tower, out var tiles, out var set)) return false;
        NearestEdgePoint(tiles, set, fromWorld, out point);
        return true;
    }

    /// <summary>
    /// Squared distance from <paramref name="worldPoint"/> to the nearest
    /// outward-facing footprint edge of <paramref name="tower"/>. Returns
    /// float.MaxValue if the tower has no registered tiles.
    /// </summary>
    public float DistanceSqToFootprint(Node2D tower, Vector2 worldPoint)
    {
        if (!TryGetTiles(tower, out var tiles, out var set)) return float.MaxValue;
        return NearestEdgePoint(tiles, set, worldPoint, out _);
    }

    /// <summary>True if <paramref name="worldPoint"/> lies within <paramref name="distance"/> px of any outward-facing footprint edge.</summary>
    public bool IsWithinDistance(Node2D tower, Vector2 worldPoint, float distance)
        => DistanceSqToFootprint(tower, worldPoint) <= distance * distance;

    private bool TryGetTiles(Node2D tower, out Vector2I[] tiles, out HashSet<Vector2I> set)
    {
        tiles = null; set = null;
        if (tower == null || Coords == null) return false;
        if (!_byTower.TryGetValue(tower, out tiles) || tiles.Length == 0) return false;
        set = new HashSet<Vector2I>(tiles);
        return true;
    }

    /// <summary>
    /// Scans every outward-facing edge of every footprint tile and returns
    /// the squared distance from <paramref name="p"/> to the nearest edge
    /// point, with that edge point in <paramref name="closest"/>.
    /// O(footprint × 4). Shared by approach-point and distance queries.
    /// </summary>
    private float NearestEdgePoint(Vector2I[] tiles, HashSet<Vector2I> set, Vector2 p, out Vector2 closest)
    {
        float bestSq = float.MaxValue;
        closest = default;
        float tp = Coords.TilePixelSize;
        foreach (var t in tiles)
        {
            Vector2 origin = CoordHelper.TileToWorld(t, Coords);
            Vector2 tl = origin;
            Vector2 tr = origin + new Vector2(tp, 0);
            Vector2 bl = origin + new Vector2(0, tp);
            Vector2 br = origin + new Vector2(tp, tp);
            if (!set.Contains(t + Vector2I.Up))    TryEdge(tl, tr, p, ref bestSq, ref closest);
            if (!set.Contains(t + Vector2I.Down))  TryEdge(bl, br, p, ref bestSq, ref closest);
            if (!set.Contains(t + Vector2I.Left))  TryEdge(tl, bl, p, ref bestSq, ref closest);
            if (!set.Contains(t + Vector2I.Right)) TryEdge(tr, br, p, ref bestSq, ref closest);
        }
        return bestSq;
    }

    /// <summary>
    /// Computes the closest point on segment [<paramref name="a"/>,<paramref name="b"/>]
    /// to <paramref name="p"/> and updates the running best if it beats <paramref name="bestSq"/>.
    /// Used by <see cref="NearestEdgePoint"/> to scan footprint edges.
    /// </summary>
    private static void TryEdge(Vector2 a, Vector2 b, Vector2 p, ref float bestSq, ref Vector2 best)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq > 0f ? Mathf.Clamp((p - a).Dot(ab) / lenSq, 0f, 1f) : 0f;
        Vector2 c = a + ab * t;
        float d = c.DistanceSquaredTo(p);
        if (d < bestSq) { bestSq = d; best = c; }
    }
}
