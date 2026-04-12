using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Incrementally builds terrain collision polygons as chunks generate.
///
/// Algorithm:
///   1. On each chunk batch, identify newly generated chunks.
///   2. Contour-trace solid blobs within only the new chunks, treating
///      already-processed adjacent tiles as non-solid so every blob forms a
///      complete closed outline.
///   3. Check chunk borders via physics point query to find existing terrain
///      StaticBody2D blobs that are solid-adjacent to the new chunks.
///   4. Union touching blobs with Geometry2D.MergePolygons.
///   5. Create (or replace) one StaticBody2D + CollisionPolygon2D per blob.
///   6. Emit BlobsUpdated so NavBaker can schedule a nav mesh rebake.
/// </summary>
[GlobalClass]
public partial class PolygonTerrainManager : Node2D
{
    /// <summary>Group tag added to every terrain-blob StaticBody2D.</summary>
    private const string BlobGroup = "terrain_blob";

    /// <summary>
    /// Nodes in this group are included as nav obstructions in addition to terrain
    /// blobs. Add crystals, towers, and any other collidable scene objects here.
    /// </summary>
    public const string NavObstacleGroup = "nav_obstacle";

    /// <summary>Emitted after blobs are updated (new terrain processed or all chunks cleared).</summary>
    [Signal]
    public delegate void BlobsUpdatedEventHandler();

    /// <summary>
    /// Chunk coordinates that were part of the most recent blob update.
    /// Empty when emitted after a full chunk clear — treat as "all cells invalidated".
    /// Includes both newly processed chunks and any old chunks whose blobs were
    /// consumed by a merge, so nav cells can selectively rebake only the affected area.
    /// </summary>
    public IReadOnlyList<Vector2I> LastAffectedChunks { get; private set; } =
        System.Array.Empty<Vector2I>();

    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public CoordConfig CoordConfig { get; set; }

    /// <summary>Seconds to wait after the last chunk batch before processing.</summary>
    [Export] public double DebounceDelay { get; set; } = 0.5;

    /// <summary>
    /// Physics collision layer used for terrain blob StaticBody2D nodes.
    /// Must match the player's and enemy's collision mask so they collide with terrain.
    /// Default = 1 (layer 1).
    /// </summary>
    [Export] public uint TerrainLayer { get; set; } = 1;

    /// <summary>When true, terrain blob polygons and their vertex data are drawn in-world.</summary>
    [Export] public bool DebugDrawEnabled { get; set; }

    private double _timer = -1;
    private Node2D _blobContainer;
    private DebugObstructionDraw _debugDraw;

    // Chunks that have already been traced into blobs.
    private readonly HashSet<Vector2I> _processedChunks = new();

    // blob → set of chunk coords that contributed solid tiles to it.
    private readonly Dictionary<StaticBody2D, HashSet<Vector2I>> _blobToChunks = new();

    // chunk → blobs that contain tiles from it (reverse index for unload).
    private readonly Dictionary<Vector2I, List<StaticBody2D>> _chunkToBlobs = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (ChunkManager == null)  { GD.PushWarning($"{Name}: ChunkManager not assigned.");  return; }
        if (CoordConfig  == null)  { GD.PushWarning($"{Name}: CoordConfig not assigned.");   return; }

        _blobContainer = new Node2D { Name = "BlobContainer" };
        AddChild(_blobContainer);

        _debugDraw = new DebugObstructionDraw();
        AddChild(_debugDraw);

