using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Immutable per-tower footprint geometry. Owns the tile set plus all
/// edge-math used by enemy approach logic.
/// </summary>
public sealed class TowerFootprint
{
    private readonly Vector2I[] _tiles;
    private readonly HashSet<Vector2I> _set;
    private readonly float _tilePx;

    /// <summary>
    /// Snapshots the tile list into an array + hash set for fast neighbor
    /// lookups (used to identify outward-facing edges) and caches the tile
    /// pixel size from <paramref name="coords"/>.
    /// </summary>
    public TowerFootprint(IReadOnlyList<Vector2I> tiles, CoordConfig coords)
    {
        _tiles = new Vector2I[tiles.Count];
        for (int i = 0; i < tiles.Count; i++) _tiles[i] = tiles[i];
        _set = new HashSet<Vector2I>(_tiles);
        _tilePx = coords.TilePixelSize;
        _coords = coords;
    }

    private readonly CoordConfig _coords;

    public IReadOnlyList<Vector2I> Tiles => _tiles;

    /// <summary>Squared distance from <paramref name="p"/> to nearest outward-facing edge.</summary>
    public float DistanceSqTo(Vector2 p) => NearestEdge(p, out _);

    /// <summary>Closest point on any outward-facing edge to <paramref name="from"/>.</summary>
    public Vector2 NearestEdgePoint(Vector2 from)
    {
        NearestEdge(from, out var pt);
        return pt;
    }

    /// <summary>
    /// Earliest point on segment [<paramref name="outside"/>, <paramref name="inside"/>]
    /// whose distance to footprint ≤ sqrt(<paramref name="standoffSq"/>).
    /// 8 bisection steps → ~segment_len/256 precision.
    /// </summary>
    public Vector2 FindStandoffPoint(Vector2 outside, Vector2 inside, float standoffSq)
    {
        float lo = 0f, hi = 1f;
        for (int k = 0; k < 8; k++)
        {
            float mid = 0.5f * (lo + hi);
            Vector2 m = outside.Lerp(inside, mid);
            if (DistanceSqTo(m) <= standoffSq) hi = mid;
            else lo = mid;
        }
        return outside.Lerp(inside, hi);
    }

