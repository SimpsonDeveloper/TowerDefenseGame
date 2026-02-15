using Godot;
using System;

public partial class CameraController : Camera2D
{
	[Export]
	public float MoveSpeed { get; set; } = 500.0f;

	[Export]
	public float ZoomSpeed { get; set; } = 0.1f;

	[Export]
	public float MinZoom { get; set; } = 0.5f;

	[Export]
	public float MaxZoom { get; set; } = 2.0f;

	[Export]
	public float SmoothSpeed { get; set; } = 5.0f;

	[Export]
	public bool EnableEdgeScrolling { get; set; } = false;

	[Export]
	public float EdgeScrollMargin { get; set; } = 20.0f;

	private Vector2 _targetPosition;
	private float _targetZoom;

	public override void _Ready()
	{
		_targetPosition = GlobalPosition;
		_targetZoom = Zoom.X;
	}

	public override void _Process(double delta)
	{
		HandleKeyboardInput(delta);

		if (EnableEdgeScrolling)
		{
			HandleEdgeScrolling(delta);
		}

		ApplyCameraMovement(delta);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			HandleZoom(mouseButton);
		}
	}

	private void HandleKeyboardInput(double delta)
	{
		Vector2 moveDirection = Vector2.Zero;

		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
		{
			moveDirection.Y -= 1;
		}
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
		{
			moveDirection.Y += 1;
		}
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
		{
			moveDirection.X -= 1;
		}
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
		{
			moveDirection.X += 1;
		}

		if (moveDirection != Vector2.Zero)
		{
			moveDirection = moveDirection.Normalized();
			_targetPosition += moveDirection * MoveSpeed * (float)delta;
		}
	}

	private void HandleEdgeScrolling(double delta)
	{
		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 moveDirection = Vector2.Zero;

		if (mousePos.X < EdgeScrollMargin)
		{
			moveDirection.X -= 1;
		}
		else if (mousePos.X > viewportSize.X - EdgeScrollMargin)
		{
			moveDirection.X += 1;
		}

		if (mousePos.Y < EdgeScrollMargin)
		{
			moveDirection.Y -= 1;
		}
		else if (mousePos.Y > viewportSize.Y - EdgeScrollMargin)
		{
			moveDirection.Y += 1;
		}

		if (moveDirection != Vector2.Zero)
		{
			moveDirection = moveDirection.Normalized();
			_targetPosition += moveDirection * MoveSpeed * (float)delta;
		}
	}

	private void HandleZoom(InputEventMouseButton mouseButton)
	{
		if (mouseButton.ButtonIndex == MouseButton.WheelUp)
		{
			_targetZoom += ZoomSpeed;
		}
		else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
		{
			_targetZoom -= ZoomSpeed;
		}

		_targetZoom = Mathf.Clamp(_targetZoom, MinZoom, MaxZoom);
	}

	private void ApplyCameraMovement(double delta)
	{
		GlobalPosition = GlobalPosition.Lerp(_targetPosition, SmoothSpeed * (float)delta);
		
		float currentZoom = Mathf.Lerp(Zoom.X, _targetZoom, SmoothSpeed * (float)delta);
		Zoom = new Vector2(currentZoom, currentZoom);
	}
}
