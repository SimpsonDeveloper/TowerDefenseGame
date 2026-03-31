using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace towerdefensegame;

/// <summary>
/// Incrementally builds terrain collision polygons as chunks generate, then
/// drives navigation mesh baking from those polygons.
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
///   6. Bake the navigation mesh from all blob polygons plus any nodes in the
///      "nav_obstacle" group (crystals, towers, etc.).
///
/// Requires ChunkManager.CollisionMode = Polygon (TileMapLayer collision disabled).
/// </summary>
[GlobalClass]
public partial class PolygonTerrainManager : Node
{
    /// <summary>Group tag added to every terrain-blob StaticBody2D.</summary>
    private const string BlobGroup = "terrain_blob";

    /// <summary>
    /// Nodes in this group are included as nav obstructions in addition to terrain
    /// blobs. Add crystals, towers, and any other collidable scene objects here.
    /// </summary>
    public const string NavObstacleGroup = "nav_obstacle";

    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public NavigationRegion2D NavigationRegion { get; set; }

    /// <summary>Node to centre the walkable nav boundary on (typically the camera).</summary>
    [Export] public Node2D Center { get; set; }

    /// <summary>
    /// Half-extent of the outer walkable nav boundary in pixels.
    /// Must be a multiple of TilePixelSize (16) — enforced at bake time.
    /// </summary>
    [Export] public float WalkableExtent { get; set; } = 3008f; // 188 × 16

    /// <summary>Seconds to wait after the last chunk batch before processing.</summary>
    [Export] public double DebounceDelay { get; set; } = 0.5;

    /// <summary>
    /// Physics collision layer used for terrain blob StaticBody2D nodes.
    /// Must match the player's and enemy's collision mask so they collide with terrain.
    /// Default = 1 (layer 1).
    /// </summary>
    [Export] public uint TerrainLayer { get; set; } = 1;

    /// <summary>When true, nav rebakes only happen when the player presses R. For debugging.</summary>
    [Export] public bool DebugManualRebakeOnly { get; set; } = false;

    private double _timer    = -1;
    private double _navTimer = -1;
    private Vector2 _lastBakeCenter;
    private Node2D _blobContainer;
    private DebugObstructionDraw _debugDraw;
    private Vector2? _debugCenterOverride;
    private CenterOverrideDialog _centerDialog;

    // Snapshot of the last full bake's obstructions for debug removal.
    private List<Vector2[]>  _lastBakeObstructions = new();
    private Vector2[]        _lastBakeTraversable  = System.Array.Empty<Vector2>();
    private Rect2?           _lastChunkBounds;
    private readonly HashSet<int> _debugRemovedIndices = new();
    private RemoveObstructionDialog _removeDialog;
    private bool _minimizing = false;

    // Interactive tile editing (active after RunMinimization completes).
    private List<HashSet<Vector2I>> _minTileSets    = new();
    private List<Vector2[]>         _minPolysCurrent = new();
    private readonly Stack<(int polyIdx, Vector2I tile)> _tileUndoStack = new();

    // Global minimum search.
    private bool _globalSearchRunning  = false;
    private bool _searchInterrupted    = false;

    // Chunks that have already been traced into blobs.
    private readonly HashSet<Vector2I> _processedChunks = new();

    // blob → set of chunk coords that contributed solid tiles to it.
    private readonly System.Collections.Generic.Dictionary<StaticBody2D, HashSet<Vector2I>> _blobToChunks = new();

    // chunk → blobs that contain tiles from it (reverse index for unload).
    private readonly System.Collections.Generic.Dictionary<Vector2I, List<StaticBody2D>> _chunkToBlobs = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (ChunkManager == null)   { GD.PushWarning($"{Name}: ChunkManager not assigned.");   return; }
        if (NavigationRegion == null) { GD.PushWarning($"{Name}: NavigationRegion not assigned."); return; }

        _blobContainer = new Node2D { Name = "BlobContainer" };
        AddChild(_blobContainer);

        _debugDraw = new DebugObstructionDraw();
        AddChild(_debugDraw);

        _removeDialog = new RemoveObstructionDialog();
        _removeDialog.Confirmed += OnRemoveObstructionConfirmed;
        AddChild(_removeDialog);

