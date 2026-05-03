using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.terrain;
using towerdefensegame.scripts.towers;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Hierarchical reachability index for the pocket dimension. Partitions the
/// bounded world into chunks; within each chunk, walkable tiles are flood-filled
/// into "areas" (connected components). A union-find over areas — built from
/// shared chunk borders — answers IsReachable(from, to) in O(α).
///
/// Walkability per tile = (a) tile not inside any tower footprint, AND
/// (b) the agentRadius dilation of nearby footprints does not fully cover the
/// tile rect. Areas smaller than MinAreaTileCount are dropped as slivers.
///
/// Builds run on a <see cref="WorkerThreadPool"/> task into a fresh
/// <see cref="Snapshot"/>; the volatile <c>_current</c> reference swaps on the
/// main thread once the build commits. Queries always read a fully-formed
/// snapshot — never half-built state. <see cref="ReachabilityReady"/> fires on
/// each commit so consumers can compound it with the navmesh-ready signal.
/// </summary>
[GlobalClass]
public partial class PocketReachabilityIndex : Node2D
{
    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public CoordConfig CoordConfig { get; set; }
    [Export] public EnemyConfig EnemyConfig { get; set; }
    [Export] public TowerPlacementManager TowerPlacementManager { get; set; }
    [Export] public TowerFootprintTracker FootprintTracker { get; set; }

    [Export] public int   MinAreaTileCount  { get; set; } = 2;
    [Export] public float SnapRadiusPx      { get; set; } = 64f;
    [Export] public bool  DebugDrawAreas    { get; set; }
    [Export] public bool  DebugProbeEnabled { get; set; }

    /// <summary>Per-viewport registry, mirrors PocketNavGridManager so consumers
    /// can resolve their reachability index by their own viewport.</summary>
    private static readonly Dictionary<Viewport, PocketReachabilityIndex> ByViewport = new();

    public static PocketReachabilityIndex ForViewport(Viewport viewport)
        => viewport != null && ByViewport.TryGetValue(viewport, out var idx) ? idx : null;

    /// <summary>Fires on the main thread once a freshly built snapshot has been
    /// committed (initial bake done, or every tower-driven rebake settles).
    /// Subscribers gate work on this compounded with PocketNavGridManager's
    /// BakingComplete to retarget only once both indexes agree on the world.</summary>
    public event Action ReachabilityReady;

    /// <summary>True once the first snapshot has committed. Stays true across
    /// rebuilds — the previous snapshot keeps serving queries until the new
    /// one publishes atomically.</summary>
    public bool IsReady => _current != null;

    // ── Snapshot (immutable post-build, swapped atomically) ─────────────────────

    internal struct AreaInfo
    {
        public Vector2I Chunk;
        public int      TileCount;
        public bool     Alive;
    }

    internal sealed class Snapshot
    {
        public Vector2I TileMin, TileMax;
        public int      TilesW, TilesH;
        public bool[]   Walkable;
        public int[]    TileToArea;       // -1 = none, area id otherwise
        public List<AreaInfo>                  Areas;
        public Dictionary<Vector2I, List<int>> ChunkAreas;
        public UnionFind                       Uf;

        public bool InBounds(Vector2I tile)
            => tile.X >= TileMin.X && tile.X <= TileMax.X
            && tile.Y >= TileMin.Y && tile.Y <= TileMax.Y;

        public int TileIndex(Vector2I tile)
            => (tile.Y - TileMin.Y) * TilesW + (tile.X - TileMin.X);

        public int AreaIdAtRaw(Vector2I tile)
        {
            if (!InBounds(tile)) return -1;
            int id = TileToArea[TileIndex(tile)];
            return id >= 0 ? id : -1;
        }
    }

    // Volatile so main-thread reads observe the fully-populated snapshot after
    // the worker's deferred Commit publishes it.
    private volatile Snapshot _current;

    // ── Bake job state (main-thread only) ───────────────────────────────────────

    private long _bakeTaskId;
    private bool _bakeInFlight;
    private bool _dirty;

