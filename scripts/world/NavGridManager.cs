using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Maintains a sparse grid of NavigationRegion2D cells around a moving centre node.
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

    /// <summary>Node whose position drives cell loading (typically the player).</summary>
    [Export] public Node2D Center { get; set; }

    /// <summary>
    /// Cell size in chunks (must be >= 1).
    /// Pixel size = CellSizeChunks × ChunkManager.ChunkSize × TilePixelSize.
    /// </summary>
    [Export] public int CellSizeChunks { get; set; } = 1;

    /// <summary>
    /// Chebyshev radius in cells around the centre cell to keep loaded and baked.
    /// Radius 2 keeps a 5×5 grid (25 cells) active.
    /// </summary>
    [Export] public int ActiveRadius { get; set; } = 2;

    // ── Internal types ──────────────────────────────────────────────────────────

    private enum CellState { Queued, Baking, Active }

    private sealed class NavCell(NavigationRegion2D region)
    {
        public readonly NavigationRegion2D Region = region;
        public CellState State;
    }

    // ── State ───────────────────────────────────────────────────────────────────

    private readonly Dictionary<Vector2I, NavCell> _cells      = new();
    private readonly HashSet<Vector2I>             _bakePending = new();
    private bool    _baking;
    private Vector2I _lastCentreCell = new(int.MinValue, int.MinValue);

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (TerrainManager == null) { GD.PushWarning($"{Name}: TerrainManager not assigned."); return; }
        if (ChunkManager   == null) { GD.PushWarning($"{Name}: ChunkManager not assigned.");   return; }
        if (Center         == null) { GD.PushWarning($"{Name}: Center not assigned.");          return; }

        if (CellSizeChunks < 1)
        {
            GD.PushWarning($"{Name}: CellSizeChunks must be >= 1; clamping to 1.");
            CellSizeChunks = 1;
        }

        TerrainManager.BlobsUpdated += OnBlobsUpdated;
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
                cell.Region.QueueFree();
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
        UpdateCellGrid();
        ProcessBakePending();
    }

    private void UpdateCellGrid()
    {
        var centreCell = WorldToCell(Center.GlobalPosition);
        if (centreCell == _lastCentreCell) return;
        _lastCentreCell = centreCell;

        // Load any cell within radius not yet tracked.
        for (int dy = -ActiveRadius; dy <= ActiveRadius; dy++)
        for (int dx = -ActiveRadius; dx <= ActiveRadius; dx++)
        {
            var coord = new Vector2I(centreCell.X + dx, centreCell.Y + dy);
            if (_cells.ContainsKey(coord)) continue;

            var region = new NavigationRegion2D();
            AddChild(region);
            _cells[coord] = new NavCell(region) { State = CellState.Queued };
            _bakePending.Add(coord);
        }

        // Unload cells outside the radius.
        var toRemove = new List<Vector2I>();
        foreach (var coord in _cells.Keys)
        {
            if (Mathf.Abs(coord.X - centreCell.X) > ActiveRadius ||
                Mathf.Abs(coord.Y - centreCell.Y) > ActiveRadius)
                toRemove.Add(coord);
        }
        foreach (var coord in toRemove)
        {
            _cells[coord].Region.QueueFree();
            _cells.Remove(coord);
            _bakePending.Remove(coord);
        }
    }

    private void ProcessBakePending()
    {
        if (_baking || _bakePending.Count == 0) return;

        // Pick the pending cell closest to the current centre first.
        var centreCell = WorldToCell(Center.GlobalPosition);
        var best      = Vector2I.Zero;
        float bestDist = float.MaxValue;

        foreach (var coord in _bakePending)
        {
            float d = ((Vector2)(coord - centreCell)).LengthSquared();
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

        float cellPx = CellSizePixels();
        var origin   = new Vector2(coord.X * cellPx, coord.Y * cellPx);
        var cellRect = new Vector2[]
        {
            origin,
            origin + new Vector2(cellPx, 0),
            origin + new Vector2(cellPx, cellPx),
            origin + new Vector2(0,      cellPx),
        };

        var navPoly    = new NavigationPolygon { AgentRadius = 9f };
        var sourceData = new NavigationMeshSourceGeometryData2D();

        sourceData.AddTraversableOutline(cellRect);

        // Terrain blob obstructions clipped to this cell's rect.
        foreach (var blob in TerrainManager.GetBlobPolygons())
            AddClippedObstruction(sourceData, blob, cellRect);

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
                AddClippedObstruction(sourceData, world, cellRect);
            }
        }

        // Bake on a background thread; apply result on the main thread.
        var region = cell.Region;
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData, Callable.From(() =>
            Callable.From(() => ApplyCellBake(coord, navPoly, region)).CallDeferred()
        ));
    }

    private void ApplyCellBake(Vector2I coord, NavigationPolygon navPoly, NavigationRegion2D region)
    {
        _baking = false;

        // The cell may have been unloaded while the bake was in flight.
        if (!_cells.TryGetValue(coord, out var cell) || cell.Region != region)
            return;

        region.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(region.GetRid(), navPoly);
        cell.State = CellState.Active;
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

    private float CellSizePixels() =>
        CellSizeChunks * ChunkManager.ChunkSize * (float)ChunkRenderer.TilePixelSize;

    private Vector2I WorldToCell(Vector2 worldPos)
    {
        float cellPx = CellSizePixels();
        return new Vector2I(
            Mathf.FloorToInt(worldPos.X / cellPx),
            Mathf.FloorToInt(worldPos.Y / cellPx));
    }

    private Vector2I ChunkToCell(Vector2I chunkCoord) =>
        new(Mathf.FloorToInt((float)chunkCoord.X / CellSizeChunks),
            Mathf.FloorToInt((float)chunkCoord.Y / CellSizeChunks));
}
