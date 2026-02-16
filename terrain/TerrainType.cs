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
/// Manages terrain colors - can be loaded from textures or hard-coded.
/// </summary>
public static class TerrainColors
{
    private const int TileSize = 4;
    private const int VariantsPerTerrain = 4;
    private const int AtlasWidth = 2;  // 2x2 atlas

    // Color storage: [terrainType][variantIndex]
    private static Color[][] _colors;
    private static bool _initialized = false;

    // Hard-coded colors (stub - fill these in after sampling from textures)
    // Set UseHardCodedColors to true once you've filled in the values
    private static readonly bool UseHardCodedColors = true;
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

    // Texture paths for each terrain type
    private static readonly string[] TexturePaths = new string[]
    {
        "res://assets/4pxTilesWater.png",
        "res://assets/4pxTilesGrass.png",
        "res://assets/4pxTilesSand.png",
        "res://assets/4pxTilesRock.png",
    };

    /// <summary>
    /// Initializes terrain colors by sampling from textures.
    /// Call this once at game startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        if (UseHardCodedColors)
        {
            _colors = HardCodedColors;
            _initialized = true;
            GD.Print("[TerrainColors] Using hard-coded colors");
            return;
        }

        _colors = new Color[TexturePaths.Length][];

        for (int terrainIndex = 0; terrainIndex < TexturePaths.Length; terrainIndex++)
        {
            _colors[terrainIndex] = new Color[VariantsPerTerrain];
            
            string path = TexturePaths[terrainIndex];
            var texture = GD.Load<Texture2D>(path);
            
            if (texture == null)
            {
                GD.PrintErr($"[TerrainColors] Failed to load texture: {path}");
                // Fill with magenta as error color
                for (int i = 0; i < VariantsPerTerrain; i++)
                    _colors[terrainIndex][i] = new Color(1f, 0f, 1f);
                continue;
            }

            var image = texture.GetImage();
            
            // Sample center pixel of each 4x4 tile in the 2x2 atlas
            for (int variantIndex = 0; variantIndex < VariantsPerTerrain; variantIndex++)
            {
                int atlasX = variantIndex % AtlasWidth;
                int atlasY = variantIndex / AtlasWidth;
                
                // Get center pixel of the tile (offset by 1 to get center of 4x4)
                int pixelX = atlasX * TileSize + TileSize / 2;
                int pixelY = atlasY * TileSize + TileSize / 2;
                
                Color color = image.GetPixel(pixelX, pixelY);
                _colors[terrainIndex][variantIndex] = color;
            }

            // Log the colors for this terrain type
            string terrainName = ((TerrainType)terrainIndex).ToString();
            GD.Print($"[TerrainColors] {terrainName} colors:");
            for (int i = 0; i < VariantsPerTerrain; i++)
            {
                Color c = _colors[terrainIndex][i];
                string hex = c.ToHtml(false);
                GD.Print($"  Variant {i}: #{hex}");
            }
        }

        GD.Print("[TerrainColors] Initialization complete. Copy the hex values above to HardCodedColors if desired.");
        _initialized = true;
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

        variantIndex = Mathf.Clamp(variantIndex, 0, VariantsPerTerrain - 1);
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
