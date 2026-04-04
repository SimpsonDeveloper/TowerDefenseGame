using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Watches the camera center and PolygonTerrainManager blob changes to decide when to
/// rebake the navigation mesh. Fetches blob polygon data from PolygonTerrainManager at
/// bake time and collects nav_obstacle nodes from the scene.
/// </summary>
[GlobalClass]
public partial class NavBaker : Node
{
    [Export] public PolygonTerrainManager TerrainManager { get; set; }
    [Export] public NavigationRegion2D NavigationRegion { get; set; }

    /// <summary>Node whose position centres the walkable nav boundary (typically the camera).</summary>
    [Export] public Node2D Center { get; set; }

    /// <summary>
    /// Half-extent of the outer walkable nav boundary in pixels.
    /// Must be a multiple of TilePixelSize (16).
    /// </summary>
    [Export] public float WalkableExtent { get; set; } = 3008f; // 188 × 16

    /// <summary>Distance in pixels the center must move from the last bake position to queue a rebake.</summary>
    [Export] public float RebakeDistance { get; set; } = 512f;

    /// <summary>Seconds to wait after a rebake is triggered before actually baking.</summary>
    [Export] public double DebounceDelay { get; set; } = 0.5;

    private double _timer = -1;
    private Vector2 _lastBakeCenter;

    public override void _Ready()
    {
        if (TerrainManager == null)   { GD.PushWarning($"{Name}: TerrainManager not assigned.");   return; }
        if (NavigationRegion == null) { GD.PushWarning($"{Name}: NavigationRegion not assigned."); return; }

        TerrainManager.BlobsUpdated += MarkDirty;
    }

    public override void _ExitTree()
    {
        if (TerrainManager != null)
            TerrainManager.BlobsUpdated -= MarkDirty;
    }

    public void MarkDirty() => _timer = DebounceDelay;
    public void Cancel()    => _timer = -1;

    public override void _Process(double delta)
    {
        // Queue a rebake only when the center has moved far enough and none is already pending.
        if (Center != null && _timer < 0 &&
            Center.GlobalPosition.DistanceTo(_lastBakeCenter) >= RebakeDistance)
            MarkDirty();

        if (_timer >= 0)
        {
            _timer -= delta;
            if (_timer <= 0)
            {
                _timer = -1;
                Bake();
            }
        }
    }

    private void Bake()
    {
        var navPoly    = new NavigationPolygon { AgentRadius = 9f };
        // navPoly.SamplePartitionType = NavigationPolygon.SamplePartitionTypeEnum.Triangulate;
        var sourceData = new NavigationMeshSourceGeometryData2D();

        // Outer walkable boundary. WalkableExtent is rounded to the nearest tile size
        // at bake time. Center is snapped to a half-tile offset so boundary edges always
        // run through the middle of tiles — never on a terrain polygon vertex or edge.
        Vector2 c = Center?.GlobalPosition ?? Vector2.Zero;
        float tileSize = ChunkRenderer.TilePixelSize;
        float e    = Mathf.Round(WalkableExtent / tileSize) * tileSize;
        float half = tileSize * 0.5f;
        c = new Vector2(
            Mathf.Round((c.X - half) / tileSize) * tileSize + half,
            Mathf.Round((c.Y - half) / tileSize) * tileSize + half);
        var traversable = new Vector2[]
        {
            new(c.X - e, c.Y - e),
            new(c.X + e, c.Y - e),
            new(c.X + e, c.Y + e),
            new(c.X - e, c.Y + e),
        };
        sourceData.AddTraversableOutline(traversable);

        // Terrain blob obstructions from PolygonTerrainManager.
        foreach (var poly in TerrainManager.GetBlobPolygons())
            AddClippedObstruction(sourceData, poly, traversable);

        // Extra nav obstacles (crystals, towers, etc.).
        foreach (var node in GetTree().GetNodesInGroup(PolygonTerrainManager.NavObstacleGroup))
        {
            if (node is not StaticBody2D body) continue;
            foreach (var child in body.GetChildren())
            {
                if (child is not CollisionPolygon2D cp) continue;
                var xform = cp.GlobalTransform;
                var local = cp.Polygon;
                var world = new Vector2[local.Length];
                for (int i = 0; i < local.Length; i++)
                    world[i] = xform * local[i];
                AddClippedObstruction(sourceData, world, traversable);
            }
        }

        _lastBakeCenter = Center?.GlobalPosition ?? Vector2.Zero;

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData);
        NavigationRegion.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(NavigationRegion.GetRid(), navPoly);
    }

    /// <summary>
    /// Clips an obstruction polygon to the traversable boundary before adding it
    /// to source data. Polygons entirely outside the boundary are dropped.
    /// Polygons partially outside are trimmed so Godot never sees geometry that
    /// straddles or touches the boundary edge, which causes convex partition failures.
    /// </summary>
    private static void AddClippedObstruction(
        NavigationMeshSourceGeometryData2D sourceData,
        Vector2[] poly,
        Vector2[] boundary)
    {
        var clipped = Geometry2D.IntersectPolygons(poly, boundary);
        foreach (var piece in clipped)
            sourceData.AddObstructionOutline(piece);
    }
}
