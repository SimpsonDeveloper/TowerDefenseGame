using Godot;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.towers;

public partial class TurretTower : StaticBody2D, ITowerPlaceable
{
	[Export] public SpriteComponent TurretSprite;
	[Export] public DetectionZone TargetingZone;
	[Export] public CollisionShape2D TargetingZoneCollisionShape;
	[Export] public float RotationSpeed = 8;

	private Node2D _target;
	private float _targetRadius;

	// Stores radius before entering the tree; TargetingZone resolves in _Ready.
	public void Configure(TowerDef def) => _targetRadius = def.TargetRadius;

	public override void _Ready()
	{
		if (TargetingZoneCollisionShape?.Shape is CircleShape2D circle)
			circle.Radius = _targetRadius;
	}

	public override void _Process(double delta)
	{
		_target = FindClosestInZone();
		if (_target == null) return;

		Vector2 directionToTarget = _target.GlobalPosition - TurretSprite.GlobalPosition;
		float targetAngle = directionToTarget.Angle();

		float angleDiff = Mathf.Wrap(targetAngle - TurretSprite.Rotation, -Mathf.Pi, Mathf.Pi);
		float rotationStep = Mathf.Clamp(angleDiff, -RotationSpeed * (float)delta, RotationSpeed * (float)delta);

		TurretSprite.Rotation += rotationStep;
	}

	// Returns the closest body in the DetectionZone that belongs to the enemies group.
	private Node2D FindClosestInZone()
	{
		Node2D closest = null;
		float closestDist = float.MaxValue;

		foreach (Node2D body in TargetingZone.GetOverlappingBodies())
		{
			if (!body.IsInGroup("enemies")) continue;
			float dist = GlobalPosition.DistanceTo(body.GlobalPosition);
			if (dist < closestDist)
			{
				closestDist = dist;
				closest = body;
			}
		}

		return closest;
	}
}
