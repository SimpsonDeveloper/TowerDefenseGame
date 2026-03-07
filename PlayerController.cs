using Godot;

public partial class PlayerController : Node2D
{
	[Export]
	public float MoveSpeed { get; set; } = 200.0f;

	public override void _Process(double delta)
	{
		Vector2 moveDirection = Vector2.Zero;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
			moveDirection.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
			moveDirection.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
			moveDirection.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
			moveDirection.X += 1;

		if (moveDirection != Vector2.Zero)
			Position += moveDirection.Normalized() * MoveSpeed * (float)delta;
	}
}
