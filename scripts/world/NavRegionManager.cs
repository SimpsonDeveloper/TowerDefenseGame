using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Builds and maintains the NavigationPolygon from ChunkManager tile data,
/// keeping nav logic entirely out of ChunkManager.
///
/// Why build manually instead of baking?
/// NavigationPolygon's bake only parses StaticBody2D and the legacy TileMap node.
/// ChunkManager uses TileMapLayer, which is not parsed. Building from tile data
/// directly is reliable and fast — it's just dictionary lookups and outline math.
///
/// How it works:
///   1. On Ready, subscribe to ChunkManager.ChunksBatchApplied.
///   2. Each signal (or on Ready) resets a debounce timer.
///   3. When the timer expires, RebuildPolygon() iterates every tile row globally
///      across all loaded chunks (left to right), merges solid runs vertically
///      across consecutive rows, and emits one obstruction outline per merged
///      rectangle. BakeFromSourceGeometryData() bakes the result and pushes it
///      to the NavigationServer.
///
/// Why iterate by row instead of by chunk?
/// Per-chunk iteration breaks solid runs at chunk boundaries, producing two
/// adjacent obstacle rects sharing a vertical edge. Iterating rows globally
/// continues runs across adjacent chunks, producing one rect instead of two.
/// Vertical merging (extending rects with identical X spans into the next row)
/// avoids rects sharing horizontal edges. Together these eliminate the
/// "more than 2 edges in rasterization space" navigation warning.
/// </summary>
[GlobalClass]
public partial class NavRegionManager : Node
{
    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public NavigationRegion2D NavigationRegion { get; set; }

    /// <summary>
    /// Node to centre the walkable boundary on each rebuild (typically the
    /// camera or player). If null, the world origin is used instead.
    /// </summary>
    [Export] public Node2D Center { get; set; }

    /// <summary>
    /// Half-extent of the outer walkable boundary in pixels, centred on
    /// Center (or the world origin). Needs to cover the area where enemies
    /// are active — a few screens larger than the visible viewport is fine.
    /// </summary>
    [Export] public float WalkableExtent { get; set; } = 3000f;

    /// <summary>
    /// Seconds to wait after the last chunk batch before rebuilding.
    /// Collapses rapid successive chunk applications into one rebuild.
    /// </summary>
    [Export] public double DebounceDelay { get; set; } = 0.5;

    private double _timer = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (ChunkManager == null)
        {
            GD.PushWarning($"{Name}: ChunkManager not assigned.");
            return;
        }
        if (NavigationRegion == null)
        {
            GD.PushWarning($"{Name}: NavigationRegion not assigned.");
            return;
        }

