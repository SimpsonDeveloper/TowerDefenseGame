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
	/// When false, player movement input is ignored.
	/// Set by WorldManager to prevent the mini-viewport player from moving.
	/// </summary>
	public bool InputEnabled { get; set; } = true;

	/// <summary>
	/// Minimum tile radius of open space required around the spawn point.
	/// 2 means a 5x5 tile area must be fully clear, giving the player breathing room.
	/// </summary>
	[Export] public int SpawnClearance { get; set; } = 2;

	/// <summary>
	/// When true, skips the spawn helper relocation and treats the current position as valid.
	/// Set this before the first _PhysicsProcess when a fixed spawn position is required.
	/// </summary>
	[Export] public bool SkipSpawnHelper { get; set; } = false;

	private bool _spawnReady;

	public override void _PhysicsProcess(double delta)
	{
		if (!_spawnReady)
		{
			TryFindValidSpawn();
			return;
		}

		if (!InputEnabled)
		{
			Velocity = Vector2.Zero;
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
		if (SkipSpawnHelper)
		{
			_spawnReady = true;
			EmitSignal(SignalName.Spawned);
			return;
		}

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
