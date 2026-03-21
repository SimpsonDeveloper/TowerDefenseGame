using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Builds and maintains the NavigationPolygon using contour tracing instead
/// of rectangular decomposition. Each connected solid region (rock, water) is
/// described by exactly one obstruction outline — its outer boundary polygon —
/// regardless of shape. This eliminates shared edges between touching rectangles
/// and removes the "more than 2 edges in rasterization space" warning.
///
/// How contour tracing works:
///   For every solid tile, check its 4 neighbours. Each solid→non-solid boundary
///   is a directed edge segment oriented clockwise around the solid region.
///   These directed edges form closed loops — one loop per connected solid blob.
///   Consecutive collinear segments are merged into one, reducing vertex count.
///   Each loop is added as one AddObstructionOutline call.
///
/// Why this beats rectangular decomposition:
///   Rectangles require multiple touching rects for non-rectangular shapes (L,
///   T, irregular). Those rects share edges → navigation warning. A contour is
///   a single polygon whose edges never touch other contour polygons.
/// </summary>
[GlobalClass]
public partial class NavContourRegionManager : Node
{
    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public NavigationRegion2D NavigationRegion { get; set; }

    /// <summary>
    /// Node to centre the walkable boundary on each rebuild (typically the camera
    /// or player). Follows the player through an infinite world.
    /// </summary>
    [Export] public Node2D Center { get; set; }

    /// <summary>Half-extent of the outer walkable boundary in pixels.</summary>
    [Export] public float WalkableExtent { get; set; } = 3000f;

    /// <summary>Seconds to wait after the last chunk batch before rebuilding.</summary>
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

    public void MarkDirty() => _timer = DebounceDelay;

    // ── Internal ──────────────────────────────────────────────────────────

    private void OnChunksBatchApplied(int count) => MarkDirty();

