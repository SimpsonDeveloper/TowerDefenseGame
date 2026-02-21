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
/// Manages terrain colors using hard-coded hex values.
/// </summary>
public static class TerrainColors
{
    private const int TileSize = 16;

    // Color storage: [terrainType][variantIndex]
    private static Color[][] _colors;
    private static bool _initialized = false;

    // Each terrain has 4 color variants
    private static readonly Color[][] HardCodedColors = new Color[][]
    {
        // Water variants (index 0)
        new Color[] { new Color("#0068d8"), new Color("#0064d1"), new Color("#0060c9"), new Color("#005cc1") },
        // Grass variants (index 1)
        new Color[] { new Color("#00b756"), new Color("#00af53"), new Color("#00a850"), new Color("#00a04b") },
        // Sand variants (index 2)
        new Color[] { new Color("#ffcc4a"), new Color("#f7c546"), new Color("#efbd42"), new Color("#e8b941") },
        // Rock variants (index 3)
        new Color[] { new Color("#916800"), new Color("#896200"), new Color("#825d00"), new Color("#7a5700") },
    };

    /// <summary>
    /// Initializes terrain colors from hard-coded hex values.
    /// Call this once at game startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _colors = HardCodedColors;
        _initialized = true;
        GD.Print("[TerrainColors] Using hard-coded colors");
    }

    /// <summary>
    /// Gets the color for a terrain type and variant.
    /// </summary>
    public static Color GetColor(TerrainType type, int variantIndex = 0)
    {
        if (!_initialized)
        {
            GD.PrintErr("[TerrainColors] Not initialized! Call Initialize() first.");
            return new Color(1f, 0f, 1f);
        }

        int typeIndex = (int)type;
        if (typeIndex < 0 || typeIndex >= _colors.Length)
            return new Color(1f, 0f, 1f);

        variantIndex = Mathf.Clamp(variantIndex, 0, _colors[typeIndex].Length - 1);
        return _colors[typeIndex][variantIndex];
    }
}

/// <summary>
/// Extension methods for TerrainType.
/// </summary>
public static class TerrainTypeExtensions
{
    /// <summary>
    /// Gets the color for a terrain type and variant.
    /// </summary>
    public static Color GetColor(this TerrainType type, int variantIndex = 0)
    {
        return TerrainColors.GetColor(type, variantIndex);
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
