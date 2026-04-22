using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.terrain;
using towerdefensegame.scripts.towers;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Manages a static grid of NavigationRegion2D cells covering the entire pocket
/// dimension. All cells within ChunkManager.Bounds are baked once
/// on _Ready and rebaked selectively whenever a tower is placed or removed.
///
/// Rebaking is scoped: given a set of tile coordinates (a tower footprint),
/// the manager maps tiles → chunks → nav cells and re-queues only the cells that
/// intersect the changed area, so unaffected regions are never touched.
/// </summary>
[GlobalClass]
public partial class PocketNavGridManager : Node2D
{
    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public CoordConfig CoordConfig { get; set; }
    [Export] public EnemyConfig EnemyConfig { get; set; }
    [Export] public TowerPlacementManager TowerPlacementManager { get; set; }

    [Export] public bool DebugDrawEnabled { get; set; }

    // ── Internal types ──────────────────────────────────────────────────────────

    private enum CellState { Queued, Baking, Active }

    private sealed class NavCell
    {
        public NavigationRegion2D? Region;
        public CellState State;
    }

    // ── State ───────────────────────────────────────────────────────────────────

    private readonly Dictionary<Vector2I, NavCell> _cells       = new();
    private readonly HashSet<Vector2I>             _bakePending = new();
    private bool _baking;
    private DebugCellDraw _debugDraw;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (ChunkManager == null) { GD.PushWarning($"{Name}: ChunkManager not assigned."); return; }
        if (CoordConfig  == null) { GD.PushWarning($"{Name}: CoordConfig not assigned.");  return; }
        if (EnemyConfig  == null) { GD.PushWarning($"{Name}: EnemyConfig not assigned.");  return; }

        if (!ChunkManager.BoundsEnabled)
            GD.PushWarning($"{Name}: ChunkManager.BoundsEnabled is false — PocketNavGridManager requires bounded terrain.");

        if (TowerPlacementManager != null)
        {
            TowerPlacementManager.TowerPlaced  += OnTowerFootprintChanged;
            TowerPlacementManager.TowerRemoved += OnTowerFootprintChanged;
        }

        var cellMin = CoordHelper.ChunkToNavCell(ChunkManager.BoundsMin, CoordConfig);
        var cellMax = CoordHelper.ChunkToNavCell(ChunkManager.BoundsMax, CoordConfig);

        for (int cellY = cellMin.Y; cellY <= cellMax.Y; cellY++)
        for (int cellX = cellMin.X; cellX <= cellMax.X; cellX++)
        {
            var cellCoord = new Vector2I(cellX, cellY);
            _cells[cellCoord] = new NavCell { State = CellState.Queued };
            _bakePending.Add(cellCoord);
        }