    /// <summary>
    /// Finds a navmesh-reachable point within <paramref name="standoff"/> of an
    /// outward-facing edge. Iterates tiles in ascending distance from
    /// <paramref name="enemyPos"/>; per tile, picks the nearest point on each
    /// outward edge to the enemy, snaps via <c>MapGetClosestPoint</c>, and
    /// accepts the first whose snap-to-edge distance ≤ standoff. The returned
    /// <paramref name="destination"/> is already on the navmesh — feed straight
    /// to <c>MapGetPath</c>.
    /// </summary>
    public bool TryFindApproachDestination(
        Vector2 enemyPos, float standoff, Rid navMap, out Vector2 destination)
    {
        destination = default;
        if (!navMap.IsValid || _tiles.Length == 0) return false;

        float standoffSq = standoff * standoff;
        int[] order = BuildTileOrderByDistance(enemyPos);
        for (int oi = 0; oi < order.Length; oi++)
        {
            if (TryFindApproachOnTile(_tiles[order[oi]], enemyPos, navMap, standoffSq, out destination))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns tile indices sorted by squared distance from each tile's center
    /// to <paramref name="enemyPos"/>, ascending. Drives the Euclidean-nearest
    /// iteration order in <see cref="TryFindApproachDestination"/>.
    /// </summary>
    private int[] BuildTileOrderByDistance(Vector2 enemyPos)
    {
        int n = _tiles.Length;
        float half = _tilePx * 0.5f;
        int[] order = new int[n];
        float[] distSq = new float[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
            Vector2 center = CoordHelper.TileToWorld(_tiles[i], _coords)
                             + new Vector2(half, half);
            distSq[i] = center.DistanceSquaredTo(enemyPos);
        }
        System.Array.Sort(distSq, order);
        return order;
    }

    /// <summary>
    /// Tests each of <paramref name="tile"/>'s four edges; an edge is
    /// outward-facing when no neighboring footprint tile sits across it.
    /// For each outward edge, hands the enemy-nearest edge point to
    /// <see cref="TrySnapEdge"/> and returns the first navmesh-snapped point
    /// within standoff. Order (up/down/left/right) is arbitrary — the first
    /// hit wins.
    /// </summary>
    private bool TryFindApproachOnTile(
        Vector2I tile, Vector2 enemyPos, Rid navMap, float standoffSq, out Vector2 destination)
    {
        destination = default;
        float tp = _tilePx;
        Vector2 origin = CoordHelper.TileToWorld(tile, _coords);
        Vector2 tl = origin;
        Vector2 tr = origin + new Vector2(tp, 0);
        Vector2 bl = origin + new Vector2(0, tp);
        Vector2 br = origin + new Vector2(tp, tp);

        if (!_set.Contains(tile + Vector2I.Up)
            && TrySnapEdge(tl, tr, enemyPos, navMap, standoffSq, out destination)) return true;
        if (!_set.Contains(tile + Vector2I.Down)
            && TrySnapEdge(bl, br, enemyPos, navMap, standoffSq, out destination)) return true;
        if (!_set.Contains(tile + Vector2I.Left)
            && TrySnapEdge(tl, bl, enemyPos, navMap, standoffSq, out destination)) return true;
        if (!_set.Contains(tile + Vector2I.Right)
            && TrySnapEdge(tr, br, enemyPos, navMap, standoffSq, out destination)) return true;
        return false;
    }

    /// <summary>
    /// Projects <paramref name="enemyPos"/> onto segment [a,b] (clamped to
    /// the segment), then snaps that edge point onto the navmesh via
    /// <c>MapGetClosestPoint</c>. Returns true (and emits the snap point) if
    /// the snap landed within sqrt(<paramref name="standoffSq"/>) of the
    /// edge — i.e. there's walkable navmesh adjacent to this edge from which
    /// the enemy can attack.
    /// </summary>
    private static bool TrySnapEdge(
        Vector2 a, Vector2 b, Vector2 enemyPos, Rid navMap, float standoffSq, out Vector2 snap)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq > 0f ? Mathf.Clamp((enemyPos - a).Dot(ab) / lenSq, 0f, 1f) : 0f;
        Vector2 edgePoint = a + ab * t;
        snap = NavigationServer2D.MapGetClosestPoint(navMap, edgePoint);
        return snap.DistanceSquaredTo(edgePoint) <= standoffSq;
    }

    /// <summary>
    /// Brute-force scan over every outward-facing tile edge: returns the
    /// minimum squared distance from <paramref name="p"/> to the footprint
    /// perimeter, with the closest-point witness in <paramref name="closest"/>.
    /// Backs both <see cref="DistanceSqTo"/> and <see cref="NearestEdgePoint"/>;
    /// also drives the standoff-zone test in <see cref="FindStandoffPoint"/>,
    /// so it correctly handles concave footprints (the metric is "any edge",
    /// not "one chosen edge").
    /// </summary>
    private float NearestEdge(Vector2 p, out Vector2 closest)
    {
        float bestSq = float.MaxValue;
        closest = default;
        float tp = _tilePx;
        foreach (var t in _tiles)
        {
            Vector2 origin = CoordHelper.TileToWorld(t, _coords);
            Vector2 tl = origin;
            Vector2 tr = origin + new Vector2(tp, 0);
            Vector2 bl = origin + new Vector2(0, tp);
            Vector2 br = origin + new Vector2(tp, tp);
            if (!_set.Contains(t + Vector2I.Up))    TryEdge(tl, tr, p, ref bestSq, ref closest);
            if (!_set.Contains(t + Vector2I.Down))  TryEdge(bl, br, p, ref bestSq, ref closest);
            if (!_set.Contains(t + Vector2I.Left))  TryEdge(tl, bl, p, ref bestSq, ref closest);
            if (!_set.Contains(t + Vector2I.Right)) TryEdge(tr, br, p, ref bestSq, ref closest);
        }
        return bestSq;
    }

    /// <summary>
    /// Inner loop body for <see cref="NearestEdge"/>: projects <paramref name="p"/>
    /// onto segment [a,b] (clamped) and updates <paramref name="bestSq"/> /
    /// <paramref name="best"/> if this segment beats the running minimum.
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
