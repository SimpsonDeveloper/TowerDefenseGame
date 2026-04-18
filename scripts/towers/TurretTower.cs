using Godot;
using System;

public partial class TurretTower : StaticBody2D
{
	[Export] public SpriteComponent TurretSprite;
	[Export] public Node2D Target;
	[Export] public string TargetGroupFallback { get; set; } = "Player";

	[Export] public float RotationSpeed = 8;

	private Node2D _target;
	
	public override void _Ready()
	{
		if (Target != null)
		{
			_target = Target;
		}
	}
	
	public override void _Process(double delta)
	{
		if (_target == null)
		{
			ResolveTarget();
			if (_target == null) return;
		}
		
		// Rotate turret sprite towards target while clamped at maximum speed
		Vector2 directionToTarget = _target.GlobalPosition - TurretSprite.GlobalPosition;
		float targetAngle = directionToTarget.Angle();
	
		// Calculate the shortest angular difference
		float angleDiff = Mathf.Wrap(targetAngle - TurretSprite.Rotation, -Mathf.Pi, Mathf.Pi);
	
		// Rotate by the maximum allowed speed, or less if closer
		float rotationStep = Mathf.Clamp(angleDiff, -RotationSpeed * (float)delta, RotationSpeed * (float)delta);
	
		TurretSprite.Rotation += rotationStep;
	}
	
	private void ResolveTarget()
	{
		if (!string.IsNullOrEmpty(TargetGroupFallback))
		{
			var viewport = GetViewport();
			foreach (Node node in GetTree().GetNodesInGroup(TargetGroupFallback))
			{
				if (node is Node2D n2d && n2d.GetViewport() == viewport)
				{
					_target = n2d;
					break;
				}
			}
		}
	}
}
