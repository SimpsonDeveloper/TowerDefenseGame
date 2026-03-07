using Godot;
using towerdefensegame;

public partial class PlayerController : CharacterBody2D
{
	[Export]
	public float MoveSpeed { get; set; } = 200.0f;

	[Export]
	public ChunkManager ChunkManager { get; set; }

	/// <summary>
	/// Minimum tile radius of open space required around the spawn point.
	/// 2 means a 5x5 tile area must be fully clear, giving the player breathing room.
	/// </summary>
	[Export]
	public int SpawnClearance { get; set; } = 2;

	/// <summary>Duration in seconds of the fade-in after a valid spawn is found.</summary>
	[Export]
	public float SpawnFadeDuration { get; set; } = 0.6f;

	private bool _spawnReady;
	private ColorRect _fadeRect;

	public override void _Ready()
	{
		SetupFadeOverlay();
	}

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

	private void SetupFadeOverlay()
	{
		// CanvasLayer renders in screen space regardless of this node's world position.
		var fadeLayer = new CanvasLayer();
		fadeLayer.Layer = 100; // above all game UI
		AddChild(fadeLayer);

		_fadeRect = new ColorRect();
		_fadeRect.Color = new Color(0, 0, 0, 1);
		_fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		fadeLayer.AddChild(_fadeRect);
	}

	private void TryFindValidSpawn()
	{
		if (ChunkManager == null)
		{
			_spawnReady = true;
			FadeIn();
			return;
		}

		Vector2? validPos = SpawnHelper.FindValidSpawnPosition(
			ChunkManager, GlobalPosition, minClearance: SpawnClearance);

		if (validPos.HasValue)
		{
			GlobalPosition = validPos.Value;
			_spawnReady = true;
			FadeIn();
		}
	}

	private void FadeIn()
	{
		var tween = CreateTween();
		tween.TweenProperty(_fadeRect, "color:a", 0.0f, SpawnFadeDuration)
			 .SetTrans(Tween.TransitionType.Sine);
		tween.TweenCallback(Callable.From(() => _fadeRect.GetParent().QueueFree()));
	}
}
