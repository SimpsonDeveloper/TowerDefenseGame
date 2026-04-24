using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Centralized cache of per-tower "approach" points on the navigation mesh.
///
/// Each registered tower contributes N points sampled along its body collision
/// shape, transformed to global space, and snapped to the nearest walkable
/// navigation polygon. Enemies consume these points when choosing where to
/// path to while attacking a tower.
///
/// Shape type support: CircleShape2D, RectangleShape2D, ConvexPolygonShape2D.
/// Add cases in <see cref="SampleShapeLocal"/> if new shape types are added.
///
/// Entries are computed lazily and marked stale whenever the underlying nav
/// mesh may have changed. Call <see cref="MarkAllStale"/> after any rebake.
/// </summary>
public partial class TowerPerimeterPointServer : Node
{
    public static TowerPerimeterPointServer Instance { get; private set; }

    [Export] public int SampleCount { get; set; } = 8;

    /// <summary>
    /// Maximum distance a sample may be displaced by <c>MapGetClosestPoint</c>.
    /// Samples moved further than this are rejected — they likely snapped
    /// across a wall to an unrelated nav polygon.
    /// </summary>
    [Export] public float SnapTolerancePixels { get; set; } = 16f;

    private sealed class Entry
    {
        public CollisionShape2D BodyShape;
        public Vector2[] Points = Array.Empty<Vector2>();
        public bool Stale = true;
    }

    private readonly Dictionary<Node2D, Entry> _entries = new();

    /// <summary>
    /// Fired after <see cref="MarkAllStale"/>. Consumers should clear
    /// reachability caches that depend on the current nav mesh state.
    /// </summary>
    public event Action NavInvalidated;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushWarning($"{Name}: another TowerPerimeterPointServer instance already present.");
            return;
        }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public void Register(Node2D tower, CollisionShape2D bodyShape)
    {
        if (tower == null || bodyShape == null) return;
        _entries[tower] = new Entry { BodyShape = bodyShape, Stale = true };
    }

    public void Unregister(Node2D tower)
    {
        if (tower == null) return;
        _entries.Remove(tower);
    }

    /// <summary>Call when a tower has been repositioned.</summary>
    public void OnTowerMoved(Node2D tower)
    {
        if (tower != null && _entries.TryGetValue(tower, out var e))
            e.Stale = true;
    }

    /// <summary>
    /// Invalidate every entry. Call after the nav mesh is (or will be) rebaked,
    /// since snapped points depend on the current walkable polygons.
    /// </summary>
    public void MarkAllStale()
    {
        foreach (var e in _entries.Values) e.Stale = true;
        NavInvalidated?.Invoke();
    }

    /// <summary>
    /// Perimeter points in global coordinates, snapped to the nav mesh.
    /// Empty array if the tower is unregistered or its shape is unsupported.
    /// </summary>
    public Vector2[] GetPerimeterPoints(Node2D tower)
    {
        if (tower == null || !_entries.TryGetValue(tower, out var e))
            return Array.Empty<Vector2>();

        if (e.Stale || e.Points.Length == 0)
        {
            e.Points = Compute(tower, e.BodyShape);
            e.Stale = false;
        }
        return e.Points;
    }

    // ── Computation ─────────────────────────────────────────────────────────

    private Vector2[] Compute(Node2D tower, CollisionShape2D bodyShape)
    {
        if (bodyShape?.Shape == null) return Array.Empty<Vector2>();

        Rid navMap = tower.GetWorld2D().NavigationMap;
        if (!navMap.IsValid) return Array.Empty<Vector2>();

        Vector2[] localSamples = SampleShapeLocal(bodyShape.Shape, SampleCount);
        if (localSamples.Length == 0) return Array.Empty<Vector2>();

        Transform2D xform = bodyShape.GlobalTransform;
        float tolSq = SnapTolerancePixels * SnapTolerancePixels;

        var results = new List<Vector2>(localSamples.Length);
        foreach (Vector2 local in localSamples)
        {
            Vector2 world = xform * local;
            Vector2 snapped = NavigationServer2D.MapGetClosestPoint(navMap, world);
            if (snapped.DistanceSquaredTo(world) > tolSq) continue; // snapped across a wall
            results.Add(snapped);
        }
        return results.ToArray();
    }

    private static Vector2[] SampleShapeLocal(Shape2D shape, int sampleCount)
    {
        var points = new Vector2[sampleCount];
        float step = Mathf.Tau / sampleCount;

        switch (shape)
        {
            case CircleShape2D c:
                for (int i = 0; i < sampleCount; i++)
                {
                    float a = i * step;
                    points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * c.Radius;
                }
                return points;

            case RectangleShape2D r:
                Vector2 half = r.Size * 0.5f;
                for (int i = 0; i < sampleCount; i++)
                {
                    float a = i * step;
                    Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                    // Cast a ray from center in direction `dir`, clip to rect edge.
                    float tx = Mathf.Abs(dir.X) > 1e-6f ? half.X / Mathf.Abs(dir.X) : float.MaxValue;
                    float ty = Mathf.Abs(dir.Y) > 1e-6f ? half.Y / Mathf.Abs(dir.Y) : float.MaxValue;
                    points[i] = dir * Mathf.Min(tx, ty);
                }
                return points;

            case ConvexPolygonShape2D poly:
                Vector2[] verts = poly.Points;
                if (verts == null || verts.Length == 0) return Array.Empty<Vector2>();

                float total = 0f;
                var lens = new float[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    lens[i] = verts[i].DistanceTo(verts[(i + 1) % verts.Length]);
                    total += lens[i];
                }
                for (int i = 0; i < sampleCount; i++)
                {
                    float target = (float)i / sampleCount * total;
                    for (int j = 0; j < verts.Length; j++)
                    {
                        if (target <= lens[j])
                        {
                            float t = lens[j] > 0f ? target / lens[j] : 0f;
                            points[i] = verts[j].Lerp(verts[(j + 1) % verts.Length], t);
                            break;
                        }
                        target -= lens[j];
                    }
                }
                return points;

            default:
                GD.PushWarning($"TowerPerimeterPointServer: unsupported shape type {shape.GetType().Name}. Add a case in SampleShapeLocal.");
                return Array.Empty<Vector2>();
        }
    }
}
