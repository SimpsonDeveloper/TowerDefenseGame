using Godot;

namespace towerdefensegame;

/// <summary>
/// Holds the generated tile data for a chunk.
/// This data is generated on a background thread and then applied on the main thread.
/// </summary>
public class ChunkData
{
    /// <summary>
    /// The chunk coordinate (not tile coordinate).
    /// </summary>
    public Vector2I ChunkCoord { get; set; }

    /// <summary>
    /// Starting tile X coordinate.
    /// </summary>
    public int StartX { get; set; }

    /// <summary>
    /// Starting tile Y coordinate.
    /// </summary>
    public int StartY { get; set; }

    /// <summary>
    /// Width of the chunk in tiles.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height of the chunk in tiles.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Tile data array. Each entry contains the SimplexGen index
    /// used to look up terrain type and color in ChunkRenderer.
    /// </summary>
    public TileInfo[,] Tiles { get; set; }

    public ChunkData(Vector2I chunkCoord, int startX, int startY, int width, int height)
    {
        ChunkCoord = chunkCoord;
        StartX = startX;
        StartY = startY;
        Width = width;
        Height = height;
        Tiles = new TileInfo[width, height];
    }
}

/// <summary>
/// Information about a single tile to be rendered.
/// </summary>
public struct TileInfo
{
    /// <summary>
    /// Index into SimplexGens array (determines terrain type and color).
    /// </summary>
    public int SimplexGenIndex;

    public TileInfo(int simplexGenIndex)
    {
        SimplexGenIndex = simplexGenIndex;
    }
}
