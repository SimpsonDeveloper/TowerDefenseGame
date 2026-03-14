using Godot;
using towerdefensegame;

public partial class PlayerController : CharacterBody2D
{
	/// <summary>Emitted once when a valid spawn position is found and the player is placed.</summary>
	[Signal]
	public delegate void SpawnedEventHandler();

	[Export] public float MoveSpeed { get; set; } = 200.0f;
	[Export] public ChunkManager ChunkManager { get; set; }

	/// <summary>
	/// Minimum tile radius of open space required around the spawn point.
	/// 2 means a 5x5 tile area must be fully clear, giving the player breathing room.
	/// </summary>
	[Export] public int SpawnClearance { get; set; } = 2;

	private bool _spawnReady;

	public override void _PhysicsProcess(double delta)
	{
		if (!_spawnReady)
		{
			TryFindValidSpawn();
			return;
		}

		Vector2 moveDirection = Vector2.Zero;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			moveDirection.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			moveDirection.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			moveDirection.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			moveDirection.X += 1;

		Velocity = moveDirection == Vector2.Zero
			? Vector2.Zero
			: moveDirection.Normalized() * MoveSpeed;

		MoveAndSlide();
	}

	private void TryFindValidSpawn()
	{
		if (ChunkManager == null)
		{
			_spawnReady = true;
			EmitSignal(SignalName.Spawned);
			return;
		}

		Vector2? validPos = SpawnHelper.FindValidSpawnPosition(
			ChunkManager, GlobalPosition, minClearance: SpawnClearance);

		if (validPos.HasValue)
		{
			GlobalPosition = validPos.Value;
			_spawnReady = true;
			EmitSignal(SignalName.Spawned);
		}
	}
}
