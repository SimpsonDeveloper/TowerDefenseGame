using Godot;
using System.Collections.Generic;

namespace towerdefensegame;

/// <summary>
/// Maintains an AStarGrid2D built from live terrain data and serves path requests
/// to EnemyAStarController instances.
///
/// Add this node to each world scene (Overworld, Pocket Dimension) and wire the
/// ChunkManager export. Enemies call RequestPath() — the manager handles grid
/// updates as new chunks load.
///
/// See docs/pathfinding_astar.md for full setup instructions.
/// </summary>
[GlobalClass]
public partial class AStarGridManager : Node
{
    // ── Configuration ─────────────────────────────────────────────────────

    /// <summary>The ChunkManager for this world (used to query terrain type).</summary>
    [Export] public ChunkManager ChunkManager { get; set; }

    /// <summary>
    /// Tile size in pixels. Must match the TileMap tile size (default 16×16).
    /// </summary>
    [Export] public int TileSize { get; set; } = 16;

    /// <summary>
    /// Half-extent of the active grid in tiles, measured from the world origin.
    /// The full grid is (2*GridRadius) × (2*GridRadius) tiles.
    /// Increase if enemies need to path-find far from the origin.
    /// </summary>
    [Export] public int GridRadius { get; set; } = 160; // 160*2 = 320 tiles = 5120px

    // ── Internal state ────────────────────────────────────────────────────

    private AStarGrid2D _grid;
    private HashSet<Vector2I> _dirtyChunks = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _grid = new AStarGrid2D();
        _grid.Region = new Rect2I(-GridRadius, -GridRadius, GridRadius * 2, GridRadius * 2);
        _grid.CellSize = new Vector2(TileSize, TileSize);
        _grid.DefaultComputeHeuristic = AStarGrid2D.Heuristic.Euclidean;
        _grid.DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles;
        _grid.Update();

        // Do an initial pass on any terrain already generated
        RefreshFullGrid();
    }

    public override void _Process(double delta)
    {
        // Apply any dirty chunks that loaded since last frame
        if (_dirtyChunks.Count > 0)
        {
            foreach (var chunk in _dirtyChunks)
                RefreshChunk(chunk);
            _dirtyChunks.Clear();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a world-space path from <paramref name="from"/> to
    /// <paramref name="to"/>, or an empty array if no path exists.
    /// </summary>
    public Vector2[] RequestPath(Vector2 from, Vector2 to)
    {
        Vector2I fromTile = WorldToTile(from);
        Vector2I toTile   = WorldToTile(to);

        if (!_grid.Region.HasPoint(fromTile) || !_grid.Region.HasPoint(toTile))
            return System.Array.Empty<Vector2>();

        // AStarGrid2D returns tile coords; convert back to world-space centres
        var tilePath = _grid.GetPointPath(fromTile, toTile);
        var worldPath = new Vector2[tilePath.Length];
        for (int i = 0; i < tilePath.Length; i++)
            worldPath[i] = TileToWorld(new Vector2I((int)tilePath[i].X, (int)tilePath[i].Y));
        return worldPath;
    }

    /// <summary>
    /// Call this when a new chunk finishes loading so the grid stays in sync.
    /// Pass the chunk coordinate (not the world position).
    /// </summary>
    public void MarkChunkDirty(Vector2I chunkCoord) => _dirtyChunks.Add(chunkCoord);

    // ── Internal ──────────────────────────────────────────────────────────

    private void RefreshFullGrid()
    {
        if (ChunkManager == null) return;
        int chunkTiles = ChunkManager.ChunkSize;

        for (int tx = -GridRadius; tx < GridRadius; tx++)
        for (int ty = -GridRadius; ty < GridRadius; ty++)
            UpdateTile(new Vector2I(tx, ty));
    }

    private void RefreshChunk(Vector2I chunkCoord)
    {
        if (ChunkManager == null) return;
        int chunkTiles = ChunkManager.ChunkSize;
        int startX = chunkCoord.X * chunkTiles;
        int startY = chunkCoord.Y * chunkTiles;

        for (int tx = startX; tx < startX + chunkTiles; tx++)
        for (int ty = startY; ty < startY + chunkTiles; ty++)
        {
            var tile = new Vector2I(tx, ty);
            if (_grid.Region.HasPoint(tile))
                UpdateTile(tile);
        }
    }

    private void UpdateTile(Vector2I tileCoord)
    {
        Vector2 worldPos = TileToWorld(tileCoord);
        TerrainType? terrain = ChunkManager.GetTerrainTypeAtWorldPos(worldPos);
        bool solid = terrain.HasValue && TerrainTypeExtensions.HasCollision(terrain.Value);
        _grid.SetPointSolid(tileCoord, solid);
    }

    private Vector2I WorldToTile(Vector2 worldPos) =>
        new Vector2I(
            Mathf.FloorToInt(worldPos.X / TileSize),
            Mathf.FloorToInt(worldPos.Y / TileSize)
        );

    private Vector2 TileToWorld(Vector2I tile) =>
        new Vector2(tile.X * TileSize + TileSize * 0.5f, tile.Y * TileSize + TileSize * 0.5f);
}
