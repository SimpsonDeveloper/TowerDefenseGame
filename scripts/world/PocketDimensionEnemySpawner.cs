using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.terrain;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Queues enemies hit-in by the overworld and releases them in timed waves
/// at random border tiles of the pocket-dimension play area.
///
/// Lifecycle: the first queued enemy starts the wave timer and opens a
/// persistent VFX portal at a random border checkpoint. As more enemies queue
/// during the countdown, additional portals open so their count tracks the
/// crowd size (see <see cref="MinEnemiesPerPortal"/>), up to
/// <see cref="MaxCheckpointsPerWave"/>. Those portals glow for the whole countdown,
/// telegraphing where the wave will arrive. When the timer reaches zero, the
/// queue is partitioned evenly across the already-open checkpoints; each is
/// handed its share to spawn at <see cref="SpawnInterval"/> via a transient
/// sub-spawner that flares the portal per spawn and closes it when its list is
/// exhausted. The queue then clears and the timer goes idle until the next
/// enqueue (which picks fresh checkpoints).
/// </summary>
[GlobalClass]
public partial class PocketDimensionEnemySpawner : Node2D
{
    [Signal] public delegate void QueueChangedEventHandler(int queueSize);
    [Signal] public delegate void TimerStartedEventHandler();
    [Signal] public delegate void TimerResetEventHandler();
    [Signal] public delegate void WaveFiredEventHandler(int enemyCount, int checkpointCount);

    /// <summary>Live source of truth for pocket-dim bounds. Read each wave fire — bounds may change at runtime.</summary>
    [Export] public ChunkManager ChunkManager { get; set; }

    [Export] public CoordConfig CoordConfig { get; set; }

    /// <summary>Scene instantiated for every spawn. Configures itself from the EnemyType resource.</summary>
    [Export] public PackedScene EnemyScene { get; set; }

    /// <summary>Optional VFX portal spawned at each checkpoint for the duration of its sub-wave. Leave null to disable.</summary>
    [Export] public PackedScene PortalScene { get; set; }

    /// <summary>Container that spawned enemies are parented under. Typically a Node2D inside the pocket dim.</summary>
    [Export] public Node2D EnemiesContainer { get; set; }

    /// <summary>Hard cap on border checkpoints/portals opened per wave. The actual count scales with queue size (see <see cref="MinEnemiesPerPortal"/>).</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int MaxCheckpointsPerWave { get; set; } = 4;

    /// <summary>
    /// Target enemies per portal. Portal count = ceil(queueSize / this), clamped to
    /// [1, <see cref="MaxCheckpointsPerWave"/>]. Lower = more portals for the same crowd.
    /// As the queue grows during the countdown, extra portals open to keep up.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,1")] public int MinEnemiesPerPortal { get; set; } = 3;

    /// <summary>Seconds between waves. Counts down only while the queue is non-empty.</summary>
    [Export] public float WaveInterval { get; set; } = 15f;

    /// <summary>Seconds between consecutive spawns at a single checkpoint.</summary>
    [Export] public float SpawnInterval { get; set; } = 0.4f;

    /// <summary>Safety cap on border-tile re-rolls when looking for a traversable, well-spaced checkpoint.</summary>
    [Export] public int CheckpointMaxRerolls { get; set; } = 32;

    /// <summary>Minimum world-pixel distance between two checkpoints in the same wave. Picks closer than this are re-rolled. 0 disables spacing.</summary>
    [Export] public float MinCheckpointSpacing { get; set; } = 256f;

    /// <summary>A border spot reserved for the pending wave, plus its open portal (null if PortalScene unset).</summary>
    private readonly struct ActiveCheckpoint
    {
        public readonly Vector2 Position;
        public readonly SpawnerPortal Portal;
        public ActiveCheckpoint(Vector2 position, SpawnerPortal portal) { Position = position; Portal = portal; }
    }

    private readonly List<EnemyType> _queue = new();
    // Checkpoints chosen when the countdown starts; portals stay open until the wave fires.
    private readonly List<ActiveCheckpoint> _checkpoints = new();
    private float _timer;
    private bool _timerActive;
    private RandomNumberGenerator _rng = new();

