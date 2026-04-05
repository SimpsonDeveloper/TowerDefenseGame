using Godot;

namespace towerdefensegame;

/// <summary>
/// Static conversion helpers between every coordinate space in the game.
/// All methods take a CoordConfig so the size constants come from one place.
///
/// Spaces (all use top-left / floor conventions):
///   World    — Vector2  pixels
///   Tile     — Vector2I, 1 unit = TilePixelSize px
///   Chunk    — Vector2I, 1 unit = ChunkSizeTiles tiles
///   Nav cell — Vector2I, 1 unit = NavCellSizeChunks chunks
///   Sub-tile — Vector2  noise-sample coordinate,
///              1 tile = SubTileVariationsPerAxis units on each axis
/// </summary>
public static class CoordHelper
{
    // ── Derived pixel sizes ─────────────────────────────────────────────────────

    public static int   ChunkSizePixels  (CoordConfig cfg) => cfg.ChunkSizeTiles   * cfg.TilePixelSize;
    public static float NavCellSizePixels(CoordConfig cfg) => cfg.NavCellSizeChunks * cfg.ChunkSizeTiles * cfg.TilePixelSize;

    // ── World ↔ Tile ────────────────────────────────────────────────────────────

    public static Vector2I WorldToTile(Vector2 world, CoordConfig cfg) => new(
        Mathf.FloorToInt(world.X / cfg.TilePixelSize),
        Mathf.FloorToInt(world.Y / cfg.TilePixelSize));

    /// <summary>Returns the top-left world pixel of the tile.</summary>
    public static Vector2 TileToWorld(Vector2I tile, CoordConfig cfg) => new(
        tile.X * cfg.TilePixelSize,
        tile.Y * cfg.TilePixelSize);

    // ── World ↔ Chunk ───────────────────────────────────────────────────────────

    public static Vector2I WorldToChunk(Vector2 world, CoordConfig cfg)
    {
        int chunkPx = ChunkSizePixels(cfg);
        return new(
            Mathf.FloorToInt(world.X / chunkPx),
            Mathf.FloorToInt(world.Y / chunkPx));
    }

    /// <summary>Returns the top-left world pixel of the chunk.</summary>
    public static Vector2 ChunkToWorld(Vector2I chunk, CoordConfig cfg)
    {
        int chunkPx = ChunkSizePixels(cfg);
        return new(chunk.X * chunkPx, chunk.Y * chunkPx);
    }

    // ── Tile ↔ Chunk ────────────────────────────────────────────────────────────

    public static Vector2I TileToChunk(Vector2I tile, CoordConfig cfg) => new(
        Mathf.FloorToInt((float)tile.X / cfg.ChunkSizeTiles),
        Mathf.FloorToInt((float)tile.Y / cfg.ChunkSizeTiles));

    /// <summary>Returns the first (top-left) tile coordinate of the chunk.</summary>
    public static Vector2I ChunkToFirstTile(Vector2I chunk, CoordConfig cfg) => new(
        chunk.X * cfg.ChunkSizeTiles,
        chunk.Y * cfg.ChunkSizeTiles);

    // ── World ↔ Nav cell ────────────────────────────────────────────────────────

    public static Vector2I WorldToNavCell(Vector2 world, CoordConfig cfg)
    {
        float cellPx = NavCellSizePixels(cfg);
        return new(
            Mathf.FloorToInt(world.X / cellPx),
            Mathf.FloorToInt(world.Y / cellPx));
    }

    /// <summary>Returns the top-left world pixel of the nav cell.</summary>
    public static Vector2 NavCellToWorld(Vector2I cell, CoordConfig cfg)
    {
        float cellPx = NavCellSizePixels(cfg);
        return new(cell.X * cellPx, cell.Y * cellPx);
    }

    // ── Chunk ↔ Nav cell ────────────────────────────────────────────────────────

    public static Vector2I ChunkToNavCell(Vector2I chunk, CoordConfig cfg) => new(
        Mathf.FloorToInt((float)chunk.X / cfg.NavCellSizeChunks),
        Mathf.FloorToInt((float)chunk.Y / cfg.NavCellSizeChunks));

    /// <summary>Returns the first (top-left) chunk coordinate of the nav cell.</summary>
    public static Vector2I NavCellToFirstChunk(Vector2I cell, CoordConfig cfg) => new(
        cell.X * cfg.NavCellSizeChunks,
        cell.Y * cfg.NavCellSizeChunks);

    // ── Tile → Sub-tile ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the noise-sample coordinate for a sub-tile within a world tile.
    /// subOffset components must be in [0, SubTileVariationsPerAxis).
    /// This is the coordinate passed to SimplexGen.GetVariantIndex.
    /// </summary>
    public static Vector2 TileToSubTile(Vector2I worldTile, Vector2I subOffset, CoordConfig cfg) => new(
        worldTile.X * cfg.SubTileVariationsPerAxis + subOffset.X,
        worldTile.Y * cfg.SubTileVariationsPerAxis + subOffset.Y);
}
