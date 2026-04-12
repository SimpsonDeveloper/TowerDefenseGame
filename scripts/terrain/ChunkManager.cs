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
public partial class ChunkManager : Node2D
{
    /// <summary>
    /// Emitted on the main thread each frame that at least one chunk finishes
    /// being applied to the scene. Subscribers use this to know when collision
    /// geometry has changed without any nav logic living inside ChunkManager itself.
    /// </summary>
    [Signal]
    public delegate void ChunksBatchAppliedEventHandler(int count);

    /// <summary>Emitted when all chunks are cleared (e.g. for sequential debug regeneration).</summary>
    [Signal]
    public delegate void ChunksClearedEventHandler();

    [Export] public TerrainGen TerrainGen { get; set; }
    [Export] public Camera2D Camera { get; set; }
    [Export] public CoordConfig CoordConfig { get; set; }

    /// <summary>How many chunks to generate beyond the visible area (buffer).</summary>
    [Export] public int ChunkBuffer { get; set; } = 1;

    /// <summary>Maximum number of chunks to apply per frame (SetCell calls on main thread). Higher values = faster tile placement but more stutter.</summary>
    [Export] public int ChunksToApplyPerFrame { get; set; } = 2;

    /// <summary>Maximum number of async chunk generation tasks to run concurrently.</summary>
    [Export] public int MaxConcurrentGenerations { get; set; } = 4;

    /// <summary>If true, chunk generation is clamped to BoundsMin/BoundsMax (inclusive, in chunk coords).</summary>
    [Export] public bool BoundsEnabled { get; set; } = false;

    /// <summary>Minimum chunk coordinate (inclusive) when BoundsEnabled is true.</summary>
    [Export] public Vector2I BoundsMin { get; set; } = Vector2I.Zero;

    /// <summary>Maximum chunk coordinate (inclusive) when BoundsEnabled is true.</summary>
    [Export] public Vector2I BoundsMax { get; set; } = new Vector2I(31, 31);

    /// <summary>
    /// If true, all chunks within bounds are queued immediately on _Ready and
    /// camera-based generation is disabled. Requires BoundsEnabled = true.
    /// </summary>
    [Export] public bool PreGenerateAll { get; set; } = false;

    /// <summary>If true, draws a blue outline around each chunk boundary as it is generated.</summary>
    [Export] public bool DrawDebugEnabled { get; set; } = false;

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
    private ChunkDebugDraw _debugDraw;

    // Generation order tracked for debug index labels.
    private readonly List<Vector2I> _chunkGenerationOrder = new();

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

        if (CoordConfig == null)
        {
            GD.PrintErr("ChunkManager: CoordConfig not assigned!");
            return;
        }

        // Create container for chunk renderers
        _chunkContainer = new Node2D();
        _chunkContainer.Name = "ChunkContainer";
        AddChild(_chunkContainer);

        _debugDraw = new ChunkDebugDraw();
        AddChild(_debugDraw);

        if (PreGenerateAll && BoundsEnabled)
            QueueAllBoundedChunks();
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