    public int QueueSize => _queue.Count;
    public bool IsTimerActive => _timerActive;
    public float SecondsRemaining => _timerActive ? Mathf.Max(0f, _timer) : 0f;
    public float WaveIntervalSeconds => WaveInterval;

    public override void _Ready()
    {
        _rng.Randomize();
    }

    public override void _Process(double delta)
    {
        if (!_timerActive) return;

        _timer -= (float)delta;
        if (_timer <= 0f)
            FireWave();
    }

    /// <summary>Queue an enemy. Starts the timer if it was idle.</summary>
    public void Enqueue(EnemyType type)
    {
        if (type == null) return;
        _queue.Add(type);
        EmitSignal(SignalName.QueueChanged, _queue.Count);

        if (!_timerActive)
        {
            _timer = WaveInterval;
            _timerActive = true;
            EmitSignal(SignalName.TimerStarted);
        }

        EnsurePortals();
    }

    /// <summary>Portals wanted right now: ceil(queueSize / MinEnemiesPerPortal), clamped to [1, MaxCheckpointsPerWave].</summary>
    private int DesiredPortalCount()
    {
        if (_queue.Count == 0) return 0;
        int byDensity = Mathf.CeilToInt(_queue.Count / (float)Mathf.Max(1, MinEnemiesPerPortal));
        return Mathf.Clamp(byDensity, 1, MaxCheckpointsPerWave);
    }

    /// <summary>
    /// Open portals until we have as many as the queue now warrants. Only ever
    /// adds (the queue never shrinks mid-countdown), so existing portals and their
    /// positions stay put. A failed pick (e.g. bounds not ready) just stops early;
    /// the next enqueue — or FireWave — retries.
    /// </summary>
    private void EnsurePortals()
    {
        int desired = DesiredPortalCount();
        while (_checkpoints.Count < desired && TryOpenOnePortal()) { }
    }

    private bool TryOpenOnePortal()
    {
        var picks = PickBorderCheckpoints(1);
        if (picks.Count == 0) return false;

        Vector2 pos = picks[0];
        SpawnerPortal portal = null;
        if (PortalScene != null && EnemiesContainer != null)
        {
            portal = PortalScene.Instantiate<SpawnerPortal>();
            EnemiesContainer.AddChild(portal);
            portal.GlobalPosition = pos;
            portal.Flare();
        }
        _checkpoints.Add(new ActiveCheckpoint(pos, portal));
        return true;
    }

    private void CloseAndClearPortals()
    {
        foreach (ActiveCheckpoint cp in _checkpoints)
            cp.Portal?.Close();
        _checkpoints.Clear();
    }

    private void FireWave()
    {
        if (_queue.Count == 0)
        {
            CloseAndClearPortals();
            ResetTimer();
            return;
        }

        // Portals open as the queue grows during the countdown; this is a late
        // catch-up in case earlier picks failed (e.g. bounds not ready then).
        EnsurePortals();
        if (_checkpoints.Count == 0)
        {
            GD.PushWarning($"{Name}: no valid border checkpoints found — keeping queue, will retry next interval.");
            _timer = WaveInterval;
            return;
        }

        int n = _checkpoints.Count;

        // Random even partition: shuffle the queue, then deal round-robin across
        // the preselected checkpoints. Checkpoints beyond the queue size get an
        // empty list — their portal simply closes without spawning.
        var lists = new List<List<EnemyType>>(n);
        for (int i = 0; i < n; i++) lists.Add(new List<EnemyType>());

        ShuffleQueue();
        for (int i = 0; i < _queue.Count; i++)
            lists[i % n].Add(_queue[i]);

        int totalEnemies = _queue.Count;
        _queue.Clear();
        EmitSignal(SignalName.QueueChanged, 0);

        for (int i = 0; i < n; i++)
            SpawnSubSpawner(_checkpoints[i], lists[i]);

        EmitSignal(SignalName.WaveFired, totalEnemies, n);
        _checkpoints.Clear(); // ownership of portals passes to the sub-spawners
        ResetTimer();
    }

    private void ResetTimer()
    {
        _timerActive = false;
        _timer = 0f;
        EmitSignal(SignalName.TimerReset);
    }