    private void RebuildPolygon()
    {
        if (NavigationRegion == null || ChunkManager == null) return;

        var chunks = ChunkManager.GetGeneratedChunks();
        if (chunks.Count == 0) return;

        var navPoly   = new NavigationPolygon();
        navPoly.AgentRadius = 8f;

        var sourceData = new NavigationMeshSourceGeometryData2D();

        // Outer walkable boundary centred on the camera/player.
        Vector2 c = Center?.GlobalPosition ?? Vector2.Zero;
        float e = WalkableExtent;
        sourceData.AddTraversableOutline([
            new(c.X - e, c.Y - e), new(c.X + e, c.Y - e),
            new(c.X + e, c.Y + e), new(c.X - e, c.Y + e)
        ]);

        // Build a directed edge graph from tile boundary segments, then trace
        // closed loops to produce one obstruction outline per solid blob.
        var edgeGraph = BuildEdgeGraph(chunks);
        TraceContours(edgeGraph, sourceData);

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, sourceData);
        NavigationRegion.NavigationPolygon = navPoly;
        NavigationServer2D.RegionSetNavigationPolygon(NavigationRegion.GetRid(), navPoly);
    }

    // ── Edge graph ────────────────────────────────────────────────────────

    /// <summary>
    /// For every solid tile, emit a directed edge segment for each face that
    /// borders a non-solid tile (or unloaded space). Edges are oriented clockwise
    /// around the solid region in y-down screen space, so the solid is always
    /// to the RIGHT of the direction of travel.
    ///
    /// Corners are in "tile-corner" space: corner (cx, cy) maps to pixel
    /// (cx * TilePixelSize, cy * TilePixelSize).
    ///
    /// CW winding rules in y-down space:
    ///   non-solid above  →  top edge    : (tx,   ty  ) → (tx+1, ty  )  [RIGHT]
    ///   non-solid right  →  right edge  : (tx+1, ty  ) → (tx+1, ty+1)  [DOWN]
    ///   non-solid below  →  bottom edge : (tx+1, ty+1) → (tx,   ty+1)  [LEFT]
    ///   non-solid left   →  left edge   : (tx,   ty+1) → (tx,   ty  )  [UP]
    /// </summary>
    private Dictionary<Vector2I, List<Vector2I>> BuildEdgeGraph(
        IReadOnlyCollection<Vector2I> chunks)
    {
        var graph = new Dictionary<Vector2I, List<Vector2I>>();
        int cs = ChunkManager.ChunkSize;

        void AddEdge(Vector2I start, Vector2I end)
        {
            if (!graph.TryGetValue(start, out var list))
                graph[start] = list = new List<Vector2I>();
            list.Add(end);
        }

        foreach (var chunkCoord in chunks)
        {
            int startX = chunkCoord.X * cs;
            int startY = chunkCoord.Y * cs;

            for (int ty = startY; ty < startY + cs; ty++)
            {
                for (int tx = startX; tx < startX + cs; tx++)
                {
                    if (!IsSolid(tx, ty)) continue;

                    if (!IsSolid(tx, ty - 1)) AddEdge(new(tx,     ty    ), new(tx + 1, ty    ));
                    if (!IsSolid(tx + 1, ty)) AddEdge(new(tx + 1, ty    ), new(tx + 1, ty + 1));
                    if (!IsSolid(tx, ty + 1)) AddEdge(new(tx + 1, ty + 1), new(tx,     ty + 1));
                    if (!IsSolid(tx - 1, ty)) AddEdge(new(tx,     ty + 1), new(tx,     ty    ));
                }
            }
        }

        return graph;
    }

    // ── Contour tracing ───────────────────────────────────────────────────

    /// <summary>
    /// Walks all closed loops in the edge graph. Each loop is one contour polygon.
    /// Collinear consecutive corners are merged before the outline is emitted.
    /// When a corner has two outgoing edges (two solid blobs touch at a single
    /// corner point), the right-hand rule picks the edge that continues CW.
    /// </summary>
    private static void TraceContours(
        Dictionary<Vector2I, List<Vector2I>> graph,
        NavigationMeshSourceGeometryData2D sourceData)
    {
        const int ts = ChunkRenderer.TilePixelSize;
        var corners  = new List<Vector2I>();

        while (graph.Count > 0)
        {
            // Pick any untraversed edge as the loop start.
            var startCorner = FirstKey(graph);
            corners.Clear();

            var     current = startCorner;
            Vector2I prevDir = Vector2I.Zero;

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
                    // Two solid blobs share this corner. Continue CW: prefer right
                    // turn, then straight, then left relative to the arrival direction.
                    next = PickCW(current, outgoing, prevDir);
                    outgoing.Remove(next);
                    if (outgoing.Count == 0)
                        graph.Remove(current);
                }

                prevDir = next - current;
                current = next;
            }
            while (current != startCorner);

            // Remove corners that lie on a straight run between two others — they
            // add vertices without adding information.
            Simplify(corners);

            // Convert tile-corner coords to pixel world coords for Godot.
            var pixels = new Vector2[corners.Count];
            for (int i = 0; i < corners.Count; i++)
                pixels[i] = new Vector2(corners[i].X * ts, corners[i].Y * ts);

            sourceData.AddObstructionOutline([.. pixels]);
        }
    }

    /// <summary>
    /// Among the outgoing edges from <paramref name="current"/>, pick the one
    /// whose direction is closest to a right turn from <paramref name="prevDir"/>.
    /// Priority: right turn > straight > left turn.
    /// </summary>
    private static Vector2I PickCW(
        Vector2I current, List<Vector2I> options, Vector2I prevDir)
    {
        if (prevDir == Vector2I.Zero)
            return options[0];

        // CW (right) rotation in y-down screen space: (dx, dy) → (-dy, dx)
        var rightDir = new Vector2I(-prevDir.Y,  prevDir.X);
        var leftDir  = new Vector2I( prevDir.Y, -prevDir.X);

        foreach (var priority in new[] { rightDir, prevDir, leftDir })
            foreach (var opt in options)
                if (opt - current == priority)
                    return opt;

        return options[0];
    }

    /// <summary>
    /// Removes any corner B where A→B→C are all on the same horizontal or
    /// vertical line. Iterates until stable.
    /// </summary>
    private static void Simplify(List<Vector2I> corners)
    {
        bool changed = true;
        while (changed && corners.Count > 2)
        {
            changed = false;
            for (int i = 0; i < corners.Count; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % corners.Count];
                var cc = corners[(i + 2) % corners.Count];

                bool collinear = (a.X == b.X && b.X == cc.X)
                              || (a.Y == b.Y && b.Y == cc.Y);
                if (collinear)
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

    // ── Tile query ────────────────────────────────────────────────────────

    private bool IsSolid(int tileX, int tileY)
    {
        const int ts = ChunkRenderer.TilePixelSize;
        var worldPos = new Vector2(tileX * ts + ts * 0.5f, tileY * ts + ts * 0.5f);
        TerrainType? terrain = ChunkManager.GetTerrainTypeAtWorldPos(worldPos);
        return terrain.HasValue && TerrainTypeExtensions.HasCollision(terrain.Value);
    }
}
