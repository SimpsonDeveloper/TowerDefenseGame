using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Maintains a sparse grid of NavigationRegion2D cells around a moving center node.
/// Each cell covers CellSizeChunks × CellSizeChunks chunks. Cells are baked
/// asynchronously on a background thread and stitched together automatically by
/// Godot's navigation server — adjacent cells share exact polygon edges at their
/// shared boundary, so no NavigationLink2D nodes are needed.
///
/// Replaces NavBaker.
/// </summary>
[GlobalClass]
public partial class NavGridManager : Node
{
    [Export] public PolygonTerrainManager TerrainManager { get; set; }
    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public CoordConfig CoordConfig { get; set; }

    /// <summary>
    /// Chebyshev radius in cells around the center cell to keep loaded and baked.
    /// Radius 2 keeps a 5×5 grid (25 cells) active.
    /// </summary>
    [Export] public int ActiveRadius { get; set; } = 2;

    /// <summary>When true, draws cell boundaries and coords in-world. Press F6 to scan for isolated cells.</summary>
    [Export] public bool DebugDrawEnabled { get; set; }
    
    /// <summary>Node whose position drives cell loading (typically the player).</summary>
    [ExportGroup("Center")]
    [Export] public Node2D Center { get; set; }
    /// <summary>Group of the Node whose position drives cell loading (typically the player). Overrides Center</summary>
    [Export] public string CenterGroup { get; set; } = "Player";

    // ── Internal types ──────────────────────────────────────────────────────────

    private enum CellState { Queued, Baking, Active }

    private sealed class NavCell
    {
        /// <summary>
        /// Null until the first bake completes and the region is added to the scene.
        /// Keeping it null avoids registering an empty region with the navigation server,
        /// which prevents stale/incomplete edge-connection evaluations on first bake.
        /// </summary>
        public NavigationRegion2D? Region;
        public CellState State;
    }

    // ── State ───────────────────────────────────────────────────────────────────

    private readonly Dictionary<Vector2I, NavCell> _cells       = new();
    private readonly HashSet<Vector2I>             _bakePending = new();
    private bool     _baking;
    private Vector2I _lastCenterCell = new(int.MinValue, int.MinValue);
    private DebugCellDraw _debugDraw;
    private DebugIsolationDraw _debugIsolationDraw;
    private List<Vector2[]> _debugBadPaths = [];

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (TerrainManager == null) { GD.PushWarning($"{Name}: TerrainManager not assigned."); return; }
        if (ChunkManager   == null) { GD.PushWarning($"{Name}: ChunkManager not assigned.");   return; }
        if (CoordConfig    == null) { GD.PushWarning($"{Name}: CoordConfig not assigned.");    return; }

        if (CoordConfig.NavCellSizeChunks < 1)
        {
            GD.PushWarning($"{Name}: CoordConfig.NavCellSizeChunks must be >= 1; clamping to 1.");
            CoordConfig.NavCellSizeChunks = 1;
        }

        TerrainManager.BlobsUpdated += OnBlobsUpdated;

