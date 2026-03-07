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
    /// the center of the nearest loaded tile that has no collision.
    ///
    /// Returns null when either:
    ///   - The surrounding chunks aren't loaded yet (retry next frame), or
    ///   - No clear tile was found within <paramref name="maxRadius"/> tiles.
    /// </summary>
    /// <param name="chunkManager">Source of terrain type data.</param>
    /// <param name="desiredWorldPos">Preferred spawn point in world pixels.</param>
    /// <param name="maxRadius">How many tiles outward to search before giving up.</param>
    public static Vector2? FindValidSpawnPosition(
        ChunkManager chunkManager,
        Vector2 desiredWorldPos,
        int maxRadius = 20)
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

                    // Sample the center of the tile so FloorToInt maps back to the same cell.
                    Vector2 tileCenter = new Vector2((tx + 0.5f) * tileSize, (ty + 0.5f) * tileSize);
                    TerrainType? type = chunkManager.GetTerrainTypeAtWorldPos(tileCenter);

                    // null → chunk not loaded yet, keep searching other tiles this ring.
                    if (type.HasValue && !type.Value.HasCollision())
                        return tileCenter;
                }
            }
        }

        return null;
    }
}