    private void ShuffleQueue()
    {
        for (int i = _queue.Count - 1; i > 0; i--)
        {
            int j = (int)_rng.RandiRange(0, i);
            (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
        }
    }

    // Picks up to `count` random border tiles inside the live ChunkManager bounds
    // that are traversable and at least MinCheckpointSpacing from every already-open
    // checkpoint and from picks made earlier in this call. Re-rolls (with a per-pick
    // cap) until each slot fills or the budget is exhausted.
    private List<Vector2> PickBorderCheckpoints(int count)
    {
        var picks = new List<Vector2>(count);
        if (ChunkManager is not { BoundsEnabled: true })
        {
            GD.PushWarning($"{Name}: ChunkManager bounds not enabled — cannot pick border checkpoints.");
            return picks;
        }

        Vector2I min = ChunkManager.BoundsMin;
        Vector2I max = ChunkManager.BoundsMax;
        int chunkTiles = CoordConfig.ChunkSizeTiles;
        int tileMinX = min.X * chunkTiles;
        int tileMinY = min.Y * chunkTiles;
        int tileMaxX = (max.X + 1) * chunkTiles - 1;
        int tileMaxY = (max.Y + 1) * chunkTiles - 1;
        int spanX = tileMaxX - tileMinX + 1;
        int spanY = tileMaxY - tileMinY + 1;
        int perimeter = 2 * spanX + 2 * Mathf.Max(0, spanY - 2);
        if (perimeter <= 0) return picks;

        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < CheckpointMaxRerolls; attempt++)
            {
                Vector2I tile = SampleBorderTile(_rng.RandiRange(0, perimeter - 1),
                    tileMinX, tileMinY, tileMaxX, tileMaxY, spanX);
                Vector2 world = CoordHelper.TileToWorld(tile, CoordConfig)
                                + new Vector2(CoordConfig.TilePixelSize, CoordConfig.TilePixelSize) * 0.5f;

                if (IsTraversable(world) && IsFarEnough(world, picks))
                {
                    picks.Add(world);
                    break;
                }
            }
        }
        return picks;
    }

    // True if `world` clears MinCheckpointSpacing from every open checkpoint and
    // from each pick accepted earlier in the current call. Distance check is
    // squared to avoid the per-attempt sqrt.
    private bool IsFarEnough(Vector2 world, List<Vector2> pendingPicks)
    {
        if (MinCheckpointSpacing <= 0f) return true;
        float minSq = MinCheckpointSpacing * MinCheckpointSpacing;

        foreach (ActiveCheckpoint cp in _checkpoints)
            if (cp.Position.DistanceSquaredTo(world) < minSq) return false;

        foreach (Vector2 p in pendingPicks)
            if (p.DistanceSquaredTo(world) < minSq) return false;

        return true;
    }

    // Maps a 1D perimeter index to a 2D border tile. Walks the border clockwise:
    // top edge → right edge → bottom edge → left edge.
    private static Vector2I SampleBorderTile(int idx, int minX, int minY, int maxX, int maxY, int spanX)
    {
        int top = spanX;
        int right = Mathf.Max(0, (maxY - minY - 1));
        int bottom = spanX;
        if (idx < top)                  return new Vector2I(minX + idx, minY);
        idx -= top;
        if (idx < right)                return new Vector2I(maxX, minY + 1 + idx);
        idx -= right;
        if (idx < bottom)               return new Vector2I(maxX - idx, maxY);
        idx -= bottom;
                                        return new Vector2I(minX, maxY - 1 - idx);
    }

    private bool IsTraversable(Vector2 world)
    {
        var terrain = ChunkManager.GetTerrainTypeAtWorldPos(world);
        return terrain is { } t && !t.HasCollision();
    }

    private void SpawnSubSpawner(ActiveCheckpoint checkpoint, List<EnemyType> enemies)
    {
        if (EnemyScene == null || EnemiesContainer == null)
        {
            GD.PushWarning($"{Name}: EnemyScene or EnemiesContainer not assigned — wave dropped.");
            checkpoint.Portal?.Close();
            return;
        }

        var sub = new PocketCheckpointSpawner
        {
            EnemyScene = EnemyScene,
            EnemiesContainer = EnemiesContainer,
            SpawnInterval = SpawnInterval,
            SpawnPosition = checkpoint.Position,
            Enemies = enemies,
            Portal = checkpoint.Portal,
        };
        AddChild(sub);
    }
}
