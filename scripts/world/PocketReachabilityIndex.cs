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

    // ── Bounds & flat tile arrays ───────────────────────────────────────────────

    private Vector2I _tileMin, _tileMax;
    private int _tilesW, _tilesH;
    private bool[] _walkable   = Array.Empty<bool>();
    private int[]  _tileToArea = Array.Empty<int>();   // -1 = none

    // ── Areas ───────────────────────────────────────────────────────────────────

    private struct AreaInfo
    {
        public Vector2I Chunk;
        public int      TileCount;
        public bool     Alive;
    }

    private readonly List<AreaInfo>                  _areas       = new();
    private readonly Stack<int>                      _freeAreaIds = new();
    private readonly Dictionary<Vector2I, List<int>> _chunkAreas  = new();

    // ── Reachability ────────────────────────────────────────────────────────────

    private UnionFind _uf;

    // ── Scratch ─────────────────────────────────────────────────────────────────

    private readonly HashSet<TowerFootprint> _scratchChunkFootprints = new();
    private readonly Queue<Vector2I>         _scratchFloodQueue      = new();
    private readonly List<int>               _scratchFloodTiles      = new();
    private DebugDraw _debug;

    // Probe state — left-click anchors a "from" point; cursor is the live "to".
    private Vector2? _probeAnchor;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

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

        InitBounds();

        if (TowerPlacementManager != null)
        {
            TowerPlacementManager.TowerPlaced  += OnTowerFootprintChanged;
            TowerPlacementManager.TowerRemoved += OnTowerFootprintChanged;
        }

        // Defer initial bake so FootprintTracker has registered any preplaced towers.
        Callable.From(InitialFullRebuild).CallDeferred();

        _debug = new DebugDraw(this);
        AddChild(_debug);
    }

    public override void _Process(double delta)
    {
        // Probe overlay follows the cursor — needs per-frame redraw.
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
        if (TowerPlacementManager != null)
        {
            TowerPlacementManager.TowerPlaced  -= OnTowerFootprintChanged;
            TowerPlacementManager.TowerRemoved -= OnTowerFootprintChanged;
        }
    }

    private void InitBounds()
    {
        int cs = CoordConfig.ChunkSizeTiles;
        _tileMin = new Vector2I(
            ChunkManager.BoundsMin.X * cs,
            ChunkManager.BoundsMin.Y * cs);
        _tileMax = new Vector2I(
            (ChunkManager.BoundsMax.X + 1) * cs - 1,
            (ChunkManager.BoundsMax.Y + 1) * cs - 1);
        _tilesW = _tileMax.X - _tileMin.X + 1;
        _tilesH = _tileMax.Y - _tileMin.Y + 1;
        _walkable   = new bool[_tilesW * _tilesH];
        _tileToArea = new int[_tilesW * _tilesH];
        for (int i = 0; i < _tileToArea.Length; i++) _tileToArea[i] = -1;
    }

    private void InitialFullRebuild()
    {
        for (int cy = ChunkManager.BoundsMin.Y; cy <= ChunkManager.BoundsMax.Y; cy++)
        for (int cx = ChunkManager.BoundsMin.X; cx <= ChunkManager.BoundsMax.X; cx++)
            RecomputeChunk(new Vector2I(cx, cy));
        RebuildUnionFind();
        _debug?.QueueRedraw();
    }

    // ── Tower change handling ───────────────────────────────────────────────────

    private void OnTowerFootprintChanged(IReadOnlyList<Vector2I> tiles)
    {
        if (_walkable.Length == 0) return;

        float r  = EnemyConfig.AgentRadius;
        float tp = CoordConfig.TilePixelSize;
        int reach = Mathf.CeilToInt(r / tp) + 1;

        var touched = new HashSet<Vector2I>();
        foreach (var t in tiles)
        {
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                var nt = new Vector2I(t.X + dx, t.Y + dy);
                if (!InBounds(nt)) continue;
                touched.Add(CoordHelper.TileToChunk(nt, CoordConfig));
            }
        }

        foreach (var c in touched) RecomputeChunk(c);
        RebuildUnionFind();
        _debug?.QueueRedraw();
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// True iff there exists a walkable path between <paramref name="fromWorld"/>
    /// and <paramref name="toWorld"/>. If either point is non-walkable, snaps to
    /// the nearest walkable tile within SnapRadiusPx before testing.
    /// </summary>
    public bool IsReachable(Vector2 fromWorld, Vector2 toWorld)
    {
        var a = NearestWalkableTile(fromWorld, SnapRadiusPx);
        var b = NearestWalkableTile(toWorld,   SnapRadiusPx);
        if (a == null || b == null) return false;
        return AreTilesReachable(a.Value, b.Value);
    }

    public bool AreTilesReachable(Vector2I a, Vector2I b)
    {
        int ai = AreaIdAtRaw(a);
        int bi = AreaIdAtRaw(b);
        if (ai < 0 || bi < 0 || _uf == null) return false;
        return _uf.Find(ai) == _uf.Find(bi);
    }

    public int? AreaIdAt(Vector2I tile)
    {
        int id = AreaIdAtRaw(tile);
        return id >= 0 ? id : null;
    }

    // ── Per-chunk recompute ─────────────────────────────────────────────────────

    private void RecomputeChunk(Vector2I chunk)
    {
        if (!_chunkAreas.TryGetValue(chunk, out var areasInChunk))
        {
            areasInChunk = new List<int>();
            _chunkAreas[chunk] = areasInChunk;
        }
        else
        {
            foreach (var id in areasInChunk) FreeAreaId(id);
            areasInChunk.Clear();
        }

        int cs = CoordConfig.ChunkSizeTiles;
        Vector2I tileOrigin = CoordHelper.ChunkToFirstTile(chunk, CoordConfig);

        // Collect footprints near this chunk once — reused by every tile predicate.
        float r   = EnemyConfig.AgentRadius;
        float rSq = r * r;
        float tp  = CoordConfig.TilePixelSize;
        int reach = Mathf.CeilToInt(r / tp) + 1;

        var nearby = _scratchChunkFootprints;
        nearby.Clear();
        int yLo = tileOrigin.Y - reach,
            yHi = tileOrigin.Y + cs - 1 + reach,
            xLo = tileOrigin.X - reach,
            xHi = tileOrigin.X + cs - 1 + reach;
        for (int y = yLo; y <= yHi; y++)
        for (int x = xLo; x <= xHi; x++)
            if (FootprintTracker.TryGetFootprintAt(new Vector2I(x, y), out var fp))
                nearby.Add(fp);

        // Recompute walkability and clear old area assignments for this chunk.
        for (int ly = 0; ly < cs; ly++)
        for (int lx = 0; lx < cs; lx++)
        {
            var tile = new Vector2I(tileOrigin.X + lx, tileOrigin.Y + ly);
            if (!InBounds(tile)) continue;
            int idx = TileIndex(tile);
            _walkable[idx]   = IsTileWalkable(tile, nearby, rSq, tp);
            _tileToArea[idx] = -1;
        }

        // Flood-fill into areas, dropping slivers smaller than MinAreaTileCount.
        for (int ly = 0; ly < cs; ly++)
        for (int lx = 0; lx < cs; lx++)
        {
            var tile = new Vector2I(tileOrigin.X + lx, tileOrigin.Y + ly);
            if (!InBounds(tile)) continue;
            int idx = TileIndex(tile);
            if (!_walkable[idx] || _tileToArea[idx] != -1) continue;

            int count = FloodFillCollect(tile, tileOrigin, cs, _scratchFloodTiles);
            if (count < MinAreaTileCount)
            {
                // Mark sliver tiles with -2 so the seed loop doesn't retry them.
                // AreaIdAtRaw treats any negative value as "no area".
                foreach (var i in _scratchFloodTiles) _tileToArea[i] = -2;
                continue;
            }

            int areaId = AllocAreaId(chunk, count);
            foreach (var i in _scratchFloodTiles) _tileToArea[i] = areaId;
            areasInChunk.Add(areaId);
        }
    }

    /// <summary>
    /// BFS flood-fill restricted to a single chunk over walkable tiles whose
    /// _tileToArea slot is -1. Marks visited tiles with -2 sentinel and collects
    /// their indices into <paramref name="outIndices"/>. Caller commits an areaId
    /// or reverts to -1.
    /// </summary>
    private int FloodFillCollect(Vector2I seed, Vector2I tileOrigin, int cs, List<int> outIndices)
    {
        outIndices.Clear();
        var q = _scratchFloodQueue;
        q.Clear();

        int seedIdx = TileIndex(seed);
        _tileToArea[seedIdx] = -2;
        outIndices.Add(seedIdx);
        q.Enqueue(seed);

        int xMin = tileOrigin.X, xMax = tileOrigin.X + cs - 1;
        int yMin = tileOrigin.Y, yMax = tileOrigin.Y + cs - 1;

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            TryVisit(t.X + 1, t.Y, xMin, xMax, yMin, yMax, q, outIndices);
            TryVisit(t.X - 1, t.Y, xMin, xMax, yMin, yMax, q, outIndices);
            TryVisit(t.X, t.Y + 1, xMin, xMax, yMin, yMax, q, outIndices);
            TryVisit(t.X, t.Y - 1, xMin, xMax, yMin, yMax, q, outIndices);
        }
        return outIndices.Count;
    }

    private void TryVisit(int x, int y, int xMin, int xMax, int yMin, int yMax,
                          Queue<Vector2I> q, List<int> outIndices)
    {
        if (x < xMin || x > xMax || y < yMin || y > yMax) return;
        var t = new Vector2I(x, y);
        if (!InBounds(t)) return;
        int idx = TileIndex(t);
        if (!_walkable[idx] || _tileToArea[idx] != -1) return;
        _tileToArea[idx] = -2;
        outIndices.Add(idx);
        q.Enqueue(t);
    }

    // ── Walkability predicate ───────────────────────────────────────────────────

    private bool IsTileWalkable(Vector2I tile, HashSet<TowerFootprint> nearby, float rSq, float tp)
    {
        if (FootprintTracker.IsOccupied(tile)) return false;
        if (nearby.Count == 0) return true;

        const float eps = 0.001f;
        Vector2 origin = CoordHelper.TileToWorld(tile, CoordConfig);
        float lo = eps, hi = tp - eps, mid = tp * 0.5f;

        Span<Vector2> samples = stackalloc Vector2[5];
        samples[0] = origin + new Vector2(mid, mid);
        samples[1] = origin + new Vector2(lo,  lo);
        samples[2] = origin + new Vector2(hi,  lo);
        samples[3] = origin + new Vector2(lo,  hi);
        samples[4] = origin + new Vector2(hi,  hi);

        foreach (var s in samples)
        {
            bool covered = false;
            foreach (var fp in nearby)
            {
                if (fp.DistanceSqTo(s) < rSq) { covered = true; break; }
            }
            if (!covered) return true;
        }
        return false;
    }

    // ── Union-find rebuild ──────────────────────────────────────────────────────

    private void RebuildUnionFind()
    {
        _uf = new UnionFind(_areas.Count);

        for (int cy = ChunkManager.BoundsMin.Y; cy <= ChunkManager.BoundsMax.Y; cy++)
        for (int cx = ChunkManager.BoundsMin.X; cx <= ChunkManager.BoundsMax.X; cx++)
        {
            var chunk = new Vector2I(cx, cy);
            UnionAcrossEdge(chunk, axisX: true);
            UnionAcrossEdge(chunk, axisX: false);
        }
    }

    private void UnionAcrossEdge(Vector2I chunkA, bool axisX)
    {
        var chunkB = axisX ? chunkA + new Vector2I(1, 0) : chunkA + new Vector2I(0, 1);
        if (chunkB.X > ChunkManager.BoundsMax.X || chunkB.Y > ChunkManager.BoundsMax.Y)
            return;

        int cs = CoordConfig.ChunkSizeTiles;
        Vector2I aOrigin = CoordHelper.ChunkToFirstTile(chunkA, CoordConfig);

        if (axisX)
        {
            int xA = aOrigin.X + cs - 1;
            int xB = xA + 1;
            for (int y = aOrigin.Y; y < aOrigin.Y + cs; y++)
                UnionTilePair(new Vector2I(xA, y), new Vector2I(xB, y));
        }
        else
        {
            int yA = aOrigin.Y + cs - 1;
            int yB = yA + 1;
            for (int x = aOrigin.X; x < aOrigin.X + cs; x++)
                UnionTilePair(new Vector2I(x, yA), new Vector2I(x, yB));
        }
    }

    private void UnionTilePair(Vector2I a, Vector2I b)
    {
        int ai = AreaIdAtRaw(a);
        int bi = AreaIdAtRaw(b);
        if (ai >= 0 && bi >= 0) _uf.Union(ai, bi);
    }

    // ── Snap-to-walkable ────────────────────────────────────────────────────────

    private Vector2I? NearestWalkableTile(Vector2 worldPos, float radiusPx)
    {
        var start = CoordHelper.WorldToTile(worldPos, CoordConfig);
        if (HasArea(start)) return start;

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
                if (HasArea(t)) return t;
            (current, next) = (next, current);
        }
        return null;
    }

    private static void AddIfNew(Vector2I t, HashSet<Vector2I> visited, List<Vector2I> next)
    {
        if (visited.Add(t)) next.Add(t);
    }

    private bool HasArea(Vector2I tile) => AreaIdAtRaw(tile) >= 0;

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private bool InBounds(Vector2I tile)
        => tile.X >= _tileMin.X && tile.X <= _tileMax.X
        && tile.Y >= _tileMin.Y && tile.Y <= _tileMax.Y;

    private int TileIndex(Vector2I tile)
        => (tile.Y - _tileMin.Y) * _tilesW + (tile.X - _tileMin.X);

    private int AreaIdAtRaw(Vector2I tile)
    {
        if (!InBounds(tile)) return -1;
        int id = _tileToArea[TileIndex(tile)];
        return id >= 0 ? id : -1;
    }

    private int AllocAreaId(Vector2I chunk, int tileCount)
    {
        var info = new AreaInfo { Chunk = chunk, TileCount = tileCount, Alive = true };
        if (_freeAreaIds.Count > 0)
        {
            int id = _freeAreaIds.Pop();
            _areas[id] = info;
            return id;
        }
        _areas.Add(info);
        return _areas.Count - 1;
    }

    private void FreeAreaId(int id)
    {
        var info = _areas[id];
        info.Alive = false;
        _areas[id] = info;
        _freeAreaIds.Push(id);
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
            if (idx._uf == null) return;
            if (idx.DebugDrawAreas)    DrawAreas();
            if (idx.DebugProbeEnabled) DrawProbe();
        }

        private void DrawAreas()
        {
            float tp = idx.CoordConfig.TilePixelSize;
            for (int y = 0; y < idx._tilesH; y++)
            for (int x = 0; x < idx._tilesW; x++)
            {
                int id = idx._tileToArea[y * idx._tilesW + x];
                if (id < 0) continue;
                int root = idx._uf.Find(id);
                var color = Palette[root % Palette.Length];
                var tile = new Vector2I(idx._tileMin.X + x, idx._tileMin.Y + y);
                var world = CoordHelper.TileToWorld(tile, idx.CoordConfig);
                DrawRect(new Rect2(world, new Vector2(tp, tp)), color);
            }
        }

        private void DrawProbe()
        {
            var cursor = GetGlobalMousePosition();
            var cursorSnap = idx.NearestWalkableTile(cursor, idx.SnapRadiusPx);
            DrawEndpoint(cursor, cursorSnap, ColCursorRing);

            if (idx._probeAnchor is not Vector2 anchor) return;
            var anchorSnap = idx.NearestWalkableTile(anchor, idx.SnapRadiusPx);
            DrawEndpoint(anchor, anchorSnap, ColAnchorRing);

            bool reachable = idx.IsReachable(anchor, cursor);
            DrawLine(anchor, cursor, reachable ? ColReachable : ColUnreachable, width: 2f);

            // Readout text near the cursor.
            string status = reachable ? "REACHABLE" : "UNREACHABLE";
            string aInfo = AreaText(anchorSnap);
            string bInfo = AreaText(cursorSnap);
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
            if (snap is not Vector2I s) return;

            float tp = idx.CoordConfig.TilePixelSize;
            var tileWorld = CoordHelper.TileToWorld(s, idx.CoordConfig);
            var tileCenter = tileWorld + new Vector2(tp * 0.5f, tp * 0.5f);
            DrawRect(new Rect2(tileWorld, new Vector2(tp, tp)),
                new Color(ColSnapTarget, 0.35f));
            DrawRect(new Rect2(tileWorld, new Vector2(tp, tp)),
                ColSnapTarget, filled: false, width: 1.5f);
            DrawLine(worldPos, tileCenter, new Color(ColSnapTarget, 0.6f), width: 1f);
        }

        private string AreaText(Vector2I? snap)
        {
            if (snap is not Vector2I s) return "(no walkable tile in snap radius)";
            int id = idx.AreaIdAtRaw(s);
            if (id < 0) return $"tile {s} (no area)";
            int root = idx._uf.Find(id);
            return $"tile {s}  area#{id}  comp#{root}";
        }
    }
}
