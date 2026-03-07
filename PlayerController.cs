using Godot;
using towerdefensegame;

public partial class PlayerController : CharacterBody2D
{
	[Export]
	public float MoveSpeed { get; set; } = 200.0f;

	[Export]
	public ChunkManager ChunkManager { get; set; }

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
			return;
		}

		Vector2? validPos = SpawnHelper.FindValidSpawnPosition(ChunkManager, GlobalPosition);
		if (validPos.HasValue)
		{
			GlobalPosition = validPos.Value;
			_spawnReady = true;
		}
	}
}
