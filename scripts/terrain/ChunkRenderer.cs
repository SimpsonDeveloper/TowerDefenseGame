using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Renders a chunk using a Sprite2D with a generated texture.
/// </summary>
public partial class ChunkRenderer : Node2D
{
    /// <summary>
    /// Size of each tile in pixels (16x16 tile with 4x4 color variations).
    /// </summary>
    public const int TilePixelSize = 16;

    /// <summary>
    /// Size of each color variation sub-tile within the main tile.
    /// A 16x16 tile contains 4x4 sub-tiles of 4x4 pixels each.
    /// </summary>
    public const int SubTileSize = 4;

    /// <summary>
    /// Number of color variations per axis within a tile.
    /// </summary>
    public const int VariationsPerAxis = 4;

    private Sprite2D _sprite;
    private Image _image;
    private ImageTexture _texture;
    private ChunkData _chunkData;
    private Dictionary<TerrainType, SimplexGen> _simplexGens;

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
        Position = new Vector2(
            chunkData.StartX * TilePixelSize,
            chunkData.StartY * TilePixelSize
        );

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
    /// Each 16x16 tile contains 4x4 sub-tiles with color variations based on noise.
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

        // Calculate world tile position for noise sampling
        int worldTileX = _chunkData.StartX + tileX;
        int worldTileY = _chunkData.StartY + tileY;

        // Draw 4x4 sub-tiles with color variations from noise
        for (int subTileX = 0; subTileX < VariationsPerAxis; subTileX++)
        {
            for (int subTileY = 0; subTileY < VariationsPerAxis; subTileY++)
            {
                // Calculate world position for this sub-tile (scale to sub-tile coordinates)
                // Each sub-tile needs a unique noise sample position
                float subWorldX = worldTileX * VariationsPerAxis + subTileX;
                float subWorldY = worldTileY * VariationsPerAxis + subTileY;

                // Get variant index from noise at sub-tile position, range driven by color array size
                int variantIndex = simplexGen?.GetVariantIndex(subWorldX, subWorldY, variantCount) ?? 0;
                Color color = terrainType.GetColor(variantIndex);

                // Calculate pixel position for this sub-tile
                int subPixelX = basePixelX + subTileX * SubTileSize;
                int subPixelY = basePixelY + subTileY * SubTileSize;

                // Fill the 4x4 pixel sub-tile region
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
    /// <param name="variantIndex">The color variant (0-3), defaults to 0</param>
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
