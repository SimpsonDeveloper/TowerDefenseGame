using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Stateless snap math for tower placement.
///
/// Alignment rule per axis:
///   tiles = pixelSize / TilePixelSize   (integer division)
///   odd  tiles → snap center to middle of a tile
///   even tiles → snap center to tile boundary
/// </summary>
public static class TowerSnapHelper
{
    /// <summary>
    /// Snaps one world-space coordinate so the tower center lands on the
    /// correct sub-tile position for the given pixel extent on that axis.
    /// </summary>
    public static float SnapAxis(float worldPos, int pixelSize, int tilePixelSize)
    {
        int tiles = pixelSize / tilePixelSize;
        if (tiles % 2 == 1)
        {
            // Odd tile count → center aligns with the middle of a tile.
            float col = Mathf.Floor(worldPos / tilePixelSize);
            return col * tilePixelSize + tilePixelSize * 0.5f;
        }
        else
        {
            // Even tile count → center aligns with a tile boundary.
            return Mathf.Round(worldPos / tilePixelSize) * tilePixelSize;
        }
    }

    /// <summary>
    /// Returns the snapped world-space center position for a tower of the
    /// given pixel size, given a raw mouse world position.
    /// </summary>
    public static Vector2 SnapCenter(Vector2 mouseWorld, Vector2I sizePixels, CoordConfig cfg)
    {
        return new Vector2(
            SnapAxis(mouseWorld.X, sizePixels.X, cfg.TilePixelSize),
            SnapAxis(mouseWorld.Y, sizePixels.Y, cfg.TilePixelSize));
    }

    /// <summary>
    /// Enumerates every tile coordinate covered by the tower footprint when
    /// its center is at <paramref name="snappedCenter"/>.
    /// </summary>
    public static IEnumerable<Vector2I> FootprintTiles(Vector2 snappedCenter, Vector2I sizePixels, CoordConfig cfg)
    {
        int tilesX = sizePixels.X / cfg.TilePixelSize;
        int tilesY = sizePixels.Y / cfg.TilePixelSize;

        // Top-left world pixel of the footprint.
        Vector2 topLeft = snappedCenter - new Vector2(sizePixels.X * 0.5f, sizePixels.Y * 0.5f);
        Vector2I topLeftTile = CoordHelper.WorldToTile(topLeft, cfg);

        for (int dy = 0; dy < tilesY; dy++)
        for (int dx = 0; dx < tilesX; dx++)
            yield return new Vector2I(topLeftTile.X + dx, topLeftTile.Y + dy);
    }
}
