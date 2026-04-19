using Godot;
using towerdefensegame.scripts.player;

namespace towerdefensegame.scripts.camera;

public partial class PlayerCameraController : Camera2D
{
	[Export] public float ZoomSpeed { get; set; } = 0.1f;
	[Export] public float MinZoom { get; set; } = 0.5f;
	[Export] public float MaxZoom { get; set; } = 4.0f;
	[Export] public float SmoothSpeed { get; set; } = 5.0f;
	[Export] public bool UseSmoothing { get; set; }
	[Export] public bool SnapZoom { get; set; } = true;
	
	private Node2D _player;
	private float _targetZoom;

	public override void _Ready()
	{
		Zoom = new Vector2(2, 2);
		_targetZoom = Zoom.X;
	}

	public override void _Process(double delta)
	{
		if (UseSmoothing)
		{
			if (_player != null)
				GlobalPosition = GlobalPosition.Lerp(_player.GlobalPosition, SmoothSpeed * (float)delta);

			float currentZoom = Mathf.Lerp(Zoom.X, _targetZoom, SmoothSpeed * (float)delta);
			Zoom = new Vector2(currentZoom, currentZoom);
		}
		else
		{
			if (_player != null)
				GlobalPosition = _player.GlobalPosition;
			Zoom = new Vector2(_targetZoom, _targetZoom);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } mb)
		{
			float dir = mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f;
			ApplyZoomStep(dir);
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>Zoom by direction: +1 = zoom in, -1 = zoom out.</summary>
	public void ApplyZoomStep(float direction)
	{
		if (SnapZoom)
			_targetZoom *= direction > 0 ? 2f : 0.5f;
		else
			_targetZoom += direction * ZoomSpeed;

		_targetZoom = Mathf.Clamp(_targetZoom, MinZoom, MaxZoom);
	}

	/// <summary>Called via the PlayerSpawner.PlayerSpawned signal.</summary>
	public void OnPlayerSpawned(PlayerController player) => _player = player;
}
