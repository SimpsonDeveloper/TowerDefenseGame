using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Transient per-checkpoint emitter. Holds its share of a wave's enemy list
/// and instantiates them one-by-one at <see cref="SpawnInterval"/> seconds.
/// Frees itself when the list is empty.
/// </summary>
public partial class PocketCheckpointSpawner : Node
{
    public PackedScene EnemyScene { get; set; }
    public Node2D EnemiesContainer { get; set; }
    public float SpawnInterval { get; set; }
    public Vector2 SpawnPosition { get; set; }
    public List<EnemyType> Enemies { get; set; }

    /// <summary>Optional VFX portal at this checkpoint. Flared per spawn, closed when the queue empties.</summary>
    public SpawnerPortal Portal { get; set; }

    private float _timer;
    private int _index;

    public override void _Process(double delta)
    {
        _timer -= (float)delta;
        if (_timer > 0f) return;

        if (Enemies == null || _index >= Enemies.Count)
        {
            Portal?.Close();
            QueueFree();
            return;
        }

        SpawnNext();
        _timer = SpawnInterval;
    }

    private void SpawnNext()
    {
        EnemyFactory.Spawn(EnemyScene, Enemies[_index], SpawnPosition, EnemiesContainer);
        Portal?.Flare();
        _index++;
    }
}