        if (!PreGenerateAll)
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
    /// Queues every chunk within the declared bounds. Called once on _Ready
    /// when PreGenerateAll is true.
    /// </summary>
    private void QueueAllBoundedChunks()
    {
        for (int cx = BoundsMin.X; cx <= BoundsMax.X; cx++)
        {
            for (int cy = BoundsMin.Y; cy <= BoundsMax.Y; cy++)
            {
                var chunkCoord = new Vector2I(cx, cy);
                if (!_generatedChunks.Contains(chunkCoord) &&
                    !_generatingChunks.Contains(chunkCoord) &&
                    !_queuedChunks.Contains(chunkCoord))
                {
                    _pendingChunks.Enqueue(chunkCoord);
                    _queuedChunks.Add(chunkCoord);
                }
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

        // Clamp to bounds if enabled
        if (BoundsEnabled)
        {
            minChunk = new Vector2I(Math.Max(minChunk.X, BoundsMin.X), Math.Max(minChunk.Y, BoundsMin.Y));
            maxChunk = new Vector2I(Math.Min(maxChunk.X, BoundsMax.X), Math.Min(maxChunk.Y, BoundsMax.Y));
        }

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
        int startTileX = chunkCoord.X * CoordConfig.ChunkSizeTiles;
        int startTileY = chunkCoord.Y * CoordConfig.ChunkSizeTiles;

        // Capture values for the closure
        int chunkSize = CoordConfig.ChunkSizeTiles;
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
            renderer.CoordConfig = CoordConfig;
            renderer.SimplexGens = TerrainGen.SimplexGensMapped;
            _chunkContainer.AddChild(renderer);
            renderer.Initialize(chunkData);

            // Track the renderer
            _chunkRenderers[chunkData.ChunkCoord] = renderer;

            // Update tracking
            _generatingChunks.Remove(chunkData.ChunkCoord);
            _chunkGenerationOrder.Add(chunkData.ChunkCoord);
            _generatedChunks.Add(chunkData.ChunkCoord);

            chunksApplied++;
        }

        if (chunksApplied > 0)
        {
            EmitSignal(SignalName.ChunksBatchApplied, chunksApplied);
            if (DrawDebugEnabled)
                _debugDraw.Update(_chunkGenerationOrder, CoordHelper.ChunkSizePixels(CoordConfig));
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
    private Vector2I WorldToChunkCoords(Vector2 worldPos) => CoordHelper.WorldToChunk(worldPos, CoordConfig);

    private Vector2I WorldToTileCoords(Vector2 worldPos) => CoordHelper.WorldToTile(worldPos, CoordConfig);

    /// <summary>
    /// Clears all generated chunks and resets the manager.
    /// </summary>
    public void ClearAllChunks()
    {
        // Remove all chunk renderers
        foreach (var renderer in _chunkRenderers.Values)
            renderer.QueueFree();
        _chunkRenderers.Clear();

        _generatedChunks.Clear();
        _generatingChunks.Clear();
        _pendingChunks.Clear();
        _queuedChunks.Clear();
        _chunkGenerationOrder.Clear();

        while (_completedChunks.TryDequeue(out _)) { }
        while (_failedChunks.TryDequeue(out _)) { }

        if (DrawDebugEnabled)
            _debugDraw.Update(_chunkGenerationOrder, CoordHelper.ChunkSizePixels(CoordConfig));

        EmitSignal(SignalName.ChunksCleared);
    }

    /// <summary>Queues a specific chunk for generation regardless of camera visibility.</summary>
    public void ForceGenerateChunk(Vector2I coord)
    {
        if (_generatedChunks.Contains(coord) || _generatingChunks.Contains(coord) || _queuedChunks.Contains(coord))
            return;
        _pendingChunks.Enqueue(coord);
        _queuedChunks.Add(coord);
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
    /// <returns>True if the tile was modified, false if chunk not loaded</returns>
    public bool ModifyTile(int tileX, int tileY, TerrainType newTerrainType)
    {
        return ModifyTileAtWorldPos(CoordHelper.TileToWorld(new Vector2I(tileX, tileY), CoordConfig), newTerrainType);
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

        return renderer.ChunkData.Tiles[localTileX, localTileY].TerrainType;
    }

    /// <summary>
    /// Gets the ChunkRenderer for the given chunk coordinates.
    /// </summary>
    public ChunkRenderer GetChunkRenderer(Vector2I chunkCoord)
    {
        _chunkRenderers.TryGetValue(chunkCoord, out ChunkRenderer renderer);
        return renderer;
    }

    /// <summary>
    /// Returns the set of chunk coordinates that have been fully generated and
    /// applied. Read-only view — do not modify.
    /// </summary>
    public IReadOnlyCollection<Vector2I> GetGeneratedChunks() => _generatedChunks;

    // ── Debug draw ─────────────────────────────────────────────────────────────

    private partial class ChunkDebugDraw : Node2D
    {
        private List<Vector2I> _chunks = new();
        private int _chunkPixelSize;

        public void Update(List<Vector2I> generationOrder, int chunkPixelSize)
        {
            _chunks = new List<Vector2I>(generationOrder);
            _chunkPixelSize = chunkPixelSize;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var borderCol = new Color(0.2f, 0.5f, 1f, 0.8f);
            var labelCol  = Colors.Cyan;
            float s = _chunkPixelSize;
            for (int i = 0; i < _chunks.Count; i++)
            {
                var cc = _chunks[i];
                float x = cc.X * s, y = cc.Y * s;
                DrawPolyline(new[]
                {
                    new Vector2(x,     y),
                    new Vector2(x + s, y),
                    new Vector2(x + s, y + s),
                    new Vector2(x,     y + s),
                    new Vector2(x,     y),
                }, borderCol, 2f);
                DrawString(ThemeDB.FallbackFont,
                    new Vector2(x + s * 0.5f, y + s * 0.5f),
                    i.ToString(), fontSize: 80, modulate: labelCol);
                DrawString(ThemeDB.FallbackFont,
                    new Vector2(x + s * 0.5f, y + s * 0.5f + 80),
                    $"({cc.X},{cc.Y})", fontSize: 80, modulate: labelCol);
            }
        }
    }

}

