using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Manages terrain chunk generation and tracking.
/// Chunks are generated on-demand as the camera reveals new areas.
/// </summary>
public partial class ChunkManager : Node
{
    [Export]
    public TerrainGen TerrainGen { get; set; }

    [Export]
    public Camera2D Camera { get; set; }

    /// <summary>
    /// Size of each chunk in tiles (NxN).
    /// </summary>
    [Export]
    public int ChunkSize { get; set; } = 600;

    /// <summary>
    /// How many chunks to generate beyond the visible area (buffer).
    /// </summary>
    [Export]
    public int ChunkBuffer { get; set; } = 1;

    /// <summary>
    /// Maximum number of chunks to apply per frame (SetCell calls on main thread).
    /// Higher values = faster tile placement but more stutter.
    /// </summary>
    [Export]
    public int ChunksToApplyPerFrame { get; set; } = 2;

    /// <summary>
    /// Maximum number of async chunk generation tasks to run concurrently.
    /// </summary>
    [Export]
    public int MaxConcurrentGenerations { get; set; } = 4;

    // Tracks which chunks have been fully generated and applied
    private HashSet<Vector2I> _generatedChunks = new HashSet<Vector2I>();

    // Tracks which chunks are currently being generated asynchronously
    private HashSet<Vector2I> _generatingChunks = new HashSet<Vector2I>();

    // Queue of chunks waiting to be generated (not yet started)
    private Queue<Vector2I> _pendingChunks = new Queue<Vector2I>();

    // Tracks which chunks are already queued to avoid duplicates
    private HashSet<Vector2I> _queuedChunks = new HashSet<Vector2I>();

    // Thread-safe queue for completed chunk data ready to be applied on main thread
    private ConcurrentQueue<ChunkData> _completedChunks = new ConcurrentQueue<ChunkData>();

    // Thread-safe queue for failed chunks that need to be retried
    private ConcurrentQueue<Vector2I> _failedChunks = new ConcurrentQueue<Vector2I>();

    // Maps chunk coordinates to their renderers
    private Dictionary<Vector2I, ChunkRenderer> _chunkRenderers = new Dictionary<Vector2I, ChunkRenderer>();

    // Parent node for all chunk renderers
    private Node2D _chunkContainer;

    // Collision TileMapLayer (shared across all chunks)
    private TileMapLayer _collisionTileMap;

    public override void _Ready()
    {
        if (TerrainGen == null)
        {
            GD.PrintErr("ChunkManager: TerrainGen not assigned!");
            return;
        }

        if (Camera == null)
        {
            GD.PrintErr("ChunkManager: Camera not assigned!");
            return;
        }

        // Initialize terrain colors from textures
        TerrainColors.Initialize();

        // Create container for chunk renderers
        _chunkContainer = new Node2D();
        _chunkContainer.Name = "ChunkContainer";
        AddChild(_chunkContainer);

        // Create collision TileMapLayer
        SetupCollisionTileMap();
    }

    /// <summary>
    /// Sets up the collision-only TileMapLayer.
    /// </summary>
    private void SetupCollisionTileMap()
    {
        _collisionTileMap = new TileMapLayer();
        _collisionTileMap.Name = "CollisionTileMap";

        // Create a minimal TileSet for collision
        var tileSet = new TileSet();
        tileSet.TileSize = new Vector2I(ChunkRenderer.TilePixelSize, ChunkRenderer.TilePixelSize);

        // Create a TileSetAtlasSource with a small transparent texture
        var atlasSource = new TileSetAtlasSource();
        
        // Create a 4x4 transparent image for the collision tile
        var image = Image.CreateEmpty(ChunkRenderer.TilePixelSize, ChunkRenderer.TilePixelSize, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));  // Fully transparent
        var texture = ImageTexture.CreateFromImage(image);
        
        atlasSource.Texture = texture;
        atlasSource.TextureRegionSize = new Vector2I(ChunkRenderer.TilePixelSize, ChunkRenderer.TilePixelSize);
        atlasSource.CreateTile(Vector2I.Zero);

        // Add physics layer to tileset
        tileSet.AddPhysicsLayer();
        
