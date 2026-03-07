using Godot;
using towerdefensegame;

/// <summary>
/// Finds valid (non-colliding) spawn positions by scanning terrain tile data outward
/// from a desired origin. Returns null if no valid tile is found yet, which usually
/// means the surrounding chunks haven't loaded — call again next frame.
/// </summary>
public static class SpawnHelper
{
    /// <summary>
    /// Scans outward ring-by-ring from <paramref name="desiredWorldPos"/> and returns
    /// the center of the nearest loaded tile where a clear area of at least
    /// <paramref name="minClearance"/> tiles in every direction is guaranteed.
    ///
    /// Returns null when either:
    ///   - The surrounding chunks aren't loaded yet (retry next frame), or
    ///   - No sufficiently open tile was found within <paramref name="maxRadius"/> tiles.
    /// </summary>
    /// <param name="chunkManager">Source of terrain type data.</param>
    /// <param name="desiredWorldPos">Preferred spawn point in world pixels.</param>
    /// <param name="maxRadius">How many tiles outward to search before giving up.</param>
    /// <param name="minClearance">
    /// Minimum tile radius of open space required around the candidate tile.
    /// 0 = only the tile itself must be clear.
    /// 2 = a 5x5 area around the tile must all be clear (good default for breathing room).
    /// </param>
    public static Vector2? FindValidSpawnPosition(
        ChunkManager chunkManager,
        Vector2 desiredWorldPos,
        int maxRadius = 20,
        int minClearance = 2)
    {
        int tileSize = ChunkRenderer.TilePixelSize;
        int originTileX = Mathf.FloorToInt(desiredWorldPos.X / tileSize);
        int originTileY = Mathf.FloorToInt(desiredWorldPos.Y / tileSize);

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int tx = originTileX - radius; tx <= originTileX + radius; tx++)
            {
                for (int ty = originTileY - radius; ty <= originTileY + radius; ty++)
                {
                    // Skip tiles in already-checked inner rings.
                    if (radius > 0
                        && Mathf.Abs(tx - originTileX) < radius
                        && Mathf.Abs(ty - originTileY) < radius)
                        continue;

                    if (HasClearance(chunkManager, tx, ty, tileSize, minClearance))
                        return new Vector2((tx + 0.5f) * tileSize, (ty + 0.5f) * tileSize);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true only if every tile within <paramref name="clearanceRadius"/> of
    /// (<paramref name="centerTileX"/>, <paramref name="centerTileY"/>) is loaded
    /// and has no collision.
    /// </summary>
    private static bool HasClearance(
        ChunkManager chunkManager,
        int centerTileX, int centerTileY,
        int tileSize, int clearanceRadius)
    {
        for (int dx = -clearanceRadius; dx <= clearanceRadius; dx++)
        {
            for (int dy = -clearanceRadius; dy <= clearanceRadius; dy++)
            {
                Vector2 tileCenter = new Vector2(
                    (centerTileX + dx + 0.5f) * tileSize,
                    (centerTileY + dy + 0.5f) * tileSize);

                TerrainType? type = chunkManager.GetTerrainTypeAtWorldPos(tileCenter);

                // Unloaded or colliding — this candidate fails.
                if (!type.HasValue || type.Value.HasCollision())
                    return false;
            }
        }
        return true;
    }
}
