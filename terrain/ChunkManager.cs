using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Maximum number of chunks to generate per frame.
    /// Higher values = faster loading but more stutter.
    /// </summary>
    [Export]
    public int ChunksPerFrame { get; set; } = 2;

    // Tracks which chunks have been generated using chunk coordinates as keys
    private HashSet<Vector2I> _generatedChunks = new HashSet<Vector2I>();

    // Queue of chunks waiting to be generated
    private Queue<Vector2I> _pendingChunks = new Queue<Vector2I>();

    // Tracks which chunks are already queued to avoid duplicates
    private HashSet<Vector2I> _queuedChunks = new HashSet<Vector2I>();

    private Vector2I _lastCameraChunk = new Vector2I(int.MinValue, int.MinValue);

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
    }

    public override void _Process(double delta)
    {
        if (TerrainGen == null || Camera == null)
            return;

        QueueVisibleChunks();
        ProcessPendingChunks();
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

                // Skip if already generated or already queued
                if (_generatedChunks.Contains(chunkCoord) || _queuedChunks.Contains(chunkCoord))
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
    /// Processes a limited number of pending chunks per frame.
    /// </summary>
    private void ProcessPendingChunks()
    {
        int chunksGenerated = 0;

        while (_pendingChunks.Count > 0 && chunksGenerated < ChunksPerFrame)
        {
            Vector2I chunkCoord = _pendingChunks.Dequeue();
            _queuedChunks.Remove(chunkCoord);

            // Double-check it hasn't been generated (in case of race conditions)
            if (_generatedChunks.Contains(chunkCoord))
                continue;

            GenerateChunk(chunkCoord);
            _generatedChunks.Add(chunkCoord);
            chunksGenerated++;
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
        if (TerrainGen.TileMapLayer == null)
            return Vector2I.Zero;

        Vector2I tileSize = TerrainGen.TileMapLayer.TileSet.TileSize;
        int chunkWorldSize = ChunkSize * tileSize.X; // Assuming square tiles

        int chunkX = Mathf.FloorToInt(worldPos.X / chunkWorldSize);
        int chunkY = Mathf.FloorToInt(worldPos.Y / chunkWorldSize);

        return new Vector2I(chunkX, chunkY);
    }

    /// <summary>
    /// Generates terrain for a specific chunk.
    /// </summary>
    private void GenerateChunk(Vector2I chunkCoord)
    {
        Stopwatch sw = Stopwatch.StartNew();

        // Calculate tile coordinates for this chunk
        int startTileX = chunkCoord.X * ChunkSize;
        int startTileY = chunkCoord.Y * ChunkSize;

        TerrainGen.GenerateChunk(startTileX, startTileY, ChunkSize, ChunkSize);

        sw.Stop();
        GD.Print($"[ChunkManager] Chunk {chunkCoord}: {sw.ElapsedMilliseconds}ms total");
    }

    /// <summary>
    /// Clears all generated chunks and resets the manager.
    /// </summary>
    public void ClearAllChunks()
    {
        _generatedChunks.Clear();
        _pendingChunks.Clear();
        _queuedChunks.Clear();
        _lastCameraChunk = new Vector2I(int.MinValue, int.MinValue);
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
}
