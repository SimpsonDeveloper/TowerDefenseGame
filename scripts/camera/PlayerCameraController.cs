using Godot;

public partial class PlayerCameraController : Camera2D
{
	[Export] public float ZoomSpeed { get; set; } = 0.1f;
	[Export] public float MinZoom { get; set; } = 0.5f;
	[Export] public float MaxZoom { get; set; } = 2.0f;
	[Export] public float SmoothSpeed { get; set; } = 5.0f;
	[Export] public Node2D Player { get; set; }

	private float _targetZoom;

	public override void _Ready()
	{
		_targetZoom = Zoom.X;
	}

	public override void _Process(double delta)
	{
		if (Player != null)
			GlobalPosition = GlobalPosition.Lerp(Player.GlobalPosition, SmoothSpeed * (float)delta);

		float currentZoom = Mathf.Lerp(Zoom.X, _targetZoom, SmoothSpeed * (float)delta);
		Zoom = new Vector2(currentZoom, currentZoom);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
			HandleZoom(mouseButton);
	}

	private void HandleZoom(InputEventMouseButton mouseButton)
	{
		if (mouseButton.ButtonIndex == MouseButton.WheelUp)
			_targetZoom += ZoomSpeed;
		else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
			_targetZoom -= ZoomSpeed;

		_targetZoom = Mathf.Clamp(_targetZoom, MinZoom, MaxZoom);
	}
}