        ChunkManager.ChunksBatchApplied += OnChunksBatchApplied;
        ChunkManager.ChunksCleared      += OnChunksCleared;
        MarkDirty();
    }

    public override void _ExitTree()
    {
        if (ChunkManager != null)
        {
            ChunkManager.ChunksBatchApplied -= OnChunksBatchApplied;
            ChunkManager.ChunksCleared      -= OnChunksCleared;
        }
    }

    private void OnChunksCleared()
    {
        _processedChunks.Clear();
        _blobToChunks.Clear();
        _chunkToBlobs.Clear();
        foreach (var child in _blobContainer.GetChildren())
            child.QueueFree();
        _timer = -1;
        RefreshDebugDraw();
        LastAffectedChunks = System.Array.Empty<Vector2I>(); // empty = full invalidation
        EmitSignal(SignalName.BlobsUpdated);
    }

    public override void _Process(double delta)
    {
        if (_timer >= 0)
        {
            _timer -= delta;
            if (_timer <= 0)
            {
                _timer = -1;
                ProcessNewChunks();
            }
        }
    }

    public void MarkDirty() => _timer = DebounceDelay;

    private void OnChunksBatchApplied(int count) => MarkDirty();

    // ── Main processing ────────────────────────────────────────────────────────

    private void ProcessNewChunks()
    {
        // Find chunks that have generated since our last run.
        var newChunks = new List<Vector2I>();
        foreach (var c in ChunkManager.GetGeneratedChunks())
            if (!_processedChunks.Contains(c))
                newChunks.Add(c);

        if (newChunks.Count == 0) return;

        // Track all chunk coords that are invalidated by this update.
        // Starts with the new chunks; merges also invalidate the old blob's chunks.
        var allAffectedChunks = new HashSet<Vector2I>(newChunks);

        // Find existing terrain blobs adjacent to the new chunks BEFORE tracing,
        // while the physics state still reflects only previously processed blobs.
        var adjacentBlobs = FindAdjacentBlobs(newChunks);

        // Build contour graph. Processed-chunk solid tiles are treated as non-solid
        // so each new blob forms a complete closed outline ready for MergePolygons.
        var edgeGraph = BuildEdgeGraph(newChunks);

        if (edgeGraph.Count > 0)
        {
            var newBlobPolygons = TraceContours(edgeGraph, CoordConfig.TilePixelSize);
            int chunkPixelSize = CoordHelper.ChunkSizePixels(CoordConfig);

            foreach (var blobPixels in newBlobPolygons)
            {
                var mergedPoly   = blobPixels;
                var mergedChunks = GetSpanningChunks(blobPixels, newChunks, chunkPixelSize);

                // Try to merge with each adjacent existing blob.
                // Iterate a snapshot so we can mutate adjacentBlobs during the loop.
                foreach (var candidate in new List<StaticBody2D>(adjacentBlobs))
                {
                    var candidatePoly = GetBodyPolygon(candidate);
                    var results = Geometry2D.MergePolygons(mergedPoly, candidatePoly);
                    if (results.Count != 1) continue; // polygons don't touch — skip

                    mergedPoly = results[0];

                    if (_blobToChunks.TryGetValue(candidate, out var oldChunks))
                    {
                        foreach (var c in oldChunks)
                        {
                            mergedChunks.Add(c);
                            allAffectedChunks.Add(c); // merged blob's old cells also need rebake
                        }
                    }

                    DestroyBlob(candidate);
                    adjacentBlobs.Remove(candidate);
                }

                var newBody = CreateBlobBody(mergedPoly);
                _blobContainer.AddChild(newBody);
                RegisterBlob(newBody, mergedChunks);
            }
        }

        foreach (var c in newChunks) _processedChunks.Add(c);

        RefreshDebugDraw();
        LastAffectedChunks = new List<Vector2I>(allAffectedChunks);
        EmitSignal(SignalName.BlobsUpdated);
    }

    // ── Edge graph ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a directed edge graph for solid tile boundaries within the new chunks.
    /// Neighbours that are solid but belong to already-processed chunks are treated as
    /// non-solid, ensuring every blob in the new chunks forms a complete closed loop.
    /// Merging with processed-chunk blobs is handled later via MergePolygons.
    /// </summary>
    private Dictionary<Vector2I, List<Vector2I>> BuildEdgeGraph(List<Vector2I> newChunks)
    {
        var graph      = new Dictionary<Vector2I, List<Vector2I>>();
        int cs         = CoordConfig.ChunkSizeTiles;
        var newChunkSet = new HashSet<Vector2I>(newChunks);

        // Returns true only when the tile is inside a new chunk AND solid.
        bool IsNewSolid(int tx, int ty)
        {
            var cc = new Vector2I(Mathf.FloorToInt((float)tx / cs), Mathf.FloorToInt((float)ty / cs));
            return newChunkSet.Contains(cc) && IsSolid(tx, ty);
        }

        void AddEdge(Vector2I start, Vector2I end)
        {
            if (!graph.TryGetValue(start, out var list))
                graph[start] = list = new List<Vector2I>();
            list.Add(end);
        }

        foreach (var chunkCoord in newChunks)
        {
            int startX = chunkCoord.X * cs;
            int startY = chunkCoord.Y * cs;

            for (int ty = startY; ty < startY + cs; ty++)
            {
                for (int tx = startX; tx < startX + cs; tx++)
                {
                    if (!IsSolid(tx, ty)) continue;

                    // Emit a directed CW edge for every face bordering a non-new-solid tile.
                    if (!IsNewSolid(tx,     ty - 1)) AddEdge(new(tx,     ty    ), new(tx + 1, ty    ));
                    if (!IsNewSolid(tx + 1, ty    )) AddEdge(new(tx + 1, ty    ), new(tx + 1, ty + 1));
                    if (!IsNewSolid(tx,     ty + 1)) AddEdge(new(tx + 1, ty + 1), new(tx,     ty + 1));
                    if (!IsNewSolid(tx - 1, ty    )) AddEdge(new(tx,     ty + 1), new(tx,     ty    ));
                }
            }
        }

        return graph;
    }

    // ── Contour tracing ────────────────────────────────────────────────────────

    private static List<Vector2[]> TraceContours(Dictionary<Vector2I, List<Vector2I>> graph, int tilePixelSize)
    {
        var result  = new List<Vector2[]>();
        var corners = new List<Vector2I>();

        while (graph.Count > 0)
        {
            var startCorner = FirstKey(graph);
            corners.Clear();

            var current = startCorner;
            var prevDir = Vector2I.Zero;

            do
            {
                corners.Add(current);
                var outgoing = graph[current];
                Vector2I next;

                if (outgoing.Count == 1)
                {
                    next = outgoing[0];
                    graph.Remove(current);
                }
                else
                {
                    next = PickCw(current, outgoing, prevDir);
                    outgoing.Remove(next);
                    if (outgoing.Count == 0) graph.Remove(current);
                }

                prevDir = next - current;
                current = next;
            }
            while (current != startCorner);

            Simplify(corners);

            var pixels = new Vector2[corners.Count];
            for (int i = 0; i < corners.Count; i++)
                pixels[i] = new Vector2(corners[i].X * tilePixelSize, corners[i].Y * tilePixelSize);

            result.Add(pixels);
        }

        return result;
    }

    private static Vector2I PickCw(Vector2I current, List<Vector2I> options, Vector2I prevDir)
    {
        if (prevDir == Vector2I.Zero) return options[0];
        var rightDir = new Vector2I(-prevDir.Y,  prevDir.X);
        var leftDir  = new Vector2I( prevDir.Y, -prevDir.X);
        foreach (var priority in new[] { rightDir, prevDir, leftDir })
            foreach (var opt in options)
                if (opt - current == priority) return opt;
        return options[0];
    }

    private static void Simplify(List<Vector2I> corners)
    {
        bool changed = true;
        while (changed && corners.Count > 2)
        {
            changed = false;
            for (int i = 0; i < corners.Count; i++)
            {
                var a  = corners[i];
                var b  = corners[(i + 1) % corners.Count];
                var cc = corners[(i + 2) % corners.Count];
                if ((a.X == b.X && b.X == cc.X) || (a.Y == b.Y && b.Y == cc.Y))
                {
                    corners.RemoveAt((i + 1) % corners.Count);
                    changed = true;
                    break;
                }
            }
        }
    }

    private static Vector2I FirstKey(Dictionary<Vector2I, List<Vector2I>> dict)
    {
        foreach (var key in dict.Keys) return key;
        throw new System.InvalidOperationException("Empty graph.");
    }

    // ── Blob management ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns existing terrain blobs that are solid-adjacent to any of the new chunks.
    /// Uses physics point queries at the center of processed-chunk tiles that border
    /// a solid new-chunk tile.
    /// </summary>
    private HashSet<StaticBody2D> FindAdjacentBlobs(List<Vector2I> newChunks)
    {
        var found      = new HashSet<StaticBody2D>();
        int cs         = CoordConfig.ChunkSizeTiles;
        int ts         = CoordConfig.TilePixelSize;
        var spaceState = GetViewport().World2D.DirectSpaceState;
        var query      = new PhysicsPointQueryParameters2D { CollisionMask = TerrainLayer };

        foreach (var chunkCoord in newChunks)
        {
            int startX = chunkCoord.X * cs;
            int startY = chunkCoord.Y * cs;

            foreach (var dir in new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down })
            {
                if (!_processedChunks.Contains(chunkCoord + dir)) continue;

                if (dir.X != 0)
                {
                    int borderTx   = dir.X > 0 ? startX + cs - 1 : startX;
                    int neighborTx = borderTx + dir.X;
                    for (int ty = startY; ty < startY + cs; ty++)
                    {
                        if (!IsSolid(borderTx, ty) || !IsSolid(neighborTx, ty)) continue;
                        query.Position = new Vector2(neighborTx * ts + ts * 0.5f, ty * ts + ts * 0.5f);
                        foreach (var hit in spaceState.IntersectPoint(query))
                            if (hit["collider"].Obj is StaticBody2D body && body.IsInGroup(BlobGroup))
                                found.Add(body);
                    }
                }
                else
                {
                    int borderTy   = dir.Y > 0 ? startY + cs - 1 : startY;
                    int neighborTy = borderTy + dir.Y;
                    for (int tx = startX; tx < startX + cs; tx++)
                    {
                        if (!IsSolid(tx, borderTy) || !IsSolid(tx, neighborTy)) continue;
                        query.Position = new Vector2(tx * ts + ts * 0.5f, neighborTy * ts + ts * 0.5f);
                        foreach (var hit in spaceState.IntersectPoint(query))
                            if (hit["collider"].Obj is StaticBody2D body && body.IsInGroup(BlobGroup))
                                found.Add(body);
                    }
                }
            }
        }

        return found;
    }

    private StaticBody2D CreateBlobBody(Vector2[] polygon)
    {
        var body = new StaticBody2D
        {
            CollisionLayer = TerrainLayer,
            CollisionMask  = 0,
        };
        body.AddToGroup(BlobGroup);
        body.AddChild(new CollisionPolygon2D { Polygon = polygon });
        return body;
    }

    private static Vector2[] GetBodyPolygon(StaticBody2D body)
    {
        foreach (var child in body.GetChildren())
            if (child is CollisionPolygon2D cp) return cp.Polygon;
        return System.Array.Empty<Vector2>();
    }

    private void RegisterBlob(StaticBody2D body, HashSet<Vector2I> chunks)
    {
        _blobToChunks[body] = chunks;
        foreach (var c in chunks)
        {
            if (!_chunkToBlobs.TryGetValue(c, out var list))
                _chunkToBlobs[c] = list = new List<StaticBody2D>();
            list.Add(body);
        }
    }

    private void DestroyBlob(StaticBody2D body)
    {
        if (_blobToChunks.TryGetValue(body, out var chunks))
        {
            foreach (var c in chunks)
                if (_chunkToBlobs.TryGetValue(c, out var list))
                    list.Remove(body);
            _blobToChunks.Remove(body);
        }
        _blobContainer.RemoveChild(body);
        body.QueueFree();
    }

    /// <summary>
    /// Finds which of the given new chunks the blob polygon overlaps based on
    /// bounding-box intersection. Conservative — may include adjacent empty chunks,
    /// but never misses a chunk that contributed solid tiles.
    /// </summary>
    private static HashSet<Vector2I> GetSpanningChunks(
        Vector2[] poly, List<Vector2I> newChunks, int chunkPixelSize)
    {
        if (poly.Length == 0) return new HashSet<Vector2I>(newChunks);

        float minX = poly[0].X, minY = poly[0].Y, maxX = poly[0].X, maxY = poly[0].Y;
        foreach (var pt in poly)
        {
            if (pt.X < minX) minX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
            if (pt.X > maxX) maxX = pt.X;
            if (pt.Y > maxY) maxY = pt.Y;
        }

        var result = new HashSet<Vector2I>();
        foreach (var c in newChunks)
        {
            float x0 = c.X * chunkPixelSize;
            float y0 = c.Y * chunkPixelSize;
            if (maxX > x0 && minX < x0 + chunkPixelSize && maxY > y0 && minY < y0 + chunkPixelSize)
                result.Add(c);
        }
        return result;
    }

    // ── Blob data access ───────────────────────────────────────────────────────

    /// <summary>Returns all terrain blob polygons. Called by NavBaker at bake time.</summary>
    public IEnumerable<Vector2[]> GetBlobPolygons()
    {
        foreach (var child in _blobContainer.GetChildren())
        {
            if (child is not StaticBody2D body) continue;
            foreach (var bodyChild in body.GetChildren())
                if (bodyChild is CollisionPolygon2D cp)
                    yield return cp.Polygon;
        }
    }

    private void RefreshDebugDraw()
    {
        if (!DebugDrawEnabled) return;
        var blobs = new List<Vector2[]>(GetBlobPolygons());
        _debugDraw.UpdateBlobs(blobs);
    }

    // ── Tile query ─────────────────────────────────────────────────────────────

    private bool IsSolid(int tileX, int tileY)
    {
        int ts = CoordConfig.TilePixelSize;
        var worldPos = new Vector2(tileX * ts + ts * 0.5f, tileY * ts + ts * 0.5f);
        TerrainType? terrain = ChunkManager.GetTerrainTypeAtWorldPos(worldPos);
        return terrain.HasValue && terrain.Value.HasCollision();
    }

    // ── Debug draw ─────────────────────────────────────────────────────────────

    private partial class DebugObstructionDraw : Node2D
    {
        private static readonly Color[] Palette =
        {
            new(1.00f, 0.30f, 0.30f), // red
            new(0.30f, 1.00f, 0.45f), // green
            new(0.30f, 0.65f, 1.00f), // blue
            new(1.00f, 0.90f, 0.20f), // yellow
            new(1.00f, 0.55f, 0.10f), // orange
            new(0.75f, 0.25f, 1.00f), // purple
            new(0.20f, 1.00f, 0.95f), // cyan
            new(1.00f, 0.25f, 0.80f), // pink
        };

        private List<Vector2[]> _blobs = new();

        public void UpdateBlobs(List<Vector2[]> blobs)
        {
            _blobs = blobs;
            QueueRedraw();
        }

        public override void _Draw()
        {
            for (int i = 0; i < _blobs.Count; i++)
            {
                var poly = _blobs[i];
                if (poly.Length < 3) continue;

                var col = Palette[i % Palette.Length];

                // Mostly transparent fill.
                DrawPolygon(poly, new[] { new Color(col, 0.45f) });

                // Opaque-ish border — close the loop manually.
                var ring = new Vector2[poly.Length + 1];
                poly.CopyTo(ring, 0);
                ring[poly.Length] = poly[0];
                DrawPolyline(ring, new Color(col, 0.75f), 2f);

                // Index label at centroid.
                var centroid = Vector2.Zero;
                foreach (var pt in poly) centroid += pt;
                centroid /= poly.Length;
                DrawString(ThemeDB.FallbackFont, centroid, i.ToString(),
                    fontSize: 140, modulate: Colors.White);

                // Vertex index labels.
                for (int v = 0; v < poly.Length; v++)
                    DrawString(ThemeDB.FallbackFont, poly[v], v.ToString(),
                        fontSize: 14, modulate: Colors.Black);
            }
        }
    }
}
