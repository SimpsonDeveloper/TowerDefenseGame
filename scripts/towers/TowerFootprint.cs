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
    private readonly Vector2I[] _outwardNeighbors;
    private readonly float _tilePx;
    private readonly CoordConfig _coords;

    /// <summary>
    /// Snapshots the tile list into an array + hash set for fast neighbor
    /// lookups (used to identify outward-facing edges) and caches the tile
    /// pixel size from <paramref name="coords"/>. Also precomputes the unique
    /// set of tiles immediately outside the footprint's outward edges, used
    /// by callers that need to sample adjacent walkable space.
    /// </summary>
    public TowerFootprint(IReadOnlyList<Vector2I> tiles, CoordConfig coords)
    {
        _tiles = new Vector2I[tiles.Count];
        for (int i = 0; i < tiles.Count; i++) _tiles[i] = tiles[i];
        _set = new HashSet<Vector2I>(_tiles);
        _tilePx = coords.TilePixelSize;
        _coords = coords;

        var neighbors = new HashSet<Vector2I>();
        foreach (var t in _tiles)
        {
            var u = t + Vector2I.Up;    if (!_set.Contains(u)) neighbors.Add(u);
            var d = t + Vector2I.Down;  if (!_set.Contains(d)) neighbors.Add(d);
            var l = t + Vector2I.Left;  if (!_set.Contains(l)) neighbors.Add(l);
            var r = t + Vector2I.Right; if (!_set.Contains(r)) neighbors.Add(r);
        }
        _outwardNeighbors = new Vector2I[neighbors.Count];
        neighbors.CopyTo(_outwardNeighbors);
    }

    public IReadOnlyList<Vector2I> Tiles => _tiles;

    /// <summary>Tiles immediately outside the footprint's outward-facing edges
    /// (deduplicated). Use to sample adjacent map state — e.g. fast-rejecting
    /// a tower whose neighbors are all in a different reachability component
    /// than the enemy.</summary>
    public IReadOnlyList<Vector2I> OutwardNeighborTiles => _outwardNeighbors;

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
    /// Enumerates navmesh-snapped approach points in ascending Euclidean
    /// distance from <paramref name="enemyPos"/> (tile order × four edges).
    /// Each yielded point is on the navmesh and within <paramref name="standoff"/>
    /// of an outward-facing edge — feed straight to <c>MapGetPath</c>. The
    /// caller drives termination: take the first that passes additional gates
    /// (e.g. reachability), then break.
    /// </summary>
    public ApproachSnapEnumerator EnumerateApproachSnaps(
        Vector2 enemyPos, Rid navMap, float standoff)
        => new(this, enemyPos, navMap, standoff);

    /// <summary>
    /// Struct enumerator over <see cref="EnumerateApproachSnaps"/> output. Wraps
    /// the per-tile distance-sorted scan plus the four-edge test inside
    /// <see cref="MoveNext"/>, so a foreach over the enumerable allocates only
    /// the tile-order array (one int[] + one float[] per scan).
    /// </summary>
    public struct ApproachSnapEnumerator
    {
        private readonly TowerFootprint _fp;
        private readonly Vector2 _enemyPos;
        private readonly Rid _navMap;
        private readonly float _standoffSq;
        private readonly int[] _order;
        private int _orderIdx;
        private int _edgeIdx;
        public Vector2 Current { get; private set; }

        internal ApproachSnapEnumerator(
            TowerFootprint fp, Vector2 enemyPos, Rid navMap, float standoff)
        {
            _fp = fp;
            _enemyPos = enemyPos;
            _navMap = navMap;
            _standoffSq = standoff * standoff;
            _order = (navMap.IsValid && fp._tiles.Length > 0)
                ? fp.BuildTileOrderByDistance(enemyPos)
                : null;
            _orderIdx = 0;
            _edgeIdx = 0;
            Current = default;
        }

        public ApproachSnapEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_order == null) return false;
            while (_orderIdx < _order.Length)
            {
                Vector2I tile = _fp._tiles[_order[_orderIdx]];
                while (_edgeIdx < 4)
                {
                    int e = _edgeIdx++;
                    if (TryEdge(tile, e, out Vector2 snap))
                    {
                        Current = snap;
                        return true;
                    }
                }
                _orderIdx++;
                _edgeIdx = 0;
            }
            return false;
        }

        private bool TryEdge(Vector2I tile, int edge, out Vector2 snap)
        {
            snap = default;
            float tp = _fp._tilePx;
            Vector2 origin = CoordHelper.TileToWorld(tile, _fp._coords);
            Vector2 a, b; Vector2I neighbor;
            switch (edge)
            {
                case 0: a = origin;                       b = origin + new Vector2(tp, 0);  neighbor = tile + Vector2I.Up;    break;
                case 1: a = origin + new Vector2(0, tp);  b = origin + new Vector2(tp, tp); neighbor = tile + Vector2I.Down;  break;
                case 2: a = origin;                       b = origin + new Vector2(0, tp);  neighbor = tile + Vector2I.Left;  break;
                default:a = origin + new Vector2(tp, 0);  b = origin + new Vector2(tp, tp); neighbor = tile + Vector2I.Right; break;
            }
            if (_fp._set.Contains(neighbor)) return false;
            return TrySnapEdge(a, b, _enemyPos, _navMap, _standoffSq, out snap);
        }
    }

    /// <summary>
    /// Returns tile indices sorted by squared distance from each tile's center
    /// to <paramref name="enemyPos"/>, ascending. Drives the Euclidean-nearest
    /// iteration order in <see cref="EnumerateApproachSnaps"/>.
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
