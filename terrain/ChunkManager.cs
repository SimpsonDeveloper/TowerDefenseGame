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

    // Tracks which chunks have been generated using chunk coordinates as keys
    private HashSet<Vector2I> _generatedChunks = new HashSet<Vector2I>();

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

        UpdateVisibleChunks();
    }

    /// <summary>
    /// Checks which chunks should be visible and generates any that haven't been created yet.
    /// </summary>
    private void UpdateVisibleChunks()
    {
        // Get visible area in world coordinates
        Rect2 visibleRect = GetVisibleWorldRect();

        // Convert to chunk coordinates
        Vector2I minChunk = WorldToChunkCoords(visibleRect.Position);
        Vector2I maxChunk = WorldToChunkCoords(visibleRect.Position + visibleRect.Size);

        // Add buffer chunks around the visible area
        minChunk -= new Vector2I(ChunkBuffer, ChunkBuffer);
        maxChunk += new Vector2I(ChunkBuffer, ChunkBuffer);

        // Generate any missing chunks
        for (int cx = minChunk.X; cx <= maxChunk.X; cx++)
        {
            for (int cy = minChunk.Y; cy <= maxChunk.Y; cy++)
            {
                Vector2I chunkCoord = new Vector2I(cx, cy);

                if (!_generatedChunks.Contains(chunkCoord))
                {
                    GenerateChunk(chunkCoord);
                    _generatedChunks.Add(chunkCoord);
                }
            }
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
        _lastCameraChunk = new Vector2I(int.MinValue, int.MinValue);
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