        // Add the atlas source to the tileset
        tileSet.AddSource(atlasSource);

        // Set up collision for the tile (full tile collision)
        var physicsLayerIdx = 0;
        var tileData = atlasSource.GetTileData(Vector2I.Zero, 0);
        
        // Create a square collision polygon covering the full tile
        var polygon = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(ChunkRenderer.TilePixelSize, 0),
            new Vector2(ChunkRenderer.TilePixelSize, ChunkRenderer.TilePixelSize),
            new Vector2(0, ChunkRenderer.TilePixelSize)
        };
        tileData.AddCollisionPolygon(physicsLayerIdx);
        tileData.SetCollisionPolygonPoints(physicsLayerIdx, 0, polygon);

        _collisionTileMap.TileSet = tileSet;
        
        // Make collision layer invisible (it's only for physics)
        _collisionTileMap.Modulate = new Color(1, 1, 1, 0);

        AddChild(_collisionTileMap);
    }

    public override void _Process(double delta)
    {
        if (TerrainGen == null || Camera == null)
            return;

        // Don't process chunks until TerrainGen is fully initialized
        if (!TerrainGen.IsInitialized)
            return;

        // Re-queue any failed chunks for retry
        RequeueFailedChunks();

        QueueVisibleChunks();
        StartPendingGenerations();
        ApplyCompletedChunks();
    }

    /// <summary>
    /// Re-queues any chunks that failed during async generation.
    /// </summary>
    private void RequeueFailedChunks()
    {
        while (_failedChunks.TryDequeue(out Vector2I failedCoord))
        {
            // Remove from generating set so it can be queued again
            _generatingChunks.Remove(failedCoord);

            // Only re-queue if not already generated or queued
            if (!_generatedChunks.Contains(failedCoord) && !_queuedChunks.Contains(failedCoord))
            {
                _pendingChunks.Enqueue(failedCoord);
                _queuedChunks.Add(failedCoord);
            }
        }
    }

    /// <summary>
    /// Queues chunks that should be visible but haven't been generated yet.
    /// </summary>
    private void QueueVisibleChunks()
    {
        // Get visible area in world coordinates
        Rect2 visibleRect = GetVisibleWorldRect();

        // Convert to chunk coordinates
        Vector2I minChunk = WorldToChunkCoords(visibleRect.Position);
        Vector2I maxChunk = WorldToChunkCoords(visibleRect.Position + visibleRect.Size);

        // Add buffer chunks around the visible area
        minChunk -= new Vector2I(ChunkBuffer, ChunkBuffer);
        maxChunk += new Vector2I(ChunkBuffer, ChunkBuffer);

        // Get camera chunk position for distance-based prioritization
        Vector2I cameraChunk = WorldToChunkCoords(Camera.GlobalPosition);

        // Collect chunks that need generation with their distances
        List<(Vector2I coord, int distance)> chunksToQueue = new List<(Vector2I, int)>();

        for (int cx = minChunk.X; cx <= maxChunk.X; cx++)
        {
            for (int cy = minChunk.Y; cy <= maxChunk.Y; cy++)
            {
                Vector2I chunkCoord = new Vector2I(cx, cy);

                // Skip if already generated, generating, or queued
                if (_generatedChunks.Contains(chunkCoord) || 
                    _generatingChunks.Contains(chunkCoord) || 
                    _queuedChunks.Contains(chunkCoord))
                    continue;

                // Calculate Manhattan distance from camera for prioritization
                int distance = Math.Abs(cx - cameraChunk.X) + Math.Abs(cy - cameraChunk.Y);
                chunksToQueue.Add((chunkCoord, distance));
            }
        }

        // Sort by distance (closest first) and add to queue
        chunksToQueue.Sort((a, b) => a.distance.CompareTo(b.distance));

        foreach (var (coord, _) in chunksToQueue)
        {
            _pendingChunks.Enqueue(coord);
            _queuedChunks.Add(coord);
        }
    }

    /// <summary>
    /// Starts async generation for pending chunks, up to MaxConcurrentGenerations.
    /// </summary>
    private void StartPendingGenerations()
    {
        while (_pendingChunks.Count > 0 && _generatingChunks.Count < MaxConcurrentGenerations)
        {
            Vector2I chunkCoord = _pendingChunks.Dequeue();
            _queuedChunks.Remove(chunkCoord);

            // Double-check it hasn't been generated
            if (_generatedChunks.Contains(chunkCoord) || _generatingChunks.Contains(chunkCoord))
                continue;

            _generatingChunks.Add(chunkCoord);
            StartAsyncChunkGeneration(chunkCoord);
        }
    }

    /// <summary>
    /// Starts async generation of a chunk on a background thread.
    /// </summary>
    private void StartAsyncChunkGeneration(Vector2I chunkCoord)
    {
        int startTileX = chunkCoord.X * ChunkSize;
        int startTileY = chunkCoord.Y * ChunkSize;

        // Capture values for the closure
        int chunkSize = ChunkSize;
        TerrainGen terrainGen = TerrainGen;

        Task.Run(() =>
        {
            try
            {
                // Generate chunk data on background thread (no Godot calls)
                ChunkData chunkData = terrainGen.GenerateChunkData(
                    chunkCoord, startTileX, startTileY, chunkSize, chunkSize);

                // Queue the completed data for main thread application
                _completedChunks.Enqueue(chunkData);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ChunkManager] Failed to generate chunk {chunkCoord}: {ex.Message}");
                
                // Queue for retry
                _failedChunks.Enqueue(chunkCoord);
            }
        });
    }

    /// <summary>
    /// Applies completed chunk data on the main thread using ChunkRenderer.
    /// </summary>
    private void ApplyCompletedChunks()
    {
        int chunksApplied = 0;

        while (chunksApplied < ChunksToApplyPerFrame && _completedChunks.TryDequeue(out ChunkData chunkData))
        {
            // Create and initialize chunk renderer
            var renderer = new ChunkRenderer();
            renderer.CollisionTileMap = _collisionTileMap;
            renderer.SimplexGens = TerrainGen.SimplexGens;
            _chunkContainer.AddChild(renderer);
            renderer.Initialize(chunkData);

            // Track the renderer
            _chunkRenderers[chunkData.ChunkCoord] = renderer;

            // Update tracking
            _generatingChunks.Remove(chunkData.ChunkCoord);
            _generatedChunks.Add(chunkData.ChunkCoord);

            chunksApplied++;
        }
    }

    /// <summary>
    /// Gets the visible world rectangle based on camera position and zoom.
    /// </summary>
    private Rect2 GetVisibleWorldRect()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2 cameraPos = Camera.GlobalPosition;
        Vector2 zoom = Camera.Zoom;

        // Account for zoom - smaller zoom = see more area
        Vector2 visibleSize = viewportSize / zoom;

        // Camera is centered, so offset by half the visible size
        Vector2 topLeft = cameraPos - (visibleSize / 2);

        return new Rect2(topLeft, visibleSize);
    }

    /// <summary>
    /// Converts world position to chunk coordinates.
    /// </summary>
    private Vector2I WorldToChunkCoords(Vector2 worldPos)
    {
        int chunkWorldSize = ChunkSize * ChunkRenderer.TilePixelSize;

        int chunkX = Mathf.FloorToInt(worldPos.X / chunkWorldSize);
        int chunkY = Mathf.FloorToInt(worldPos.Y / chunkWorldSize);

        return new Vector2I(chunkX, chunkY);
    }

    /// <summary>
    /// Converts world position to tile coordinates.
    /// </summary>
    private Vector2I WorldToTileCoords(Vector2 worldPos)
    {
        int tileX = Mathf.FloorToInt(worldPos.X / ChunkRenderer.TilePixelSize);
        int tileY = Mathf.FloorToInt(worldPos.Y / ChunkRenderer.TilePixelSize);
        return new Vector2I(tileX, tileY);
    }

    /// <summary>
    /// Clears all generated chunks and resets the manager.
    /// </summary>
    public void ClearAllChunks()
    {
        // Remove all chunk renderers
        foreach (var renderer in _chunkRenderers.Values)
        {
            renderer.QueueFree();
        }
        _chunkRenderers.Clear();

        _generatedChunks.Clear();
        _generatingChunks.Clear();
        _pendingChunks.Clear();
        _queuedChunks.Clear();
        
        // Clear the completed chunks queue
        while (_completedChunks.TryDequeue(out _)) { }
        
        // Clear the failed chunks queue
        while (_failedChunks.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Gets the number of chunks waiting to be generated.
    /// </summary>
    public int GetPendingChunkCount()
    {
        return _pendingChunks.Count;
    }

    /// <summary>
    /// Checks if a chunk at the given coordinates has been generated.
    /// </summary>
    public bool IsChunkGenerated(Vector2I chunkCoord)
    {
        return _generatedChunks.Contains(chunkCoord);
    }

    /// <summary>
    /// Gets the total number of generated chunks.
    /// </summary>
    public int GetGeneratedChunkCount()
    {
        return _generatedChunks.Count;
    }

    /// <summary>
    /// Modifies a tile at the given world position.
    /// </summary>
    /// <param name="worldPos">World position in pixels</param>
    /// <param name="newTerrainType">The new terrain type</param>
    /// <param name="variantIndex">The color variant (0-3), defaults to 0</param>
    /// <returns>True if the tile was modified, false if chunk not loaded</returns>
    public bool ModifyTileAtWorldPos(Vector2 worldPos, TerrainType newTerrainType)
    {
        Vector2I chunkCoord = WorldToChunkCoords(worldPos);
        
        if (!_chunkRenderers.TryGetValue(chunkCoord, out ChunkRenderer renderer))
            return false;

        // Calculate local tile coordinates within the chunk
        Vector2I tileCoord = WorldToTileCoords(worldPos);
        int localTileX = tileCoord.X - renderer.ChunkData.StartX;
        int localTileY = tileCoord.Y - renderer.ChunkData.StartY;

        // Validate bounds
        if (localTileX < 0 || localTileX >= renderer.ChunkData.Width ||
            localTileY < 0 || localTileY >= renderer.ChunkData.Height)
            return false;

        renderer.ModifyTile(localTileX, localTileY, newTerrainType);
        return true;
    }

    /// <summary>
    /// Modifies a tile at the given tile coordinates.
    /// </summary>
    /// <param name="tileX">Tile X coordinate</param>
    /// <param name="tileY">Tile Y coordinate</param>
    /// <param name="newTerrainType">The new terrain type</param>
    /// <param name="variantIndex">The color variant (0-3), defaults to 0</param>
    /// <returns>True if the tile was modified, false if chunk not loaded</returns>
    public bool ModifyTile(int tileX, int tileY, TerrainType newTerrainType)
    {
        Vector2 worldPos = new Vector2(
            tileX * ChunkRenderer.TilePixelSize,
            tileY * ChunkRenderer.TilePixelSize
        );
        return ModifyTileAtWorldPos(worldPos, newTerrainType);
    }

    /// <summary>
    /// Gets the terrain type at the given world position.
    /// </summary>
    /// <param name="worldPos">World position in pixels</param>
    /// <returns>The terrain type, or null if chunk not loaded</returns>
    public TerrainType? GetTerrainTypeAtWorldPos(Vector2 worldPos)
    {
        Vector2I chunkCoord = WorldToChunkCoords(worldPos);
        
        if (!_chunkRenderers.TryGetValue(chunkCoord, out ChunkRenderer renderer))
            return null;

        Vector2I tileCoord = WorldToTileCoords(worldPos);
        int localTileX = tileCoord.X - renderer.ChunkData.StartX;
        int localTileY = tileCoord.Y - renderer.ChunkData.StartY;

        if (localTileX < 0 || localTileX >= renderer.ChunkData.Width ||
            localTileY < 0 || localTileY >= renderer.ChunkData.Height)
            return null;

        return (TerrainType)renderer.ChunkData.Tiles[localTileX, localTileY].SimplexGenIndex;
    }

    /// <summary>
    /// Gets the ChunkRenderer for the given chunk coordinates.
    /// </summary>
    public ChunkRenderer GetChunkRenderer(Vector2I chunkCoord)
    {
        _chunkRenderers.TryGetValue(chunkCoord, out ChunkRenderer renderer);
        return renderer;
    }
}