        ChunkManager.ChunksBatchApplied += OnChunksBatchApplied;
        MarkDirty();
    }

    public override void _ExitTree()
    {
        if (ChunkManager != null)
            ChunkManager.ChunksBatchApplied -= OnChunksBatchApplied;
    }

    public override void _Process(double delta)
    {
        if (_timer < 0) return;
        _timer -= delta;
        if (_timer <= 0)
        {
            _timer = -1;
            RebuildPolygon();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Schedule a rebuild after the debounce window.</summary>
    public void MarkDirty() => _timer = DebounceDelay;

    // ── Internal ──────────────────────────────────────────────────────────

    private void OnChunksBatchApplied(int count) => MarkDirty();

    private void RebuildPolygon()
    {
        if (NavigationRegion == null || ChunkManager == null) return;

        var chunks = ChunkManager.GetGeneratedChunks();
        if (chunks.Count == 0) return;

        var navPoly = new NavigationPolygon();
        navPoly.AgentRadius = 9f;

        var sourceData = new NavigationMeshSourceGeometryData2D();

        // Outer walkable boundary — centred on the camera/player so it follows
        // the player through an infinite world instead of being fixed at the origin.
        Vector2 c = Center?.GlobalPosition ?? Vector2.Zero;
        float e = WalkableExtent;
        sourceData.AddTraversableOutline([
            new(c.X - e, c.Y - e), new(c.X + e, c.Y - e),
            new(c.X + e, c.Y + e), new(c.X - e, c.Y + e)
        ]);

        int cs = ChunkManager.ChunkSize;

        // ── Build lookup: chunkY → sorted list of chunkX ──────────────────
        // Sorting by chunkX lets us scan each tile row left-to-right across
        // all loaded chunks, continuing runs across adjacent chunk boundaries.

        var chunkYToXs = new Dictionary<int, List<int>>();
        int minChunkY = int.MaxValue, maxChunkY = int.MinValue;

        foreach (var coord in chunks)
        {
            if (!chunkYToXs.TryGetValue(coord.Y, out var xs))
                chunkYToXs[coord.Y] = xs = new List<int>();
            xs.Add(coord.X);
            if (coord.Y < minChunkY) minChunkY = coord.Y;
            if (coord.Y > maxChunkY) maxChunkY = coord.Y;
        }
        foreach (var xs in chunkYToXs.Values)
            xs.Sort();

        // Tile row range. maxTileY is one past the last real row so that the
        // final loop iteration has empty nextActive and flushes all active rects.
        int minTileY = minChunkY * cs;
        int maxTileY = (maxChunkY + 1) * cs;

        // ── Vertical merging ───────────────────────────────────────────────
        // key   = (tileX1, tileX2) solid run extent
        // value = tile row where this obstacle rect started

        var activeRects = new Dictionary<(int x1, int x2), int>();
        var nextActive  = new Dictionary<(int x1, int x2), int>();

        for (int ty = minTileY; ty <= maxTileY; ty++)
        {
            nextActive.Clear();

            if (ty < maxTileY)
            {
                int chunkY = Mathf.FloorToInt((float)ty / cs);

                if (chunkYToXs.TryGetValue(chunkY, out var chunkXs))
                {
                    int runStart   = 0;
                    bool inRun     = false;
                    int? prevChunkX = null;

                    foreach (int chunkX in chunkXs)
                    {
                        // Gap between chunks: close any run that was open at the
                        // end of the previous chunk rather than letting it bridge
                        // across unloaded tiles.
                        if (prevChunkX.HasValue && chunkX > prevChunkX.Value + 1 && inRun)
                        {
                            int gapEnd = (prevChunkX.Value + 1) * cs;
                            var gapKey = (runStart, gapEnd);
                            nextActive[gapKey] = activeRects.TryGetValue(gapKey, out int gsy) ? gsy : ty;
                            inRun = false;
                        }

                        int startX = chunkX * cs;
                        int endX   = startX + cs;

                        // Loop to tx < endX (not <=). The run is NOT force-closed
                        // at the chunk edge — if the next chunk is adjacent it will
                        // continue seamlessly, eliminating the vertical shared edge.
                        for (int tx = startX; tx < endX; tx++)
                        {
                            bool solid = IsSolid(tx, ty);

                            if (solid && !inRun)
                            {
                                runStart = tx;
                                inRun    = true;
                            }
                            else if (!solid && inRun)
                            {
                                var key = (runStart, tx);
                                nextActive[key] = activeRects.TryGetValue(key, out int sy) ? sy : ty;
                                inRun = false;
                            }
                        }

                        prevChunkX = chunkX;
                    }

                    // Close any run still open after the last chunk in this row.
                    if (inRun && prevChunkX.HasValue)
                    {
                        int endX = (prevChunkX.Value + 1) * cs;
                        var key  = (runStart, endX);
                        nextActive[key] = activeRects.TryGetValue(key, out int sy) ? sy : ty;
                    }
                }
            }
            // When ty == maxTileY, nextActive stays empty and all remaining
            // active rects are flushed by the loop below.

            // Flush rects whose run did not continue into this row.
            foreach (var kvp in activeRects)
            {
                if (!nextActive.ContainsKey(kvp.Key))
                    AddObstacleRect(sourceData, kvp.Key.x1, kvp.Value, kvp.Key.x2, ty);
            }

            (activeRects, nextActive) = (nextActive, activeRects);
        }

        // Bake synchronously on the main thread.
        // MakePolygonsFromOutlines() was deprecated in Godot 4.4 and silently
        // produces no polygons in 4.5. BakeFromSourceGeometryData is the
        // supported replacement and handles outline → polygon conversion correctly.
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData);

        NavigationRegion.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(NavigationRegion.GetRid(), navPoly);
    }

    private bool IsSolid(int tileX, int tileY)
    {
        const int ts = ChunkRenderer.TilePixelSize;
        var worldPos = new Vector2(tileX * ts + ts * 0.5f, tileY * ts + ts * 0.5f);
        TerrainType? terrain = ChunkManager.GetTerrainTypeAtWorldPos(worldPos);
        return terrain.HasValue && TerrainTypeExtensions.HasCollision(terrain.Value);
    }

    /// <summary>
    /// Adds a rectangle obstruction outline for tile range
    /// [tileX1, tileX2) × [tileY1, tileY2) in tile coordinates.
    /// </summary>
    private static void AddObstacleRect(
        NavigationMeshSourceGeometryData2D sourceData,
        int tileX1, int tileY1, int tileX2, int tileY2)
    {
        const int ts = ChunkRenderer.TilePixelSize;
        float wx1 = tileX1 * ts, wy1 = tileY1 * ts;
        float wx2 = tileX2 * ts, wy2 = tileY2 * ts;

        sourceData.AddObstructionOutline([new(wx1, wy1), new(wx2, wy1), new(wx2, wy2), new(wx1, wy2)]);
    }
}
