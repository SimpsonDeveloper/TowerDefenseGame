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

/// <summary>
/// Utility class for terrain type operations.
/// </summary>
public static class TerrainTypeExtensions
{
    /// <summary>
    /// Gets the base color for a terrain type.
    /// </summary>
    public static Color GetColor(this TerrainType type)
    {
        return type switch
        {
            TerrainType.Water => new Color(0.2f, 0.4f, 0.8f),  // Blue
            TerrainType.Grass => new Color(0.3f, 0.7f, 0.3f),  // Green
            TerrainType.Sand => new Color(0.9f, 0.85f, 0.6f),  // Tan
            TerrainType.Rock => new Color(0.5f, 0.5f, 0.5f),   // Gray
            _ => new Color(1f, 0f, 1f)  // Magenta for unknown
        };
    }

    /// <summary>
    /// Gets a color variation based on noise value for visual variety.
    /// </summary>
    public static Color GetColorVariation(this TerrainType type, float noiseValue)
    {
        Color baseColor = type.GetColor();
        
        // Apply subtle variation based on noise (-0.1 to +0.1 brightness)
        float variation = noiseValue * 0.1f;
        
        return new Color(
            Mathf.Clamp(baseColor.R + variation, 0f, 1f),
            Mathf.Clamp(baseColor.G + variation, 0f, 1f),
            Mathf.Clamp(baseColor.B + variation, 0f, 1f)
        );
    }

    /// <summary>
    /// Returns whether this terrain type has collision.
    /// </summary>
    public static bool HasCollision(this TerrainType type)
    {
        return type switch
        {
            TerrainType.Water => true,
            TerrainType.Rock => true,
            _ => false
        };
    }
}
