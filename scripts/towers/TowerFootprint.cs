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
