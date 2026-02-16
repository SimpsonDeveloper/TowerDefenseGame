using Godot;

namespace towerdefensegame;

/// <summary>
/// Renders a chunk using a Sprite2D with a generated texture.
/// Manages both visual rendering and collision tile placement.
/// </summary>
public partial class ChunkRenderer : Node2D
{
    /// <summary>
    /// Size of each tile in pixels.
    /// </summary>
    public const int TilePixelSize = 4;

    private Sprite2D _sprite;
    private Image _image;
    private ImageTexture _texture;
    private ChunkData _chunkData;

    /// <summary>
    /// Reference to the collision TileMapLayer for placing collision tiles.
    /// </summary>
    public TileMapLayer CollisionTileMap { get; set; }

    /// <summary>
    /// The chunk data this renderer is displaying.
    /// </summary>
    public ChunkData ChunkData => _chunkData;

    public override void _Ready()
    {
        _sprite = new Sprite2D();
        _sprite.Centered = false;  // Position from top-left
        _sprite.TextureFilter = TextureFilterEnum.Nearest;  // Pixel-perfect scaling
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

        // Set up collision tiles
        if (CollisionTileMap != null)
        {
            GenerateCollisionTiles();
        }
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
    /// </summary>
    private void DrawTileToImage(int tileX, int tileY)
    {
        TileInfo tileInfo = _chunkData.Tiles[tileX, tileY];
        TerrainType terrainType = (TerrainType)tileInfo.SimplexGenIndex;
        Color color = terrainType.GetColor(tileInfo.VariantIndex);

        // Calculate pixel coordinates
        int pixelX = tileX * TilePixelSize;
        int pixelY = tileY * TilePixelSize;

        // Fill the 4x4 pixel region
        for (int px = 0; px < TilePixelSize; px++)
        {
            for (int py = 0; py < TilePixelSize; py++)
            {
                _image.SetPixel(pixelX + px, pixelY + py, color);
            }
        }
    }

    /// <summary>
    /// Generates collision tiles for all collidable terrain in the chunk.
    /// </summary>
    private void GenerateCollisionTiles()
    {
        for (int tileX = 0; tileX < _chunkData.Width; tileX++)
        {
            for (int tileY = 0; tileY < _chunkData.Height; tileY++)
            {
                TileInfo tileInfo = _chunkData.Tiles[tileX, tileY];
                TerrainType terrainType = (TerrainType)tileInfo.SimplexGenIndex;

                if (terrainType.HasCollision())
                {
                    int worldTileX = _chunkData.StartX + tileX;
                    int worldTileY = _chunkData.StartY + tileY;
                    
                    // Place collision tile (using source 0, atlas coord 0,0)
                    CollisionTileMap.SetCell(new Vector2I(worldTileX, worldTileY), 0, Vector2I.Zero);
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
    public void ModifyTile(int localTileX, int localTileY, TerrainType newTerrainType, int variantIndex = 0)
    {
        // Update chunk data
        _chunkData.Tiles[localTileX, localTileY] = new TileInfo(
            (int)newTerrainType,
            0,  // TileSetIndex not used for texture rendering
            Vector2I.Zero,  // AtlasCoords not used for texture rendering
            variantIndex
        );

        // Update texture for this tile
        DrawTileToImage(localTileX, localTileY);
        _texture.Update(_image);

        // Update collision
        if (CollisionTileMap != null)
        {
            int worldTileX = _chunkData.StartX + localTileX;
            int worldTileY = _chunkData.StartY + localTileY;

            if (newTerrainType.HasCollision())
            {
                CollisionTileMap.SetCell(new Vector2I(worldTileX, worldTileY), 0, Vector2I.Zero);
            }
            else
            {
                // Remove collision tile
                CollisionTileMap.EraseCell(new Vector2I(worldTileX, worldTileY));
            }
        }
    }

    /// <summary>
    /// Clears all collision tiles for this chunk.
    /// </summary>
    public void ClearCollisionTiles()
    {
        if (CollisionTileMap == null || _chunkData == null)
            return;

        for (int tileX = 0; tileX < _chunkData.Width; tileX++)
        {
            for (int tileY = 0; tileY < _chunkData.Height; tileY++)
            {
                int worldTileX = _chunkData.StartX + tileX;
                int worldTileY = _chunkData.StartY + tileY;
                CollisionTileMap.EraseCell(new Vector2I(worldTileX, worldTileY));
            }
        }
    }

    public override void _ExitTree()
    {
        // Clean up collision tiles when chunk is removed
        ClearCollisionTiles();

        _image?.Dispose();
        _texture?.Dispose();
    }
}
