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
    ///
    /// Cost: O(footprint × 4). Returns false if tower has no registered tiles.
    /// </summary>
    public bool TryGetNearestApproachPoint(Node2D tower, Vector2 fromWorld, out Vector2 point)
    {
        point = default;
        if (tower == null || Coords == null) return false;
        if (!_byTower.TryGetValue(tower, out var tiles) || tiles.Length == 0) return false;

        var set = new HashSet<Vector2I>(tiles);
        float tp = Coords.TilePixelSize;
        float bestDistSq = float.MaxValue;
        bool found = false;

        foreach (var t in tiles)
        {
            Vector2 origin = CoordHelper.TileToWorld(t, Coords);
            Vector2 tl = origin;
            Vector2 tr = origin + new Vector2(tp, 0);
            Vector2 bl = origin + new Vector2(0, tp);
            Vector2 br = origin + new Vector2(tp, tp);

            if (!set.Contains(t + Vector2I.Up))    TryEdge(tl, tr, fromWorld, ref bestDistSq, ref point, ref found);
            if (!set.Contains(t + Vector2I.Down))  TryEdge(bl, br, fromWorld, ref bestDistSq, ref point, ref found);
            if (!set.Contains(t + Vector2I.Left))  TryEdge(tl, bl, fromWorld, ref bestDistSq, ref point, ref found);
            if (!set.Contains(t + Vector2I.Right)) TryEdge(tr, br, fromWorld, ref bestDistSq, ref point, ref found);
        }
        return found;
    }

    /// <summary>
    /// Tests segment [<paramref name="a"/>,<paramref name="b"/>] against
    /// <paramref name="p"/>: computes the closest point on the segment to
    /// <paramref name="p"/> and updates <paramref name="best"/>/<paramref name="bestDistSq"/>
    /// if it beats the current best.
    ///
    /// Used by <see cref="TryGetNearestApproachPoint"/> to scan every
    /// outward-facing tile edge of a tower's footprint and pick the single
    /// edge point closest to the query position.
    /// </summary>
    private static void TryEdge(Vector2 a, Vector2 b, Vector2 p,
        ref float bestDistSq, ref Vector2 best, ref bool found)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq > 0f ? Mathf.Clamp((p - a).Dot(ab) / lenSq, 0f, 1f) : 0f;
        Vector2 c = a + ab * t;
        float d = c.DistanceSquaredTo(p);
        if (d < bestDistSq) { bestDistSq = d; best = c; found = true; }
    }

    /// <summary>
    /// True if the tile containing <paramref name="worldPoint"/> is within
    /// Euclidean distance sqrt(2) * maxTiles * TilePixelSize of any footprint
    /// tile of <paramref name="tower"/> (tile origins compared in world pixels).
    /// Caller picks maxTiles = ceil(AgentRadius / TilePixelSize).
    /// </summary>
    public bool IsWithinTileReach(Node2D tower, Vector2 worldPoint, int maxTiles)
    {
        if (tower == null || Coords == null) return false;
        if (!_byTower.TryGetValue(tower, out var tiles) || tiles.Length == 0) return false;

        Vector2I snappedTile = CoordHelper.WorldToTile(worldPoint, Coords);
        Vector2 snappedWorld = CoordHelper.TileToWorld(snappedTile, Coords);

        float scaledTileSize = maxTiles * Coords.TilePixelSize;
        float requiredDistanceSq = 2f * scaledTileSize * scaledTileSize;

        foreach (var t in tiles)
        {
            Vector2 towerTileWorld = CoordHelper.TileToWorld(t, Coords);
            if (snappedWorld.DistanceSquaredTo(towerTileWorld) <= requiredDistanceSq)
                return true;
        }
        return false;
    }
}
