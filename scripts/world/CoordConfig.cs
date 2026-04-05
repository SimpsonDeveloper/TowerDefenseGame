using Godot;

namespace towerdefensegame;

/// <summary>
/// Single source of truth for every coordinate-space size constant in the game.
/// Export this resource on any node that converts between world, tile, chunk,
/// nav-cell, or sub-tile coordinates, rather than exporting the sizes individually.
///
/// Coordinate spaces:
///   World    — Vector2  (pixels, float)
///   Tile     — Vector2I (1 tile  = TilePixelSize px)
///   Chunk    — Vector2I (1 chunk = ChunkSizeTiles tiles)
///   Nav cell — Vector2I (1 cell  = NavCellSizeChunks chunks)
///   Sub-tile — float pair (1 tile = SubTileVariationsPerAxis² noise samples)
/// </summary>
[GlobalClass]
public partial class CoordConfig : Resource
{
    /// <summary>Pixel side-length of one tile. Must match ChunkRenderer.TilePixelSize.</summary>
    [Export] public int TilePixelSize { get; set; } = 16;

    /// <summary>Side-length of one chunk in tiles (NxN). Replaces ChunkManager.ChunkSize.</summary>
    [Export] public int ChunkSizeTiles { get; set; } = 32;

    /// <summary>Side-length of one nav cell in chunks (NxN). Replaces NavGridManager.CellSizeChunks.</summary>
    [Export] public int NavCellSizeChunks { get; set; } = 1;

    /// <summary>
    /// Color-variation sub-tile divisions per tile axis.
    /// Must match ChunkRenderer.VariationsPerAxis.
    /// A tile contains (SubTileVariationsPerAxis × SubTileVariationsPerAxis) noise samples.
    /// </summary>
    [Export] public int SubTileVariationsPerAxis { get; set; } = 4;
}