        _centerDialog = new CenterOverrideDialog();
        _centerDialog.Confirmed += pos => { _debugCenterOverride = pos; RebakeNav(); };
        _centerDialog.Cleared   += ()  => { _debugCenterOverride = null; RebakeNav(); };
        AddChild(_centerDialog);

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
        _timer    = -1;
        _navTimer = -1;
    }

    public override void _Process(double delta)
    {
        if (Center != null)
        {
            float threshold = ChunkManager.ChunkSize * ChunkRenderer.TilePixelSize;
            if (Center.GlobalPosition.DistanceTo(_lastBakeCenter) >= threshold)
                MarkNavDirty();
        }

        if (_timer >= 0)
        {
            _timer -= delta;
            if (_timer <= 0)
            {
                _timer = -1;
                ProcessNewChunks(); // also calls RebakeNav, so clear nav timer
                _navTimer = -1;
            }
        }

        if (_navTimer >= 0)
        {
            _navTimer -= delta;
            if (_navTimer <= 0)
            {
                _navTimer = -1;
                RebakeNav();
            }
        }
    }

    public void MarkDirty() => _timer = DebounceDelay;

    /// <summary>
    /// Schedules a nav rebake without reprocessing chunks. Use this when the
    /// nav boundary needs updating (e.g. camera moved) but terrain hasn't changed.
    /// </summary>
    public void MarkNavDirty()
    {
        if (DebugManualRebakeOnly) return;
        _navTimer = DebounceDelay;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!DebugManualRebakeOnly) return;

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left
            && _minTileSets.Count > 0)
        {
            TryRemoveTileAtMouse();
            return;
        }

        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.R) RebakeNav();
        if (key.Keycode == Key.O) _removeDialog.Open();
        if (key.Keycode == Key.T) _centerDialog.Open(_debugCenterOverride ?? new Vector2(-120, -264));
        if (key.Keycode == Key.M)
        {
            if (_globalSearchRunning) { _searchInterrupted = true; return; }
            if (!_minimizing)
            {
                if (_minTileSets.Count > 0) RunGlobalMinSearch();
                else RunMinimization();
            }
        }
        if (key.Keycode == Key.U) UndoTileRemove();
        if (key.Keycode == Key.N) LoadMinimalScenario();
    }

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

        // Find existing terrain blobs adjacent to the new chunks BEFORE tracing,
        // while the physics state still reflects only previously processed blobs.
        var adjacentBlobs = FindAdjacentBlobs(newChunks);

        // Build contour graph. Processed-chunk solid tiles are treated as non-solid
        // so each new blob forms a complete closed outline ready for MergePolygons.
        var edgeGraph = BuildEdgeGraph(newChunks);

        if (edgeGraph.Count > 0)
        {
            var newBlobPolygons = TraceContours(edgeGraph);
            int chunkPixelSize = ChunkManager.ChunkSize * ChunkRenderer.TilePixelSize;

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
                        foreach (var c in oldChunks) mergedChunks.Add(c);

                    DestroyBlob(candidate);
                    adjacentBlobs.Remove(candidate);
                }

                var newBody = CreateBlobBody(mergedPoly);
                _blobContainer.AddChild(newBody);
                RegisterBlob(newBody, mergedChunks);
            }
        }

        foreach (var c in newChunks) _processedChunks.Add(c);

        if (!DebugManualRebakeOnly)
            RebakeNav();

        // Sequential debug: notify ChunkManager that this chunk is fully processed
        // so it can queue the next one.
        if (ChunkManager.IsSequentialDebugMode)
            ChunkManager.DebugAdvanceSequential();
    }

    // ── Edge graph ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a directed edge graph for solid tile boundaries within the new chunks.
    /// Neighbours that are solid but belong to already-processed chunks are treated as
    /// non-solid, ensuring every blob in the new chunks forms a complete closed loop.
    /// Merging with processed-chunk blobs is handled later via MergePolygons.
    /// </summary>
    private System.Collections.Generic.Dictionary<Vector2I, List<Vector2I>> BuildEdgeGraph(List<Vector2I> newChunks)
    {
        var graph      = new System.Collections.Generic.Dictionary<Vector2I, List<Vector2I>>();
        int cs         = ChunkManager.ChunkSize;
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

    private static List<Vector2[]> TraceContours(System.Collections.Generic.Dictionary<Vector2I, List<Vector2I>> graph)
    {
        const int ts = ChunkRenderer.TilePixelSize;
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
                    next = PickCW(current, outgoing, prevDir);
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
                pixels[i] = new Vector2(corners[i].X * ts, corners[i].Y * ts);

            result.Add(pixels);
        }

        return result;
    }

    private static Vector2I PickCW(Vector2I current, List<Vector2I> options, Vector2I prevDir)
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

    private static Vector2I FirstKey(System.Collections.Generic.Dictionary<Vector2I, List<Vector2I>> dict)
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
        int cs         = ChunkManager.ChunkSize;
        const int ts   = ChunkRenderer.TilePixelSize;
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
                    int borderTX   = dir.X > 0 ? startX + cs - 1 : startX;
                    int neighborTX = borderTX + dir.X;
                    for (int ty = startY; ty < startY + cs; ty++)
                    {
                        if (!IsSolid(borderTX, ty) || !IsSolid(neighborTX, ty)) continue;
                        query.Position = new Vector2(neighborTX * ts + ts * 0.5f, ty * ts + ts * 0.5f);
                        foreach (var hit in spaceState.IntersectPoint(query))
                            if (hit["collider"].Obj is StaticBody2D body && body.IsInGroup(BlobGroup))
                                found.Add(body);
                    }
                }
                else
                {
                    int borderTY   = dir.Y > 0 ? startY + cs - 1 : startY;
                    int neighborTY = borderTY + dir.Y;
                    for (int tx = startX; tx < startX + cs; tx++)
                    {
                        if (!IsSolid(tx, borderTY) || !IsSolid(tx, neighborTY)) continue;
                        query.Position = new Vector2(tx * ts + ts * 0.5f, neighborTY * ts + ts * 0.5f);
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

    // ── Navigation bake ────────────────────────────────────────────────────────

    private void RebakeNav()
    {
        NavigationPolygon navPoly    = new NavigationPolygon { AgentRadius = 9f};
        // navPoly.SamplePartitionType = NavigationPolygon.SamplePartitionTypeEnum.Triangulate;
        var sourceData = new NavigationMeshSourceGeometryData2D();

        // Outer walkable boundary. WalkableExtent is rounded to the nearest tile size
        // at bake time. Center is snapped to a half-tile offset so boundary edges always
        // run through the middle of tiles — never on a terrain polygon vertex or edge.
        Vector2 c  = _debugCenterOverride ?? Center?.GlobalPosition ?? Vector2.Zero;
        float tileSize = ChunkRenderer.TilePixelSize;
        float e    = Mathf.Round(WalkableExtent / tileSize) * tileSize;
        float half = tileSize * 0.5f;
        c = new Vector2(
            Mathf.Round((c.X - half) / tileSize) * tileSize + half,
            Mathf.Round((c.Y - half) / tileSize) * tileSize + half);
        var traversable = new Vector2[]
        {
            new(c.X - e, c.Y - e),
            new(c.X + e, c.Y - e),
            new(c.X + e, c.Y + e),
            new(c.X - e, c.Y + e),
        };
        sourceData.AddTraversableOutline(traversable);

        // Terrain blob obstructions — read directly from existing StaticBody2D nodes.
        foreach (var child in _blobContainer.GetChildren())
        {
            if (child is not StaticBody2D body) continue;
            foreach (var bodyChild in body.GetChildren())
                if (bodyChild is CollisionPolygon2D cp)
                    AddClippedObstruction(sourceData, cp.Polygon, traversable);
        }

        // Collect terrain blob polygons once so we can subtract them from nav obstacles.
        var terrainPolygons = new List<Vector2[]>();
        foreach (var child in _blobContainer.GetChildren())
        {
            if (child is not StaticBody2D tb) continue;
            foreach (var tbChild in tb.GetChildren())
                if (tbChild is CollisionPolygon2D tcp)
                    terrainPolygons.Add(tcp.Polygon);
        }

        // Extra nav obstacles registered by other systems (crystals, towers, etc.).
        // Subtract all terrain blob polygons so obstacle geometry never overlaps a
        // terrain obstruction — overlapping polygons cause convex partition failures.
        foreach (var node in GetTree().GetNodesInGroup(NavObstacleGroup))
        {
            if (node is not StaticBody2D body) continue;

            foreach (var child in body.GetChildren())
            {
                if (child is not CollisionPolygon2D cp) continue;
                var xform = cp.GlobalTransform;
                var local = cp.Polygon;
                var world = new Vector2[local.Length];
                for (int i = 0; i < local.Length; i++)
                    world[i] = xform * local[i];

                // Skip any obstacle that overlaps or touches a terrain blob:
                //  - IntersectPolygons: handles normal overlap
                //  - IsPointInPolygon:  handles nesting (IntersectPolygons can miss this
                //                       when the polygon sits exactly on the boundary)
                //  - HasTIntersection:  handles a crystal vertex landing exactly on a
                //                       terrain edge (no area overlap, but still crashes
                //                       Godot's convex partitioner)
                bool overlaps = false;
                foreach (var terrainPoly in terrainPolygons)
                {
                    if (Geometry2D.IntersectPolygons(world, terrainPoly).Count > 0
                        || Geometry2D.IsPointInPolygon(world[0], terrainPoly)
                        || HasTIntersection(world, terrainPoly, out _))
                    { overlaps = true; break; }
                }
                if (overlaps) continue;

                AddClippedObstruction(sourceData, world, traversable);
            }
        }

        _lastBakeCenter = Center?.GlobalPosition ?? Vector2.Zero;
        _debugRemovedIndices.Clear();

        // Bounding box of processed chunks that overlap the traversable boundary.
        int chunkPixelSize = ChunkManager.ChunkSize * ChunkRenderer.TilePixelSize;
        float bMinX = float.MaxValue, bMinY = float.MaxValue,
              bMaxX = float.MinValue, bMaxY = float.MinValue;
        foreach (var cc in _processedChunks)
        {
            float cx0 = cc.X * chunkPixelSize, cy0 = cc.Y * chunkPixelSize;
            float cx1 = cx0 + chunkPixelSize,   cy1 = cy0 + chunkPixelSize;
            if (cx1 <= traversable[0].X || cx0 >= traversable[2].X ||
                cy1 <= traversable[0].Y || cy0 >= traversable[2].Y) continue;
            bMinX = Mathf.Min(bMinX, cx0); bMinY = Mathf.Min(bMinY, cy0);
            bMaxX = Mathf.Max(bMaxX, cx1); bMaxY = Mathf.Max(bMaxY, cy1);
        }
        Rect2? chunkBounds = bMinX < float.MaxValue
            ? new Rect2(bMinX, bMinY, bMaxX - bMinX, bMaxY - bMinY)
            : null;

        GD.Print($"[NavDebug] Rebake — center: {c}" +
            $"  nav bounds: ({traversable[0].X:F0},{traversable[0].Y:F0}) → ({traversable[2].X:F0},{traversable[2].Y:F0})" +
            $"  chunk bounds: ({bMinX:F0},{bMinY:F0}) → ({bMaxX:F0},{bMaxY:F0})");
        _lastBakeObstructions = new List<Vector2[]>(sourceData.GetObstructionOutlines());
        _lastBakeTraversable  = traversable;
        _lastChunkBounds      = chunkBounds;

        DebugDumpSourceGeometry(sourceData, traversable);
        _debugDraw?.UpdateObstructions(sourceData.GetObstructionOutlines(), traversable, chunkBounds);

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData);
        NavigationRegion.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(NavigationRegion.GetRid(), navPoly);
    }

    /// <summary>
    /// Clips an obstruction polygon to the traversable boundary before adding it
    /// to source data. Polygons entirely outside the boundary are dropped.
    /// Polygons partially outside are trimmed so Godot never sees geometry that
    /// straddles or touches the boundary edge, which causes convex partition failures.
    /// </summary>
    private static void AddClippedObstruction(
        NavigationMeshSourceGeometryData2D sourceData,
        Vector2[] poly,
        Vector2[] boundary)
    {
        var clipped = Geometry2D.IntersectPolygons(poly, boundary);
        foreach (var piece in clipped)
            sourceData.AddObstructionOutline(piece);
    }

    /// <summary>
    /// Deep interaction checks between obstruction polygons.
    /// Looks for T-intersections, actual polygon intersections, and self-intersections.
    /// Remove once root cause is identified.
    /// </summary>
    private static void DebugDumpSourceGeometry(
        NavigationMeshSourceGeometryData2D sourceData,
        Vector2[] traversable)
    {
        Array<Vector2[]> obs = sourceData.GetObstructionOutlines();
        GD.Print($"[NavDebug] === Bake begin === traversable: ({traversable[0].X:F1},{traversable[0].Y:F1}) → ({traversable[2].X:F1},{traversable[2].Y:F1})");
        GD.Print($"[NavDebug] {obs.Count} obstruction(s):");

        // Compute bboxes and print per-obstruction summary.
        (float x0, float y0, float x1, float y1)[] bboxes = new (float x0, float y0, float x1, float y1)[obs.Count];
        for (int i = 0; i < obs.Count; i++)
        {
            var p = obs[i];
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (Vector2 v in p) { x0=Mathf.Min(x0,v.X); y0=Mathf.Min(y0,v.Y); x1=Mathf.Max(x1,v.X); y1=Mathf.Max(y1,v.Y); }
            bboxes[i] = (x0, y0, x1, y1);

            // Signed area (negative = CW in y-down space).
            float area = 0f;
            for (int j = 0; j < p.Length; j++)
            {
                var a = p[j]; var b = p[(j + 1) % p.Length];
                area += (a.X * b.Y - b.X * a.Y);
            }
            area *= 0.5f;

            var verts = new System.Text.StringBuilder();
            foreach (var v in p) verts.Append($" ({v.X:F1},{v.Y:F1})");
            GD.Print($"[NavDebug]   [{i}] verts={p.Length} area={area:F1} bbox=({x0:F1},{y0:F1})→({x1:F1},{y1:F1}) |{verts}");
        }

        bool found = false;

        // 1. Self-intersection check for each polygon.
        for (int i = 0; i < obs.Count; i++)
        {
            var p = obs[i];
            int n = p.Length;
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 2; b < n; b++)
                {
                    if (a == 0 && b == n - 1) continue; // adjacent wrap
                    if (SegmentsIntersect(p[a], p[(a+1)%n], p[b], p[(b+1)%n]))
                    {
                        GD.PrintErr($"[NavDebug] SELF-INTERSECT polygon [{i}]: edge {a}→{a+1} crosses edge {b}→{b+1}  ({p[a]}→{p[(a+1)%n]}) × ({p[b]}→{p[(b+1)%n]})");
                        found = true;
                    }
                }
            }
        }

        // 2. T-intersection and polygon-intersection checks for bbox-overlapping pairs.
        for (int i = 0; i < obs.Count; i++)
        {
            for (int j = i + 1; j < obs.Count; j++)
            {
                var a = bboxes[i]; var b = bboxes[j];
                if (!(a.x1 > b.x0 && a.x0 < b.x1 && a.y1 > b.y0 && a.y0 < b.y1)) continue;

                // T-intersection: vertex of i on interior of an edge of j, or vice versa.
                if (HasTIntersection(obs[i], obs[j], out var tv) || HasTIntersection(obs[j], obs[i], out tv))
                {
                    GD.PrintErr($"[NavDebug] T-INTERSECTION [{i}]×[{j}] at ({tv.X:F2},{tv.Y:F2})");
                    found = true;
                }

                // Actual polygon intersection (edges cross).
                if (PolygonsIntersect(obs[i], obs[j]))
                {
                    GD.PrintErr($"[NavDebug] POLYGON INTERSECTION [{i}] ({a.x0:F0},{a.y0:F0})→({a.x1:F0},{a.y1:F0})  ×  [{j}] ({b.x0:F0},{b.y0:F0})→({b.x1:F0},{b.y1:F0})");
                    found = true;
                }

                // Nesting: one polygon entirely inside the other.
                if (Geometry2D.IsPointInPolygon(obs[j][0], obs[i]))
                {
                    GD.PrintErr($"[NavDebug] NESTED: [{j}] is inside [{i}]  bbox [{j}]=({b.x0:F0},{b.y0:F0})→({b.x1:F0},{b.y1:F0})");
                    found = true;
                }
                else if (Geometry2D.IsPointInPolygon(obs[i][0], obs[j]))
                {
                    GD.PrintErr($"[NavDebug] NESTED: [{i}] is inside [{j}]  bbox [{i}]=({a.x0:F0},{a.y0:F0})→({a.x1:F0},{a.y1:F0})");
                    found = true;
                }
            }
        }

        if (!found)
            GD.Print("[NavDebug] No structural issues detected — problem may be inside Godot's partitioner itself.");
    }

    /// <summary>
    /// Checks if a T-intersection exists between two polygons.
    /// A T-intersection occurs when a vertex from one polygon lies on the interior of an edge from the other polygon.
    /// </summary>
    /// <param name="polyA">First polygon (array of Vector2 points).</param>
    /// <param name="polyB">Second polygon (array of Vector2 points).</param>
    /// <param name="point">The point of intersection (the vertex from polyA) if a T-intersection is found.</param>
    /// <returns>True if a T-intersection exists, false otherwise.</returns>
    private static bool HasTIntersection(Vector2[] polyA, Vector2[] polyB, out Vector2 point)
    {
        // Epsilon value used for floating-point comparisons to account for precision errors.
        const float eps = 0.01f;
        
        // Get the number of vertices in polyB.
        int n = polyB.Length;
        
        // Iterate over each vertex in the first polygon (polyA).
        // Edges are defined by consecutive vertices, with the last edge connecting back to the first (modulo n).
        foreach (var v in polyA)
        {
            for (int i = 0; i < n; i++)
            {
                // Get the start (e0) and end (e1) points of the current edge in polyB.
                var e0 = polyB[i];
                var e1 = polyB[(i + 1) % n];
                
                // Calculate the cross product of the edge vector (e1 - e0) and the vector from e0 to the vertex (v).
                // This gives the perpendicular (or "signed") distance from the point v to the infinite line defined by the edge.
                // If the absolute value is greater than epsilon, the point is not on the line.
                float cross = (e1.X - e0.X) * (v.Y - e0.Y) - (e1.Y - e0.Y) * (v.X - e0.X);
                if (Mathf.Abs(cross) > eps) continue;
                
                // Calculate the dot product of the vector (v - e0) and the edge vector (e1 - e0).
                // This projects the point v onto the edge line. The value 'dot' represents how far along the edge the projection lies.
                float dot  = (v - e0).Dot(e1 - e0);
                
                // Calculate the squared length of the edge. This is used to avoid a costly square root operation.
                float len2 = (e1 - e0).LengthSquared();
                
                // Check if the projected point lies strictly within the interior of the edge segment.
                // This is true if the dot product is greater than a small epsilon (not at e0) and less than the squared length minus epsilon (not at e1).
                // If both conditions are met, a T-intersection is found.
                if (dot > eps && dot < len2 - eps)
                {
                    point = v; return true;
                }
            }
        }
        point = default;
        return false;
    }

    private static bool PolygonsIntersect(Vector2[] a, Vector2[] b)
    {
        int na = a.Length, nb = b.Length;
        for (int i = 0; i < na; i++)
            for (int j = 0; j < nb; j++)
                if (SegmentsIntersect(a[i], a[(i+1)%na], b[j], b[(j+1)%nb]))
                    return true;
        return false;
    }
    
    /// <summary>
    /// Determines if two 2D line segments intersect using parametric equations.
    /// </summary>
    /// <param name="p">Start point of the first segment.</param>
    /// <param name="q">End point of the first segment.</param>
    /// <param name="r">Start point of the second segment.</param>
    /// <param name="s">End point of the second segment.</param>
    /// <returns>True if the segments intersect (excluding endpoints), false otherwise.</returns>
    private static bool SegmentsIntersect(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
    {
        // Calculate the direction vectors for both segments.
        // d1 represents the vector from p to q.
        // d2 represents the vector from r to s.
        Vector2 d1 = q - p; 
        Vector2 d2 = s - r;

        // Calculate the denominator of the parametric equations.
        // This is the determinant of a matrix formed by the direction vectors d1 and d2.
        // It is proportional to the sine of the angle between the two segments.
        // If the segments are parallel or collinear, the determinant is zero (or very close to it).
        float denom = d1.X * d2.Y - d1.Y * d2.X;

        // Check if the segments are parallel (or nearly parallel).
        // A very small absolute value of the determinant indicates parallel lines.
        // We use a small epsilon (1e-6f) for floating-point comparison.
        // If parallel, the segments cannot intersect (within this function's logic) so return false.
        if (Mathf.Abs(denom) < 1e-6f) return false;

        // Calculate the vector from the start of the first segment (p) to the start of the second segment (r).
        // This vector (diff) is used to find the relative position of the segments.
        Vector2 diff = r - p;

        // Solve for the parameter 't' which represents the position along the first segment (p->q).
        // The formula is derived from the parametric equation of the lines.
        // t = (diff x d2) / (d1 x d2), where 'x' is the 2D cross product (perp-dot product).
        // If t is between 0 and 1, the intersection point lies on the first segment.
        float t = (diff.X * d2.Y - diff.Y * d2.X) / denom;

        // Solve for the parameter 'u' which represents the position along the second segment (r->s).
        // The formula is derived similarly: u = (diff x d1) / (d1 x d2).
        // If u is between 0 and 1, the intersection point lies on the second segment.
        float u = (diff.X * d1.Y - diff.Y * d1.X) / denom;

        // Check if both parameters t and u are within the (0,1) range.
        // The epsilon (1e-6f) is subtracted from 1 to ensure the intersection is not at the endpoints.
        // This function returns true only if the segments intersect at an interior point.
        return t > 1e-6f && t < 1f - 1e-6f && u > 1e-6f && u < 1f - 1e-6f;
    }

    // ── Debug obstruction removal ──────────────────────────────────────────────

    private void OnRemoveObstructionConfirmed(List<int> indices)
    {
        foreach (var index in indices)
        {
            if (index < 0 || index >= _lastBakeObstructions.Count)
                GD.PrintErr($"[NavDebug] Remove: index {index} out of range (0–{_lastBakeObstructions.Count - 1})");
            else
                _debugRemovedIndices.Add(index);
        }
        GD.Print($"[NavDebug] Removed [{string.Join(", ", indices)}] — total removed: [{string.Join(", ", _debugRemovedIndices)}]");
        RebakeNavWithRemovals();
    }

    private void RebakeNavWithRemovals()
    {
        var navPoly    = new NavigationPolygon { AgentRadius = 9f };
        var sourceData = new NavigationMeshSourceGeometryData2D();
        sourceData.AddTraversableOutline(_lastBakeTraversable);

        int kept = 0;
        for (int i = 0; i < _lastBakeObstructions.Count; i++)
        {
            if (_debugRemovedIndices.Contains(i)) continue;
            sourceData.AddObstructionOutline(_lastBakeObstructions[i]);
            kept++;
        }

        GD.Print($"[NavDebug] Rebaking — {kept} obstructions remaining.");
        _debugDraw?.UpdateObstructions(sourceData.GetObstructionOutlines(), _lastBakeTraversable, _lastChunkBounds);

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData);
        NavigationRegion.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(NavigationRegion.GetRid(), navPoly);
    }

    // ── Minimization ──────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 1: greedily drops whole polygons while failure persists.
    /// Phase 2: for each remaining polygon, greedily drops vertices (min 3) while failure persists.
    /// Updates the debug view with the final minimal geometry and prints each polygon's vertices.
    /// Press M to trigger (requires DebugManualRebakeOnly).
    /// </summary>
    private void RunMinimization()
    {
        if (_lastBakeObstructions.Count == 0)
        {
            GD.PrintErr("[NavMin] No obstructions recorded — run a full rebake first (R).");
            return;
        }

        var allIndices = new List<int>();
        for (int i = 0; i < _lastBakeObstructions.Count; i++) allIndices.Add(i);

        if (!BakeSubsetFails(allIndices))
        {
            GD.PrintErr("[NavMin] Full set does not trigger failure — nothing to minimize.");
            return;
        }

        _minimizing = true;
        GD.Print($"[NavMin] Phase 1: minimizing polygons ({allIndices.Count} total)...");

        // ── Phase 1: drop whole polygons ──────────────────────────────────────
        var current = new List<int>(allIndices);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = current.Count - 1; i >= 0; i--)
            {
                var candidate = new List<int>(current);
                candidate.RemoveAt(i);
                if (candidate.Count == 0) continue;
                if (BakeSubsetFails(candidate))
                {
                    current = candidate;
                    changed = true;
                    break;
                }
            }
        }
        GD.Print($"[NavMin] Phase 1 done: {current.Count} polygons remain.");

        // ── Phase 2: tile-based polygon minimization ─────────────────────────
        // Convert each polygon to its tile set, then greedily remove boundary
        // tiles one at a time. Rebuilds a clean rectilinear polygon after each
        // removal — never produces triangles, slivers, or self-intersections.
        GD.Print("[NavMin] Phase 2: minimizing tiles per polygon...");
        var minPolys = new List<Vector2[]>();
        foreach (int idx in current)
            minPolys.Add((Vector2[])_lastBakeObstructions[idx].Clone());

        for (int p = 0; p < minPolys.Count; p++)
        {
            var tiles = PolygonToTiles(minPolys[p]);
            GD.Print($"[NavMin]   Poly {p}: {minPolys[p].Length} verts, {tiles.Count} tiles");
            changed = true;
            while (changed && tiles.Count > 1)
            {
                changed = false;
                foreach (var tile in GetBoundaryTiles(tiles))
                {
                    var candidate = new HashSet<Vector2I>(tiles);
                    candidate.Remove(tile);
                    if (!IsTileSetConnected(candidate)) continue;
                    var candidatePoly = TilesToPolygon(candidate);
                    if (candidatePoly == null) continue;
                    var testPolys = new List<Vector2[]>(minPolys);
                    testPolys[p] = candidatePoly;
                    if (BakePolysFails(testPolys))
                    {
                        tiles      = candidate;
                        minPolys[p] = candidatePoly;
                        changed    = true;
                        break;
                    }
                }
            }
        }

        // ── Save interactive state ────────────────────────────────────────────
        _minPolysCurrent = minPolys;
        _minTileSets.Clear();
        foreach (var poly in minPolys)
            _minTileSets.Add(PolygonToTiles(poly));
        _tileUndoStack.Clear();

        // ── Report ────────────────────────────────────────────────────────────
        GD.Print($"[NavMin] Done. {minPolys.Count} polygon(s) — click a tile to remove it, U to undo:");
        for (int p = 0; p < minPolys.Count; p++)
        {
            var verts = string.Join(", ", System.Array.ConvertAll(minPolys[p], v => $"({v.X:F0},{v.Y:F0})"));
            GD.Print($"  [{p}] {_minTileSets[p].Count} tiles, {minPolys[p].Length} verts: {verts}");
        }

        // ── Update debug view ─────────────────────────────────────────────────
        _lastBakeObstructions = minPolys;
        _debugRemovedIndices.Clear();
        ApplyMinPolyState();

        _minimizing = false;
    }

    // ── Interactive tile editing ───────────────────────────────────────────────

    private void TryRemoveTileAtMouse()
    {
        const int ts = ChunkRenderer.TilePixelSize;
        var worldPos  = _debugDraw.GetGlobalMousePosition();
        var tileCoord = new Vector2I(Mathf.FloorToInt(worldPos.X / ts), Mathf.FloorToInt(worldPos.Y / ts));

        int polyIdx = -1;
        for (int p = 0; p < _minTileSets.Count; p++)
            if (_minTileSets[p].Contains(tileCoord)) { polyIdx = p; break; }

        if (polyIdx < 0)
        {
            GD.Print($"[TileEdit] No polygon at tile {tileCoord}");
            return;
        }

        var candidate = new HashSet<Vector2I>(_minTileSets[polyIdx]);
        candidate.Remove(tileCoord);

        if (candidate.Count == 0)
        {
            GD.Print($"[TileEdit] Cannot remove the last tile from poly {polyIdx}");
            return;
        }

        if (!IsTileSetConnected(candidate))
        {
            GD.Print($"[TileEdit] Tile {tileCoord} is a bridge in poly {polyIdx} — removal would disconnect it");
            return;
        }

        var newPoly = TilesToPolygon(candidate);
        if (newPoly == null)
        {
            GD.Print($"[TileEdit] Polygon rebuild failed for poly {polyIdx}");
            return;
        }

        _tileUndoStack.Push((polyIdx, tileCoord));
        _minTileSets[polyIdx]    = candidate;
        _minPolysCurrent[polyIdx] = newPoly;

        GD.Print($"[TileEdit] Removed tile {tileCoord} from poly {polyIdx} ({candidate.Count} tiles remaining, U to undo)");
        ApplyMinPolyState();
    }

    private void UndoTileRemove()
    {
        if (_tileUndoStack.Count == 0)
        {
            GD.Print("[TileEdit] Nothing to undo");
            return;
        }
        var (polyIdx, tile) = _tileUndoStack.Pop();
        _minTileSets[polyIdx].Add(tile);
        var restored = TilesToPolygon(_minTileSets[polyIdx]);
        if (restored != null) _minPolysCurrent[polyIdx] = restored;
        GD.Print($"[TileEdit] Undo: restored tile {tile} to poly {polyIdx} ({_minTileSets[polyIdx].Count} tiles, {_tileUndoStack.Count} undos remain)");
        ApplyMinPolyState();
    }

    private void ApplyMinPolyState()
    {
        var source = new NavigationMeshSourceGeometryData2D();
        source.AddTraversableOutline(_lastBakeTraversable);
        foreach (var poly in _minPolysCurrent)
            source.AddObstructionOutline(poly);

        _lastBakeObstructions = new List<Vector2[]>(_minPolysCurrent);
        _debugDraw?.UpdateObstructions(source.GetObstructionOutlines(), _lastBakeTraversable, _lastChunkBounds);

        var navPoly = new NavigationPolygon { AgentRadius = 9f};
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, source);
        NavigationRegion.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(NavigationRegion.GetRid(), navPoly);

        if (navPoly.GetPolygonCount() == 0)
        {
            GD.Print("[TileEdit] Bake FAILED — current polygons:");
            for (int p = 0; p < _minPolysCurrent.Count; p++)
            {
                var verts = string.Join(", ", System.Array.ConvertAll(_minPolysCurrent[p], v => $"({v.X:F1},{v.Y:F1})"));
                GD.Print($"  [{p}] {_minPolysCurrent[p].Length} verts: {verts}");
            }
        }
        else
        {
            GD.Print("[TileEdit] Bake OK");
        }
    }

    // ── Hardcoded minimal scenario ────────────────────────────────────────────

    /// <summary>
    /// N key: loads the known-minimal 3-polygon failing scenario directly,
    /// bypassing chunk generation and the minimizer entirely.
    /// </summary>
    private void LoadMinimalScenario()
    {
        var polys = new List<Vector2[]>
        {
            new Vector2[] { new(-720,-224), new(-704,-224), new(-704,-208), new(-720,-208) },
            new Vector2[] { new(-688,-192), new(-624,-192), new(-624,-160), new(-640,-160), new(-640,-176), new(-688,-176) },
            new Vector2[] { new(480,-208),  new(496,-208),  new(496,-192),  new(480,-192)  },
        };

        _minPolysCurrent = polys;
        _minTileSets.Clear();
        foreach (var poly in polys) _minTileSets.Add(PolygonToTiles(poly));
        _tileUndoStack.Clear();

        GD.Print("[NavMin] Loaded minimal scenario — calling bake.");
        ApplyMinPolyState();
    }

    // ── Global minimum search ──────────────────────────────────────────────────

    /// <summary>
    /// Enumerates every valid tile-subset combination across all polygons and finds
    /// the one with the fewest total vertices that still fails. Press M to interrupt;
    /// best result found so far is printed and applied on interrupt or completion.
    /// </summary>
    private async void RunGlobalMinSearch()
    {
        if (_minTileSets.Count == 0) { GD.PrintErr("[Search] No tile sets — run M (minimization) first."); return; }
        _globalSearchRunning = true;
        _searchInterrupted   = false;

        int N = _minTileSets.Count;
        var validSubsets = new List<(HashSet<Vector2I> tiles, Vector2[] poly)>[N];
        for (int p = 0; p < N; p++)
        {
            validSubsets[p] = GetValidSubsets(_minTileSets[p]);
            GD.Print($"[Search] Poly {p}: {_minTileSets[p].Count} tiles → {validSubsets[p].Count} valid subsets");
        }

        long total = 1;
        foreach (var sv in validSubsets) total *= sv.Count;
        GD.Print($"[Search] {total:N0} combinations to test. Press M to interrupt.");

        int bestVerts = int.MaxValue;
        List<Vector2[]> bestPolys = null;
        long tested = 0;
        var indices = new int[N];

        while (true)
        {
            if (_searchInterrupted) break;

            var testPolys = new List<Vector2[]>(N);
            for (int p = 0; p < N; p++) testPolys.Add(validSubsets[p][indices[p]].poly);

            if (BakePolysFails(testPolys))
            {
                int verts = 0; foreach (var poly in testPolys) verts += poly.Length;
                if (verts < bestVerts) { bestVerts = verts; bestPolys = new List<Vector2[]>(testPolys); }
            }

            tested++;
            if (tested % 1000 == 0)
            {
                GD.Print($"[Search] {tested:N0}/{total:N0} tested — best so far: {bestVerts} verts");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // Increment index counter.
            bool carry = true;
            for (int p = N - 1; p >= 0 && carry; p--)
            {
                indices[p]++;
                if (indices[p] >= validSubsets[p].Count) indices[p] = 0;
                else carry = false;
            }
            if (carry) break; // all combinations exhausted
        }

        PrintAndApplyBest(bestPolys, bestVerts, tested, _searchInterrupted);
        _globalSearchRunning = false;
    }

    private void PrintAndApplyBest(List<Vector2[]> bestPolys, int bestVerts, long tested, bool interrupted)
    {
        GD.Print($"[Search] {(interrupted ? "Interrupted" : "Done")} after {tested:N0} combinations tested.");
        if (bestPolys == null) { GD.Print("[Search] No failing combination found."); return; }
        GD.Print($"[Search] Best: {bestVerts} total verts:");
        for (int p = 0; p < bestPolys.Count; p++)
        {
            var verts = string.Join(", ", System.Array.ConvertAll(bestPolys[p], v => $"({v.X:F1},{v.Y:F1})"));
            GD.Print($"  [{p}] {bestPolys[p].Length} verts: {verts}");
        }
        _minPolysCurrent = bestPolys;
        _minTileSets.Clear();
        foreach (var poly in bestPolys) _minTileSets.Add(PolygonToTiles(poly));
        _tileUndoStack.Clear();
        ApplyMinPolyState();
    }

    /// <summary>
    /// Returns all non-empty, connected tile subsets that produce a valid polygon.
    /// Uses bitmask enumeration — only suitable for tile sets up to ~25 tiles.
    /// </summary>
    private static List<(HashSet<Vector2I> tiles, Vector2[] poly)> GetValidSubsets(HashSet<Vector2I> tileSet)
    {
        var arr = tileSet.ToArray();
        int n = arr.Length;
        if (n > 25)
        {
            GD.PrintErr($"[Search] Polygon has {n} tiles — too many for exhaustive enumeration (max 25). Reduce further with clicks first.");
            // Return just the full set as the only option so the search still runs with the other polygons.
            var full = new HashSet<Vector2I>(tileSet);
            var fullPoly = TilesToPolygon(full);
            return fullPoly != null
                ? new List<(HashSet<Vector2I>, Vector2[])> { (full, fullPoly) }
                : new List<(HashSet<Vector2I>, Vector2[])>();
        }

        var result = new List<(HashSet<Vector2I>, Vector2[])>();
        long count = 1L << n;
        for (long mask = 1; mask < count; mask++)
        {
            var subset = new HashSet<Vector2I>();
            for (int i = 0; i < n; i++)
                if ((mask & (1L << i)) != 0) subset.Add(arr[i]);
            if (!IsTileSetConnected(subset)) continue;
            var poly = TilesToPolygon(subset);
            if (poly != null) result.Add((subset, poly));
        }
        return result;
    }

    /// <summary>Returns all tile coords whose center falls inside <paramref name="poly"/>.</summary>
    private static HashSet<Vector2I> PolygonToTiles(Vector2[] poly)
    {
        const int ts = ChunkRenderer.TilePixelSize;
        float minX = float.MaxValue, minY = float.MaxValue,
              maxX = float.MinValue, maxY = float.MinValue;
        foreach (var v in poly)
        {
            minX = Mathf.Min(minX, v.X); minY = Mathf.Min(minY, v.Y);
            maxX = Mathf.Max(maxX, v.X); maxY = Mathf.Max(maxY, v.Y);
        }
        var tiles = new HashSet<Vector2I>();
        int x0 = Mathf.FloorToInt(minX / ts), y0 = Mathf.FloorToInt(minY / ts);
        int x1 = Mathf.CeilToInt(maxX  / ts), y1 = Mathf.CeilToInt(maxY  / ts);
        for (int x = x0; x < x1; x++)
        for (int y = y0; y < y1; y++)
        {
            var center = new Vector2((x + 0.5f) * ts, (y + 0.5f) * ts);
            if (Geometry2D.IsPointInPolygon(center, poly))
                tiles.Add(new Vector2I(x, y));
        }
        return tiles;
    }

    /// <summary>
    /// Builds a rectilinear polygon outline from a connected tile set.
    /// Returns null if the edge graph doesn't form a single closed loop.
    /// Collinear vertices are removed so only corners remain.
    /// </summary>
    private static Vector2[] TilesToPolygon(HashSet<Vector2I> tiles)
    {
        const int ts = ChunkRenderer.TilePixelSize;

        // Each tile contributes 4 directed edges (CCW in screen space).
        // Shared edges between adjacent tiles appear in opposite directions and cancel.
        var edgeSet = new HashSet<(Vector2I, Vector2I)>();
        foreach (var tile in tiles)
        {
            var tl = new Vector2I( tile.X      * ts,  tile.Y      * ts);
            var tr = new Vector2I((tile.X + 1) * ts,  tile.Y      * ts);
            var br = new Vector2I((tile.X + 1) * ts, (tile.Y + 1) * ts);
            var bl = new Vector2I( tile.X      * ts, (tile.Y + 1) * ts);
            foreach (var e in new[] { (tl, tr), (tr, br), (br, bl), (bl, tl) })
                if (!edgeSet.Remove((e.Item2, e.Item1)))
                    edgeSet.Add(e);
        }

        // Build adjacency map for boundary edges only.
        var next = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
        foreach (var (from, to) in edgeSet)
            next[from] = to;

        if (next.Count == 0) return null;

        // Walk the loop.
        var start   = next.Keys.First();
        var outline = new List<Vector2I>();
        var cur     = start;
        for (int guard = next.Count + 1; guard > 0; guard--)
        {
            outline.Add(cur);
            if (!next.TryGetValue(cur, out cur)) return null;
            if (cur == start) break;
        }
        if (cur != start) return null;

        // Remove collinear vertices (keep only corners).
        var corners = new List<Vector2>();
        int n = outline.Count;
        for (int i = 0; i < n; i++)
        {
            var prev = outline[(i - 1 + n) % n];
            var c    = outline[i];
            var nxt  = outline[(i + 1) % n];
            // Cross product == 0 means collinear — skip.
            var d1 = new Vector2(c.X - prev.X, c.Y - prev.Y);
            var d2 = new Vector2(nxt.X - c.X,  nxt.Y - c.Y);
            if (d1.X * d2.Y - d1.Y * d2.X != 0f)
                corners.Add(new Vector2(c.X, c.Y));
        }
        return corners.Count >= 3 ? corners.ToArray() : null;
    }

    /// <summary>Returns tiles that have at least one neighbour not in the set.</summary>
    private static HashSet<Vector2I> GetBoundaryTiles(HashSet<Vector2I> tiles)
    {
        var boundary = new HashSet<Vector2I>();
        foreach (var t in tiles)
            if (!tiles.Contains(t + Vector2I.Up)    || !tiles.Contains(t + Vector2I.Down) ||
                !tiles.Contains(t + Vector2I.Left)  || !tiles.Contains(t + Vector2I.Right))
                boundary.Add(t);
        return boundary;
    }

    /// <summary>Returns true if all tiles in the set are reachable from the first.</summary>
    private static bool IsTileSetConnected(HashSet<Vector2I> tiles)
    {
        if (tiles.Count <= 1) return true;
        var start   = tiles.First();
        var visited = new HashSet<Vector2I> { start };
        var queue   = new Queue<Vector2I>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var nb in new[] { t + Vector2I.Up, t + Vector2I.Down, t + Vector2I.Left, t + Vector2I.Right })
                if (tiles.Contains(nb) && visited.Add(nb))
                    queue.Enqueue(nb);
        }
        return visited.Count == tiles.Count;
    }

    /// <summary>Silent bake of a subset by index; returns true on failure (0 polygons produced).</summary>
    private bool BakeSubsetFails(List<int> keepIndices)
    {
        var polys = new List<Vector2[]>(keepIndices.Count);
        foreach (int i in keepIndices) polys.Add(_lastBakeObstructions[i]);
        return BakePolysFails(polys);
    }

    /// <summary>Silent bake of explicit polygon arrays; returns true on failure (0 polygons produced).</summary>
    private bool BakePolysFails(List<Vector2[]> polys)
    {
        var navPoly    = new NavigationPolygon { AgentRadius = 9f };
        var sourceData = new NavigationMeshSourceGeometryData2D();
        sourceData.AddTraversableOutline(_lastBakeTraversable);
        foreach (var poly in polys)
            sourceData.AddObstructionOutline(poly);
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData);
        return navPoly.GetPolygonCount() == 0;
    }

    // ── Tile query ─────────────────────────────────────────────────────────────

    private bool IsSolid(int tileX, int tileY)
    {
        const int ts = ChunkRenderer.TilePixelSize;
        var worldPos = new Vector2(tileX * ts + ts * 0.5f, tileY * ts + ts * 0.5f);
        TerrainType? terrain = ChunkManager.GetTerrainTypeAtWorldPos(worldPos);
        return terrain.HasValue && terrain.Value.HasCollision();
    }

    // ── Debug UI ───────────────────────────────────────────────────────────────

    private partial class RemoveObstructionDialog : CanvasLayer
    {
        public event System.Action<List<int>> Confirmed;
        private LineEdit _lineEdit;

        public override void _Ready()
        {
            var panel = new Panel();
            panel.AnchorLeft   = 0.5f; panel.AnchorRight  = 0.5f;
            panel.AnchorTop    = 0.5f; panel.AnchorBottom = 0.5f;
            panel.OffsetLeft   = -170f; panel.OffsetRight  =  170f;
            panel.OffsetTop    =  -65f; panel.OffsetBottom =   65f;

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left",   14);
            margin.AddThemeConstantOverride("margin_right",  14);
            margin.AddThemeConstantOverride("margin_top",    12);
            margin.AddThemeConstantOverride("margin_bottom", 12);

            var vbox   = new VBoxContainer();
            var label  = new Label { Text = "Remove obstructions (comma-separated):" };
            _lineEdit  = new LineEdit { PlaceholderText = "e.g. 0, 3, 5" };

            var hbox   = new HBoxContainer();
            var accept = new Button { Text = "Accept" };
            var cancel = new Button { Text = "Cancel" };
            accept.Pressed += OnAccept;
            cancel.Pressed += () => Visible = false;
            hbox.AddChild(accept);
            hbox.AddChild(cancel);

            vbox.AddChild(label);
            vbox.AddChild(_lineEdit);
            vbox.AddChild(hbox);
            margin.AddChild(vbox);
            panel.AddChild(margin);
            AddChild(panel);

            Visible = false;
        }

        public void Open()
        {
            _lineEdit.Text = "";
            Visible = true;
            _lineEdit.GrabFocus();
        }

        private void OnAccept()
        {
            var indices = new List<int>();
            foreach (var part in _lineEdit.Text.Split(','))
                if (int.TryParse(part.Trim(), out int idx))
                    indices.Add(idx);
            if (indices.Count > 0)
            {
                Visible = false;
                Confirmed?.Invoke(indices);
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible) return;
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode is Key.Enter or Key.KpEnter) OnAccept();
                else if (key.Keycode == Key.Escape) Visible = false;
            }
        }
    }

    // ── Debug center override ──────────────────────────────────────────────────

    private partial class CenterOverrideDialog : CanvasLayer
    {
        public event System.Action<Vector2> Confirmed;
        public event System.Action Cleared;
        private LineEdit _lineEdit;

        public override void _Ready()
        {
            var panel = new Panel();
            panel.AnchorLeft   = 0.5f; panel.AnchorRight  = 0.5f;
            panel.AnchorTop    = 0.5f; panel.AnchorBottom = 0.5f;
            panel.OffsetLeft   = -180f; panel.OffsetRight  =  180f;
            panel.OffsetTop    =  -60f; panel.OffsetBottom =   60f;

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left",   14);
            margin.AddThemeConstantOverride("margin_right",  14);
            margin.AddThemeConstantOverride("margin_top",    12);
            margin.AddThemeConstantOverride("margin_bottom", 12);

            var vbox   = new VBoxContainer();
            var label  = new Label { Text = "Rebake center (x, y):" };
            _lineEdit  = new LineEdit();

            var hbox   = new HBoxContainer();
            var accept = new Button { Text = "Accept" };
            var clear  = new Button { Text = "Clear Override" };
            var cancel = new Button { Text = "Cancel" };
            accept.Pressed += OnAccept;
            clear.Pressed  += () => { Visible = false; Cleared?.Invoke(); };
            cancel.Pressed += () => Visible = false;
            hbox.AddChild(accept);
            hbox.AddChild(clear);
            hbox.AddChild(cancel);

            vbox.AddChild(label);
            vbox.AddChild(_lineEdit);
            vbox.AddChild(hbox);
            margin.AddChild(vbox);
            panel.AddChild(margin);
            AddChild(panel);

            Visible = false;
        }

        public void Open(Vector2 current)
        {
            _lineEdit.Text = $"{current.X:F0}, {current.Y:F0}";
            Visible = true;
            _lineEdit.GrabFocus();
            _lineEdit.SelectAll();
        }

        private void OnAccept()
        {
            var parts = _lineEdit.Text.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y))
            {
                Visible = false;
                Confirmed?.Invoke(new Vector2(x, y));
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible) return;
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode is Key.Enter or Key.KpEnter) OnAccept();
                else if (key.Keycode == Key.Escape) Visible = false;
            }
        }
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

        private Godot.Collections.Array<Vector2[]> _obstructions = new();
        private Vector2[] _boundary = System.Array.Empty<Vector2>();
        private Rect2? _chunkBounds;

        public void UpdateObstructions(Godot.Collections.Array<Vector2[]> obstructions, Vector2[] boundary, Rect2? chunkBounds)
        {
            _obstructions = obstructions;
            _boundary     = boundary;
            _chunkBounds  = chunkBounds;
            QueueRedraw();
        }

        public override void _Draw()
        {
            // Nav boundary — always drawn, white opaque, unfilled.
            if (_boundary.Length >= 4)
            {
                var ring = new Vector2[_boundary.Length + 1];
                _boundary.CopyTo(ring, 0);
                ring[_boundary.Length] = _boundary[0];
                DrawPolyline(ring, Colors.White, 9f);
            }

            // Chunk extent within the nav boundary — yellow, unfilled.
            if (_chunkBounds.HasValue)
            {
                var r = _chunkBounds.Value;
                DrawPolyline(new[]
                {
                    r.Position,
                    new Vector2(r.End.X,   r.Position.Y),
                    r.End,
                    new Vector2(r.Position.X, r.End.Y),
                    r.Position,
                }, new Color(1f, 0.9f, 0f), 6f);
            }

            for (int i = 0; i < _obstructions.Count; i++)
            {
                var poly = _obstructions[i];
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