    // Probe state — left-click anchors a "from" point; cursor is the live "to".
    private Vector2? _probeAnchor;

    private DebugDraw _debug;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public override void _EnterTree()
    {
        Viewport vp = GetViewport();
        if (vp == null) return;
        if (ByViewport.TryGetValue(vp, out var existing) && existing != this)
        {
            GD.PushWarning($"{Name}: another PocketReachabilityIndex already registered for this viewport.");
            return;
        }
        ByViewport[vp] = this;
    }

    public override void _Ready()
    {
        if (ChunkManager == null || CoordConfig == null || EnemyConfig == null
            || FootprintTracker == null)
        {
            GD.PushWarning($"{Name}: missing required exports.");
            return;
        }
        if (!ChunkManager.BoundsEnabled)
        {
            GD.PushWarning($"{Name}: requires ChunkManager.BoundsEnabled.");
            return;
        }

        if (TowerPlacementManager != null)
        {
            TowerPlacementManager.TowerPlaced  += OnTowerFootprintChanged;
            TowerPlacementManager.TowerRemoved += OnTowerFootprintChanged;
        }

        _debug = new DebugDraw(this);
        AddChild(_debug);

        // Defer initial bake so FootprintTracker has registered any preplaced towers.
        Callable.From(KickoffBake).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (DebugProbeEnabled) _debug?.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!DebugProbeEnabled) return;
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)  _probeAnchor = GetGlobalMousePosition();
            if (mb.ButtonIndex == MouseButton.Right) _probeAnchor = null;
            _debug?.QueueRedraw();
        }
    }

    public override void _ExitTree()
    {
        // Block on the in-flight worker so its captured BakeInput outlives it
        // and no stray Commit lands after we tear down.
        if (_bakeInFlight && _bakeTaskId != 0)
            WorkerThreadPool.WaitForTaskCompletion(_bakeTaskId);

        if (TowerPlacementManager != null)
        {
            TowerPlacementManager.TowerPlaced  -= OnTowerFootprintChanged;
            TowerPlacementManager.TowerRemoved -= OnTowerFootprintChanged;
        }

        Viewport vp = GetViewport();
        if (vp != null && ByViewport.TryGetValue(vp, out var idx) && idx == this)
            ByViewport.Remove(vp);
    }

    // ── Tower change handling ───────────────────────────────────────────────────

    private void OnTowerFootprintChanged(IReadOnlyList<Vector2I> _) => KickoffBake();

    // ── Bake orchestration (main thread) ────────────────────────────────────────

    private void KickoffBake()
    {
        if (_bakeInFlight)
        {
            _dirty = true;
            return;
        }

        BakeInput input = SnapshotInputs();
        _bakeInFlight = true;
        _bakeTaskId = WorkerThreadPool.AddTask(Callable.From(() =>
        {
            Snapshot built = BuildSnapshot(input);
            Callable.From(() => Commit(built)).CallDeferred();
        }));
    }

    /// <summary>
    /// Captures every input the worker needs into immutable POCO state. The
    /// build never touches the live FootprintTracker or scene graph.
    /// </summary>
    private BakeInput SnapshotInputs()
    {
        int cs = CoordConfig.ChunkSizeTiles;
        var tileMin = new Vector2I(
            ChunkManager.BoundsMin.X * cs,
            ChunkManager.BoundsMin.Y * cs);
        var tileMax = new Vector2I(
            (ChunkManager.BoundsMax.X + 1) * cs - 1,
            (ChunkManager.BoundsMax.Y + 1) * cs - 1);

        // Walk bounded tile rect once to snapshot tile→footprint. Linear in
        // tile count but bounded by board size; small relative to the bake.
        var tileToFootprint = new Dictionary<Vector2I, TowerFootprint>();
        for (int y = tileMin.Y; y <= tileMax.Y; y++)
        for (int x = tileMin.X; x <= tileMax.X; x++)
        {
            var t = new Vector2I(x, y);
            if (FootprintTracker.TryGetFootprintAt(t, out var fp))
                tileToFootprint[t] = fp;
        }

        return new BakeInput
        {
            BoundsMinChunk    = ChunkManager.BoundsMin,
            BoundsMaxChunk    = ChunkManager.BoundsMax,
            ChunkSizeTiles    = cs,
            TilePixelSize     = CoordConfig.TilePixelSize,
            CoordConfig       = CoordConfig,
            AgentRadius       = EnemyConfig.AgentRadius,
            MinAreaTileCount  = MinAreaTileCount,
            TileMin           = tileMin,
            TileMax           = tileMax,
            TileToFootprint   = tileToFootprint,
        };
    }

    private void Commit(Snapshot built)
    {
        _bakeInFlight = false;
        _bakeTaskId   = 0;
        _current      = built;            // volatile publish

        ReachabilityReady?.Invoke();
        _debug?.QueueRedraw();

        if (_dirty)
        {
            _dirty = false;
            KickoffBake();
        }
    }

    // ── Public API (reads through volatile snapshot) ────────────────────────────

    /// <summary>
    /// True iff a path exists between the two world points in the latest
    /// committed snapshot. Returns false before the first bake commits.
    /// </summary>
    public bool IsReachable(Vector2 fromWorld, Vector2 toWorld)
    {
        Snapshot s = _current; if (s == null) return false;
        Vector2I? a = NearestWalkableTile(s, fromWorld, SnapRadiusPx);
        Vector2I? b = NearestWalkableTile(s, toWorld,   SnapRadiusPx);
        if (a == null || b == null) return false;
        return AreTilesReachable(s, a.Value, b.Value);
    }

    public bool AreTilesReachable(Vector2I a, Vector2I b)
    {
        Snapshot s = _current;
        return s != null && AreTilesReachable(s, a, b);
    }

    private static bool AreTilesReachable(Snapshot s, Vector2I a, Vector2I b)
    {
        int ai = s.AreaIdAtRaw(a);
        int bi = s.AreaIdAtRaw(b);
        if (ai < 0 || bi < 0) return false;
        return s.Uf.Find(ai) == s.Uf.Find(bi);
    }

    public int? AreaIdAt(Vector2I tile)
    {
        Snapshot s = _current; if (s == null) return null;
        int id = s.AreaIdAtRaw(tile);
        return id >= 0 ? id : null;
    }

    /// <summary>Captures the current snapshot once so a multi-step caller (e.g.
    /// an enemy approach resolve scanning many candidates) sees a consistent
    /// view of the world even if a rebake commits mid-scan. Returns false until
    /// the first bake commits.</summary>
    public bool TryAcquireProbe(out Probe probe)
    {
        Snapshot s = _current;
        if (s == null) { probe = default; return false; }
        probe = new Probe(s, CoordConfig);
        return true;
    }

    /// <summary>Read-only handle over a captured <see cref="Snapshot"/>. Safe
    /// to use from any thread — the snapshot is immutable post-build, and the
    /// reference is captured at acquire-time so concurrent rebakes don't affect
    /// the in-flight queries.</summary>
    public readonly struct Probe
    {
        private readonly Snapshot _s;
        public CoordConfig CoordConfig { get; }

        internal Probe(Snapshot s, CoordConfig coords)
        {
            _s = s;
            CoordConfig = coords;
        }

        /// <summary>Union-find root of the connected component containing
        /// <paramref name="tile"/>, or null if the tile is out of bounds /
        /// unwalkable / a dropped sliver. The integer is opaque — only useful
        /// for equality comparison ("same component as enemy?").</summary>
        public int? ComponentRootAt(Vector2I tile)
        {
            int id = _s.AreaIdAtRaw(tile);
            return id >= 0 ? _s.Uf.Find(id) : null;
        }
    }

    // ── Worker-thread build (no scene-graph access) ─────────────────────────────

    private sealed class BakeInput
    {
        public Vector2I BoundsMinChunk, BoundsMaxChunk;
        public int      ChunkSizeTiles;
        public float    TilePixelSize;
        public CoordConfig CoordConfig;     // Resource — properties are pure getters
        public float    AgentRadius;
        public int      MinAreaTileCount;
        public Vector2I TileMin, TileMax;
        public Dictionary<Vector2I, TowerFootprint> TileToFootprint;
    }

    private static Snapshot BuildSnapshot(BakeInput inp)
    {
        int tilesW = inp.TileMax.X - inp.TileMin.X + 1;
        int tilesH = inp.TileMax.Y - inp.TileMin.Y + 1;

        var s = new Snapshot
        {
            TileMin    = inp.TileMin,
            TileMax    = inp.TileMax,
            TilesW     = tilesW,
            TilesH     = tilesH,
            Walkable   = new bool[tilesW * tilesH],
            TileToArea = new int[tilesW * tilesH],
            Areas      = new List<AreaInfo>(),
            ChunkAreas = new Dictionary<Vector2I, List<int>>(),
        };
        for (int i = 0; i < s.TileToArea.Length; i++) s.TileToArea[i] = -1;

        var freeIds    = new Stack<int>();
        var nearby     = new HashSet<TowerFootprint>();
        var floodQ     = new Queue<Vector2I>();
        var floodTiles = new List<int>();

        for (int cy = inp.BoundsMinChunk.Y; cy <= inp.BoundsMaxChunk.Y; cy++)
        for (int cx = inp.BoundsMinChunk.X; cx <= inp.BoundsMaxChunk.X; cx++)
            RecomputeChunk(s, inp, new Vector2I(cx, cy),
                nearby, floodQ, floodTiles, freeIds);

        s.Uf = BuildUnionFind(s, inp);
        return s;
    }

    private static void RecomputeChunk(
        Snapshot s, BakeInput inp, Vector2I chunk,
        HashSet<TowerFootprint> nearby, Queue<Vector2I> floodQ, List<int> floodTiles,
        Stack<int> freeIds)
    {
        if (!s.ChunkAreas.TryGetValue(chunk, out var areasInChunk))
        {
            areasInChunk = new List<int>();
            s.ChunkAreas[chunk] = areasInChunk;
        }

        int cs = inp.ChunkSizeTiles;
        Vector2I tileOrigin = CoordHelper.ChunkToFirstTile(chunk, inp.CoordConfig);

        float r   = inp.AgentRadius;
        float rSq = r * r;
        float tp  = inp.TilePixelSize;
        int reach = Mathf.CeilToInt(r / tp) + 1;

        nearby.Clear();
        int yLo = tileOrigin.Y - reach,
            yHi = tileOrigin.Y + cs - 1 + reach,
            xLo = tileOrigin.X - reach,
            xHi = tileOrigin.X + cs - 1 + reach;
        for (int y = yLo; y <= yHi; y++)
        for (int x = xLo; x <= xHi; x++)
            if (inp.TileToFootprint.TryGetValue(new Vector2I(x, y), out var fp))
                nearby.Add(fp);

        for (int ly = 0; ly < cs; ly++)
        for (int lx = 0; lx < cs; lx++)
        {
            var tile = new Vector2I(tileOrigin.X + lx, tileOrigin.Y + ly);
            if (!s.InBounds(tile)) continue;
            int idx = s.TileIndex(tile);
            s.Walkable[idx]   = IsTileWalkable(tile, inp, nearby, rSq, tp);
            s.TileToArea[idx] = -1;
        }

        for (int ly = 0; ly < cs; ly++)
        for (int lx = 0; lx < cs; lx++)
        {
            var tile = new Vector2I(tileOrigin.X + lx, tileOrigin.Y + ly);
            if (!s.InBounds(tile)) continue;
            int idx = s.TileIndex(tile);
            if (!s.Walkable[idx] || s.TileToArea[idx] != -1) continue;

            int count = FloodFillCollect(s, tile, tileOrigin, cs, floodQ, floodTiles);
            if (count < inp.MinAreaTileCount)
            {
                // Mark sliver tiles with -2 so the seed loop doesn't retry them.
                // AreaIdAtRaw treats any negative value as "no area".
                foreach (var i in floodTiles) s.TileToArea[i] = -2;
                continue;
            }

            int areaId = AllocAreaId(s, freeIds, chunk, count);
            foreach (var i in floodTiles) s.TileToArea[i] = areaId;
            areasInChunk.Add(areaId);
        }
    }

    private static int FloodFillCollect(
        Snapshot s, Vector2I seed, Vector2I tileOrigin, int cs,
        Queue<Vector2I> q, List<int> outIndices)
    {
        outIndices.Clear();
        q.Clear();

        int seedIdx = s.TileIndex(seed);
        s.TileToArea[seedIdx] = -2;
        outIndices.Add(seedIdx);
        q.Enqueue(seed);

        int xMin = tileOrigin.X, xMax = tileOrigin.X + cs - 1;
        int yMin = tileOrigin.Y, yMax = tileOrigin.Y + cs - 1;

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            TryVisit(s, t.X + 1, t.Y, xMin, xMax, yMin, yMax, q, outIndices);
            TryVisit(s, t.X - 1, t.Y, xMin, xMax, yMin, yMax, q, outIndices);
            TryVisit(s, t.X, t.Y + 1, xMin, xMax, yMin, yMax, q, outIndices);
            TryVisit(s, t.X, t.Y - 1, xMin, xMax, yMin, yMax, q, outIndices);
        }
        return outIndices.Count;
    }

    private static void TryVisit(
        Snapshot s, int x, int y, int xMin, int xMax, int yMin, int yMax,
        Queue<Vector2I> q, List<int> outIndices)
    {
        if (x < xMin || x > xMax || y < yMin || y > yMax) return;
        var t = new Vector2I(x, y);
        if (!s.InBounds(t)) return;
        int idx = s.TileIndex(t);
        if (!s.Walkable[idx] || s.TileToArea[idx] != -1) return;
        s.TileToArea[idx] = -2;
        outIndices.Add(idx);
        q.Enqueue(t);
    }

    private static bool IsTileWalkable(
        Vector2I tile, BakeInput inp, HashSet<TowerFootprint> nearby, float rSq, float tp)
    {
        if (inp.TileToFootprint.ContainsKey(tile)) return false;
        if (nearby.Count == 0) return true;

        const float eps = 0.001f;
        Vector2 origin = CoordHelper.TileToWorld(tile, inp.CoordConfig);
        float lo = eps, hi = tp - eps, mid = tp * 0.5f;

        Span<Vector2> samples = stackalloc Vector2[5];
        samples[0] = origin + new Vector2(mid, mid);
        samples[1] = origin + new Vector2(lo,  lo);
        samples[2] = origin + new Vector2(hi,  lo);
        samples[3] = origin + new Vector2(lo,  hi);
        samples[4] = origin + new Vector2(hi,  hi);

        foreach (var sample in samples)
        {
            bool covered = false;
            foreach (var fp in nearby)
            {
                if (fp.DistanceSqTo(sample) < rSq) { covered = true; break; }
            }
            if (!covered) return true;
        }
        return false;
    }

    private static int AllocAreaId(Snapshot s, Stack<int> freeIds, Vector2I chunk, int tileCount)
    {
        var info = new AreaInfo { Chunk = chunk, TileCount = tileCount, Alive = true };
        if (freeIds.Count > 0)
        {
            int id = freeIds.Pop();
            s.Areas[id] = info;
            return id;
        }
        s.Areas.Add(info);
        return s.Areas.Count - 1;
    }

    private static UnionFind BuildUnionFind(Snapshot s, BakeInput inp)
    {
        var uf = new UnionFind(s.Areas.Count);
        for (int cy = inp.BoundsMinChunk.Y; cy <= inp.BoundsMaxChunk.Y; cy++)
        for (int cx = inp.BoundsMinChunk.X; cx <= inp.BoundsMaxChunk.X; cx++)
        {
            var chunk = new Vector2I(cx, cy);
            UnionAcrossEdge(s, inp, uf, chunk, axisX: true);
            UnionAcrossEdge(s, inp, uf, chunk, axisX: false);
        }
        return uf;
    }

    private static void UnionAcrossEdge(
        Snapshot s, BakeInput inp, UnionFind uf, Vector2I chunkA, bool axisX)
    {
        var chunkB = axisX ? chunkA + new Vector2I(1, 0) : chunkA + new Vector2I(0, 1);
        if (chunkB.X > inp.BoundsMaxChunk.X || chunkB.Y > inp.BoundsMaxChunk.Y) return;

        int cs = inp.ChunkSizeTiles;
        Vector2I aOrigin = CoordHelper.ChunkToFirstTile(chunkA, inp.CoordConfig);

        if (axisX)
        {
            int xA = aOrigin.X + cs - 1;
            int xB = xA + 1;
            for (int y = aOrigin.Y; y < aOrigin.Y + cs; y++)
                UnionTilePair(s, uf, new Vector2I(xA, y), new Vector2I(xB, y));
        }
        else
        {
            int yA = aOrigin.Y + cs - 1;
            int yB = yA + 1;
            for (int x = aOrigin.X; x < aOrigin.X + cs; x++)
                UnionTilePair(s, uf, new Vector2I(x, yA), new Vector2I(x, yB));
        }
    }

    private static void UnionTilePair(Snapshot s, UnionFind uf, Vector2I a, Vector2I b)
    {
        int ai = s.AreaIdAtRaw(a);
        int bi = s.AreaIdAtRaw(b);
        if (ai >= 0 && bi >= 0) uf.Union(ai, bi);
    }

    // ── Snap-to-walkable ────────────────────────────────────────────────────────

    private Vector2I? NearestWalkableTile(Snapshot s, Vector2 worldPos, float radiusPx)
    {
        var start = CoordHelper.WorldToTile(worldPos, CoordConfig);
        if (s.AreaIdAtRaw(start) >= 0) return start;

        int radiusTiles = Mathf.CeilToInt(radiusPx / CoordConfig.TilePixelSize);
        if (radiusTiles <= 0) return null;

        var visited = new HashSet<Vector2I> { start };
        var current = new List<Vector2I> { start };
        var next    = new List<Vector2I>();

        for (int d = 1; d <= radiusTiles; d++)
        {
            next.Clear();
            foreach (var t in current)
            {
                AddIfNew(new Vector2I(t.X + 1, t.Y), visited, next);
                AddIfNew(new Vector2I(t.X - 1, t.Y), visited, next);
                AddIfNew(new Vector2I(t.X, t.Y + 1), visited, next);
                AddIfNew(new Vector2I(t.X, t.Y - 1), visited, next);
            }
            foreach (var t in next)
                if (s.AreaIdAtRaw(t) >= 0) return t;
            (current, next) = (next, current);
        }
        return null;
    }

    private static void AddIfNew(Vector2I t, HashSet<Vector2I> visited, List<Vector2I> next)
    {
        if (visited.Add(t)) next.Add(t);
    }

    // ── Debug draw ──────────────────────────────────────────────────────────────

    private partial class DebugDraw(PocketReachabilityIndex idx) : Node2D
    {
        private static readonly Color[] Palette =
        {
            new(0.20f, 0.80f, 0.30f, 0.30f),
            new(0.20f, 0.50f, 0.90f, 0.30f),
            new(0.90f, 0.60f, 0.20f, 0.30f),
            new(0.80f, 0.30f, 0.80f, 0.30f),
            new(0.20f, 0.80f, 0.80f, 0.30f),
            new(0.90f, 0.30f, 0.30f, 0.30f),
        };

        private static readonly Color ColReachable    = new(0.20f, 1.00f, 0.30f, 0.95f);
        private static readonly Color ColUnreachable  = new(1.00f, 0.25f, 0.20f, 0.95f);
        private static readonly Color ColAnchorRing   = new(0.95f, 0.95f, 0.20f, 0.95f);
        private static readonly Color ColCursorRing   = new(0.95f, 0.95f, 0.95f, 0.95f);
        private static readonly Color ColSnapTarget   = new(0.20f, 0.80f, 1.00f, 0.95f);

        public override void _Draw()
        {
            Snapshot s = idx._current;
            if (s == null) return;
            if (idx.DebugDrawAreas)    DrawAreas(s);
            if (idx.DebugProbeEnabled) DrawProbe(s);
        }

        private void DrawAreas(Snapshot s)
        {
            float tp = idx.CoordConfig.TilePixelSize;
            for (int y = 0; y < s.TilesH; y++)
            for (int x = 0; x < s.TilesW; x++)
            {
                int id = s.TileToArea[y * s.TilesW + x];
                if (id < 0) continue;
                int root = s.Uf.Find(id);
                var color = Palette[root % Palette.Length];
                var tile  = new Vector2I(s.TileMin.X + x, s.TileMin.Y + y);
                var world = CoordHelper.TileToWorld(tile, idx.CoordConfig);
                DrawRect(new Rect2(world, new Vector2(tp, tp)), color);
            }
        }

        private void DrawProbe(Snapshot s)
        {
            var cursor = GetGlobalMousePosition();
            var cursorSnap = idx.NearestWalkableTile(s, cursor, idx.SnapRadiusPx);
            DrawEndpoint(cursor, cursorSnap, ColCursorRing);

            if (idx._probeAnchor is not Vector2 anchor) return;
            var anchorSnap = idx.NearestWalkableTile(s, anchor, idx.SnapRadiusPx);
            DrawEndpoint(anchor, anchorSnap, ColAnchorRing);

            bool reachable = idx.IsReachable(anchor, cursor);
            DrawLine(anchor, cursor, reachable ? ColReachable : ColUnreachable, width: 2f);

            string status = reachable ? "REACHABLE" : "UNREACHABLE";
            string aInfo = AreaText(s, anchorSnap);
            string bInfo = AreaText(s, cursorSnap);
            DrawString(ThemeDB.FallbackFont, cursor + new Vector2(12, -24),
                $"{status}", HorizontalAlignment.Left, -1, 14,
                reachable ? ColReachable : ColUnreachable);
            DrawString(ThemeDB.FallbackFont, cursor + new Vector2(12, -8),
                $"from: {aInfo}", HorizontalAlignment.Left, -1, 12, ColAnchorRing);
            DrawString(ThemeDB.FallbackFont, cursor + new Vector2(12, 8),
                $"to:   {bInfo}", HorizontalAlignment.Left, -1, 12, ColCursorRing);
        }

        private void DrawEndpoint(Vector2 worldPos, Vector2I? snap, Color ringColor)
        {
            DrawCircle(worldPos, 4f, ringColor);
            DrawArc(worldPos, 6f, 0f, Mathf.Tau, 24, ringColor, width: 1.5f);
            if (snap is not Vector2I sTile) return;

            float tp = idx.CoordConfig.TilePixelSize;
            var tileWorld  = CoordHelper.TileToWorld(sTile, idx.CoordConfig);
            var tileCenter = tileWorld + new Vector2(tp * 0.5f, tp * 0.5f);
            DrawRect(new Rect2(tileWorld, new Vector2(tp, tp)),
                new Color(ColSnapTarget, 0.35f));
            DrawRect(new Rect2(tileWorld, new Vector2(tp, tp)),
                ColSnapTarget, filled: false, width: 1.5f);
            DrawLine(worldPos, tileCenter, new Color(ColSnapTarget, 0.6f), width: 1f);
        }

        private string AreaText(Snapshot s, Vector2I? snap)
        {
            if (snap is not Vector2I sTile) return "(no walkable tile in snap radius)";
            int id = s.AreaIdAtRaw(sTile);
            if (id < 0) return $"tile {sTile} (no area)";
            int root = s.Uf.Find(id);
            return $"tile {sTile}  area#{id}  comp#{root}";
        }
    }
}