        _debugDraw = new DebugCellDraw(this);
        AddChild(_debugDraw);
    }

    public override void _ExitTree()
    {
        if (TowerPlacementManager != null)
        {
            TowerPlacementManager.TowerPlaced  -= OnTowerFootprintChanged;
            TowerPlacementManager.TowerRemoved -= OnTowerFootprintChanged;
        }
    }

    // ── Tower change handling ───────────────────────────────────────────────────

    private void OnTowerFootprintChanged(IReadOnlyList<Vector2I> tiles) => RebakeFootprint(tiles);

    /// <summary>
    /// Re-queues every nav cell that intersects the given tile footprint.
    /// Call this after a tower is placed or removed.
    /// </summary>
    public void RebakeFootprint(IEnumerable<Vector2I> tiles)
    {
        foreach (var tile in tiles)
        {
            var chunkCoord = CoordHelper.TileToChunk(tile, CoordConfig);
            var cellCoord  = CoordHelper.ChunkToNavCell(chunkCoord, CoordConfig);

            if (!_cells.TryGetValue(cellCoord, out var cell)) continue;
            if (cell.State == CellState.Active)
                cell.State = CellState.Queued;
            _bakePending.Add(cellCoord);
        }
    }

    // ── Per-frame update ────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        ProcessBakePending();
        if (DebugDrawEnabled) _debugDraw.QueueRedraw();
    }

    private void ProcessBakePending()
    {
        if (_baking || _bakePending.Count == 0) return;

        // No distance-to-center prioritization needed — pick any pending cell.
        Vector2I cellCoord = default;
        foreach (var pending in _bakePending) { cellCoord = pending; break; }

        _bakePending.Remove(cellCoord);
        if (_cells.TryGetValue(cellCoord, out var cell) && cell.State == CellState.Queued)
            BakeCell(cellCoord, cell);
    }

    // ── Baking ──────────────────────────────────────────────────────────────────

    private void BakeCell(Vector2I cellCoord, NavCell cell)
    {
        _baking    = true;
        cell.State = CellState.Baking;

        float agentRadius = EnemyConfig.AgentRadius;
        float cellPx = CoordHelper.NavCellSizePixels(CoordConfig);
        var cellWorldPos = CoordHelper.NavCellToWorld(cellCoord, CoordConfig);

        // Expand boundary outward by agentRadius so the eroded navigable area lands
        // exactly on the true cell edge — keeps adjacent cells stitchable.
        var e = new Vector2(agentRadius, agentRadius);
        var expandedRect = new[]
        {
            cellWorldPos - e,
            cellWorldPos + new Vector2(cellPx + agentRadius, -agentRadius),
            cellWorldPos + new Vector2(cellPx + agentRadius,  cellPx + agentRadius),
            cellWorldPos + new Vector2(-agentRadius,           cellPx + agentRadius),
        };

        var navPoly    = new NavigationPolygon { AgentRadius = agentRadius };
        var sourceData = new NavigationMeshSourceGeometryData2D();

        sourceData.AddTraversableOutline(expandedRect);

        // Tower obstacles — scan "Towers" group and extract collision geometry.
        // Handles both CollisionPolygon2D and CollisionShape2D (RectangleShape2D).
        foreach (var node in GetTree().GetNodesInGroup("Towers"))
        {
            if (node is not StaticBody2D body) continue;
            foreach (var child in body.GetChildren())
            {
                Vector2[]? poly = null;

                if (child is CollisionPolygon2D cp)
                {
                    var xform = cp.GlobalTransform;
                    var local = cp.Polygon;
                    poly = new Vector2[local.Length];
                    for (int i = 0; i < local.Length; i++)
                        poly[i] = xform * local[i];
                }
                else if (child is CollisionShape2D cs && cs.Shape is RectangleShape2D rect)
                {
                    poly = RectShapeToPolygon(cs.GlobalTransform, rect.Size);
                }

                if (poly != null)
                    AddClippedObstruction(sourceData, poly, expandedRect);
            }
        }

        var capturedRegion = cell.Region;
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData, Callable.From(() =>
            Callable.From(() => ApplyCellBake(cellCoord, navPoly, capturedRegion)).CallDeferred()
        ));
    }

    private void ApplyCellBake(Vector2I cellCoord, NavigationPolygon navPoly, NavigationRegion2D? capturedRegion)
    {
        _baking = false;

        if (!_cells.TryGetValue(cellCoord, out var cell)) return;

        if (capturedRegion == null)
        {
            if (cell.Region != null) return;
            var region = new NavigationRegion2D { NavigationPolygon = navPoly };
            AddChild(region);
            cell.Region = region;
        }
        else
        {
            if (cell.Region != capturedRegion) return;
            capturedRegion.NavigationPolygon = navPoly;
        }

        cell.State = _bakePending.Contains(cellCoord) ? CellState.Queued : CellState.Active;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Vector2[] RectShapeToPolygon(Transform2D xform, Vector2 size)
    {
        var h = size * 0.5f;
        return new[]
        {
            xform * new Vector2(-h.X, -h.Y),
            xform * new Vector2( h.X, -h.Y),
            xform * new Vector2( h.X,  h.Y),
            xform * new Vector2(-h.X,  h.Y),
        };
    }

    private static void AddClippedObstruction(
        NavigationMeshSourceGeometryData2D sourceData,
        Vector2[] poly,
        Vector2[] boundary)
    {
        foreach (var piece in Geometry2D.IntersectPolygons(poly, boundary))
            sourceData.AddObstructionOutline(piece);
    }

    // ── Debug draw ──────────────────────────────────────────────────────────────

    private partial class DebugCellDraw(PocketNavGridManager manager) : Node2D
    {
        private static readonly Color ColActive = new(0.20f, 1.00f, 0.30f, 0.12f);
        private static readonly Color ColBaking = new(1.00f, 0.90f, 0.10f, 0.18f);
        private static readonly Color ColQueued = new(1.00f, 0.50f, 0.10f, 0.18f);

        public override void _Draw()
        {
            if (!manager.DebugDrawEnabled) return;
            float cellPx = CoordHelper.NavCellSizePixels(manager.CoordConfig);

            foreach (var (cellCoord, cell) in manager._cells)
            {
                var cellWorldPos = CoordHelper.NavCellToWorld(cellCoord, manager.CoordConfig);
                var size         = Vector2.One * cellPx;

                var fill = cell.State switch
                {
                    CellState.Active => ColActive,
                    CellState.Baking => ColBaking,
                    _                => ColQueued,
                };

                DrawRect(new Rect2(cellWorldPos, size), fill);
                DrawRect(new Rect2(cellWorldPos, size), new Color(fill, 0.7f), false, 3f);

                var center = cellWorldPos + size * 0.5f;
                DrawString(ThemeDB.FallbackFont, center, cellCoord.ToString(),
                    HorizontalAlignment.Center, -1, (int)(cellPx * 0.12f), Colors.White);
            }
        }
    }
}