        _debugDraw = new DebugCellDraw(this);
        AddChild(_debugDraw);
        _debugIsolationDraw = new DebugIsolationDraw(this);
        AddChild(_debugIsolationDraw);
    }

    public override void _ExitTree()
    {
        if (TerrainManager != null)
            TerrainManager.BlobsUpdated -= OnBlobsUpdated;
    }

    // ── Terrain change handling ─────────────────────────────────────────────────

    private void OnBlobsUpdated()
    {
        var affected = TerrainManager.LastAffectedChunks;

        if (affected.Count == 0)
        {
            // Full clear (chunks reset) — tear down everything.
            foreach (var cell in _cells.Values)
                cell.Region?.QueueFree();
            _cells.Clear();
            _bakePending.Clear();
            _baking = false;
            return;
        }

        // Re-queue only cells that overlap the changed chunks.
        foreach (var chunkCoord in affected)
        {
            var cellCoord = ChunkToCell(chunkCoord);
            if (!_cells.TryGetValue(cellCoord, out var cell)) continue;
            if (cell.State == CellState.Active)
                cell.State = CellState.Queued;
            _bakePending.Add(cellCoord);
        }
    }

    // ── Per-frame update ────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        ResolveCenter();
        if (Center == null) return;
        UpdateCellGrid();
        ProcessBakePending();
        if (DebugDrawEnabled) _debugDraw.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!DebugDrawEnabled) return;
        if (e is not InputEventKey { Pressed: true, Keycode: Key.F6 }) return;

        GD.Print("[NavGridManager] Scanning for isolated cells...");
        _debugBadPaths = [];
        bool anyFound = false;
        foreach (var (coord, cell) in _cells)
        {
            if (cell.State != CellState.Active) continue;
            if (IsCellIsolated(coord))
            {
                GD.Print($"  Isolated: {coord}");
                anyFound = true;
            }
        }
        if (!anyFound) GD.Print("  None found.");
        _debugIsolationDraw.QueueRedraw();
    }

    private void UpdateCellGrid()
    {
        var centerCell = WorldToCell(Center.GlobalPosition);
        if (centerCell == _lastCenterCell) return;
        _lastCenterCell = centerCell;

        // Load any cell within radius not yet tracked.
        for (int dy = -ActiveRadius; dy <= ActiveRadius; dy++)
        for (int dx = -ActiveRadius; dx <= ActiveRadius; dx++)
        {
            var coord = new Vector2I(centerCell.X + dx, centerCell.Y + dy);
            if (_cells.ContainsKey(coord)) continue;

            _cells[coord] = new NavCell { State = CellState.Queued };
            _bakePending.Add(coord);
        }

        // Unload cells outside the radius.
        var toRemove = new List<Vector2I>();
        foreach (var coord in _cells.Keys)
        {
            if (Mathf.Abs(coord.X - centerCell.X) > ActiveRadius ||
                Mathf.Abs(coord.Y - centerCell.Y) > ActiveRadius)
                toRemove.Add(coord);
        }
        foreach (var coord in toRemove)
        {
            _cells[coord].Region?.QueueFree();
            _cells.Remove(coord);
            _bakePending.Remove(coord);
        }
    }

    private void ProcessBakePending()
    {
        if (_baking || _bakePending.Count == 0) return;

        // Pick the pending cell closest to the current center first.
        var centerCell = WorldToCell(Center.GlobalPosition);
        var best      = Vector2I.Zero;
        float bestDist = float.MaxValue;

        foreach (var coord in _bakePending)
        {
            float d = ((Vector2)(coord - centerCell)).LengthSquared();
            if (d < bestDist) { bestDist = d; best = coord; }
        }

        _bakePending.Remove(best);
        if (_cells.TryGetValue(best, out var cell) && cell.State == CellState.Queued)
            BakeCell(best, cell);
    }

    // ── Baking ──────────────────────────────────────────────────────────────────

    private void BakeCell(Vector2I coord, NavCell cell)
    {
        _baking     = true;
        cell.State  = CellState.Baking;

        const float agentRadius = 5f;
        float cellPx = CellSizePixels();
        var origin   = new Vector2(coord.X * cellPx, coord.Y * cellPx);

        // Expand the traversable boundary outward by agentRadius on all sides.
        // BakeFromSourceGeometryData erodes inward by agentRadius, so the eroded
        // navigable area lands exactly on the true cell boundary — no gap between
        // adjacent cells after stitching.
        var e = new Vector2(agentRadius, agentRadius);
        var expandedRect = new []
        {
            origin - e,
            origin + new Vector2(cellPx + agentRadius, -agentRadius),
            origin + new Vector2(cellPx + agentRadius,  cellPx + agentRadius),
            origin + new Vector2(-agentRadius,           cellPx + agentRadius),
        };

        var navPoly    = new NavigationPolygon { AgentRadius = agentRadius };
        var sourceData = new NavigationMeshSourceGeometryData2D();

        sourceData.AddTraversableOutline(expandedRect);

        // Clip obstacles to the expanded rect so nothing is missed in the expansion strip.
        foreach (var blob in TerrainManager.GetBlobPolygons())
            AddClippedObstruction(sourceData, blob, expandedRect);

        // Extra nav obstacles (crystals, towers, etc.) clipped to this cell's rect.
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
                AddClippedObstruction(sourceData, world, expandedRect);
            }
        }

        // Bake on a background thread; apply result on the main thread.
        // capturedRegion is null for first bakes (region not yet added to scene).
        var capturedRegion = cell.Region;
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData, Callable.From(() =>
            Callable.From(() => ApplyCellBake(coord, navPoly, capturedRegion)).CallDeferred()
        ));
    }

    private void ApplyCellBake(Vector2I coord, NavigationPolygon navPoly, NavigationRegion2D? capturedRegion)
    {
        _baking = false;

        if (!_cells.TryGetValue(coord, out var cell)) return;

        if (capturedRegion == null)
        {
            // First bake: create the region with the polygon already set, then add it to
            // the scene. The navigation server sees it for the first time with a valid
            // polygon, so edge-connection evaluation is never done against an empty region.
            if (cell.Region != null) return; // shouldn't happen — bake was not a first bake
            var region = new NavigationRegion2D { NavigationPolygon = navPoly };
            AddChild(region);
            cell.Region = region;
        }
        else
        {
            // Rebake: the cell may have been unloaded/reloaded while in flight.
            if (cell.Region != capturedRegion) return;
            capturedRegion.NavigationPolygon = navPoly;
        }

        // If a terrain update landed while this cell was baking, OnBlobsUpdated will have
        // re-added the coord to _bakePending (but couldn't change state away from Baking).
        // Keep it Queued so ProcessBakePending schedules a fresh rebake with correct data.
        cell.State = _bakePending.Contains(coord) ? CellState.Queued : CellState.Active;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static void AddClippedObstruction(
        NavigationMeshSourceGeometryData2D sourceData,
        Vector2[] poly,
        Vector2[] boundary)
    {
        foreach (var piece in Geometry2D.IntersectPolygons(poly, boundary))
            sourceData.AddObstructionOutline(piece);
    }

    private float     CellSizePixels()                  => CoordHelper.NavCellSizePixels(CoordConfig);
    private Vector2I  WorldToCell(Vector2 worldPos)     => CoordHelper.WorldToNavCell(worldPos, CoordConfig);
    private Vector2I  ChunkToCell(Vector2I chunkCoord)  => CoordHelper.ChunkToNavCell(chunkCoord, CoordConfig);
    
    private bool IsCellIsolated(Vector2I coord)
    {
        if (CenterOfCellHasCollision(coord)) return false;
        float cellPx = CellSizePixels();
        var center = new Vector2((coord.X + 0.5f) * cellPx, (coord.Y + 0.5f) * cellPx);

        // for all cell neighbors
        // try to enter cell center from neighboring cell center if both cell centers are reachable (not in solid terrain)
        foreach (var dir in new[] { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down })
        {
            if (CenterOfCellHasCollision(coord + dir)) continue;
            var neighbour = coord + dir;
            if (!_cells.TryGetValue(neighbour, out var n) || n.State != CellState.Active) continue;

            var nCenter = new Vector2((neighbour.X + 0.5f) * cellPx, (neighbour.Y + 0.5f) * cellPx);
            var map = _cells[coord].Region.GetNavigationMap();
            var path = NavigationServer2D.MapGetPath(map, nCenter, center, false);
            if (path.Length == 0 || !AlmostEqual(path[^1].X, center.X) || !AlmostEqual(path[^1].Y, center.Y))
            {
                GD.PrintErr($"Cell {neighbour} could not enter cell {coord}. Neighbor path end: {path[^1]}. Current center: {center}");
                _debugBadPaths.Add(path);
                return true;
            }
        }
        return false;
    }
    
    private bool CenterOfCellHasCollision(Vector2I cellCoord)
    {
        Vector2 topLeftWorldOfCell = CoordHelper.NavCellToWorld(cellCoord, CoordConfig);
        float halfCellPx = CellSizePixels() * 0.5f;
        Vector2 centerWorldOfCell = new(topLeftWorldOfCell.X + halfCellPx, topLeftWorldOfCell.Y + halfCellPx);
        TerrainType? terrainTypeAtWorldPos = ChunkManager.GetTerrainTypeAtWorldPos(centerWorldOfCell);
        return terrainTypeAtWorldPos.HasValue && terrainTypeAtWorldPos.Value.HasCollision();
    }
    
    public static bool AlmostEqual(float a, float b, float tolerance = 10f)
    {
        return Math.Abs(a - b) < tolerance;
    }
    
    // ── Internal ──────────────────────────────────────────────────────────

    private void ResolveCenter()
    {
        if (!string.IsNullOrEmpty(CenterGroup))
        {
            var nodes = GetTree().GetNodesInGroup(CenterGroup);
            if (nodes.Count > 0)
                Center = nodes[0] as Node2D;
        }
    }

    // ── Debug draw ──────────────────────────────────────────────────────────────

    private partial class DebugCellDraw(NavGridManager manager) : Node2D
    {
        private static readonly Color ColActive  = new(0.20f, 1.00f, 0.30f, 0.12f);
        private static readonly Color ColBaking  = new(1.00f, 0.90f, 0.10f, 0.18f);
        private static readonly Color ColQueued  = new(1.00f, 0.50f, 0.10f, 0.18f);

        public override void _Draw()
        {
            if (!manager.DebugDrawEnabled) return;

            float cellPx = manager.CellSizePixels();

            foreach (var (coord, cell) in manager._cells)
            {
                var origin = new Vector2(coord.X * cellPx, coord.Y * cellPx);
                var size   = Vector2.One * cellPx;

                var fill = cell.State switch
                {
                    CellState.Active => ColActive,
                    CellState.Baking => ColBaking,
                    _                => ColQueued,
                };

                DrawRect(new Rect2(origin, size), fill);
                DrawRect(new Rect2(origin, size), new Color(fill, 0.7f), false, 3f);

                // Coord label at cell center.
                var center = origin + size * 0.5f;
                DrawString(ThemeDB.FallbackFont, center, coord.ToString(),
                    HorizontalAlignment.Center, -1, (int)(cellPx * 0.12f), Colors.White);
            }
        }
    }
    
    private partial class DebugIsolationDraw(NavGridManager manager) : Node2D
    {
        public override void _Draw()
        {
            if (!manager.DebugDrawEnabled || (manager?._debugBadPaths?.Count ?? 0) == 0) return;
            List<Vector2[]> badPaths = manager._debugBadPaths ?? [];
            Vector2 previous = Vector2.Zero;
            foreach (Vector2[] badPath in badPaths)
            {
                if (badPath.Length == 0) continue;
                foreach (Vector2 pathPoint in badPath)
                {
                    if (previous != Vector2.Zero)
                    {
                        // line segment
                        DrawLine(previous, pathPoint, Colors.BlueViolet);
                    }
                    else
                    {
                        // starting point
                        DrawCircle(pathPoint, 10f, Colors.Black);
                    }

                    previous = pathPoint;
                }
                DrawCircle(badPath[^1], 10f, Colors.White);
            }
        }
    }
}
