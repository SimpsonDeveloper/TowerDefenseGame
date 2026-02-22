using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Defines the types of terrain in the game.
/// </summary>
public enum TerrainType
{
    Water = 0,
    Grass = 1,
    Sand = 2,
    Rock = 3
}

public class TerrainTypeData(Color[] colors, bool hasCollision)
{
    public readonly Color[] Colors = colors;
    public readonly bool HasCollision = hasCollision;
}

/// <summary>
/// Extension methods for TerrainType.
/// </summary>
public static class TerrainTypeExtensions
{
    private static readonly Dictionary<TerrainType, TerrainTypeData> TerrainData = new()
    {
        {
            TerrainType.Water,
            new TerrainTypeData
            (
                [ new Color("#0068d8"), new Color("#0064d1"), new Color("#0060c9"), new Color("#005cc1") ], 
                true
            )
        },
        {
            TerrainType.Grass,
            new TerrainTypeData
            (
                [ new Color("#00b756"), new Color("#00af53"), new Color("#00a850"), new Color("#00a04b") ],
                false
            )
        },
        {
            TerrainType.Sand,
            new TerrainTypeData
            (
                [ new Color("#ffcc4a"), new Color("#f7c546"), new Color("#efbd42"), new Color("#e8b941") ],
                false
            )
        },
        {
            TerrainType.Rock,
            new TerrainTypeData
            (
                [ new Color("#916800"), new Color("#896200"), new Color("#825d00"), new Color("#7a5700") ],
                false
            )
        }
    };
    
    /// <summary>
    /// Gets the number of color variants defined for a terrain type.
    /// </summary>
    public static int GetVariantCount(this TerrainType type)
    {
        Color[] colors = GetColors(type);
        return colors.Length;
    }

    /// <summary>
    /// Gets the color for a terrain type and variant.
    /// </summary>
    public static Color GetColor(this TerrainType type, int variantIndex = 0)
    {
        Color[] colors = GetColors(type);
        return colors[variantIndex];
    }

    /// <summary>
    /// Returns whether this terrain type has collision.
    /// </summary>
    public static bool HasCollision(this TerrainType type)
    {
        return GetTerrainTypeData(type).HasCollision;
    }

    private static Color[] GetColors(TerrainType type)
    {
        TerrainTypeData terrainData = GetTerrainTypeData(type);
        if (terrainData.Colors == null)
            throw new InvalidOperationException($"Terrain data for '{type}' has invalid or missing color data.");
        return terrainData.Colors;
    }
    
    private static TerrainTypeData GetTerrainTypeData(TerrainType type)
    {
        if (!TerrainData.TryGetValue(type, out var terrainData))
            throw new ArgumentException($"Terrain type '{type}' is not supported.", nameof(type));
        return terrainData;
    }
}
