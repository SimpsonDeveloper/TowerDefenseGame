using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.terrain;

/// <summary>
/// Renders a chunk using a Sprite2D with a generated texture.
/// </summary>
public partial class ChunkRenderer : Node2D
{
    /// <summary>
    /// Size of each tile in pixels (NxN tile with VxV sub-tiles).
    /// </summary>
    private int TilePixelSize => CoordConfig.TilePixelSize;
    /// <summary>
    /// The number of color variants per tile axis.
    /// </summary>
    private int VariationsPerAxis => CoordConfig.SubTileVariationsPerAxis;
    /// <summary>
    /// The calculated size of a sub-tile.
    /// </summary>
    private int SubTileSize => TilePixelSize / VariationsPerAxis;

    private Sprite2D _sprite;
    private Image _image;
    private ImageTexture _texture;
    private ChunkData _chunkData;
    private Dictionary<TerrainType, SimplexGen> _simplexGens;

    public CoordConfig CoordConfig { get; set; }

    /// <summary>
    /// Lookup from terrain type to its SimplexGen for sub-tile noise sampling.
    /// </summary>
    public Dictionary<TerrainType, SimplexGen> SimplexGens
    {
        get => _simplexGens;
        set => _simplexGens = value;
    }

    /// <summary>
    /// The chunk data this renderer is displaying.
    /// </summary>
    public ChunkData ChunkData => _chunkData;

    public override void _Ready()
    {
        _sprite = new Sprite2D();
        _sprite.Centered = false;  // Position from top-left
        _sprite.TextureFilter = TextureFilterEnum.Nearest;  // Pixel-perfect scaling
        _sprite.ZIndex = -1;
        AddChild(_sprite);
    }

    /// <summary>
    /// Initializes the renderer with chunk data and generates the initial texture.
    /// </summary>
    public void Initialize(ChunkData chunkData)
    {
        _chunkData = chunkData;

        // Calculate pixel dimensions
        int pixelWidth = chunkData.Width * TilePixelSize;
        int pixelHeight = chunkData.Height * TilePixelSize;

        // Create image and texture
        _image = Image.CreateEmpty(pixelWidth, pixelHeight, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        _sprite.Texture = _texture;

        // Position the sprite in world space
        Position = CoordHelper.TileToWorld(new Vector2I(chunkData.StartX, chunkData.StartY), CoordConfig);

        // Generate the full texture
        GenerateFullTexture();
    }

    /// <summary>
    /// Generates the texture for all tiles in the chunk.
    /// </summary>
    private void GenerateFullTexture()
    {
        for (int tileX = 0; tileX < _chunkData.Width; tileX++)
        {
            for (int tileY = 0; tileY < _chunkData.Height; tileY++)
            {
                DrawTileToImage(tileX, tileY);
            }
        }

        // Update the texture from the image
        _texture.Update(_image);
    }

    /// <summary>
    /// Draws a single tile to the image at the given tile coordinates.
    /// Each NxN tile contains VxV sub-tiles with color variations based on noise.
    /// </summary>
    private void DrawTileToImage(int tileX, int tileY)
    {
        TileInfo tileInfo = _chunkData.Tiles[tileX, tileY];
        TerrainType terrainType = tileInfo.TerrainType;
        if (!(_simplexGens?.TryGetValue(terrainType, out SimplexGen simplexGen) ?? false))
        {
            throw new Exception("Simplex gen not found");
        }
        
        int variantCount = terrainType.GetVariantCount();

        // Calculate base pixel coordinates for this tile
        int basePixelX = tileX * TilePixelSize;
        int basePixelY = tileY * TilePixelSize;

        var worldTile = new Vector2I(_chunkData.StartX + tileX, _chunkData.StartY + tileY);

        // Draw sub-tiles with color variations from noise
        for (int subTileX = 0; subTileX < VariationsPerAxis; subTileX++)
        {
            for (int subTileY = 0; subTileY < VariationsPerAxis; subTileY++)
            {
                // Each sub-tile gets a unique noise sample position in sub-tile space
                Vector2 subWorld = CoordHelper.TileToSubTile(worldTile, new Vector2I(subTileX, subTileY), CoordConfig);

                // Get variant index from noise at sub-tile position, range driven by color array size
                int variantIndex = simplexGen?.GetVariantIndex(subWorld.X, subWorld.Y, variantCount) ?? 0;
                Color color = terrainType.GetColor(variantIndex);

                // Calculate pixel position for this sub-tile
                int subPixelX = basePixelX + subTileX * SubTileSize;
                int subPixelY = basePixelY + subTileY * SubTileSize;

                // Fill the nxn pixel sub-tile region
                for (int px = 0; px < SubTileSize; px++)
                {
                    for (int py = 0; py < SubTileSize; py++)
                    {
                        _image.SetPixel(subPixelX + px, subPixelY + py, color);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Modifies a tile at the given local tile coordinates.
    /// Updates both the texture and collision.
    /// </summary>
    /// <param name="localTileX">Local tile X coordinate within the chunk</param>
    /// <param name="localTileY">Local tile Y coordinate within the chunk</param>
    /// <param name="newTerrainType">The new terrain type</param>
    public void ModifyTile(int localTileX, int localTileY, TerrainType newTerrainType)
    {
        // Update chunk data
        _chunkData.Tiles[localTileX, localTileY] = new TileInfo(newTerrainType);

        // Update texture for this tile
        DrawTileToImage(localTileX, localTileY);
        _texture.Update(_image);
    }

    public override void _ExitTree()
    {
        _image?.Dispose();
        _texture?.Dispose();
    }
}
