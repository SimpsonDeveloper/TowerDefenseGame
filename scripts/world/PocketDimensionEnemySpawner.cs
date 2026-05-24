using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.terrain;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Queues enemies hit-in by the overworld and releases them in timed waves
/// at random border tiles of the pocket-dimension play area.
///
/// Lifecycle: a queued enemy starts the wave timer. When the timer reaches
/// zero, the entire queue is partitioned evenly across <see cref="CheckpointsPerWave"/>
/// randomly chosen border checkpoints; each checkpoint is given its share to
/// spawn at <see cref="SpawnInterval"/> via a transient sub-spawner that frees
/// itself when its list is exhausted. The queue then clears and the timer goes
/// idle until the next enqueue.
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

    /// <summary>Container that spawned enemies are parented under. Typically a Node2D inside the pocket dim.</summary>
    [Export] public Node2D EnemiesContainer { get; set; }

    /// <summary>Number of border checkpoints picked per wave. Queue is split evenly across them.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int CheckpointsPerWave { get; set; } = 4;

    /// <summary>Seconds between waves. Counts down only while the queue is non-empty.</summary>
    [Export] public float WaveInterval { get; set; } = 15f;

    /// <summary>Seconds between consecutive spawns at a single checkpoint.</summary>
    [Export] public float SpawnInterval { get; set; } = 0.4f;

    /// <summary>Safety cap on border-tile re-rolls when looking for a traversable checkpoint.</summary>
    [Export] public int CheckpointMaxRerolls { get; set; } = 32;

    private readonly List<EnemyType> _queue = new();
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
    }

    private void FireWave()
    {
        if (_queue.Count == 0)
        {
            ResetTimer();
            return;
        }

        int requested = Mathf.Min(CheckpointsPerWave, _queue.Count);
        var checkpoints = PickBorderCheckpoints(requested);

        if (checkpoints.Count == 0)
        {
            GD.PushWarning($"{Name}: no valid border checkpoints found this wave — keeping queue, will retry next interval.");
            _timer = WaveInterval;
            return;
        }

        // Random even partition: shuffle the queue, then deal round-robin.
        var lists = new List<List<EnemyType>>(checkpoints.Count);
        for (int i = 0; i < checkpoints.Count; i++) lists.Add(new List<EnemyType>());

        ShuffleQueue();
        for (int i = 0; i < _queue.Count; i++)
            lists[i % checkpoints.Count].Add(_queue[i]);

        int totalEnemies = _queue.Count;
        _queue.Clear();
        EmitSignal(SignalName.QueueChanged, 0);

        for (int i = 0; i < checkpoints.Count; i++)
            SpawnSubSpawner(checkpoints[i], lists[i]);

        EmitSignal(SignalName.WaveFired, totalEnemies, checkpoints.Count);
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
    // that are traversable. Re-rolls (with a per-pick cap) until each slot fills
    // or the budget is exhausted.
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

                if (IsTraversable(world))
                {
                    picks.Add(world);
                    break;
                }
            }
        }
        return picks;
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

    private void SpawnSubSpawner(Vector2 worldPos, List<EnemyType> enemies)
    {
        if (EnemyScene == null || EnemiesContainer == null)
        {
            GD.PushWarning($"{Name}: EnemyScene or EnemiesContainer not assigned — wave dropped.");
            return;
        }

        var sub = new PocketCheckpointSpawner
        {
            EnemyScene = EnemyScene,
            EnemiesContainer = EnemiesContainer,
            SpawnInterval = SpawnInterval,
            SpawnPosition = worldPos,
            Enemies = enemies,
        };
        AddChild(sub);
    }
}
