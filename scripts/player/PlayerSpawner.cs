using Godot;
using towerdefensegame.scripts.terrain;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.player;

/// <summary>
/// Polls <see cref="SpawnHelper"/> each frame until a valid world position is found,
/// then instantiates <see cref="PlayerScene"/> at that position and emits
/// <see cref="PlayerSpawned"/>. All nodes that need a player reference — camera,
/// crystal spawner, UI — should connect to that signal rather than holding a direct
/// export reference to the player.
/// </summary>
public partial class PlayerSpawner : Node2D
{
    [Signal]
    public delegate void PlayerSpawnedEventHandler(PlayerController player);

    [Export] public ChunkManager ChunkManager { get; set; }
    [Export] public CoordConfig CoordConfig { get; set; }
    [Export] public PackedScene PlayerScene { get; set; }

    /// <summary>World-space origin to search outward from when looking for a clear spawn tile.</summary>
    [Export] public Vector2 SpawnOrigin { get; set; } = Vector2.Zero;

    /// <summary>Minimum tile radius of open space required around the spawn point.</summary>
    [Export] public int SpawnClearance { get; set; } = 2;

    private bool _spawned;

    public override void _Process(double delta)
    {
        if (_spawned) return;
        if (ChunkManager == null || CoordConfig == null || PlayerScene == null) return;

        Vector2? pos = SpawnHelper.FindValidSpawnPosition(
            ChunkManager, CoordConfig, SpawnOrigin, minClearance: SpawnClearance);

        if (!pos.HasValue) return;

        var player = PlayerScene.Instantiate<PlayerController>();
        GetParent().AddChild(player);
        player.GlobalPosition = pos.Value;

        _spawned = true;
        EmitSignal(SignalName.PlayerSpawned, player);
        GD.Print("Spawned");
    }
}
